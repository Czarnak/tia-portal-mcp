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
    IReadOnlyList<NetworkVerificationCheckInfo> FinalChecks, StructuredOperationOmission? Omission);
public sealed record NetworkWriteEffectPresentation(string OperationId, NetworkWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record NetworkGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<NetworkWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, NetworkWriteVerification? Verification,
    StructuredOperationOmission? Omission = null);
