using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

/// <summary>
/// Builds and shows OUR themed desktop right-click menu (a WPF ContextMenu, so it picks up the active theme's
/// styles automatically — matching the taskbar and start menu). Populated with the user's custom "Desktop" entries
/// plus the usual desktop actions. Shown by App when DesktopRightClickHook fires.
/// </summary>
internal static class DesktopMenu
{
    private static ContextMenu? _open;   // keep a reference while open so it is not collected

    public static void ShowAtCursor(Settings settings)
    {
        // Position at the cursor in DIPs. (MousePoint placement misfires here because we open the menu after the
        // hook returns, from a process with no window at that point.)
        Win32.GetCursorPos(out var cur);
        double sx = App.Current.Taskbar?.DpiScaleX ?? 1.0;
        double sy = App.Current.Taskbar?.DpiScaleY ?? 1.0;
        var menu = new ContextMenu
        {
            Placement = PlacementMode.Absolute,
            HorizontalOffset = cur.X / sx,
            VerticalOffset = cur.Y / sy,
            StaysOpen = false,
        };

        // 1) user-defined entries that target the desktop background
        var custom = settings.ContextEntries
            .Where(e => e.Enabled && e.Targets.Contains(ContextTarget.DesktopBackground)
                        && !string.IsNullOrWhiteSpace(e.Label) && !string.IsNullOrWhiteSpace(e.Command))
            .ToList();
        foreach (var e in custom)
            menu.Items.Add(MakeItem(e.Label, e.IconPath, () => RunCommand(e.Command)));

        if (custom.Count > 0) menu.Items.Add(new Separator());

        // 2) standard desktop actions
        menu.Items.Add(MakeItem("Refresh", null, RefreshDesktop));
        menu.Items.Add(MakeItem("Paste", null, Paste));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Display settings", null, () => Launch("ms-settings:display")));
        menu.Items.Add(MakeItem("Personalize", null, () => Launch("ms-settings:personalization")));
        menu.Items.Add(new Separator());
        menu.Items.Add(MakeItem("Taskbar settings", null, () => App.Current.OpenSettings()));

        _open = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_open, menu)) _open = null; };
        menu.IsOpen = true;
    }

    private static MenuItem MakeItem(string header, string? iconPath, Action onClick)
    {
        var mi = new MenuItem { Header = header };
        var icon = LoadIcon(iconPath);
        if (icon != null) mi.Icon = new Image { Source = icon, Width = 16, Height = 16 };
        mi.Click += (_, _) => { try { onClick(); } catch (Exception ex) { App.LogError(ex); } };
        return mi;
    }

    private static ImageSource? LoadIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath)) return null;
        // Only handle a plain file path here (a "file.dll,3" style spec is left iconless rather than mis-parsed).
        var path = iconPath!.Contains(',') ? iconPath[..iconPath.LastIndexOf(',')] : iconPath;
        try { return System.IO.File.Exists(path) ? IconHelper.GetFileIcon(path, large: false) : null; }
        catch { return null; }
    }

    // ------------------------------------------------------------------ actions
    private static void RunCommand(string command)
    {
        // The clicked "folder" for a desktop right-click is the Desktop directory.
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string cmd = command.Replace("%V", desktop).Replace("%1", desktop);
        var (exe, args) = SplitCommand(cmd);
        if (exe.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = true, WorkingDirectory = desktop }); }
        catch (Exception ex)
        {
            if (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }) App.LogError(ex);
        }
    }

    /// <summary>Split a command line into the executable and the remaining arguments (respecting a quoted exe).</summary>
    private static (string exe, string args) SplitCommand(string cmd)
    {
        cmd = cmd.TrimStart();
        if (cmd.StartsWith('"'))
        {
            int end = cmd.IndexOf('"', 1);
            if (end > 0) return (cmd[1..end], cmd[(end + 1)..].TrimStart());
        }
        int sp = cmd.IndexOf(' ');
        return sp < 0 ? (cmd, "") : (cmd[..sp], cmd[(sp + 1)..].TrimStart());
    }

    private static void Launch(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { App.LogError(ex); }
    }

    private static void RefreshDesktop()
    {
        var defview = FindDefView();
        if (defview != IntPtr.Zero) Win32.SendMessage(defview, Win32.WM_COMMAND, (IntPtr)0x7103 /* refresh */, IntPtr.Zero);
    }

    private static void Paste()
    {
        var defview = FindDefView();
        if (defview != IntPtr.Zero) Win32.SendMessage(defview, Win32.WM_COMMAND, (IntPtr)0x7402 /* paste */, IntPtr.Zero);
    }

    /// <summary>Find the desktop's SHELLDLL_DefView (under Progman, or under a WorkerW when wallpaper slideshow is on).</summary>
    private static IntPtr FindDefView()
    {
        var progman = Win32.FindWindow("Progman", null);
        var defview = Win32.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defview != IntPtr.Zero) return defview;

        IntPtr worker = IntPtr.Zero;
        while ((worker = Win32.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null)) != IntPtr.Zero)
        {
            defview = Win32.FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defview != IntPtr.Zero) return defview;
        }
        return IntPtr.Zero;
    }
}
