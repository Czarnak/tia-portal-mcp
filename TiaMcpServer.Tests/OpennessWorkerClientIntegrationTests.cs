using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Drives the real persistent IPC pipeline against TiaMcpServer.FakeWorker. Each test owns
/// its client (and therefore its worker process); clients are disposed so no fake worker
/// outlives its test. One class so xunit runs these sequentially.
/// </summary>
public class OpennessWorkerClientIntegrationTests
{
    private static OpennessWorkerClient CreateClient(string? workerPath = null, TimeSpan? requestTimeout = null)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath ?? FakeWorkerLocator.Locate(),
            requestTimeout: requestTimeout);

    private const string InspectStateBeforeRetryGuidance =
        "The write outcome is unknown. Inspect current project state before retrying.";

    [Fact]
    public async Task CollapsedOpenProject_PreviewThenApply_RoundTrips()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();

        var preview = await ProjectLifecycleTools.OpenProject(client, safety, projectPath: "ok");
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.OpenProject(
            client,
            safety,
            projectPath: "ok",
            confirm: true,
            safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.Equal("open_project", appliedDoc.RootElement.GetProperty("toolName").GetString());
        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Success_ReturnsStructuredPayload()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("ok");

        Assert.True(result.Success);
        Assert.Equal("{\"seq\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task PersistentWorker_ReusesOneProcessAcrossRequests()
    {
        using var client = CreateClient();

        var first = await client.GetProjectStatusAsync("ok");
        var second = await client.GetProjectStatusAsync("ok");

        Assert.Equal("{\"seq\":1}", first.Payload);
        // seq=2 can only happen if the SAME fake worker process handled both requests.
        Assert.Equal("{\"seq\":2}", second.Payload);
    }

    [Fact]
    public async Task HangingWrite_ReturnsWorkerTimeout_AndIsIssuedOnce()
    {
        using var client = CreateClient(requestTimeout: TimeSpan.FromSeconds(2));

        var timedOut = await client.GetProjectStatusAsync("hang");
        // SendAsync issues exactly one write+read per call (no internal retry loop); the fresh
        // process below restarting at seq=1 is the observable evidence that the timed-out
        // request was never reissued against a still-alive worker.
        var recovered = await client.GetProjectStatusAsync("ok");

        Assert.False(timedOut.Success);
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, timedOut.FailureCategory);
        Assert.Equal(InspectStateBeforeRetryGuidance, timedOut.Error);
        Assert.True(recovered.Success);
        // A fresh process restarts its request counter — proves the worker was restarted for
        // the NEXT call only, never replayed for the one that timed out.
        Assert.Equal("{\"seq\":1}", recovered.Payload);
    }

    [Theory]
    [InlineData("crash")]
    [InlineData("malformed")]
    [InlineData("null-response")]
    public async Task LostWrite_ReturnsWorkerCrashed_AndIsIssuedOnce(string scenario)
    {
        using var client = CreateClient();

        var lost = await client.GetProjectStatusAsync(scenario);
        var recovered = await client.GetProjectStatusAsync("ok");

        Assert.False(lost.Success);
        Assert.Equal(WorkerFailureCategories.WorkerCrashed, lost.FailureCategory);
        Assert.Equal(InspectStateBeforeRetryGuidance, lost.Error);
        Assert.True(recovered.Success);
        // A fresh process restarts its request counter — proves the crashed/lost worker was
        // restarted for the NEXT call only, never replayed for the one that was lost.
        Assert.Equal("{\"seq\":1}", recovered.Payload);
    }

    [Fact]
    public async Task ResponseWarnings_SurfaceOnTheResult()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("ok-with-warnings");

        Assert.True(result.Success);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Skipping device 'X'"));
    }

    [Fact]
    public async Task OrphanStderr_DoesNotBecomeWarnings()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("ok-with-stderr");

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task PayloadStartingWithErrorPrefix_IsNotMisclassified()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("error-prefix-payload");

        Assert.True(result.Success);
        Assert.StartsWith("Error:", result.Payload);
    }

    [Fact]
    public async Task WorkerReportedError_IsStructuredFailure()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("worker-error");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
        Assert.Equal("Error: boom", result.ToText());
        // The worker reported no category, so the client defaults to worker_operation_failed.
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
    }

    [Fact]
    public async Task WorkerReportedErrorWithApprovedCategory_PreservesIt()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("worker-error-with-category");

        Assert.False(result.Success);
        Assert.Equal("invalid value", result.Error);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public async Task FailedFirstWorkerResponse_DoesNotBindTheSession()
    {
        using var client = CreateClient();

        var failed = await client.GetProjectStatusAsync("worker-error");
        var recovered = await client.GetProjectStatusAsync("ok");

        Assert.False(failed.Success);
        Assert.True(recovered.Success);
        Assert.Equal("{\"seq\":2}", recovered.Payload);
    }

    [Fact]
    public async Task SuccessfulFirstRequest_BindsToTheResolvedPathAndRejectsADifferentProjectAfterward()
    {
        using var client = CreateClient();

        var succeeded = await client.GetProjectStatusAsync("ok-with-resolved-path");
        var changedProject = await client.GetProjectStatusAsync("worker-error");

        Assert.True(succeeded.Success);
        Assert.False(changedProject.Success);
        Assert.Contains("already bound to project 'C:\\resolved\\Ground.ap21'", changedProject.Error);
    }

    [Fact]
    public async Task UnboundSession_BindsToTheWorkerReportedPathAfterSuccess()
    {
        var binding = new ProjectSessionBinding(null);
        using var boundClient = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await boundClient.GetProjectStatusAsync("ok-with-resolved-path");

        Assert.True(result.Success);
        Assert.Equal("C:\\resolved\\Ground.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public async Task FailedCall_LeavesTheSessionUnbound()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await client.GetProjectStatusAsync("worker-error");

        Assert.False(result.Success);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public async Task SuccessfulCallWithoutAResolvedPath_LeavesTheSessionUnbound()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await client.GetProjectStatusAsync("ok");

        Assert.True(result.Success);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public async Task AlreadyBoundSession_SurfacesAWarningWhenTheWorkerReportsADifferentProject()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\Session.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // No explicit projectPath: TryResolve forwards the bound path itself, so the FakeWorker
        // scenario key IS the bound path (see the "C:\\bound\\Session.ap21" case).
        var result = await client.GetProjectStatusAsync(null);

        Assert.True(result.Success);
        // Finding 1 is containment only: the divergence is surfaced, the session binding itself
        // is untouched (still bound to what it was bound to before this call).
        Assert.Equal("C:\\bound\\Session.ap21", binding.BoundProjectPath);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("C:\\bound\\Session.ap21", StringComparison.Ordinal)
                && w.Contains("C:\\actual\\Other.ap21", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AlreadyBoundSession_NoSpuriousWarningWhenTheWorkerReportsTheSameProject()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\stable\\Project.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Bound path equals the FakeWorker "C:\\stable\\Project.ap21" scenario key, and that
        // scenario reports the identical resolvedProjectPath back - no divergence.
        var result = await client.GetProjectStatusAsync(null);

        Assert.True(result.Success);
        Assert.Equal("C:\\stable\\Project.ap21", binding.BoundProjectPath);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task AlreadyBoundSession_NoSpuriousWarningWhenTheWorkerReportsAnEquivalentlySpelledPath()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\equivalent\\Project.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Finding 2 regression: the divergence check used a raw string.Equals, which would treat
        // a forward-vs-back-slash spelling of the identical path as a different project and warn
        // on every call. ProjectSessionBinding.IsBoundTo canonicalizes both sides (matching
        // TryResolve/Bind's own "same project?" logic) so an equivalent spelling must NOT warn.
        var result = await client.GetProjectStatusAsync(null);

        Assert.True(result.Success);
        Assert.Equal("C:\\equivalent\\Project.ap21", binding.BoundProjectPath);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task SaveProjectAsWithRebind_NoDivergenceWarningWhenTheWorkerReportsTheCopiedProjectPath()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\Session.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Finding 1 regression: save_project_as with rebind=true deliberately changes which
        // project is attached (the worker closes the original and opens the copy before this
        // call returns). Reusing the "C:\\bound\\Session.ap21" FakeWorker scenario - which
        // reports a genuinely different resolvedProjectPath ("C:\\actual\\Other.ap21") - proves
        // this documented attachment change no longer produces the divergence warning that an
        // ordinary bound call gets under the identical worker response (see
        // AlreadyBoundSession_SurfacesAWarningWhenTheWorkerReportsADifferentProject below, which
        // exercises the same scenario through GetProjectStatusAsync and still warns).
        var result = await client.SaveProjectAsAsync(
            projectPath: null,
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: true);

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task NonExecutableWorkerPath_ProducesActionableWin32Message()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"tia-fake-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(bogus, "not an executable");
        try
        {
            using var client = CreateClient(workerPath: bogus);
            var result = await client.GetProjectStatusAsync("ok");

            Assert.False(result.Success);
            Assert.Contains(".NET Framework 4.8", result.Error);
            Assert.Contains("openness-worker", result.Error);
            Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        }
        finally
        {
            File.Delete(bogus);
        }
    }
}
