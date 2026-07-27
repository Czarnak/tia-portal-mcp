using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Reads the object name a submitted document declares, so a write can refuse a document whose
/// name does not match the object it was addressed to.
///
/// <para>
/// This is what makes update_type_content strict rather than an upsert: Openness'
/// GenerateBlocksFromSource creates an object it does not recognize, so without this check a typo
/// in the path would silently create a stray type instead of failing.
/// </para>
/// <para>
/// Handles TYPE (.udt) and DATA_BLOCK (.db) in one place because the DB phase reuses it unchanged.
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class PlcTypeSourcePreflight
{
    private static readonly Regex DeclarationPattern = new Regex(
        @"^\s*(?<keyword>TYPE|DATA_BLOCK)\s+(?:""(?<quoted>[^""]+)""|(?<bare>[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static bool TryReadDeclaredName(
        string content,
        string format,
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
            : TryReadFromSource(content, out declaredName, out error);
    }

    private static bool TryReadFromSource(string content, out string declaredName, out string? error)
    {
        declaredName = string.Empty;

        var match = DeclarationPattern.Match(content);
        if (!match.Success)
        {
            error = "The submitted source declares no object. Expected a line beginning with "
                + "TYPE (for a PLC data type) or DATA_BLOCK (for a data block).";
            return false;
        }

        var quoted = match.Groups["quoted"];
        declaredName = quoted.Success ? quoted.Value : match.Groups["bare"].Value;
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