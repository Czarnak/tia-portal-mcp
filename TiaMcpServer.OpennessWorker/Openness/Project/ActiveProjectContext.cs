using Siemens.Engineering;
using Siemens.Engineering.Multiuser;
using TiaMcpServer.Contracts.Multiuser;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

using Project = Siemens.Engineering.Project;

/// <summary>Internal engineering identity and ownership; never an authorization decision.</summary>
internal sealed class ActiveProjectContext
{
    private ActiveProjectContext(ProjectLifecycleOwner owner, string containerKind, string sessionMode)
    {
        Owner = owner;
        ContainerKind = containerKind;
        SessionMode = sessionMode;
        if (owner is LocalSessionOwner)
            ConnectionObservation = new ProjectServerConnectionObservation
            {
                State = ProjectServerConnectionStates.Unknown,
                ObservedAt = DateTimeOffset.UtcNow,
                ObservationSource = owner.OpenedByWorker
                    ? ProjectServerConnectionObservationSources.SessionOpen
                    : ProjectServerConnectionObservationSources.SessionBind
            };
    }

    public ProjectBase EngineeringRoot => Owner.EngineeringRoot;
    public string BindingPath => ProjectPathNormalization.Canonicalize(EngineeringRoot.Path?.FullName)
        ?? throw new WorkerOperationException(WorkerFailureCategories.PostconditionFailed,
            "TIA Portal returned an engineering root without a readable path.");
    public ProjectLifecycleOwner Owner { get; }
    public string ContainerKind { get; }
    public string SessionMode { get; }

    public IReadOnlyList<ProjectCapabilityInfo> CapabilitySet => ProjectCapabilityCatalog.Describe(ContainerKind);
    public MultiuserRemoteIdentity? RemoteIdentity => null;
    public ProjectServerConnectionObservation? ConnectionObservation { get; }
    public string? SessionContainerPath { get; private set; }

    internal void RecordSuccessfulOpen(string sessionContainerPath)
        => SessionContainerPath = sessionContainerPath;

    public ProjectContextInfo ToInfo() => new ProjectContextInfo
    {
        ContainerKind = ContainerKind,
        SessionMode = SessionMode,
        Capabilities = ProjectCapabilityCatalog.Describe(ContainerKind).ToList(),
        RemoteIdentity = RemoteIdentity,
        ConnectionObservation = ConnectionObservation,
        EngineeringProjectPath = BindingPath,
        SessionContainerPath = SessionContainerPath,
        OpenedByWorker = Owner.OpenedByWorker
    }.DeepCopy();

    internal ProjectStatusInfo ToLocalBasicStatusInfo()
    {
        if (Owner is not LocalSessionOwner)
            throw new InvalidOperationException("Basic local-session status requires a local-session owner.");
        var context = ToInfo();
        bool? isModified;
        try { isModified = EngineeringRoot.IsModified; }
        catch (EngineeringException ex)
        {
            Console.Error.WriteLine($"Could not read project metadata: {ex.Message}");
            isModified = null;
        }
        return new ProjectStatusInfo
        {
            IsOpen = true,
            Path = context.EngineeringProjectPath,
            IsModified = isModified,
            Context = context
        };
    }

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
