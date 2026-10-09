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
        var body = Method(Read("Program.cs"), "private static WorkerResponse WithActiveContext(");
        AssertOrdered(body, "ValidateExpectedAfterProjectResolution(", "return body(session)");
        // Content writes and compile run on the ProjectBase root of any delivered container; the
        // capability gate in WithSession decides which container kinds reach a handler at all.
        Assert.DoesNotContain("WithProject(", Read("Program.cs"));
        var root = Method(Read("Program.cs"), "private static WorkerResponse WithEngineeringRoot(");
        Assert.Contains("WithActiveContext(request, session => body(session.EngineeringRoot!))", root);
        Assert.DoesNotContain("session.Project", Read("Program.cs"));
        var subnet = Method(Read("Program.cs"), "private static WorkerResponse WithSubnetLifecycleProject(");
        AssertOrdered(subnet, "ValidateExpectedAfterProjectResolution(", "body(session)");
    }

    [Fact]
    public void QualificationProbes_RemainStandaloneOnly()
    {
        // Internal probes save or compile-and-revert a project; they are not delivered for local sessions.
        var program = Read("Program.cs");
        Assert.Equal(4, program.Split("session.RequireStandaloneOwner().Project").Length - 1);
        Assert.Contains("SubnetLifecycleMutationProbeService.Run(", Method(program, "private static WorkerResponse ProbeSubnetLifecycleMutations("));
    }

    [Fact]
    public void SaveProject_DispatchesLocalSessionSaveAfterFreshIdentityCheck()
    {
        var body = Method(Read("Openness/Project/ProjectLifecycleService.cs"), "public static ProjectLifecycleResultInfo SaveProject(");
        AssertOrdered(body, "EnsureRequestedContext(session, projectPath)", "Owner: LocalSessionOwner",
            "ValidateExpectedImmediatelyBeforeMutation(", "LocalSession.Save()", "LocalResult(\"save_project\"",
            "RequireStandaloneOwner().Project", "ValidateExpectedImmediatelyBeforeMutation(", "project.Save()");
        Assert.DoesNotContain(".Close(", body);
        Assert.DoesNotContain(".CloseAndCommit(", body);
    }

    [Fact]
    public void StatusReadsUseActiveContextAndMutationRequiresStandaloneOwner()
    {
        var service = Read("Openness/Project/ProjectLifecycleService.cs");
        var read = Method(service, "private static ActiveProjectContext? ResolveProjectForRead(");
        AssertOrdered(read, "session.EnsureConnected(", "ProjectOpenPolicy.Decide", "return session.ActiveContext");
        Assert.DoesNotContain(".Open(", read);
        var resolve = Method(service, "private static void EnsureRequestedContext(");
        AssertOrdered(resolve, "session.EnsureConnected(", "ProjectOpenPolicy.Decide");
        Assert.DoesNotContain(".Open(", resolve);
        var mutation = Method(service, "private static Project EnsureProject(");
        AssertOrdered(mutation, "EnsureRequestedContext(session, projectPath)", "RequireStandaloneOwner().Project");
        Assert.DoesNotContain(".Open(", mutation);
    }

    [Fact]
    public void Create_RejectsLocalSourceBeforeAnySiemensCreate()
    {
        var body = Method(Read("Openness/Project/ProjectLifecycleService.cs"), "public static ProjectLifecycleResultInfo CreateProject(");
        AssertOrdered(body, "session.ActiveContext is not null", "RequireStandaloneOwner()", "session.TiaPortal.Projects.Create(");
        AssertOrdered(body, "RequireStandaloneOwner()", ".Create(typeof(Project)");
    }

    [Fact]
    public void LocalContext_HasNoUnconditionalTerminalAction()
    {
        var files = Directory.GetFiles(Path.Combine(Root, "TiaMcpServer.OpennessWorker"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar));
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(".OpenServerProject(", source);
            Assert.DoesNotContain(".CloseAndCommit(", source);
            Assert.DoesNotContain("LocalSession.Close(", source);
            // LocalSession.Save() is reachable only through the guarded save_project lifecycle call.
            if (!file.EndsWith(Path.Combine("Openness", "Project", "ProjectLifecycleService.cs"), StringComparison.Ordinal))
                Assert.DoesNotContain("LocalSession.Save(", source);
        }
        var lifecycle = Read("Openness/Project/ProjectLifecycleService.cs");
        Assert.Equal(1, lifecycle.Split("LocalSession.Save(").Length - 1);
        Assert.Contains("LocalSession.Save(", Method(lifecycle, "public static ProjectLifecycleResultInfo SaveProject("));
        foreach (var file in new[] { "Openness/Project/ProjectLifecycleOwner.cs", "Openness/Project/LocalSessionOwner.cs" })
        {
            var owner = Read(file);
            Assert.DoesNotContain("IDisposable", owner);
            Assert.DoesNotContain("void Save(", owner);
            Assert.DoesNotContain("void Close(", owner);
        }
        foreach (var method in new[] { "public void Disconnect(", "private void OnDisposed(" })
        {
            var cleanup = Method(Read("Openness/Project/TiaPortalSession.cs"), method);
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
