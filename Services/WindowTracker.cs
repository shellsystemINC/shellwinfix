using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

/// <summary>
/// Polls the desktop for alt-tab-style top-level windows and keeps an ObservableCollection of TaskItems in sync
/// (pinned launchers first, then running windows in first-seen order).
/// </summary>
internal sealed class WindowTracker
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
        "Windows.Internal.Shell.TabProxyWindow", "ForegroundStaging", "MultitaskingViewFrame", "XamlExplorerHostIslandWindow",
        "TaskManagerWindow_HiddenWindow", "Shell_InputSwitchTopLevelWindow", "Windows.UI.Composition.DesktopWindowContentBridge",
    };

    private readonly Settings _settings;
    private readonly DispatcherTimer _timer;
    private readonly HashSet<IntPtr> _ownHwnds = new();
    private readonly Dictionary<IntPtr, string?> _exeCache = new();
    private readonly Dictionary<string, string> _pinTargetCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IntPtr> _order = new(); // stable ordering of running windows

    public ObservableCollection<TaskItem> Items { get; } = new();

    public WindowTracker(Settings settings)
    {
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) => Refresh();
    }

    public void RegisterOwnWindow(IntPtr hwnd) => _ownHwnds.Add(hwnd);
    public void Start() { Refresh(); _timer.Start(); }
    public void Stop() => _timer.Stop();

    /// <summary>UWP apps are all hosted by ApplicationFrameHost.exe, so they must never be grouped by exe.</summary>
    private static bool IsFrameHost(string? exe) =>
        exe != null && Path.GetFileName(exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);

    private string GroupKey(IntPtr h)
    {
        var exe = _exeCache.GetValueOrDefault(h);
        return exe == null || IsFrameHost(exe) ? $"hwnd:{h}" : exe;
    }

    private string ResolvePin(string pin)
    {
        if (!_pinTargetCache.TryGetValue(pin, out var target))
        {
            target = ShortcutResolver.GetTarget(pin) ?? pin;
            _pinTargetCache[pin] = target;
        }
        return target;
    }

    public void Refresh()
    {
        // Runs every 400ms; a single transient failure must not tear the app down through the dispatcher.
        try { RefreshCore(); }
        catch (Exception ex) { App.LogError(ex); }
    }

    private void RefreshCore()
    {
        var windows = new List<IntPtr>();
        Win32.EnumWindows((h, _) => { if (IsTaskWindow(h)) windows.Add(h); return true; }, IntPtr.Zero);
        var windowSet = windows.ToHashSet();

        // keep stable order
        foreach (var h in windows) if (!_order.Contains(h)) _order.Add(h);
        _order.RemoveAll(h => !windowSet.Contains(h));
        foreach (var h in _order) if (!_exeCache.ContainsKey(h)) _exeCache[h] = Win32.GetProcessPath(h);
        foreach (var dead in _exeCache.Keys.Where(k => !windowSet.Contains(k)).ToList()) _exeCache.Remove(dead);

        IntPtr fg = Win32.GetForegroundWindow();
        var desired = new List<TaskItem>();

        // Pinned
        var pinTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pin in _settings.Pinned)
        {
            var target = ResolvePin(pin);
            pinTargets.Add(target);
            var existing = Items.FirstOrDefault(i => i.PinPath == pin) ?? new TaskItem { PinPath = pin, ExePath = target, Title = Path.GetFileNameWithoutExtension(pin) };
            LoadIconAsync(existing, () => IconHelper.GetFileIcon(pin));
            var mine = _order.Where(h => string.Equals(_exeCache.GetValueOrDefault(h), target, StringComparison.OrdinalIgnoreCase)).ToList();
            existing.Windows.Clear();
            existing.Windows.AddRange(mine);
            existing.Hwnd = mine.Contains(fg) ? fg : mine.FirstOrDefault();
            existing.IsRunning = mine.Count > 0;
            existing.WindowCount = mine.Count;
            existing.IsActive = mine.Contains(fg);
            existing.Title = mine.Count > 0 ? Win32.GetWindowTitle(existing.Hwnd) : Path.GetFileNameWithoutExtension(pin);
            desired.Add(existing);
        }

        // Running (not covered by a pin)
        var consumed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in _order)
        {
            var exe = _exeCache.GetValueOrDefault(h);
            if (exe != null && pinTargets.Contains(exe)) continue;

            string key = _settings.CombineWindows ? GroupKey(h) : $"hwnd:{h}";
            if (!consumed.Add(key)) continue;

            var group = _settings.CombineWindows ? _order.Where(w => GroupKey(w) == key).ToList() : new List<IntPtr> { h };
            var item = Items.FirstOrDefault(i => !i.IsPinned && i.GroupKey == key) ?? new TaskItem { GroupKey = key, ExePath = IsFrameHost(exe) ? null : exe };
            item.Windows.Clear(); item.Windows.AddRange(group);
            item.Hwnd = group.Contains(fg) ? fg : group[0];
            item.Title = Win32.GetWindowTitle(item.Hwnd);
            var hwndForIcon = item.Hwnd; var exeForIcon = item.ExePath;
            LoadIconAsync(item, () => IconHelper.GetWindowIcon(hwndForIcon, exeForIcon));
            item.IsRunning = true;
            item.WindowCount = group.Count;
            item.IsActive = group.Contains(fg);
            desired.Add(item);
        }

        // Sync collection with minimal churn
        for (int i = Items.Count - 1; i >= 0; i--) if (!desired.Contains(Items[i])) Items.RemoveAt(i);
        for (int i = 0; i < desired.Count; i++)
        {
            int idx = Items.IndexOf(desired[i]);
            if (idx == -1) Items.Insert(i, desired[i]);
            else if (idx != i) Items.Move(idx, i);
        }
    }

    /// <summary>
    /// Icon extraction (WM_GETICON round-trips to other processes, shell icon lookups) is the slowest part of a refresh,
    /// so it runs on the thread pool and lands on the item when ready. Frozen bitmaps are safe to hand across threads.
    /// This keeps the bar painting instantly at login instead of waiting for every app to answer.
    /// </summary>
    private static void LoadIconAsync(TaskItem item, Func<System.Windows.Media.ImageSource?> loader)
    {
        if (item.Icon != null || item.IconLoading) return;
        item.IconLoading = true;
        Task.Run(loader).ContinueWith(t =>
        {
            item.IconLoading = false;
            if (t.Status == TaskStatus.RanToCompletion && t.Result != null) item.Icon = t.Result;
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private bool IsTaskWindow(IntPtr h)
    {
        if (_ownHwnds.Contains(h)) return false;
        if (!Win32.IsWindowVisible(h)) return false;
        long ex = (long)Win32.GetWindowLongPtr(h, Win32.GWL_EXSTYLE);
        if ((ex & Win32.WS_EX_TOOLWINDOW) != 0 && (ex & Win32.WS_EX_APPWINDOW) == 0) return false;
        if ((ex & Win32.WS_EX_APPWINDOW) == 0 && Win32.GetWindow(h, Win32.GW_OWNER) != IntPtr.Zero) return false;
        if (Win32.IsCloaked(h)) return false;
        if (Win32.GetWindowTextLength(h) == 0) return false;
        var cls = Win32.GetWindowClass(h);
        if (IgnoredClasses.Contains(cls)) return false;
        return true;
    }
}
