namespace TiaMcpServer.Contracts.Vci;

public sealed class VciPathCheck
{
    public VciPathCheck(bool isValid, string? canonicalPath, string? reason)
    {
        IsValid = isValid;
        CanonicalPath = canonicalPath;
        Reason = reason;
    }

    public bool IsValid { get; }
    public string? CanonicalPath { get; }
    public string? Reason { get; }

    internal static VciPathCheck Accept(string? path) => new(true, path, null);
    internal static VciPathCheck Reject(string reason) => new(false, null, reason);
}

public static class VciPathPolicy
{
    private static readonly char[] Separators = { '\\', '/' };

    public static VciPathCheck ValidateRoot(string? rootPath)
        => ValidateRoot(rootPath, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), IsReparsePoint);

    public static VciPathCheck ValidateRoot(string? rootPath, string userProfile, Func<string, bool> isReparsePoint)
    {
        if (string.IsNullOrWhiteSpace(rootPath)) return VciPathCheck.Reject("workspace_root_required");
        if (rootPath!.Split(Separators).Contains("..")) return VciPathCheck.Reject("workspace_root_traversal");
        var colon = rootPath.IndexOf(':');
        if (HasUncOrDevicePrefix(rootPath) || (colon >= 0 && (colon != 1 || rootPath.IndexOf(':', colon + 1) >= 0)))
            return VciPathCheck.Reject("workspace_root_unsupported_path_syntax");
        if (!IsLocalAbsolute(rootPath)) return VciPathCheck.Reject("workspace_root_must_be_absolute");

        string candidate;
        try { candidate = Path.GetFullPath(rootPath); }
        catch (Exception e) when (IsPathException(e)) { return VciPathCheck.Reject("workspace_root_invalid"); }

        if (PathsEqual(candidate, Path.GetPathRoot(candidate)!)) return VciPathCheck.Reject("workspace_root_is_drive_root");
        if (PathsEqual(candidate, userProfile)) return VciPathCheck.Reject("workspace_root_is_user_profile");
        if (File.Exists(candidate)) return VciPathCheck.Reject("workspace_root_is_file");
        var parent = Path.GetDirectoryName(candidate);
        if (parent is null || !Directory.Exists(parent)) return VciPathCheck.Reject("workspace_root_parent_missing");

        return InspectAncestors(candidate, isReparsePoint, "workspace_root_reparse_ancestor", "workspace_root_is_file")
            ?? VciPathCheck.Accept(candidate);
    }

    public static VciPathCheck ValidateRelativeSyntax(string? relativeDirectory)
    {
        if (string.IsNullOrEmpty(relativeDirectory)) return VciPathCheck.Accept(null);
        if (relativeDirectory!.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return VciPathCheck.Reject("relative_path_invalid");
        if (HasUncOrDevicePrefix(relativeDirectory!) || Path.IsPathRooted(relativeDirectory!))
            return VciPathCheck.Reject("relative_path_must_be_relative");
        if (relativeDirectory!.IndexOf(':') >= 0) return VciPathCheck.Reject("relative_path_alternate_data_stream");
        var segments = relativeDirectory.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Contains("..")) return VciPathCheck.Reject("relative_path_traversal");
        if (segments.Contains("."))
            return VciPathCheck.Reject("relative_path_invalid");
        return VciPathCheck.Accept(null);
    }

    public static VciPathCheck ResolveRelativeDirectory(string rootPath, string? relativeDirectory)
        => ResolveRelativeDirectory(rootPath, relativeDirectory, IsReparsePoint);

    public static VciPathCheck ResolveRelativeDirectory(string rootPath, string? relativeDirectory, Func<string, bool> isReparsePoint)
    {
        string root;
        try { root = Path.GetFullPath(rootPath); }
        catch (Exception e) when (IsPathException(e)) { return VciPathCheck.Reject("workspace_root_invalid"); }
        var syntax = ValidateRelativeSyntax(relativeDirectory);
        if (!syntax.IsValid) return syntax;
        string candidate;
        try { candidate = Path.GetFullPath(Path.Combine(root, relativeDirectory ?? string.Empty)); }
        catch (Exception e) when (IsPathException(e)) { return VciPathCheck.Reject("relative_path_invalid"); }
        if (!IsContained(root, candidate)) return VciPathCheck.Reject("relative_path_outside_workspace");
        if (File.Exists(root)) return VciPathCheck.Reject("workspace_root_is_file");
        return InspectAncestors(candidate, isReparsePoint, "relative_path_reparse_point", "relative_directory_is_file")
            ?? VciPathCheck.Accept(candidate);
    }

    private static VciPathCheck? InspectAncestors(string path, Func<string, bool> isReparsePoint, string reparseReason, string fileReason)
    {
        try
        {
            for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            {
                if (File.Exists(current)) return VciPathCheck.Reject(fileReason);
                if (Directory.Exists(current) && isReparsePoint(current)) return VciPathCheck.Reject(reparseReason);
            }
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
        {
            return VciPathCheck.Reject("path_inspection_failed");
        }
        return null;
    }

    internal static bool IsContained(string root, string candidate)
        => PathsEqual(root, candidate)
            || candidate.StartsWith(root.TrimEnd(Separators) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalAbsolute(string path)
        => path.Length >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
            && path[1] == ':' && (path[2] == '\\' || path[2] == '/');

    private static bool HasUncOrDevicePrefix(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.GetFullPath(left).TrimEnd(Separators), Path.GetFullPath(right).TrimEnd(Separators), StringComparison.OrdinalIgnoreCase);

    private static bool IsPathException(Exception e)
        => e is ArgumentException || e is NotSupportedException || e is PathTooLongException;

    private static bool IsReparsePoint(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
}
