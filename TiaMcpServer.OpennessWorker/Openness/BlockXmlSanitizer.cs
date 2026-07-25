using System.Text.RegularExpressions;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Removes non-deterministic content from exported Simatic ML so get_block_content is stable
/// across calls. The DocumentInfo element carries a Created timestamp that changes on every
/// export; leaving it in makes the write-safety state hash non-deterministic and every
/// preview -> apply pair fails with "current state no longer matches" (see commit c53e6f4).
///
/// The removal is textual on purpose. An XDocument round trip would also drop the XML
/// declaration and re-indent the document, and this text is handed to Blocks.Import by
/// update_block_logic — it must stay byte-faithful everywhere except the removed element.
/// </summary>
internal static class BlockXmlSanitizer
{
    private static readonly Regex DocumentInfoElement = new Regex(
        @"[ \t]*<DocumentInfo(?:\s[^>]*)?(?:/>|>.*?</DocumentInfo>)\r?\n?",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string RemoveDocumentInfo(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return xml;
        }

        return DocumentInfoElement.Replace(xml, string.Empty);
    }
}