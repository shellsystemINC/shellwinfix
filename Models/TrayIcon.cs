using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TaskbarTYOL.Native;

namespace TaskbarTYOL.Models;

/// <summary>One notification-area icon registered by an application via Shell_NotifyIcon.</summary>
public sealed class TrayIcon : INotifyPropertyChanged
{
    private ImageSource? _icon;
    private string _tip = "";
    private bool _isHidden;
    private bool _isPromoted;

    public IntPtr HWnd { get; set; }
    public uint Id { get; set; }
    public Guid Guid { get; set; }
    public uint CallbackMessage { get; set; }
    public uint Version { get; set; }              // 0/3 = legacy messages, 4 = NOTIFYICON_VERSION_4
    public uint ProcessId { get; set; }
    public string? AppPath { get; set; }           // owning executable, used as the stable identity across sessions

    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public string Tip { get => _tip; set { if (Set(ref _tip, value)) OnChanged(nameof(DisplayName)); } }
    /// <summary>NIS_HIDDEN — the app itself asked for the icon not to be shown.</summary>
    public bool IsHidden { get => _isHidden; set => Set(ref _isHidden, value); }
    /// <summary>True = shown on the bar, false = lives in the overflow flyout behind the chevron.</summary>
    public bool IsPromoted { get => _isPromoted; set => Set(ref _isPromoted, value); }

    public string AppName => AppPath != null ? Path.GetFileNameWithoutExtension(AppPath) : "";
    public string DisplayName => !string.IsNullOrWhiteSpace(Tip) ? Tip : (AppName.Length > 0 ? AppName : "Unknown app");

    public string Key => Guid != Guid.Empty ? "g:" + Guid : $"h:{HWnd}:{Id}";

    /// <summary>
    /// Identity that survives restarts *and* app updates: the exe file name (+ id for apps with several icons), or the GUID.
    /// (Full paths would break for Electron-style apps that install into a new versioned folder on every update.)
    /// </summary>
    public string PersistKey =>
        AppPath != null ? $"{Path.GetFileName(AppPath).ToLowerInvariant()}|{Id}"
        : Guid != Guid.Empty ? "g:" + Guid
        : "tip:" + Tip;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnChanged(name);
        return true;
    }

    private void OnChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
