using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;

namespace Glue.Views;

/// <summary>
/// A single command in the palette — a display name and the action to run
/// when it's chosen. Execute runs AFTER this dialog has already closed
/// (see MainWindow's OnCommandPaletteClicked), since some commands (Rename,
/// Extract Method) open their own dialog, and stacking dialog-on-dialog
/// would be awkward.
/// </summary>
public record CommandPaletteItem(string Name, Action Execute);

/// <summary>
/// Ctrl+Shift+P command palette — every registered command in Glue,
/// searchable by substring (not true fuzzy matching, a simpler and safer
/// first pass). Returns the chosen CommandPaletteItem via
/// ShowDialog&lt;CommandPaletteItem?&gt;, or null if cancelled.
/// </summary>
public partial class CommandPaletteDialog : Window
{
    private List<CommandPaletteItem> _allCommands = new();
    private List<CommandPaletteItem> _filteredCommands = new();

    public CommandPaletteDialog()
    {
        InitializeComponent();
    }

    public CommandPaletteDialog(IReadOnlyList<CommandPaletteItem> commands) : this()
    {
        _allCommands = commands.ToList();
        _filteredCommands = _allCommands;
        CommandsList.ItemsSource = _filteredCommands;
        if (_filteredCommands.Count > 0) CommandsList.SelectedIndex = 0;

        SearchBox.Focus();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text ?? "";

        _filteredCommands = string.IsNullOrWhiteSpace(query)
            ? _allCommands
            : _allCommands
                .Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        CommandsList.ItemsSource = _filteredCommands;
        if (_filteredCommands.Count > 0) CommandsList.SelectedIndex = 0;
    }

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (_filteredCommands.Count > 0)
                {
                    CommandsList.SelectedIndex = Math.Min(CommandsList.SelectedIndex + 1, _filteredCommands.Count - 1);
                }
                e.Handled = true;
                break;

            case Key.Up:
                if (_filteredCommands.Count > 0)
                {
                    CommandsList.SelectedIndex = Math.Max(CommandsList.SelectedIndex - 1, 0);
                }
                e.Handled = true;
                break;

            case Key.Enter:
                if (CommandsList.SelectedItem is CommandPaletteItem item) Close(item);
                e.Handled = true;
                break;

            case Key.Escape:
                Close(null);
                e.Handled = true;
                break;
        }
    }

    private void OnCommandDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CommandsList.SelectedItem is CommandPaletteItem item) Close(item);
    }
}
