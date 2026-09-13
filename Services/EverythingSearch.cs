using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Win32;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Services;

public sealed record EverythingResult(string Name, string Folder, bool IsFolder)
{
    public string FullPath => System.IO.Path.Combine(Folder, Name);
}

public sealed record EverythingResults(IReadOnlyList<EverythingResult> Items, int TotalItems);

/// <summary>
/// Talks to voidtools Everything (1.4 / 1.5) over its documented WM_COPYDATA IPC — no SDK dll, no es.exe needed.
/// We send an EVERYTHING_IPC_QUERYW to Everything's window; it replies with an EVERYTHING_IPC_LISTW to our
/// message-only window. Must be used from the UI thread (it owns the reply window).
/// </summary>
internal static class EverythingSearch
{
    private const uint EVERYTHING_IPC_COPYDATAQUERYW = 2;
    private const uint EVERYTHING_IPC_FOLDER = 1;
    private const uint EVERYTHING_IPC_MATCHPATH = 4;   // words match anywhere in the full path ("taskbartyol readme" → ...\TaskbarTYOL\README.md)
    private static readonly string[] WindowClasses = { "EVERYTHING_TASKBAR_NOTIFICATION", "EVERYTHING_TASKBAR_NOTIFICATION_(1.5a)" };

    private static HwndSource? _replyWindow;
    private static uint _nextReplyId = 0x4000;
    private static readonly Dictionary<uint, TaskCompletionSource<EverythingResults>> _pending = new();

    public static IntPtr FindEverything()
    {
        foreach (var cls in WindowClasses)
        {
            var h = Win32.FindWindow(cls, null);
            if (h != IntPtr.Zero) return h;
        }
        return IntPtr.Zero;
    }

    public static bool IsRunning => FindEverything() != IntPtr.Zero;

    public static string? ExePath()
    {
        try
        {
            foreach (var key in new[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Everything",
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Everything",
                @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Everything",
            })
            {
                if (Registry.GetValue(key, "InstallLocation", null) is string loc)
                {
                    var exe = Path.Combine(loc, "Everything.exe");
                    if (File.Exists(exe)) return exe;
                }
            }
        }
        catch { /* ignore */ }

        foreach (var candidate in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Everything", "Everything.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Everything", "Everything.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Everything", "Everything.exe"),
        })
            if (File.Exists(candidate)) return candidate;

        return Process.GetProcessesByName("Everything").Select(p => { try { return p.MainModule?.FileName; } catch { return null; } })
                      .FirstOrDefault(p => p != null);
    }

    public static bool IsInstalled => ExePath() != null;

    /// <summary>Start Everything in the background (minimised to tray) if it is installed but not running.</summary>
    public static bool TryStart()
    {
        if (IsRunning) return true;
        var exe = ExePath();
        if (exe == null) return false;
        try { Process.Start(new ProcessStartInfo(exe, "-startup") { UseShellExecute = true }); return true; }
        catch { return false; }
    }

    public static void OpenInEverything(string query)
    {
        var exe = ExePath();
        if (exe == null) return;
        try { Process.Start(new ProcessStartInfo(exe, $"-search \"{query.Replace("\"", "\"\"")}\"") { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    /// <summary>Run a search. Returns null if Everything is not running or did not answer in time.</summary>
    public static async Task<EverythingResults?> QueryAsync(string search, int maxResults, CancellationToken ct = default)
    {
        var target = FindEverything();
        if (target == IntPtr.Zero) return null;

        EnsureReplyWindow();
        uint replyId = _nextReplyId++;
        var tcs = new TaskCompletionSource<EverythingResults>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[replyId] = tcs;

        // EVERYTHING_IPC_QUERYW: DWORD reply_hwnd, reply_copydata_message, search_flags, offset, max_results; WCHAR search_string[]
        var text = System.Text.Encoding.Unicode.GetBytes(search + "\0");
        int size = 5 * 4 + text.Length;
        IntPtr buf = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.WriteInt32(buf, 0, (int)(long)_replyWindow!.Handle);
            Marshal.WriteInt32(buf, 4, (int)replyId);
            Marshal.WriteInt32(buf, 8, (int)EVERYTHING_IPC_MATCHPATH); // launcher-style: match against the whole path
            Marshal.WriteInt32(buf, 12, 0);         // offset
            Marshal.WriteInt32(buf, 16, maxResults);
            Marshal.Copy(text, 0, buf + 20, text.Length);

            var cds = new Win32.COPYDATASTRUCT { dwData = (IntPtr)EVERYTHING_IPC_COPYDATAQUERYW, cbData = size, lpData = buf };
            IntPtr cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf<Win32.COPYDATASTRUCT>());
            try
            {
                Marshal.StructureToPtr(cds, cdsPtr, false);
                var ok = Win32.SendMessageTimeout(target, Win32.WM_COPYDATA, _replyWindow.Handle, cdsPtr, 0x0002, 2000, out var result);
                if (ok == IntPtr.Zero || result == IntPtr.Zero) { _pending.Remove(replyId); return null; }
            }
            finally { Marshal.FreeHGlobal(cdsPtr); }
        }
        finally { Marshal.FreeHGlobal(buf); }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(3000);
        using (timeout.Token.Register(() => tcs.TrySetCanceled()))
        {
            try { return await tcs.Task.ConfigureAwait(true); }
            catch (OperationCanceledException) { return null; }
            finally { _pending.Remove(replyId); }
        }
    }

    private static void EnsureReplyWindow()
    {
        if (_replyWindow != null && _replyWindow.Handle != IntPtr.Zero) return;
        _replyWindow = new HwndSource(new HwndSourceParameters("TaskbarTYOL.EverythingReply")
        {
            ParentWindow = Win32.HWND_MESSAGE, WindowStyle = 0, Width = 0, Height = 0,
        });
        _replyWindow.AddHook(ReplyHook);
    }

    private static IntPtr ReplyHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != (int)Win32.WM_COPYDATA) return IntPtr.Zero;
        var cds = Marshal.PtrToStructure<Win32.COPYDATASTRUCT>(lParam);
        if (!_pending.TryGetValue((uint)(long)cds.dwData, out var tcs)) return IntPtr.Zero;

        try
        {
            // EVERYTHING_IPC_LISTW: DWORD totfolders, totfiles, totitems, numfolders, numfiles, numitems, offset; items[]
            IntPtr p = cds.lpData;
            int totItems = Marshal.ReadInt32(p, 8);
            int numItems = Marshal.ReadInt32(p, 20);
            var items = new List<EverythingResult>(numItems);
            IntPtr item = p + 28;
            for (int i = 0; i < numItems; i++, item += 12)
            {
                uint flags = (uint)Marshal.ReadInt32(item, 0);
                int nameOff = Marshal.ReadInt32(item, 4);
                int pathOff = Marshal.ReadInt32(item, 8);
                string name = Marshal.PtrToStringUni(p + nameOff) ?? "";
                string folder = Marshal.PtrToStringUni(p + pathOff) ?? "";
                items.Add(new EverythingResult(name, folder, (flags & EVERYTHING_IPC_FOLDER) != 0));
            }
            tcs.TrySetResult(new EverythingResults(items, totItems));
        }
        catch (Exception ex) { tcs.TrySetException(ex); }

        handled = true;
        return (IntPtr)1;
    }
}
