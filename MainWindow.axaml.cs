using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
using Glue.Views;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;

namespace Glue;

public partial class MainWindow : Window
{
    private string? _currentFilePath;
    private FoldingManager? _foldingManager;
    private SearchPanel? _searchPanel;
    private CompletionWindow? _completionWindow;
    private readonly RoslynProjectService _projectService = new();
    private CancellationTokenSource? _runCancellation;

    // Snapshot of what's actually on disk for _currentFilePath (or "" for a
    // new/unsaved file), used to detect unsaved changes in the open editor.
    private string _lastSavedText = "";

    // Rename can affect files that aren't the one currently open — Glue only
    // edits one file at a time, so there's no editor tab to hold "unsaved"
    // state for those. Their new content lives here instead until Save All
    // (or opening that specific file, which surfaces its pending content)
    // actually writes it to disk.
    private readonly Dictionary<string, string> _pendingFileChanges = new(StringComparer.OrdinalIgnoreCase);

    // Guards against re-triggering the unsaved-changes prompt after the user
    // has already confirmed they want to close (see the Closing handler).
    private bool _isReallyClosing;

    private bool IsCurrentFileDirty => Editor.Text != _lastSavedText;

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
            _ = ReanalyzeCurrentFile(restoreFoldState: false);
        };

        Editor.TextChanged += (_, _) =>
        {
            _reanalysisDebounceTimer.Stop();
            _reanalysisDebounceTimer.Start();
        };

        Closing += OnWindowClosing;

        // Seed content so the window isn't empty on first run.
        Editor.Text = SampleFile.Content;
        UpdateStatus("Untitled.cs (sample — not saved)");
        _ = ReanalyzeCurrentFile(restoreFoldState: false);
    }

    /// <summary>
    /// Gatekeeper for closing the window while there are unsaved changes —
    /// either the current editor is dirty, or Rename left pending changes
    /// for files that aren't currently open (_pendingFileChanges). Cancels
    /// the close, shows Views/UnsavedChangesDialog.axaml, and only actually
    /// closes once the user has explicitly chosen Save All or Discard All.
    /// _isReallyClosing prevents this from re-triggering itself when we
    /// call Close() again after the user's choice.
    /// </summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isReallyClosing) return;

        var hasUnsavedChanges = IsCurrentFileDirty || _pendingFileChanges.Count > 0;
        if (!hasUnsavedChanges)
        {
            SaveFoldStateForCurrentFile();
            _runCancellation?.Cancel();
            return;
        }

        e.Cancel = true;

        var dialog = new UnsavedChangesDialog(GetUnsavedFileNames());
        var choice = await dialog.ShowDialog<UnsavedChangesChoice>(this);

        switch (choice)
        {
            case UnsavedChangesChoice.SaveAll:
                await SaveAllAsync();
                _isReallyClosing = true;
                Close();
                break;

            case UnsavedChangesChoice.Discard:
                _isReallyClosing = true;
                Close();
                break;

            case UnsavedChangesChoice.Cancel:
            default:
                // Stay open — do nothing.
                break;
        }
    }

    /// <summary>
    /// Lists every file with unsaved changes, for display in the exit
    /// prompt and the rename confirmation dialog's context.
    /// </summary>
    private List<string> GetUnsavedFileNames()
    {
        var names = new List<string>();

        if (IsCurrentFileDirty)
        {
            names.Add(_currentFilePath is not null
                ? Path.GetFileName(_currentFilePath) + " (open)"
                : "Untitled.cs (open)");
        }

        names.AddRange(_pendingFileChanges.Keys.Select(Path.GetFileName)!);
        return names;
    }

    /// <summary>
    /// If the currently open file has unsaved changes, asks whether to save,
    /// discard, or cancel — returns false only on Cancel, meaning whatever
    /// the caller was about to do (switch to a different file) should be
    /// aborted entirely. Scoped to just the current file's own dirty state —
    /// unrelated pending rename changes for OTHER files aren't affected by
    /// switching away from this one, so they're not part of this check.
    /// </summary>
    private async Task<bool> ConfirmProceedPastUnsavedChangesAsync()
    {
        if (!IsCurrentFileDirty) return true;

        var fileName = _currentFilePath is not null ? Path.GetFileName(_currentFilePath) : "Untitled.cs";
        var dialog = new UnsavedChangesDialog(new List<string> { fileName + " (open)" });
        var choice = await dialog.ShowDialog<UnsavedChangesChoice>(this);

        switch (choice)
        {
            case UnsavedChangesChoice.SaveAll:
                await SaveAsync();
                return true;
            case UnsavedChangesChoice.Discard:
                return true;
            case UnsavedChangesChoice.Cancel:
            default:
                return false;
        }
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
    /// Runs Roslyn analysis on the current editor text and refreshes both the
    /// Problems panel and the folding regions. No-ops (and clears both
    /// panels) for anything that isn't a C# file — see IsCSharpFile.
    ///
    /// Uses TRUE project-aware analysis (RoslynDiagnosticsService.
    /// AnalyzeProjectDocumentAsync) when the current file is part of a
    /// project loaded via File > Open Project — falls back to single-file
    /// analysis (RoslynDiagnosticsService.Analyze) otherwise, e.g. before any
    /// project has been loaded, or for a file outside the loaded one.
    /// </summary>
    /// <param name="restoreFoldState">
    /// True right after opening a file (apply previously saved collapsed
    /// regions); false on ordinary typing (keep whatever fold state the
    /// user currently has, just recompute the regions themselves).
    /// </param>
    private async Task ReanalyzeCurrentFile(bool restoreFoldState)
    {
        if (!IsCSharpFile)
        {
            ProblemsList.ItemsSource = null;
            ProblemsHeader.Text = "Problems";
            _foldingManager?.UpdateFoldings(Enumerable.Empty<AvaloniaEdit.Folding.NewFolding>(), -1);
            return;
        }

        var projectDocument = _currentFilePath is not null
            ? _projectService.FindDocument(_currentFilePath)
            : null;

        var results = projectDocument is not null
            ? await RoslynDiagnosticsService.AnalyzeProjectDocumentAsync(projectDocument, Editor.Text)
            : RoslynDiagnosticsService.Analyze(Editor.Text, _currentFilePath);

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

    private async void OnFormatDocumentClicked(object? sender, RoutedEventArgs e) => await FormatDocument();

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
                _ = FormatDocument();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.N when ctrl:
                _ = NewFile();
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

            case Avalonia.Input.Key.B when ctrl && !shift:
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

            case Avalonia.Input.Key.B when ctrl && shift:
                OnBuildProjectClicked(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Avalonia.Input.Key.F5 when ctrl:
                OnRunProjectClicked(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Avalonia.Input.Key.G when ctrl && alt:
                OnGoToDefinitionClicked(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Avalonia.Input.Key.R when ctrl && alt:
                OnFindAllReferencesClicked(sender, new RoutedEventArgs());
                e.Handled = true;
                break;

            case Avalonia.Input.Key.F2:
                OnRenameSymbolClicked(sender, new RoutedEventArgs());
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
    private async Task FormatDocument()
    {
        if (!IsCSharpFile) return;

        var caretOffset = Editor.CaretOffset;
        var formatted = RoslynFormattingService.Format(Editor.Text);

        // Document.Replace (not the Text property setter) — setting Editor.Text
        // directly is treated as loading a brand-new document, which bypasses
        // the undo stack entirely (confirmed real bug: Ctrl+Z did nothing after
        // Format Document or Rename). Replace() goes through the same editing
        // pipeline as ordinary typing, so it's a single, real undo step.
        Editor.Document.Replace(0, Editor.Document.TextLength, formatted);

        Editor.CaretOffset = Math.Min(caretOffset, Editor.Text.Length);
        await ReanalyzeCurrentFile(restoreFoldState: false);
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
    /// Loads a .csproj via MSBuildWorkspace for true, cross-file-aware
    /// Roslyn analysis (Phase 1 item 2), AND populates the Explorer sidebar
    /// with the project's containing folder — these were previously two
    /// disconnected actions (loading a project didn't touch the file tree
    /// at all), which was a real gap: the status bar would confirm the
    /// project loaded, but the Explorer sidebar stayed empty.
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

        var csprojPath = files[0].Path.LocalPath;
        UpdateStatus("Loading project...");

        try
        {
            var project = await _projectService.OpenProjectAsync(csprojPath);
            var docCount = project?.Documents.Count() ?? 0;

            // Populate the Explorer sidebar with the project's containing
            // folder — same tree-building logic as File > Open Folder,
            // reused here rather than duplicated.
            var projectFolder = Path.GetDirectoryName(csprojPath);
            if (projectFolder is not null)
            {
                var rootNode = ProjectTreeBuilder.Build(projectFolder);
                ExplorerTree.ItemsSource = new[] { rootNode };
            }

            UpdateStatus($"Project loaded: {project?.Name} ({docCount} file(s))");

            // If a file was already open when the project loaded, and it
            // turns out to be part of this project, re-analyze immediately
            // rather than waiting for the next edit/debounce tick.
            await ReanalyzeCurrentFile(restoreFoldState: false);
        }
        catch (Exception ex)
        {
            UpdateStatus($"Project load failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Runs the real `dotnet build` toolchain against the currently loaded
    /// project (see Services/BuildService.cs) and streams its output live
    /// into the Output tab, switching to it automatically so the build is
    /// visible without an extra click. Requires a project to have been
    /// loaded via File > Open Project first.
    /// </summary>
    private async void OnBuildProjectClicked(object? sender, RoutedEventArgs e)
    {
        var csprojPath = _projectService.CsprojPath;
        if (csprojPath is null)
        {
            UpdateStatus("Build: no project loaded — use File > Open Project first");
            return;
        }

        OutputText.Text = "";
        BottomPanelTabs.SelectedIndex = 1; // switch to the Output tab
        UpdateStatus("Building...");

        var result = await BuildService.BuildAsync(csprojPath, line =>
        {
            // BuildService's callback fires on the process's own background
            // thread — UI updates have to be marshalled back onto the UI
            // thread via the dispatcher, or Avalonia will throw.
            Dispatcher.UIThread.Post(() =>
            {
                OutputText.Text += line + "\n";
                OutputScrollViewer.ScrollToEnd();
            });
        });

        UpdateStatus(result.Success ? "Build succeeded" : "Build failed");
    }

    /// <summary>
    /// Runs `dotnet run` (Services/RunService.cs) against the currently
    /// loaded project — rebuilds if needed, then launches the compiled app,
    /// streaming its console output live into the Output tab. Cancels any
    /// previous run first, so clicking Run again while something's already
    /// running restarts it rather than piling up processes.
    /// </summary>
    private async void OnRunProjectClicked(object? sender, RoutedEventArgs e)
    {
        var csprojPath = _projectService.CsprojPath;
        if (csprojPath is null)
        {
            UpdateStatus("Run: no project loaded — use File > Open Project first");
            return;
        }

        _runCancellation?.Cancel();
        _runCancellation = new CancellationTokenSource();

        OutputText.Text += "\n--- Running ---\n";
        BottomPanelTabs.SelectedIndex = 1;
        UpdateStatus("Running...");

        try
        {
            var result = await RunService.RunAsync(csprojPath, line =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    OutputText.Text += line + "\n";
                    OutputScrollViewer.ScrollToEnd();
                });
            }, _runCancellation.Token);

            UpdateStatus($"Process exited with code {result.ExitCode}");
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Run stopped");
        }
    }

    private void OnStopRunClicked(object? sender, RoutedEventArgs e) => _runCancellation?.Cancel();

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
    /// Also the single shared path for Go to Definition and Find References
    /// jumping to a different file, which is why the unsaved-changes guard
    /// below covers all of these at once rather than needing to be
    /// duplicated at every call site.
    ///
    /// If this file has a pending change from a Rename that hasn't been
    /// saved yet (_pendingFileChanges), that content is shown instead of
    /// what's on disk — and removed from the pending dict, since it's now
    /// tracked as ordinary editor-dirty state instead (compared against
    /// _lastSavedText, which is always set to what's actually on disk).
    /// </summary>
    private async Task<bool> LoadFileIntoEditorAsync(string filePath)
    {
        // If the file currently open has unsaved changes, ask before
        // discarding them by switching away. Returns without doing anything
        // if the user cancels — same class of protection as Rename/Exit.
        if (!await ConfirmProceedPastUnsavedChangesAsync()) return false;

        // Save fold state for whatever was open before switching away from it.
        SaveFoldStateForCurrentFile();

        var diskText = await File.ReadAllTextAsync(filePath);

        _currentFilePath = filePath;
        _lastSavedText = diskText;

        if (_pendingFileChanges.TryGetValue(filePath, out var pendingText))
        {
            Editor.Text = pendingText;
            _pendingFileChanges.Remove(filePath);
        }
        else
        {
            Editor.Text = diskText;
        }

        // Re-pick syntax highlighting based on the opened file's actual extension.
        // ".cs" now resolves to our custom Glue definition (registered above);
        // anything else still falls back to AvaloniaEdit's built-in definitions
        // until Phase 5 wires in LSP-backed highlighting per language.
        var ext = Path.GetExtension(_currentFilePath);
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(ext);

        UpdateStatus(_currentFilePath);
        await ReanalyzeCurrentFile(restoreFoldState: true);
        return true;
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

    private async void OnSaveAllClicked(object? sender, RoutedEventArgs e) => await SaveAllAsync();

    private async void OnNewFileClicked(object? sender, RoutedEventArgs e) => await NewFile();

    /// <summary>
    /// Prompts to save if there's no current file path yet, otherwise saves
    /// straight to it. Shared by File > Save and the Ctrl+S shortcut.
    /// File > Save All (SaveAllAsync) also saves the current file this way,
    /// plus every pending rename change for other files.
    /// </summary>
    private async Task SaveAsync()
    {
        if (_currentFilePath is null)
        {
            await SaveAsAsync();
            return;
        }

        await File.WriteAllTextAsync(_currentFilePath, Editor.Text);
        _lastSavedText = Editor.Text;
        UpdateStatus(_currentFilePath);
        await ReanalyzeCurrentFile(restoreFoldState: false);
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
        _lastSavedText = Editor.Text;
        UpdateStatus(_currentFilePath);
        await ReanalyzeCurrentFile(restoreFoldState: false);
        SaveFoldStateForCurrentFile();
    }

    /// <summary>
    /// Saves the current editor's content (if dirty) AND every pending
    /// rename change for files that aren't currently open — the real,
    /// complete Save All, distinct from plain Save which only ever touches
    /// the currently open file.
    ///
    /// Reloads the loaded project afterward (if any pending changes were
    /// written) so Roslyn's own symbol table reflects the new names —
    /// deliberately NOT done right after Rename itself, since at that point
    /// the pending files' disk content is still the OLD, un-renamed text;
    /// reloading then would just re-read stale state for no benefit.
    /// </summary>
    private async Task SaveAllAsync()
    {
        if (IsCurrentFileDirty)
        {
            await SaveAsync();
        }

        var hadPendingChanges = _pendingFileChanges.Count > 0;

        foreach (var (filePath, newText) in _pendingFileChanges)
        {
            await File.WriteAllTextAsync(filePath, newText);
        }
        _pendingFileChanges.Clear();

        if (hadPendingChanges && _projectService.CsprojPath is not null)
        {
            await _projectService.OpenProjectAsync(_projectService.CsprojPath);
            await ReanalyzeCurrentFile(restoreFoldState: false);
        }

        UpdateStatus("Save All: all changes saved");
    }

    private async Task NewFile()
    {
        SaveFoldStateForCurrentFile();
        _currentFilePath = null;
        Editor.Text = string.Empty;
        _lastSavedText = string.Empty;
        Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinitionByExtension(".cs");
        UpdateStatus("Untitled.cs (unsaved)");
        await ReanalyzeCurrentFile(restoreFoldState: false);
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

            // True project-aware completion (sees the loaded project's other
            // files' types/members) when this file is part of a project
            // loaded via File > Open Project; falls back to single-file
            // completion otherwise — same pattern as ReanalyzeCurrentFile.
            var projectDocument = _currentFilePath is not null
                ? _projectService.FindDocument(_currentFilePath)
                : null;

            var suggestions = projectDocument is not null
                ? await RoslynCompletionService.GetCompletionsForDocumentAsync(
                    projectDocument, Editor.Text, caretOffset, typedPrefix)
                : await RoslynCompletionService.GetCompletionsAsync(
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

    /// <summary>
    /// Jumps to the definition of the symbol under the caret, via real
    /// Roslyn symbol resolution against the loaded project (Services/
    /// RoslynNavigationService.cs). Requires a project loaded via
    /// File > Open Project — same restriction as diagnostics/completion's
    /// project-aware path, and for the same reason (needs actual multiple
    /// documents to navigate between).
    /// </summary>
    private async void OnGoToDefinitionClicked(object? sender, RoutedEventArgs e)
    {
        if (!IsCSharpFile) return;

        var projectDocument = _currentFilePath is not null
            ? _projectService.FindDocument(_currentFilePath)
            : null;

        if (projectDocument is null)
        {
            UpdateStatus("Go to Definition: load a project via File > Open Project first");
            return;
        }

        var definition = await RoslynNavigationService.FindDefinitionAsync(
            projectDocument, Editor.Text, Editor.CaretOffset);

        if (definition is null)
        {
            UpdateStatus("Go to Definition: no definition found at cursor");
            return;
        }

        if (string.Equals(definition.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            // Same file — just move to it, no need to reload anything.
            NavigateEditorTo(definition.Offset);
        }
        else
        {
            // Different file — load it first, then navigate. LoadFileIntoEditorAsync
            // re-analyzes the newly opened file, which is exactly what we want here too.
            // If the user cancels (current file had unsaved changes), don't navigate —
            // that offset belongs to a file that never actually got opened.
            var loaded = await LoadFileIntoEditorAsync(definition.FilePath);
            if (loaded) NavigateEditorTo(definition.Offset);
        }
    }

    /// <summary>
    /// Moves the caret to a character offset in the current editor content,
    /// selecting the whole containing line so the jump is visually obvious —
    /// same pattern as OnProblemDoubleTapped's Problems-panel navigation.
    /// Shared here so Find References (a following step) can reuse it too.
    /// </summary>
    private void NavigateEditorTo(int offset)
    {
        offset = Math.Clamp(offset, 0, Editor.Text.Length);
        var line = Editor.Document.GetLineByOffset(offset);

        Editor.Focus();
        Editor.CaretOffset = offset;
        Editor.Select(line.Offset, line.Length);
        Editor.ScrollToLine(line.LineNumber);
        Editor.TextArea.Caret.BringCaretToView();
    }

    /// <summary>
    /// Finds every usage of the symbol under the caret across the whole
    /// loaded project (Services/RoslynNavigationService.cs), and lists them
    /// in the References tab. Same project-loaded requirement as Go to
    /// Definition, for the same reason.
    /// </summary>
    private async void OnFindAllReferencesClicked(object? sender, RoutedEventArgs e)
    {
        if (!IsCSharpFile) return;

        var projectDocument = _currentFilePath is not null
            ? _projectService.FindDocument(_currentFilePath)
            : null;

        if (projectDocument is null)
        {
            UpdateStatus("Find All References: load a project via File > Open Project first");
            return;
        }

        UpdateStatus("Finding references...");

        var references = await RoslynNavigationService.FindReferencesAsync(
            projectDocument, Editor.Text, Editor.CaretOffset);

        ReferencesList.ItemsSource = references.Select(r => new ReferenceDisplayItem
        {
            FileName = Path.GetFileName(r.FilePath),
            FilePath = r.FilePath,
            Line = r.Line,
            LineText = r.LineText
        }).ToList();

        BottomPanelTabs.SelectedIndex = 2; // References tab
        UpdateStatus($"Find All References: {references.Count} result(s)");
    }

    private async void OnReferenceDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (ReferencesList.SelectedItem is not ReferenceDisplayItem item) return;

        if (!string.Equals(item.FilePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
        {
            var loaded = await LoadFileIntoEditorAsync(item.FilePath);
            if (!loaded) return; // cancelled — don't navigate into a file that never opened
        }

        var lineNumber = Math.Clamp(item.Line, 1, Editor.Document.LineCount);
        var line = Editor.Document.GetLineByNumber(lineNumber);
        NavigateEditorTo(line.Offset);
    }

    /// <summary>
    /// Renames the symbol under the caret across the whole loaded project
    /// (Services/RoslynNavigationService.cs). Prompts for the new name via
    /// a small custom dialog (Views/RenameDialog.axaml — Avalonia has no
    /// built-in input box), then shows Views/ConfirmRenameDialog.axaml
    /// listing every file that would change, BEFORE anything is written.
    ///
    /// On confirmation: the currently open file's content updates directly
    /// in the editor (ordinary unsaved-changes state, same as any edit).
    /// Every OTHER changed file's new content goes into _pendingFileChanges
    /// instead of being written to disk immediately — nothing outside the
    /// currently open file is touched on disk until Save All (or opening
    /// that specific file, which surfaces its pending content).
    /// </summary>
    private async void OnRenameSymbolClicked(object? sender, RoutedEventArgs e)
    {
        if (!IsCSharpFile) return;

        var projectDocument = _currentFilePath is not null
            ? _projectService.FindDocument(_currentFilePath)
            : null;

        if (projectDocument is null)
        {
            UpdateStatus("Rename: load a project via File > Open Project first");
            return;
        }

        // Resolve the symbol first, just to prefill the dialog with its
        // current name — the actual rename re-resolves it independently.
        var updatedDocument = projectDocument.WithText(SourceText.From(Editor.Text));
        var semanticModel = await updatedDocument.GetSemanticModelAsync();
        var symbol = semanticModel is null
            ? null
            : await SymbolFinder.FindSymbolAtPositionAsync(
                semanticModel, Editor.CaretOffset, updatedDocument.Project.Solution.Workspace);

        if (symbol is null)
        {
            UpdateStatus("Rename: no symbol found at cursor");
            return;
        }

        var dialog = new RenameDialog(symbol.Name);
        var newName = await dialog.ShowDialog<string?>(this);

        if (string.IsNullOrWhiteSpace(newName))
        {
            UpdateStatus("Rename cancelled");
            return;
        }

        if (newName == symbol.Name)
        {
            // Previously a silent no-op here — the dialog pre-fills and
            // pre-selects the current name, so clicking Rename without
            // actually typing something new looked like the feature was
            // broken (dialog appears, nothing happens, no explanation).
            UpdateStatus("Rename: new name is the same as the current name — nothing to do");
            return;
        }

        UpdateStatus("Computing rename...");

        var result = await RoslynNavigationService.RenameSymbolAsync(
            projectDocument, Editor.Text, Editor.CaretOffset, newName);

        if (!result.Success)
        {
            UpdateStatus($"Rename failed: {result.ErrorMessage}");
            return;
        }

        if (result.ChangedFiles.Count == 0)
        {
            UpdateStatus("Rename: no changes needed");
            return;
        }

        // Nothing has been written yet — confirm before touching anything.
        var affectedFileNames = result.ChangedFiles.Keys.Select(Path.GetFileName).ToList()!;
        var confirmDialog = new ConfirmRenameDialog(symbol.Name, newName, affectedFileNames);
        var confirmed = await confirmDialog.ShowDialog<bool>(this);

        if (!confirmed)
        {
            UpdateStatus("Rename cancelled");
            return;
        }

        foreach (var (filePath, newText) in result.ChangedFiles)
        {
            if (string.Equals(filePath, _currentFilePath, StringComparison.OrdinalIgnoreCase))
            {
                var caretOffset = Editor.CaretOffset;
                // Document.Replace, not the Text setter — see FormatDocument's
                // comment for why (setting Text directly bypasses the undo stack).
                Editor.Document.Replace(0, Editor.Document.TextLength, newText);
                Editor.CaretOffset = Math.Min(caretOffset, Editor.Text.Length);
            }
            else
            {
                // Not written to disk yet — held as a pending change until
                // Save All, or until this specific file is opened (see
                // LoadFileIntoEditorAsync).
                _pendingFileChanges[filePath] = newText;
            }
        }

        UpdateStatus(
            $"Renamed '{symbol.Name}' to '{newName}' — {result.ChangedFiles.Count} file(s) changed, " +
            "not yet saved (use Save All)");

        await ReanalyzeCurrentFile(restoreFoldState: false);
    }

    private void OnExitClicked(object? sender, RoutedEventArgs e)
    {
        // Previously called Environment.Exit(0) directly, which bypasses the
        // window's Closing event entirely — meaning File > Exit skipped the
        // unsaved-changes prompt while the window's own X button honored it.
        // Routing through Close() instead makes both paths behave the same.
        Close();
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
/// UI-friendly wrapper around a ReferenceItem (Services/RoslynNavigationService.cs)
/// for the References tab's data template.
/// </summary>
internal class ReferenceDisplayItem
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
    public string LineText { get; init; } = "";
    public string LineLabel => $"Ln {Line}";
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
