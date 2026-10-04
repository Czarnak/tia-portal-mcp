using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

// These tests pin worker call boundaries that cannot execute in the net10 test process.
// Stub/source evidence does not qualify live Siemens attachment and detachment behavior.
public sealed class PortalSelectionSourceTests
{
    private static string Read(string file) => File.ReadAllText(Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")),
        "TiaMcpServer.OpennessWorker", file));

    private static string Method(string source, string declaration)
    {
        var start = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method: {declaration}");
        var open = source.IndexOf('{', start);
        var depth = 1;
        for (var end = open + 1; end < source.Length; end++)
        {
            if (source[end] == '{') depth++;
            if (source[end] == '}' && --depth == 0) return source.Substring(start, end - start + 1);
        }
        throw new InvalidOperationException("Missing closing brace.");
    }

    private static string Selection => Method(Read("Openness/TiaPortalSession.cs"), "public PortalProjectSelectionInfo SelectPortalProject(");

    [Fact]
    public void ListingRequestMatchesCatalogAndBothWorkerDispatches()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var host = File.ReadAllText(Path.Combine(root, "TiaMcpServer/Worker/OpennessWorkerClient.cs"));
        var list = Method(host, "async Task<WorkerCallResult?> List()");
        var request = Regex.Match(list, "Method = \"([^\"]+)\"");
        Assert.True(request.Success);
        var operation = request.Groups[1].Value;
        Assert.Equal("list_tia_portal_processes", operation);
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
        var workerDispatch = Regex.Match(Read("Program.cs"), "\"([^\"]+)\" => ListPortalProcesses\\(request\\)");
        Assert.True(workerDispatch.Success);
        Assert.Equal(operation, workerDispatch.Groups[1].Value);
        var fake = File.ReadAllText(Path.Combine(root, "TiaMcpServer.FakeWorker/Program.cs"));
        Assert.Contains($"if (currentMethod == \"{operation}\")", fake);
    }

    [Fact]
    public void ListingBypassesWithSession()
    {
        var body = Method(Read("Program.cs"), "private static WorkerResponse ListPortalProcesses(");
        Assert.Contains("Execute(", body);
        Assert.DoesNotContain("WithSession(", body);
        Assert.DoesNotContain("EnsureConnected(", body);
        Assert.Contains("TiaPortalProcessInventory.Read()", body);
    }

    [Fact]
    public void SelectionBypassesWithSession()
    {
        var body = Method(Read("Program.cs"), "private static WorkerResponse SelectPortalProject(");
        Assert.Contains("Execute(", body);
        Assert.DoesNotContain("WithSession(", body);
        Assert.DoesNotContain("EnsureConnected(", body);
        Assert.Contains("WorkerFailureCategories.ValidationError", body);
    }

    [Fact]
    public void SelectionValidatesSuppliedIdentityBeforeLookup()
    {
        var body = Method(Read("Program.cs"), "private static WorkerResponse SelectPortalProject(");
        var validationIndex = body.IndexOf("ValidateExpectedSessionIdentityForRequest(", StringComparison.Ordinal);
        var lookupIndex = body.IndexOf("_sharedSession.SelectPortalProject(", StringComparison.Ordinal);
        Assert.True(validationIndex >= 0 && lookupIndex > validationIndex);
        Assert.Contains("allowMissingExpectedIdentity: true", body);
        Assert.Contains("useCachedIdentity: true", body);
        var validation = Method(Read("Openness/TiaPortalSession.cs"), "public void ValidateExpectedSessionIdentity(");
        Assert.Contains("useCachedIdentity ? GetCachedSessionIdentity() : GetSessionIdentity()", validation);
    }

    [Fact]
    public void SelectPortalProjectNeverOpensClosesOrSaves()
    {
        Assert.DoesNotContain("OpenProject(", Selection);
        Assert.DoesNotContain(".Open(", Selection);
        Assert.DoesNotContain(".Close(", Selection);
        Assert.DoesNotContain(".Save", Selection);
    }

    [Fact]
    public void SelectPortalProjectDoesNotReuseSelectOpenProject()
    {
        Assert.DoesNotContain("SelectOpenProject(", Selection);
        Assert.Contains("SelectProjectIndex(", Selection);
        Assert.Contains("AdoptProject(", Selection);
    }

    [Fact]
    public void DisconnectUnsubscribesBeforeRelease()
    {
        var body = Method(Read("Openness/TiaPortalSession.cs"), "public void Disconnect(");
        var release = body.IndexOf("portal?.Dispose()", StringComparison.Ordinal);
        Assert.True(release >= 0);
        foreach (var before in new[] { "Notification -=", "Confirmation -=", "Disposed -=", "SetActiveContext(null)", "_selectedProjectPath = null", "SetPortalHandle(null, null)" })
            Assert.True(body.IndexOf(before, StringComparison.Ordinal) >= 0 && body.IndexOf(before, StringComparison.Ordinal) < release, before);
    }

    [Fact]
    public void GuardRunsBeforeDisconnect()
    {
        var guardIndex = Selection.IndexOf("PortalDetachGuard.EvaluateProjects(", StringComparison.Ordinal);
        var detachIndex = Selection.IndexOf("Disconnect()", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0 && detachIndex > guardIndex);
        var before = Selection.Substring(0, Selection.IndexOf("Disconnect()", StringComparison.Ordinal));
        Assert.DoesNotContain("CurrentProjectPath", before);
        Assert.DoesNotContain("SetPortalHandle(", before);
        Assert.DoesNotContain("Project =", before);
    }

    [Fact]
    public void DetachGuardInspectsAttachedProjectsWithoutChangingSelectedHandle()
    {
        Assert.Contains("ReadAttachedProjectModifiedStates()", Selection);
        Assert.Contains("PortalDetachGuard.EvaluateProjects(", Selection);
        var read = Method(Read("Openness/TiaPortalSession.cs"), "private IReadOnlyList<bool?>? ReadAttachedProjectModifiedStates(");
        Assert.Contains("_tiaPortal!.Projects", read);
        Assert.Contains("catch (Exception", read);
        Assert.Contains("return null", read);
        Assert.DoesNotContain("AdoptProject(", read);
        Assert.DoesNotContain("Project =", read);
    }

    [Fact]
    public void NoPreDetachCategoryAfterDisconnect()
    {
        var after = Selection.Substring(Selection.IndexOf("Disconnect()", StringComparison.Ordinal));
        Assert.DoesNotContain("WorkerFailureCategories.TargetNotFound", after);
        Assert.DoesNotContain("WorkerFailureCategories.TargetAmbiguous", after);
        Assert.DoesNotContain("WorkerFailureCategories.GuardBlocked", after);
        Assert.Contains("catch (Exception", after);
        Assert.Contains("WorkerFailureCategories.WorkerOperationFailed", after);
    }

    [Fact]
    public void ConnectAndListingShareInventory()
    {
        Assert.Contains("TiaPortalProcessInventory.Read()", Method(Read("Openness/TiaPortalSession.cs"), "public void Connect("));
        var inventory = Read("Openness/TiaPortalProcessInventory.cs");
        Assert.Contains("TiaPortal.GetProcesses()", inventory);
        Assert.DoesNotContain(".Attach()", inventory);
    }

    [Fact]
    public void MainDisposesSharedSessionAfterLoop()
    {
        var body = Method(Read("Program.cs"), "private static void Main(");
        Assert.Contains("finally", body);
        Assert.True(body.IndexOf("_sharedSession.Dispose()", StringComparison.Ordinal)
            > body.IndexOf("Console.In.ReadLine()", StringComparison.Ordinal));
    }
}
