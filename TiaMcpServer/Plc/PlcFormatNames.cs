using TiaMcpServer.Contracts.Block;

namespace TiaMcpServer.Plc;

/// <summary>Resolves the document format of a PLC content operation.</summary>
public static class PlcFormatNames
{
    /// <summary>
    /// Returns the normalized format, applying the per-operation default (types default to source,
    /// blocks to xml). Throws <see cref="ArgumentException"/> for an unknown format.
    /// </summary>
    public static string Normalize(string operation, string? format)
        => TryNormalize(operation, format, out var normalized, out var error)
            ? normalized
            : throw new ArgumentException(error);

    /// <summary>Non-throwing form; <paramref name="error"/> is the plain validation text.</summary>
    public static bool TryNormalize(string operation, string? format, out string normalized, out string? error)
    {
        var fallback = operation is "get_type_content" or "update_type_content"
            ? SourceFormatNames.Source
            : SourceFormatNames.Xml;
        return SourceFormatNames.TryNormalize(format, fallback, out normalized, out error);
    }
}
