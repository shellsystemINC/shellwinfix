using System.Runtime.InteropServices;

namespace TaskbarTYOL.Native;

/// <summary>
/// Low-level keyboard hook that intercepts a *standalone* Windows-key tap (no other key in between)
/// and raises <see cref="WinKeyPressed"/> instead of letting the native Start menu open. Also takes over
/// Ctrl+Esc (classic Start shortcut) and Win+S (search). Every other Win+X chord passes through untouched.
///
/// Technique (the same "menu mask key" idea AutoHotkey uses): right after the Win key goes down we
/// inject a Ctrl tap from a worker thread. The shell then sees "Win + Ctrl" instead of a bare Win tap
/// and does not open its Start menu on release. On release we decide whether it was a bare tap.
/// Our own injected Ctrl carries a marker in dwExtraInfo so the hook can ignore it.
/// </summary>
internal sealed class WinKeyHook : IDisposable
{
    private const byte VK_MASK = (byte)Win32.VK_CONTROL;
    private const uint VK_ESCAPE = 0x1B, VK_S = 0x53;
    private static readonly UIntPtr Marker = new(0x54594F4C); // "TYOL"

    private IntPtr _hook;
    private Win32.LowLevelKeyboardProc? _proc;   // kept alive so the GC never collects the delegate
    private const uint VK_SHIFT = 0x10;
    private bool _winDown;
    private bool _otherKeyWhileWinDown;
    private bool _swallowS;

    private static bool IsDown(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
    private static bool IsDown(uint vk) => IsDown((int)vk);

    public event Action? WinKeyPressed;
    public event Action? SearchRequested;
    public bool Enabled { get; set; } = true;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookProc;
        _hook = Win32.SetWindowsHookEx(Win32.WH_KEYBOARD_LL, _proc, Win32.GetModuleHandle(null), 0);
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && Enabled)
        {
            var info = Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool isDown = msg is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN;
            bool isUp = msg is Win32.WM_KEYUP or Win32.WM_SYSKEYUP;
            bool isWin = info.vkCode is Win32.VK_LWIN or Win32.VK_RWIN;
            bool isOurMask = info.dwExtraInfo == (IntPtr)(long)(ulong)Marker;

            if (isOurMask)
            {
                // our own marker tap: let it through so the shell counts it, but never treat it as "another key"
            }
            else if (isDown && info.vkCode == VK_ESCAPE && IsDown(Win32.VK_CONTROL) && !IsDown(VK_SHIFT) && !IsDown(Win32.VK_MENU) && !_winDown)
            {
                // Ctrl+Esc is the classic keyboard shortcut for the Start menu: take it over and swallow it.
                // (Ctrl+Shift+Esc = Task Manager must keep working, hence the Shift check.)
                WinKeyPressed?.Invoke();
                return (IntPtr)1;
            }
            else if (isWin && isDown)
            {
                if (!_winDown)
                {
                    _winDown = true;
                    _otherKeyWhileWinDown = false;
                    // Inject AFTER this hook returns so the Win-down is already delivered when Ctrl arrives.
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        Win32.keybd_event(VK_MASK, 0, 0, Marker);
                        Win32.keybd_event(VK_MASK, 0, Win32.KEYEVENTF_KEYUP, Marker);
                    });
                }
            }
            else if (isWin && isUp)
            {
                bool standalone = _winDown && !_otherKeyWhileWinDown;
                _winDown = false;
                if (standalone) WinKeyPressed?.Invoke();
            }
            else if (_winDown && isDown)
            {
                _otherKeyWhileWinDown = true;
                if (info.vkCode == VK_S && !IsDown(VK_SHIFT) && !IsDown(Win32.VK_CONTROL) && !IsDown(Win32.VK_MENU))
                {
                    // Win+S → our Everything search instead of Windows Search. Swallow the S (down and up).
                    // Win+Shift+S (Snipping Tool) and other modifiers are left alone.
                    _swallowS = true;
                    SearchRequested?.Invoke();
                    return (IntPtr)1;
                }
            }
            else if (_swallowS && isUp && info.vkCode == VK_S)
            {
                _swallowS = false;
                return (IntPtr)1;
            }
        }
        return Win32.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
