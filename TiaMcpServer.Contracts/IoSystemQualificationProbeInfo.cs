using System;

namespace TiaMcpServer.Contracts;

/// <summary>Worker-only input for bounded IO-system qualification.</summary>
public sealed class IoSystemQualificationProbeInfo
{
    public string Mode { get; set; } = string.Empty;
    public NetworkObjectSelectorInfo? Target { get; set; }
    public string? AttributeName { get; set; }
    public IoSystemQualificationScalarInfo? ExpectedValue { get; set; }
    public IoSystemQualificationScalarInfo? DesiredValue { get; set; }
}

/// <summary>Exactly one scalar member must be set and agree with Kind.</summary>
public sealed class IoSystemQualificationScalarInfo
{
    public string Kind { get; set; } = string.Empty;
    public string? StringValue { get; set; }
    public int? IntegerValue { get; set; }
    public bool? BooleanValue { get; set; }
}

/// <summary>Closed, side-effect-free validation before any Siemens object access.</summary>
public static class IoSystemQualificationProbeValidator
{
    private const string Method = "probe_io_system_qualification";

    public static string? Validate(WorkerRequest request)
    {
        if (request is null || !string.Equals(request.Method, Method, StringComparison.Ordinal))
            return "Expected a probe_io_system_qualification request.";
        if (!request.Confirm)
            return "Qualification requires confirm=true.";
        if (request.ExpectedSessionIdentity is null)
            return "Qualification requires expectedSessionIdentity.";

        var probe = request.IoSystemQualification;
        if (probe is null)
            return "Qualification requires ioSystemQualification.";
        if (probe.Target is null || !string.Equals(probe.Target.Kind, NetworkObjectKinds.IoSystem, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(probe.Target.SubnetId) || probe.Target.Number is null or < 0
            || HasExtraSelectorFields(probe.Target))
            return "Qualification requires an exact ioSystem target with subnetId and nonnegative number.";

        if (string.Equals(probe.Mode, "compileBaseline", StringComparison.Ordinal)
            || string.Equals(probe.Mode, "inspectOwner", StringComparison.Ordinal))
        {
            return probe.AttributeName is null && probe.ExpectedValue is null && probe.DesiredValue is null
                ? null : "This mode does not accept attribute or value fields.";
        }
        if (!string.Equals(probe.Mode, "setAndCompile", StringComparison.Ordinal))
            return "Unsupported qualification mode.";

        var requiredKind = probe.AttributeName switch
        {
            "Name" => "string",
            "Number" or "MaxNumberIWlanLinksPerSegment" => "integer",
            "MultipleUseIoSystem" or "UseIoSystemNameAsDeviceNameExtension" => "boolean",
            _ => null
        };
        if (requiredKind is null)
            return "Unsupported IO-system attribute name.";
        if (!HasExactScalar(probe.ExpectedValue, requiredKind) || !HasExactScalar(probe.DesiredValue, requiredKind))
            return "Expected and desired values must each have exactly one scalar matching the attribute kind.";
        if (SameValue(probe.ExpectedValue!, probe.DesiredValue!))
            return "Expected and desired values must differ.";
        return null;
    }

    private static bool HasExtraSelectorFields(NetworkObjectSelectorInfo target)
        => target.DeviceName is not null || target.ItemPath is not null || target.InterfaceName is not null
            || target.InterfaceType is not null || target.InterfaceOperatingMode is not null
            || target.NodeId is not null || target.NodeIndex is not null || target.IoSystemIndex is not null
            || target.IoSystemName is not null || target.ConnectionIndex is not null
            || target.ConnectionType is not null || target.LocalConnectionName is not null
            || target.LocalConnectionId is not null;

    private static bool HasExactScalar(IoSystemQualificationScalarInfo? scalar, string requiredKind)
    {
        if (scalar is null || !string.Equals(scalar.Kind, requiredKind, StringComparison.Ordinal))
            return false;
        return requiredKind switch
        {
            "string" => scalar.StringValue is not null && scalar.IntegerValue is null && scalar.BooleanValue is null,
            "integer" => scalar.StringValue is null && scalar.IntegerValue is not null && scalar.BooleanValue is null,
            "boolean" => scalar.StringValue is null && scalar.IntegerValue is null && scalar.BooleanValue is not null,
            _ => false
        };
    }

    private static bool SameValue(IoSystemQualificationScalarInfo expected, IoSystemQualificationScalarInfo desired)
        => expected.Kind switch
        {
            "string" => string.Equals(expected.StringValue, desired.StringValue, StringComparison.Ordinal),
            "integer" => expected.IntegerValue == desired.IntegerValue,
            "boolean" => expected.BooleanValue == desired.BooleanValue,
            _ => false
        };
}

public sealed class IoSystemQualificationResultInfo
{
    public string Mode { get; set; } = string.Empty;
    public NetworkObjectSelectorInfo? OriginalTarget { get; set; }
    public NetworkObjectSelectorInfo? AppliedTarget { get; set; }
    public NetworkObjectSelectorInfo? OwnerTarget { get; set; }
    public int OwnerMatchCount { get; set; }
    public bool OwnerIdentityVerified { get; set; }
    public IoSystemQualificationOwnerDiagnosticInfo? OwnerDiagnostics { get; set; }
    public string HardwareTargetKind { get; set; } = string.Empty;
    public string HardwareTargetAlias { get; set; } = string.Empty;
    public bool MutationCommitted { get; set; }
    public bool EvidenceOmitted { get; set; }
    public string CompileState { get; set; } = "notRequested";
    public int? ErrorCount { get; set; }
    public int? WarningCount { get; set; }
    public System.Collections.Generic.List<IoSystemQualificationAttributeInfo> Before { get; set; } = new();
    public System.Collections.Generic.List<IoSystemQualificationAttributeInfo> After { get; set; } = new();
    public System.Collections.Generic.List<string> Messages { get; set; } = new();
    public int OmittedMessageCount { get; set; }
    public string RestorationGuidance { get; set; } = string.Empty;
}

public sealed class IoSystemQualificationAttributeInfo
{
    public string Name { get; set; } = string.Empty;
    public bool Available { get; set; }
    public bool Writable { get; set; }
    public System.Collections.Generic.List<string> SupportedTypes { get; set; } = new();
    public IoSystemQualificationScalarInfo? Value { get; set; }
}

/// <summary>Closed stage/reason codes and aggregate counts only; never contains engineering identifiers.</summary>
public sealed class IoSystemQualificationOwnerDiagnosticInfo
{
    public string Stage { get; set; } = "targetResolution";
    public string Reason { get; set; } = "target_unresolved";
    public bool TraversalCompleted { get; set; }
    public int MatchCount { get; set; }
    public IoSystemQualificationOwnerPathEvidenceInfo? Path { get; set; }
}

public sealed class IoSystemQualificationOwnerPathEvidenceInfo
{
    public int Depth { get; set; }
    public int BlankDeviceNameCount { get; set; }
    public int BlankNameCount { get; set; }
    public int BlankTypeIdentifierCount { get; set; }
    public int NegativePositionCount { get; set; }
    public int NegativeIndexCount { get; set; }
}
