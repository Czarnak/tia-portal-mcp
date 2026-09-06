using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class HardwarePageWorkerClientTests
{
    private const string ProjectPath = @"C:\fixtures\echo";

    [Fact]
    public async Task FirstPage_CapturesHostSnapshotAfterEnteringTheSerializedBindingOperation()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateFakeWorkerClient(binding);
        var bootstrap = await FirstPageAsync(client, "hardware-pagination");
        var identity = AssertCompleteIdentity(bootstrap.WorkerResult);
        var before = binding.CaptureSnapshot();
        var enteredLease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = client.ExecuteWithPinnedBindingAsync(
            before,
            async () =>
            {
                enteredLease.TrySetResult(true);
                await releaseLease.Task;
                return WorkerCallResult.Ok("{}");
            });
        await enteredLease.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var firstPage = client.ReadHardwarePageCandidatesAsync(
            projectPath: null,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: null,
            requiredHostBinding: null,
            expectedSessionIdentity: null);

        Assert.True(binding.BindVerified(identity, forceRebind: false, out var bindError), bindError);
        var rebound = binding.CaptureSnapshot();
        releaseLease.TrySetResult(true);

        var blocked = await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        var call = await firstPage.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(blocked.Success);
        Assert.True(call.WorkerResult.Success, call.WorkerResult.Error);
        Assert.True(rebound.SameBinding(call.HostBinding));
        Assert.False(before.SameBinding(call.HostBinding));
    }

    [Fact]
    public async Task UnboundContinuation_UsesCursorPathAndIdentityWithoutCreatingAHostBinding()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateFakeWorkerClient(binding);
        var first = await FirstPageAsync(client, ProjectPath);
        var expectedIdentity = AssertCompleteIdentity(first.WorkerResult);

        var continuation = await client.ReadHardwarePageCandidatesAsync(
            projectPath: null,
            deviceName: "PLC_1",
            plcName: "CPU_1",
            includeIoDetails: true,
            includeTagMatches: true,
            pageSize: 23,
            continuation: Continuation(),
            requiredHostBinding: first.HostBinding,
            expectedSessionIdentity: expectedIdentity);

        Assert.True(continuation.WorkerResult.Success, continuation.WorkerResult.Error);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, continuation.HostBinding.State);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, binding.BindingState);
        using var request = JsonDocument.Parse(continuation.WorkerResult.Payload);
        Assert.Equal(ProjectPath, request.RootElement.GetProperty("projectPath").GetString());
        Assert.Equal(23, request.RootElement.GetProperty("hardwarePageSize").GetInt32());
        Assert.Equal(7, request.RootElement
            .GetProperty("hardwarePageContinuation")
            .GetProperty("offset")
            .GetInt32());
        AssertIdentity(expectedIdentity, request.RootElement.GetProperty("expectedSessionIdentity"));
    }

    [Fact]
    public async Task Continuation_AcceptsEquivalentExplicitPathAndRejectsDifferentPathWithoutSending()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateFakeWorkerClient(binding);
        var first = await FirstPageAsync(client, ProjectPath);
        var expectedIdentity = AssertCompleteIdentity(first.WorkerResult);

        var equivalent = await client.ReadHardwarePageCandidatesAsync(
            projectPath: "c:/FIXTURES/echo",
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: Continuation(),
            requiredHostBinding: first.HostBinding,
            expectedSessionIdentity: expectedIdentity);
        Assert.True(equivalent.WorkerResult.Success, equivalent.WorkerResult.Error);

        var different = await client.ReadHardwarePageCandidatesAsync(
            projectPath: @"C:\different\echo",
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: Continuation(),
            requiredHostBinding: first.HostBinding,
            expectedSessionIdentity: expectedIdentity);

        Assert.False(different.WorkerResult.Success);
        Assert.Equal(
            WorkerFailureCategories.CursorBindingMismatch,
            different.WorkerResult.FailureCategory);

        var nextRequest = await client.ReadHardwarePageCandidatesAsync(
            projectPath: "ok",
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: null,
            requiredHostBinding: null,
            expectedSessionIdentity: null);
        Assert.True(nextRequest.WorkerResult.Success, nextRequest.WorkerResult.Error);
        Assert.Equal("{\"seq\":3}", nextRequest.WorkerResult.Payload);
    }

    [Theory]
    [InlineData("bindingId")]
    [InlineData("revision")]
    [InlineData("path")]
    public async Task Continuation_RejectsChangedBoundHostSnapshotLocally(string changedField)
    {
        var binding = new ProjectSessionBinding(null);
        var identity = Identity(@"C:\Projects\Bound.ap21");
        Assert.True(binding.BindVerified(identity, forceRebind: false, out var bindError), bindError);
        var current = binding.CaptureSnapshot();
        var required = new ProjectBindingSnapshot(
            current.State,
            changedField == "bindingId" ? "different-binding" : current.BindingId,
            changedField == "revision" ? current.Revision + 1 : current.Revision,
            changedField == "path" ? @"C:\Projects\Other.ap21" : current.ProjectPath,
            current.WorkerSessionId,
            current.SessionGeneration,
            current.PortalProcessId,
            current.InvalidatedReason);
        using var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: MissingWorkerPath(),
            requestTimeout: TimeSpan.FromSeconds(1));

        var call = await client.ReadHardwarePageCandidatesAsync(
            projectPath: null,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: Continuation(),
            requiredHostBinding: required,
            expectedSessionIdentity: identity);

        Assert.False(call.WorkerResult.Success);
        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, call.WorkerResult.FailureCategory);
        Assert.True(current.SameBinding(call.HostBinding));
    }

    [Fact]
    public async Task Continuation_ChecksRequiredSnapshotAfterEnteringTheSerializedBindingOperation()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: MissingWorkerPath(),
            requestTimeout: TimeSpan.FromSeconds(1));
        var required = binding.CaptureSnapshot();
        var enteredLease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = client.ExecuteWithPinnedBindingAsync(
            required,
            async () =>
            {
                enteredLease.TrySetResult(true);
                await releaseLease.Task;
                return WorkerCallResult.Ok("{}");
            });
        await enteredLease.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var continuation = client.ReadHardwarePageCandidatesAsync(
            projectPath: null,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: Continuation(),
            requiredHostBinding: required,
            expectedSessionIdentity: Identity(ProjectPath));
        Assert.False(continuation.IsCompleted);

        Assert.True(binding.Bind(@"C:\Projects\NowBound.ap21", forceRebind: false, out var bindError), bindError);
        releaseLease.TrySetResult(true);

        var blocked = await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        var call = await continuation.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(blocked.Success);
        Assert.False(call.WorkerResult.Success);
        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, call.WorkerResult.FailureCategory);
        Assert.Equal(ProjectBindingSnapshot.ConfiguredUnverifiedState, call.HostBinding.State);
    }

    [Theory]
    [InlineData("hardware-pagination-missing-continuation-identity", WorkerFailureCategories.ProtocolError)]
    [InlineData("hardware-pagination-identity-drift", WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData("hardware-pagination-binding-conflict", WorkerFailureCategories.CursorBindingMismatch)]
    public async Task Continuation_MapsEnvelopeIdentityFailures(
        string scenario,
        string expectedCategory)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateFakeWorkerClient(binding);
        var first = await FirstPageAsync(client, scenario);
        var identity = AssertCompleteIdentity(first.WorkerResult);

        var call = await client.ReadHardwarePageCandidatesAsync(
            projectPath: null,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: Continuation(),
            requiredHostBinding: first.HostBinding,
            expectedSessionIdentity: identity);

        Assert.False(call.WorkerResult.Success);
        Assert.Equal(expectedCategory, call.WorkerResult.FailureCategory);
    }

    [Fact]
    public async Task SuccessfulFirstPage_DoesNotAcceptPayloadIdentityWhenEnvelopeIdentityIsMissing()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateFakeWorkerClient(binding);

        var call = await FirstPageAsync(client, "hardware-pagination-payload-only-identity");

        Assert.False(call.WorkerResult.Success);
        Assert.Equal(WorkerFailureCategories.ProtocolError, call.WorkerResult.FailureCategory);
    }

    [Fact]
    public async Task FirstPageFailure_RetainsItsOriginalFailureCategory()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateFakeWorkerClient(binding);

        var call = await FirstPageAsync(client, "worker-error-with-category");

        Assert.False(call.WorkerResult.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, call.WorkerResult.FailureCategory);
    }

    private static OpennessWorkerClient CreateFakeWorkerClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            requestTimeout: TimeSpan.FromSeconds(5));

    private static Task<HardwarePageWorkerCallResult> FirstPageAsync(
        OpennessWorkerClient client,
        string projectPath)
        => client.ReadHardwarePageCandidatesAsync(
            projectPath,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            pageSize: 50,
            continuation: null,
            requiredHostBinding: null,
            expectedSessionIdentity: null);

    private static HardwarePageContinuationInfo Continuation()
        => new(
            OrderingVersion: 1,
            QueryHash: new string('a', 64),
            SnapshotHash: new string('b', 64),
            Offset: 7);

    private static WorkerSessionIdentity Identity(string projectPath)
        => new()
        {
            WorkerSessionId = "hardware-page-test-worker",
            SessionGeneration = 1,
            PortalProcessId = 4242,
            ProjectPath = projectPath
        };

    private static WorkerSessionIdentity AssertCompleteIdentity(WorkerCallResult result)
    {
        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.SessionIdentity);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionIdentity!.WorkerSessionId));
        Assert.True(result.SessionIdentity.SessionGeneration >= 0);
        Assert.True(result.SessionIdentity.PortalProcessId > 0);
        Assert.False(string.IsNullOrWhiteSpace(result.SessionIdentity.ProjectPath));
        return result.SessionIdentity;
    }

    private static void AssertIdentity(WorkerSessionIdentity expected, JsonElement actual)
    {
        Assert.Equal(expected.WorkerSessionId, actual.GetProperty("workerSessionId").GetString());
        Assert.Equal(expected.SessionGeneration, actual.GetProperty("sessionGeneration").GetInt64());
        Assert.Equal(expected.PortalProcessId, actual.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(expected.ProjectPath, actual.GetProperty("projectPath").GetString());
    }

    private static string MissingWorkerPath()
        => Path.Combine(Path.GetTempPath(), "missing-hardware-page-worker", "worker.exe");

}
