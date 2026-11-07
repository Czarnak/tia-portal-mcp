using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Library;

// The net48 dispatch is Siemens-facing; like ActiveProjectContextSourceTests, these pins guard its routing.
public sealed class LibraryWorkerDispatchTests
{
    private static readonly string Program =
        File.ReadAllText(Path.Combine(RepositoryFiles.Root, "TiaMcpServer.OpennessWorker", "Program.cs")).ReplaceLineEndings("\n");

    private static string ReadLibrary()
    {
        const string declaration = "private static WorkerResponse ReadLibrary(WorkerRequest request)";
        var start = Program.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, "Missing ReadLibrary dispatch.");
        var end = Program.IndexOf("\n\n", start, StringComparison.Ordinal);
        return Program.Substring(start, (end < 0 ? Program.Length : end) - start);
    }

    [Fact]
    public void EveryLibraryMethod_RoutesThroughWithActiveContext()
    {
        Assert.Contains("var m when LibraryReadMethods.All.Contains(m, StringComparer.Ordinal) => ReadLibrary(request),", Program);
        var body = ReadLibrary();
        Assert.Contains("=> WithActiveContext(request, session => Success(LibraryReader.Read(", body);
        Assert.Contains("session.TiaPortal!, session.EngineeringRoot!, request.Method,", body);
        Assert.Contains("LibraryReader.SharedSnapshots", body);
        Assert.DoesNotContain("WithEngineeringRoot", body);
    }

    [Fact]
    public void NullLibraryQuery_ThrowsValidationError()
        => Assert.Contains(
            "request.LibraryQuery ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError,",
            ReadLibrary());

    [Theory]
    [InlineData("PlanLibraryWrite", "LibraryWritePlanReader.Plan")]
    [InlineData("WriteLibrary", "LibraryWriter.Write")]
    public void LibraryWrites_UseWithActiveContextAndRequiredRequest(string handler, string callee)
    {
        var start = Program.IndexOf($"private static WorkerResponse {handler}(WorkerRequest request)", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing {handler} dispatch.");
        var end = Program.IndexOf("\n\n", start, StringComparison.Ordinal);
        var body = Program.Substring(start, (end < 0 ? Program.Length : end) - start);
        Assert.Contains($"=> WithActiveContext(request, session => Success({callee}(", body);
        Assert.Contains("session.TiaPortal!, session.EngineeringRoot!, request.Method,", body);
        Assert.Contains("request.LibraryWrite ?? throw new WorkerOperationException(WorkerFailureCategories.ValidationError,", body);
        Assert.DoesNotContain("SharedSnapshots", body);
        Assert.Contains(handler == "PlanLibraryWrite"
            ? "LibraryWriteMethods.GlobalPlan or LibraryWriteMethods.ProjectPlan => PlanLibraryWrite(request),"
            : "var m when LibraryWriteMethods.All.Contains(m, StringComparer.Ordinal) => WriteLibrary(request),", Program);
    }

    [Fact]
    public void Dispatch_EveryTypeMethodReachesWriter()
    {
        Assert.Contains("var m when LibraryWriteMethods.All.Contains(m, StringComparer.Ordinal) => WriteLibrary(request),", Program);
        var writer = File.ReadAllText(Path.Combine(RepositoryFiles.Root, "TiaMcpServer.OpennessWorker", "Openness", "Library", "LibraryWriter.cs"));
        Assert.Contains("if (LibraryWriteMethods.TypeOperations.Contains(item.Operation))", writer);
        Assert.Contains("return LibraryTypeWriter.Write(portal, project, method, item);", writer);
        foreach (var operation in LibraryWriteMethods.TypeOperations)
            Assert.Contains("library_project_" + operation, LibraryWriteMethods.All);
    }

    [Fact]
    public void Capability_ProjectAndGlobalTypeMethods()
    {
        foreach (var method in LibraryWriteMethods.All.Where(x => LibraryWriteMethods.TypeOperations.Any(operation => x.EndsWith("_" + operation, StringComparison.Ordinal))))
        {
            var global = method.StartsWith("library_global_", StringComparison.Ordinal);
            Assert.Equal(global ? "library_write" : "library_project_write", ProjectCapabilityCatalog.OperationFor(method));
            Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, method));
            Assert.Equal(global, ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, method));
            Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability(method));
        }
    }
}
