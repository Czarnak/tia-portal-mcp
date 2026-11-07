using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Library;

public sealed class LibraryVerificationPolicy(IReadOnlyDictionary<string, LibraryWriteEffect> effects, IReadOnlyList<LibraryWriteItem>? items = null)
    : IWriteVerificationPolicy<LibraryWriteItem>
{
    public string ReadTool => "library_read for library objects or plc_read for PLC destinations";
    public bool HasReadBack(LibraryWriteItem item) => false;
    public Task<WriteOperationVerification> ReadBackAsync(LibraryWriteItem item, StructuredOperationFailure? failure)
        => throw new NotSupportedException();

    public IReadOnlyList<string> ObjectKeys(LibraryWriteItem item, bool asLater)
    {
        if (item.Operation is "instantiate_master_copy" or "instantiate_type_version")
        {
            // shortcut: PLC container keys merge case aliases only; replace with resolved PLC evidence when device/software aliases or exact leaves are required.
            return [Key(["plc", item.PlcName!.ToUpperInvariant(), item.PlcKind ?? "typeInstance",
                CanonicalJson.Serialize((item.PlcFolderPath ?? []).Select(segment => segment.ToUpperInvariant()).ToArray())])];
        }

        effects.TryGetValue(item.OperationId, out var effect);
        var root = Root(effect?.Library, item.Operation == "open_global" ? item.FilePath : null)
            ?? (asLater ? SelectorRoot(item) : null);
        if (root is null) return [];
        if (effect is null) return [Key(root)];
        if (LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal)) return [Key(root)];
        if (effect.Type is { } type)
        {
            var typeRoot = root.Concat(new[] { "types" }).Concat(type.FolderPath.SelectMany(segment => new[] { "folder", segment }))
                .Concat(new[] { "type", type.TypeGuid.ToUpperInvariant() });
            return [Key(type.VersionGuid is { } guid && item.Operation is "create_type_version" or "delete_type_version"
                ? typeRoot.Concat(new[] { "version", guid.ToUpperInvariant() }) : typeRoot)];
        }
        if (effect.Target is not { } target) return asLater ? [Key(root)] : [];
        var components = root.Concat(new[] { target.FolderKind! })
            .Concat(target.FolderPath.SelectMany(segment => new[] { "folder", segment }));
        if (target.Kind == "masterCopy") components = components.Concat(new[] { "masterCopy", target.Name.ToUpperInvariant() });
        return [Key(components)];
    }

    private string[]? SelectorRoot(LibraryWriteItem item)
    {
        if (item.Scope == "project" || item.Operation is "create_type" or "create_type_version") return ["library", "project"];
        if (item.LibraryPath is { } path) return Root(null, path);
        if (item.LibraryName is not { } name) return null;
        foreach (var entry in effects)
        {
            var library = entry.Value.Library;
            if (library?.Scope == "global" && (library.LibraryName == name || library.Name == name
                || items?.Any(i => i.OperationId == entry.Key && i.LibraryName == name) == true))
                return Root(library, null);
        }
        return ["library", "globalName", name];
    }

    private static string[]? Root(LibraryIdentityInfo? library, string? openPath)
    {
        if (library?.Scope == "project") return ["library", "project"];
        var path = library?.LibraryPath ?? openPath;
        return string.IsNullOrWhiteSpace(path) ? null : ["library", "global", Path.GetFullPath(path).ToUpperInvariant()];
    }

    // Encode components so literal separators cannot manufacture parent/descendant ownership.
    private static string Key(IEnumerable<string> components) => string.Join("\n", components.Select(CanonicalJson.Serialize));
}
