namespace TiaMcpServer.Contracts.Library;

public static class LibraryVersionRules
{
    public static string? Check(string? versionNumber)
    {
        var parts = versionNumber?.Split('.');
        return parts is null || parts.Length != 3
            || parts.Any(part => part.Length == 0 || part.Any(c => c is < '0' or > '9'))
            || parts.All(part => part.All(c => c == '0'))
            ? "versionNumber must contain three numeric parts and must not be 0.0.0." : null;
    }
}
