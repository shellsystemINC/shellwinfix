using System.Runtime.InteropServices;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

/// <summary>
/// Hides / restores the built-in Windows taskbar(s). Always restore on exit!
///
/// Simply hiding Shell_TrayWnd is not enough: the shell keeps reserving its strip of the work area,
/// so any AppBar we register gets pushed *above* that ghost strip and our bar floats. The fix is to
/// flip Explorer's taskbar into auto-hide (which reserves nothing) before hiding its window.
/// The taskbar's original state is persisted in settings so it survives a force-kill of this process
/// and is still restored correctly on the next graceful exit.
/// </summary>
internal static class NativeTaskbar
{
    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;

    private static bool _hidden;

    public static void Hide(Settings settings)
    {
        var primary = Win32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero)
        {
            if (settings.NativeTaskbarOriginalState is null)
            {
                int current = GetState(primary);
                // If the tray is already hidden, a previous instance of ours was killed mid-flight and the
                // auto-hide flag is ours, not the user's. Otherwise trust what we see.
                settings.NativeTaskbarOriginalState = Win32.IsWindowVisible(primary) ? current : current & ~ABS_AUTOHIDE;
                settings.Save();
            }
            SetState(primary, settings.NativeTaskbarOriginalState.Value | ABS_AUTOHIDE);
        }
        foreach (var h in FindAll()) Win32.ShowWindow(h, Win32.SW_HIDE);
        _hidden = true;
    }

    public static void Show(Settings? settings)
    {
        foreach (var h in FindAll()) Win32.ShowWindow(h, Win32.SW_SHOW);
        var primary = Win32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && settings?.NativeTaskbarOriginalState is int st)
        {
            SetState(primary, st);
            settings.NativeTaskbarOriginalState = null; // restored → forget, re-capture next time
            settings.Save();
        }
        _hidden = false;
    }

    /// <summary>Explorer likes to re-show its taskbar (e.g. after a crash/restart). Call periodically. Returns true if it had to act.</summary>
    public static bool Enforce(Settings settings)
    {
        if (!_hidden) return false;
        bool visibleAgain = FindAll().Any(Win32.IsWindowVisible);
        if (!visibleAgain) return false;

        // Explorer restarted: its state was reset, redo the whole dance.
        var primary = Win32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) SetState(primary, (settings.NativeTaskbarOriginalState ?? 0) | ABS_AUTOHIDE);
        foreach (var h in FindAll()) Win32.ShowWindow(h, Win32.SW_HIDE);
        return true;
    }

    private static int GetState(IntPtr tray)
    {
        var abd = new Win32.APPBARDATA { cbSize = Marshal.SizeOf<Win32.APPBARDATA>(), hWnd = tray };
        return (int)Win32.SHAppBarMessage(ABM_GETSTATE, ref abd);
    }

    private static void SetState(IntPtr tray, int state)
    {
        var abd = new Win32.APPBARDATA { cbSize = Marshal.SizeOf<Win32.APPBARDATA>(), hWnd = tray, lParam = (IntPtr)state };
        Win32.SHAppBarMessage(ABM_SETSTATE, ref abd);
    }

    private static IEnumerable<IntPtr> FindAll()
    {
        var primary = Win32.FindWindow("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero) yield return primary;
        IntPtr sec = IntPtr.Zero;
        while ((sec = Win32.FindWindowEx(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            yield return sec;
    }
}
