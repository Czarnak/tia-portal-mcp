namespace TiaMcpServer.Contracts;

/// <summary>
/// Parameters of one <c>hmi_read</c> operation as forwarded to the worker. The host has already
/// validated them; <see cref="Offset"/> and <see cref="Limit"/> are concrete for paged operations.
/// </summary>
public class HmiQueryInfo
{
    public string? HmiName { get; set; }

    public string? Language { get; set; }

    public int? Offset { get; set; }

    public int? Limit { get; set; }

    public string? GroupPath { get; set; }

    public string? TableName { get; set; }

    public string? TagName { get; set; }

    public string? DataLogName { get; set; }

    public string? AlarmName { get; set; }

    public string? AlarmKind { get; set; }

    public string? ScreenName { get; set; }

    public string? Category { get; set; }

    public string? Name { get; set; }
}
