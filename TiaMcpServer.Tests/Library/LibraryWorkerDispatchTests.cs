using TiaMcpServer.Tests.TestSupport;
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
}
