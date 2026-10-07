namespace TiaMcpServer.Contracts;

public class TagMutationResultInfo
{
    public bool Success { get; set; } = true;

    public string Operation { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public string PlcName { get; set; } = string.Empty;

    public string TableName { get; set; } = string.Empty;

    public string FolderPath { get; set; } = "/";

    public string? TagName { get; set; }

    public string? UserConstantName { get; set; }
}
