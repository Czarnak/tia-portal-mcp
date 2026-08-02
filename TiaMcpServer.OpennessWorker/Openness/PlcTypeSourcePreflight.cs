using System;
using System.Linq;
using System.Xml.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Reads the object name a submitted document declares, so a write can refuse a document whose
/// name or kind does not match the object it was addressed to.
///
/// <para>
/// This is what makes update_type_content and the external-source half of update_block_logic
/// strict rather than upserts: Openness' GenerateBlocksFromSource creates whatever the source
/// declares, so without these checks a typo in the path would silently create a stray object
/// instead of failing.
/// </para>
/// <para>
/// Exactly one declaration is required. A source declaring several — which a real V21 export does
/// whenever dependencies are included — is refused, because a write's preview names one object and
/// its safety token binds to one object.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class PlcTypeSourcePreflight
{
    public static bool TryReadDeclaredName(
        string content,
        string format,
        SourceObjectKind expectedKind,
        out string declaredName,
        out string? error)
    {
        declaredName = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The submitted document is empty.";
            return false;
        }

        return string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
            ? TryReadFromXml(content, out declaredName, out error)
            : TryReadFromSource(content, expectedKind, out declaredName, out error);
    }

    private static bool TryReadFromSource(
        string content,
        SourceObjectKind expectedKind,
        out string declaredName,
        out string? error)
    {
        declaredName = string.Empty;

        var declarations = SourceDeclarationScanner.Scan(content);

        if (declarations.Count == 0)
        {
            error = "The submitted source declares no object. Expected a line beginning with "
                + "TYPE, DATA_BLOCK, FUNCTION_BLOCK, FUNCTION, or ORGANIZATION_BLOCK.";
            return false;
        }

        if (declarations.Count > 1)
        {
            error = $"The submitted source declares {declarations.Count} objects: "
                + SourceDeclarationScanner.Describe(declarations)
                + ". A write accepts exactly one, because its preview and safety token name exactly "
                + "one object. Submit a source declaring only the object being updated, and write "
                + "the others separately.";
            return false;
        }

        var declaration = declarations[0];

        if (declaration.Kind != expectedKind)
        {
            error = $"The submitted source declares "
                + $"{SourceDeclaration.KeywordFor(declaration.Kind)} '{declaration.Name}', but this "
                + $"write targets a {SourceDeclaration.KeywordFor(expectedKind)}. Submit a source "
                + $"declaring a {SourceDeclaration.KeywordFor(expectedKind)}, or address the object "
                + "the source actually declares.";
            return false;
        }

        declaredName = declaration.Name;
        error = null;
        return true;
    }

    private static bool TryReadFromXml(string content, out string declaredName, out string? error)
    {
        declaredName = string.Empty;

        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (Exception ex)
        {
            error = $"The submitted document is not well-formed XML: {ex.Message}";
            return false;
        }

        var name = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Name")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

        if (string.IsNullOrEmpty(name))
        {
            error = "The submitted Simatic ML document has no <Name> element to identify the object.";
            return false;
        }

        declaredName = name!;
        error = null;
        return true;
    }
}
