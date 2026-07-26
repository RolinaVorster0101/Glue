using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;
using Microsoft.CodeAnalysis.Text;

namespace Glue.Services;

/// <summary>
/// Where a symbol's definition actually lives — a file path plus a
/// character offset into that file.
/// </summary>
public record DefinitionLocation(string FilePath, int Offset);

/// <summary>
/// A single usage of a symbol, found by Find All References — a file,
/// an offset/line to jump to, and the line's own text for display context.
/// </summary>
public record ReferenceItem(string FilePath, int Offset, int Line, string LineText);

/// <summary>
/// Result of a rename: whether it succeeded, an error message if not, and
/// every changed file's path mapped to its complete new text content.
/// </summary>
public record RenameResult(bool Success, string? ErrorMessage, IReadOnlyDictionary<string, string> ChangedFiles);

/// <summary>
/// Go to Definition, Find All References, and Rename — all backed by real
/// Roslyn symbol resolution against the loaded project (see
/// RoslynProjectService). Requires a project to be loaded — single-file
/// analysis has no meaningful cross-file definitions/references/renames to
/// resolve, so this is deliberately project-only rather than trying to
/// support both paths.
/// </summary>
public static class RoslynNavigationService
{
    public static async Task<DefinitionLocation?> FindDefinitionAsync(
        Document document, string currentText, int caretOffset)
    {
        var updatedDocument = document.WithText(SourceText.From(currentText));
        var semanticModel = await updatedDocument.GetSemanticModelAsync();
        if (semanticModel is null) return null;

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel, caretOffset, updatedDocument.Project.Solution.Workspace);
        if (symbol is null) return null;

        // A symbol can have multiple declaration locations (partial classes,
        // partial methods) — take the first real source location. Symbols
        // from referenced assemblies (no source available) are skipped
        // rather than producing a broken navigation target.
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location?.SourceTree is null) return null;

        var targetDocument = updatedDocument.Project.GetDocument(location.SourceTree);
        if (targetDocument?.FilePath is null) return null;

        return new DefinitionLocation(targetDocument.FilePath, location.SourceSpan.Start);
    }

    /// <summary>
    /// Finds every usage of the symbol under the caret, across every file in
    /// the loaded project — not just the current file. Uses Roslyn's own
    /// SymbolFinder.FindReferencesAsync against the whole Solution, so this
    /// is genuinely project-wide, the same cross-file capability that makes
    /// Go to Definition useful rather than single-file-only.
    /// </summary>
    public static async Task<IReadOnlyList<ReferenceItem>> FindReferencesAsync(
        Document document, string currentText, int caretOffset)
    {
        var updatedDocument = document.WithText(SourceText.From(currentText));
        var semanticModel = await updatedDocument.GetSemanticModelAsync();
        if (semanticModel is null) return Array.Empty<ReferenceItem>();

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel, caretOffset, updatedDocument.Project.Solution.Workspace);
        if (symbol is null) return Array.Empty<ReferenceItem>();

        var referencedSymbols = await SymbolFinder.FindReferencesAsync(
            symbol, updatedDocument.Project.Solution);

        var results = new List<ReferenceItem>();

        foreach (var referencedSymbol in referencedSymbols)
        {
            foreach (var location in referencedSymbol.Locations)
            {
                if (location.Document.FilePath is null) continue;

                var text = await location.Document.GetTextAsync();
                var lineSpan = location.Location.GetLineSpan();
                var lineNumber = lineSpan.StartLinePosition.Line;
                var lineText = lineNumber < text.Lines.Count
                    ? text.Lines[lineNumber].ToString().Trim()
                    : "";

                results.Add(new ReferenceItem(
                    location.Document.FilePath,
                    location.Location.SourceSpan.Start,
                    lineNumber + 1,
                    lineText));
            }
        }

        return results
            .OrderBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Line)
            .ToList();
    }

    /// <summary>
    /// Renames the symbol under the caret across the whole loaded project,
    /// via Roslyn's Renamer.RenameSymbolAsync — a real, semantically-aware
    /// rename (every actual reference gets updated), not a text find-and-
    /// replace. Returns every changed file's full new text content; the
    /// caller is responsible for actually applying those changes (writing to
    /// disk for files other than the one currently open in the editor).
    ///
    /// Written blind against the exact Renamer/SymbolRenameOptions API shape
    /// for Roslyn 4.11 (no local build environment) — double-check against
    /// any build error rather than assuming this is exactly right.
    /// </summary>
    public static async Task<RenameResult> RenameSymbolAsync(
        Document document, string currentText, int caretOffset, string newName)
    {
        var updatedDocument = document.WithText(SourceText.From(currentText));
        var semanticModel = await updatedDocument.GetSemanticModelAsync();
        if (semanticModel is null)
        {
            return new RenameResult(false, "Could not analyze the current file.", new Dictionary<string, string>());
        }

        var symbol = await SymbolFinder.FindSymbolAtPositionAsync(
            semanticModel, caretOffset, updatedDocument.Project.Solution.Workspace);
        if (symbol is null)
        {
            return new RenameResult(false, "No symbol found at cursor.", new Dictionary<string, string>());
        }

        var solution = updatedDocument.Project.Solution;
        Solution newSolution;

        try
        {
            newSolution = await Renamer.RenameSymbolAsync(solution, symbol, new SymbolRenameOptions(), newName);
        }
        catch (Exception ex)
        {
            return new RenameResult(false, $"Rename failed: {ex.Message}", new Dictionary<string, string>());
        }

        var changedFiles = new Dictionary<string, string>();

        foreach (var project in newSolution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath is null) continue;

                var newText = await doc.GetTextAsync();
                var oldDoc = solution.GetDocument(doc.Id);

                if (oldDoc is not null)
                {
                    var oldText = await oldDoc.GetTextAsync();
                    if (oldText.ContentEquals(newText)) continue;
                }

                changedFiles[doc.FilePath] = newText.ToString();
            }
        }

        return new RenameResult(true, null, changedFiles);
    }
}
