using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Internal engineering identity and ownership; never an authorization decision.</summary>
internal sealed class ActiveProjectContext
{
    private ActiveProjectContext(ProjectLifecycleOwner owner, string containerKind, string sessionMode)
    {
        Owner = owner;
        ContainerKind = containerKind;
        SessionMode = sessionMode;
    }

    public ProjectBase EngineeringRoot => Owner.EngineeringRoot;
    public ProjectLifecycleOwner Owner { get; }
    public string ContainerKind { get; }
    public string SessionMode { get; }

    // PR2 does not populate compatibility classifications or publish capabilities.
    public IReadOnlyList<ProjectCapabilityInfo> CapabilitySet { get; }
        = Array.AsReadOnly(Array.Empty<ProjectCapabilityInfo>());
    public MultiuserRemoteIdentity? RemoteIdentity => null;
    public ProjectServerConnectionObservation? ConnectionObservation => null;

    public static ActiveProjectContext ForStandalone(Project project, bool openedByWorker)
        => new(new StandaloneProjectOwner(project, openedByWorker),
            ProjectContainerKinds.StandaloneProject, MultiuserSessionModes.NotApplicable);

    public static ActiveProjectContext ForLocalSession(
        LocalSession localSession, bool openedByWorker, string containerKind, string sessionMode)
    {
        if (containerKind != ProjectContainerKinds.LocalSession && containerKind != ProjectContainerKinds.ServerProject)
            throw new ArgumentException("A local-session owner requires a local or server container.", nameof(containerKind));
        if (sessionMode != MultiuserSessionModes.Multiuser && sessionMode != MultiuserSessionModes.Exclusive
            && sessionMode != MultiuserSessionModes.Unknown)
            throw new ArgumentException("A local-session owner requires a multiuser, exclusive, or unknown mode.", nameof(sessionMode));
        return new ActiveProjectContext(new LocalSessionOwner(localSession, openedByWorker), containerKind, sessionMode);
    }
}
