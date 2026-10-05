using Xunit;

namespace TiaMcpServer.Tests.Worker;

// Dispatch/lifecycle code remains net48 Siemens-facing; these guards complement session behavior tests.
public sealed class ActiveProjectContextSourceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, "TiaMcpServer.OpennessWorker", path));

    [Fact]
    public void ProjectDispatch_ResolvesOwnerAfterFreshIdentityCheck()
    {
        var body = Method(Read("Program.cs"), "private static WorkerResponse WithProject(");
        AssertOrdered(body, "ValidateExpectedAfterProjectResolution(", "return body(session.RequireStandaloneOwner().Project)");
        Assert.Contains("Func<Project, WorkerResponse>", body);
        Assert.DoesNotContain("session.Project", Read("Program.cs"));
        var subnet = Method(Read("Program.cs"), "private static WorkerResponse WithSubnetLifecycleProject(");
        AssertOrdered(subnet, "ValidateExpectedAfterProjectResolution(", "RequireStandaloneOwner()", "body(session)");
    }

    [Fact]
    public void StatusAndLifecycle_UseStandaloneOwnerWithoutOpening()
    {
        var service = Read("Openness/ProjectLifecycleService.cs");
        foreach (var declaration in new[] { "private static Project? ResolveProjectForRead(", "private static Project EnsureProject(" })
        {
            var body = Method(service, declaration);
            AssertOrdered(body, "session.EnsureConnected(", "ProjectOpenPolicy.Decide", "RequireStandaloneOwner().Project");
            Assert.DoesNotContain(".Open(", body);
        }
        Assert.Contains("session.ActiveContext is null", Method(service, "private static Project? ResolveProjectForRead("));
    }

    [Fact]
    public void Create_RejectsLocalSourceBeforeAnySiemensCreate()
    {
        var body = Method(Read("Openness/ProjectLifecycleService.cs"), "public static ProjectLifecycleResultInfo CreateProject(");
        AssertOrdered(body, "session.ActiveContext is not null", "RequireStandaloneOwner()", "session.TiaPortal.Projects.Create(");
        AssertOrdered(body, "RequireStandaloneOwner()", ".Create(typeof(Project)");
    }

    [Fact]
    public void LocalContext_HasNoProductionActivationOrTerminalAction()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "TiaMcpServer.OpennessWorker"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("ActiveProjectContext.ForLocalSession(", source);
            Assert.DoesNotContain(".LocalSessions.Open", source);
            Assert.DoesNotContain(".OpenServerProject(", source);
            Assert.DoesNotContain(".CloseAndCommit(", source);
            Assert.DoesNotContain("LocalSession.Save(", source);
            Assert.DoesNotContain("LocalSession.Close(", source);
        }
        foreach (var file in new[] { "Openness/ProjectLifecycleOwner.cs", "Openness/LocalSessionOwner.cs" })
        {
            var owner = Read(file);
            Assert.DoesNotContain("IDisposable", owner);
            Assert.DoesNotContain("void Save(", owner);
            Assert.DoesNotContain("void Close(", owner);
        }
        foreach (var method in new[] { "public void Disconnect(", "private void OnDisposed(" })
        {
            var cleanup = Method(Read("Openness/TiaPortalSession.cs"), method);
            Assert.DoesNotContain(".Save(", cleanup);
            Assert.DoesNotContain(".Close(", cleanup);
            Assert.DoesNotContain(".CloseAndCommit(", cleanup);
        }
    }

    private static void AssertOrdered(string source, params string[] fragments)
    {
        var position = -1;
        foreach (var fragment in fragments)
        {
            var next = source.IndexOf(fragment, position + 1, StringComparison.Ordinal);
            Assert.True(next > position, $"Missing or out of order: {fragment}");
            position = next;
        }
    }

    private static string Method(string source, string declaration)
    {
        var start = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method {declaration}");
        var open = source.IndexOf('{', start);
        var depth = 1;
        for (var end = open + 1; end < source.Length; end++)
        {
            if (source[end] == '{') depth++;
            if (source[end] == '}' && --depth == 0) return source.Substring(start, end - start + 1);
        }
        throw new InvalidOperationException("Missing closing brace.");
    }
}
