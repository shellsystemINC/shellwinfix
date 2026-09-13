using System.Windows;

namespace TaskbarTYOL.Services;

public sealed record ThemeInfo(string Name, string File, string Description);

/// <summary>Swaps the merged theme ResourceDictionary at runtime. Every theme defines the same set of keys.</summary>
internal static class ThemeManager
{
    public static readonly IReadOnlyList<ThemeInfo> Themes = new List<ThemeInfo>
    {
        new("Fluent Dark",     "FluentDark",     "Windows 11 style, dark mica, centred icons"),
        new("Fluent Light",    "FluentLight",    "Windows 11 style, light mica"),
        new("Aero",            "Aero",           "Windows 7 glass: translucent blue, round orb"),
        new("Luna",            "Luna",           "Windows XP: blue bar, green Start button"),
        new("Classic",         "Classic",        "Windows 95/98/2000 battleship grey"),
        new("Metro Dark",      "MetroDark",      "Windows 10: flat black, sharp corners"),
        new("Dracula",         "Dracula",        "Purple-tinted dark palette"),
        new("Nord",            "Nord",           "Arctic, bluish frost palette"),
        new("Cyberpunk",       "Cyberpunk",      "Neon magenta & cyan on near-black"),
        new("Catppuccin",      "Catppuccin",     "Soft pastel Mocha palette"),
        new("Solarized",       "Solarized",      "Solarized Dark, warm accents"),
        new("Amber Terminal",  "AmberTerminal",  "Retro monochrome amber CRT"),
        new("Rose Pine",       "RosePine",       "Muted rose & pine, soho vibes"),
        new("Gruvbox",         "Gruvbox",        "Retro groove, warm earthy tones"),
    };

    private static ResourceDictionary? _current;

    public static string CurrentName { get; private set; } = "";

    public static void Apply(string name)
    {
        var theme = Themes.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];
        var uri = new Uri($"pack://application:,,,/Themes/{theme.File}.xaml", UriKind.Absolute);
        var dict = new ResourceDictionary { Source = uri };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current != null) merged.Remove(_current);
        merged.Insert(0, dict);
        _current = dict;
        CurrentName = theme.Name;
        ThemeChanged?.Invoke(theme.Name);
    }

    public static event Action<string>? ThemeChanged;

    /// <summary>Loads a theme dictionary without applying it (used for preview swatches).</summary>
    public static ResourceDictionary Peek(ThemeInfo theme) =>
        new() { Source = new Uri($"pack://application:,,,/Themes/{theme.File}.xaml", UriKind.Absolute) };
}
