using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePaginationCoordinatorTests
{
    private const string SnapshotHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public async Task Executor_UnpagedHardwareUsesTheExistingInvokerProjectionPath()
    {
        var operation = Operation(pageSize: null);
        var worker = WorkerCallResult.Ok(CanonicalJson.Serialize(new HardwareConfigInfo()));
        var pagedCalls = 0;
        var executor = new NetworkReadOperationExecutor(
            _ => Task.FromResult(worker),
            _ =>
            {
                pagedCalls++;
                throw new InvalidOperationException("Paged route must not run.");
            });

        var item = await executor.ExecuteAsync(operation);

        Assert.Equal(0, pagedCalls);
        Assert.Equal(
            CanonicalJson.Serialize(NetworkPayloadContract.Project(operation, worker)),
            CanonicalJson.Serialize(item));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Executor_EitherPaginationFieldSelectsTheCoordinator(
        bool withPageSize,
        bool withCursor)
    {
        var operation = Operation(withPageSize ? 1 : null);
        operation.Cursor = withCursor ? "opaque" : null;
        var pagedCalls = 0;
        var executor = new NetworkReadOperationExecutor(
            _ => throw new InvalidOperationException("Legacy route must not run."),
            request =>
            {
                pagedCalls++;
                return Task.FromResult(Failed(request, WorkerFailureCategories.InvalidCursor));
            });

        var item = await executor.ExecuteAsync(operation);

        Assert.Equal(1, pagedCalls);
        Assert.Equal(WorkerFailureCategories.InvalidCursor, item.Failure!.Category);
    }

    [Fact]
    public async Task Coordinator_InvalidCursorFailsBeforeWorkerAccess()
    {
        var workerCalls = 0;
        var coordinator = Coordinator(
            Unbound(),
            _ =>
            {
                workerCalls++;
                throw new InvalidOperationException("Worker must not be called.");
            });
        var operation = Operation(pageSize: null);
        operation.Cursor = "not-a-cursor";

        var item = await coordinator.ExecuteAsync(operation);

        Assert.Equal(0, workerCalls);
        Assert.Equal(WorkerFailureCategories.InvalidCursor, item.Failure!.Category);
    }

    [Fact]
    public async Task Coordinator_QueryMismatchFailsBeforeWorkerAccess()
    {
        var binding = Unbound();
        var cursor = Cursor(binding, offset: 0, deviceName: "device-a");
        var workerCalls = 0;
        var coordinator = Coordinator(
            binding,
            _ =>
            {
                workerCalls++;
                throw new InvalidOperationException("Worker must not be called.");
            });
        var operation = Operation(pageSize: null);
        operation.DeviceName = "device-b";
        operation.Cursor = cursor;

        var item = await coordinator.ExecuteAsync(operation);

        Assert.Equal(0, workerCalls);
        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, item.Failure!.Category);
    }

    [Fact]
    public async Task Coordinator_CursorOnlyUsesPageSizeFiftyAndInjectsCursorPathIdentityAndBinding()
    {
        var binding = Unbound();
        HardwarePageCandidateCall? observed = null;
        var coordinator = Coordinator(
            binding,
            call =>
            {
                observed = call;
                return Task.FromResult(Success(call, binding, totalDevices: 0));
            });
        var operation = Operation(pageSize: null);
        operation.Cursor = Cursor(binding, offset: 0);

        var item = await coordinator.ExecuteAsync(operation);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.NotNull(observed);
        Assert.Equal(50, observed!.PageSize);
        Assert.Equal(ResolvedPath(), observed.ProjectPath);
        Assert.Equal("worker-session", observed.ExpectedSessionIdentity!.WorkerSessionId);
        Assert.True(binding.SameBinding(observed.RequiredHostBinding!));
        Assert.Equal(0, observed.Continuation!.Offset);
    }

    [Fact]
    public async Task Coordinator_FirstPageCursorRecordsAllAuthoritativeEvidenceAndActualNextOffset()
    {
        var binding = Bound();
        var coordinator = Coordinator(
            binding,
            call => Task.FromResult(Success(call, binding, totalDevices: 2, returnedDevices: 1)));
        var operation = Operation(pageSize: 1);

        var item = await coordinator.ExecuteAsync(operation);
        var page = CanonicalJson.Deserialize<HardwareConfigInfo>(item.Result!.Value.GetRawText());
        var cursor = Codec().Decode(page.Pagination!.NextCursor!);

        Assert.Equal(ResolvedPath(), cursor.ResolvedProjectPath);
        Assert.Equal("worker-session", cursor.SessionIdentity.WorkerSessionId);
        Assert.True(cursor.HostBinding.IsBound);
        Assert.Equal(binding.BindingId, cursor.HostBinding.BindingId);
        Assert.Equal(binding.Revision, cursor.HostBinding.Revision);
        Assert.Equal(QueryHash(operation), cursor.QueryHash);
        Assert.Equal(SnapshotHash, cursor.SnapshotHash);
        Assert.Equal(1, cursor.OrderingVersion);
        Assert.Equal(1, cursor.Offset);
    }

    [Theory]
    [InlineData(WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData(WorkerFailureCategories.CursorSnapshotMismatch)]
    [InlineData(WorkerFailureCategories.CursorOutOfRange)]
    [InlineData(WorkerFailureCategories.ProtocolError)]
    public async Task Coordinator_RetainsApprovedWorkerFailureCategories(string category)
    {
        var coordinator = Coordinator(
            Unbound(),
            _ => Task.FromResult(new HardwarePageWorkerCallResult(
                WorkerCallResult.Fail(category, "safe failure"),
                Unbound())));

        var item = await coordinator.ExecuteAsync(Operation(pageSize: 1));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(category, item.Failure!.Category);
    }

    [Fact]
    public async Task Coordinator_MalformedSuccessPayloadBecomesProtocolErrorWithoutPayloadEcho()
    {
        const string Rejected = "worker-private-locator";
        var binding = Unbound();
        var coordinator = Coordinator(
            binding,
            _ => Task.FromResult(new HardwarePageWorkerCallResult(
                WorkerCallResult.Ok($"{{\"locator\":\"{Rejected}\"}}") with
                {
                    ResolvedProjectPath = ResolvedPath(),
                    SessionIdentity = Identity(),
                },
                binding)));

        var item = await coordinator.ExecuteAsync(Operation(pageSize: 1));

        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(Rejected, CanonicalJson.Serialize(item), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Coordinator_ContinuationResolvedPathChangeIsCursorBindingMismatch()
    {
        var binding = Unbound();
        var coordinator = Coordinator(
            binding,
            call =>
            {
                var success = Success(call, binding, totalDevices: 0);
                return Task.FromResult(success with
                {
                    WorkerResult = success.WorkerResult with
                    {
                        ResolvedProjectPath = @"C:\Projects\Different.ap21",
                    },
                });
            });
        var operation = Operation(pageSize: null);
        operation.Cursor = Cursor(binding, offset: 0);

        var item = await coordinator.ExecuteAsync(operation);

        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, item.Failure!.Category);
    }

    private static HardwarePaginationCoordinator Coordinator(
        ProjectBindingSnapshot binding,
        Func<HardwarePageCandidateCall, Task<HardwarePageWorkerCallResult>> read)
        => new(Codec(), new HardwarePageProjector(Codec()), () => binding, read);

    private static HardwarePageWorkerCallResult Success(
        HardwarePageCandidateCall call,
        ProjectBindingSnapshot binding,
        int totalDevices,
        int returnedDevices = 0)
    {
        var startOffset = call.Continuation?.Offset ?? 0;
        var payload = new HardwarePageCandidateResultInfo(
            OrderingVersion: 1,
            QueryHash: HardwarePageEvidence.CreateQueryHash(
                call.DeviceName,
                call.PlcName,
                call.IncludeIoDetails,
                call.IncludeTagMatches),
            SnapshotHash,
            StartOffset: startOffset,
            TotalDevices: totalDevices,
            TotalSubnets: 0,
            Messages: Array.Empty<string>(),
            DeviceCandidates: Enumerable.Range(0, returnedDevices)
                .Select(index => new HardwareDevicePageCandidateInfo(
                    startOffset + index,
                    HardwarePagePayloadContractTests.Device($"device-{startOffset + index}"),
                    Array.Empty<string>()))
                .ToArray(),
            SubnetCandidates: Array.Empty<HardwareSubnetPageCandidateInfo>());
        var worker = WorkerCallResult.Ok(CanonicalJson.Serialize(payload)) with
        {
            ResolvedProjectPath = ResolvedPath(),
            SessionIdentity = Identity(),
        };
        return new HardwarePageWorkerCallResult(worker, binding);
    }

    private static StructuredOperationItem Failed(NetworkOperationRequest operation, string category)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, "failed"),
            Omission: null,
            SkipReason: null,
            Warnings: Array.Empty<string>());

    private static string Cursor(
        ProjectBindingSnapshot binding,
        int offset,
        string? deviceName = null)
        => Codec().Encode(new HardwarePageCursorState(
            Version: 1,
            ResolvedProjectPath: ResolvedPath(),
            Identity(),
            ProjectBindingCursorState.FromSnapshot(binding),
            HardwarePageEvidence.CreateQueryHash(deviceName, null, false, false),
            OrderingVersion: 1,
            SnapshotHash,
            Offset: offset));

    private static HardwarePageCursorCodec Codec() => new(new byte[32]);

    private static string QueryHash(NetworkOperationRequest operation)
        => HardwarePageEvidence.CreateQueryHash(
            operation.DeviceName,
            operation.PlcName,
            operation.IncludeIoDetails,
            operation.IncludeTagMatches);

    private static NetworkOperationRequest Operation(int? pageSize) => new()
    {
        OperationId = "hardware",
        Operation = "read_hardware_config",
        PageSize = pageSize,
    };

    private static string ResolvedPath() => @"C:\Projects\Paged.ap21";

    private static WorkerSessionIdentity Identity() => new()
    {
        WorkerSessionId = "worker-session",
        SessionGeneration = 3,
        PortalProcessId = 42,
        ProjectPath = ResolvedPath(),
    };

    private static ProjectBindingSnapshot Unbound() => new(
        ProjectBindingSnapshot.UnboundState,
        "unbound-binding",
        0,
        projectPath: null,
        workerSessionId: null,
        sessionGeneration: null,
        portalProcessId: null,
        invalidatedReason: null);

    private static ProjectBindingSnapshot Bound() => new(
        ProjectBindingSnapshot.VerifiedState,
        "bound-binding",
        7,
        ResolvedPath(),
        "worker-session",
        3,
        42,
        invalidatedReason: null);
}
