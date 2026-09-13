using System.ComponentModel;
using System.IO;
using System.Windows.Media;

namespace TaskbarTYOL.Services;

public sealed class AppEntry : INotifyPropertyChanged
{
    private ImageSource? _icon;

    public string Name { get; init; } = "";
    public string Path { get; init; } = "";          // .lnk or .exe
    public string Category { get; init; } = "";       // folder name under Programs, if any
    public ImageSource? Icon
    {
        get => _icon;
        set { if (!ReferenceEquals(_icon, value)) { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); } }
    }
    public char Letter => char.IsLetter(Name.FirstOrDefault()) ? char.ToUpperInvariant(Name[0]) : '#';

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Scans the Start Menu folders for shortcuts and produces a sorted, de-duplicated app list.</summary>
internal static class AppIndexer
{
    private static readonly string[] IgnoreNames = { "uninstall", "readme", "license", "website", "release notes" };

    public static List<AppEntry> Scan()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };

        var map = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories); }
            catch { continue; }

            foreach (var f in files)
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".lnk" or ".url" or ".appref-ms")) continue;
                var name = Path.GetFileNameWithoutExtension(f);
                if (IgnoreNames.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;

                var rel = Path.GetRelativePath(root, Path.GetDirectoryName(f)!);
                var category = rel == "." ? "" : rel.Split(Path.DirectorySeparatorChar)[0];

                if (!map.ContainsKey(name))
                    map[name] = new AppEntry { Name = name, Path = f, Category = category };
            }
        }

        return map.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Loads an icon (safe on a background thread — the returned bitmap is frozen).</summary>
    public static ImageSource? LoadIcon(AppEntry e) => IconHelper.GetFileIcon(e.Path, large: true);

    /// <summary>Result of a background pre-scan, consumed by the start menu on first open.</summary>
    public static List<AppEntry>? Cached { get; private set; }

    public static void PrewarmAsync()
    {
        if (Cached != null) return;
        Task.Run(() =>
        {
            try
            {
                var list = Scan();
                foreach (var a in list) a.Icon = LoadIcon(a);   // frozen bitmaps, safe off-thread
                Cached = list;
            }
            catch { /* the start menu will simply scan on demand */ }
        });
    }

    /// <summary>Take the pre-scanned list (once), or scan now.</summary>
    public static List<AppEntry> TakeCachedOrScan()
    {
        var c = Cached;
        Cached = null;
        return c ?? Scan();
    }
}
