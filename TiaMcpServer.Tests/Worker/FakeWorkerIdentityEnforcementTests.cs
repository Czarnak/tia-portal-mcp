using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class FakeWorkerIdentityEnforcementTests
{
    [Fact]
    public async Task ProtectedRequestWithoutExpectedIdentityFailsBeforeScenarioDispatch()
    {
        using var transport = CreateTransport();
        await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip"
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Theory]
    [InlineData("workerSessionId")]
    [InlineData("sessionGeneration")]
    [InlineData("portalProcessId")]
    [InlineData("projectPath")]
    public async Task ProtectedRequestRejectsEveryMismatchedIdentityField(string field)
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = Change(observed, field)
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task ProtectedRequestRejectsARequestPathOutsideTheExpectedProject()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip-other",
            ExpectedSessionIdentity = observed
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task OptionalObserveRequestStillRejectsASuppliedMismatchedIdentity()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_project_status",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = Change(observed, "workerSessionId")
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Fact]
    public async Task RejectedRequestDoesNotStampResponseOrMutateTheFakeWorkerSession()
    {
        using var transport = CreateTransport();
        var observed = await PrimeAsync(transport);

        var rejected = await transport.SendAsync(new WorkerRequest
        {
            Method = "probe_project_status_for_lifecycle",
            ProjectPath = "network-roundtrip-other",
            ExpectedSessionIdentity = observed
        });

        Assert.False(rejected.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, rejected.FailureCategory);
        Assert.Null(rejected.SessionIdentity);

        var afterRejection = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config",
            ProjectPath = "network-roundtrip",
            ExpectedSessionIdentity = observed
        });

        Assert.True(afterRejection.Success, afterRejection.Error);
        var identityAfterRejection = Assert.IsType<WorkerSessionIdentity>(
            afterRejection.SessionIdentity);
        Assert.Equal(observed.WorkerSessionId, identityAfterRejection.WorkerSessionId);
        Assert.Equal(observed.SessionGeneration, identityAfterRejection.SessionGeneration);
        Assert.Equal(observed.PortalProcessId, identityAfterRejection.PortalProcessId);
        Assert.Equal(observed.ProjectPath, identityAfterRejection.ProjectPath);
    }

    private static PersistentWorkerTransport CreateTransport()
        => new(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));

    private static async Task<WorkerSessionIdentity> PrimeAsync(
        PersistentWorkerTransport transport)
    {
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config",
            ProjectPath = "network-roundtrip"
        });

        Assert.True(response.Success, response.Error);
        return Assert.IsType<WorkerSessionIdentity>(response.SessionIdentity);
    }

    private static WorkerSessionIdentity Change(
        WorkerSessionIdentity source,
        string field)
        => new()
        {
            WorkerSessionId = field == "workerSessionId"
                ? source.WorkerSessionId + "-different"
                : source.WorkerSessionId,
            SessionGeneration = field == "sessionGeneration"
                ? source.SessionGeneration + 1
                : source.SessionGeneration,
            PortalProcessId = field == "portalProcessId"
                ? source.PortalProcessId + 1
                : source.PortalProcessId,
            ProjectPath = field == "projectPath"
                ? source.ProjectPath + ".different"
                : source.ProjectPath
        };
}
