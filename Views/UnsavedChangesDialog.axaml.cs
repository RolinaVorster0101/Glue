using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Glue.Views;

public enum UnsavedChangesChoice
{
    SaveAll,
    Discard,
    Cancel
}

/// <summary>
/// Shown when closing Glue (or otherwise navigating away) while there are
/// unsaved changes — either the currently open file is dirty, or Rename has
/// pending changes for files that aren't currently open (see
/// MainWindow.axaml.cs's _pendingFileChanges). Returns which of the three
/// choices the user made via ShowDialog&lt;UnsavedChangesChoice&gt;.
/// </summary>
public partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog()
    {
        InitializeComponent();
    }

    public UnsavedChangesDialog(IReadOnlyList<string> unsavedFileNames) : this()
    {
        FilesList.ItemsSource = unsavedFileNames;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Cancel);

    private void OnDiscardClicked(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.Discard);

    private void OnSaveAllClicked(object? sender, RoutedEventArgs e) => Close(UnsavedChangesChoice.SaveAll);
}
