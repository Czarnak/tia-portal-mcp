using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class StandaloneProjectOwner : ProjectLifecycleOwner
{
    public StandaloneProjectOwner(Project project, bool openedByWorker) : base(openedByWorker)
        => Project = project ?? throw new ArgumentNullException(nameof(project));

    public Project Project { get; }
    public override ProjectBase EngineeringRoot => Project;
}
