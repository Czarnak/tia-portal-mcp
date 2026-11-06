using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Library;

public sealed class LibraryVerificationPolicy(IReadOnlyDictionary<string, LibraryWriteEffect> effects)
    : IWriteVerificationPolicy<LibraryWriteItem>
{
    public string ReadTool => "library_read";
    public bool HasReadBack(LibraryWriteItem item) => false;
    public Task<WriteOperationVerification> ReadBackAsync(LibraryWriteItem item, StructuredOperationFailure? failure)
        => throw new NotSupportedException();

    public IReadOnlyList<string> ObjectKeys(LibraryWriteItem item, bool asLater)
    {
        if (item.Operation == "instantiate_master_copy")
        {
            // shortcut: PLC container keys merge case aliases only; replace with resolved PLC evidence when device/software aliases or exact leaves are required.
            return [Key(["plc", item.PlcName!.ToUpperInvariant(), item.PlcKind!,
                CanonicalJson.Serialize((item.PlcFolderPath ?? []).Select(segment => segment.ToUpperInvariant()).ToArray())])];
        }

        var effect = effects[item.OperationId];
        var root = Root(effect.Library, item.Operation == "open_global" ? item.FilePath : null);
        if (root is null) return [];
        if (LibraryWriteMethods.LifecycleOperations.Contains(item.Operation, StringComparer.Ordinal)) return [Key(root)];
        if (effect.Target is not { } target) return [];
        var components = root.Concat(new[] { target.FolderKind! })
            .Concat(target.FolderPath.SelectMany(segment => new[] { "folder", segment }));
        if (target.Kind == "masterCopy") components = components.Concat(new[] { "masterCopy", target.Name.ToUpperInvariant() });
        return [Key(components)];
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
