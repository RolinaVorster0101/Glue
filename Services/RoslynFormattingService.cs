using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Formatting;

namespace Glue.Services;

/// <summary>
/// Formats C# source using Roslyn's own formatter — the same engine Visual
/// Studio uses. Currently applies Roslyn's default conventions (4-space
/// indents, standard brace placement, standard operator spacing).
///
/// TODO once a personal .editorconfig house style is defined (see
/// docs/ROADMAP.md, "Open Decisions"): load it via AnalyzerConfigOptions
/// and pass it into Formatter.Format so this honors that style instead
/// of Roslyn's defaults.
/// </summary>
public static class RoslynFormattingService
{
    // A single shared AdhocWorkspace is fine here — Formatter.Format only
    // reads default formatting options/services off it, it doesn't need
    // real project/solution state.
    private static readonly AdhocWorkspace Workspace = new();

    public static string Format(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();
        var formattedRoot = Formatter.Format(root, Workspace);
        return formattedRoot.ToFullString();
    }
}
