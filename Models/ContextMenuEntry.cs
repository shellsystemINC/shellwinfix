using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskbarTYOL.Models;

/// <summary>Which shell context menus an entry appears in.</summary>
public enum ContextTarget
{
    DesktopBackground,  // right-click empty desktop / folder background  (Directory\Background)
    Folder,             // right-click a folder                          (Directory)
    File,               // right-click any file                          (*)
    Drive,              // right-click a drive                           (Drive)
}

/// <summary>
/// One user-defined entry in the Windows right-click menu. Written to HKCU\Software\Classes (per-user, no admin),
/// which is exactly how "Open in Terminal / Open with X here" entries are added. We keep the definition here so the
/// UI can list/edit/remove them; the registry is generated from this by ContextMenuManager.
/// </summary>
public sealed class ContextMenuEntry : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _label = "";

    /// <summary>Stable id → registry key name "TYOL.&lt;Id&gt;". Never reuse across different entries.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    public string Label { get => _label; set => Set(ref _label, value); }

    /// <summary>
    /// Command line. Placeholders: %V = the clicked folder / background folder, %1 = the clicked file/folder path.
    /// Example: "\"C:\\Program Files\\Zed\\zed.exe\" \"%V\""
    /// </summary>
    public string Command { get; set; } = "";

    /// <summary>Optional icon: an .exe/.dll/.ico path, optionally with ",index".</summary>
    public string? IconPath { get; set; }

    /// <summary>Show only when Shift is held (a Windows "extended" verb).</summary>
    public bool Extended { get; set; }

    /// <summary>Pin to the top of the menu (else it lands wherever Windows puts it, effectively the bottom group).</summary>
    public bool Top { get; set; }

    public List<ContextTarget> Targets { get; set; } = new() { ContextTarget.DesktopBackground };

    public string TargetsText => Targets.Count == 0 ? "(nowhere)" : string.Join(", ", Targets.Select(Nice));
    private static string Nice(ContextTarget t) => t switch
    {
        ContextTarget.DesktopBackground => "Desktop",
        ContextTarget.Folder => "Folders",
        ContextTarget.File => "Files",
        ContextTarget.Drive => "Drives",
        _ => t.ToString(),
    };

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? n = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public ContextMenuEntry Clone() => new()
    {
        Id = Id, Enabled = Enabled, Label = Label, Command = Command, IconPath = IconPath,
        Extended = Extended, Top = Top, Targets = new List<ContextTarget>(Targets),
    };
}
