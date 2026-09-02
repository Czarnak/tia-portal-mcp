using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class TagUpdateSafetySnapshotIdentityEnforcementTests
{
    [Fact]
    public async Task WorkerRejectsSnapshotReadWithMissingIdentity()
    {
        using var transport = CreateFakeWorkerTransport();
        var observed = await PrimeAndReadIdentityAsync(transport);
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_update_tag_safety_snapshot",
            ProjectPath = observed.ProjectPath,
            PlcName = "PLC_1",
            TableName = "Default tag table",
            FolderPath = "/",
            Name = "MotorReady",
            ExpectedSessionIdentity = null,
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    [Theory]
    [InlineData("workerSessionId")]
    [InlineData("sessionGeneration")]
    [InlineData("portalProcessId")]
    [InlineData("projectPath")]
    public async Task WorkerRejectsEveryMismatchedSnapshotIdentityField(string field)
    {
        using var transport = CreateFakeWorkerTransport();
        var observed = await PrimeAndReadIdentityAsync(transport);
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_update_tag_safety_snapshot",
            ProjectPath = observed.ProjectPath,
            PlcName = "PLC_1",
            TableName = "Default tag table",
            FolderPath = "/",
            Name = "MotorReady",
            ExpectedSessionIdentity = CopyWithChangedField(observed, field),
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }

    private static PersistentWorkerTransport CreateFakeWorkerTransport()
        => new(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));

    private static async Task<WorkerSessionIdentity> PrimeAndReadIdentityAsync(PersistentWorkerTransport transport)
    {
        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_project_status",
            ProjectPath = "tag-update-flag-drift",
        });

        Assert.True(response.Success, response.Error);
        return Assert.IsType<WorkerSessionIdentity>(response.SessionIdentity);
    }

    private static WorkerSessionIdentity CopyWithChangedField(WorkerSessionIdentity source, string field)
        => new()
        {
            WorkerSessionId = field == "workerSessionId" ? source.WorkerSessionId + "-different" : source.WorkerSessionId,
            SessionGeneration = field == "sessionGeneration" ? source.SessionGeneration + 1 : source.SessionGeneration,
            PortalProcessId = field == "portalProcessId" ? source.PortalProcessId + 1 : source.PortalProcessId,
            ProjectPath = field == "projectPath" ? source.ProjectPath + ".different" : source.ProjectPath,
        };
}
