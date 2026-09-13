using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TaskbarTYOL.Models;
using TaskbarTYOL.Services;

namespace TaskbarTYOL.Views;

/// <summary>The "^" overflow flyout: notification icons that are not promoted to the bar.</summary>
public partial class TrayFlyoutWindow : Window
{
    private ListCollectionView? _view;
    private DateTime _lastDeactivated;

    public TrayFlyoutWindow()
    {
        InitializeComponent();
    }

    public bool JustDeactivated => (DateTime.UtcNow - _lastDeactivated).TotalMilliseconds < 300;

    private void EnsureView()
    {
        var tray = App.Current.Tray;
        if (tray == null) { IconList.ItemsSource = null; _view = null; return; }
        if (_view != null && ReferenceEquals(_view.SourceCollection, tray.Icons)) return;

        _view = new ListCollectionView(tray.Icons)
        {
            Filter = o => o is TrayIcon i && !i.IsHidden && !i.IsPromoted,
            IsLiveFiltering = true,
        };
        _view.LiveFilteringProperties.Add(nameof(TrayIcon.IsHidden));
        _view.LiveFilteringProperties.Add(nameof(TrayIcon.IsPromoted));
        ((INotifyCollectionChanged)_view).CollectionChanged += (_, _) => UpdateEmpty();
        IconList.ItemsSource = _view;
        UpdateEmpty();
    }

    private void UpdateEmpty() => EmptyText.Visibility = _view is { IsEmpty: true } ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Show above/below the chevron, right-aligned to it (like Windows), flush with the bar.</summary>
    public void ShowAt(TaskbarWindow bar, Rect chevronDip)
    {
        EnsureView();
        var barRect = bar.GetBarRectDip();
        var screen = bar.GetMonitorRectDip();
        const double shadow = 10;

        // Need our size first: measure via SizeToContent by showing off-screen-safe, then position.
        UpdateLayout();
        double w = ActualWidth > 0 ? ActualWidth : 200;
        double h = ActualHeight > 0 ? ActualHeight : 120;

        Left = Math.Max(screen.Left - shadow, Math.Min(chevronDip.Right - w + shadow, screen.Right - w + shadow));
        Top = App.Current.Settings.Edge == TaskbarEdge.Top ? barRect.Bottom - shadow : barRect.Top - h + shadow;

        Show();
        Native.Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Activate();

        // SizeToContent may have changed the size after Show → re-anchor once.
        Dispatcher.BeginInvoke(() =>
        {
            Left = Math.Max(screen.Left - shadow, Math.Min(chevronDip.Right - ActualWidth + shadow, screen.Right - ActualWidth + shadow));
            Top = App.Current.Settings.Edge == TaskbarEdge.Top ? barRect.Bottom - shadow : barRect.Top - ActualHeight + shadow;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---- icon input (shared with the bar) ----
    private static TrayIcon? IconOf(object sender) => (sender as FrameworkElement)?.DataContext as TrayIcon;
    private void Icon_MouseDown(object sender, MouseButtonEventArgs e) { if (IconOf(sender) is { } i) TrayInput.MouseDown(i, e); }
    private void Icon_MouseUp(object sender, MouseButtonEventArgs e) { if (IconOf(sender) is { } i) TrayInput.MouseUp(i, e); }
    private void Icon_DoubleClick(object sender, MouseButtonEventArgs e) { if (IconOf(sender) is { } i) TrayInput.DoubleClick(i, e); }
    private void Icon_MouseMove(object sender, MouseEventArgs e) { if (sender is FrameworkElement el && IconOf(sender) is { } i) TrayInput.MouseMove(el, i, e); }
    private void Icon_ContextMenuOpening(object sender, ContextMenuEventArgs e) => e.Handled = true;

    // ---- drop target: hide (demote) ----
    private void Root_DragOver(object sender, DragEventArgs e) => TrayInput.DragOver(e);
    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (TrayInput.DroppedKey(e) is { } key) App.Current.PromoteTrayIcon(key, promote: false);
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!IsVisible) return;
        _lastDeactivated = DateTime.UtcNow;
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Current.IsShuttingDown) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
