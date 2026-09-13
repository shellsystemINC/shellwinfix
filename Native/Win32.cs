using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarTYOL.Native;

internal static class Win32
{
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_VISIBLE = 0x10000000L;
    public const long WS_EX_TOOLWINDOW = 0x00000080L;
    public const long WS_EX_APPWINDOW = 0x00040000L;
    public const long WS_EX_NOACTIVATE = 0x08000000L;

    public const uint GW_OWNER = 4;
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;

    public const int DWMWA_CLOAKED = 14;

    public const uint WM_GETICON = 0x007F;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const int SC_CLOSE = 0xF060;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int ICON_SMALL2 = 2;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_TAB = 0x09;
    public const int VK_MENU = 0x12;
    public const int VK_CONTROL = 0x11;
    public const int VK_D = 0x44;
    public const byte VK_NONAME = 0xFC;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_SMALLICON = 0x000000001;

    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    public const uint EWX_LOGOFF = 0x00000000;
    public const uint EWX_SHUTDOWN = 0x00000001;
    public const uint EWX_REBOOT = 0x00000002;
    public const uint EWX_POWEROFF = 0x00000008;

    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    // ---- user32 ----
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? lpClassName, string? lpWindowName);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW")] public static extern IntPtr SetWindowsHookExPtr(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool LockWorkStation();
    [DllImport("user32.dll")] public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int nIndex);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref RECT pvParam, uint fWinIni);

    public const uint SPI_SETWORKAREA = 0x002F, SPI_GETWORKAREA = 0x0030;
    public const uint SPIF_SENDCHANGE = 0x0002;

    // ---- raw window creation (for the fake Shell_TrayWnd) ----
    public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEX
    {
        public int cbSize;
        public uint style;
        public WndProc lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    public const uint WM_COPYDATA = 0x004A;
    public const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203,
                      WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_CONTEXTMENU = 0x007B;
    public const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;
    public const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000;
    public const uint WS_EX_TOOLWINDOW_U = 0x00000080, WS_EX_TOPMOST_U = 0x00000008;
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern ushort RegisterClassEx(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowEx(uint exStyle, string className, string? windowName, uint style, int x, int y, int w, int h,
                                               IntPtr parent, IntPtr menu, IntPtr hInstance, IntPtr param);
    [DllImport("user32.dll")] public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr CopyIcon(IntPtr hIcon);
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(uint pid);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    // ---- dwmapi ----
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // ---- shell32 ----
    [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll")] public static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(IntPtr hWnd, ref POINT pt);

    // Cross-process memory (for LVM_HITTEST against the desktop listview in explorer.exe).
    [DllImport("kernel32.dll")] public static extern IntPtr VirtualAllocEx(IntPtr hProc, IntPtr addr, IntPtr size, uint allocType, uint protect);
    [DllImport("kernel32.dll")] public static extern bool VirtualFreeEx(IntPtr hProc, IntPtr addr, IntPtr size, uint freeType);
    [DllImport("kernel32.dll")] public static extern bool WriteProcessMemory(IntPtr hProc, IntPtr addr, byte[] buffer, IntPtr size, out IntPtr written);

    public const uint PROCESS_VM_OPERATION = 0x0008, PROCESS_VM_READ = 0x0010, PROCESS_VM_WRITE = 0x0020;
    public const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, PAGE_READWRITE = 0x04;
    public const uint LVM_HITTEST = 0x1012; // LVM_FIRST(0x1000)+18

    public const int WH_MOUSE_LL = 14;
    public const uint WM_COMMAND = 0x0111;

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    // ---- kernel32 ----
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr LoadLibrary(string lpFileName);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)] public static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr hObject);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string? lpModuleName);

    // ---- powrprof ----
    [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    // ---- helpers ----
    public static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string GetWindowClass(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static bool IsCloaked(IntPtr hWnd)
    {
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0)
            return cloaked != 0;
        return false;
    }

    public static string? GetProcessPath(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        if (pid == 0) return null;
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    /// <summary>Activates a window the way the real taskbar does (Alt-key trick so SetForegroundWindow is honoured).</summary>
    public static void ActivateWindow(IntPtr hWnd)
    {
        if (IsIconic(hWnd)) ShowWindowAsync(hWnd, SW_RESTORE);
        keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        SetForegroundWindow(hWnd);
        BringWindowToTop(hWnd);
    }

    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

    /// <summary>
    /// Brings one of OUR popup windows (start menu, search) to the foreground even though this process is not the
    /// foreground process — Windows normally refuses SetForegroundWindow in that situation, which left the popup
    /// visible-but-inactive so keystrokes went to the previous app. Attaching to the foreground thread's input queue
    /// grants the permission; the Alt tap is the fallback for the rare case where that is not enough.
    /// </summary>
    public static void ForceForeground(IntPtr hWnd)
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == hWnd) return;
        uint fgThread = fg != IntPtr.Zero ? GetWindowThreadProcessId(fg, out _) : 0;
        uint myThread = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != myThread && AttachThreadInput(myThread, fgThread, true);
        try
        {
            SetForegroundWindow(hWnd);
            BringWindowToTop(hWnd);
            SetFocus(hWnd);
        }
        finally { if (attached) AttachThreadInput(myThread, fgThread, false); }

        if (GetForegroundWindow() != hWnd)
        {
            keybd_event((byte)VK_MENU, 0, 0, UIntPtr.Zero);
            keybd_event((byte)VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            SetForegroundWindow(hWnd);
            SetFocus(hWnd);
        }
    }

    /// <summary>Sends a key chord such as Win+Tab or Win+D via synthesized input.</summary>
    public static void SendChord(params byte[] keys)
    {
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
