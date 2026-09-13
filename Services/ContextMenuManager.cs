using System.Diagnostics;
using Microsoft.Win32;
using TaskbarTYOL.Models;

namespace TaskbarTYOL.Services;

/// <summary>
/// Customises the Windows right-click (context) menu, per-user and reversibly:
///   • adds/removes user-defined verbs under HKCU\Software\Classes\...\shell (desktop, folders, files, drives);
///   • toggles the Windows 11 classic (full) context menu via the documented empty-CLSID trick.
/// Everything is HKCU only — no admin, and uninstall/disable removes it cleanly. Menu changes to already-open
/// Explorer windows, and the classic-menu toggle, need Explorer to re-read the registry → RestartExplorer().
/// </summary>
internal static class ContextMenuManager
{
    private const string KeyPrefix = "TYOL.";   // our entries are named TYOL.<id> so we can find/clean only ours
    // The shell key that, when present with an empty default, makes Windows 11 fall back to the classic menu.
    private const string Win11MenuClsid = @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";

    private static string BasePath(ContextTarget t) => t switch
    {
        ContextTarget.DesktopBackground => @"Software\Classes\Directory\Background\shell",
        ContextTarget.Folder => @"Software\Classes\Directory\shell",
        ContextTarget.File => @"Software\Classes\*\shell",
        ContextTarget.Drive => @"Software\Classes\Drive\shell",
        _ => throw new ArgumentOutOfRangeException(nameof(t)),
    };

    private static readonly ContextTarget[] AllTargets =
        { ContextTarget.DesktopBackground, ContextTarget.Folder, ContextTarget.File, ContextTarget.Drive };

    /// <summary>Reconcile the registry with the settings list: write enabled entries, remove everything else of ours.</summary>
    public static void Sync(Settings settings)
    {
        try
        {
            foreach (var target in AllTargets)
            {
                var desired = settings.ContextEntries
                    .Where(e => e.Enabled && e.Targets.Contains(target) && !string.IsNullOrWhiteSpace(e.Label) && !string.IsNullOrWhiteSpace(e.Command))
                    .ToDictionary(e => KeyPrefix + e.Id, e => e);

                // Remove our stale keys for this target.
                using (var shell = Registry.CurrentUser.OpenSubKey(BasePath(target), writable: true))
                {
                    if (shell != null)
                        foreach (var name in shell.GetSubKeyNames().Where(n => n.StartsWith(KeyPrefix, StringComparison.Ordinal)))
                            if (!desired.ContainsKey(name))
                                try { shell.DeleteSubKeyTree(name, throwOnMissingSubKey: false); } catch { /* ignore */ }
                }

                // Write the desired ones.
                foreach (var (keyName, entry) in desired) WriteEntry(target, keyName, entry);
            }
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    private static void WriteEntry(ContextTarget target, string keyName, ContextMenuEntry e)
    {
        using var k = Registry.CurrentUser.CreateSubKey($@"{BasePath(target)}\{keyName}");
        if (k == null) return;
        k.SetValue("MUIVerb", e.Label);
        k.SetValue(null, e.Label);                                   // default value = fallback label
        if (!string.IsNullOrWhiteSpace(e.IconPath)) k.SetValue("Icon", e.IconPath!); else k.DeleteValue("Icon", false);
        k.SetValue("Position", e.Top ? "Top" : "Bottom");
        if (e.Extended) k.SetValue("Extended", ""); else k.DeleteValue("Extended", false);
        using var cmd = k.CreateSubKey("command");
        cmd?.SetValue(null, e.Command);
    }

    /// <summary>Remove every entry we ever created (used on uninstall / "remove all").</summary>
    public static void RemoveAll()
    {
        foreach (var target in AllTargets)
        {
            try
            {
                using var shell = Registry.CurrentUser.OpenSubKey(BasePath(target), writable: true);
                if (shell == null) continue;
                foreach (var name in shell.GetSubKeyNames().Where(n => n.StartsWith(KeyPrefix, StringComparison.Ordinal)))
                    try { shell.DeleteSubKeyTree(name, false); } catch { /* ignore */ }
            }
            catch { /* ignore */ }
        }
    }

    // ------------------------------------------------------------------ Windows 11 classic menu
    public static bool IsClassicMenuEnabled()
    {
        using var k = Registry.CurrentUser.OpenSubKey(Win11MenuClsid + @"\InprocServer32", false);
        return k != null && (k.GetValue(null) as string) == "";
    }

    public static void SetClassicMenu(bool on)
    {
        try
        {
            if (on)
            {
                using var k = Registry.CurrentUser.CreateSubKey(Win11MenuClsid + @"\InprocServer32");
                k?.SetValue(null, "");   // empty default value is the whole trick
            }
            else
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(Win11MenuClsid, throwOnMissingSubKey: false); } catch { /* ignore */ }
            }
        }
        catch (Exception ex) { App.LogError(ex); }
    }

    // ------------------------------------------------------------------ apply changes to a live shell
    /// <summary>
    /// Explorer caches context-menu registrations and the compact/classic choice, so changes only show after it
    /// re-reads them. Killing explorer.exe makes Windows relaunch the shell (our watchdog re-hides its taskbar and
    /// re-injects). This also closes any open File Explorer windows — the caller should warn the user.
    /// </summary>
    public static void RestartExplorer()
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
                try { p.Kill(); } catch { /* ignore */ }
            // Windows normally auto-restarts the shell; relaunch as a fallback if it did not within a moment.
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                if (Process.GetProcessesByName("explorer").Length == 0)
                    try { Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true }); } catch { /* ignore */ }
            });
        }
        catch (Exception ex) { App.LogError(ex); }
    }
}
