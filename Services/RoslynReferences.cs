using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Glue.Services;

/// <summary>
/// Shared helper: reference assemblies for the currently running .NET
/// runtime, so a standalone single-file compilation can resolve System.*
/// types without needing a real project/csproj context. Used by both
/// RoslynDiagnosticsService and RoslynCompletionService — previously
/// duplicated in RoslynDiagnosticsService, factored out here.
/// </summary>
internal static class RoslynReferences
{
    private static readonly Lazy<List<MetadataReference>> SystemReferences = new(Load);

    public static IReadOnlyList<MetadataReference> System => SystemReferences.Value;

    private static List<MetadataReference> Load()
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
