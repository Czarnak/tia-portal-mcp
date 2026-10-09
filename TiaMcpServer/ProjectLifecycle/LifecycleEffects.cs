using System.Text.Json.Serialization;
using TiaMcpServer.Contracts.Project;

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
    string? ArchivePath,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ProjectContextInfo? SourceContext = null)
{
    internal LifecycleEffects ForConsequenceComparison()
    {
        static ProjectContextInfo? Stable(ProjectContextInfo? source)
        {
            if (source is null) return null;
            var copy = source.DeepCopy();
            copy.ConnectionObservation = null;
            copy.Capabilities = new();
            return copy;
        }

        var status = SourceStatus is null ? null : new ProjectStatusInfo
        {
            IsOpen = SourceStatus.IsOpen,
            Path = SourceStatus.Path,
            Name = SourceStatus.Name,
            Author = SourceStatus.Author,
            IsModified = SourceStatus.IsModified,
            Version = SourceStatus.Version,
            Size = SourceStatus.Size,
            CreationTime = SourceStatus.CreationTime,
            LastModified = SourceStatus.LastModified,
            LastModifiedBy = SourceStatus.LastModifiedBy,
            Metadata = SourceStatus.Metadata,
            Context = Stable(SourceStatus.Context)
        };
        return this with { SourceContext = Stable(SourceContext), SourceStatus = status };
    }
}
