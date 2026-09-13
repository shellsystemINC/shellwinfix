using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TaskbarTYOL.Models;

/// <summary>One button on the taskbar: a pinned launcher, a running window, or both.</summary>
public sealed class TaskItem : INotifyPropertyChanged
{
    private string _title = "";
    private ImageSource? _icon;
    private bool _isActive;
    private bool _isRunning;
    private int _windowCount;

    public IntPtr Hwnd { get; set; }
    public string? ExePath { get; set; }          // null for UWP apps (hosted by ApplicationFrameHost) → cannot launch/pin
    public string? PinPath { get; set; }          // path stored in settings (lnk or exe)
    public string? GroupKey { get; set; }         // how running windows were grouped (exe path or per-hwnd)
    public bool IsPinned => PinPath != null;
    public bool CanLaunch => PinPath != null || ExePath != null;
    public bool CanPin => !IsPinned && ExePath != null;
    public List<IntPtr> Windows { get; } = new();
    /// <summary>Set while the icon is being extracted on a worker thread (so we do not queue it twice).</summary>
    public bool IconLoading { get; set; }

    public string Title { get => _title; set => Set(ref _title, value); }
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
    public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }
    public int WindowCount { get => _windowCount; set { if (Set(ref _windowCount, value)) OnPropertyChanged(nameof(HasMultiple)); } }
    public bool HasMultiple => WindowCount > 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
