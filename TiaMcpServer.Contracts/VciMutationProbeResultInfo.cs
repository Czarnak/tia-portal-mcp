using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>Terminal evidence for exactly one mutation-probe case.</summary>
public sealed class VciMutationProbeCaseResultInfo
{
    public string SchemaVersion { get; set; } = VciMutationProbeContract.SchemaVersion;
    public string RunId { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public string ScenarioId { get; set; } = string.Empty;
    public string CaseId { get; set; } = string.Empty;
    public string CaseInstanceId { get; set; } = string.Empty;
    public string InvocationLayer { get; set; } = string.Empty;
    public string InputCategory { get; set; } = string.Empty;
    public List<VciMutationArgumentInfo> SanitizedArguments { get; set; } = new();
    public List<VciMutationCheckInfo> Preconditions { get; set; } = new();
    public List<VciMutationCheckInfo> SafetyInvariants { get; set; } = new();
    public string Outcome { get; set; } = string.Empty;
    public VciProbeReturnInfo? Return { get; set; }
    public VciProbeExceptionInfo? Exception { get; set; }
    public VciProbeSnapshotInfo? Before { get; set; }
    public VciProbeSnapshotInfo? After { get; set; }
    public VciProbeProjectStateInfo ProjectState { get; set; } = new();
    public VciMutationTransactionInfo Transaction { get; set; } = new();
    public VciMutationCanaryInfo Canary { get; set; } = new();
    public bool UncertainOutcome { get; set; }
    public bool StopScenarioFamily { get; set; }
    public string? NotObservableReason { get; set; }
    public List<VciProbeOmissionInfo> Omissions { get; set; } = new();
}

/// <summary>One ordered, sanitized argument observation.</summary>
public sealed class VciMutationArgumentInfo
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Value { get; set; }
}

/// <summary>One ordered precondition or safety-invariant observation.</summary>
public sealed class VciMutationCheckInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Satisfied { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Transaction lifecycle evidence captured without assuming rollback semantics.</summary>
public sealed class VciMutationTransactionInfo
{
    public bool Requested { get; set; }
    public bool Started { get; set; }
    public bool CommitRequested { get; set; }
    public bool CanCommitBeforeDispose { get; set; }
    public bool Disposed { get; set; }
}

/// <summary>Post-case read-only canary evidence.</summary>
public sealed class VciMutationCanaryInfo
{
    public bool Attempted { get; set; }
    public bool Usable { get; set; }
    public string Outcome { get; set; } = string.Empty;
}
