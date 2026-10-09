using Siemens.Engineering;
using Siemens.Engineering.Multiuser;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

/// <summary>Retains a session without save, discard, commit, or disposal behavior.</summary>
internal sealed class LocalSessionOwner : ProjectLifecycleOwner
{
    public LocalSessionOwner(LocalSession localSession, bool openedByWorker) : base(openedByWorker)
    {
        LocalSession = localSession ?? throw new ArgumentNullException(nameof(localSession));
        Project = localSession.Project ?? throw new ArgumentException(
            "A local session must expose an engineering root.", nameof(localSession));
    }

    public LocalSession LocalSession { get; }
    public MultiuserProject Project { get; }
    public override ProjectBase EngineeringRoot => Project;
}
