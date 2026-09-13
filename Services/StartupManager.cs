using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Xml.Linq;
using Microsoft.Win32;

namespace TaskbarTYOL.Services;

/// <summary>
/// Autostart. Preferred mechanism is a *logon scheduled task*: Windows launches those the moment the desktop
/// appears, whereas Run-key / Startup-folder apps are deliberately held back for several seconds
/// (the "startup apps delay"), which is exactly the window in which the user stares at the stock taskbar.
/// Falls back to the HKCU Run key if Task Scheduler is unavailable (e.g. locked-down machines).
/// </summary>
internal static class StartupManager
{
    private const string TaskName = "TaskbarTYOL";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TaskbarTYOL";

    private static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "TaskbarTYOL.exe");

    public static bool IsEnabled() => TaskExists() || RunKeyExists();

    /// <summary>True when the registered task points at the *current* executable (a re-install moved it otherwise).</summary>
    public static bool IsCurrent()
    {
        var xml = QueryTaskXml();
        if (xml != null) return xml.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string v && v.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (!TryCreateTask())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
                key.SetValue(ValueName, $"\"{ExePath}\"");
                return;
            }
            // Task registered → make sure the slower Run-key path is gone (two launchers would race).
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            k?.DeleteValue(ValueName, false);
        }
        else
        {
            RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            key?.DeleteValue(ValueName, false);
        }
    }

    // ------------------------------------------------------------------ task scheduler
    private static bool TryCreateTask()
    {
        try
        {
            string user = WindowsIdentity.GetCurrent().Name;
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var doc = new XDocument(
                new XElement(ns + "Task", new XAttribute("version", "1.4"),
                    new XElement(ns + "RegistrationInfo",
                        new XElement(ns + "Description", "Starts the TaskbarTYOL taskbar replacement immediately at sign-in.")),
                    new XElement(ns + "Triggers",
                        new XElement(ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "UserId", user))),
                    new XElement(ns + "Principals",
                        new XElement(ns + "Principal", new XAttribute("id", "Author"),
                            new XElement(ns + "UserId", user),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "LeastPrivilege"))),
                    new XElement(ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "false"),
                        new XElement(ns + "StartWhenAvailable", "true"),
                        new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "RunOnlyIfIdle", "false"),
                        new XElement(ns + "WakeToRun", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S"),   // never kill it
                        new XElement(ns + "RestartOnFailure",              // crash → Task Scheduler relaunches it
                            new XElement(ns + "Interval", "PT1M"),
                            new XElement(ns + "Count", "3")),
                        new XElement(ns + "Priority", "4")),               // above-normal-ish: it IS the shell
                    new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                        new XElement(ns + "Exec",
                            new XElement(ns + "Command", ExePath),
                            new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(ExePath) ?? "")))));

            string tmp = Path.Combine(Path.GetTempPath(), "TaskbarTYOL-task.xml");
            File.WriteAllText(tmp, doc.Declaration + Environment.NewLine + doc, System.Text.Encoding.Unicode);
            try { return RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{tmp}\" /F"); }
            finally { try { File.Delete(tmp); } catch { /* ignore */ } }
        }
        catch { return false; }
    }

    private static bool TaskExists() => RunSchtasks($"/Query /TN \"{TaskName}\"");

    private static string? QueryTaskXml()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\" /XML")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p == null) return null;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return p.ExitCode == 0 ? output : null;
        }
        catch { return null; }
    }

    private static bool RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.StandardOutput.ReadToEnd();
            p.WaitForExit(10000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private static bool RunKeyExists()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is string;
    }
}
