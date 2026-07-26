using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Glue.Views;

/// <summary>
/// A minimal "enter new method name" dialog for Extract Method — same
/// pattern as RenameDialog, kept separate rather than parameterizing that
/// one, since the title/wording genuinely differ.
/// </summary>
public partial class ExtractMethodDialog : Window
{
    public ExtractMethodDialog()
    {
        InitializeComponent();
    }

    public ExtractMethodDialog(string suggestedName) : this()
    {
        NameBox.Text = suggestedName;
        NameBox.SelectAll();
        NameBox.Focus();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnExtractClicked(object? sender, RoutedEventArgs e) => Close(NameBox.Text);

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Close(NameBox.Text);
        else if (e.Key == Key.Escape) Close(null);
    }
}
