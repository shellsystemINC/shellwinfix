using System.Windows;
using System.Windows.Input;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

/// <summary>
/// Mouse handling shared by every place a hosted tray icon is rendered (bar + overflow flyout):
/// forwards clicks to the owning app the way Explorer does, and turns a left-drag into a WPF drag-and-drop
/// carrying the icon's PersistKey so it can be dropped on the bar (promote) or into the flyout (demote).
/// </summary>
internal static class TrayInput
{
    public const string DragFormat = "TaskbarTYOL.TrayIconKey";
    private const double DragThreshold = 8;

    private static TrayIcon? _downIcon;
    private static Point _downPos;
    private static bool _dragged;

    private static (int x, int y) Cursor()
    {
        Win32.GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    public static void MouseDown(TrayIcon icon, MouseButtonEventArgs e)
    {
        if (App.Current.Tray is not { } tray) return;
        var (x, y) = Cursor();
        if (e.ChangedButton == MouseButton.Left)
        {
            _downIcon = icon;
            _downPos = new Point(x, y);
            _dragged = false;
        }
        uint msg = e.ChangedButton switch
        {
            MouseButton.Left => Win32.WM_LBUTTONDOWN,
            MouseButton.Right => Win32.WM_RBUTTONDOWN,
            MouseButton.Middle => Win32.WM_MBUTTONDOWN,
            _ => 0,
        };
        if (msg != 0) tray.SendMouse(icon, msg, x, y);
    }

    public static void MouseMove(FrameworkElement source, TrayIcon icon, MouseEventArgs e)
    {
        if (App.Current.Tray is not { } tray) return;
        var (x, y) = Cursor();

        // NOTE: wired to PreviewMouseMove — ButtonBase marks the bubbling MouseMove as handled while it is pressed
        // and has capture, which would silently starve this drag detector.
        if (e.LeftButton == MouseButtonState.Pressed && ReferenceEquals(_downIcon, icon) && !_dragged)
        {
            var d = new Point(x, y) - _downPos;
            if (Math.Abs(d.X) > DragThreshold || Math.Abs(d.Y) > DragThreshold)
            {
                _dragged = true;
                App.Log($"drag start {icon.DisplayName} key={icon.PersistKey}");
                try
                {
                    var result = DragDrop.DoDragDrop(source, new DataObject(DragFormat, icon.PersistKey), DragDropEffects.Move);
                    App.Log($"drag end result={result}");
                }
                catch (Exception ex) { App.Log("drag failed: " + ex.Message); }
                return;
            }
        }
        tray.SendMouse(icon, Win32.WM_MOUSEMOVE, x, y);
    }

    public static void MouseUp(TrayIcon icon, MouseButtonEventArgs e)
    {
        if (App.Current.Tray is not { } tray) return;
        if (e.ChangedButton == MouseButton.Left && _dragged) { _dragged = false; _downIcon = null; return; }
        var (x, y) = Cursor();
        switch (e.ChangedButton)
        {
            case MouseButton.Left:
                tray.SendMouse(icon, Win32.WM_LBUTTONUP, x, y);
                if (icon.Version >= 4) tray.SendMouse(icon, Win32.NIN_SELECT, x, y);
                _downIcon = null;
                break;
            case MouseButton.Right:
                tray.SendMouse(icon, Win32.WM_RBUTTONUP, x, y);
                if (icon.Version >= 4) tray.SendMouse(icon, Win32.WM_CONTEXTMENU, x, y);
                break;
            case MouseButton.Middle:
                tray.SendMouse(icon, Win32.WM_MBUTTONUP, x, y);
                break;
        }
    }

    public static void DoubleClick(TrayIcon icon, MouseButtonEventArgs e)
    {
        if (App.Current.Tray is not { } tray || e.ChangedButton != MouseButton.Left) return;
        var (x, y) = Cursor();
        tray.SendMouse(icon, Win32.WM_LBUTTONDBLCLK, x, y);
    }

    // ---- drop-target helpers ----
    public static void DragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>Returns the dragged icon key, or null if this drop is not ours.</summary>
    public static string? DroppedKey(DragEventArgs e)
    {
        var key = e.Data.GetDataPresent(DragFormat) ? e.Data.GetData(DragFormat) as string : null;
        App.Log($"drop key={key ?? "(none)"} on {(e.Source as FrameworkElement)?.GetType().Name}");
        return key;
    }
}
