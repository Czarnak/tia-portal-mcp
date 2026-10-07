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
}
