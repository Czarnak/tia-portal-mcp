using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>The object kinds a Siemens external-source file can declare.</summary>
internal enum SourceObjectKind
{
    Type,
    DataBlock,
    FunctionBlock,
    Function,
    OrganizationBlock,
}

/// <summary>One object declaration found in a source file.</summary>
internal sealed class SourceDeclaration
{
    public SourceDeclaration(SourceObjectKind kind, string name, int lineNumber)
    {
        Kind = kind;
        Name = name;
        LineNumber = lineNumber;
    }

    public SourceObjectKind Kind { get; }

    public string Name { get; }

    /// <summary>1-based, so it matches what an editor shows.</summary>
    public int LineNumber { get; }

    public string Describe() => $"{KeywordFor(Kind)} '{Name}' (line {LineNumber})";

    public static string KeywordFor(SourceObjectKind kind)
    {
        switch (kind)
        {
            case SourceObjectKind.Type: return "TYPE";
            case SourceObjectKind.DataBlock: return "DATA_BLOCK";
            case SourceObjectKind.FunctionBlock: return "FUNCTION_BLOCK";
            case SourceObjectKind.Function: return "FUNCTION";
            case SourceObjectKind.OrganizationBlock: return "ORGANIZATION_BLOCK";
            default: return kind.ToString();
        }
    }
}

/// <summary>
/// Lists every object a Siemens external-source document declares.
///
/// <para>
/// A single .scl file routinely declares several objects of different kinds — the real V21 export
/// DamperAnalog.scl declares two TYPEs, a DATA_BLOCK and a FUNCTION_BLOCK — because
/// GenerateSource emits a block's dependency closure when asked to. GenerateBlocksFromSource then
/// creates all of them, with no notion of which one the caller was addressing. Counting
/// declarations is therefore a safety primitive, not a convenience: it is what lets a write refuse
/// a document that would touch objects the caller never named.
/// </para>
/// <para>
/// Comments and string literals are masked before matching, so a keyword mentioned in prose is not
/// mistaken for a declaration. Double quotes are NOT masked — in SCL they delimit identifiers, and
/// the declared name itself is usually quoted.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class SourceDeclarationScanner
{
    private const char ByteOrderMark = '\uFEFF';

    // Anchored to the start of a line: END_TYPE, END_FUNCTION_BLOCK and friends therefore cannot
    // match, and neither can a member whose name happens to be Type. FUNCTION_BLOCK precedes
    // FUNCTION in the alternation so the longer keyword wins.
    private static readonly Regex DeclarationPattern = new Regex(
        @"^[ \t]*(?<keyword>ORGANIZATION_BLOCK|FUNCTION_BLOCK|DATA_BLOCK|FUNCTION|TYPE)[ \t]+"
        + @"(?:""(?<quoted>[^""\r\n]+)""|(?<bare>[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static IReadOnlyList<SourceDeclaration> Scan(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<SourceDeclaration>();
        }

        var text = content[0] == ByteOrderMark ? content.Substring(1) : content;
        var masked = MaskCommentsAndStrings(text);
        var declarations = new List<SourceDeclaration>();

        foreach (Match match in DeclarationPattern.Matches(masked))
        {
            var quoted = match.Groups["quoted"];
            var name = quoted.Success ? quoted.Value : match.Groups["bare"].Value;

            declarations.Add(new SourceDeclaration(
                KindFor(match.Groups["keyword"].Value),
                name,
                LineNumberAt(masked, match.Index)));
        }

        return declarations;
    }

    public static string Describe(IReadOnlyList<SourceDeclaration> declarations)
    {
        var parts = new List<string>(declarations.Count);
        foreach (var declaration in declarations)
        {
            parts.Add(declaration.Describe());
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Replaces comment and string-literal characters with spaces, preserving length and line
    /// breaks so match offsets and line numbers stay accurate against the original text.
    /// </summary>
    private static string MaskCommentsAndStrings(string content)
    {
        var masked = new StringBuilder(content);
        var length = content.Length;
        var i = 0;

        while (i < length)
        {
            var current = content[i];

            if (current == '/' && i + 1 < length && content[i + 1] == '/')
            {
                while (i < length && content[i] != '\n')
                {
                    Blank(masked, i);
                    i++;
                }

                continue;
            }

            if (current == '(' && i + 1 < length && content[i + 1] == '*')
            {
                Blank(masked, i);
                Blank(masked, i + 1);
                i += 2;

                while (i < length && !(content[i] == '*' && i + 1 < length && content[i + 1] == ')'))
                {
                    Blank(masked, i);
                    i++;
                }

                if (i < length)
                {
                    Blank(masked, i);
                    if (i + 1 < length)
                    {
                        Blank(masked, i + 1);
                    }

                    i += 2;
                }

                continue;
            }

            if (current == '\'')
            {
                Blank(masked, i);
                i++;

                while (i < length && content[i] != '\'')
                {
                    // SCL escapes inside a string literal start with '$'; $' is a literal quote and
                    // must not end the string.
                    if (content[i] == '$' && i + 1 < length)
                    {
                        Blank(masked, i);
                        i++;
                    }

                    Blank(masked, i);
                    i++;
                }

                if (i < length)
                {
                    Blank(masked, i);
                    i++;
                }

                continue;
            }

            i++;
        }

        return masked.ToString();
    }

    private static void Blank(StringBuilder builder, int index)
    {
        if (builder[index] != '\n' && builder[index] != '\r')
        {
            builder[index] = ' ';
        }
    }

    private static int LineNumberAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static SourceObjectKind KindFor(string keyword)
    {
        if (keyword.Equals("TYPE", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.Type;
        if (keyword.Equals("DATA_BLOCK", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.DataBlock;
        if (keyword.Equals("FUNCTION_BLOCK", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.FunctionBlock;
        if (keyword.Equals("ORGANIZATION_BLOCK", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.OrganizationBlock;
        return SourceObjectKind.Function;
    }
}