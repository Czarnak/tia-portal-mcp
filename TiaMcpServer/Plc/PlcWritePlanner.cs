using TiaMcpServer.Json;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Plc;

/// <summary>
/// Plans plc_write items from public reads only, each at most once per call: the tag inventory
/// (which also resolves the PLC), the project tree of a PLC only when one of its items needs it
/// (<see cref="PlcWorkingState.NeedsTree"/>) and one export per content item. Earlier items are
/// applied to a working state so later items see them and carry <c>DependsOn</c>; only those
/// dependent items are re-planned before their mutation. The shared runner owns sequencing and mutation.
/// </summary>
public sealed class PlcWritePlanner(OpennessWorkerClient client)
{
    internal sealed record Evidence(PlcTagInventoryInfo? Inventory, WriteToolError? Error);

    private sealed record ItemOutcome(PlcWorkingState? State, PlcWriteEffect? Effect, IReadOnlyList<FiredGuard> Guards,
        IReadOnlyList<CheckedPrecondition> Preconditions, WriteToolError? Error);

    /// <summary>The planning reads of one call: the inventory, each PLC's working state and which PLCs need a tree.</summary>
    private sealed class CallReads(PlcTagInventoryInfo inventory, IReadOnlyList<PlcOperationRequest> items)
    {
        public PlcTagInventoryInfo Inventory { get; } = inventory;

        public Dictionary<PlcTagInventoryPlcInfo, PlcWorkingState> States { get; } = new(ReferenceEqualityComparer.Instance);

        public HashSet<PlcTagInventoryPlcInfo> TreePlcs { get; } = new(items.Where(PlcWorkingState.NeedsTree)
            .Select(item => MatchPlc(item, inventory).Plc).OfType<PlcTagInventoryPlcInfo>(), ReferenceEqualityComparer.Instance);
    }

    public async Task<PlanResult<PlcWriteEffect>> PlanAsync(IReadOnlyList<PlcOperationRequest> items)
    {
        var evidence = await ReadInventoryAsync().ConfigureAwait(false);
        if (evidence.Error is { } inventoryError) return Failed(inventoryError);
        var reads = new CallReads(evidence.Inventory!, items);
        // One budget per call keeps every diff of the call inside the per-call excerpt bound.
        var budget = new PlcContentDiffBudget();
        var plans = new List<ItemPlan<PlcWriteEffect>>();
        var guards = new Dictionary<string, IReadOnlyList<FiredGuard>>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            var outcome = await ResolveAsync(item, reads, budget).ConfigureAwait(false);
            if (outcome.Error is { } error) return Failed(error);
            var effect = outcome.Effect ?? throw new InvalidOperationException($"Operation '{item.OperationId}' resolved without an effect or an error.");
            var dependsOn = outcome.State?.LastTouchedBy(effect.Target);
            outcome.State?.Apply(item);
            // The effect stays known: the pipeline drops plans with a null effect.
            plans.Add(ItemPlan<PlcWriteEffect>.Resolved(effect with { DependsOn = dependsOn }, outcome.Preconditions) with { DependsOn = dependsOn });
            guards[item.OperationId] = outcome.Guards;
        }

        return new PlanResult<PlcWriteEffect>(WritePlan<PlcWriteEffect>.Ok(plans), guards);
    }

    /// <summary>Re-reads real state for one dependent item just before its own mutation, with that item's read set.</summary>
    public async Task<ReplanResult<PlcWriteEffect>> ReplanAsync(PlcOperationRequest item)
    {
        var evidence = await ReadInventoryAsync().ConfigureAwait(false);
        var outcome = evidence.Error is { } inventoryError
            ? new ItemOutcome(null, null, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), inventoryError)
            : await ResolveAsync(item, new CallReads(evidence.Inventory!, new[] { item }), new PlcContentDiffBudget()).ConfigureAwait(false);
        return outcome.Error is { } error
            ? new ReplanResult<PlcWriteEffect>(ItemReplan<PlcWriteEffect>.Fail(error.Category, error.Message), Array.Empty<FiredGuard>())
            : new ReplanResult<PlcWriteEffect>(ItemReplan<PlcWriteEffect>.Ok(ItemPlan<PlcWriteEffect>.Resolved(outcome.Effect!, outcome.Preconditions)), outcome.Guards);
    }

    private async Task<ItemOutcome> ResolveAsync(PlcOperationRequest item, CallReads reads, PlcContentDiffBudget budget)
    {
        var inventory = reads.Inventory;
        var (plc, matchError) = MatchPlc(item, inventory);
        if (matchError is { Category: WorkerFailureCategories.TargetNotFound } && !inventory.IsComplete)
        {
            // The PLC may be one whose discovery failed: unverifiable, never absent.
            var hidden = new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                $"{matchError.Message} The tag inventory is incomplete, so the PLC may exist but cannot be verified.");
            return new ItemOutcome(null, PlcWorkingState.Unresolved(item, PlcSelector(item) ?? string.Empty, null), new[] { hidden },
                Array.Empty<CheckedPrecondition>(), null);
        }

        if (matchError is not null) return Fail(matchError);
        if (!reads.States.TryGetValue(plc!, out var state))
        {
            ProjectTreeObservation? tree = null;
            if (reads.TreePlcs.Contains(plc!))
            {
                var (read, treeError) = await ReadTreeAsync(plc!).ConfigureAwait(false);
                if (treeError is not null) return Fail(treeError);
                tree = read;
            }

            reads.States[plc!] = state = PlcWorkingState.Create(plc!, inventory.IsComplete, tree);
        }

        var resolution = state.Resolve(item);
        if (resolution.Error is not null) return Fail(resolution.Error);
        var effect = resolution.Effect!;
        if (item.Operation is not ("update_block_logic" or "update_type_content"))
            return new ItemOutcome(state, effect, resolution.Guards, Array.Empty<CheckedPrecondition>(), null);
        return await ResolveContentAsync(item, state, effect, resolution.Guards, budget).ConfigureAwait(false);
    }

    /// <summary>Exports the target in the write's format: the hash check and the diff evidence.</summary>
    private async Task<ItemOutcome> ResolveContentAsync(PlcOperationRequest item, PlcWorkingState state,
        PlcWriteEffect effect, IReadOnlyList<FiredGuard> guards, PlcContentDiffBudget budget)
    {
        var format = PlcFormatNames.Normalize(item.Operation, item.Format);
        var export = item.Operation == "update_block_logic"
            ? await client.GetBlockContentAsync(item.BlockPath!, format: format).ConfigureAwait(false)
            : await client.GetTypeContentAsync(item.TypePath!, format: format).ConfigureAwait(false);
        if (!export.Success && export.FailureCategory is WorkerFailureCategories.TargetNotFound or WorkerFailureCategories.TargetAmbiguous)
            return Fail(new(export.FailureCategory!, $"Operation '{item.OperationId}': {export.Error}"));
        if (!export.Success || string.IsNullOrEmpty(export.Payload))
        {
            // Unreadable content is never hashed: an empty export is not "unchanged" evidence.
            var unverifiable = new FiredGuard(PlcGuardDefinitions.StateUnverifiable, item.OperationId,
                $"Operation '{item.OperationId}': the current content could not be exported ({(export.Success ? "the export was empty" : export.Error)}), so expectedContentHash cannot be checked.");
            return new ItemOutcome(state, effect, guards.Append(unverifiable).ToArray(), Array.Empty<CheckedPrecondition>(), null);
        }

        var check = ContentHashes.Check(item.ExpectedContentHash, format, export.Payload);
        if (!check.Success) return Fail(new(check.ErrorCategory!, $"Operation '{item.OperationId}': {check.Message}"));
        return new ItemOutcome(state, effect with { ContentDiff = PlcContentDiff.Build(export.Payload, item.Content!, budget) },
            guards, new[] { check.Evidence! }, null);
    }

    /// <summary>D5: the selector matches a software or device name among every PLC, case-insensitively.</summary>
    private static (PlcTagInventoryPlcInfo? Plc, WriteToolError? Error) MatchPlc(PlcOperationRequest item, PlcTagInventoryInfo inventory)
    {
        string? selector;
        try
        {
            selector = PlcSelector(item);
        }
        catch (ArgumentException ex)
        {
            return (null, new(WorkerFailureCategories.ValidationError, $"Operation '{item.OperationId}': {ex.Message}"));
        }

        var matches = inventory.Plcs.Where(plc => selector is null
            || PlcNameRules.Comparer.Equals(plc.PlcName, selector) || PlcNameRules.Comparer.Equals(plc.DeviceName, selector)).ToArray();
        var described = selector is null ? "An omitted PLC name" : $"PLC '{selector}'";
        return matches.Length switch
        {
            1 => (matches[0], null),
            0 => (null, new(WorkerFailureCategories.TargetNotFound, $"Operation '{item.OperationId}': {described} matches no PLC in the project.")),
            _ => (null, new(WorkerFailureCategories.TargetAmbiguous,
                $"Operation '{item.OperationId}': {described} matches {matches.Length} PLCs ({string.Join(", ", matches.Select(m => $"'{m.PlcName}' on device '{m.DeviceName}'"))}). Name the PLC software uniquely.")),
        };
    }

    /// <exception cref="ArgumentException">The block or type path is malformed.</exception>
    private static string? PlcSelector(PlcOperationRequest item)
        => item.BlockPath is not null ? BlockAddress.Parse(item.BlockPath).PlcName
            : item.TypePath is not null ? PlcTypeAddress.Parse(item.TypePath).PlcName
            : item.PlcName;

    internal async Task<Evidence> ReadInventoryAsync()
    {
        var request = new PlcOperationRequest { OperationId = "plan", Operation = "list_tag_tables" };
        var projected = PlcPayloadContract.Project(request, await client.ListTagTablesAsync(plcName: null).ConfigureAwait(false));
        return projected.Result is { } result
            ? new Evidence(CanonicalJson.Deserialize<PlcTagInventoryInfo>(result.GetRawText()), null)
            : new Evidence(null, new(projected.Failure?.Category ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The PLC tag inventory could not be read: {projected.Failure?.Message}"));
    }

    internal async Task<(ProjectTreeObservation? Tree, WriteToolError? Error)> ReadTreeAsync(PlcTagInventoryPlcInfo plc)
    {
        if (string.IsNullOrWhiteSpace(plc.DeviceName))
            return (null, new(WorkerFailureCategories.WorkerOperationFailed, $"The device of PLC '{plc.PlcName}' could not be identified, so its project tree cannot be read."));
        var selector = new[]
        {
            new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.Device, Name = plc.DeviceName },
            new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.PlcSoftware, Name = plc.PlcName },
        };
        var call = await client.BrowseProjectTreeV3SnapshotAsync(startSelector: selector).ConfigureAwait(false);
        if (!call.WorkerResult.Success)
            return (null, new(call.WorkerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                $"The project tree of PLC '{plc.PlcName}' could not be read: {call.WorkerResult.Error}"));
        try
        {
            return (ProjectTreeWorkerPayloadContract.Decode(call.WorkerResult, selector, null), null);
        }
        catch (ProjectTreeProtocolException ex)
        {
            return (null, new(ex.Category, ex.Message));
        }
    }

    private static ItemOutcome Fail(WriteToolError error)
        => new(null, null, Array.Empty<FiredGuard>(), Array.Empty<CheckedPrecondition>(), error);

    private static PlanResult<PlcWriteEffect> Failed(WriteToolError error)
        => new(WritePlan<PlcWriteEffect>.Fail(error.Category, error.Message), new Dictionary<string, IReadOnlyList<FiredGuard>>());
}
