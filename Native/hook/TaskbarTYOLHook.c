/*
 * TaskbarTYOLHook.dll - the part of TaskbarTYOL that runs INSIDE explorer.exe.
 *
 * How it gets there: TaskbarTYOL.exe LoadLibrary()s this DLL, then calls
 *   SetWindowsHookEx(WH_GETMESSAGE, TytolHookProc, hThisDll, <explorer's shell thread id>)
 * Windows maps this DLL into explorer.exe the next time that thread pumps a message. That is a
 * documented, supported API - no DLL search-order hijacking, no file dropped into C:\Windows, no
 * admin, and NOT PERSISTENT: the hook is owned by TaskbarTYOL.exe, so when our app exits (or the
 * machine reboots) Windows unmaps this DLL from explorer automatically. That is deliberately safer
 * than StartAllBack/ExplorerPatcher, which replace dxgi.dll next to explorer.exe and can leave you
 * in a login loop after a Windows update. Because the hook's lifetime is tied to our app, this
 * module does not try to supervise/relaunch the app (the logon task's restart-on-failure does that);
 * it does an in-shell job that genuinely belongs on the shell thread.
 *
 * What it does once inside: keeps explorer's OWN taskbar windows hidden. Our app hides them from the
 * outside too, but doing it here - on the very thread that owns those windows, on every message pump
 * tick - suppresses the flicker where the real bar peeks out for a frame when the shell recreates it.
 * It only ever touches Shell_TrayWnd / Shell_SecondaryTrayWnd owned by THIS (explorer) process, so it
 * can never hit TaskbarTYOL's own look-alike tray-host window.
 *
 * SAFETY: no worker thread is created; all work runs inline in the hook callback (which explorer
 * already calls), throttled. So when the DLL is unloaded there is never any of our code still running
 * inside explorer. Only Win32 APIs are used (no CRT) so the binary stays tiny.
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

#define CHECK_INTERVAL_MS  250

static HMODULE       g_self      = NULL;
static volatile LONG g_everRan   = 0;
static DWORD         g_lastCheck  = 0;
static BOOL          g_inShell    = FALSE;
static BOOL          g_shellKnown = FALSE;

/* Are we executing inside the process that owns the shell (explorer.exe)? Cached after first success. */
static BOOL IsShellProcess(void)
{
    if (g_shellKnown) return g_inShell;
    HWND shell = GetShellWindow();
    if (shell) {
        DWORD pid = 0;
        GetWindowThreadProcessId(shell, &pid);
        g_inShell = (pid == GetCurrentProcessId());
        g_shellKnown = TRUE;
    }
    return g_inShell;
}

/* Hide explorer's own taskbar windows (this process only). */
static void HideOwnTaskbars(void)
{
    DWORD me = GetCurrentProcessId();
    HWND h = NULL;
    /* primary */
    h = FindWindowW(L"Shell_TrayWnd", NULL);
    while (h) {
        DWORD pid = 0;
        GetWindowThreadProcessId(h, &pid);
        if (pid == me && IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
        h = FindWindowExW(NULL, h, L"Shell_TrayWnd", NULL);
    }
    /* secondary monitors */
    h = FindWindowExW(NULL, NULL, L"Shell_SecondaryTrayWnd", NULL);
    while (h) {
        DWORD pid = 0;
        GetWindowThreadProcessId(h, &pid);
        if (pid == me && IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
        h = FindWindowExW(NULL, h, L"Shell_SecondaryTrayWnd", NULL);
    }
}

/* ------------------------------------------------------------------ exports */

__declspec(dllexport) LRESULT CALLBACK TytolHookProc(int code, WPARAM wParam, LPARAM lParam)
{
    InterlockedExchange(&g_everRan, 1);

    if (code >= 0 && IsShellProcess()) {
        DWORD now = GetTickCount();
        if (g_lastCheck == 0 || (now - g_lastCheck) >= CHECK_INTERVAL_MS) {
            g_lastCheck = now;
            HideOwnTaskbars();
        }
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}

/* Bit flags: 1 = running inside the shell process, 2 = hook has fired at least once. */
__declspec(dllexport) DWORD TytolGetStatus(void)
{
    DWORD flags = 0;
    if (IsShellProcess()) flags |= 1;
    if (g_everRan)        flags |= 2;
    return flags;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = inst;
        DisableThreadLibraryCalls(inst);
    }
    return TRUE;
}
