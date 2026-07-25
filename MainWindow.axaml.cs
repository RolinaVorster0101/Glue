using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Glue.Services;
using Microsoft.CodeAnalysis;

namespace Glue;

public partial class MainWindow : Window
{
    private string? _currentFilePath;

    // Debounce timer: re-analyze ~500ms after the user stops typing,
    // rather than on every keystroke (which would be wasteful and laggy).
    private readonly DispatcherTimer _diagnosticsDebounceTimer;

    public MainWindow()
    {
        InitializeComponent();

        // Load our own C# highlighting (docs/STYLEGUIDE.md palette) instead of
        // AvaloniaEdit's built-in default theme, and register it under ".cs"
        // so any .cs file opened later picks it up automatically too.
        var glueCSharpHighlighting = LoadGlueSyntaxHighlighting();
        HighlightingManager.Instance.RegisterHighlighting(
            "Glue C#", new[] { ".cs" }, glueCSharpHighlighting);

        Editor.SyntaxHighlighting = glueCSharpHighlighting;

        _diagnosticsDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _diagnosticsDebounceTimer.Tick += (_, _) =>
        {
            _diagnosticsDebounceTimer.Stop();
            RunDiagnostics();
        };

        Editor.TextChanged += (_, _) =>
        {
            _diagnosticsDebounceTimer.Stop();
            _diagnosticsDebounceTimer.Start();
        };

        // Seed content so the window isn't empty on first run.
        Editor.Text = SampleFile.Content;
        UpdateStatus("Untitled.cs (sample — not saved)");
        RunDiagnostics();
    }

    /// <summary>
    /// Reads Styles/GlueCSharp.xshd (packaged as an Avalonia resource, see
    /// Glue.csproj) and compiles it into an AvaloniaEdit highlighting definition.
    /// </summary>
    private static IHighlightingDefinition LoadGlueSyntaxHighlighting()
    {
        var uri = new Uri("avares://Glue/Styles/GlueCSharp.xshd");
        using var stream = AssetLoader.Open(uri);
        using var reader = XmlReader.Create(stream);
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }

    /// <summary>
    /// Runs Roslyn analysis on the current editor text (single-file only —
    /// see the note in Services/RoslynDiagnosticsService.cs) and refreshes
    /// the Problems panel.
    /// </summary>
    private void RunDiagnostics()
    {
        var results = RoslynDiagnosticsService.Analyze(Editor.Text, _currentFilePath);

        ProblemsList.ItemsSource = results.Select(ToDisplayItem).ToList();
        ProblemsHeader.Text = $"Problems ({results.Count})";
    }

    private DiagnosticDisplayItem ToDisplayItem(DiagnosticItem d) => new()
    {
        SeverityGlyph = d.Severity switch
        {
            DiagnosticSeverity.Error => "●",
            DiagnosticSeverity.Warning => "▲",
            _ => "ℹ"
        },
        SeverityBrush = d.Severity switch
        {
            DiagnosticSeverity.Error => (IBrush)this.FindResource("DangerBrush")!,
            DiagnosticSeverity.Warning => (IBrush)this.FindResource("WarningBrush")!,
            _ => (IBrush)this.FindResource("InfoBrush")!
        },
        Message = d.Message,
        LineLabel = $"Ln {d.Line}, Col {d.Column}",
        Line = d.Line
    };

    private void OnProblemDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (ProblemsList.SelectedItem is DiagnosticDisplayItem item)
        {
            var lineNumber = Math.Clamp(item.Line, 1, Editor.Document.LineCount);
            var line = Editor.Document.GetLineByNumber(lineNumber);

            Editor.Focus();
            Editor.CaretOffset = line.Offset;

            // Select the whole offending line so the jump is visually obvious
            // even when the line was already on-screen (a moved caret alone
            // can be invisible in a static screenshot / at a glance).
            Editor.Select(line.Offset, line.Length);

            Editor.ScrollToLine(lineNumber);
            Editor.TextArea.Caret.BringCaretToView();
        }
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

        // Re-pick syntax highlighting based on the opened file's actual extension.
        // ".cs" now resolves to our custom Glue definition (registered above);
        // anything else still falls back to AvaloniaEdit's built-in definitions
        // until Phase 5 wires in LSP-backed highlighting per language.
        var ext = Path.GetExtension(_currentFilePath);
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);

        UpdateStatus(_currentFilePath);
        RunDiagnostics();
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
        RunDiagnostics();
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
/// UI-friendly wrapper around a DiagnosticItem for the Problems panel's
/// data template — pre-resolves the severity glyph and brush so the XAML
/// template can bind directly without value converters.
/// </summary>
internal class DiagnosticDisplayItem
{
    public string SeverityGlyph { get; init; } = "";
    public IBrush SeverityBrush { get; init; } = Brushes.Gray;
    public string Message { get; init; } = "";
    public string LineLabel { get; init; } = "";
    public int Line { get; init; }
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
