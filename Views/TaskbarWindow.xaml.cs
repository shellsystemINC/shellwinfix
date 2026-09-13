using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;
using TaskbarTYOL.Services;

namespace TaskbarTYOL.Views;

public partial class TaskbarWindow : Window, INotifyPropertyChanged
{
    private readonly AppBar _appBar;
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _status;
    private DpiScale _dpi = new(1, 1);
    private IntPtr _hwnd;
    private bool _volSliderSyncing;
    private bool _statusBusy;
    // When a StaysOpen=False popup is open and its own button is clicked, WPF sets IsOpen=false during the dismiss
    // (on mouse-down) but raises the Closed event only AFTER the button's Click fires. So at Click time IsOpen already
    // reads false and a naive toggle reopens it — the "button won't close the popup" bug. These bools are driven by
    // the Opened/Closed events, which LAG IsOpen, so at Click time they still reflect the pre-dismiss state.
    private bool _volPopupOpen;
    private bool _calPopupOpen;

    public event PropertyChangedEventHandler? PropertyChanged;

    private static Settings S => App.Current.Settings;

    internal MonitorInfo Monitor { get; set; }
    public bool IsPrimary => Monitor.IsPrimary;
    public Visibility PrimaryVis => IsPrimary ? Visibility.Visible : Visibility.Collapsed;

    public ObservableCollection<TaskItem> Items => App.Current.Tracker.Items;
    public ObservableCollection<TrayIcon>? TrayIcons => App.Current.Tray?.Icons;
    public Visibility TrayVis => IsPrimary && App.Current.Tray is { IsRunning: true } ? Visibility.Visible : Visibility.Collapsed;

    // Two live-filtered views over the same icon list: promoted ones on the bar, the rest behind the chevron.
    private ListCollectionView? _visibleView, _overflowView;
    public ICollectionView? TrayVisibleIcons => _visibleView ??= MakeTrayView(promoted: true);
    public ICollectionView? TrayOverflowIcons => _overflowView ??= MakeTrayView(promoted: false);
    public Visibility OverflowVis => TrayVis == Visibility.Visible && !S.TrayShowAll && TrayOverflowIcons is { IsEmpty: false }
        ? Visibility.Visible : Visibility.Collapsed;
    public string ChevronGlyph => S.Edge == TaskbarEdge.Top ? "" : "";

    private ListCollectionView? MakeTrayView(bool promoted)
    {
        if (App.Current.Tray is not { } tray) return null;
        var view = new ListCollectionView(tray.Icons)
        {
            Filter = o => o is TrayIcon i && !i.IsHidden && (S.TrayShowAll ? promoted : i.IsPromoted == promoted),
            IsLiveFiltering = true,
        };
        view.LiveFilteringProperties.Add(nameof(TrayIcon.IsHidden));
        view.LiveFilteringProperties.Add(nameof(TrayIcon.IsPromoted));
        ((INotifyCollectionChanged)view).CollectionChanged += (_, _) => Raise(nameof(OverflowVis));
        return view;
    }

    private void ResetTrayViews()
    {
        var src = App.Current.Tray?.Icons;
        if (_visibleView != null && !ReferenceEquals(_visibleView.SourceCollection, src)) _visibleView = null;
        if (_overflowView != null && !ReferenceEquals(_overflowView.SourceCollection, src)) _overflowView = null;
        _visibleView?.Refresh();
        _overflowView?.Refresh();
    }
    public double IconSize => S.IconSize;
    public Visibility LabelVisibility => S.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
    public HorizontalAlignment TaskAlign => S.Alignment == TaskAlignment.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left;
    public Visibility SearchVis => S.ShowSearchButton && IsPrimary ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TaskViewVis => S.ShowTaskViewButton && IsPrimary ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DesktopVis => S.ShowDesktopButton ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsVis => S.ShowSettingsButton ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DateVis => S.ShowDate ? Visibility.Visible : Visibility.Collapsed;
    public double BarOpacity => S.Opacity;
    public string TimeText { get; private set; } = "";
    public string DateText { get; private set; } = "";

    // tray status
    public string VolGlyph { get; private set; } = "";
    public string VolTip { get; private set; } = "Volume";
    public string VolText { get; private set; } = "";
    public string NetGlyph { get; private set; } = "";
    public string NetTip { get; private set; } = "Network";
    public string BatGlyph { get; private set; } = "";
    public string BatText { get; private set; } = "";
    public string BatTip { get; private set; } = "Battery";
    public Visibility BatteryVis { get; private set; } = Visibility.Collapsed;

    internal TaskbarWindow(MonitorInfo monitor)
    {
        Monitor = monitor;
        InitializeComponent();
        DataContext = this;
        _appBar = new AppBar(this);

        _clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();
        UpdateClock();

        _status = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _status.Tick += async (_, _) => await UpdateStatusAsync();
        // Volume/network/battery probing (CoreAudio COM init, NIC enumeration) waits until the bar is on screen.
        if (IsPrimary) ContentRendered += (_, _) => { _status.Start(); _ = UpdateStatusAsync(); };

        VolumePopup.Opened += (_, _) => _volPopupOpen = true;
        VolumePopup.Closed += (_, _) => _volPopupOpen = false;
        CalendarPopup.Opened += (_, _) => _calPopupOpen = true;
        CalendarPopup.Closed += (_, _) => _calPopupOpen = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        _dpi = VisualTreeHelper.GetDpi(this);

        // Never steal focus from the app the user is working in.
        long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, (IntPtr)(ex | Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW));

        App.Current.Tracker.RegisterOwnWindow(_hwnd);
        Reposition();
        UpdateNativeToggleText();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _dpi = newDpi;
        Reposition();
    }

    public void Reposition()
    {
        uint edge = S.Edge == TaskbarEdge.Top ? AppBar.ABE_TOP : AppBar.ABE_BOTTOM;
        int px = (int)Math.Round(S.Height * _dpi.DpiScaleY);
        _appBar.SetPosition(edge, px, Monitor.Bounds, forceEdge: S.HideNativeTaskbar);
        var placement = S.Edge == TaskbarEdge.Top ? PlacementMode.Bottom : PlacementMode.Top;
        // Popups are anchored to the bar itself (Root), so their outer edge meets the bar edge exactly; the only
        // correction needed is the 8px shadow margin inside the popup content.
        double vOffset = S.Edge == TaskbarEdge.Top ? -PopupShadowMargin : PopupShadowMargin;
        CalendarPopup.Placement = placement; CalendarPopup.VerticalOffset = vOffset;
        VolumePopup.Placement = placement; VolumePopup.VerticalOffset = vOffset;
    }

    public void ApplyFromSettings()
    {
        ResetTrayViews();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Reposition();
        UpdateClock();
        UpdateNativeToggleText();
        HookTrayRects();
    }

    /// <summary>Screen rectangle of the taskbar in DIPs (device-independent pixels).</summary>
    public Rect GetBarRectDip()
    {
        Win32.GetWindowRect(_hwnd, out var r);
        return new Rect(r.Left / _dpi.DpiScaleX, r.Top / _dpi.DpiScaleY,
                        (r.Right - r.Left) / _dpi.DpiScaleX, (r.Bottom - r.Top) / _dpi.DpiScaleY);
    }

    /// <summary>Screen rectangle of the Start button in DIPs.</summary>
    public Rect GetStartRectDip() => ElementRectDip(StartButton);

    /// <summary>Screen rectangle of the search button in DIPs (falls back to the Start button when hidden).</summary>
    public Rect GetSearchRectDip() => SearchButton.IsVisible ? ElementRectDip(SearchButton) : GetStartRectDip();

    /// <summary>Screen rectangle of the overflow chevron in DIPs.</summary>
    public Rect GetChevronRectDip() => ElementRectDip(ChevronButton);

    private Rect ElementRectDip(FrameworkElement el)
    {
        var p = el.PointToScreen(new Point(0, 0));
        return new Rect(p.X / _dpi.DpiScaleX, p.Y / _dpi.DpiScaleY, el.ActualWidth, el.ActualHeight);
    }

    public double DpiScaleX => _dpi.DpiScaleX;
    public double DpiScaleY => _dpi.DpiScaleY;

    /// <summary>Monitor bounds in DIPs (using this window's DPI).</summary>
    public Rect GetMonitorRectDip() => new(
        Monitor.Bounds.Left / _dpi.DpiScaleX, Monitor.Bounds.Top / _dpi.DpiScaleY,
        (Monitor.Bounds.Right - Monitor.Bounds.Left) / _dpi.DpiScaleX, (Monitor.Bounds.Bottom - Monitor.Bounds.Top) / _dpi.DpiScaleY);

    protected override void OnClosed(EventArgs e)
    {
        _clock.Stop();
        _status.Stop();
        _appBar.Dispose();
        base.OnClosed(e);
    }

    private void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ------------------------------------------------------------------ clock
    private void UpdateClock()
    {
        var now = DateTime.Now;
        TimeText = now.ToString(S.ShowSeconds ? "T" : "t");
        DateText = now.ToString("d");
        Raise(nameof(TimeText), nameof(DateText));
    }

    private const double PopupShadowMargin = 8;

    /// <summary>Horizontal offset that lines a bar-anchored popup's visible left edge up with the given button.</summary>
    private double PopupXFor(FrameworkElement button) => button.TranslatePoint(new Point(0, 0), Root).X - PopupShadowMargin;

    private void Clock_Click(object sender, RoutedEventArgs e)
    {
        // Same dismiss/reopen guard as the volume popup (see _volPopupOpen).
        if (_calPopupOpen) { CalendarPopup.IsOpen = false; return; }

        CalendarPopup.HorizontalOffset = PopupXFor(ClockButton);
        CalendarControl.GoToday();
        var now = DateTime.Now;
        CalendarHeader.Text = now.ToString("dddd, d MMMM yyyy");
        int week = System.Globalization.ISOWeek.GetWeekOfYear(now);
        CalendarSubHeader.Text = $"Week {week}  ·  day {now.DayOfYear} of the year";
        CalendarPopup.IsOpen = true;
    }

    // ------------------------------------------------------------------ symmetric layout
    /// <summary>
    /// Margin for the start/task group. In centred mode both sides get the tray's width so the group is centred on
    /// the screen, not on the leftover space; the left side gives way only when the group would hit the tray.
    /// </summary>
    public Thickness TaskAreaMargin
    {
        get
        {
            double tray = TrayPanel?.ActualWidth ?? 0;
            if (S.Alignment != TaskAlignment.Center) return new Thickness(0, 0, tray, 0);
            double bar = BarGrid?.ActualWidth ?? 0;
            double group = TaskGroup?.ActualWidth ?? 0;
            double left = Math.Clamp(bar - tray - group, 0, tray);
            return new Thickness(left, 0, tray, 0);
        }
    }

    private void Layout_SizeChanged(object sender, SizeChangedEventArgs e) => Raise(nameof(TaskAreaMargin));

    // ------------------------------------------------------------------ tray status (volume / network / battery)
    private async Task UpdateStatusAsync()
    {
        if (_statusBusy) return;
        _statusBusy = true;
        try
        {
            // Audio stays on the UI thread (COM object lives here); network/battery probing goes off-thread.
            bool audio = AudioService.IsAvailable;
            int vol = audio ? AudioService.GetVolume() : 0;
            bool mute = audio && AudioService.GetMute();
            var (net, bat) = await Task.Run(() => (SystemStatus.GetNetwork(), SystemStatus.GetBattery()));
            ApplyStatus(audio, vol, mute, net, bat);
        }
        catch (Exception ex) { App.LogError(ex); }
        finally { _statusBusy = false; }
    }

    private void UpdateVolumeNow()
    {
        bool audio = AudioService.IsAvailable;
        ApplyVolume(audio, audio ? AudioService.GetVolume() : 0, audio && AudioService.GetMute());
        Raise(nameof(VolGlyph), nameof(VolTip), nameof(VolText));
    }

    private void ApplyVolume(bool audio, int vol, bool mute)
    {
        if (audio)
        {
            VolGlyph = mute || vol == 0 ? "" : vol < 34 ? "" : vol < 67 ? "" : "";
            VolTip = mute ? "Muted" : $"Volume: {vol}%";
            VolText = mute ? "Muted" : $"{vol}%";
            if (VolumePopup.IsOpen && !VolumeSlider.IsMouseCaptureWithin)
            {
                _volSliderSyncing = true;
                VolumeSlider.Value = vol;
                _volSliderSyncing = false;
            }
        }
        else { VolGlyph = ""; VolTip = "No audio device"; VolText = "—"; }
    }

    private void ApplyStatus(bool audio, int vol, bool mute, NetworkState net, BatteryState b)
    {
        ApplyVolume(audio, vol, mute);

        switch (net)
        {
            case NetworkState.Wifi: NetGlyph = ""; NetTip = "Wi-Fi connected"; break;
            case NetworkState.Wired: NetGlyph = ""; NetTip = "Ethernet connected"; break;
            default: NetGlyph = ""; NetTip = "No network"; break;
        }

        if (b.Present)
        {
            int level = Math.Clamp(b.Percent / 10, 0, 10);
            BatGlyph = b.Charging
                ? (level == 10 ? "" : ((char)(0xE85A + level)).ToString())
                : (level == 10 ? "" : ((char)(0xE850 + level)).ToString());
            BatText = $"{b.Percent}%";
            BatTip = b.Charging ? $"Battery {b.Percent}% (charging)" : $"Battery {b.Percent}%";
            BatteryVis = Visibility.Visible;
        }
        else BatteryVis = Visibility.Collapsed;

        Raise(nameof(VolGlyph), nameof(VolTip), nameof(VolText), nameof(NetGlyph), nameof(NetTip),
              nameof(BatGlyph), nameof(BatText), nameof(BatTip), nameof(BatteryVis));
    }

    private void Volume_Click(object sender, RoutedEventArgs e)
    {
        // _volPopupOpen reflects the state before WPF's dismiss (see field comment). If it was open, this click is
        // the one that closed it → ensure closed and do not reopen. Otherwise open it fresh.
        if (_volPopupOpen) { VolumePopup.IsOpen = false; return; }

        VolumePopup.HorizontalOffset = PopupXFor(VolumeButton);
        _volSliderSyncing = true;
        VolumeSlider.Value = AudioService.GetVolume();
        _volSliderSyncing = false;
        VolumePopup.IsOpen = true;
    }

    private void Volume_Wheel(object sender, MouseWheelEventArgs e)
    {
        AudioService.SetVolume(AudioService.GetVolume() + (e.Delta > 0 ? 2 : -2));
        UpdateVolumeNow();
        e.Handled = true;
    }

    private void Volume_RightClick(object sender, MouseButtonEventArgs e)
    {
        AudioService.SetMute(!AudioService.GetMute());
        UpdateVolumeNow();
        e.Handled = true;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        AudioService.SetMute(!AudioService.GetMute());
        UpdateVolumeNow();
    }

    private void VolumeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_volSliderSyncing) return;
        AudioService.SetVolume((int)Math.Round(e.NewValue));
        UpdateVolumeNow();
    }

    private void Network_Click(object sender, RoutedEventArgs e) => Win32.SendChord((byte)Win32.VK_LWIN, (byte)'A'); // Quick Settings
    private void Battery_Click(object sender, RoutedEventArgs e) => TryStart("ms-settings:batterysaver");
    private void Notifications_Click(object sender, RoutedEventArgs e) => Win32.SendChord((byte)Win32.VK_LWIN, (byte)'N');

    // ------------------------------------------------------------------ third-party tray icons
    private static TrayIcon? IconOf(object sender) => (sender as FrameworkElement)?.DataContext as TrayIcon;

    // Input is shared with the overflow flyout (see TrayInput). Events are deliberately NOT marked handled:
    // ButtonBase needs the bubbling MouseLeftButtonUp to release its capture, and the Button has no Click handler.
    private void TrayIcon_MouseDown(object sender, MouseButtonEventArgs e) { if (IconOf(sender) is { } i) TrayInput.MouseDown(i, e); }
    private void TrayIcon_MouseUp(object sender, MouseButtonEventArgs e) { if (IconOf(sender) is { } i) TrayInput.MouseUp(i, e); }
    private void TrayIcon_DoubleClick(object sender, MouseButtonEventArgs e) { if (IconOf(sender) is { } i) TrayInput.DoubleClick(i, e); }
    private void TrayIcon_MouseMove(object sender, MouseEventArgs e) { if (sender is FrameworkElement el && IconOf(sender) is { } i) TrayInput.MouseMove(el, i, e); }

    /// <summary>The owning app shows its own menu; keep the bar's context menu from opening on top of it.</summary>
    private void TrayIcon_ContextMenuOpening(object sender, ContextMenuEventArgs e) => e.Handled = true;

    private void Chevron_Click(object sender, RoutedEventArgs e) => App.Current.ToggleTrayFlyout(this);

    private void TrayShowAll_Click(object sender, RoutedEventArgs e)
    {
        S.TrayShowAll = true;
        App.Current.ApplySettings();
    }

    // Dropping an icon anywhere on the tray area promotes it onto the bar.
    private void TrayArea_DragOver(object sender, DragEventArgs e) => TrayInput.DragOver(e);
    private void TrayArea_Drop(object sender, DragEventArgs e)
    {
        if (TrayInput.DroppedKey(e) is { } key) App.Current.PromoteTrayIcon(key, promote: true);
        e.Handled = true;
    }

    /// <summary>Answers Shell_NotifyIconGetRect for apps that anchor flyouts to their icon (device pixels).</summary>
    private Win32.RECT? TrayIconRect(TrayIcon icon)
    {
        try
        {
            if (TrayItems.ItemContainerGenerator.ContainerFromItem(icon) is not FrameworkElement container) return null;
            var el = container.IsVisible ? container : null;
            if (el == null || PresentationSource.FromVisual(el) == null) return null;
            var tl = el.PointToScreen(new Point(0, 0));
            var br = el.PointToScreen(new Point(el.ActualWidth, el.ActualHeight));
            return new Win32.RECT { Left = (int)tl.X, Top = (int)tl.Y, Right = (int)br.X, Bottom = (int)br.Y };
        }
        catch { return null; }
    }

    private void HookTrayRects()
    {
        if (IsPrimary && App.Current.Tray is { } tray) tray.RectProvider = TrayIconRect;
    }

    // ------------------------------------------------------------------ start / tray
    private void Start_Click(object sender, RoutedEventArgs e) => App.Current.ToggleStartMenu(this);
    private void Search_Click(object sender, RoutedEventArgs e) => App.Current.ToggleSearch(this);
    private void TaskView_Click(object sender, RoutedEventArgs e) => Win32.SendChord((byte)Win32.VK_LWIN, (byte)Win32.VK_TAB);
    private void ShowDesktop_Click(object sender, RoutedEventArgs e) => Win32.SendChord((byte)Win32.VK_LWIN, (byte)Win32.VK_D);
    private void OpenSettings_Click(object sender, RoutedEventArgs e) => App.Current.OpenSettings();
    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();

    private void TaskManager_Click(object sender, RoutedEventArgs e) => TryStart("taskmgr.exe");

    private void ToggleNative_Click(object sender, RoutedEventArgs e)
    {
        S.HideNativeTaskbar = !S.HideNativeTaskbar;
        App.Current.ApplySettings();
    }

    private void UpdateNativeToggleText() =>
        ToggleNativeItem.Header = S.HideNativeTaskbar ? "Show Windows taskbar" : "Hide Windows taskbar";

    private void TaskScroller_Wheel(object sender, MouseWheelEventArgs e)
    {
        if (TaskScroller.ScrollableWidth <= 0) return;
        TaskScroller.ScrollToHorizontalOffset(TaskScroller.HorizontalOffset - Math.Sign(e.Delta) * 60);
        e.Handled = true;
    }

    // ------------------------------------------------------------------ task buttons
    private static TaskItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as TaskItem;

    private void Task_Click(object sender, RoutedEventArgs e)
    {
        var item = ItemOf(sender);
        if (item == null) return;

        if (!item.IsRunning)
        {
            Launch(item);
            return;
        }

        var fg = Win32.GetForegroundWindow();
        if (item.Windows.Count > 1)
        {
            // Cycle through the group's windows
            int idx = item.Windows.IndexOf(fg);
            var next = item.Windows[(idx + 1) % item.Windows.Count];
            Win32.ActivateWindow(next);
        }
        else if (item.Hwnd == fg)
        {
            Win32.ShowWindowAsync(item.Hwnd, Win32.SW_MINIMIZE);
        }
        else Win32.ActivateWindow(item.Hwnd);

        App.Current.Tracker.Refresh();
    }

    private void Task_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && ItemOf(sender) is { CanLaunch: true } item) Launch(item);
    }

    private void TaskLaunchNew_Click(object sender, RoutedEventArgs e) { if (ItemOf(sender) is { } i) Launch(i); }

    private void TaskPin_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { ExePath: { } exe }) return;
        if (!S.Pinned.Contains(exe, StringComparer.OrdinalIgnoreCase)) S.Pinned.Add(exe);
        App.Current.ApplySettings();
    }

    private void TaskUnpin_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { PinPath: { } pin }) return;
        S.Pinned.RemoveAll(p => string.Equals(p, pin, StringComparison.OrdinalIgnoreCase));
        App.Current.ApplySettings();
    }

    private void TaskClose_Click(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item) return;
        foreach (var h in item.Windows.ToList()) Win32.PostMessage(h, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static void Launch(TaskItem item)
    {
        var path = item.PinPath ?? item.ExePath;
        if (path != null) TryStart(path);
    }

    private static void TryStart(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Could not start", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
