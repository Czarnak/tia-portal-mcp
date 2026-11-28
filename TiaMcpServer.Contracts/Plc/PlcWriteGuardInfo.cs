namespace TiaMcpServer.Contracts.Plc;

/// <summary>Required PLC library-instance protection evidence; clear results retain explicit nulls.</summary>
public sealed class PlcWriteGuardInfo
{
    public bool Blocked { get; set; }
    public string? GuardId { get; set; }
    public string? Message { get; set; }
}

/// <summary>The closed operations accepted by the internal bound PLC protection read.</summary>
public static class PlcWriteGuardOperation
{
    public const string UpdateBlockLogic = "update_block_logic";
    public const string UpdateTypeContent = "update_type_content";
    public const string DeleteBlock = "delete_block";
    public const string DeleteBlockGroup = "delete_block_group";

    public static bool IsSupported(string? operation)
        => operation is UpdateBlockLogic or UpdateTypeContent or DeleteBlock or DeleteBlockGroup;
}

public sealed class PlcWriteGuardRequestInfo
{
    public string Operation { get; set; } = string.Empty;
    public string? BlockPath { get; set; }
    public string? TypePath { get; set; }
}
