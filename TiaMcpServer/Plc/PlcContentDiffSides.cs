using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>
/// Picks the two texts a content write's diff compares. An XML write whose content is a single
/// Simatic ML document is compared with the <c>.xml</c> document of the current bundle, not with
/// the whole bundle (whose separator and companions it never carries). Everything else, including
/// any text that does not parse as a bundle, is compared whole, as before. Diff evidence only:
/// the hash check always covers the whole served bundle.
/// </summary>
internal static class PlcContentDiffSides
{
    public static (string Current, string Requested) Select(string format, string currentExport, string submitted)
    {
        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
            return (currentExport, submitted);

        var currentXml = SimaticMlDocument(currentExport, "current.xml");
        if (currentXml is null)
            return (currentExport, submitted);

        if (!BlockBundleText.ContainsDelimiterLine(submitted))
            return (currentXml.Content, submitted);

        var submittedBundle = TryParse("submitted.xml", submitted);
        if (submittedBundle is { Documents.Count: 1 }
            && BlockBundleText.FindSimaticMlDocument(submittedBundle) is { } submittedXml)
            return (currentXml.Content, submittedXml.Content);

        return (currentExport, submitted);
    }

    private static BlockImportDocument? SimaticMlDocument(string text, string fallbackName)
        => TryParse(fallbackName, text) is { } bundle ? BlockBundleText.FindSimaticMlDocument(bundle) : null;

    private static ParsedBlockImportBundle? TryParse(string fallbackName, string text)
    {
        try
        {
            return BlockBundleText.Parse(fallbackName, text);
        }
        catch (BlockBundleFormatException)
        {
            return null;
        }
    }
}
