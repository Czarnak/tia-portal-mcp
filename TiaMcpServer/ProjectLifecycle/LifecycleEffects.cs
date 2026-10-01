using TiaMcpServer.Contracts;

namespace TiaMcpServer.ProjectLifecycle;

/// <summary>Fresh source evidence and the requested destination and consequences.</summary>
public sealed record LifecycleEffects(
    string? SourceProjectPath,
    string? DestinationProjectPath,
    string? DestinationDirectory,
    ProjectStatusInfo? SourceStatus,
    bool? SourceOpenedByWorker,
    bool WillCloseSource,
    bool SavesSource,
    bool RebindsToDestination,
    bool TargetExists,
    string? ArchiveMode,
    string? ArchivePath);
