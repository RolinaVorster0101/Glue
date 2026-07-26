using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Glue.Services;

/// <summary>
/// A single diagnostic finding, shaped for display in the Problems panel.
/// </summary>
public record DiagnosticItem(DiagnosticSeverity Severity, string Message, int Line, int Column);

/// <summary>
/// Compiles the current editor text in isolation using Roslyn and returns
/// its diagnostics (syntax errors always; semantic errors only to the extent
/// this single file is self-contained).
///
/// NOTE: this is single-file analysis, not whole-project analysis. Used as a
/// fallback for files that aren't part of a loaded project (or before any
/// project has been loaded via File > Open Project). See
/// AnalyzeProjectDocumentAsync for the true project-aware path, which uses a
/// real Roslyn Document from RoslynProjectService/MSBuildWorkspace instead.
/// </summary>
public static class RoslynDiagnosticsService
{
    public static IReadOnlyList<DiagnosticItem> Analyze(string sourceText, string? filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath ?? "Untitled.cs");

        var compilation = CSharpCompilation.Create(
            assemblyName: "GlueLiveAnalysis",
            syntaxTrees: new[] { tree },
            references: RoslynReferences.System,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = compilation.GetDiagnostics();

        return ToDiagnosticItems(diagnostics);
    }

    /// <summary>
    /// True project-aware analysis: takes the real Document for this file
    /// from the loaded project (see RoslynProjectService.FindDocument), and
    /// returns diagnostics that can see the whole project's other files,
    /// references, and target framework — not just this one file in
    /// isolation. This is what fixes the false "type not found" errors that
    /// single-file Analyze() produces for legitimate cross-file references.
    ///
    /// currentText is passed separately (rather than just using the
    /// Document's on-disk content) because the Document was loaded once at
    /// project-open time — it doesn't automatically reflect unsaved edits
    /// happening live in the editor. WithText() gives an updated, in-memory
    /// version of the Document reflecting exactly what's on screen right now.
    /// </summary>
    public static async Task<IReadOnlyList<DiagnosticItem>> AnalyzeProjectDocumentAsync(
        Document document, string currentText)
    {
        var updatedDocument = document.WithText(SourceText.From(currentText));
        var semanticModel = await updatedDocument.GetSemanticModelAsync();
        if (semanticModel is null) return System.Array.Empty<DiagnosticItem>();

        var diagnostics = semanticModel.GetDiagnostics();

        return ToDiagnosticItems(diagnostics);
    }

    private static IReadOnlyList<DiagnosticItem> ToDiagnosticItems(
        IEnumerable<Diagnostic> diagnostics)
    {
        return diagnostics
            .Select(d =>
            {
                var lineSpan = d.Location.GetLineSpan();
                return new DiagnosticItem(
                    Severity: d.Severity,
                    Message: $"{d.Id}: {d.GetMessage()}",
                    Line: lineSpan.StartLinePosition.Line + 1,
                    Column: lineSpan.StartLinePosition.Character + 1);
            })
            .OrderByDescending(d => d.Severity)
            .ThenBy(d => d.Line)
            .ToList();
    }
}
