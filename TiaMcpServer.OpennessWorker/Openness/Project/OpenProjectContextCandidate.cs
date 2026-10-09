namespace TiaMcpServer.OpennessWorker.Openness.Project;

internal sealed class OpenProjectContextCandidate
{
    public OpenProjectContextCandidate(ActiveProjectContext context)
    {
        Context = context;
        BindingPath = context.BindingPath;
    }

    public ActiveProjectContext Context { get; }
    public string BindingPath { get; }
}
