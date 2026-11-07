namespace TiaMcpServer.Contracts.Library;

/// <summary>Shared exact library selectors; paths contain folder-name segments.</summary>
public static class LibrarySelectorRules
{
    public static string? CheckType(string? typeName, string? typeGuid, List<string>? folderPath)
    {
        if ((typeName is null) == (typeGuid is null)) return "Supply exactly one of typeName or typeGuid.";
        if (typeName is not null && string.IsNullOrWhiteSpace(typeName)) return "typeName must be nonblank.";
        if (typeGuid is not null && !IsNonEmptyGuid(typeGuid)) return "typeGuid must be a nonempty GUID.";
        if (folderPath is { Count: > 32 } || folderPath?.Any(string.IsNullOrWhiteSpace) == true)
            return "folderPath must contain at most 32 nonblank exact name segments.";
        return null;
    }

    public static string? CheckVersion(string? versionNumber, string? versionGuid, bool? inWork, bool required)
    {
        var count = (versionNumber is null ? 0 : 1) + (versionGuid is null ? 0 : 1) + (inWork is null ? 0 : 1);
        if (count > 1 || required && count == 0) return "Supply exactly one version selector when required: versionNumber, versionGuid or inWork:true.";
        if (inWork == false) return "inWork must be true when supplied.";
        if (versionNumber is not null && LibraryVersionRules.Check(versionNumber) is { } error) return error;
        if (versionGuid is not null && !IsNonEmptyGuid(versionGuid)) return "versionGuid must be a nonempty GUID.";
        return null;
    }

    private static bool IsNonEmptyGuid(string value) => Guid.TryParse(value, out var guid) && guid != Guid.Empty;

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
