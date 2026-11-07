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
        var root = Root(effect?.Library, item.Operation == "open_global" ? item.FilePath : null);
        IReadOnlyList<string[]> roots = root is not null ? [root] : asLater ? SelectorRoots(item) : [];
        return roots.Select(value => ObjectKey(item, effect, value, asLater)).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string? ObjectKey(LibraryWriteItem item, LibraryWriteEffect? effect, string[] root, bool asLater)
    {
        if (effect is null) return Key(root);
        if (LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal)) return Key(root);
        if (effect.Type is { } type)
        {
            var typeRoot = root.Concat(new[] { "types" }).Concat(type.FolderPath.SelectMany(segment => new[] { "folder", segment }))
                .Concat(new[] { "type", type.TypeGuid.ToUpperInvariant() });
            var affectsType = asLater && (type.TypeDeleted == true || type.ReplacedInWork == true
                || item.Operation == "delete_type_version" && type.TypeDeleted is null
                || item.Operation == "create_type_version" && type.ReplacedInWork is null);
            return Key(!affectsType && type.VersionGuid is { } guid && item.Operation is "create_type_version" or "delete_type_version"
                ? typeRoot.Concat(new[] { "version", guid.ToUpperInvariant() }) : typeRoot);
        }
        if (effect.Target is not { } target) return asLater ? Key(root) : null;
        var components = root.Concat(new[] { target.FolderKind! })
            .Concat(target.FolderPath.SelectMany(segment => new[] { "folder", segment }));
        if (target.Kind == "masterCopy") components = components.Concat(new[] { "masterCopy", target.Name.ToUpperInvariant() });
        return Key(components);
    }

    private IReadOnlyList<string[]> SelectorRoots(LibraryWriteItem item)
    {
        if (item.Scope == "project" || item.Operation is "create_type" or "create_type_version") return [["library", "project"]];
        if (item.LibraryPath is { } path) return Root(null, path) is { } exact ? [exact] : [];
        if (item.LibraryName is not { } name) return [];
        var roots = new List<string[]>();
        foreach (var entry in effects)
        {
            var library = entry.Value.Library;
            if (library?.Scope == "global" && (library.LibraryName == name || library.Name == name
                || items?.Any(i => i.OperationId == entry.Key && i.LibraryName == name) == true))
                if (Root(library, null) is { } root) roots.Add(root);
        }
        return roots.Count > 0 ? roots : [["library", "globalName", name]];
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
