using TiaMcpServer.Contracts.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Library;

public sealed record LibraryWriteEffect(LibraryIdentityInfo? Library, LibraryIdentityInfo? Target, bool? AlreadyOpen,
    IReadOnlyList<LibraryIdentityInfo> FolderContents, bool ContentsTruncated);
public sealed record LibraryWriteEffectPresentation(string OperationId, LibraryWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record LibraryGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<LibraryWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission);
