namespace TiaMcpServer.Contracts;

public class BlockMutationResultInfo
{
    public bool Success { get; set; } = true;

    public string Operation { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public string PlcName { get; set; } = string.Empty;

    public string BlockPath { get; set; } = string.Empty;

    public string? BlockType { get; set; }

    public string? Language { get; set; }

    /// <summary>The created block's number as read back after import; null for the other block operations.</summary>
    public int? Number { get; set; }

    /// <summary>The created OB's event class; null for any other block or operation.</summary>
    public string? ObEventClass { get; set; }
}
