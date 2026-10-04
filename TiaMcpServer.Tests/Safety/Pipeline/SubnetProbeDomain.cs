using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Safety.Pipeline;

/// <summary>What deleting one subnet does: its identity and how many nodes it still connects.</summary>
public sealed record SubnetDeleteEffect(string SubnetId, string Name, int ConnectedNodeCount);

/// <summary>The post-write subnet count, next to the count the plan read.</summary>
public sealed record SubnetCountVerification(int SubnetCountBefore, int SubnetCountAfter);

public sealed record SubnetProbeResponse(
    string Tool,
    string ContractVersion,
    string Phase,
    bool Success,
    WriteToolError? Error,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<WriteEffect<SubnetDeleteEffect>> Effects,
    StructuredOperationBatch? Batch,
    SubnetCountVerification? Verification);

/// <summary>
/// A test-only write domain over the existing network seams: <c>delete_subnet</c> planned from
/// <see cref="NetworkWritePlanner.ReadCurrentStateAsync"/> by exact <c>subnetId</c>, mutated through
/// <see cref="NetworkWorkerInvoker.InvokeWriteAsync"/>, projected by
/// <see cref="NetworkPayloadContract.Project(NetworkOperationRequest, WorkerCallResult)"/>, and
/// verified by the subnet count. Deleting a connected subnet fires an acknowledge guard.
/// </summary>
public sealed class SubnetProbeDomain
    : IWriteDomain<NetworkOperationRequest, SubnetDeleteEffect, SubnetCountVerification, SubnetProbeResponse>
{
    public const string ConnectedSubnetGuard = "test_deletes_connected_subnet";
    private const string DeleteSubnetOperation = "delete_subnet";

    public static readonly WriteGuardCatalog Catalog = new(new[]
    {
        new WriteGuardDefinition(
            ConnectedSubnetGuard,
            WriteGuardSeverities.Acknowledge,
            "Deleting a subnet that still connects nodes disconnects them."),
    });

    private readonly OpennessWorkerClient _client;
    private int? _subnetCountBefore;

    public SubnetProbeDomain(OpennessWorkerClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public string ToolName => "subnet_probe";

    public string ContractVersion => "subnet-probe/1";

    public static NetworkOperationRequest DeleteSubnet(string operationId, string subnetId, string projectPath) => new()
    {
        OperationId = operationId,
        Operation = DeleteSubnetOperation,
        ProjectPath = projectPath,
        Target = new NetworkObjectTarget { Kind = NetworkObjectKinds.Subnet, SubnetId = subnetId },
    };

    public WriteValidation Validate(IReadOnlyList<NetworkOperationRequest> items, McpAccessMode accessMode)
    {
        if (accessMode is not (McpAccessMode.ReadWrite or McpAccessMode.Full))
        {
            return WriteValidation.Invalid(WorkerFailureCategories.ValidationError, "The session is read-only.");
        }

        var invalid = items.FirstOrDefault(item =>
            item.Operation != DeleteSubnetOperation || string.IsNullOrWhiteSpace(item.Target?.SubnetId));
        return invalid is null
            ? WriteValidation.Valid()
            : WriteValidation.Invalid(
                WorkerFailureCategories.ValidationError,
                $"Operation '{invalid.OperationId}' must be delete_subnet with a target subnetId.");
    }

    public async Task<WritePlan<SubnetDeleteEffect>> PlanAsync(
        string? projectPath,
        IReadOnlyList<NetworkOperationRequest> items)
    {
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(_client, projectPath).ConfigureAwait(false);
        if (!snapshot.Success)
        {
            return WritePlan<SubnetDeleteEffect>.Fail(snapshot.FailureCategory!, snapshot.Error!);
        }

        var subnets = snapshot.State!.Subnets;
        _subnetCountBefore = subnets.Count;
        var plans = new List<ItemPlan<SubnetDeleteEffect>>(items.Count);
        foreach (var item in items)
        {
            var subnetId = item.Target!.SubnetId!;
            var subnet = subnets.FirstOrDefault(s => string.Equals(s.SubnetId, subnetId, StringComparison.Ordinal));
            if (subnet is null)
            {
                return WritePlan<SubnetDeleteEffect>.Fail(
                    WorkerFailureCategories.TargetNotFound, $"No subnet has subnetId '{subnetId}'.");
            }

            plans.Add(ItemPlan<SubnetDeleteEffect>.Resolved(
                new SubnetDeleteEffect(subnet.SubnetId, subnet.Name, subnet.ConnectedNodeNames.Count),
                new[] { new CheckedPrecondition("subnetExists", subnetId, subnet.SubnetId, Satisfied: true) }));
        }

        return WritePlan<SubnetDeleteEffect>.Ok(plans);
    }

    public IReadOnlyList<FiredGuard> EvaluateGuards(
        IReadOnlyList<NetworkOperationRequest> items,
        IReadOnlyList<ItemPlan<SubnetDeleteEffect>> plans)
        => items
            .Select((item, index) => (item, effect: plans[index].Effect))
            .Where(pair => pair.effect is { ConnectedNodeCount: > 0 })
            .Select(pair => new FiredGuard(
                ConnectedSubnetGuard,
                pair.item.OperationId,
                $"Subnet '{pair.effect!.SubnetId}' still connects {pair.effect.ConnectedNodeCount} node(s)."))
            .ToArray();

    /// <summary>Never called: this domain plans no dependent items.</summary>
    public Task<ItemReplan<SubnetDeleteEffect>> ReplanAsync(string? projectPath, NetworkOperationRequest item)
        => throw new InvalidOperationException("SubnetProbeDomain plans no dependent items.");

    public Task<WorkerCallResult> MutateAsync(string? projectPath, NetworkOperationRequest item)
        => NetworkWorkerInvoker.InvokeWriteAsync(_client, item, projectPath);

    public StructuredOperationItem Project(NetworkOperationRequest item, WorkerCallResult result)
        => NetworkPayloadContract.Project(item, result);

    public async Task<SubnetCountVerification?> VerifyAsync(string? projectPath, StructuredOperationBatch batch)
    {
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(_client, projectPath).ConfigureAwait(false);
        return snapshot.Success && _subnetCountBefore is { } before
            ? new SubnetCountVerification(before, snapshot.State!.Subnets.Count)
            : null;
    }

    public SubnetProbeResponse Compose(WriteReport<SubnetDeleteEffect, SubnetCountVerification> report)
        => new(
            ToolName, ContractVersion, report.Phase, report.Success, report.Error, report.Warnings,
            report.Guards, report.Effects, report.Batch, report.Verification);
}
