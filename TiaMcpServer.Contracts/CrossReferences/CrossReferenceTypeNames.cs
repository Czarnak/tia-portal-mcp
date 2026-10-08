namespace TiaMcpServer.Contracts;

/// <summary>Closed names of <c>Siemens.Engineering.CrossReference.ReferenceType</c> in V21, in declaration order.</summary>
public static class CrossReferenceTypeNames
{
    public const string Unknown = "Unknown";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "Uses", "UsedBy", "Undefined", "TypeInstance", "InstanceType", "Assigns", "MemberGroup",
        "GroupMember", "Defines", "DefinedBy", "OverlapsWith", "Scope", Unknown
    };
}
