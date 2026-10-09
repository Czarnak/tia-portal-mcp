using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness.Project;

/// <summary>Retains the lifecycle handle and its provenance; grants no mutation authority.</summary>
internal abstract class ProjectLifecycleOwner
{
    protected ProjectLifecycleOwner(bool openedByWorker) => OpenedByWorker = openedByWorker;

    public abstract ProjectBase EngineeringRoot { get; }
    public bool OpenedByWorker { get; }
}
