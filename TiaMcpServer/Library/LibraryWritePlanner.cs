using System.Text.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Library;

public sealed record LibraryPlanResult(WritePlan<LibraryWriteEffect> Plan,
    IReadOnlyDictionary<string, IReadOnlyList<FiredGuard>> Guards);

public sealed class LibraryWritePlanner(OpennessWorkerClient client)
{
    public async Task<LibraryPlanResult> PlanAsync(IReadOnlyList<LibraryWriteItem> items)
    {
        var plans = new List<ItemPlan<LibraryWriteEffect>>();
        var guards = new Dictionary<string, IReadOnlyList<FiredGuard>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var request = LibraryWriteCatalog.ToWorkerRequest(item);
            var result = await client.WriteLibraryAsync(LibraryWriteMethods.PlanFor(request), request).ConfigureAwait(false);
            if (!result.Success)
                return Failed(result.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    result.Error ?? "Library planning failed.");
            try
            {
                var plan = CanonicalJson.DeserializeWorkerPayload<LibraryWritePlanInfo>(result.Payload);
                if (plan.FolderContents.Any(identity => identity is null)) throw new JsonException();
                plans.Add(ItemPlan<LibraryWriteEffect>.Resolved(new(plan.Library, plan.Target, plan.AlreadyOpen,
                    plan.FolderContents, plan.ContentsTruncated)));
                guards[item.OperationId] = Guards(item, plan);
            }
            catch (JsonException)
            {
                return Failed(WorkerFailureCategories.ProtocolError,
                    "Library planning evidence did not match its typed payload contract.");
            }
        }
        return new(WritePlan<LibraryWriteEffect>.Ok(plans), guards);
    }

    private static IReadOnlyList<FiredGuard> Guards(LibraryWriteItem item, LibraryWritePlanInfo plan)
    {
        var guards = new List<FiredGuard>();
        void Fire(string id, string message) => guards.Add(new(id, item.OperationId, message));
        if (plan.MasterCopyExists) Fire(LibraryGuardDefinitions.MasterCopyExists, "A master copy with this name already exists in the destination folder.");
        if (plan.FolderContents.Count > 0) Fire(LibraryGuardDefinitions.DeletesFolderContents, "Deleting this folder also deletes its contents.");
        if (item.Operation == "close_global" && item.CloseAction == "discard")
            Fire(LibraryGuardDefinitions.DiscardsChanges, "Closing without saving discards any unsaved library changes.");
        if (plan.Unverifiable is not null) Fire(LibraryGuardDefinitions.StateUnverifiable, plan.Unverifiable);
        return guards;
    }

    private static LibraryPlanResult Failed(string category, string message)
        => new(WritePlan<LibraryWriteEffect>.Fail(category, message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
}
