using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Project;

/// <summary>Live source state for one proposed open-project rebind.</summary>
public sealed class ProjectRebindStateInfo
{
    [JsonRequired]
    public string? SourceProjectPath { get; set; }

    [JsonRequired]
    public string DestinationProjectPath { get; set; } = string.Empty;

    [JsonRequired]
    public bool? SourceIsModified { get; set; }

    [JsonRequired]
    public bool SourceOpenedByWorker { get; set; }

    [JsonRequired]
    public bool WillCloseSource { get; set; }

    /// <summary>Required for a local source, absent for legacy standalone rebind payloads.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProjectContextInfo? SourceContext { get; set; }

    public static ProjectRebindStateInfo Create(
        string? sourceProjectPath,
        string destinationProjectPath,
        bool? sourceIsModified,
        bool sourceOpenedByWorker,
        ProjectContextInfo? sourceContext = null)
    {
        var destination = ProjectPathNormalization.Canonicalize(destinationProjectPath)
            ?? throw new ArgumentException("A destination project path is required.", nameof(destinationProjectPath));
        var source = ProjectPathNormalization.Canonicalize(sourceProjectPath);
        if (source is not null && sourceIsModified is null)
        {
            throw new ArgumentException(
                "Modified state is required when a source project is open.",
                nameof(sourceIsModified));
        }

        if (source is null && (sourceIsModified is not null || sourceOpenedByWorker))
        {
            throw new ArgumentException("An absent source cannot have modified state or worker ownership.");
        }

        if (source is null && sourceContext is not null)
            throw new ArgumentException("An absent source cannot have project context.", nameof(sourceContext));
        if (source is not null
            && string.Equals(Path.GetExtension(source), ".amc21", StringComparison.OrdinalIgnoreCase)
            && sourceContext is null)
            throw new ArgumentException("A local-session source requires typed source context.", nameof(sourceContext));
        if (sourceContext is not null &&
            (!string.Equals(sourceContext.ContainerKind, ProjectContainerKinds.LocalSession, StringComparison.Ordinal)
             || !string.Equals(ProjectPathNormalization.Canonicalize(sourceContext.EngineeringProjectPath), source, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("The source context must identify the exact local-session engineering path.", nameof(sourceContext));

        return new ProjectRebindStateInfo
        {
            SourceProjectPath = source,
            DestinationProjectPath = destination,
            SourceIsModified = sourceIsModified,
            SourceOpenedByWorker = sourceOpenedByWorker,
            SourceContext = sourceContext?.DeepCopy(),
            WillCloseSource = source is not null
                && sourceOpenedByWorker
                && sourceContext is null
                && string.Equals(Path.GetExtension(source), ".ap21", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
        };
    }
}
