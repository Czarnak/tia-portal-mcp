using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.Network;

/// <summary>One strictly decoded ordinary hardware read, or its closed failure category.</summary>
public sealed record NetworkStateSnapshot(bool Success, HardwareConfigInfo? State, string? FailureCategory, string? Error)
{
    public static NetworkStateSnapshot Ok(HardwareConfigInfo state) => new(true, state, null, null);
    public static NetworkStateSnapshot Fail(string failureCategory, string error) => new(false, null, failureCategory, error);
}

public sealed record NetworkWriteEffect(string Operation, NetworkWriteTargetEvidence Target,
    IReadOnlyDictionary<string, string> RequestedSettings, IReadOnlyDictionary<string, string?> CurrentSettings,
    IReadOnlyList<NetworkNodeIdentityInfo> AffectedNodes, bool? ConnectionsComplete);
public sealed record NetworkWriteEffectPresentation(string OperationId, NetworkWriteEffect? Effect, StructuredOperationOmission? Omission);
public sealed record NetworkGuardedWriteResponse(string Tool, string ContractVersion, string Phase, bool Success,
    WriteToolError? Error, IReadOnlyList<string> Warnings, IReadOnlyList<WriteGuardReport> Guards,
    IReadOnlyList<NetworkWriteEffectPresentation> Effects, StructuredOperationBatch? Batch, WriteVerification? Verification,
    StructuredOperationOmission? Omission = null);
