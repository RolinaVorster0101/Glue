using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Glue.Views;

/// <summary>
/// Shown before Rename touches ANY files — lists every file that will
/// change, so nothing gets written to disk without explicit confirmation.
/// Returns true (Rename) or false (Cancel) via ShowDialog&lt;bool&gt;.
/// </summary>
public partial class ConfirmRenameDialog : Window
{
    public ConfirmRenameDialog()
    {
        InitializeComponent();
    }

    public ConfirmRenameDialog(string oldName, string newName, IReadOnlyList<string> affectedFileNames) : this()
    {
        MessageText.Text = $"Rename '{oldName}' to '{newName}' across {affectedFileNames.Count} file(s)?";
        FilesList.ItemsSource = affectedFileNames;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClicked(object? sender, RoutedEventArgs e) => Close(true);
}
