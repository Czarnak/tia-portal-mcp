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
                LibraryWritePayloadContract.ValidateIdentity(plan.Library);
                LibraryWritePayloadContract.ValidateIdentity(plan.Target);
                foreach (var identity in plan.FolderContents) LibraryWritePayloadContract.ValidateIdentity(identity);
                LibraryWritePayloadContract.ValidateType(plan.Type);
                foreach (var values in new[] { plan.BlockingInstances, plan.MasterCopiesContainingInstances, plan.Dependencies, plan.Notes, plan.GuardIds })
                    if (values?.Any(value => value is null) == true) throw new JsonException();
                int? createdBy = null;
                foreach (var missing in plan.MissingTargets ?? [])
                {
                    if (missing is null) throw new JsonException();
                    LibraryWritePayloadContract.ValidateIdentity(missing);
                    var producer = Enumerable.Range(0, plans.Count).LastOrDefault(index => Produces(items[index], plans[index].Effect!, item, missing), -1);
                    if (producer < 0) return Failed(WorkerFailureCategories.TargetNotFound, "A required library target does not exist and is not created by an earlier item.");
                    createdBy = Math.Max(createdBy ?? 0, producer + 1);
                }
                plans.Add(ItemPlan<LibraryWriteEffect>.Resolved(new(plan.Library, plan.Target, plan.AlreadyOpen,
                    plan.FolderContents, plan.ContentsTruncated)
                { Type = plan.Type, BlockingInstances = plan.BlockingInstances, Dependencies = plan.Dependencies,
                    MasterCopiesContainingInstances = plan.MasterCopiesContainingInstances, Notes = plan.Notes, CreatedByItem = createdBy }));
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
        foreach (var id in plan.GuardIds ?? [])
        {
            var definition = LibraryGuardDefinitions.Definitions.SingleOrDefault(value => value.Id == id);
            if (definition is null) throw new JsonException();
            Fire(id, definition.Description);
        }
        return guards;
    }

    private static bool Produces(LibraryWriteItem producer, LibraryWriteEffect effect, LibraryWriteItem consumer, LibraryIdentityInfo missing)
    {
        var kind = producer.Operation switch
        { "create_folder" => "folder", "create_master_copy" or "copy_master_copy" => "masterCopy", "create_type" or "synchronize_type" => "type", _ => null };
        if (kind is null || kind != missing.Kind || effect.Library is not { } library || library.Scope != missing.Scope) return false;
        if (library.Scope != "project" && !string.Equals(library.LibraryPath, missing.LibraryPath, StringComparison.OrdinalIgnoreCase)) return false;
        var name = effect.Target?.Name ?? effect.Type?.TypeName;
        var path = effect.Target?.FolderPath ?? effect.Type?.FolderPath;
        if (name is null || path is null) return false;
        if (kind == "type" && missing.TypeGuid is not null)
            return string.Equals(effect.Type?.TypeGuid ?? effect.Target?.TypeGuid, missing.TypeGuid, StringComparison.OrdinalIgnoreCase);
        return string.Equals(name, missing.Name, StringComparison.Ordinal)
            && (kind == "type" && consumer.FolderPath is null || path.SequenceEqual(missing.FolderPath, StringComparer.Ordinal))
            && (kind != "folder" || effect.Target?.FolderKind == missing.FolderKind);
    }

    private static LibraryPlanResult Failed(string category, string message)
        => new(WritePlan<LibraryWriteEffect>.Fail(category, message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
}
