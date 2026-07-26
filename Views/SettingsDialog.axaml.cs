using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Glue.Services;

namespace Glue.Views;

/// <summary>
/// Editor/formatting preferences (Services/SettingsService.cs). Returns the
/// new GlueSettings via ShowDialog&lt;GlueSettings?&gt; on Save, or null on Cancel.
/// Plain TextBoxes with manual numeric parsing rather than a NumericUpDown
/// control — fewer unknowns to get wrong blind, at the cost of no spinner
/// buttons.
/// </summary>
public partial class SettingsDialog : Window
{
    public SettingsDialog()
    {
        InitializeComponent();
    }

    public SettingsDialog(GlueSettings current) : this()
    {
        FontFamilyBox.Text = current.FontFamily;
        FontSizeBox.Text = current.FontSize.ToString(CultureInfo.InvariantCulture);
        IndentationSizeBox.Text = current.IndentationSize.ToString(CultureInfo.InvariantCulture);
        ConvertTabsCheckBox.IsChecked = current.ConvertTabsToSpaces;
        FormatOnSaveCheckBox.IsChecked = current.FormatOnSave;
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close(null);

    private void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        var fontSize = double.TryParse(FontSizeBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var fs)
            ? fs
            : 14;

        var indentationSize = int.TryParse(IndentationSizeBox.Text, out var indent)
            ? indent
            : 4;

        var settings = new GlueSettings
        {
            FontFamily = string.IsNullOrWhiteSpace(FontFamilyBox.Text)
                ? "Cascadia Code,Consolas,Menlo,monospace"
                : FontFamilyBox.Text,
            FontSize = fontSize,
            IndentationSize = indentationSize,
            ConvertTabsToSpaces = ConvertTabsCheckBox.IsChecked ?? true,
            FormatOnSave = FormatOnSaveCheckBox.IsChecked ?? false
        };

        Close(settings);
    }
}
