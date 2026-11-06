namespace TiaMcpServer.Contracts.Library;

/// <summary>Shared exact library selectors; paths contain folder-name segments.</summary>
public static class LibrarySelectorRules
{
    public static string? Check(string? scope, string? libraryPath, string? libraryName,
        List<string>? folderPath, string memberPrefix)
    {
        var scopeMember = Member("scope");
        var pathMember = Member("libraryPath");
        var nameMember = Member("libraryName");
        if (scope is not ("project" or "global")) return $"{scopeMember} must be project or global.";
        if (scope == "project" && (libraryPath is not null || libraryName is not null))
            return memberPrefix.Length == 0
                ? "Project library identity comes from the verified project binding, not a library path/name."
                : $"Project library identity comes from the verified project binding, not {pathMember}/{nameMember}.";
        if (scope == "global" && string.IsNullOrWhiteSpace(libraryPath) && string.IsNullOrWhiteSpace(libraryName))
            return $"Global library selectors require an exact {pathMember} or {nameMember} of an already-open library.";
        if (libraryPath is not null && !LibraryReadMethods.IsAbsolutePath(libraryPath)) return $"{pathMember} must be absolute.";
        if (libraryName is not null && string.IsNullOrWhiteSpace(libraryName)) return $"{nameMember} must be nonblank.";
        if (folderPath is { Count: > 32 } || folderPath?.Any(string.IsNullOrWhiteSpace) == true)
            return $"{Member("folderPath")} must contain at most 32 nonblank exact name segments.";
        return null;

        string Member(string name) => memberPrefix.Length == 0 ? name : memberPrefix + char.ToUpperInvariant(name[0]) + name.Substring(1);
    }
}
