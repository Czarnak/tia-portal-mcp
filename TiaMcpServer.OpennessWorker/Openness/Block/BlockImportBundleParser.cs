using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// Worker entry point to <see cref="BlockBundleText.Parse"/>: a format refusal becomes a
/// <c>validation_error</c> with the same message.
/// </summary>
internal static class BlockImportBundleParser
{
    public static ParsedBlockImportBundle Parse(string documentName, string rawContent)
    {
        try
        {
            return BlockBundleText.Parse(documentName, rawContent);
        }
        catch (BlockBundleFormatException exception)
        {
            throw BlockBundleFormat.ToValidationFailure(exception);
        }
    }
}
