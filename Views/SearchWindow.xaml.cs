using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskbarTYOL.Models;
using TaskbarTYOL.Services;

namespace TaskbarTYOL.Views;

public partial class SearchWindow : Window
{
    public sealed class Row
    {
        public required EverythingResult Result { get; init; }
        public string Name => Result.Name;
        public string Folder => Result.Folder;
        public ImageSource? Icon { get; init; }
    }

    private const int MaxResults = 80;
    private readonly ObservableCollection<Row> _rows = new();
    private readonly DispatcherTimer _debounce;
    private CancellationTokenSource? _cts;
    private DateTime _lastDeactivated;

    public SearchWindow()
    {
        InitializeComponent();
        ResultList.ItemsSource = _rows;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RunSearchAsync(); };
    }

    /// <summary>True if the window was just hidden by losing focus (so a click on the bar button should not re-open it).</summary>
    public bool JustDeactivated => (DateTime.UtcNow - _lastDeactivated).TotalMilliseconds < 300;

    public void ShowAt(TaskbarWindow bar)
    {
        var anchor = bar.GetSearchRectDip();
        var barRect = bar.GetBarRectDip();
        var screen = bar.GetMonitorRectDip();
        const double shadowMargin = 10;

        // Centered taskbar: centre the search panel on the monitor; otherwise anchor it to the search button.
        double desiredLeft = App.Current.Settings.Alignment == TaskAlignment.Center
            ? screen.Left + (screen.Width - Width) / 2
            : anchor.Left - shadowMargin;
        Left = Math.Max(screen.Left - shadowMargin, Math.Min(desiredLeft, screen.Right - Width + shadowMargin));
        Top = App.Current.Settings.Edge == TaskbarEdge.Top ? barRect.Bottom - shadowMargin : barRect.Top - Height + shadowMargin;
        Top = Math.Max(screen.Top, Top);

        Show();
        Native.Win32.ForceForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        Activate();
        QueryBox.SelectAll();
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        _ = RunSearchAsync();
    }

    // ------------------------------------------------------------------ searching
    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RunSearchAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        string q = QueryBox.Text.Trim();

        if (!EverythingSearch.IsRunning)
        {
            _rows.Clear();
            ShowStatus(EverythingSearch.IsInstalled
                ? "Everything is installed but not running."
                : "voidtools Everything is not installed. Get it from voidtools.com — this search box is powered by it.",
                canStart: EverythingSearch.IsInstalled);
            return;
        }

        if (q.Length == 0)
        {
            _rows.Clear();
            ShowStatus("Type to search every file and folder on this PC, instantly.", canStart: false);
            return;
        }

        var results = await EverythingSearch.QueryAsync(q, MaxResults, cts.Token);
        if (cts.IsCancellationRequested) return;

        _rows.Clear();
        if (results == null) { ShowStatus("Everything did not answer.", canStart: false); return; }
        if (results.Items.Count == 0) { ShowStatus("No matches.", canStart: false); return; }

        StatusPanel.Visibility = Visibility.Collapsed;
        foreach (var r in results.Items)
            _rows.Add(new Row { Result = r, Icon = IconHelper.GetTypeIcon(r.FullPath, r.IsFolder) });
        ResultList.SelectedIndex = 0;
        FooterText.Text = results.TotalItems > results.Items.Count
            ? $"Showing {results.Items.Count:N0} of {results.TotalItems:N0} matches   ·   Enter: open   ·   Ctrl+Enter: open folder"
            : $"{results.TotalItems:N0} match{(results.TotalItems == 1 ? "" : "es")}   ·   Enter: open   ·   Ctrl+Enter: open folder";
    }

    private void ShowStatus(string text, bool canStart)
    {
        StatusText.Text = text;
        StartEverythingButton.Visibility = canStart ? Visibility.Visible : Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Visible;
        FooterText.Text = "Enter: open   ·   Ctrl+Enter: open folder   ·   Esc: close";
    }

    private async void StartEverything_Click(object sender, RoutedEventArgs e)
    {
        if (!EverythingSearch.TryStart()) return;
        ShowStatus("Starting Everything…", canStart: false);
        for (int i = 0; i < 40 && !EverythingSearch.IsRunning; i++) await Task.Delay(250);
        await RunSearchAsync();
    }

    // ------------------------------------------------------------------ keyboard
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Hide(); e.Handled = true; return; }
        if (e.Key == Key.Enter)
        {
            var row = ResultList.SelectedItem as Row ?? _rows.FirstOrDefault();
            if (row == null) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) OpenFolder(row); else Open(row);
            e.Handled = true;
        }
        else if (e.Key == Key.Down && QueryBox.IsKeyboardFocused && _rows.Count > 0)
        {
            ResultList.SelectedIndex = Math.Min(_rows.Count - 1, Math.Max(0, ResultList.SelectedIndex) + (ResultList.SelectedIndex < 0 ? 0 : 1));
            (ResultList.ItemContainerGenerator.ContainerFromIndex(ResultList.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Up && ResultList.SelectedIndex == 0 && !QueryBox.IsKeyboardFocused)
        {
            QueryBox.Focus();
            e.Handled = true;
        }
    }

    private void ResultList_KeyDown(object sender, KeyEventArgs e)
    {
        // Typing while the list has focus goes back to the query box.
        if (e.Key is >= Key.A and <= Key.Z or >= Key.D0 and <= Key.D9 or Key.Space or Key.Back)
        {
            QueryBox.Focus();
            QueryBox.CaretIndex = QueryBox.Text.Length;
        }
    }

    // ------------------------------------------------------------------ actions
    private static Row? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as Row;

    private void Result_MouseUp(object sender, MouseButtonEventArgs e) { if (RowOf(sender) is { } r) Open(r); }
    private void Open_Click(object sender, RoutedEventArgs e) { if (RowOf(sender) is { } r) Open(r); }
    private void OpenFolder_Click(object sender, RoutedEventArgs e) { if (RowOf(sender) is { } r) OpenFolder(r); }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r) return;
        try { Clipboard.SetText(r.Result.FullPath); } catch { /* clipboard busy */ }
    }

    private void RunAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } r) return;
        Hide();
        TryStart(new ProcessStartInfo(r.Result.FullPath) { UseShellExecute = true, Verb = "runas" });
    }

    private void OpenInEverything_Click(object sender, RoutedEventArgs e)
    {
        var q = QueryBox.Text;
        Hide();
        EverythingSearch.OpenInEverything(q);
    }

    private void Open(Row r)
    {
        Hide();
        TryStart(new ProcessStartInfo(r.Result.FullPath) { UseShellExecute = true, WorkingDirectory = r.Result.IsFolder ? r.Result.FullPath : r.Result.Folder });
    }

    private void OpenFolder(Row r)
    {
        Hide();
        TryStart(new ProcessStartInfo("explorer.exe", $"/select,\"{r.Result.FullPath}\"") { UseShellExecute = true });
    }

    private static void TryStart(ProcessStartInfo psi)
    {
        try { Process.Start(psi); }
        catch (Exception ex)
        {
            if (ex is not System.ComponentModel.Win32Exception { NativeErrorCode: 1223 }) // UAC cancelled
                MessageBox.Show(ex.Message, "Could not open", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (!IsVisible) return;            // we hid ourselves (Esc / open) — not a focus loss
        _lastDeactivated = DateTime.UtcNow;
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!App.Current.IsShuttingDown) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
}
