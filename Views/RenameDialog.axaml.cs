using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Glue.Views;

/// <summary>
/// A minimal "enter new name" dialog for Rename — Avalonia has no built-in
/// input box, so this is a small custom Window. Pre-fills and selects the
/// current symbol name, returns the entered text via ShowDialog&lt;string?&gt;,
/// or null if cancelled.
/// </summary>
public partial class RenameDialog : Window
{
    public RenameDialog()
    {
        InitializeComponent();
    }

    public RenameDialog(string currentName) : this()
    {
        NameBox.Text = currentName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnRenameClicked(object? sender, RoutedEventArgs e) => Close(NameBox.Text);

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // Enter confirms, Escape cancels — standard dialog behavior.
        if (e.Key == Key.Enter) Close(NameBox.Text);
        else if (e.Key == Key.Escape) Close(null);
    }
}
