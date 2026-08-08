using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Terminal result of exactly one probe case invocation. The envelope shape is deliberately
/// stable: every case reports the same identity/outcome fields, and exactly one of
/// <see cref="Return"/>, <see cref="Snapshot"/>, or <see cref="Exception"/> is populated depending
/// on <see cref="Outcome"/> (all three are null for <c>not_observable</c>, in which case
/// <see cref="NotObservableReason"/> explains why).
/// </summary>
public sealed class VciProbeCaseResultInfo
{
    /// <summary>Wire schema version. Must equal <see cref="VciReadProbeContract.SchemaVersion"/>.</summary>
    public string SchemaVersion { get; set; } = VciReadProbeContract.SchemaVersion;

    /// <summary>Echoes the request's <see cref="VciProbeRequestInfo.RunId"/>.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Echoes the request's <see cref="VciProbeRequestInfo.SessionId"/>.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Echoes the request's <see cref="VciProbeRequestInfo.CaseId"/>.</summary>
    public string CaseId { get; set; } = string.Empty;

    /// <summary>Echoes the request's <see cref="VciProbeRequestInfo.CaseInstanceId"/>.</summary>
    public string CaseInstanceId { get; set; } = string.Empty;

    /// <summary>One of <see cref="VciReadProbeContract.Outcomes"/>.</summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>Normalized return-value / member observation. Populated only when <see cref="Outcome"/> is <c>returned</c> or <c>returned_null</c>.</summary>
    public VciProbeReturnInfo? Return { get; set; }

    /// <summary>Bounded snapshot captured by service/group/workspace/mapping/format cases.</summary>
    public VciProbeSnapshotInfo? Snapshot { get; set; }

    /// <summary>Captured exception evidence. Populated only when <see cref="Outcome"/> is <c>threw</c>.</summary>
    public VciProbeExceptionInfo? Exception { get; set; }

    /// <summary>Repeated-read evidence. Populated only by case <c>R-REP</c>.</summary>
    public VciProbeRepeatabilityInfo? Repeatability { get; set; }

    /// <summary>Explains why the case could not be observed. Populated only when <see cref="Outcome"/> is <c>not_observable</c>.</summary>
    public string? NotObservableReason { get; set; }

    /// <summary><c>Project.IsModified</c> read immediately before and after the observed call.</summary>
    public VciProbeProjectStateInfo ProjectState { get; set; } = new();

    /// <summary>Things the worker chose not to observe because a configured budget was exhausted.</summary>
    public List<VciProbeOmissionInfo> Omissions { get; set; } = new();
}

/// <summary>Normalized return-value / member observation for a single successful VCI read.</summary>
public sealed class VciProbeReturnInfo
{
    /// <summary>CLR type name of the returned value, as observed by the worker.</summary>
    public string ClrTypeName { get; set; } = string.Empty;

    /// <summary>True when the returned value itself was null.</summary>
    public bool IsNull { get; set; }

    /// <summary>Best-effort string rendering of the returned value, when it is a scalar.</summary>
    public string? StringValue { get; set; }

    /// <summary>Normalized member (property/field) observations captured off the returned object.</summary>
    public List<VciProbeMemberObservationInfo> Members { get; set; } = new();
}

/// <summary>One normalized member (property/field) observation captured off a returned object.</summary>
public sealed class VciProbeMemberObservationInfo
{
    /// <summary>Member name as declared on the observed CLR type.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>CLR type name of the member's value.</summary>
    public string ClrTypeName { get; set; } = string.Empty;

    /// <summary>Best-effort string rendering of the member's value, when it is a scalar.</summary>
    public string? StringValue { get; set; }

    /// <summary>True when the member's value itself was null.</summary>
    public bool IsNull { get; set; }
}

/// <summary>Captured exception evidence. Deliberately excludes the stack trace.</summary>
public sealed class VciProbeExceptionInfo
{
    /// <summary>Full CLR type name of the caught exception.</summary>
    public string ExceptionTypeName { get; set; } = string.Empty;

    /// <summary>Exception message.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>Exception HResult.</summary>
    public int HResult { get; set; }
}

/// <summary><c>Project.IsModified</c> read immediately before and after the observed call.</summary>
public sealed class VciProbeProjectStateInfo
{
    /// <summary><c>Project.IsModified</c> immediately before the observed call.</summary>
    public bool IsModifiedBefore { get; set; }

    /// <summary><c>Project.IsModified</c> immediately after the observed call.</summary>
    public bool IsModifiedAfter { get; set; }
}

/// <summary>Repeated-read evidence for case <c>R-REP</c>: two or more ordered observations of the same read.</summary>
public sealed class VciProbeRepeatabilityInfo
{
    /// <summary>Ordered observations, one per repetition, in invocation order.</summary>
    public List<VciProbeReturnInfo> Observations { get; set; } = new();

    /// <summary>True when every observation in <see cref="Observations"/> was identical.</summary>
    public bool IsIdentical { get; set; }
}

/// <summary>One thing the worker chose not to observe because a configured budget was exhausted.</summary>
public sealed class VciProbeOmissionInfo
{
    /// <summary>Human-readable reason the item was omitted.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Name of the exhausted budget field (e.g. <c>maxGroups</c>).</summary>
    public string BudgetName { get; set; } = string.Empty;

    /// <summary>Configured limit for the exhausted budget.</summary>
    public int BudgetValue { get; set; }

    /// <summary>Number of items actually observed before the budget was exhausted.</summary>
    public int ObservedCount { get; set; }
}
