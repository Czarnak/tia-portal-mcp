using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>Prepares lifecycle source identity before the pipeline acquires its one pinned lease.</summary>
public sealed class LifecycleBindingStrategy(OpennessWorkerClient workerClient) : IWriteBindingStrategy<LifecycleWriteItem>
{
    public async Task<WriteBindingPreparation> PrepareAsync(
        WriteCall<LifecycleWriteItem> call, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var item = call.Items.Single();
        var denial = workerClient.AccessPolicy.Authorize(item.Operation);
        if (denial is not null) return Reject(denial);

        if (item.Operation == "open_project")
        {
            var binding = workerClient.BindingSnapshot;
            var check = workerClient.CheckOpenProjectBinding(item.ProjectPath!, item.ForceRebind);
            if (!check.Success) return Reject(check);
            if (!binding.SameBinding(workerClient.BindingSnapshot))
                return WriteBindingPreparation.Rejected(new(WorkerFailureCategories.BindingConflict,
                    "The lifecycle source binding changed during preparation."));
            if (binding.State == ProjectBindingSnapshot.InvalidatedState)
            {
                var recovery = await workerClient.RegroundInvalidatedSourceForOpenAsync(item.ForceRebind, binding)
                    .ConfigureAwait(false);
                if (!recovery.Success) return Reject(recovery.Failure!);
                // Preserve exactly the recovery revision; never recapture a competing binding.
                return WriteBindingPreparation.Prepared(recovery.Value!);
            }
            if (binding.State == ProjectBindingSnapshot.UnboundState)
            {
                var empty = await workerClient.RevalidateRecoveredEmptyPortalAsync(binding).ConfigureAwait(false);
                if (!empty.Success) return Reject(empty);
                return WriteBindingPreparation.Prepared(binding);
            }
            return await PrepareVerifiedSourceAsync(binding, binding.ProjectPath, cancellationToken).ConfigureAwait(false);
        }

        var before = workerClient.BindingSnapshot;
        if (item.Operation == "create_project" && before.State == ProjectBindingSnapshot.UnboundState)
            return WriteBindingPreparation.Prepared(before);

        return await PrepareVerifiedSourceAsync(before, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WriteBindingPreparation> PrepareVerifiedSourceAsync(
        ProjectBindingSnapshot before, string? projectPath, CancellationToken cancellationToken)
    {
        var result = await workerClient.RequireVerifiedBindingAsync(projectPath).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Success) return Reject(result);
        var prepared = workerClient.BindingSnapshot;
        return WriteBindingPreparation.IsConsistent(before, prepared) ? WriteBindingPreparation.Prepared(prepared)
            : WriteBindingPreparation.Rejected(new(WorkerFailureCategories.BindingConflict,
                "The source binding changed while lifecycle preparation was being checked. Inspect project status before retrying."));
    }

    private static WriteBindingPreparation Reject(WorkerCallResult result)
        => WriteBindingPreparation.Rejected(new(result.FailureCategory ?? WorkerFailureCategories.BindingConflict,
            result.Error ?? "The lifecycle source binding could not be prepared."));
}
