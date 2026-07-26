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
/// Go to Definition, backed by real Roslyn symbol resolution against the
/// loaded project (see RoslynProjectService). Requires a project to be
/// loaded — single-file analysis has no meaningful cross-file "definition"
/// to jump to, so this is deliberately project-only rather than trying to
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
}
