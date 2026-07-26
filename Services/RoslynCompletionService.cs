using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Text;

namespace Glue.Services;

/// <summary>
/// A single completion suggestion, shaped for AvaloniaEdit's CompletionWindow.
/// </summary>
public record CompletionSuggestion(string DisplayText, string? Description);

/// <summary>
/// Basic Roslyn-backed C# completion (Phase 1 item 3's "basic completion").
/// Same single-file scope as RoslynDiagnosticsService — no cross-file/project
/// awareness yet, see docs/ROADMAP.md Phase 1 item 2. Uses an in-memory
/// AdhocWorkspace + Document (not a real project), just enough for Roslyn's
/// CompletionService to reason about the current file's own symbols and
/// System.* types.
/// </summary>
public static class RoslynCompletionService
{
    public static async Task<IReadOnlyList<CompletionSuggestion>> GetCompletionsAsync(
        string sourceText, int caretOffset, string? filterPrefix = null)
    {
        using var workspace = new AdhocWorkspace();

        var projectId = ProjectId.CreateNewId();
        var projectInfo = ProjectInfo.Create(
            projectId,
            VersionStamp.Default,
            name: "GlueCompletion",
            assemblyName: "GlueCompletion",
            language: LanguageNames.CSharp,
            metadataReferences: RoslynReferences.System);

        var project = workspace.AddProject(projectInfo);
        var document = workspace.AddDocument(project.Id, "Document.cs", SourceText.From(sourceText));

        var completionService = CompletionService.GetService(document);
        if (completionService is null) return Array.Empty<CompletionSuggestion>();

        var results = await completionService.GetCompletionsAsync(document, caretOffset);
        return ToSuggestions(results, filterPrefix);
    }

    /// <summary>
    /// True project-aware completion: uses the real Document for this file
    /// from the loaded project (see RoslynProjectService.FindDocument), so
    /// suggestions include the project's other files' types/members, not
    /// just this one file plus System.* in isolation.
    ///
    /// currentText is applied via WithText() for the same reason as
    /// RoslynDiagnosticsService.AnalyzeProjectDocumentAsync — the loaded
    /// Document doesn't automatically reflect unsaved live edits.
    /// </summary>
    public static async Task<IReadOnlyList<CompletionSuggestion>> GetCompletionsForDocumentAsync(
        Document document, string currentText, int caretOffset, string? filterPrefix = null)
    {
        var updatedDocument = document.WithText(SourceText.From(currentText));

        var completionService = CompletionService.GetService(updatedDocument);
        if (completionService is null) return Array.Empty<CompletionSuggestion>();

        var results = await completionService.GetCompletionsAsync(updatedDocument, caretOffset);
        return ToSuggestions(results, filterPrefix);
    }

    private static IReadOnlyList<CompletionSuggestion> ToSuggestions(
        CompletionList? results, string? filterPrefix)
    {
        if (results is null) return Array.Empty<CompletionSuggestion>();

        IEnumerable<CompletionItem> items = results.ItemsList;

        // Filter by whatever's already typed BEFORE capping the list size —
        // capping first, alphabetically, before filtering was a real bug:
        // anything not sorting into the first ~50 raw results (out of
        // hundreds/thousands of in-scope symbols) got discarded before it
        // ever had a chance to match the typed prefix.
        if (!string.IsNullOrEmpty(filterPrefix))
        {
            items = items.Where(i =>
                i.DisplayText.StartsWith(filterPrefix, StringComparison.OrdinalIgnoreCase));
        }

        return items
            .OrderBy(i => i.SortText)
            .Take(50)
            .Select(i => new CompletionSuggestion(i.DisplayText, i.InlineDescription))
            .ToList();
    }
}
