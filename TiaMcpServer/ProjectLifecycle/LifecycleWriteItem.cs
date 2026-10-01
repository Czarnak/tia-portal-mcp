using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>One lifecycle call; operation identity is supplied by the host, never the caller.</summary>
public sealed record LifecycleWriteItem(string Operation) : IOperationBatchItem
{
    public string OperationId => "lifecycle";
    public string? ProjectPath { get; init; }
    public string? ProjectDirectory { get; init; }
    public string? ProjectName { get; init; }
    public string? Author { get; init; }
    public string? Comment { get; init; }
    public bool ForceRebind { get; init; }
    public string? TargetDirectory { get; init; }
    public string? TargetName { get; init; }
    public bool Rebind { get; init; } = true;
    public string? ArchiveDirectory { get; init; }
    public string? ArchiveName { get; init; }
    public string? Mode { get; init; }
    public bool SaveBeforeArchive { get; init; } = true;
    public bool SaveBeforeClose { get; init; } = true;
}
