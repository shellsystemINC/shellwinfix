using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;
using TaskbarTYOL.Services;
using TaskbarTYOL.Views;

namespace TaskbarTYOL;

public partial class App : Application
{
    public static new App Current => (App)Application.Current;

    public Settings Settings { get; private set; } = null!;
    internal WindowTracker Tracker { get; private set; } = null!;
    internal TrayService? Tray { get; private set; }

    /// <summary>All taskbars, primary monitor first.</summary>
    public List<TaskbarWindow> Taskbars { get; } = new();
    public TaskbarWindow? Taskbar => Taskbars.FirstOrDefault();
    public StartMenuWindow? StartMenu { get; private set; }
    public SearchWindow? Search { get; private set; }
    public TrayFlyoutWindow? TrayFlyout { get; private set; }
    private SettingsWindow? _settingsWin;

    /// <summary>True once shutdown began — lets popup windows that normally refuse to close actually close.</summary>
    public bool IsShuttingDown { get; private set; }

    private const string ExitEventName = "TaskbarTYOL_ExitRequest";
    private Mutex? _mutex;
    private EventWaitHandle? _exitEvent;
    private WinKeyHook? _hook;
    private ExplorerInjector? _injector;
    private DispatcherTimer? _enforceTimer;
    private DispatcherTimer? _displayDebounce;
    private DateTime _lastErrorDialog = DateTime.MinValue;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // "TaskbarTYOL.exe --exit" asks a running instance to quit gracefully (restoring the Windows taskbar).
        if (e.Args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
        {
            try { using var evt = EventWaitHandle.OpenExisting(ExitEventName); evt.Set(); } catch { /* not running */ }
            Shutdown();
            return;
        }

        _mutex = new Mutex(true, "TaskbarTYOL_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            // Started twice (e.g. autostart + manual launch) → just go away quietly, like Explorer would.
            _mutex = null;
            Shutdown();
            return;
        }

        _exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);
        var exitWaiter = new Thread(() => { _exitEvent.WaitOne(); Dispatcher.BeginInvoke(ExitApp); }) { IsBackground = true, Name = "exit-listener" };
        exitWaiter.Start();

        // Never leave the user without a taskbar if we crash.
        AppDomain.CurrentDomain.UnhandledException += (_, args) => { EmergencyRestore(); if (args.ExceptionObject is Exception ex) LogError(ex); };
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            LogError(args.Exception);
            // A fault in a 400ms/1s timer would otherwise pop a new modal dialog every tick, stacking dozens of
            // blocking boxes and freezing the app. Always log; surface a dialog at most once per 30s.
            if ((DateTime.UtcNow - _lastErrorDialog).TotalSeconds >= 30)
            {
                _lastErrorDialog = DateTime.UtcNow;
                MessageBox.Show(args.Exception.Message + "\n\nDetails were written to " + ErrorLogPath,
                    "TaskbarTYOL error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };
        SessionEnding += (_, _) => { IsShuttingDown = true; EmergencyRestore(); };

        Settings = Settings.Load();
        ImportExplorerPinsOnce();
        Task.Run(ExplorerIntegration.CleanupDeadRegistration); // scrub dead SSO entries left by earlier builds
        // Keep the Windows right-click menu in agreement with our settings (registry I/O off the startup path).
        Task.Run(() =>
        {
            try { ContextMenuManager.SetClassicMenu(Settings.ClassicContextMenu); ContextMenuManager.Sync(Settings); }
            catch (Exception ex) { LogError(ex); }
        });
        Tracker = new WindowTracker(Settings);
        ThemeManager.Apply(Settings.Theme);

        // Hide Explorer's taskbar FIRST so its strip is no longer reserved when our AppBars claim the edge.
        if (Settings.HideNativeTaskbar) NativeTaskbar.Hide(Settings);
        CreateTaskbars();
        Tracker.Start();
        ApplyBehavior();
        foreach (var bar in Taskbars) bar.ApplyFromSettings(); // bars must learn that the tray host / hook now exist

        // Startup diagnostics (only written when %AppData%\TaskbarTYOL\debug.log exists).
        if (Taskbar != null)
            Taskbar.ContentRendered += (_, _) =>
                Log($"bar rendered {(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0} ms after process start");

        // Warm the start-menu index a few seconds after we are up, so the first Win-key press is instant.
        var prewarm = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        prewarm.Tick += (_, _) => { prewarm.Stop(); AppIndexer.PrewarmAsync(); };
        prewarm.Start();

        // Inject the supervisor into explorer.exe so the bar lives with the shell.
        TryInject();

        _enforceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _enforceTimer.Tick += (_, _) => Enforce();
        _enforceTimer.Start();

        // Monitors plugged / unplugged / resolution changed → rebuild bars (debounced, the event fires in bursts).
        _displayDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _displayDebounce.Tick += (_, _) =>
        {
            _displayDebounce.Stop();
            CreateTaskbars();
            // New bar objects must learn the current settings and re-establish the tray-rect provider on the new
            // primary bar (otherwise it still points at the old, now-closed bar).
            foreach (var bar in Taskbars) bar.ApplyFromSettings();
        };
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.BeginInvoke(() => { _displayDebounce.Stop(); _displayDebounce.Start(); });
    }

    private void Enforce()
    {
        if (!Settings.HideNativeTaskbar) return;
        bool explorerCameBack = NativeTaskbar.Enforce(Settings);
        if (explorerCameBack)
        {
            // Explorer restarted: re-claim the screen edge and pull the tray icons back from its brand-new tray window.
            foreach (var bar in Taskbars) bar.Reposition();
            if (Tray is { IsRunning: true }) { Tray.EnsureOnTop(); Tray.BroadcastTaskbarCreated(); }
            // The old hook died with the old explorer.exe (its handle is now stale). Drop it; the retry below hooks
            // the new shell over the next ticks (GetShellWindow may not point at the new explorer immediately).
            _injector?.Uninstall();
        }
        else if (Tray is { IsRunning: true } && Tray.EnsureOnTop())
        {
            Tray.BroadcastTaskbarCreated();
        }

        // Keep the hook attached to the current shell (retries harmlessly until it sticks; no-op once installed).
        if (Settings.InjectIntoExplorer && _injector is { IsInstalled: false }) TryInject();
    }

    /// <summary>Human-readable state of the explorer.exe injection, for the settings window.</summary>
    public string ExplorerHookStatus
    {
        get
        {
            if (!Settings.InjectIntoExplorer) return "Off. The bar runs as its own process (still started with the shell by the logon task).";
            if (_injector is not { DllPresent: true }) return "The TaskbarTYOLHook.dll module is not installed next to the app. Run install.ps1.";
            return _injector.IsInstalled
                ? "Active. TaskbarTYOL's code is running inside explorer.exe, keeping the Windows taskbar suppressed from the shell thread. It is removed cleanly when the app exits and nothing persists across a reboot."
                : "Enabled, but not injected yet — it attaches once the shell is available.";
        }
    }

    /// <summary>Install (or re-install) the explorer.exe supervisor hook, if enabled and the DLL shipped.</summary>
    private void TryInject()
    {
        if (!Settings.InjectIntoExplorer) return;
        _injector ??= new ExplorerInjector();
        if (!_injector.DllPresent || _injector.IsInstalled) return;
        try
        {
            if (_injector.Install()) Log("explorer hook installed");
            else Log("explorer hook install failed (err " + Marshal.GetLastWin32Error() + ")");
        }
        catch (Exception ex) { LogError(ex); }
    }

    private void CreateTaskbars()
    {
        foreach (var bar in Taskbars) bar.Close();
        Taskbars.Clear();

        var monitors = Monitors.All();
        if (monitors.Count == 0) return;

        foreach (var mon in monitors)
        {
            if (!mon.IsPrimary && !Settings.ShowOnAllMonitors) continue;
            var bar = new TaskbarWindow(mon);
            bar.Show();
            Taskbars.Add(bar);
        }
    }

    private void ApplyBehavior()
    {
        if (Settings.HideNativeTaskbar) NativeTaskbar.Hide(Settings); else NativeTaskbar.Show(Settings);

        // Tray hijack only makes sense while Explorer's taskbar is out of the way.
        bool wantTray = Settings.HideNativeTaskbar && Settings.ShowTrayIcons;
        if (wantTray && Tray is not { IsRunning: true })
        {
            try { Tray = new TrayService(); Tray.Start(); }
            catch (Exception ex) { LogError(ex); Tray = null; }
        }
        else if (!wantTray && Tray != null)
        {
            Tray.Dispose();
            Tray = null;
        }

        if (Settings.ReplaceWinKey)
        {
            if (_hook == null)
            {
                _hook = new WinKeyHook();
                _hook.WinKeyPressed += () => Dispatcher.BeginInvoke(() => ToggleStartMenu());
                _hook.SearchRequested += () => Dispatcher.BeginInvoke(() => ToggleSearch());
                _hook.Install();
            }
            _hook.Enabled = true;
        }
        else if (_hook != null) _hook.Enabled = false;

        // Autostart via a logon scheduled task (starts with the shell, restarts itself on failure). Registration
        // runs schtasks → keep it off the startup path.
        bool wantAutostart = Settings.StartWithWindows;
        Task.Run(() =>
        {
            try
            {
                bool enabled = StartupManager.IsEnabled();
                if (enabled != wantAutostart || (wantAutostart && !StartupManager.IsCurrent()))
                    StartupManager.SetEnabled(wantAutostart);
            }
            catch (Exception ex) { LogError(ex); }
        });

        // Apply the explorer-injection toggle live.
        if (Settings.InjectIntoExplorer) TryInject();
        else if (_injector is { IsInstalled: true }) { _injector.Uninstall(); Log("explorer hook removed by settings"); }
    }

    /// <summary>Persist settings and push every change live into the running UI.</summary>
    public void ApplySettings()
    {
        Settings.Save();
        if (!string.Equals(ThemeManager.CurrentName, Settings.Theme, StringComparison.OrdinalIgnoreCase))
            ThemeManager.Apply(Settings.Theme);

        ApplyBehavior(); // native taskbar hide/show first, so the bars below re-claim the correct edge
        Tray?.ApplyPromotion();

        bool wantSecondary = Settings.ShowOnAllMonitors && Monitors.All().Count > 1;
        bool haveSecondary = Taskbars.Count > 1;
        if (wantSecondary != haveSecondary) CreateTaskbars();
        else foreach (var bar in Taskbars) bar.ApplyFromSettings();

        Tracker.Refresh();
    }

    /// <summary>Opens/closes the start menu on the given bar (or the bar under the mouse / primary).</summary>
    public void ToggleStartMenu(TaskbarWindow? bar = null)
    {
        bar ??= BarUnderMouse() ?? Taskbar;
        if (bar == null) return;
        StartMenu ??= new StartMenuWindow();
        if (StartMenu.IsVisible || StartMenu.JustDeactivated) StartMenu.Hide();
        else { Search?.Hide(); StartMenu.ShowAt(bar); }
    }

    /// <summary>Opens/closes the Everything-powered search on the given bar.</summary>
    public void ToggleSearch(TaskbarWindow? bar = null)
    {
        bar ??= BarUnderMouse() ?? Taskbar;
        if (bar == null) return;
        Search ??= new SearchWindow();
        if (Search.IsVisible || Search.JustDeactivated) Search.Hide();
        else { StartMenu?.Hide(); Search.ShowAt(bar); }
    }

    /// <summary>Opens/closes the "^" overflow flyout for hidden notification icons.</summary>
    public void ToggleTrayFlyout(TaskbarWindow bar)
    {
        TrayFlyout ??= new TrayFlyoutWindow();
        if (TrayFlyout.IsVisible || TrayFlyout.JustDeactivated) TrayFlyout.Hide();
        else { StartMenu?.Hide(); Search?.Hide(); TrayFlyout.ShowAt(bar, bar.GetChevronRectDip()); }
    }

    /// <summary>Is this notification icon shown on the bar (true) or parked in the overflow flyout (false)?</summary>
    public bool IsTrayPromoted(TrayIcon icon) =>
        Settings.TrayShowAll || Settings.TrayPromoted.Contains(icon.PersistKey, StringComparer.OrdinalIgnoreCase);

    /// <summary>Move an icon between the bar and the overflow flyout, persistently.</summary>
    public void PromoteTrayIcon(string persistKey, bool promote)
    {
        Settings.TrayPromoted.RemoveAll(k => string.Equals(k, persistKey, StringComparison.OrdinalIgnoreCase));
        if (promote) Settings.TrayPromoted.Add(persistKey);
        Settings.Save();
        Tray?.ApplyPromotion();
        foreach (var bar in Taskbars) bar.ApplyFromSettings();
        if (TrayFlyout is { IsVisible: true } && Tray != null && !Tray.Icons.Any(i => !i.IsPromoted && !i.IsHidden)) TrayFlyout.Hide();
    }

    private TaskbarWindow? BarUnderMouse()
    {
        if (Taskbars.Count <= 1) return Taskbar;
        Win32.GetCursorPos(out var p);
        return Taskbars.FirstOrDefault(b => p.X >= b.Monitor.Bounds.Left && p.X < b.Monitor.Bounds.Right
                                          && p.Y >= b.Monitor.Bounds.Top && p.Y < b.Monitor.Bounds.Bottom);
    }

    public void OpenSettings()
    {
        if (_settingsWin == null || !_settingsWin.IsLoaded)
        {
            _settingsWin = new SettingsWindow();
            _settingsWin.Closed += (_, _) => _settingsWin = null;
        }
        _settingsWin.Show();
        _settingsWin.Activate();
    }

    public void ExitApp()
    {
        // Clean exit → exit code 0 → the logon task's restart-on-failure does NOT fire, and the hook (owned by this
        // process) is unmapped from explorer.exe automatically. The bar stays gone until next sign-in.
        IsShuttingDown = true;
        Shutdown();
    }

    /// <summary>First run: adopt the shortcuts the user had pinned to the Windows taskbar.</summary>
    private void ImportExplorerPinsOnce()
    {
        if (Settings.ImportedExplorerPins) return;
        try
        {
            foreach (var lnk in ExplorerIntegration.ExplorerPinnedShortcuts())
            {
                var target = ShortcutResolver.GetTarget(lnk) ?? lnk;
                bool already = Settings.Pinned.Any(p =>
                    string.Equals(p, lnk, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(ShortcutResolver.GetTarget(p) ?? p, target, StringComparison.OrdinalIgnoreCase));
                if (!already) Settings.Pinned.Add(lnk);
            }
        }
        catch (Exception ex) { LogError(ex); }
        Settings.ImportedExplorerPins = true;
        Settings.Save();
    }

    /// <summary>Best-effort restoration used from crash / logoff paths.</summary>
    private void EmergencyRestore()
    {
        try { Tray?.Dispose(); } catch { /* ignore */ }
        try { NativeTaskbar.Show(Settings); } catch { /* ignore */ }
    }

    public static string ErrorLogPath => System.IO.Path.Combine(Settings.Folder, "error.log");
    public static string DebugLogPath => System.IO.Path.Combine(Settings.Folder, "debug.log");

    /// <summary>Lightweight diagnostics (drag/drop, tray protocol…). Only written when debug.log already exists.</summary>
    public static void Log(string message)
    {
        try
        {
            if (!System.IO.File.Exists(DebugLogPath)) return;
            System.IO.File.AppendAllText(DebugLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch { /* ignore */ }
    }

    public static void LogError(Exception ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Settings.Folder);
            System.IO.File.AppendAllText(ErrorLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
        }
        catch { /* nothing more we can do */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        IsShuttingDown = true;
        try
        {
            _enforceTimer?.Stop();
            Tracker?.Stop();
            _hook?.Dispose();
            _injector?.Dispose();                        // remove our DLL from explorer.exe (clean unmap, no thread left)
            foreach (var bar in Taskbars) bar.Close();   // ABM_REMOVE → shell recomputes the work area
            Tray?.Dispose();                             // hands the notification icons back to Explorer
            NativeTaskbar.Show(Settings);
            Settings?.Save();
        }
        finally
        {
            _mutex?.ReleaseMutex();
            base.OnExit(e);
        }
    }
}
