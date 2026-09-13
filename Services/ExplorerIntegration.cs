using System.IO;
using Microsoft.Win32;

namespace TaskbarTYOL.Services;

/// <summary>
/// Explorer-shell integration helpers.
///
/// NOTE ON SHELL INJECTION: the classic supported way to run code inside explorer.exe was a Shell Service Object
/// listed under HKLM\...\ShellServiceObjectDelayLoad. On current Windows 11 the shell no longer loads third-party
/// (or even its own default WebCheck) SSO entries, so that mechanism is dead — verified on this machine. The only
/// remaining way to literally live inside explorer.exe is to inject a native DLL via undocumented hooks, which is
/// brittle, trips anti-virus, and crashes the shell on Windows updates. TaskbarTYOL deliberately does NOT do that.
/// Instead it achieves the same user-visible behaviour safely: a logon task starts it with the shell, a Task
/// Scheduler restart-on-failure brings it back if it dies, and its own watchdog survives and re-asserts across
/// Explorer restarts. This class now only imports the user's existing taskbar pins and cleans up any dead SSO
/// registry entries left by earlier builds.
/// </summary>
internal static class ExplorerIntegration
{
    private const string Clsid = "{7F1C2B3A-5D4E-4F60-9A8B-2C3D4E5F6071}";
    private const string SsodlKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ShellServiceObjectDelayLoad";
    private const string SsodlValue = "TaskbarTYOL";

    /// <summary>Remove COM/SSODL registrations written by earlier builds. HKCU always; HKLM only if we can.</summary>
    public static void CleanupDeadRegistration()
    {
        try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\CLSID\{Clsid}", false); } catch { /* ignore */ }
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(SsodlKey, true);
            if (key?.GetValue(SsodlValue) != null) key.DeleteValue(SsodlValue, false);
        }
        catch { /* needs admin; a stale entry just points at a DLL Explorer never loads — harmless */ }
    }

    /// <summary>Import the shortcuts the user had pinned to the Windows taskbar (Explorer keeps them as .lnk files).</summary>
    public static IEnumerable<string> ExplorerPinnedShortcuts()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                               "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");
        if (!Directory.Exists(dir)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir, "*.lnk").OrderBy(File.GetCreationTimeUtc).ToList(); }
        catch { yield break; }
        foreach (var f in files)
        {
            var name = Path.GetFileNameWithoutExtension(f);
            if (name.Equals("File Explorer", StringComparison.OrdinalIgnoreCase)) continue; // we pin explorer.exe ourselves
            yield return f;
        }
    }
}
