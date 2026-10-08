using System.Text.Json.Serialization;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Typed active-container evidence. The engineering path identifies an already-open local owner;
/// the optional session-container path records only an exact successful opening input.
/// Capabilities and connection observations describe the context but grant no authority.
/// </summary>
public sealed class ProjectContextInfo
{
    public string ContainerKind { get; set; } = string.Empty;

    public string SessionMode { get; set; } = MultiuserSessionModes.Unknown;

    public List<ProjectCapabilityInfo> Capabilities { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public MultiuserRemoteIdentity? RemoteIdentity { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public ProjectServerConnectionObservation? ConnectionObservation { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? EngineeringProjectPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? SessionContainerPath { get; set; }

    public bool OpenedByWorker { get; set; }

    /// <summary>Capture independent mutable evidence for a binding or pinned request.</summary>
    public ProjectContextInfo DeepCopy() => new()
    {
        ContainerKind = ContainerKind,
        SessionMode = SessionMode,
        Capabilities = (Capabilities ?? new List<ProjectCapabilityInfo>()).Select(capability => new ProjectCapabilityInfo
        {
            Operation = capability.Operation,
            Applicability = capability.Applicability
        }).ToList(),
        RemoteIdentity = RemoteIdentity is null ? null : new MultiuserRemoteIdentity
        {
            ServerAlias = RemoteIdentity.ServerAlias,
            Host = RemoteIdentity.Host,
            Port = RemoteIdentity.Port,
            Protocol = RemoteIdentity.Protocol,
            Group = RemoteIdentity.Group is null ? null : new ProjectServerGroupIdentity
            {
                IsRoot = RemoteIdentity.Group.IsRoot,
                Name = RemoteIdentity.Group.Name
            },
            ServerProjectName = RemoteIdentity.ServerProjectName,
            LocalSessionId = RemoteIdentity.LocalSessionId,
            LocalSessionPath = RemoteIdentity.LocalSessionPath
        },
        ConnectionObservation = ConnectionObservation is null ? null : new ProjectServerConnectionObservation
        {
            State = ConnectionObservation.State,
            ObservedAt = ConnectionObservation.ObservedAt,
            ObservationSource = ConnectionObservation.ObservationSource,
            PreviousState = ConnectionObservation.PreviousState,
            Transition = ConnectionObservation.Transition
        },
        EngineeringProjectPath = EngineeringProjectPath,
        SessionContainerPath = SessionContainerPath,
        OpenedByWorker = OpenedByWorker
    };
}
