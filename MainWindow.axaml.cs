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
using AvaloniaEdit.Folding;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using Glue.Services;
using Microsoft.CodeAnalysis;

namespace Glue;

public partial class MainWindow : Window
{
    private string? _currentFilePath;
    private FoldingManager? _foldingManager;

    // Debounce timer: re-analyze (diagnostics + folding) ~500ms after the
    // user stops typing, rather than on every keystroke.
    private readonly DispatcherTimer _reanalysisDebounceTimer;

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

        _foldingManager = FoldingManager.Install(Editor.TextArea);

        _reanalysisDebounceTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _reanalysisDebounceTimer.Tick += (_, _) =>
        {
            _reanalysisDebounceTimer.Stop();
            ReanalyzeCurrentFile(restoreFoldState: false);
        };

        Editor.TextChanged += (_, _) =>
        {
            _reanalysisDebounceTimer.Stop();
            _reanalysisDebounceTimer.Start();
        };

        Closing += (_, _) => SaveFoldStateForCurrentFile();

        // Seed content so the window isn't empty on first run.
        Editor.Text = SampleFile.Content;
        UpdateStatus("Untitled.cs (sample — not saved)");
        ReanalyzeCurrentFile(restoreFoldState: false);
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
    /// True for .cs files, and for the unsaved sample/new-file state (no path
    /// yet, treated as C# since that's Glue's current default new-file type).
    /// Roslyn diagnostics and folding are C#-specific — running them on an
    /// opened .csproj, .json, etc. would parse that content AS C# and produce
    /// nonsense errors (this was a real bug: opening Glue.csproj showed
    /// "CS1525: Invalid expression term '&lt;'" because the XML was being fed
    /// straight into the C# compiler).
    /// </summary>
    private bool IsCSharpFile =>
        _currentFilePath is null ||
        string.Equals(Path.GetExtension(_currentFilePath), ".cs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs Roslyn analysis on the current editor text (single-file only —
    /// see the note in Services/RoslynDiagnosticsService.cs) and refreshes
    /// both the Problems panel and the folding regions. No-ops (and clears
    /// both panels) for anything that isn't a C# file — see IsCSharpFile.
    /// </summary>
    /// <param name="restoreFoldState">
    /// True right after opening a file (apply previously saved collapsed
    /// regions); false on ordinary typing (keep whatever fold state the
    /// user currently has, just recompute the regions themselves).
    /// </param>
    private void ReanalyzeCurrentFile(bool restoreFoldState)
    {
        if (!IsCSharpFile)
        {
            ProblemsList.ItemsSource = null;
            ProblemsHeader.Text = "Problems";
            _foldingManager?.UpdateFoldings(Enumerable.Empty<AvaloniaEdit.Folding.NewFolding>(), -1);
            return;
        }

        var results = RoslynDiagnosticsService.Analyze(Editor.Text, _currentFilePath);
        ProblemsList.ItemsSource = results.Select(ToDisplayItem).ToList();
        ProblemsHeader.Text = $"Problems ({results.Count})";

        if (_foldingManager is null) return;

        var newFoldings = RoslynFoldingService.ComputeFoldings(Editor.Text);
        _foldingManager.UpdateFoldings(newFoldings, -1);

        if (restoreFoldState && _currentFilePath is not null)
        {
            var collapsedOffsets = FoldStateStore.GetCollapsedOffsets(_currentFilePath).ToHashSet();
            foreach (var section in _foldingManager.AllFoldings)
            {
                if (collapsedOffsets.Contains(section.StartOffset))
                {
                    section.IsFolded = true;
                }
            }
        }
    }

    private void SaveFoldStateForCurrentFile()
    {
        if (_foldingManager is null || _currentFilePath is null) return;

        var collapsedOffsets = _foldingManager.AllFoldings
            .Where(f => f.IsFolded)
            .Select(f => f.StartOffset);

        FoldStateStore.SetCollapsedOffsets(_currentFilePath, collapsedOffsets);
    }

    private void OnExpandAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_foldingManager is null) return;
        foreach (var section in _foldingManager.AllFoldings) section.IsFolded = false;
    }

    private void OnCollapseAllClicked(object? sender, RoutedEventArgs e)
    {
        if (_foldingManager is null) return;
        foreach (var section in _foldingManager.AllFoldings) section.IsFolded = true;
    }

    private void OnFormatDocumentClicked(object? sender, RoutedEventArgs e) => FormatDocument();

    private void OnWindowKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var isCtrlAltF = e.Key == Avalonia.Input.Key.F
            && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);

        if (isCtrlAltF)
        {
            FormatDocument();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Formats the whole document via Roslyn (Services/RoslynFormattingService.cs).
    /// Only applies to C# files — see IsCSharpFile. Running the C# formatter
    /// on, say, a .csproj would attempt to parse XML as C# and corrupt it,
    /// the same class of bug that affected diagnostics/folding.
    ///
    /// Reformatting shifts offsets throughout the file, so fold state gets
    /// recomputed fresh afterward rather than preserved — any regions the
    /// user had manually collapsed before formatting will re-expand. That's
    /// an acceptable tradeoff for now, not something silently broken.
    /// </summary>
    private void FormatDocument()
    {
        if (!IsCSharpFile) return;

        var caretOffset = Editor.CaretOffset;

        Editor.Text = RoslynFormattingService.Format(Editor.Text);

        Editor.CaretOffset = Math.Min(caretOffset, Editor.Text.Length);
        ReanalyzeCurrentFile(restoreFoldState: false);
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

        // Save fold state for whatever was open before switching away from it.
        SaveFoldStateForCurrentFile();

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
        ReanalyzeCurrentFile(restoreFoldState: true);
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
        ReanalyzeCurrentFile(restoreFoldState: false);
        SaveFoldStateForCurrentFile();
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        // Environment.Exit bypasses the window's Closing event entirely,
        // so fold state has to be saved explicitly here too.
        SaveFoldStateForCurrentFile();
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
