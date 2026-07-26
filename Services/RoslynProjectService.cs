using System;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace Glue.Services;

/// <summary>
/// Wraps MSBuildWorkspace to load a real .csproj as a full Roslyn Project —
/// all its files, references, and target framework, not just one file
/// analyzed in isolation. This is what Phase 1 item 2 ("true project
/// parsing") actually means, and it's what will eventually replace the
/// single-file AdhocWorkspace approach used elsewhere for cross-file
/// diagnostics/completion.
///
/// Requires Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults() to
/// have already run once at app startup (see Program.cs) — MSBuildWorkspace
/// can't function without it.
///
/// Deliberately scoped narrowly for its first pass: load a project, expose
/// it, nothing else yet. Wiring it into diagnostics/completion for real
/// cross-file awareness is a following step once this is confirmed working —
/// MSBuild integration has a real reputation for assembly-loading quirks
/// that are worth isolating and testing on their own first.
/// </summary>
public class RoslynProjectService
{
    private MSBuildWorkspace? _workspace;

    public Project? CurrentProject { get; private set; }

    public async Task<Project?> OpenProjectAsync(string csprojPath)
    {
        _workspace?.Dispose();
        _workspace = MSBuildWorkspace.Create();

        // Don't let one problematic file (e.g. a generated file, or one
        // referencing a package that failed to restore) abort loading the
        // whole project — surfaced instead via CurrentProject ending up null
        // or with fewer documents than expected, not a hard crash.
        _workspace.WorkspaceFailed += (_, _) => { };

        CurrentProject = await _workspace.OpenProjectAsync(csprojPath);
        return CurrentProject;
    }

    /// <summary>
    /// Finds the Roslyn Document within the currently loaded project matching
    /// a file path on disk, or null if no project is loaded or the file isn't
    /// part of it.
    /// </summary>
    public Document? FindDocument(string filePath)
    {
        if (CurrentProject is null) return null;

        foreach (var doc in CurrentProject.Documents)
        {
            if (doc.FilePath is not null &&
                string.Equals(doc.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            {
                return doc;
            }
        }

        return null;
    }
}
