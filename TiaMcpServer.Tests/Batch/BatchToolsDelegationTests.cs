using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class BatchToolsDelegationTests
{
    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static OpennessWorkerClient CreateReadOnlyClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

    [Fact]
    public async Task PreviewWriteBatch_WrapperMatchesRegisteredReadOnlyRejection()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateReadOnlyClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "op-1",
                Operation = "create_user_constant",
                TableName = "Constants",
                Name = "Gain",
                DataType = "Int",
                Value = "1",
                ProjectPath = "type-content-roundtrip"
            }
        };

        var registered = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var wrapper = await BatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Equal(registered, wrapper);
    }
}
