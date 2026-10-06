using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Network;

/// <summary>One strictly decoded ordinary hardware read, or its closed failure category.</summary>
public sealed record NetworkStateSnapshot(bool Success, HardwareConfigInfo? State, string? FailureCategory, string? Error)
{
    public static NetworkStateSnapshot Ok(HardwareConfigInfo state) => new(true, state, null, null);
    public static NetworkStateSnapshot Fail(string failureCategory, string error) => new(false, null, failureCategory, error);
}

public sealed record NetworkWriteEffect(string Operation, NetworkWriteTargetEvidence Target,
    IReadOnlyDictionary<string, string> RequestedSettings, IReadOnlyDictionary<string, NetworkAttributeInfo> CurrentSettings,
    IReadOnlyList<NetworkNodeIdentityInfo> AffectedNodes, bool? ConnectionsComplete, int? RootDeviceCount);
public sealed record NetworkOperationVerification(string OperationId, string Operation, string Status,
    NetworkMutationVerificationInfo? Evidence, StructuredOperationOmission? Omission);
public sealed record NetworkWriteVerification(bool Success, IReadOnlyList<NetworkOperationVerification> Operations,
    IReadOnlyList<NetworkFinalCheck> FinalChecks, StructuredOperationOmission? Omission);

/// <summary>
/// One final-state check of <see cref="Field"/>. Kind device, node or subnet names a typed
/// <see cref="Subject"/> (a node's removedSubnet check also carries the removed subnetId);
/// kind operation names an <see cref="OperationId"/>; kind write concerns the whole call.
/// The factories are the single construction shared by the verifier and the payload budget.
/// </summary>
public sealed record NetworkFinalCheck(string Kind, string? OperationId, NetworkMutationIdentityInfo? Subject, string Field,
    string Status = "unverified", string? Expected = null, string? Observed = null, string? Message = null)
{
    public static NetworkFinalCheck Device(string deviceName, string? deviceItemName, string field)
        => new("device", null, new() { DeviceName = deviceName, DeviceItemName = deviceItemName }, field);
    public static NetworkFinalCheck Node(NetworkNodeIdentityInfo node, string field, string? removedSubnetId = null)
        => new("node", null, new() { DeviceName = node.DeviceName, NodeId = node.NodeId,
            InterfacePath = node.InterfacePath is null ? null : NetworkWritePlanner.ClonePath(node.InterfacePath),
            InterfaceName = node.InterfaceName, SubnetId = removedSubnetId }, field);
    public static NetworkFinalCheck Subnet(string subnetId, string field) => new("subnet", null, new() { SubnetId = subnetId }, field);
    public static NetworkFinalCheck Operation(string operationId, string field) => new("operation", operationId, null, field);
    public static NetworkFinalCheck Write(string field) => new("write", null, null, field);
}
public sealed record NetworkWriteEffectPresentation(string OperationId, NetworkWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record NetworkGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<NetworkWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, NetworkWriteVerification? Verification,
    StructuredOperationOmission? Omission = null);
