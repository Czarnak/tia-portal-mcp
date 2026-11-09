using TiaMcpServer.Contracts.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Library;

public sealed record LibraryWriteEffect(LibraryIdentityInfo? Library, LibraryIdentityInfo? Target, bool? AlreadyOpen,
    IReadOnlyList<LibraryIdentityInfo> FolderContents, bool ContentsTruncated)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LibraryMetadataChangeInfo? MetadataChange { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public LibraryTypeResultInfo? Type { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? BlockingInstances { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? MasterCopiesContainingInstances { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Dependencies { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<string>? Notes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? CreatedByItem { get; init; }
}
public sealed record LibraryWriteEffectPresentation(string OperationId, LibraryWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record LibraryGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<LibraryWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission);
