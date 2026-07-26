using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Glue.Services;

public record ExtractMethodResult(bool Success, string? ErrorMessage, string? NewDocumentText);

/// <summary>
/// Extract Method — genuinely more involved than Go to Definition/Find
/// References/Rename, which all lean on official Roslyn APIs (SymbolFinder,
/// Renamer) doing the hard work. Roslyn's real Extract Method feature is
/// internal, not public API, so this is hand-rolled using
/// SemanticModel.AnalyzeDataFlow (which IS public) to correctly infer
/// parameters and the return value.
///
/// Deliberately scoped narrow for a first pass:
/// - The selection must consist of whole statements (not a partial
///   expression) within a single method body.
/// - Supports 0 or 1 "output" variables (a value the extracted code
///   computes that's still used afterward) — multiple outputs would need
///   a tuple return or out parameters, not attempted here.
/// - No local function / lambda / async awareness beyond what falls out of
///   the general syntax handling.
///
/// Written blind (no local build/test environment) against a genuinely
/// custom implementation, not just an API call — expect more rounds of
/// fixes than usual for this one.
/// </summary>
public static class RoslynExtractMethodService
{
    public static async Task<ExtractMethodResult> ExtractMethodAsync(
        Document document, string currentText, int selectionStart, int selectionEnd, string newMethodName)
    {
        if (selectionEnd <= selectionStart)
        {
            return new ExtractMethodResult(false, "Select the statements to extract first.", null);
        }

        var updatedDocument = document.WithText(SourceText.From(currentText));
        var syntaxTree = await updatedDocument.GetSyntaxTreeAsync();
        var semanticModel = await updatedDocument.GetSemanticModelAsync();

        if (syntaxTree is null || semanticModel is null)
        {
            return new ExtractMethodResult(false, "Could not analyze the current file.", null);
        }

        var root = await syntaxTree.GetRootAsync();

        // Find the smallest node fully containing the selection, then walk up
        // to the enclosing method — the selection has to live inside one.
        var selectionSpan = TextSpan.FromBounds(selectionStart, selectionEnd);
        var node = root.FindNode(selectionSpan, findInsideTrivia: false, getInnermostNodeForTie: true);
        var containingMethod = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();

        if (containingMethod?.Body is null)
        {
            return new ExtractMethodResult(false,
                "Selection must be inside a single method's body (expression-bodied methods aren't supported yet).",
                null);
        }

        // Only whole statements directly in the method's block, fully inside
        // the selection — not a partial expression, not a nested block's
        // statements reached across a brace boundary.
        var selectedStatements = containingMethod.Body.Statements
            .Where(s => selectionSpan.Contains(s.Span))
            .ToList();

        if (selectedStatements.Count == 0)
        {
            // Temporary diagnostic detail — same approach that cracked the
            // completion-filtering bug earlier: show the actual numbers
            // instead of guessing at the mismatch blind.
            var statementSpans = containingMethod.Body.Statements
                .Select(s => $"[{s.SpanStart}-{s.Span.End}]")
                .ToList();

            return new ExtractMethodResult(false,
                $"No complete statement(s) found. Selection span: [{selectionSpan.Start}-{selectionSpan.End}]. " +
                $"Method '{containingMethod.Identifier.Text}' has {containingMethod.Body.Statements.Count} statement(s) at: " +
                string.Join(", ", statementSpans),
                null);
        }

        var firstStatement = selectedStatements.First();
        var lastStatement = selectedStatements.Last();

        var dataFlow = semanticModel.AnalyzeDataFlow(firstStatement, lastStatement);
        if (dataFlow is null || !dataFlow.Succeeded)
        {
            return new ExtractMethodResult(false, "Roslyn couldn't analyze data flow for this selection.", null);
        }

        // Parameters: variables read by the selection whose value comes from
        // outside it.
        var parameters = dataFlow.DataFlowsIn
            .OrderBy(s => s.Name)
            .ToList();

        // Output: variables the selection assigns that are still used
        // afterward. Scoped to 0 or 1 for this first pass.
        var outputs = dataFlow.DataFlowsOut
            .OrderBy(s => s.Name)
            .ToList();

        if (outputs.Count > 1)
        {
            return new ExtractMethodResult(false,
                $"This selection produces {outputs.Count} values still used afterward ({string.Join(", ", outputs.Select(o => o.Name))}) " +
                "— Extract Method here only supports 0 or 1 for now (would need a tuple return or out parameters).",
                null);
        }

        var outputVariable = outputs.FirstOrDefault();
        var returnTypeText = outputVariable is null ? "void" : GetTypeDisplayString(outputVariable);

        // Build the new method's signature and body.
        var parameterList = string.Join(", ", parameters.Select(p => $"{GetTypeDisplayString(p)} {p.Name}"));
        var statementsText = string.Join("\n    ", selectedStatements.Select(s => s.ToFullString().Trim()));

        var newMethodText =
            $"\n\n    private {returnTypeText} {newMethodName}({parameterList})\n" +
            "    {\n" +
            $"        {statementsText}\n" +
            (outputVariable is not null ? $"        return {outputVariable.Name};\n" : "") +
            "    }";

        // Build the call-site replacement for the original selected statements.
        var argumentList = string.Join(", ", parameters.Select(p => p.Name));
        var callText = outputVariable is null
            ? $"{newMethodName}({argumentList});"
            : $"var {outputVariable.Name} = {newMethodName}({argumentList});";

        // Apply both edits against the same original text: replace the
        // selected statements with the call, and insert the new method
        // right after the containing method's closing brace. Offsets are
        // computed from the ORIGINAL text before either edit, then applied
        // in descending offset order so the earlier edit's position isn't
        // invalidated by the later one.
        var replaceStart = firstStatement.SpanStart;
        var replaceEnd = lastStatement.Span.End;
        var insertOffset = containingMethod.Span.End;

        var sb = new System.Text.StringBuilder(currentText);

        // Later offset first (insertOffset > replaceEnd always, since the
        // method's closing brace comes after its own statements).
        sb.Insert(insertOffset, newMethodText);
        sb.Remove(replaceStart, replaceEnd - replaceStart);
        sb.Insert(replaceStart, callText);

        return new ExtractMethodResult(true, null, sb.ToString());
    }

    private static string GetTypeDisplayString(ISymbol symbol) => symbol switch
    {
        ILocalSymbol local => local.Type.ToDisplayString(),
        IParameterSymbol param => param.Type.ToDisplayString(),
        _ => "var"
    };
}
