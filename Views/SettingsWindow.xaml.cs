using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarTYOL.Models;
using TaskbarTYOL.Services;

namespace TaskbarTYOL.Views;

public partial class SettingsWindow : Window
{
    private sealed record PinRow(string Path, string Name, ImageSource? Icon);

    private static Settings S => App.Current.Settings;
    private bool _loading = true;
    private readonly ObservableCollection<PinRow> _pins = new();
    private readonly Dictionary<string, Button> _themeCards = new();

    public SettingsWindow()
    {
        InitializeComponent();
        BuildThemeCards();
        LoadFromSettings();
        PinnedList.ItemsSource = _pins;
        ThemeManager.ThemeChanged += OnThemeChanged;
        Closed += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;
        _loading = false;
    }

    private void OnThemeChanged(string name) => HighlightTheme(name);

    // ------------------------------------------------------------------ theme cards
    private void BuildThemeCards()
    {
        ThemePanel.Children.Clear();
        foreach (var t in ThemeManager.Themes)
        {
            var dict = ThemeManager.Peek(t);
            Brush Get(string k) => dict[k] as Brush ?? Brushes.Transparent;

            var preview = new Border
            {
                Height = 40, CornerRadius = new CornerRadius(4), Background = Get("TaskbarBackground"),
                BorderBrush = Get("TaskbarBorder"), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0),
                    Children =
                    {
                        Swatch(Get("StartBackground") is SolidColorBrush { Color.A: 0 } ? Get("LogoBrush1") : Get("StartBackground"), 22),
                        Swatch(Get("Accent"), 14),
                        Swatch(Get("ButtonActiveIndicator") is SolidColorBrush { Color.A: 0 } ? Get("ButtonActive") : Get("ButtonActiveIndicator"), 14),
                        Swatch(Get("TaskbarForeground"), 14),
                    },
                },
            };

            var card = new Button
            {
                Style = (Style)FindResource("ThemeCard"),
                Content = new StackPanel
                {
                    Children =
                    {
                        preview,
                        new TextBlock { Text = t.Name, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("MenuForeground") },
                        new TextBlock { Text = t.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Opacity = 0.7,
                                        Foreground = (Brush)FindResource("MenuForeground") },
                    },
                },
            };
            card.Click += (_, _) => { S.Theme = t.Name; App.Current.ApplySettings(); };
            _themeCards[t.Name] = card;
            ThemePanel.Children.Add(card);
        }
        HighlightTheme(S.Theme);
    }

    private static Border Swatch(Brush b, double size) => new()
    {
        Width = size, Height = size, Background = b, CornerRadius = new CornerRadius(size / 2), Margin = new Thickness(0, 0, 6, 0),
        BorderBrush = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)), BorderThickness = new Thickness(1),
    };

    private void HighlightTheme(string name)
    {
        foreach (var (n, card) in _themeCards)
            card.Tag = string.Equals(n, name, StringComparison.OrdinalIgnoreCase) ? "Selected" : null;
    }

    // ------------------------------------------------------------------ load / save
    private void LoadFromSettings()
    {
        _loading = true;
        EdgeBox.SelectedIndex = S.Edge == TaskbarEdge.Top ? 1 : 0;
        AlignBox.SelectedIndex = S.Alignment == TaskAlignment.Center ? 1 : 0;
        HeightSlider.Value = S.Height;
        IconSlider.Value = S.IconSize;
        OpacitySlider.Value = S.Opacity;
        LabelsBox.IsChecked = S.ShowLabels;
        CombineBox.IsChecked = S.CombineWindows;
        SearchBox.IsChecked = S.ShowSearchButton;
        TaskViewBox.IsChecked = S.ShowTaskViewButton;
        DesktopBox.IsChecked = S.ShowDesktopButton;
        GearBox.IsChecked = S.ShowSettingsButton;
        DateBox.IsChecked = S.ShowDate;
        SecondsBox.IsChecked = S.ShowSeconds;
        HideNativeBox.IsChecked = S.HideNativeTaskbar;
        WinKeyBox.IsChecked = S.ReplaceWinKey;
        StartupBox.IsChecked = S.StartWithWindows;
        AllMonitorsBox.IsChecked = S.ShowOnAllMonitors;
        TrayIconsBox.IsChecked = S.ShowTrayIcons;
        TrayShowAllBox.IsChecked = S.TrayShowAll;
        InjectBox.IsChecked = S.InjectIntoExplorer;
        InjectStatusText.Text = App.Current.ExplorerHookStatus;
        TrayList.ItemsSource = App.Current.Tray?.Icons;
        TrayEmptyText.Visibility = App.Current.Tray is { Icons.Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        RefreshPins();
        _loading = false;
    }

    private void Commit()
    {
        if (_loading) return;
        S.Edge = EdgeBox.SelectedIndex == 1 ? TaskbarEdge.Top : TaskbarEdge.Bottom;
        S.Alignment = AlignBox.SelectedIndex == 1 ? TaskAlignment.Center : TaskAlignment.Left;
        S.Height = (int)HeightSlider.Value;
        S.IconSize = (int)IconSlider.Value;
        S.Opacity = Math.Round(OpacitySlider.Value, 2);
        S.ShowLabels = LabelsBox.IsChecked == true;
        S.CombineWindows = CombineBox.IsChecked == true;
        S.ShowSearchButton = SearchBox.IsChecked == true;
        S.ShowTaskViewButton = TaskViewBox.IsChecked == true;
        S.ShowDesktopButton = DesktopBox.IsChecked == true;
        S.ShowSettingsButton = GearBox.IsChecked == true;
        S.ShowDate = DateBox.IsChecked == true;
        S.ShowSeconds = SecondsBox.IsChecked == true;
        S.HideNativeTaskbar = HideNativeBox.IsChecked == true;
        S.ReplaceWinKey = WinKeyBox.IsChecked == true;
        S.StartWithWindows = StartupBox.IsChecked == true;
        S.ShowOnAllMonitors = AllMonitorsBox.IsChecked == true;
        S.ShowTrayIcons = TrayIconsBox.IsChecked == true;
        S.TrayShowAll = TrayShowAllBox.IsChecked == true;
        S.InjectIntoExplorer = InjectBox.IsChecked == true;
        App.Current.ApplySettings();
        InjectStatusText.Text = App.Current.ExplorerHookStatus;
    }

    private void TrayPromote_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TrayIcon icon) return;
        App.Current.PromoteTrayIcon(icon.PersistKey, promote: ((CheckBox)sender).IsChecked == true);
    }

    // Sliders fire on every pixel of a drag; repositioning the AppBar + broadcasting a work-area change that often
    // makes the whole desktop stutter, so coalesce slider changes.
    private DispatcherTimer? _sliderDebounce;
    private void CommitDebounced()
    {
        if (_loading) return;
        _sliderDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _sliderDebounce.Stop();
        _sliderDebounce.Tick -= SliderDebounce_Tick;
        _sliderDebounce.Tick += SliderDebounce_Tick;
        _sliderDebounce.Start();
    }
    private void SliderDebounce_Tick(object? sender, EventArgs e) { _sliderDebounce!.Stop(); Commit(); }

    private void Edge_Changed(object sender, SelectionChangedEventArgs e) => Commit();
    private void Align_Changed(object sender, SelectionChangedEventArgs e) => Commit();
    private void Height_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => CommitDebounced();
    private void Icon_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => CommitDebounced();
    private void Opacity_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => CommitDebounced();
    private void Flag_Click(object sender, RoutedEventArgs e) => Commit();

    // ------------------------------------------------------------------ pins
    private void RefreshPins()
    {
        _pins.Clear();
        foreach (var p in S.Pinned)
            _pins.Add(new PinRow(p, Path.GetFileNameWithoutExtension(p), IconHelper.GetFileIcon(p, large: false)));
    }

    private void PinAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose a program or shortcut to pin",
            Filter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        if (!S.Pinned.Contains(dlg.FileName, StringComparer.OrdinalIgnoreCase)) S.Pinned.Add(dlg.FileName);
        App.Current.ApplySettings();
        RefreshPins();
    }

    private void PinRemove_Click(object sender, RoutedEventArgs e)
    {
        if (PinnedList.SelectedItem is not PinRow row) return;
        S.Pinned.RemoveAll(p => string.Equals(p, row.Path, StringComparison.OrdinalIgnoreCase));
        App.Current.ApplySettings();
        RefreshPins();
    }

    private void PinUp_Click(object sender, RoutedEventArgs e) => MovePin(-1);
    private void PinDown_Click(object sender, RoutedEventArgs e) => MovePin(+1);

    private void MovePin(int delta)
    {
        int i = PinnedList.SelectedIndex;
        int j = i + delta;
        if (i < 0 || j < 0 || j >= S.Pinned.Count) return;
        (S.Pinned[i], S.Pinned[j]) = (S.Pinned[j], S.Pinned[i]);
        App.Current.ApplySettings();
        RefreshPins();
        PinnedList.SelectedIndex = j;
    }

    // ------------------------------------------------------------------ misc
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(Settings.Folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Settings.Folder}\"") { UseShellExecute = true });
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset every setting (including pins) to defaults?", "TaskbarTYOL",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { File.Delete(Settings.FilePath); } catch { /* ignore */ }
        // Preserve the captured original Windows-taskbar state across the reset — it is live runtime bookkeeping, not
        // a user preference. Nulling it while the real taskbar is in our auto-hide state would lose the value needed
        // to restore it on exit, leaving the user's taskbar auto-hidden.
        var savedTrayState = S.NativeTaskbarOriginalState;
        var fresh = Settings.Load(); // defaults (incl. default pins) since the file is gone
        foreach (var p in typeof(Settings).GetProperties().Where(p => p.CanWrite))
            p.SetValue(S, p.GetValue(fresh));
        S.NativeTaskbarOriginalState = savedTrayState;
        App.Current.ApplySettings();
        LoadFromSettings();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => App.Current.ExitApp();
}
