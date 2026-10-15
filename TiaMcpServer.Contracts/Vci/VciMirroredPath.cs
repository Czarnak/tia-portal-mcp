using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Contracts.Vci;

public sealed class VciFileKey : IEquatable<VciFileKey>
{
    public VciFileKey(string directory, string fileName)
    {
        Directory = "\\" + directory.Replace('/', '\\').Trim('\\');
        FileName = fileName;
    }

    public string Directory { get; }
    public string FileName { get; }
    // Keep engineering-name boundaries so a separator in a name cannot become a valid directory.
    internal IReadOnlyList<string>? MirroredSegments { get; set; }

    public static VciFileKey FromMapping(string workspaceRoot, string directoryPath, string fileName)
    {
        var root = Path.GetFullPath(workspaceRoot).TrimEnd('\\', '/');
        var directory = Path.GetFullPath(directoryPath).TrimEnd('\\', '/');
        if (!VciPathPolicy.IsContained(root, directory))
            throw new ArgumentException("Mapping directory must be inside the workspace root.", nameof(directoryPath));
        return new VciFileKey(directory.Substring(root.Length), fileName);
    }

    public bool Equals(VciFileKey? other) => other is not null
        && StringComparer.OrdinalIgnoreCase.Equals(Directory, other.Directory)
        && StringComparer.OrdinalIgnoreCase.Equals(FileName, other.FileName);

    public override bool Equals(object? obj) => Equals(obj as VciFileKey);
    public override int GetHashCode() => unchecked(StringComparer.OrdinalIgnoreCase.GetHashCode(Directory) * 397
        ^ StringComparer.OrdinalIgnoreCase.GetHashCode(FileName));
}

public static class VciMirroredPath
{
    public const int MaxExtensionLength = 8;

    public static VciFileKey Build(string? relativeDirectory, IReadOnlyList<ProjectTreeSelectorSegment> leafPath)
    {
        VciScope.Validate(leafPath);
        if (leafPath is null || leafPath.Count == 0 || !VciScope.IsLeaf(leafPath[leafPath.Count - 1].NodeType))
            throw ProjectTreeSelectionException.Invalid("A mirrored path requires a leaf selector.");
        var syntax = VciPathPolicy.ValidateRelativeSyntax(relativeDirectory);
        if (!syntax.IsValid) throw new ArgumentException(syntax.Reason, nameof(relativeDirectory));

        var names = new List<string>();
        if (!string.IsNullOrEmpty(relativeDirectory))
            names.AddRange(relativeDirectory!.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries));
        names.Add(leafPath[0].Name);
        names.Add(leafPath[1].Name);
        var isHmi = leafPath[1].NodeType == VciScope.HmiSoftware;
        names.Add(VciScope.CategoryFor(leafPath[isHmi ? 1 : 2].NodeType));
        for (var i = isHmi ? 2 : 3; i < leafPath.Count - 1; i++) names.Add(leafPath[i].Name);
        var leafName = leafPath[leafPath.Count - 1].Name;
        return new VciFileKey(string.Join("\\", names), leafName) { MirroredSegments = names.Concat(new[] { leafName }).ToArray() };
    }

    public static string? FirstProblem(string rootPath, VciFileKey key)
    {
        var segments = key.MirroredSegments ?? key.Directory.TrimStart('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[] { key.FileName }).ToArray();
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)
                || IsReserved(segment))
                return "Name is not representable: " + segment;
        }
        var fullPath = Path.Combine(rootPath, key.Directory.TrimStart('\\'), key.FileName);
        return fullPath.Length + MaxExtensionLength > 259 ? "Mirrored path exceeds 259 characters: " + key.FileName : null;
    }

    private static bool IsReserved(string name)
    {
        var stem = name.Split('.')[0];
        return new[] { "CON", "PRN", "AUX", "NUL", "CLOCK$" }.Contains(stem, StringComparer.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] >= '1' && stem[3] <= '9');
    }
}
