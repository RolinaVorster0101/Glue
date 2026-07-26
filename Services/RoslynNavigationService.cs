using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
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
/// Go to Definition and Find All References, both backed by real Roslyn
/// symbol resolution against the loaded project (see RoslynProjectService).
/// Requires a project to be loaded — single-file analysis has no meaningful
/// cross-file "definition"/"references" to resolve, so this is deliberately
/// project-only rather than trying to support both paths.
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
}
