namespace TiaMcpServer.Contracts.CrossReferences;

/// <summary>Closed names of <c>Siemens.Engineering.CrossReference.Access</c> in V21, in declaration order.</summary>
public static class CrossReferenceAccessNames
{
    public const string Unknown = "Unknown";

    public static readonly IReadOnlyList<string> All = new[]
    {
        "Undefined", "Read", "Write", "RW", Unknown, "Definition", "Declaration", "Interface", "Jump",
        "Monitor", "Modify", "Force", "Call", "UC", "CC", "Multiinstance", "InstanceDB", "Open",
        "Interlock", "Supervision", "Actions", "Transition", "ReadAndSymbol", "WriteAndSymbol",
        "ReadWriteAndSymbol", "InstanceAndSymbol", "MultiinstanceAndSymbol", "ProDiagSupervision",
        "DefaultValue", "ArrayBoundary", "StringLength", "TypeAlarm", "InstanceAlarm", "Parameterinstance",
        "ParameterinstanceAndSymbol", "CreateReference", "CreateReferenceAndSymbol"
    };
}
