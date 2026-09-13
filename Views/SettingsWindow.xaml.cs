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
    private readonly ObservableCollection<ContextMenuEntry> _context = new();
    private readonly Dictionary<string, Button> _themeCards = new();

    public SettingsWindow()
    {
        InitializeComponent();
        BuildThemeCards();
        LoadFromSettings();
        PinnedList.ItemsSource = _pins;
        ContextList.ItemsSource = _context;
        BuildPresetMenu();
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
        ClassicMenuBox.IsChecked = S.ClassicContextMenu;
        ThemedDesktopBox.IsChecked = S.ThemedDesktopMenu;
        _context.Clear();
        foreach (var e in S.ContextEntries) _context.Add(e.Clone());
        UpdateContextEmpty();
        RefreshPins();
        _loading = false;
    }

    private void UpdateContextEmpty() =>
        ContextEmptyText.Visibility = _context.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

    // ------------------------------------------------------------------ right-click menu
    /// <summary>Persist the current entry list and push it into the registry.</summary>
    private void SaveContext()
    {
        S.ContextEntries = _context.Select(e => e.Clone()).ToList();
        S.Save();
        ContextMenuManager.Sync(S);
        UpdateContextEmpty();
    }

    private void ThemedDesktop_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.ThemedDesktopMenu = ThemedDesktopBox.IsChecked == true;
        App.Current.ApplySettings();   // installs/removes the desktop hook live
    }

    private void ClassicMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.ClassicContextMenu = ClassicMenuBox.IsChecked == true;
        S.Save();
        ContextMenuManager.SetClassicMenu(S.ClassicContextMenu);
        PromptRestartExplorer("Switching the classic / compact right-click menu needs Explorer to reload.");
    }

    private void ContextToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ContextMenuEntry entry) return;
        entry.Enabled = ((CheckBox)sender).IsChecked == true;
        SaveContext();
    }

    private void ContextAdd_Click(object sender, RoutedEventArgs e)
    {
        var entry = new ContextMenuEntry { Label = "", Command = "" };
        if (new ContextEntryDialog(entry, this).ShowDialog() == true)
        {
            _context.Add(entry);
            SaveContext();
        }
    }

    private void ContextEdit_Click(object sender, RoutedEventArgs e)
    {
        if (ContextList.SelectedItem is not ContextMenuEntry sel) return;
        var copy = sel.Clone();
        if (new ContextEntryDialog(copy, this).ShowDialog() == true)
        {
            int i = _context.IndexOf(sel);
            if (i >= 0) _context[i] = copy;
            SaveContext();
        }
    }

    private void ContextRemove_Click(object sender, RoutedEventArgs e)
    {
        if (ContextList.SelectedItem is not ContextMenuEntry sel) return;
        _context.Remove(sel);
        SaveContext();
    }

    private void ContextPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.ContextMenu is { } cm) { cm.PlacementTarget = b; cm.IsOpen = true; }
    }

    private sealed record Preset(string Label, string Command, string? Icon, ContextTarget[] Targets, bool Top = false);

    private void BuildPresetMenu()
    {
        string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string pwsh = System.IO.Path.Combine(sys, "WindowsPowerShell", "v1.0", "powershell.exe");
        string cmd = System.IO.Path.Combine(sys, "cmd.exe");
        string notepad = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        var presets = new List<Preset>
        {
            new("Open PowerShell here", $"\"{pwsh}\" -NoExit -Command \"Set-Location -LiteralPath '%V'\"", pwsh,
                new[] { ContextTarget.DesktopBackground, ContextTarget.Folder, ContextTarget.Drive }, Top: true),
            new("Open Command Prompt here", $"\"{cmd}\" /s /k pushd \"%V\"", cmd,
                new[] { ContextTarget.DesktopBackground, ContextTarget.Folder, ContextTarget.Drive }, Top: true),
            new("Open with Notepad", $"\"{notepad}\" \"%1\"", notepad, new[] { ContextTarget.File }),
            new("Copy as path", "cmd.exe /c echo \"%1\"|clip", "imageres.dll,-5302", new[] { ContextTarget.File, ContextTarget.Folder }),
        };

        if (EverythingSearch.ExePath() is { } evExe)
            presets.Add(new("Search this folder in Everything", $"\"{evExe}\" -search \"%V\"", evExe,
                new[] { ContextTarget.DesktopBackground, ContextTarget.Folder }));

        PresetMenu.Items.Clear();
        foreach (var p in presets)
        {
            var mi = new MenuItem { Header = p.Label, Tag = p };
            mi.Click += Preset_Click;
            PresetMenu.Items.Add(mi);
        }
        var custom = new MenuItem { Header = "Custom…" };
        custom.Click += ContextAdd_Click;
        PresetMenu.Items.Add(new Separator());
        PresetMenu.Items.Add(custom);
    }

    private void Preset_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not Preset p) return;
        _context.Add(new ContextMenuEntry
        {
            Label = p.Label, Command = p.Command, IconPath = p.Icon,
            Targets = p.Targets.ToList(), Top = p.Top, Enabled = true,
        });
        SaveContext();
    }

    private void RestartExplorer_Click(object sender, RoutedEventArgs e) =>
        PromptRestartExplorer("Reload Explorer now so right-click menu changes take effect?");

    private void PromptRestartExplorer(string why)
    {
        var r = MessageBox.Show(this, why + "\n\nThis briefly closes any open File Explorer windows.",
            "Reload Explorer", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r == MessageBoxResult.Yes) ContextMenuManager.RestartExplorer();
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
