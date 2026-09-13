using System.IO;
using System.Runtime.InteropServices;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

/// <summary>
/// Injects TaskbarTYOLHook.dll into explorer.exe via SetWindowsHookEx(WH_GETMESSAGE, ..., shellThreadId).
///
/// This is the supported, non-persistent way to run inside the shell:
///   • no admin, no file placed in a system directory, nothing survives a reboot;
///   • unhooking removes our code from explorer.exe cleanly;
///   • if the hook DLL ever misbehaves it can only affect the current Explorer session, and a reboot is a full reset.
/// It is intentionally NOT the dxgi.dll-next-to-explorer trick StartAllBack/ExplorerPatcher use, which persists and
/// can brick logon after a Windows update.
///
/// Once mapped in, the DLL runs a tiny supervisor that keeps TaskbarTYOL.exe alive with the shell. We keep the hook
/// installed for the lifetime of our process (and re-install it if Explorer restarts).
/// </summary>
internal sealed class ExplorerInjector : IDisposable
{
    private const int WH_GETMESSAGE = 3;

    private IntPtr _dll;
    private IntPtr _hook;
    private IntPtr _procAddr;
    private IntPtr _statusAddr;

    public string DllPath => Path.Combine(AppContext.BaseDirectory, "TaskbarTYOLHook.dll");
    public bool DllPresent => File.Exists(DllPath);
    public bool IsInstalled => _hook != IntPtr.Zero;

    /// <summary>Load the DLL into our own process and resolve the exported entry points.</summary>
    private bool EnsureLoaded()
    {
        if (_dll != IntPtr.Zero) return true;
        if (!DllPresent) return false;
        _dll = Win32.LoadLibrary(DllPath);
        if (_dll == IntPtr.Zero) return false;
        _procAddr = Win32.GetProcAddress(_dll, "TytolHookProc");
        _statusAddr = Win32.GetProcAddress(_dll, "TytolGetStatus");
        return _procAddr != IntPtr.Zero;
    }

    /// <summary>
    /// The explorer.exe shell thread. We use GetShellWindow() (the Progman desktop window) rather than
    /// FindWindow("Shell_TrayWnd"), because our own tray-hijack window ALSO has class Shell_TrayWnd — hooking that
    /// would inject into ourselves. Progman is unambiguously owned by explorer's shell thread.
    /// </summary>
    private static uint ShellThreadId(out uint pid)
    {
        pid = 0;
        var shell = Win32.GetShellWindow();
        if (shell == IntPtr.Zero) return 0;
        uint tid = Win32.GetWindowThreadProcessId(shell, out pid);
        // Sanity: never hook our own process.
        return pid == (uint)Environment.ProcessId ? 0 : tid;
    }

    /// <summary>Install the hook. Returns true if the hook handle was created.</summary>
    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        if (!EnsureLoaded()) return false;

        uint tid = ShellThreadId(out uint pid);
        if (tid == 0) return false;

        _hook = Win32.SetWindowsHookExPtr(WH_GETMESSAGE, _procAddr, _dll, tid);
        if (_hook == IntPtr.Zero) return false;

        // Nudge the shell thread so the hook fires promptly (maps the DLL in without waiting for user input).
        Win32.PostThreadMessage(tid, 0x0000 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);
        return true;
    }

    public void Uninstall()
    {
        if (_hook != IntPtr.Zero) { Win32.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
    }

    /// <summary>Ask the injected DLL (in whichever process) for its status bit-flags: 1=in shell, 2=supervising, 4=app alive.</summary>
    public uint QueryStatus()
    {
        if (_statusAddr == IntPtr.Zero) return 0;
        // Calls our own in-process copy; the "in shell" bit reflects THIS process, so callers use it only as a load check.
        var fn = Marshal.GetDelegateForFunctionPointer<StatusProc>(_statusAddr);
        try { return fn(); } catch { return 0; }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint StatusProc();

    public void Dispose() => Uninstall();
}
