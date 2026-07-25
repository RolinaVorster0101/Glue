using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Folding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Glue.Services;

/// <summary>
/// Computes foldable regions for a C# source file using Roslyn's syntax tree —
/// real structure (a parser), not brace-counting or regex. Covers two cases:
///   1. #region / #endregion directives (handles nesting correctly via Roslyn's
///      own directive-matching, GetRelatedDirectives()).
///   2. Multi-line bodies of classes, structs, interfaces, methods,
///      constructors, and properties with block accessors.
/// </summary>
public static class RoslynFoldingService
{
    public static List<NewFolding> ComputeFoldings(string sourceText)
    {
        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetRoot();
        var text = tree.GetText();

        var foldings = new List<NewFolding>();

        AddRegionFoldings(root, text, foldings);
        AddBlockFoldings(root, text, foldings);

        // AvaloniaEdit expects foldings sorted by start offset.
        return foldings.OrderBy(f => f.StartOffset).ToList();
    }

    private static void AddRegionFoldings(SyntaxNode root, SourceText text, List<NewFolding> foldings)
    {
        var regionTrivia = root.DescendantTrivia()
            .Where(t => t.IsKind(SyntaxKind.RegionDirectiveTrivia));

        foreach (var trivia in regionTrivia)
        {
            if (trivia.GetStructure() is not RegionDirectiveTriviaSyntax region) continue;

            var endRegion = region.GetRelatedDirectives()
                .OfType<EndRegionDirectiveTriviaSyntax>()
                .FirstOrDefault();

            if (endRegion is null) continue;

            // Computed via line boundaries rather than trivia FullSpan — structured
            // trivia span edges are easy to get subtly wrong (this previously caused
            // the fold to swallow part of the region label and clip #endregion).
            var regionLine = text.Lines.GetLineFromPosition(region.SpanStart);
            var endRegionLine = text.Lines.GetLineFromPosition(endRegion.SpanStart);

            // Need at least one real content line between #region and #endregion.
            if (endRegionLine.LineNumber <= regionLine.LineNumber + 1) continue;

            // Start right after the "#region ..." line's own line break, but
            // skip past that first content line's leading whitespace too —
            // otherwise the fold swallows the indentation itself, and the
            // collapsed marker renders flush-left instead of staying aligned
            // with the surrounding code.
            var firstContentLine = text.Lines[regionLine.LineNumber + 1];
            var firstLineText = text.ToString(firstContentLine.Span);
            var leadingWhitespaceLength = firstLineText.Length - firstLineText.TrimStart().Length;
            var start = firstContentLine.Start + leadingWhitespaceLength;
            var lastContentLine = text.Lines[endRegionLine.LineNumber - 1];
            var end = lastContentLine.End;
            if (end <= start) continue;

            var label = region.EndOfDirectiveToken.LeadingTrivia.ToString().Trim();
            var name = string.IsNullOrWhiteSpace(label) ? "#region" : label;

            foldings.Add(new NewFolding(start, end) { Name = name });
        }
    }

    private static void AddBlockFoldings(SyntaxNode root, SourceText text, List<NewFolding> foldings)
    {
        foreach (var node in root.DescendantNodes())
        {
            BlockSyntax? block = node switch
            {
                MethodDeclarationSyntax m => m.Body,
                ConstructorDeclarationSyntax c => c.Body,
                AccessorDeclarationSyntax a => a.Body,
                _ => null
            };

            if (block is not null)
            {
                TryAddBraceFolding(block.OpenBraceToken, block.CloseBraceToken, text, foldings);
                continue;
            }

            // Type declarations (class/struct/interface/record) fold their whole body too.
            if (node is TypeDeclarationSyntax typeDecl)
            {
                TryAddBraceFolding(typeDecl.OpenBraceToken, typeDecl.CloseBraceToken, text, foldings);
            }
        }
    }

    private static void TryAddBraceFolding(
        SyntaxToken openBrace, SyntaxToken closeBrace, SourceText text, List<NewFolding> foldings)
    {
        if (openBrace.IsMissing || closeBrace.IsMissing) return;

        var start = openBrace.Span.End;
        var end = closeBrace.Span.Start;
        if (end <= start) return;

        // Only fold if the block actually spans more than one line —
        // folding a single-line block is pointless and visually noisy.
        var startLine = text.Lines.GetLineFromPosition(start).LineNumber;
        var endLine = text.Lines.GetLineFromPosition(end).LineNumber;
        if (endLine <= startLine) return;

        foldings.Add(new NewFolding(start, end));
    }
}
