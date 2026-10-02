using System.Reflection;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public class OpennessWorkerClientWarningTests
{
    [Fact]
    public void CapWarnings_TruncatesEachWarningLine()
    {
        var method = typeof(OpennessWorkerClient).GetMethod(
            "CapWarnings",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var warnings = (IReadOnlyList<string>)method.Invoke(null, new object?[] { new[] { new string('w', 2_000) } })!;

        Assert.Single(warnings);
        Assert.True(warnings[0].Length <= 1_000);
        Assert.Contains("TRUNCATED", warnings[0]);
        Assert.EndsWith(" [TRUNCATED]", warnings[0]);
    }

    [Fact]
    public void CapWarnings_LimitsToTwentyLinesWithExplicitTruncationSummary()
    {
        var method = typeof(OpennessWorkerClient).GetMethod(
            "CapWarnings",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var input = Enumerable.Range(0, 25).Select(i => $"warning {i}").ToArray();

        var warnings = (IReadOnlyList<string>)method.Invoke(null, new object?[] { input })!;

        // 20 real lines are kept; the 21st entry is an explicit truncation summary, so the
        // surfaced list never silently drops warnings.
        Assert.Equal(21, warnings.Count);
        Assert.Equal("warning 0", warnings[0]);
        Assert.Equal("warning 19", warnings[19]);
        Assert.Contains("more worker warnings truncated", warnings[^1]);
    }

    [Fact]
    public void AppendWarning_ReCapsAppendedWarningsToTheLineCap()
    {
        var appendWarning = typeof(OpennessWorkerClient).GetMethod(
            "AppendWarning",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        // A degraded read can already carry the full 20-line cap; appending the binding-divergence
        // warning on top must route back through CapWarnings so the surfaced list still honors the
        // 20-line cap rather than growing to 21 raw lines with no truncation marker.
        var existing = Enumerable.Range(0, 20).Select(i => $"worker warning {i}").ToArray();
        var result = (IReadOnlyList<string>)appendWarning.Invoke(
            null,
            new object?[] { (IReadOnlyList<string>)existing, "session/worker project divergence" })!;

        Assert.Equal(21, result.Count);
        Assert.Contains("more worker warnings truncated", result[^1]);
        Assert.All(result, line => Assert.True(line.Length <= 1_000));
    }

    [Fact]
    public async Task DirectStatus_WorkerRefusesDifferentOpenProject_LeavesBindingUnverified()
    {
        // Actual UI-open source B differs from configured A. The worker refuses before
        // returning a successful identity; configured_unverified therefore remains intact.
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\Session.ap21", forceRebind: false, out _));
        using var uiOpen = new FakeWorkerUiOpenProject(@"C:\actual\Other.ap21");
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await client.GetProjectStatusAsync(null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, binding.BindingState);
        Assert.Equal("C:\\bound\\Session.ap21", binding.BoundProjectPath);
        Assert.Contains("C:\\bound\\Session.ap21", result.Error);
        Assert.Contains("C:\\actual\\Other.ap21", result.Error);
    }

    [Fact]
    public async Task DirectStatus_SuccessfulIdentityDivergence_InvalidatesRatherThanWarningAndRecoversExplicitly()
    {
        const string source = @"C:\bound\Session.ap21";
        const string divergent = @"C:\actual\Other.ap21";
        var binding = new ProjectSessionBinding(source);
        using var client = new OpennessWorkerClient(binding);
        using var transport = new ScriptedStatusTransport(client,
            ScriptedStatusTransport.Success(source),
            ScriptedStatusTransport.Success(divergent, "worker diagnostic"),
            ScriptedStatusTransport.Success(source));
        var initial = await client.GetProjectStatusAsync(null);
        Assert.True(initial.Success, initial.Error + transport.Diagnostics);
        Assert.True(binding.IsVerified);

        var result = await client.GetProjectStatusAsync(null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.True(result.IsPostOperationFailure);
        Assert.Equal(WorkerDispatchState.Sent, result.DispatchState);
        Assert.Equal(divergent, result.SessionIdentity!.ProjectPath);
        Assert.Equal(new[] { "worker diagnostic" }, result.Warnings);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.BindingState);
        Assert.Equal(source, binding.BoundProjectPath);

        var blocked = await client.GetProjectStatusAsync(null);
        Assert.False(blocked.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, blocked.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, blocked.DispatchState);
        Assert.Equal(new[] { "hello", "get_project_status", "get_project_status" }, transport.Requests);

        Assert.True(binding.Bind(source, forceRebind: true, out var error), error);
        var recovered = await client.GetProjectStatusAsync(null);
        Assert.True(recovered.Success, recovered.Error);
        Assert.Empty(recovered.Warnings);
        Assert.True(binding.IsVerified);
        Assert.Equal(source, binding.BoundProjectPath);
        Assert.Equal(new[] { "hello", "get_project_status", "get_project_status", "get_project_status" }, transport.Requests);
    }
}
