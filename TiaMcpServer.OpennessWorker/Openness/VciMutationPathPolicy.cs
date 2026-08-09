using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Pure result from a mutation-probe path validation.</summary>
public sealed class VciMutationPathValidationResult
{
    private VciMutationPathValidationResult(
        bool isValid,
        string? canonicalPath,
        string? rejectionCategory,
        string? detail)
    {
        IsValid = isValid;
        CanonicalPath = canonicalPath;
        RejectionCategory = rejectionCategory;
        Detail = detail;
    }

    public bool IsValid { get; }
    public string? CanonicalPath { get; }
    public string? RejectionCategory { get; }
    public string? Detail { get; }

    internal static VciMutationPathValidationResult Accept(string canonicalPath)
        => new VciMutationPathValidationResult(true, canonicalPath, null, null);

    internal static VciMutationPathValidationResult Reject(string category, string? detail = null)
        => new VciMutationPathValidationResult(false, null, category, detail);
}

/// <summary>
/// Vendor-free, non-mutating path confinement for the VCI mutation harness. All comparisons are
/// canonical and Windows-case-insensitive, and all existing path components are inspected for
/// reparse points before a path is accepted.
/// </summary>
public static class VciMutationPathPolicy
{
    private static readonly char[] DirectorySeparators =
        { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };

    public static VciMutationPathValidationResult ValidateWorkspaceRoot(
        string? workspaceRoot,
        string repositoryRoot,
        IReadOnlyList<string> projectPaths,
        string userProfileRoot)
        => ValidateWorkspaceRoot(
            workspaceRoot,
            repositoryRoot,
            projectPaths,
            userProfileRoot,
            IsReparsePoint);

    internal static VciMutationPathValidationResult ValidateWorkspaceRoot(
        string? workspaceRoot,
        string repositoryRoot,
        IReadOnlyList<string> projectPaths,
        string userProfileRoot,
        Func<string, bool> isReparsePoint)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_required");
        }

        if (ContainsParentTraversal(workspaceRoot!))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_traversal");
        }

        if (HasUnsupportedAbsoluteSyntax(workspaceRoot!))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_unsupported_path_syntax");
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(workspaceRoot!);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return VciMutationPathValidationResult.Reject("workspace_root_invalid", exception.GetType().Name);
        }

        if (!Path.IsPathRooted(candidate))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_must_be_absolute");
        }

        var driveRoot = Path.GetPathRoot(candidate);
        if (driveRoot is not null && PathsEqual(candidate, driveRoot))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_is_drive_root");
        }

        if (PathsEqual(candidate, CanonicalizeProtectedPath(userProfileRoot)))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_is_user_profile");
        }

        if (PathsEqual(candidate, CanonicalizeProtectedPath(repositoryRoot)))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_is_repository_root");
        }

        foreach (var projectPath in projectPaths ?? Array.Empty<string>())
        {
            var canonicalProjectPath = CanonicalizeProtectedPath(projectPath);
            var projectDirectory = Path.GetDirectoryName(canonicalProjectPath);
            if (projectDirectory is not null && PathsEqual(candidate, projectDirectory))
            {
                return VciMutationPathValidationResult.Reject("workspace_root_is_project_directory");
            }
        }

        if (Directory.Exists(candidate) || File.Exists(candidate))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_already_exists");
        }

        var parent = Path.GetDirectoryName(candidate);
        if (parent is null || !Directory.Exists(parent))
        {
            return VciMutationPathValidationResult.Reject("workspace_root_parent_missing");
        }

        var inspectionError = InspectExistingAncestors(parent, isReparsePoint);
        return inspectionError ?? VciMutationPathValidationResult.Accept(candidate);
    }

    public static VciMutationPathValidationResult ResolveRelativeDirectory(
        string workspaceRoot,
        string? relativeDirectory)
        => ResolveRelativeDirectory(workspaceRoot, relativeDirectory, IsReparsePoint);

    internal static VciMutationPathValidationResult ResolveRelativeDirectory(
        string workspaceRoot,
        string? relativeDirectory,
        Func<string, bool> isReparsePoint)
    {
        string canonicalRoot;
        try
        {
            canonicalRoot = Path.GetFullPath(workspaceRoot);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return VciMutationPathValidationResult.Reject("workspace_root_invalid", exception.GetType().Name);
        }

        if (!Directory.Exists(canonicalRoot))
        {
            return File.Exists(canonicalRoot)
                ? VciMutationPathValidationResult.Reject("workspace_root_is_file")
                : VciMutationPathValidationResult.Reject("workspace_root_missing");
        }

        var relativeError = ValidateRelativePathSyntax(relativeDirectory);
        if (relativeError is not null)
        {
            return relativeError;
        }

        string candidate;
        try
        {
            candidate = string.IsNullOrEmpty(relativeDirectory)
                ? canonicalRoot
                : Path.GetFullPath(Path.Combine(canonicalRoot, relativeDirectory!));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return VciMutationPathValidationResult.Reject("relative_path_invalid", exception.GetType().Name);
        }

        if (!IsContained(canonicalRoot, candidate))
        {
            return VciMutationPathValidationResult.Reject("relative_path_outside_workspace");
        }

        if (File.Exists(candidate))
        {
            return VciMutationPathValidationResult.Reject("relative_directory_is_file");
        }

        if (!Directory.Exists(candidate))
        {
            return VciMutationPathValidationResult.Reject("relative_directory_missing");
        }

        var inspectionError = InspectContainedComponents(canonicalRoot, candidate, isReparsePoint);
        return inspectionError ?? VciMutationPathValidationResult.Accept(candidate);
    }

    public static VciMutationPathValidationResult ResolveFile(
        string workspaceRoot,
        string? relativeDirectory,
        string? fileName)
    {
        var fileNameError = ValidateFileName(fileName);
        if (fileNameError is not null)
        {
            return fileNameError;
        }

        var directoryResult = ResolveRelativeDirectory(workspaceRoot, relativeDirectory);
        if (!directoryResult.IsValid)
        {
            return directoryResult;
        }

        var candidate = Path.GetFullPath(Path.Combine(directoryResult.CanonicalPath!, fileName!));
        if (!IsContained(Path.GetFullPath(workspaceRoot), candidate))
        {
            return VciMutationPathValidationResult.Reject("file_path_outside_workspace");
        }

        if (Directory.Exists(candidate))
        {
            return VciMutationPathValidationResult.Reject("file_target_is_directory");
        }

        if (File.Exists(candidate) && IsReparsePoint(candidate))
        {
            return VciMutationPathValidationResult.Reject("file_target_is_reparse_point");
        }

        return VciMutationPathValidationResult.Accept(candidate);
    }

    private static VciMutationPathValidationResult? ValidateRelativePathSyntax(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return null;
        }

        if (HasDeviceOrUncPrefix(relativePath!) || Path.IsPathRooted(relativePath!))
        {
            return VciMutationPathValidationResult.Reject("relative_path_must_be_relative");
        }

        if (relativePath!.IndexOf(':') >= 0)
        {
            return VciMutationPathValidationResult.Reject("relative_path_alternate_data_stream");
        }

        var segments = relativePath.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            return VciMutationPathValidationResult.Reject("relative_path_traversal");
        }

        if (segments.Any(segment => string.Equals(segment, ".", StringComparison.Ordinal))
            || relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            return VciMutationPathValidationResult.Reject("relative_path_invalid");
        }

        return null;
    }

    private static VciMutationPathValidationResult? ValidateFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return VciMutationPathValidationResult.Reject("file_name_required");
        }

        if (Path.IsPathRooted(fileName!) || fileName!.IndexOfAny(DirectorySeparators) >= 0)
        {
            return VciMutationPathValidationResult.Reject("file_name_must_be_leaf");
        }

        if (fileName.IndexOf(':') >= 0)
        {
            return VciMutationPathValidationResult.Reject("file_name_alternate_data_stream");
        }

        if (fileName is "." or ".."
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || IsReservedWindowsFileName(fileName))
        {
            return VciMutationPathValidationResult.Reject("file_name_invalid");
        }

        return null;
    }

    private static VciMutationPathValidationResult? InspectExistingAncestors(
        string path,
        Func<string, bool> isReparsePoint)
    {
        try
        {
            for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            {
                if (isReparsePoint(current))
                {
                    return VciMutationPathValidationResult.Reject("workspace_root_reparse_ancestor", current);
                }

                var parent = Path.GetDirectoryName(current);
                if (parent is null || PathsEqual(parent, current))
                {
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VciMutationPathValidationResult.Reject("path_inspection_failed", exception.GetType().Name);
        }

        return null;
    }

    private static VciMutationPathValidationResult? InspectContainedComponents(
        string root,
        string candidate,
        Func<string, bool> isReparsePoint)
    {
        try
        {
            var canonicalRoot = Path.GetFullPath(root).TrimEnd(DirectorySeparators);
            var canonicalCandidate = Path.GetFullPath(candidate).TrimEnd(DirectorySeparators);
            var relative = PathsEqual(canonicalRoot, canonicalCandidate)
                ? string.Empty
                : canonicalCandidate.Substring(canonicalRoot.Length + 1);
            var current = canonicalRoot;
            if (isReparsePoint(current))
            {
                return VciMutationPathValidationResult.Reject("relative_path_reparse_point", current);
            }

            foreach (var segment in relative.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                if ((Directory.Exists(current) || File.Exists(current)) && isReparsePoint(current))
                {
                    return VciMutationPathValidationResult.Reject("relative_path_reparse_point", current);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return VciMutationPathValidationResult.Reject("path_inspection_failed", exception.GetType().Name);
        }

        return null;
    }

    private static bool IsContained(string root, string candidate)
    {
        var canonicalRoot = Path.GetFullPath(root).TrimEnd(DirectorySeparators);
        var canonicalCandidate = Path.GetFullPath(candidate).TrimEnd(DirectorySeparators);
        if (PathsEqual(canonicalRoot, canonicalCandidate))
        {
            return true;
        }

        var rootWithSeparator = canonicalRoot + Path.DirectorySeparatorChar;
        return canonicalCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUnsupportedAbsoluteSyntax(string path)
        => HasDeviceOrUncPrefix(path) || HasAlternateDataStream(path);

    private static bool ContainsParentTraversal(string path)
        => path.Split(DirectorySeparators, StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => string.Equals(segment, "..", StringComparison.Ordinal));

    private static bool HasDeviceOrUncPrefix(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal);

    private static bool HasAlternateDataStream(string path)
    {
        var colon = path.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        return colon != 1 || path.IndexOf(':', 2) >= 0;
    }

    private static string CanonicalizeProtectedPath(string path)
        => Path.GetFullPath(path);

    private static bool PathsEqual(string left, string right)
        => string.Equals(
            Path.GetFullPath(left).TrimEnd(DirectorySeparators),
            Path.GetFullPath(right).TrimEnd(DirectorySeparators),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(string path)
        => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsReservedWindowsFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        if (stem is null)
        {
            return false;
        }

        var reserved = new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$" };
        if (reserved.Contains(stem, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4
            && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
            && stem[3] is >= '1' and <= '9';
    }
}
