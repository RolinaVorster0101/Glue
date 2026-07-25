using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Highlighting;

namespace Glue;

public partial class MainWindow : Window
{
    private string? _currentFilePath;

    public MainWindow()
    {
        InitializeComponent();

        // Wire up C# syntax highlighting out of the box.
        // AvaloniaEdit ships built-in definitions for common languages by file extension;
        // in later phases this gets replaced/augmented by Roslyn's own classification
        // for C# specifically, and by LSP semantic tokens for Go/C++/TS/CSS/Python.
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".cs");

        // Seed content so the window isn't empty on first run.
        Editor.Text = SampleFile.Content;
        UpdateStatus("Untitled.cs (sample — not saved)");
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = false
        });

        if (files.Count == 0) return;

        var file = files[0];
        await using var stream = await file.OpenReadAsync();
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();

        _currentFilePath = file.Path.LocalPath;
        Editor.Text = text;

        // Re-pick syntax highlighting based on the opened file's actual extension,
        // rather than assuming C# — this is the seed of true multi-language support.
        var ext = Path.GetExtension(_currentFilePath);
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);

        UpdateStatus(_currentFilePath);
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (_currentFilePath is null)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel is null) return;

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save File",
                SuggestedFileName = "Untitled.cs"
            });

            if (file is null) return;
            _currentFilePath = file.Path.LocalPath;
        }

        await File.WriteAllTextAsync(_currentFilePath, Editor.Text);
        UpdateStatus(_currentFilePath);
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        Environment.Exit(0);
    }

    private void UpdateStatus(string path)
    {
        StatusText.Text = path;
    }
}

/// <summary>
/// Placeholder sample content shown on first launch, just to prove
/// the editor + syntax highlighting pipeline works end to end.
/// </summary>
internal static class SampleFile
{
    public const string Content = """
        // Welcome to Glue.
        // This is Phase 1: a working editor shell with syntax highlighting.
        // Next up: Roslyn-backed diagnostics, folding, and Format Document.

        namespace Glue.Sample;

        public class Greeter
        {
            public string Greet(string name) => $"Hello, {name}! Glue is alive.";
        }
        """;
}
