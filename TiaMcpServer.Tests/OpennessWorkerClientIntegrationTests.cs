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
    public async Task CollapsedOpenProject_PreviewThenApply_WorkerFailureRendersFailureCategoryNeverSuccessShaped()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();

        // "worker-error-with-category" is a FakeWorker scenario, not a real path; DescribePathState
        // just reports it as non-existent, so preview/apply token validation proceeds normally and
        // only the worker call itself (inside apply) fails.
        const string projectPath = "worker-error-with-category";

        var preview = await ProjectLifecycleTools.OpenProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.OpenProject(
            client,
            safety,
            projectPath: projectPath,
            confirm: true,
            safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);
        var root = appliedDoc.RootElement;

        Assert.Equal("open_project", root.GetProperty("toolName").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
        Assert.Equal("invalid value", root.GetProperty("error").GetString());
        // BuildApplyResult's failure branch must never be success-shaped: no operationResult,
        // no verification field, even though OpenProject's apply path always requests one.
        Assert.False(root.TryGetProperty("operationResult", out _));
        Assert.False(root.TryGetProperty("verification", out _));
    }

    [Fact]
    public async Task OpenProject_BlankProjectPath_IsValidationErrorNotBindingConflict()
    {
        using var client = CreateClient();

        // CanBind's single out-string covers two distinct reasons ("Project path is required."
        // and an already-bound conflict); a blank path must be categorized as caller input error,
        // not binding_conflict, and OpenProjectAsync must check this before ever calling CanBind.
        var result = await client.OpenProjectAsync("   ", forceRebind: false);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
        Assert.Equal("Project path is required.", result.Error);
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

        // ReadHardwareConfigAsync (not GetProjectStatusAsync) is the vehicle here deliberately:
        // this test exercises SendBoundProjectRequestAsync's default bind-on-success-if-unbound
        // behavior, which every OTHER call site still has - GetProjectStatusAsync itself opted
        // out of it (see UnboundSession_DirectStatusSuccess_DoesNotBindSession below).
        var succeeded = await client.ReadHardwareConfigAsync("ok-with-resolved-path");
        var changedProject = await client.ReadHardwareConfigAsync("worker-error");

        Assert.True(succeeded.Success);
        Assert.False(changedProject.Success);
        Assert.Contains("already bound to project 'C:\\resolved\\Ground.ap21'", changedProject.Error);
    }

    [Fact]
    public async Task UnboundSession_DirectStatusSuccess_DoesNotBindSession()
    {
        // The direct status read must never bind an unbound session, even when the worker
        // reports a resolved project path - unlike every other read/write call site, which
        // still binds on success (see SuccessfulFirstRequest_BindsToTheResolvedPathAndRejectsADifferentProjectAfterward
        // above, using ReadHardwareConfigAsync as the vehicle for that unchanged behavior).
        var binding = new ProjectSessionBinding(null);
        using var boundClient = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await boundClient.GetProjectStatusAsync("ok-with-resolved-path");

        Assert.True(result.Success);
        Assert.Null(binding.BoundProjectPath);
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

    // --- Task 2: direct status vs. internal lifecycle probe routing -----------------------

    private static string? ExtractEchoedMethod(string payload)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(payload);
        return doc.RootElement.TryGetProperty("method", out var method) ? method.GetString() : null;
    }

    [Fact]
    public async Task GetProjectStatusAsync_SendsGetProjectStatusOperationOnly()
    {
        using var client = CreateClient();

        var result = await client.GetProjectStatusAsync("echo");

        Assert.True(result.Success);
        Assert.Equal("get_project_status", ExtractEchoedMethod(result.Payload));
    }

    [Fact]
    public async Task ProbeProjectStatusForLifecycleAsync_SendsProbeOperationOnly()
    {
        using var client = CreateClient();

        var result = await client.ProbeProjectStatusForLifecycleAsync("echo");

        Assert.True(result.Success);
        Assert.Equal("probe_project_status_for_lifecycle", ExtractEchoedMethod(result.Payload));
    }

    [Fact]
    public async Task DirectGetProjectStatus_UsesGetProjectStatusOperationOnly()
    {
        using var client = CreateClient();

        // "direct-status-only" fails the call unless the worker request's method is exactly
        // get_project_status - proving the user-facing tool never routes through the internal
        // lifecycle probe.
        var result = await ProjectLifecycleTools.GetProjectStatus(client, projectPath: "direct-status-only");
        using var doc = System.Text.Json.JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task NoProjectOpen_DirectStatusReturnsIsOpenFalse()
    {
        using var client = CreateClient();

        var result = await ProjectLifecycleTools.GetProjectStatus(client, projectPath: "status-no-project");
        using var doc = System.Text.Json.JsonDocument.Parse(result);

        Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        using var payloadDoc = System.Text.Json.JsonDocument.Parse(doc.RootElement.GetProperty("payload").GetString()!);
        Assert.False(payloadDoc.RootElement.GetProperty("isOpen").GetBoolean());
    }

    [Fact]
    public async Task SaveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        const string projectPath = "lifecycle-probe-only";

        var preview = await ProjectLifecycleTools.SaveProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.SaveProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task SaveProjectAs_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        const string projectPath = "lifecycle-probe-only";

        var preview = await ProjectLifecycleTools.SaveProjectAs(
            client, safety, targetDirectory: "C:\\Target", targetName: "Copy", projectPath: projectPath, rebind: false);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.SaveProjectAs(
            client, safety, targetDirectory: "C:\\Target", targetName: "Copy", projectPath: projectPath, rebind: false,
            confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ArchiveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        const string projectPath = "lifecycle-probe-only";

        var preview = await ProjectLifecycleTools.ArchiveProject(
            client, safety, archiveDirectory: "C:\\Archives", archiveName: "Backup", projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.ArchiveProject(
            client, safety, archiveDirectory: "C:\\Archives", archiveName: "Backup", projectPath: projectPath,
            confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CloseProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = CreateClient();
        const string projectPath = "lifecycle-probe-only";

        var preview = await ProjectLifecycleTools.CloseProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.CloseProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }
}
