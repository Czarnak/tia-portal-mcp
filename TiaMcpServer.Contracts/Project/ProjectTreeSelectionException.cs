using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Contracts.Project;

public sealed class ProjectTreeSelectionException : Exception
{
    public ProjectTreeSelectionException(string category, string message)
        : base(message)
    {
        Category = category;
    }

    public string Category { get; }

    public static ProjectTreeSelectionException Invalid(string message)
        => new(WorkerFailureCategories.InvalidSelector, message);
}
