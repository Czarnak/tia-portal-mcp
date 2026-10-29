using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Safety.Pipeline;

public static class WriteVerificationChecks
{
    public const string Trusted = "trusted";
    public const string Superseded = "superseded";
}

public sealed record WriteOperationVerification(string OperationId, bool Success, string Check,
    string? Expected, string? Actual, string? Message);

public sealed record WriteVerification(bool Success, IReadOnlyList<WriteOperationVerification> Operations,
    StructuredOperationOmission? Omission);

public interface IWriteVerificationPolicy<TItem> where TItem : IOperationBatchItem
{
    /// <summary>Public read tool named in the "may have changed" message, e.g. "plc_read".</summary>
    string ReadTool { get; }
    /// <summary>Normalized keys the item owns (asLater:false) or offers to later items (asLater:true); newline-joined segments, container prefixes member.</summary>
    IReadOnlyList<string> ObjectKeys(TItem item, bool asLater);
    bool HasReadBack(TItem item);
    Task<WriteOperationVerification> ReadBackAsync(TItem item, StructuredOperationFailure? failure);
}
