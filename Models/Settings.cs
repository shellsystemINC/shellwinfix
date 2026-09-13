using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarTYOL.Models;

public enum TaskbarEdge { Bottom, Top }
public enum TaskAlignment { Left, Center }

public sealed class Settings
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskbarTYOL");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public string Theme { get; set; } = "Fluent Dark";
    public TaskbarEdge Edge { get; set; } = TaskbarEdge.Bottom;
    public TaskAlignment Alignment { get; set; } = TaskAlignment.Center;
    public int Height { get; set; } = 48;
    public int IconSize { get; set; } = 24;
    public bool ShowLabels { get; set; } = false;
    public bool HideNativeTaskbar { get; set; } = true;
    public bool ReplaceWinKey { get; set; } = true;
    public bool ShowSeconds { get; set; } = false;
    public bool ShowDate { get; set; } = true;
    public bool ShowDesktopButton { get; set; } = true;
    public bool ShowTaskViewButton { get; set; } = true;
    public bool ShowSearchButton { get; set; } = true;
    public bool ShowSettingsButton { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool ShowOnAllMonitors { get; set; } = true;
    public bool ShowTrayIcons { get; set; } = true;
    /// <summary>Skip the overflow chevron and put every notification icon straight on the bar.</summary>
    public bool TrayShowAll { get; set; } = false;
    /// <summary>Notification icons (TrayIcon.PersistKey) the user dragged onto the bar; everything else sits in the overflow flyout.</summary>
    public List<string> TrayPromoted { get; set; } = new();
    public bool CombineWindows { get; set; } = true;
    public double Opacity { get; set; } = 1.0;
    public List<string> Pinned { get; set; } = new();

    /// <summary>Set after the user's Windows-taskbar pins were imported once (so we never re-add ones they removed).</summary>
    public bool ImportedExplorerPins { get; set; } = false;
    /// <summary>Run a small supervisor DLL inside explorer.exe (SetWindowsHookEx injection) so the bar lives with the shell.</summary>
    public bool InjectIntoExplorer { get; set; } = true;

    /// <summary>Explorer taskbar ABM state captured before we first touched it (restored on exit). null = not captured yet.</summary>
    public int? NativeTaskbarOriginalState { get; set; }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object _saveLock = new();

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options);
                if (s != null) return s;
            }
        }
        catch { /* corrupted file → defaults */ }

        var def = new Settings();
        def.Pinned.AddRange(DefaultPins());
        return def;
    }

    public void Save()
    {
        // Serialized + atomic: Save is called from the UI thread and from crash/logoff paths (EmergencyRestore runs
        // on the faulting thread). A bare WriteAllText interrupted mid-write, or two concurrent writers, would corrupt
        // settings.json — and Load then silently resets EVERYTHING to defaults. Write a temp file and move it into place.
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string json = JsonSerializer.Serialize(this, Options);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, json);
                File.Move(tmp, FilePath, overwrite: true);
            }
            catch { /* ignore — a failed save just keeps the previous good file */ }
        }
    }

    private static IEnumerable<string> DefaultPins()
    {
        string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string[] candidates =
        {
            Path.Combine(win, "explorer.exe"),
            Path.Combine(sys, "notepad.exe"),
            Path.Combine(win, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
        };
        foreach (var c in candidates) if (File.Exists(c)) yield return c;
    }
}
