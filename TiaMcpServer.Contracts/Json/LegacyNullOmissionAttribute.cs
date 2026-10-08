namespace TiaMcpServer.Contracts;

/// <summary>Why a worker payload contract still omits null members on the wire.</summary>
public enum LegacyNullOmissionReason
{
    /// <summary>Returned by a tool still on the legacy text contract; switches when that tool migrates (roadmap Phases 2-3).</summary>
    ToolMigration,
}

/// <summary>
/// Marks a worker payload contract that still omits null members on the wire. Unmarked contracts
/// write every member, including nulls (docs/roadmap/json-contract.md). Remove the marker when its
/// reason is resolved; never add one without a reason from <see cref="LegacyNullOmissionReason"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class LegacyNullOmissionAttribute : Attribute
{
    public LegacyNullOmissionAttribute(LegacyNullOmissionReason reason) => Reason = reason;

    public LegacyNullOmissionReason Reason { get; }
}
