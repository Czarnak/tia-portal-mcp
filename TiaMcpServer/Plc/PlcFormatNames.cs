using TiaMcpServer.Contracts;

namespace TiaMcpServer.Plc;

/// <summary>Resolves the document format of a PLC content operation.</summary>
public static class PlcFormatNames
{
    /// <summary>
    /// Returns the normalized format, applying the per-operation default (types default to source,
    /// blocks to xml). Throws <see cref="ArgumentException"/> for an unknown format.
    /// </summary>
    public static string Normalize(string operation, string? format)
    {
        var fallback = operation is "get_type_content" or "update_type_content"
            ? SourceFormatNames.Source
            : SourceFormatNames.Xml;

        if (!SourceFormatNames.TryNormalize(format, fallback, out var normalized, out var error))
        {
            throw new ArgumentException(error, nameof(format));
        }

        return normalized;
    }
}
