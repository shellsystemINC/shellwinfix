using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;
using TaskbarTYOL.Services;

namespace TaskbarTYOL.Views;

public partial class StartMenuWindow : Window
{
    public ObservableCollection<AppEntry> Apps { get; } = new();
    private bool _scanned;
    private bool _suppressDeactivate;
    private DateTime _lastDeactivated;

    public StartMenuWindow()
    {
        InitializeComponent();
        DataContext = this;
        UserName.Text = Environment.UserName;
        UserInitial.Text = Environment.UserName.Length > 0 ? Environment.UserName[..1].ToUpperInvariant() : "?";
    }

    /// <summary>True if the menu was just hidden by losing focus (so the Start button click that caused it must not re-open it).</summary>
    public bool JustDeactivated => (DateTime.UtcNow - _lastDeactivated).TotalMilliseconds < 300;

    public void ShowAt(TaskbarWindow bar)
    {
        var start = bar.GetStartRectDip();
        var barRect = bar.GetBarRectDip();
        var screen = bar.GetMonitorRectDip(); // the monitor this bar lives on (multi-monitor aware)

        // The root border has a 10px margin reserved for its drop shadow; offset by exactly that
        // so the visible panel sits flush against the taskbar edge with no gap.
        const double shadowMargin = 10;
        // Centered taskbar (Windows 11 style): centre the menu on the monitor. Left-aligned: anchor to the Start button.
        double desiredLeft = App.Current.Settings.Alignment == TaskAlignment.Center
            ? screen.Left + (screen.Width - Width) / 2
            : start.Left - shadowMargin;
        double left = Math.Max(screen.Left - shadowMargin, Math.Min(desiredLeft, screen.Right - Width + shadowMargin));
        double top = App.Current.Settings.Edge == TaskbarEdge.Top
            ? barRect.Bottom - shadowMargin
            : barRect.Top - Height + shadowMargin;
        Left = left;
        Top = Math.Max(screen.Top, top);

        Show();
        Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Activate();
        SearchBox.Text = "";
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        if (!_scanned) { _scanned = true; _ = ScanAsync(); }
    }

    /// <summary>Re-scan installed apps (e.g. after installing something).</summary>
    public async Task RescanAsync()
    {
        var list = await Task.Run(AppIndexer.Scan);
        Apps.Clear();
        foreach (var a in list) Apps.Add(a);
        await LoadIconsAsync(list);
    }

    private async Task ScanAsync()
    {
        try
        {
            var list = await Task.Run(AppIndexer.TakeCachedOrScan);
            foreach (var a in list) Apps.Add(a);
            LoadingText.Visibility = Visibility.Collapsed;
            await LoadIconsAsync(list.Where(a => a.Icon == null).ToList());
        }
        catch (Exception ex) { App.LogError(ex); LoadingText.Text = "Could not read the Start Menu folders."; }
    }

    private async Task LoadIconsAsync(List<AppEntry> list)
    {
        // Icons in batches on a background thread; assignments happen on the UI thread via INotifyPropertyChanged.
        foreach (var chunk in list.Chunk(24))
        {
            var loaded = await Task.Run(() => chunk.Select(a => (a, AppIndexer.LoadIcon(a))).ToList());
            foreach (var (a, icon) in loaded) a.Icon = icon;
        }
    }

    // ---------------------------------------------------------------- search
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var cvs = (CollectionViewSource)Resources["AppsView"];
        string q = SearchBox.Text.Trim();
        cvs.View.Filter = string.IsNullOrEmpty(q) ? null : o => o is AppEntry a && a.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        if (!string.IsNullOrEmpty(q) && AppList.Items.Count > 0) AppList.SelectedIndex = 0;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; return; }
        if (e.Key == Key.Enter && SearchBox.IsKeyboardFocused)
        {
            var target = AppList.SelectedItem as AppEntry ?? AppList.Items.OfType<AppEntry>().FirstOrDefault();
            if (target != null) { Launch(target); e.Handled = true; }
        }
        else if (e.Key == Key.Down && SearchBox.IsKeyboardFocused && AppList.Items.Count > 0)
        {
            AppList.SelectedIndex = Math.Max(0, AppList.SelectedIndex);
            AppList.ScrollIntoView(AppList.SelectedItem);
            (AppList.ItemContainerGenerator.ContainerFromIndex(AppList.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }

    private void AppList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AppList.SelectedItem is AppEntry a) { Launch(a); e.Handled = true; }
        else if (e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space or Key.Back)
        {
            // typing while the list has focus goes back to the search box
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }
    }

    // ---------------------------------------------------------------- app actions
    private static AppEntry? EntryOf(object sender) => (sender as FrameworkElement)?.DataContext as AppEntry;

    private void AppItem_MouseUp(object sender, MouseButtonEventArgs e) { if (EntryOf(sender) is { } a) Launch(a); }
    private void AppOpen_Click(object sender, RoutedEventArgs e) { if (EntryOf(sender) is { } a) Launch(a); }

    private void AppRunAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } a) return;
        Hide();
        TryStart(new ProcessStartInfo(a.Path) { UseShellExecute = true, Verb = "runas" });
    }

    private void AppPin_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } a) return;
        var s = App.Current.Settings;
        if (!s.Pinned.Contains(a.Path, StringComparer.OrdinalIgnoreCase)) s.Pinned.Add(a.Path);
        App.Current.ApplySettings();
    }

    private void AppLocation_Click(object sender, RoutedEventArgs e)
    {
        if (EntryOf(sender) is not { } a) return;
        var target = ShortcutResolver.GetTarget(a.Path) ?? a.Path;
        Hide();
        TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
    }

    private void Launch(AppEntry a)
    {
        Hide();
        string? dir = null;
        try { dir = Path.GetDirectoryName(ShortcutResolver.GetTarget(a.Path) ?? a.Path); } catch { /* odd path */ }
        TryStart(new ProcessStartInfo(a.Path) { UseShellExecute = true, WorkingDirectory = dir ?? "" });
    }

    // ---------------------------------------------------------------- side column
    private void Side_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string target) return;
        Hide();
        if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            TryStart(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
        else
            TryStart(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    private void TaskbarSettings_Click(object sender, RoutedEventArgs e) { Hide(); App.Current.OpenSettings(); }

    private void Lock_Click(object sender, RoutedEventArgs e) { Hide(); Win32.LockWorkStation(); }
    private void SignOut_Click(object sender, RoutedEventArgs e) { if (Confirm("Sign out now?")) Win32.ExitWindowsEx(Win32.EWX_LOGOFF, 0); }
    private void Sleep_Click(object sender, RoutedEventArgs e) { Hide(); Win32.SetSuspendState(false, false, false); }
    private void Restart_Click(object sender, RoutedEventArgs e) { if (Confirm("Restart the computer now?")) TryStart(new ProcessStartInfo("shutdown.exe", "/r /t 0") { UseShellExecute = false, CreateNoWindow = true }); }
    private void Shutdown_Click(object sender, RoutedEventArgs e) { if (Confirm("Shut down the computer now?")) TryStart(new ProcessStartInfo("shutdown.exe", "/s /t 0") { UseShellExecute = false, CreateNoWindow = true }); }

    private bool Confirm(string msg)
    {
        _suppressDeactivate = true;
        var r = MessageBox.Show(this, msg, "TaskbarTYOL", MessageBoxButton.YesNo, MessageBoxImage.Question);
        _suppressDeactivate = false;
        Hide();
        return r == MessageBoxResult.Yes;
    }

    private static void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex)
        {
            // "Operation cancelled by user" for UAC prompts is not an error worth shouting about.
            if (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1223 })
                MessageBox.Show(ex.Message, "Could not start", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (_suppressDeactivate) return;
        if (!IsVisible) return;            // we hid ourselves (Esc / launch) — not a focus loss
        _lastDeactivated = DateTime.UtcNow;
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Keep the instance alive while the app runs; App owns the lifetime.
        if (!App.Current.IsShuttingDown) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
