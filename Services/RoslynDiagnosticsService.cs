using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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
/// NOTE: this is single-file analysis, not whole-project analysis. Types
/// defined in other files of the same real project will show as "not found"
/// here until Phase 1 item 2 (open the whole .csproj via MSBuildWorkspace)
/// is wired in. See docs/ROADMAP.md, Phase 1.
/// </summary>
public static class RoslynDiagnosticsService
{
    private static readonly Lazy<List<MetadataReference>> SystemReferences = new(LoadSystemReferences);

    public static IReadOnlyList<DiagnosticItem> Analyze(string sourceText, string? filePath)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: filePath ?? "Untitled.cs");

        var compilation = CSharpCompilation.Create(
            assemblyName: "GlueLiveAnalysis",
            syntaxTrees: new[] { tree },
            references: SystemReferences.Value,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var diagnostics = compilation.GetDiagnostics();

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

    /// <summary>
    /// Gathers reference assemblies for the currently running .NET runtime,
    /// so a standalone snippet can at least resolve System.* types without
    /// needing a real project/csproj context.
    /// </summary>
    private static List<MetadataReference> LoadSystemReferences()
    {
        var trustedAssembliesPaths =
            ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator);

        return trustedAssembliesPaths
            .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
            .ToList();
    }
}
