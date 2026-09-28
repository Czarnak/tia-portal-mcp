using System;
using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

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

    public static ProjectRebindStateInfo Create(
        string? sourceProjectPath,
        string destinationProjectPath,
        bool? sourceIsModified,
        bool sourceOpenedByWorker)
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

        return new ProjectRebindStateInfo
        {
            SourceProjectPath = source,
            DestinationProjectPath = destination,
            SourceIsModified = sourceIsModified,
            SourceOpenedByWorker = sourceOpenedByWorker,
            WillCloseSource = source is not null
                && sourceOpenedByWorker
                && !string.Equals(source, destination, StringComparison.OrdinalIgnoreCase)
        };
    }
}
