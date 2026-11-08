using System.Globalization;

namespace TiaMcpServer.Contracts.Library;

public static class LibraryVersionRules
{
    // No range cap beyond what System.Version (Int32 parts) can hold; the worker parses with it.
    public static string? Check(string? versionNumber)
    {
        var parts = versionNumber?.Split('.');
        return parts is null || parts.Length != 3
            || parts.Any(part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            || parts.All(part => part.All(c => c == '0'))
            ? "versionNumber must contain three numeric parts, each at most 2147483647, and must not be 0.0.0." : null;
    }
}
