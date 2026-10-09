using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Project;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>The exact source snapshot prepared for a guarded write's one pinned lease.</summary>
public sealed record WriteBindingPreparation(
    bool Success, ProjectBindingSnapshot? Binding, WriteToolError? Error)
{
    public static WriteBindingPreparation Prepared(ProjectBindingSnapshot binding) => new(true, binding, null);

    public static WriteBindingPreparation Rejected(WriteToolError error) => new(false, null, error);
}

/// <summary>
/// Server-selected preparation policy. Only a lifecycle tool supplies a lifecycle strategy;
/// ordinary writes keep the verified current-project gate.
/// </summary>
public interface IWriteBindingStrategy<TItem> where TItem : IOperationBatchItem
{
    Task<WriteBindingPreparation> PrepareAsync(WriteCall<TItem> call, CancellationToken cancellationToken = default);
}
