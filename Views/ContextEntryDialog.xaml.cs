using System.IO;
using System.Windows;
using TaskbarTYOL.Models;

namespace TaskbarTYOL.Views;

public partial class ContextEntryDialog : Window
{
    private readonly ContextMenuEntry _entry;

    /// <summary>Edit a copy; on OK the caller reads <see cref="Result"/>.</summary>
    public ContextMenuEntry Result => _entry;

    public ContextEntryDialog(ContextMenuEntry entry, Window owner)
    {
        InitializeComponent();
        Owner = owner;
        _entry = entry;

        LabelBox.Text = entry.Label;
        CommandBox.Text = entry.Command;
        IconBox.Text = entry.IconPath ?? "";
        TgtDesktop.IsChecked = entry.Targets.Contains(ContextTarget.DesktopBackground);
        TgtFolder.IsChecked = entry.Targets.Contains(ContextTarget.Folder);
        TgtFile.IsChecked = entry.Targets.Contains(ContextTarget.File);
        TgtDrive.IsChecked = entry.Targets.Contains(ContextTarget.Drive);
        TopBox.IsChecked = entry.Top;
        ShiftBox.IsChecked = entry.Extended;
    }

    private void BrowseCommand_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose the program to run",
            Filter = "Programs|*.exe;*.bat;*.cmd|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        // Default to passing the clicked folder as an argument — the common "open here" shape.
        CommandBox.Text = $"\"{dlg.FileName}\" \"%V\"";
        if (string.IsNullOrWhiteSpace(LabelBox.Text))
            LabelBox.Text = "Open with " + Path.GetFileNameWithoutExtension(dlg.FileName);
        if (string.IsNullOrWhiteSpace(IconBox.Text))
            IconBox.Text = dlg.FileName;
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose an icon source",
            Filter = "Icon sources|*.ico;*.exe;*.dll|All files|*.*",
        };
        if (dlg.ShowDialog(this) == true) IconBox.Text = dlg.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LabelBox.Text)) { Warn("Give the entry a label."); return; }
        if (string.IsNullOrWhiteSpace(CommandBox.Text)) { Warn("Enter a command to run."); return; }

        var targets = new List<ContextTarget>();
        if (TgtDesktop.IsChecked == true) targets.Add(ContextTarget.DesktopBackground);
        if (TgtFolder.IsChecked == true) targets.Add(ContextTarget.Folder);
        if (TgtFile.IsChecked == true) targets.Add(ContextTarget.File);
        if (TgtDrive.IsChecked == true) targets.Add(ContextTarget.Drive);
        if (targets.Count == 0) { Warn("Pick at least one place to show it."); return; }

        _entry.Label = LabelBox.Text.Trim();
        _entry.Command = CommandBox.Text.Trim();
        _entry.IconPath = string.IsNullOrWhiteSpace(IconBox.Text) ? null : IconBox.Text.Trim();
        _entry.Targets = targets;
        _entry.Top = TopBox.IsChecked == true;
        _entry.Extended = ShiftBox.IsChecked == true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Warn(string msg) => MessageBox.Show(this, msg, "Right-click menu entry", MessageBoxButton.OK, MessageBoxImage.Information);
}
