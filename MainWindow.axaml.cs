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
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;
using AvaloniaEdit.Search;
using Avalonia.VisualTree;
using Glue.Models;
using Glue.Services;
using Microsoft.CodeAnalysis;

namespace Glue;

public partial class MainWindow : Window
{
    private string? _currentFilePath;
    private FoldingManager? _foldingManager;
    private SearchPanel? _searchPanel;
    private CompletionWindow? _completionWindow;
    private readonly RoslynProjectService _projectService = new();

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

        // Real find (highlighting, next/prev), via AvaloniaEdit's built-in
        // SearchPanel — also wires Ctrl+F automatically. Its stock UI is
        // find-only; there's no built-in replace box (see the Edit menu
        // comment in MainWindow.axaml for the follow-up note on Replace).
        _searchPanel = SearchPanel.Install(Editor);

        // Fixes a template-priority margin that can't be overridden via XAML
        // styling alone — see AdjustChevronContainerMargins() for the full
        // explanation. Runs on every layout pass so newly expanded folders
        // get the fix applied too.
        ExplorerTree.LayoutUpdated += (_, _) => AdjustChevronContainerMargins();

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

    /// <summary>
    /// Handles keyboard shortcuts that AvaloniaEdit doesn't already provide
    /// natively. Deliberately NOT intercepting Ctrl+Z/Ctrl+Y (Undo/Redo) or
    /// Ctrl+F (Find) here — AvaloniaEdit's TextEditor and the installed
    /// SearchPanel already wire those internally; re-handling them here risks
    /// double-firing or breaking their built-in behavior.
    ///
    /// These are single-key-combo shortcuts, same placeholder situation as
    /// Ctrl+Alt+F for Format Document — full keybinding infrastructure
    /// (rebindable, chord-capable) arrives with the Command Palette/Settings
    /// work later (docs/ROADMAP.md, section 2.15).
    /// </summary>
    private void OnWindowKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control);
        var alt = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt);
        var shift = e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift);

        switch (e.Key)
        {
            case Avalonia.Input.Key.F when ctrl && alt:
                FormatDocument();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.N when ctrl:
                NewFile();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.S when ctrl && shift:
                _ = SaveAsAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.S when ctrl:
                _ = SaveAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Up when alt:
                MoveLine(-1);
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Down when alt:
                MoveLine(1);
                e.Handled = true;
                break;

            case Avalonia.Input.Key.D when ctrl:
                DuplicateLine();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.OemQuestion when ctrl: // Ctrl+/
                ToggleLineComment();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.B when ctrl:
                ToggleSidebar();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.J when ctrl:
                TogglePanel();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Space when ctrl:
                TriggerCompletion();
                e.Handled = true;
                break;
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

    private async void OnOpenFolderClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });

        if (folders.Count == 0) return;

        var rootPath = folders[0].Path.LocalPath;
        var rootNode = ProjectTreeBuilder.Build(rootPath);

        // TreeView.ItemsSource expects a collection of roots, even though we
        // only ever have one — a single opened folder.
        ExplorerTree.ItemsSource = new[] { rootNode };
    }

    /// <summary>
    /// First testable milestone for true project parsing (Phase 1 item 2) —
    /// loads a .csproj via MSBuildWorkspace and reports success/failure via
    /// the status bar. Deliberately NOT yet wired into diagnostics or
    /// completion — that's the next step once this is confirmed working.
    /// MSBuild integration has a real track record of assembly-loading
    /// quirks, worth isolating and testing on its own first.
    /// </summary>
    private async void OnOpenProjectClicked(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Project",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("C# Project") { Patterns = new[] { "*.csproj" } }
            }
        });

        if (files.Count == 0) return;

        UpdateStatus("Loading project...");

        try
        {
            var project = await _projectService.OpenProjectAsync(files[0].Path.LocalPath);
            var docCount = project?.Documents.Count() ?? 0;
            UpdateStatus($"Project loaded: {project?.Name} ({docCount} file(s))");
        }
        catch (Exception ex)
        {
            UpdateStatus($"Project load failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// PART_ExpandCollapseChevronContainer's 12,0,12,0 margin is set directly
    /// in Avalonia's built-in TreeViewItem template markup, which sits at a
    /// higher style priority than anything settable via a Style selector in
    /// our own XAML (confirmed via DevTools — an external Style override was
    /// visibly ignored, Bounds stayed pinned to the template's own X=12).
    /// Setting it here, in code, on the live control instance, is "Local"
    /// priority, which genuinely does outrank the template's value.
    ///
    /// Runs on every layout pass so newly-realized containers (e.g. after
    /// expanding a previously-collapsed folder) get fixed too, not just
    /// whatever was visible at load time.
    /// </summary>
    private void AdjustChevronContainerMargins()
    {
        var targetMargin = new Avalonia.Thickness(6, 0, 6, 0);

        foreach (var panel in ExplorerTree.GetVisualDescendants().OfType<Panel>())
        {
            if (panel.Name == "PART_ExpandCollapseChevronContainer" && panel.Margin != targetMargin)
            {
                panel.Margin = targetMargin;
            }
        }
    }

    private async void OnExplorerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ExplorerTree.SelectedItem is FileTreeNode { IsDirectory: false } node)
        {
            await LoadFileIntoEditorAsync(node.FullPath);
        }
    }

    /// <summary>
    /// Shared file-loading path used by both File > Open File and clicking
    /// a file in the Explorer tree — previously duplicated between the two.
    /// </summary>
    private async Task LoadFileIntoEditorAsync(string filePath)
    {
        // Save fold state for whatever was open before switching away from it.
        SaveFoldStateForCurrentFile();

        var text = await File.ReadAllTextAsync(filePath);

        _currentFilePath = filePath;
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

        await LoadFileIntoEditorAsync(files[0].Path.LocalPath);
    }

    private async void OnSaveClicked(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e) => await SaveAsAsync();

    private async void OnSaveAllClicked(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnNewFileClicked(object? sender, RoutedEventArgs e) => NewFile();

    /// <summary>
    /// Prompts to save if there's no current file path yet, otherwise saves
    /// straight to it. Shared by File > Save, the Ctrl+S shortcut, and
    /// File > Save All (which is currently identical — see the XAML comment).
    /// </summary>
    private async Task SaveAsync()
    {
        if (_currentFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await File.WriteAllTextAsync(_currentFilePath, Editor.Text);
        UpdateStatus(_currentFilePath);
        ReanalyzeCurrentFile(restoreFoldState: false);
        SaveFoldStateForCurrentFile();
    }

    /// <summary>Always prompts for a save location, even if a file is already open.</summary>
    private async Task SaveAsAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save As",
            SuggestedFileName = _currentFilePath is not null ? Path.GetFileName(_currentFilePath) : "Untitled.cs"
        });

        if (file is null) return;

        _currentFilePath = file.Path.LocalPath;
        await File.WriteAllTextAsync(_currentFilePath, Editor.Text);
        UpdateStatus(_currentFilePath);
        ReanalyzeCurrentFile(restoreFoldState: false);
        SaveFoldStateForCurrentFile();
    }

    private void NewFile()
    {
        SaveFoldStateForCurrentFile();
        _currentFilePath = null;
        Editor.Text = string.Empty;
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
        UpdateStatus("Untitled.cs (unsaved)");
        ReanalyzeCurrentFile(restoreFoldState: false);
    }

    private void OnUndoClicked(object? sender, RoutedEventArgs e) => Editor.Undo();

    private void OnRedoClicked(object? sender, RoutedEventArgs e) => Editor.Redo();

    private void OnFindClicked(object? sender, RoutedEventArgs e) => _searchPanel?.Open();

    private void OnMoveLineUpClicked(object? sender, RoutedEventArgs e) => MoveLine(-1);

    private void OnMoveLineDownClicked(object? sender, RoutedEventArgs e) => MoveLine(1);

    private void OnDuplicateLineClicked(object? sender, RoutedEventArgs e) => DuplicateLine();

    private void OnToggleCommentClicked(object? sender, RoutedEventArgs e) => ToggleLineComment();

    private void OnTriggerCompletionClicked(object? sender, RoutedEventArgs e) => TriggerCompletion();

    private void OnToggleSidebarClicked(object? sender, RoutedEventArgs e) => ToggleSidebar();

    private void OnTogglePanelClicked(object? sender, RoutedEventArgs e) => TogglePanel();

    private void ToggleSidebar() => SidebarBorder.IsVisible = !SidebarBorder.IsVisible;

    private void TogglePanel() => ProblemsBorder.IsVisible = !ProblemsBorder.IsVisible;

    /// <summary>
    /// Swaps the current line's text with the line above/below (direction:
    /// -1 = up, +1 = down). Done as a single Document.Replace over the
    /// combined span of both lines, rather than two separate Replace calls —
    /// two calls at pre-computed offsets would go stale after the first one
    /// if the two lines have different lengths.
    /// </summary>
    private void MoveLine(int direction)
    {
        var document = Editor.Document;
        var caret = Editor.TextArea.Caret;

        var currentLineNumber = caret.Line;
        var otherLineNumber = currentLineNumber + direction;
        if (otherLineNumber < 1 || otherLineNumber > document.LineCount) return;

        var topLineNumber = Math.Min(currentLineNumber, otherLineNumber);
        var bottomLineNumber = Math.Max(currentLineNumber, otherLineNumber);

        var topLine = document.GetLineByNumber(topLineNumber);
        var bottomLine = document.GetLineByNumber(bottomLineNumber);

        var topText = document.GetText(topLine.Offset, topLine.Length);
        var bottomText = document.GetText(bottomLine.Offset, bottomLine.Length);
        var between = document.GetText(
            topLine.Offset + topLine.Length,
            bottomLine.Offset - (topLine.Offset + topLine.Length));

        var combinedStart = topLine.Offset;
        var combinedLength = (bottomLine.Offset + bottomLine.Length) - combinedStart;

        document.Replace(combinedStart, combinedLength, bottomText + between + topText);

        caret.Line = otherLineNumber;
    }

    private void DuplicateLine()
    {
        var document = Editor.Document;
        var line = document.GetLineByNumber(Editor.TextArea.Caret.Line);

        // Include the line's own delimiter (newline) so the duplicate lands
        // on its own line rather than merging into the original.
        var totalLength = line.Length + line.DelimiterLength;
        var lineTextWithDelimiter = document.GetText(line.Offset, totalLength);

        document.Insert(line.Offset, lineTextWithDelimiter);
    }

    /// <summary>
    /// Toggles "// " line comments for the current line (or every line the
    /// selection touches). C#-only for now, matching Glue's current
    /// single-language stage — per-language comment syntax arrives with
    /// Phase 5's multi-language support.
    /// </summary>
    private void ToggleLineComment()
    {
        var document = Editor.Document;
        var selection = Editor.TextArea.Selection;

        int startLine, endLine;
        if (selection.IsEmpty)
        {
            startLine = endLine = Editor.TextArea.Caret.Line;
        }
        else
        {
            var segment = selection.SurroundingSegment!;
            startLine = document.GetLineByOffset(segment.Offset).LineNumber;
            endLine = document.GetLineByOffset(segment.EndOffset).LineNumber;
        }

        // If every touched line is already commented, uncomment all of them;
        // otherwise comment all of them (matches the usual toggle-comment feel).
        var allCommented = true;
        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);
            if (!text.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                allCommented = false;
                break;
            }
        }

        for (var i = startLine; i <= endLine; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);

            if (allCommented)
            {
                var idx = text.IndexOf("//", StringComparison.Ordinal);
                if (idx < 0) continue;

                var removeLength = 2;
                if (idx + 2 < text.Length && text[idx + 2] == ' ') removeLength = 3;
                document.Remove(line.Offset + idx, removeLength);
            }
            else
            {
                var indent = text.Length - text.TrimStart().Length;
                document.Insert(line.Offset + indent, "// ");
            }
        }
    }

    /// <summary>
    /// Requests completion suggestions from Roslyn (Services/RoslynCompletionService.cs)
    /// for the current caret position, and shows them in AvaloniaEdit's built-in
    /// CompletionWindow. Manual trigger (Ctrl+Space) only for this first pass —
    /// auto-triggering on every typed character (e.g. after ".") is a
    /// reasonable follow-up, not attempted here to keep this step well-scoped.
    /// C#-only, same as the rest of the Roslyn-backed features at this stage.
    /// </summary>
    private async void TriggerCompletion()
    {
        if (!IsCSharpFile)
        {
            UpdateStatus("Completion: skipped — not treated as a C# file (IsCSharpFile was false)");
            return;
        }

        try
        {
            var caretOffset = Editor.CaretOffset;

            var wordStart = caretOffset;
            while (wordStart > 0 && char.IsLetterOrDigit(Editor.Text[wordStart - 1]))
            {
                wordStart--;
            }
            var typedPrefix = Editor.Text.Substring(wordStart, caretOffset - wordStart);

            // Prefix is now passed INTO the service and applied before the
            // 50-item cap — see the comment in RoslynCompletionService for
            // why filtering had to move there, not stay here.
            var suggestions = await RoslynCompletionService.GetCompletionsAsync(
                Editor.Text, caretOffset, typedPrefix);

            if (suggestions.Count == 0) return;

            _completionWindow = new CompletionWindow(Editor.TextArea);
            _completionWindow.StartOffset = wordStart;

            var data = _completionWindow.CompletionList.CompletionData;
            foreach (var suggestion in suggestions)
            {
                data.Add(new GlueCompletionData(suggestion.DisplayText, suggestion.Description));
            }

            _completionWindow.Show();
            _completionWindow.Closed += (_, _) => _completionWindow = null;
        }
        catch (Exception ex)
        {
            // async void swallows exceptions silently by default — surfacing
            // this explicitly so a real failure doesn't look identical to
            // "zero suggestions found".
            UpdateStatus($"Completion error: {ex.GetType().Name}: {ex.Message}");
        }
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
/// Wraps a CompletionSuggestion (Services/RoslynCompletionService.cs) to
/// satisfy AvaloniaEdit's ICompletionData, so the CompletionWindow can
/// display and insert it. Written blind against AvaloniaEdit's API surface
/// (no local build/run environment) — the interface shape is inferred from
/// the upstream AvalonEdit API it was ported from, so double-check against
/// any build errors rather than assuming this is exactly right on the first try.
/// </summary>
internal class GlueCompletionData : ICompletionData
{
    public GlueCompletionData(string text, string? description)
    {
        Text = text;
        Description = string.IsNullOrWhiteSpace(description) ? Text : description;
    }

    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description { get; }
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
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
