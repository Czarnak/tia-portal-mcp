using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts.Project;

/// <summary>A passive connection observation; an unknown state must never be treated as healthy.</summary>
public sealed class ProjectServerConnectionObservation
{
    public string State { get; set; } = string.Empty;

    public DateTimeOffset ObservedAt { get; set; }

    public string ObservationSource { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? PreviousState { get; set; }

    public bool Transition { get; set; }
}
