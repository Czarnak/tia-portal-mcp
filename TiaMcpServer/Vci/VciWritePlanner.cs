using System.Text.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Vci;

public sealed class VciWritePlanner(OpennessWorkerClient client)
{
    public async Task<VciPlanResult> PlanAsync(IReadOnlyList<VciOperationRequest> items)
    {
        var previous = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<ItemPlan<VciWriteEffect>>();
        var guards = new Dictionary<string, IReadOnlyList<FiredGuard>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (ProjectFolderError(item) is { } pathError) return Failed(pathError);
            if (previous.TryGetValue(item.WorkspaceName!, out var predecessor))
            {
                // The predecessor changes this workspace; inventory is unknown until its mutation finishes.
                plans.Add(ItemPlan<VciWriteEffect>.DependsOnItem(predecessor));
                guards[item.OperationId] = [];
            }
            else
            {
                var resolved = await ReplanAsync(item).ConfigureAwait(false);
                if (!resolved.Replan.Success) return Failed(resolved.Replan.Error!);
                plans.Add(resolved.Replan.Plan!); guards[item.OperationId] = resolved.Guards;
            }
            previous[item.WorkspaceName!] = item.OperationId;
        }
        return new(WritePlan<VciWriteEffect>.Ok(plans), guards);
    }

    public async Task<VciReplanResult> ReplanAsync(VciOperationRequest item)
    {
        if (ProjectFolderError(item) is { } error)
            return new(ItemReplan<VciWriteEffect>.Fail(error.Category, error.Message), []);
        var result = await client.WriteVciAsync(VciMethods.Plan,
            VciWriteCatalog.ToWorkerRequest(item, string.Empty, string.Empty)).ConfigureAwait(false);
        if (!result.Success)
            return new(ItemReplan<VciWriteEffect>.Fail(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                result.Error ?? "VCI planning failed."), []);
        try
        {
            var plan = CanonicalJson.DeserializeWorkerPayload<VciPlanInfo>(result.Payload);
            if (plan is null || plan.Leaves < 0 || plan.MappedLeaves < 0 || plan.MappedLeaves > plan.Leaves
                || plan.FormatFallbacks < 0 || plan.FormatFallbacks > plan.Leaves
                || plan.KeyCollisions is null || plan.Unrepresentable is null
                || plan.KeyCollisions.Any(string.IsNullOrWhiteSpace) || plan.Unrepresentable.Any(string.IsNullOrWhiteSpace))
                throw new JsonException();
            var effect = new VciWriteEffect(item.Operation, item.WorkspaceName!, item.Scope,
                plan.WorkspaceRootPath, plan.Leaves, plan.MappedLeaves, plan.FormatFallbacks);
            return new(ItemReplan<VciWriteEffect>.Ok(ItemPlan<VciWriteEffect>.Resolved(effect)), Guards(item, plan));
        }
        catch (JsonException)
        {
            return new(ItemReplan<VciWriteEffect>.Fail(WorkerFailureCategories.ProtocolError,
                "VCI planning evidence did not match its typed payload contract."), []);
        }
    }

    private WriteToolError? ProjectFolderError(VciOperationRequest item)
    {
        var boundPath = client.BindingSnapshot.ProjectPath;
        return item.Operation == "create_workspace" && item.RootPath is not null && boundPath is not null
            && ArchiveDirectoryGuard.IsWithinProjectFolder(item.RootPath, boundPath)
            ? new(WorkerFailureCategories.ValidationError, "rootPath must not be inside the bound project's folder.") : null;
    }

    private static IReadOnlyList<FiredGuard> Guards(VciOperationRequest item, VciPlanInfo plan)
    {
        var guards = new List<FiredGuard>();
        void Fire(string id, string message) => guards.Add(new(id, item.OperationId, message));
        if (plan.KeyCollisions.Count > 0) Fire(VciGuardDefinitions.FileKeyCollision, "File key collisions: " + string.Join("; ", plan.KeyCollisions.Take(10)));
        if (plan.Unrepresentable.Count > 0) Fire(VciGuardDefinitions.NameNotRepresentable, "Unrepresentable leaves: " + string.Join("; ", plan.Unrepresentable.Take(10)));
        if (plan.WorkspaceConflict is not null) Fire(VciGuardDefinitions.WorkspaceExists, plan.WorkspaceConflict);
        if (plan.Unverifiable is not null) Fire(VciGuardDefinitions.StateUnverifiable, plan.Unverifiable);
        if (item.Operation is "delete_workspace" or "delete_mapping") Fire(VciGuardDefinitions.FilesRemain, VciNotes.FilesRemain);
        if (item.Operation == "sync_to_project") Fire(VciGuardDefinitions.ImportUnvalidated, VciNotes.ImportedNotValidated);
        return guards;
    }

    private static VciPlanResult Failed(WriteToolError error)
        => new(WritePlan<VciWriteEffect>.Fail(error.Category, error.Message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
}
