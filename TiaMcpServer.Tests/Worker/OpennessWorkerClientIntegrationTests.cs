using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// Drives the real persistent IPC pipeline against TiaMcpServer.FakeWorker. Each test owns
/// its client (and therefore its worker process); clients are disposed so no fake worker
/// outlives its test. One class so xunit runs these sequentially.
/// </summary>
public class OpennessWorkerClientIntegrationTests
{
    private static OpennessWorkerClient CreateClient(
        string? workerPath = null,
        TimeSpan? requestTimeout = null,
        ProjectSessionBinding? binding = null,
        OperationAccessPolicy? accessPolicy = null)
        => new(
            binding ?? new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath ?? FakeWorkerLocator.Locate(),
            requestTimeout: requestTimeout,
            accessPolicy: accessPolicy);

    private const string InspectStateBeforeRetryGuidance =
        "The write outcome is unknown. Inspect current project state before retrying.";

    [Fact]
    public async Task ReadHardwarePageCandidatesAsync_ForwardsTheDedicatedInternalRequest()
    {
        using var client = CreateClient();

        var call = await client.ReadHardwarePageCandidatesAsync(
            projectPath: "echo",
            deviceName: "PLC_1",
            plcName: "CPU_1",
            includeIoDetails: true,
            includeTagMatches: true,
            pageSize: 17,
            continuation: null,
            requiredHostBinding: null,
            expectedSessionIdentity: null);

        Assert.True(call.WorkerResult.Success, call.WorkerResult.Error);
        using var request = System.Text.Json.JsonDocument.Parse(call.WorkerResult.Payload);
        Assert.Equal(
            "read_hardware_page_candidates",
            request.RootElement.GetProperty("method").GetString());
        Assert.Equal("PLC_1", request.RootElement.GetProperty("deviceName").GetString());
        Assert.Equal("CPU_1", request.RootElement.GetProperty("plcName").GetString());
        Assert.True(request.RootElement.GetProperty("includeIoDetails").GetBoolean());
        Assert.True(request.RootElement.GetProperty("includeTagMatches").GetBoolean());
        Assert.Equal(17, request.RootElement.GetProperty("hardwarePageSize").GetInt32());
        Assert.Equal(ProjectBindingSnapshot.UnboundState, call.HostBinding.State);
    }

    [Fact]
    public async Task ExecuteWithPinnedBindingAsync_RejectsAStaleSnapshotWithoutInvokingTheOperation()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = "C:\\Projects\\A.ap21"
            },
            forceRebind: false,
            out _));
        var projectA = binding.CaptureSnapshot();

        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-b",
                SessionGeneration = 1,
                PortalProcessId = 4343,
                ProjectPath = "C:\\Projects\\B.ap21"
            },
            forceRebind: true,
            out _));

        using var client = CreateClient(binding: binding);
        var invoked = false;

        var execution = await client.ExecuteWithPinnedBindingAsync(
            projectA,
            () =>
            {
                invoked = true;
                return Task.FromResult(WorkerCallResult.Ok("{}"));
            });

        Assert.False(execution.Success);
        Assert.False(invoked);
        Assert.Null(execution.Value);
        Assert.NotNull(execution.Failure);
        Assert.Equal(WorkerFailureCategories.BindingConflict, execution.Failure!.FailureCategory);
        Assert.Contains("changed after preview", execution.Failure.Error);
    }

    [Fact]
    public async Task PinnedBindingLease_BlocksConcurrentRebindAndKeepsNestedReadsOnThePinnedProject()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding: binding);

        var openedA = await client.OpenProjectAsync("C:\\stable\\Project.ap21", forceRebind: false);
        Assert.True(openedA.Success, openedA.Error);
        var projectA = binding.CaptureSnapshot();
        Assert.True(projectA.IsVerified);

        var enteredLease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runNestedRead = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var nestedReadObserved = new TaskCompletionSource<WorkerCallResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var pinnedTask = client.ExecuteWithPinnedBindingAsync(
            projectA,
            async () =>
            {
                enteredLease.TrySetResult(true);
                await runNestedRead.Task;
                var nestedRead = await client.GetProjectStatusAsync(projectPath: null);
                nestedReadObserved.TrySetResult(nestedRead);
                await releaseLease.Task;
                return nestedRead;
            });

        await enteredLease.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var rebindTask = client.OpenProjectAsync("C:\\open\\Line.ap21", forceRebind: true);

        try
        {
            var earlyCompletion = await Task.WhenAny(rebindTask, Task.Delay(TimeSpan.FromMilliseconds(200)));
            Assert.NotSame(rebindTask, earlyCompletion);

            runNestedRead.TrySetResult(true);
            var nestedRead = await nestedReadObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(nestedRead.Success, nestedRead.Error);
            Assert.False(rebindTask.IsCompleted);
            Assert.True(projectA.SameBinding(binding.CaptureSnapshot()));
        }
        finally
        {
            runNestedRead.TrySetResult(true);
            releaseLease.TrySetResult(true);
        }

        var pinned = await pinnedTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(pinned.Success);
        Assert.True(pinned.Value!.Success, pinned.Value.Error);

        var rebound = await rebindTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(rebound.Success, rebound.Error);
        Assert.Equal("C:\\open\\Line.ap21", binding.BoundProjectPath);
        Assert.False(projectA.SameBinding(binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task ReadWriteStartupProject_FirstBrowseAutoVerifiesWithStatusThenReads()
    {
        // The "ok" FakeWorker scenario reports a monotonically increasing sequence number.
        // browse_project_tree must therefore return seq=2: seq=1 was the automatic read-only
        // get_project_status call that promoted the configured startup path to Verified.
        var binding = new ProjectSessionBinding("ok");
        var policy = new OperationAccessPolicy(McpAccessMode.ReadWrite);
        using var client = CreateClient(binding: binding, accessPolicy: policy);

        var result = await client.BrowseProjectTreeAsync(projectPath: null);

        Assert.True(result.Success, result.Error);
        Assert.Equal("{\"seq\":2}", result.Payload);
        Assert.True(binding.IsVerified);
        Assert.Equal(ProjectBindingSnapshot.VerifiedState, binding.BindingState);
        Assert.NotNull(binding.CaptureSnapshot().ToWorkerIdentity());
    }

    [Fact]
    public async Task CollapsedOpenProject_PreviewThenApply_RoundTrips()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);

        // "C:\\open\\Line.ap21" reports the same path back as resolvedProjectPath, so open can
        // bind to the worker's ground truth (open now requires a resolved path to bind - a bare
        // success with none is postcondition_failed).
        const string projectPath = "C:\\open\\Line.ap21";
        var preview = await ProjectWriteTools.OpenProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.OpenProject(
            client,
            safety,
            projectPath: projectPath,
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
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);

        // "worker-error-with-category" is a FakeWorker scenario, not a real path; DescribePathState
        // just reports it as non-existent, so preview/apply token validation proceeds normally and
        // only the worker call itself (inside apply) fails.
        const string projectPath = "worker-error-with-category";

        var preview = await ProjectWriteTools.OpenProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.OpenProject(
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
    [InlineData("hang")]
    [InlineData("crash")]
    [InlineData("malformed")]
    [InlineData("null-response")]
    public async Task UncertainOutcome_IssuesFailedWriteOnce_ThenRestartedWorkerServesTheNextRequests(string scenario)
    {
        using var client = CreateClient(requestTimeout: TimeSpan.FromSeconds(2));

        var failed = await client.GetProjectStatusAsync(scenario);
        Assert.False(failed.Success);
        Assert.Contains(
            failed.FailureCategory,
            new[] { WorkerFailureCategories.WorkerTimeout, WorkerFailureCategories.WorkerCrashed });
        Assert.Equal(InspectStateBeforeRetryGuidance, failed.Error);

        // The failed write was issued exactly once (no internal retry loop). A fresh worker
        // process serves the next request - seq resets to 1, proving the timed-out/lost request
        // was never replayed on a surviving worker - and the request AFTER that is served by the
        // SAME restarted process (seq=2), proving the restart is for the next caller only, not a
        // new process per request.
        var next = await client.GetProjectStatusAsync("ok");
        var afterNext = await client.GetProjectStatusAsync("ok");

        Assert.True(next.Success);
        Assert.Equal("{\"seq\":1}", next.Payload);
        Assert.Equal("{\"seq\":2}", afterNext.Payload);
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
    public async Task UpdateBlockLogic_PostconditionFailure_SurfacesUncertainStateWarningWithoutRetry()
    {
        const string projectPath = "update-block-postcondition-failed";
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding: binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var result = await client.UpdateBlockLogicAsync(
            blockPath: "PLC/Blocks/Main",
            yamlContent: "<Main />",
            projectPath: projectPath);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, result.FailureCategory);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("project state may have changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("attempt 1", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateBlock_PostconditionFailure_SurfacesUncertainStateWarningWithoutRetry()
    {
        const string projectPath = "create-block-postcondition-failed";
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding: binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var result = await client.CreateBlockAsync(
            blockPath: "PLC/Blocks/Created",
            blockType: "FC",
            language: "SCL",
            obEventClass: null,
            projectPath: projectPath);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, result.FailureCategory);
        Assert.Contains(result.Warnings, warning =>
            warning.Contains("project state may have changed", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("attempt 1", result.Error, StringComparison.OrdinalIgnoreCase);
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
    public async Task WorkerReportedTargetNotFoundCategory_PreservesIt()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("worker-error-with-target-not-found-category");

        Assert.False(result.Success);
        Assert.Equal("target not found", result.Error);
        Assert.Equal("target_not_found", result.FailureCategory);
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
    public async Task UnboundSession_UnrelatedReadSuccess_DoesNotBindSession()
    {
        // Phase 5 Plan 2 Task 3 behavior change: an unrelated data read (here
        // ReadHardwareConfigAsync) is BindingTransition.None. A successful such read no longer
        // binds an unbound session as a side effect - only open/create/save-as(rebind) bind. A
        // subsequent read of a DIFFERENT project is therefore still accepted, not rejected as an
        // already-bound conflict (which is exactly what the old bind-on-success behavior caused).
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

        var succeeded = await client.ReadHardwareConfigAsync("ok-with-resolved-path");
        var differentProject = await client.ReadHardwareConfigAsync("ok");

        Assert.True(succeeded.Success);
        Assert.NotNull(succeeded.SessionIdentity);
        // "ok" would be an already-bound binding_conflict if the first read had bound the session
        // to "C:\\resolved\\Ground.ap21"; it succeeds, proving the session stayed unbound.
        Assert.True(differentProject.Success);
        Assert.NotNull(differentProject.SessionIdentity);

        var snapshot = binding.CaptureSnapshot();
        Assert.Equal(ProjectBindingSnapshot.UnboundState, snapshot.State);
        Assert.False(snapshot.IsVerified);
        Assert.Null(snapshot.ProjectPath);
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
    public async Task ConfiguredSession_FailsClosedWhenWorkerReportsADifferentProject()
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

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.BindingState);
        Assert.Equal("C:\\bound\\Session.ap21", binding.BoundProjectPath);
        Assert.Contains("C:\\bound\\Session.ap21", result.Error);
        Assert.Contains("C:\\actual\\Other.ap21", result.Error);
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
        Assert.True(binding.IsVerified);
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
        Assert.True(binding.IsVerified);
    }

    [Fact]
    public async Task RestartedWorkerInvalidatesVerifiedBindingEvenWhenProjectPathIsUnchanged()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var opened = await client.OpenProjectAsync("C:\\stable\\Project.ap21", forceRebind: false);
        Assert.True(opened.Success, opened.Error);
        var original = binding.CaptureSnapshot();
        Assert.True(original.IsVerified);

        // Disposing the persistent transport and reusing the client starts a fresh worker process.
        // The path is identical, but workerSessionId is process-local and therefore must differ.
        client.Dispose();
        var afterRestart = await client.GetProjectStatusAsync(null);

        Assert.False(afterRestart.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, afterRestart.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, binding.BindingState);
        Assert.False(binding.TryGetVerified(original.ProjectPath, out _, out _));
    }

    [Fact]
    public async Task SaveProjectAsWithRebind_NoDivergenceWarningWhenTheWorkerReportsTheCopiedProjectPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Retain the fake process's exact worker identity, matching the verified binding an
        // explicit open would have established. This scenario reports a genuinely
        // different copied path for save_project_as, so the transition must replace the old
        // binding without treating the deliberate attachment change as divergence.
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "lifecycle-probe-only");

        var result = await client.SaveProjectAsAsync(
            projectPath: null,
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: true);

        Assert.True(result.Success, result.Error);
        Assert.Empty(result.Warnings);
        Assert.Equal("C:\\lifecycle\\Copy.ap21", binding.BoundProjectPath);
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
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding: binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");

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
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);
        const string projectPath = "lifecycle-probe-only";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var preview = await ProjectWriteTools.SaveProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.SaveProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task PostWriteVerification_UsesBasicStatusRead_NotExtendedMetadataRead()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);
        const string projectPath = "lifecycle-probe-only";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var preview = await ProjectWriteTools.SaveProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.SaveProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());

        // Post-write verification must read the BASIC status ({"isOpen":true}). The
        // "lifecycle-probe-only" scenario rejects get_project_status - the extended-metadata read -
        // so had verification regressed to it, verification.result would carry that rejection error
        // text instead of a parseable basic status payload.
        var verification = appliedDoc.RootElement.GetProperty("verification");
        Assert.Equal("get_project_status", verification.GetProperty("name").GetString());
        using var verificationDoc = System.Text.Json.JsonDocument.Parse(
            verification.GetProperty("result").GetString()!);
        Assert.True(verificationDoc.RootElement.GetProperty("isOpen").GetBoolean());
    }

    [Fact]
    public async Task SaveProjectAs_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);
        const string projectPath = "lifecycle-probe-only";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        // rebind=true (the only supported mode): the "lifecycle-probe-only" scenario reports a
        // resolvedProjectPath for the save_project_as write so the rebind bind succeeds; the
        // current-state reads still route through the probe, never get_project_status.
        var preview = await ProjectWriteTools.SaveProjectAs(
            client, safety, targetDirectory: "C:\\Target", targetName: "Copy", projectPath: projectPath, rebind: true);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.SaveProjectAs(
            client, safety, targetDirectory: "C:\\Target", targetName: "Copy", projectPath: projectPath, rebind: true,
            confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    // --- Task 4: save_project_as rebind guarantees ------------------------------------------

    [Fact]
    public async Task SaveProjectAsAsync_RebindFalse_IsValidationErrorAndNeverInvokesWorker()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\Session.ap21", forceRebind: false, out _));

        // A worker path that cannot launch: had the guard let the call reach the transport, the
        // result would be a worker launch failure (worker_operation_failed), not validation_error.
        // So validation_error is positive proof the worker was never invoked.
        var unlaunchableWorker = Path.Combine(Path.GetTempPath(), $"tia-nonexistent-{Guid.NewGuid():N}.exe");
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: unlaunchableWorker);

        var result = await client.SaveProjectAsAsync(
            projectPath: null,
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: false);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
        // The existing binding is untouched.
        Assert.Equal("C:\\bound\\Session.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public async Task SaveProjectAs_RebindTrue_BindsOnlyWorkerCopiedPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(
            client,
            binding,
            "ok-with-resolved-path");

        // "ok-with-resolved-path" reports resolvedProjectPath "C:\\resolved\\Ground.ap21" - matching
        // neither the caller's targetDirectory nor targetName. rebind=true must bind the session to
        // the worker's reported copied path, never to caller input.
        var result = await client.SaveProjectAsAsync(
            projectPath: "ok-with-resolved-path",
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: true);

        Assert.True(result.Success);
        Assert.Equal("C:\\resolved\\Ground.ap21", binding.BoundProjectPath);
        Assert.DoesNotContain("Target", binding.BoundProjectPath!);
        Assert.DoesNotContain("Copy", binding.BoundProjectPath!);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task SaveProjectAs_Failure_PreservesOriginalBinding()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\FailingSave.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(
            client,
            binding,
            "C:\\bound\\FailingSave.ap21");

        // The bound path's FakeWorker scenario fails the save_project_as call. A failed rebinding
        // save-as must leave the pre-existing binding exactly as it was - no partial rebind.
        var result = await client.SaveProjectAsAsync(
            projectPath: null,
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: true);

        Assert.False(result.Success);
        Assert.Equal("C:\\bound\\FailingSave.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public async Task SaveProjectAs_MissingCopiedPath_IsPostconditionFailedWithUncertainStateWarning()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(
            client,
            binding,
            "save-as-uncertain-state");
        var before = binding.CaptureSnapshot();

        // The worker reports it could not confirm the copied project path: a postcondition_failed
        // failure carrying the uncertain-state warning. The client must surface it unchanged and
        // retain the verified source binding rather than adopting an unconfirmed copied path.
        var result = await client.SaveProjectAsAsync(
            projectPath: "save-as-uncertain-state",
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: true);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, result.FailureCategory);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("Project state may have changed", StringComparison.Ordinal));
        Assert.True(before.SameBinding(binding.CaptureSnapshot()));
    }

    [Fact]
    public async Task ArchiveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);
        const string projectPath = "lifecycle-probe-only";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var preview = await ProjectWriteTools.ArchiveProject(
            client, safety, archiveDirectory: "C:\\Archives", archiveName: "Backup", projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.ArchiveProject(
            client, safety, archiveDirectory: "C:\\Archives", archiveName: "Backup", projectPath: projectPath,
            confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ArchiveProject_PreviewRejectsArchiveDirectoryInsideProjectFolder_WithoutIssuingSafetyToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);
        const string projectPath = "C:\\Projects\\SimpleProject\\SimpleProject.ap21";
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var preview = await ProjectWriteTools.ArchiveProject(
            client, safety,
            archiveDirectory: "C:\\Projects\\SimpleProject\\Sub",
            archiveName: "Backup",
            mode: "Compressed",
            projectPath: projectPath);

        using var doc = System.Text.Json.JsonDocument.Parse(preview);
        Assert.Equal("archive_project", doc.RootElement.GetProperty("toolName").GetString());
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, doc.RootElement.GetProperty("failureCategory").GetString());
        Assert.Contains("own folder or a subdirectory", doc.RootElement.GetProperty("error").GetString());

        // A rejection, not a preview: no safetyToken is issued, so there is nothing to replay
        // against the worker's real archive_project operation - the caller must fix the path and
        // request a fresh preview instead.
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));

        var auditLineCount = Directory.Exists(audit.Path)
            ? Directory.GetFiles(audit.Path).Sum(file => File.ReadAllLines(file).Length)
            : 0;
        Assert.Equal(0, auditLineCount);
    }

    [Fact]
    public async Task CloseProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus()
    {
        using var audit = new TempAuditDirectory();
        const string projectPath = "lifecycle-probe-only";
        var binding = new ProjectSessionBinding(projectPath);
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        using var client = CreateClient(binding: binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, projectPath);

        var preview = await ProjectWriteTools.CloseProject(client, safety, projectPath: projectPath);
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.CloseProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }

    // --- Task 3: explicit, worker-grounded binding transitions -----------------------------

    [Fact]
    public async Task FailedLifecycleCall_DoesNotChangeExistingBinding()
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\Session.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // A lifecycle call (open, forceRebind so it clears the CanBind gate) that fails at the
        // worker must leave the existing binding exactly as it was - no partial rebind.
        var result = await client.OpenProjectAsync("worker-error", forceRebind: true);

        Assert.False(result.Success);
        Assert.Equal("C:\\bound\\Session.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public async Task DirectStatusSuccess_DoesNotBindUnboundSession()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Direct status is BindingTransition.None: even a success carrying a resolvedProjectPath
        // must not bind an unbound session.
        var result = await client.GetProjectStatusAsync("ok-with-resolved-path");

        Assert.True(result.Success);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public async Task OpenSuccess_BindsWorkerResolvedPath_NotCallerPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Caller asks for "open-resolved-differs"; the worker reports it actually opened
        // "C:\\worker\\Ground.ap21". The session must bind the worker's ground truth, never the
        // caller's argument.
        var result = await client.OpenProjectAsync("open-resolved-differs", forceRebind: false);

        Assert.True(result.Success);
        Assert.Equal("C:\\worker\\Ground.ap21", binding.BoundProjectPath);
        Assert.DoesNotContain("open-resolved-differs", binding.BoundProjectPath!);
    }

    [Fact]
    public async Task CreateSuccess_BindsWorkerResolvedPath_NotCallerPath()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Caller supplies directory "create-resolved-differs" and name "Line"; the worker reports
        // it created "C:\\worker\\Created.ap21". The session must bind the worker's ground truth,
        // never the caller's directory/name arguments.
        var result = await client.CreateProjectAsync(
            projectDirectory: "create-resolved-differs",
            projectName: "Line",
            author: null,
            comment: null);

        Assert.True(result.Success);
        Assert.Equal("C:\\worker\\Created.ap21", binding.BoundProjectPath);
        Assert.DoesNotContain("create-resolved-differs", binding.BoundProjectPath!);
        Assert.DoesNotContain("Line", binding.BoundProjectPath!);
    }

    [Fact]
    public async Task RequiredResolvedPathMissing_ReturnsPostconditionFailed()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // The fake succeeds but omits sessionIdentity. Open requires a complete worker/Portal/
        // project identity to bind, so this is a broken postcondition - never a silent fallback to
        // the caller's path or resolvedProjectPath - and the session stays unbound.
        var result = await client.OpenProjectAsync("missing-session-identity", forceRebind: false);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.PostconditionFailed, result.FailureCategory);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public async Task CloseSuccess_ClearsBinding()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // Close is BindingTransition.Clear: a successful close leaves the session with nothing
        // bound. First retain the exact identity reported by this persistent fake worker so the
        // close request exercises a real Verified precondition rather than an unverified path.
        var observed = await client.GetProjectStatusAsync("C:\\stable\\Project.ap21");
        Assert.True(observed.Success, observed.Error);
        Assert.True(binding.BindVerified(observed.SessionIdentity, forceRebind: false, out _));

        var result = await client.CloseProjectAsync(projectPath: null, saveBeforeClose: false);

        Assert.True(result.Success, result.Error);
        Assert.Null(binding.BoundProjectPath);
    }
}
