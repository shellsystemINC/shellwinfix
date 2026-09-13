using System.Windows;
using System.Windows.Interop;

namespace TaskbarTYOL.Native;

/// <summary>Registers a WPF window as a shell AppBar so the desktop work area shrinks to make room for it.</summary>
internal sealed class AppBar : IDisposable
{
    private const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3;
    public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    private const int WM_USER = 0x0400;
    private const uint CallbackMsg = WM_USER + 0x1234;

    private readonly Window _window;
    private IntPtr _hwnd;
    private bool _registered;

    public AppBar(Window window) => _window = window;

    public void Register()
    {
        if (_registered) return;
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        var abd = MakeData();
        abd.uCallbackMessage = CallbackMsg;
        Win32.SHAppBarMessage(ABM_NEW, ref abd);
        _registered = true;
    }

    /// <summary>Reserve the given edge of the given monitor (device pixels) at the given thickness.</summary>
    public void SetPosition(uint edge, int thicknessPx, Win32.RECT monitor, bool forceEdge = true)
    {
        if (!_registered) Register();
        var abd = MakeData();
        abd.uEdge = edge;

        if (edge == ABE_TOP || edge == ABE_BOTTOM)
        {
            abd.rc.Left = monitor.Left; abd.rc.Right = monitor.Right;
            if (edge == ABE_TOP) { abd.rc.Top = monitor.Top; abd.rc.Bottom = monitor.Top + thicknessPx; }
            else { abd.rc.Bottom = monitor.Bottom; abd.rc.Top = monitor.Bottom - thicknessPx; }
        }
        else
        {
            abd.rc.Top = monitor.Top; abd.rc.Bottom = monitor.Bottom;
            if (edge == ABE_LEFT) { abd.rc.Left = monitor.Left; abd.rc.Right = monitor.Left + thicknessPx; }
            else { abd.rc.Right = monitor.Right; abd.rc.Left = monitor.Right - thicknessPx; }
        }

        Win32.SHAppBarMessage(ABM_QUERYPOS, ref abd);

        // We ARE the taskbar: insist on hugging the monitor edge, whatever else the shell thinks is
        // reserved there (e.g. the ghost strip of the hidden Explorer taskbar), and re-apply thickness.
        // When the user chose to keep the Windows taskbar visible we respect the shell's adjusted position instead.
        switch (edge)
        {
            case ABE_TOP: if (forceEdge) abd.rc.Top = monitor.Top; abd.rc.Bottom = abd.rc.Top + thicknessPx; break;
            case ABE_BOTTOM: if (forceEdge) abd.rc.Bottom = monitor.Bottom; abd.rc.Top = abd.rc.Bottom - thicknessPx; break;
            case ABE_LEFT: if (forceEdge) abd.rc.Left = monitor.Left; abd.rc.Right = abd.rc.Left + thicknessPx; break;
            case ABE_RIGHT: if (forceEdge) abd.rc.Right = monitor.Right; abd.rc.Left = abd.rc.Right - thicknessPx; break;
        }

        Win32.SHAppBarMessage(ABM_SETPOS, ref abd);

        // Physically move the window (SetWindowPos works in device pixels, like the AppBar rect).
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST,
            abd.rc.Left, abd.rc.Top, abd.rc.Right - abd.rc.Left, abd.rc.Bottom - abd.rc.Top,
            Win32.SWP_NOACTIVATE | Win32.SWP_SHOWWINDOW);

        // Belt and braces: set this monitor's work area to "monitor minus our bar" ourselves, so maximised
        // windows fill everything right up to the bar even if the shell's own bookkeeping is stale.
        // (Only when we own the edge — otherwise the Windows taskbar's own reservation must stay.)
        if (!forceEdge) return;
        var work = monitor;
        switch (edge)
        {
            case ABE_TOP: work.Top = abd.rc.Bottom; break;
            case ABE_BOTTOM: work.Bottom = abd.rc.Top; break;
            case ABE_LEFT: work.Left = abd.rc.Right; break;
            case ABE_RIGHT: work.Right = abd.rc.Left; break;
        }
        Win32.SystemParametersInfo(Win32.SPI_SETWORKAREA, 0, ref work, Win32.SPIF_SENDCHANGE);
    }

    public void Unregister()
    {
        if (!_registered) return;
        var abd = MakeData();
        Win32.SHAppBarMessage(ABM_REMOVE, ref abd);
        _registered = false;
    }

    private Win32.APPBARDATA MakeData() => new()
    {
        cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Win32.APPBARDATA>(),
        hWnd = _hwnd,
    };

    public void Dispose() => Unregister();
}
