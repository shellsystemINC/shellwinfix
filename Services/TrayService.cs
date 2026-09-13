using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskbarTYOL.Models;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

/// <summary>
/// Becomes the notification-area host. Shell_NotifyIcon() in every process simply does
/// FindWindow("Shell_TrayWnd") and sends WM_COPYDATA to whatever it finds first, so we create our own
/// top-most window of that class, broadcast "TaskbarCreated" so every app re-registers its icon with us,
/// and render the icons ourselves. AppBar traffic that lands on us (dwData 0) is forwarded to Explorer's
/// real tray window so SHAppBarMessage keeps working for everyone, including this app.
/// </summary>
internal sealed class TrayService : IDisposable
{
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETFOCUS = 3, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_STATE = 8, NIF_INFO = 0x10, NIF_GUID = 0x20;
    private const uint NIS_HIDDEN = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA32
    {
        public int cbSize;
        public uint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public uint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public uint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHELLTRAYDATA
    {
        public int dwMagic;
        public int dwMessage;
        public NOTIFYICONDATA32 nid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER32
    {
        public int dwMagic;
        public int dwMessage;
        public int cbSize;
        public uint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    private readonly Win32.WndProc _wndProc;          // kept alive for the unmanaged class
    private IntPtr _hwnd, _notifyHwnd;
    private IntPtr _realTray;
    private readonly DispatcherTimer _cleanup;
    private readonly uint _taskbarCreatedMsg = Win32.RegisterWindowMessage("TaskbarCreated");

    public ObservableCollection<TrayIcon> Icons { get; } = new();
    public bool IsRunning => _hwnd != IntPtr.Zero;

    public TrayService()
    {
        _wndProc = WndProc;
        _cleanup = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _cleanup.Tick += (_, _) => RemoveDeadIcons();
    }

    public void Start()
    {
        if (IsRunning) return;
        var hInst = Win32.GetModuleHandle(null);

        var wc = new Win32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = hInst,
            lpszClassName = "Shell_TrayWnd",
        };
        Win32.RegisterClassEx(ref wc); // may already be registered on a restart → fine
        var wc2 = wc; wc2.lpszClassName = "TrayNotifyWnd";
        Win32.RegisterClassEx(ref wc2);

        _hwnd = Win32.CreateWindowEx(Win32.WS_EX_TOOLWINDOW_U | Win32.WS_EX_TOPMOST_U, "Shell_TrayWnd", "", Win32.WS_POPUP,
                                     0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new InvalidOperationException("Could not create tray host window: " + Marshal.GetLastWin32Error());
        _notifyHwnd = Win32.CreateWindowEx(0, "TrayNotifyWnd", "", Win32.WS_CHILD, 0, 0, 0, 0, _hwnd, IntPtr.Zero, hInst, IntPtr.Zero);

        FindRealTray();
        EnsureOnTop();
        BroadcastTaskbarCreated();
        _cleanup.Start();
    }

    /// <summary>Make sure FindWindow("Shell_TrayWnd") returns *us* (first in Z-order). Returns true if it had to act.</summary>
    public bool EnsureOnTop()
    {
        if (!IsRunning) return false;
        if (Win32.FindWindow("Shell_TrayWnd", null) == _hwnd) return false;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
        return true;
    }

    /// <summary>Tell every app the taskbar was (re)created so it re-registers its tray icon.</summary>
    public void BroadcastTaskbarCreated() =>
        Win32.SendNotifyMessage(Win32.HWND_BROADCAST, _taskbarCreatedMsg, IntPtr.Zero, IntPtr.Zero);

    public bool IsOurs(IntPtr hwnd) => hwnd == _hwnd || hwnd == _notifyHwnd;

    /// <summary>Re-evaluate bar vs. overflow for every icon after the promotion settings changed.</summary>
    public void ApplyPromotion()
    {
        foreach (var icon in Icons) icon.IsPromoted = App.Current.IsTrayPromoted(icon);
    }

    private void FindRealTray()
    {
        IntPtr found = IntPtr.Zero;
        Win32.EnumWindows((h, _) =>
        {
            if (h != _hwnd && Win32.GetWindowClass(h) == "Shell_TrayWnd") { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        _realTray = found;
    }

    // ------------------------------------------------------------------ window procedure
    private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32.WM_COPYDATA && hWnd == _hwnd)
        {
            try
            {
                var cds = Marshal.PtrToStructure<Win32.COPYDATASTRUCT>(lParam);
                switch ((long)cds.dwData)
                {
                    case 1: return HandleNotifyIcon(cds) ? (IntPtr)1 : IntPtr.Zero;
                    case 3: return HandleIconIdentifier(cds);
                    default: return ForwardToExplorer(msg, wParam, lParam); // 0 = AppBar messages, anything else too
                }
            }
            catch (Exception ex) { App.LogError(ex); return IntPtr.Zero; }
        }
        return Win32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr ForwardToExplorer(uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (_realTray == IntPtr.Zero || !Win32.IsWindow(_realTray)) FindRealTray();
        if (_realTray == IntPtr.Zero) return IntPtr.Zero;
        // Timed send (SMTO_ABORTIFHUNG): never let a wedged Explorer freeze our UI thread here.
        Win32.SendMessageTimeout(_realTray, msg, wParam, lParam, 0x0002 /*SMTO_ABORTIFHUNG*/, 2000, out var result);
        return result;
    }

    /// <summary>Asked by the UI layer: where is this icon on screen right now? (device pixels)</summary>
    public Func<TrayIcon, Win32.RECT?>? RectProvider { get; set; }

    private bool HandleNotifyIcon(Win32.COPYDATASTRUCT cds)
    {
        if (cds.cbData < 8 + 4) return false;

        // Old apps send NOTIFYICONDATA_V1/V2 (shorter structs). Never read past what they actually sent:
        // copy into a zeroed full-size buffer first, then marshal.
        int full = Marshal.SizeOf<SHELLTRAYDATA>();
        SHELLTRAYDATA data;
        if (cds.cbData >= full) data = Marshal.PtrToStructure<SHELLTRAYDATA>(cds.lpData);
        else
        {
            IntPtr tmp = Marshal.AllocHGlobal(full);
            try
            {
                for (int i = 0; i < full; i += 8) Marshal.WriteInt64(tmp, i, 0);
                var bytes = new byte[cds.cbData];
                Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
                Marshal.Copy(bytes, 0, tmp, cds.cbData);
                data = Marshal.PtrToStructure<SHELLTRAYDATA>(tmp);
            }
            finally { Marshal.FreeHGlobal(tmp); }
        }
        var nid = data.nid;
        var hwnd = (IntPtr)(int)nid.hWnd;
        bool hasGuid = (nid.uFlags & NIF_GUID) != 0 && nid.guidItem != Guid.Empty;

        TrayIcon? icon = hasGuid
            ? Icons.FirstOrDefault(i => i.Guid == nid.guidItem)
            : Icons.FirstOrDefault(i => i.HWnd == hwnd && i.Id == nid.uID);

        switch (data.dwMessage)
        {
            case NIM_DELETE:
                if (icon != null) Icons.Remove(icon);
                return true;

            case NIM_SETVERSION:
                if (icon == null) return false;
                icon.Version = nid.uVersion;
                return true;

            case NIM_SETFOCUS:
                return true;

            case NIM_ADD:
            case NIM_MODIFY:
                bool isNew = icon == null;
                icon ??= new TrayIcon { HWnd = hwnd, Id = nid.uID, Guid = hasGuid ? nid.guidItem : Guid.Empty };
                if (isNew)
                {
                    Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                    icon.ProcessId = pid;
                    icon.AppPath = Win32.GetProcessPath(hwnd);
                    icon.IsPromoted = App.Current.IsTrayPromoted(icon);
                }
                if (hasGuid) icon.Guid = nid.guidItem;
                if ((nid.uFlags & NIF_MESSAGE) != 0) icon.CallbackMessage = nid.uCallbackMessage;
                if ((nid.uFlags & NIF_TIP) != 0) icon.Tip = nid.szTip ?? "";
                if ((nid.uFlags & NIF_STATE) != 0 && (nid.dwStateMask & NIS_HIDDEN) != 0) icon.IsHidden = (nid.dwState & NIS_HIDDEN) != 0;
                if ((nid.uFlags & NIF_ICON) != 0 && nid.hIcon != 0) icon.Icon = CopyIconToImage((IntPtr)(int)nid.hIcon) ?? icon.Icon;
                if (isNew && Win32.IsWindow(hwnd)) Icons.Add(icon);
                return true;
        }
        return false;
    }

    private IntPtr HandleIconIdentifier(Win32.COPYDATASTRUCT cds)
    {
        // Shell_NotifyIconGetRect: message 1 wants (left, top), message 2 wants (right, bottom), packed as LOWORD/HIWORD.
        var id = Marshal.PtrToStructure<NOTIFYICONIDENTIFIER32>(cds.lpData);
        var icon = id.guidItem != Guid.Empty
            ? Icons.FirstOrDefault(i => i.Guid == id.guidItem)
            : Icons.FirstOrDefault(i => i.HWnd == (IntPtr)(int)id.hWnd && i.Id == id.uID);
        if (icon == null) return IntPtr.Zero;
        // The primary bar answers with the icon's real on-screen rectangle; if it can't (no bar yet), report zero.
        var r = RectProvider?.Invoke(icon) ?? default;
        return id.dwMessage switch
        {
            1 => (IntPtr)((r.Top << 16) | (r.Left & 0xFFFF)),
            2 => (IntPtr)((r.Bottom << 16) | (r.Right & 0xFFFF)),
            _ => IntPtr.Zero,
        };
    }

    private static BitmapSource? CopyIconToImage(IntPtr remoteIcon)
    {
        // The app may destroy its HICON right after Shell_NotifyIcon returns, so copy first.
        IntPtr copy = Win32.CopyIcon(remoteIcon);
        if (copy == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(copy, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally { Win32.DestroyIcon(copy); }
    }

    private void RemoveDeadIcons()
    {
        for (int i = Icons.Count - 1; i >= 0; i--)
            if (!Win32.IsWindow(Icons[i].HWnd)) Icons.RemoveAt(i);
    }

    // ------------------------------------------------------------------ input forwarding
    /// <summary>Send a mouse event to the icon's owner exactly the way Explorer does.</summary>
    public void SendMouse(TrayIcon icon, uint mouseMsg, int screenX, int screenY)
    {
        if (!Win32.IsWindow(icon.HWnd)) { Icons.Remove(icon); return; }

        // Menus and popups opened by the app must be able to take focus, and must close on click-away.
        // Explorer does this right before the *up* / context-menu notifications, not on down or move.
        if (mouseMsg is Win32.WM_RBUTTONUP or Win32.WM_LBUTTONUP or Win32.WM_CONTEXTMENU)
        {
            Win32.AllowSetForegroundWindow(icon.ProcessId);
            Win32.SetForegroundWindow(icon.HWnd);
        }

        if (icon.Version >= 4)
        {
            IntPtr wParam = (IntPtr)((screenY << 16) | (screenX & 0xFFFF));
            IntPtr lParam = (IntPtr)(((int)icon.Id << 16) | (int)(mouseMsg & 0xFFFF));
            Win32.PostMessage(icon.HWnd, icon.CallbackMessage, wParam, lParam);
        }
        else
        {
            Win32.PostMessage(icon.HWnd, icon.CallbackMessage, (IntPtr)(int)icon.Id, (IntPtr)(int)mouseMsg);
        }
    }

    public void Dispose()
    {
        _cleanup.Stop();
        if (_notifyHwnd != IntPtr.Zero) { Win32.DestroyWindow(_notifyHwnd); _notifyHwnd = IntPtr.Zero; }
        if (_hwnd != IntPtr.Zero)
        {
            Win32.DestroyWindow(_hwnd);
            _hwnd = IntPtr.Zero;
            Icons.Clear();
            BroadcastTaskbarCreated(); // hand the icons back to Explorer
        }
    }
}
