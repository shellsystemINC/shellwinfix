using System.Runtime.InteropServices;

namespace TaskbarTYOL.Native;

/// <summary>
/// Low-level mouse hook that catches a right-click on the EMPTY desktop background and lets us show our own
/// themed menu there instead of the Windows one. It eats that right-click (so the native menu never appears) and
/// raises <see cref="Triggered"/>. Right-clicks on desktop icons, and everywhere else, pass straight through.
/// </summary>
internal sealed class DesktopRightClickHook : IDisposable
{
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;

    private IntPtr _hook;
    private Win32.LowLevelKeyboardProc? _proc;   // same delegate shape works for a mouse LL hook; kept alive from GC

    public event Action? Triggered;
    public bool Enabled { get; set; } = true;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _proc = HookProc;
        _hook = Win32.SetWindowsHookEx(Win32.WH_MOUSE_LL, _proc, Win32.GetModuleHandle(null), 0);
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        int msg = (int)wParam;
        if (code >= 0 && Enabled && msg is WM_RBUTTONDOWN or WM_RBUTTONUP)
        {
            try
            {
                var s = Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(lParam);
                if (IsEmptyDesktop(s.pt))
                {
                    // Eat BOTH down and up so the desktop never sees the click (no native menu); show ours on the up.
                    if (msg == WM_RBUTTONUP) Triggered?.Invoke();
                    return (IntPtr)1;
                }
            }
            catch (Exception ex) { App.LogError(ex); }   // never let a hook fault propagate
        }
        return Win32.CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>
    /// True only for a right-click on empty desktop space. Everything here is fast (no UI Automation — that was slow
    /// enough to blow the low-level-hook timeout, so Windows skipped us and the native menu showed). Icon vs. empty is
    /// decided with a cross-process LVM_HITTEST against the desktop listview.
    /// </summary>
    private static bool IsEmptyDesktop(Win32.POINT pt)
    {
        var h = Win32.WindowFromPoint(pt);
        if (h == IntPtr.Zero) return false;
        var cls = Win32.GetWindowClass(h);

        if (cls is "WorkerW" or "Progman") return true;

        if (cls == "SysListView32" && Win32.GetWindowClass(Win32.GetParent(h)) == "SHELLDLL_DefView")
            return HitTestItem(h, pt) < 0;   // -1 = empty space → our menu; >=0 = on an icon → leave native alone

        return false;
    }

    /// <summary>LVM_HITTEST in the desktop listview's process; returns the item index under the point, or -1.</summary>
    private static int HitTestItem(IntPtr listView, Win32.POINT screenPt)
    {
        var client = screenPt;
        if (!Win32.ScreenToClient(listView, ref client)) return -1;

        Win32.GetWindowThreadProcessId(listView, out uint pid);
        if (pid == 0) return -1;
        IntPtr hProc = Win32.OpenProcess(Win32.PROCESS_VM_OPERATION | Win32.PROCESS_VM_READ | Win32.PROCESS_VM_WRITE, false, pid);
        if (hProc == IntPtr.Zero) return -1;

        IntPtr remote = IntPtr.Zero;
        try
        {
            // LVHITTESTINFO: POINT pt (8) + flags (4) + iItem (4) + iSubItem (4) + iGroup (4) = 24 bytes.
            var buf = new byte[24];
            BitConverter.GetBytes(client.X).CopyTo(buf, 0);
            BitConverter.GetBytes(client.Y).CopyTo(buf, 4);
            remote = Win32.VirtualAllocEx(hProc, IntPtr.Zero, (IntPtr)buf.Length, Win32.MEM_COMMIT | Win32.MEM_RESERVE, Win32.PAGE_READWRITE);
            if (remote == IntPtr.Zero) return -1;
            if (!Win32.WriteProcessMemory(hProc, remote, buf, (IntPtr)buf.Length, out _)) return -1;

            // LVM_HITTEST returns the hit item index (or -1). Use a timeout so a busy shell can't stall the hook.
            if (Win32.SendMessageTimeout(listView, Win32.LVM_HITTEST, IntPtr.Zero, remote, 0x0002, 200, out var res) == IntPtr.Zero)
                return 0;   // timed out → assume "on something" so we do NOT eat (native menu stays correct)
            return (int)res;
        }
        finally
        {
            if (remote != IntPtr.Zero) Win32.VirtualFreeEx(hProc, remote, IntPtr.Zero, Win32.MEM_RELEASE);
            Win32.CloseHandle(hProc);
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }
}
