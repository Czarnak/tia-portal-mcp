using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationFakeWorkerTests
{
    [Fact]
    public async Task ApplyWriteBatch_UpdateTag_SameObjectDriftFailsWithStateChanged()
    {
        await AssertDriftRejected("tag-safety-same-object-drift", new()
        {
            OperationId = "u1", Operation = "update_tag", Name = "Start", NewName = "Start_1"
        });
    }

    [Theory]
    [InlineData("Start_1")]
    [InlineData("AddressOnly")]
    public async Task ApplyWriteBatch_CreateTag_RelevantCollisionDriftFailsWithStateChanged(string name)
    {
        await AssertDriftRejected("tag-safety-collision-drift", new()
        {
            OperationId = "c1", Operation = "create_tag", Name = name,
            DataType = "Bool", LogicalAddress = "%I0.1"
        });
    }

    [Fact]
    public async Task ApplyWriteBatch_DeleteTag_IgnoresUnrelatedSiblingTableDrift()
    {
        using var fixture = await Fixture.Create("tag-safety-unrelated-sibling");
        var operations = fixture.Operations(new()
        {
            OperationId = "d1", Operation = "delete_tag", Name = "Start"
        });
        var preview = await WriteBatchTools.PreviewWriteBatch(fixture.Client, fixture.Safety, operations);
        var token = ExtractToken(preview);
        var before = await fixture.Observe();
        Assert.Equal(0, before.GetProperty("mutationCount").GetInt32());
        Assert.Equal(1, before.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal("Outputs", before.GetProperty("siblingTableName").GetString());
        Assert.Equal("Before", before.GetProperty("siblingTagName").GetString());

        var apply = await WriteBatchTools.ApplyWriteBatch(fixture.Client, fixture.Safety, operations,
            confirm: true, safetyToken: token);

        AssertSucceeded(apply, "d1");
        var after = await fixture.Observe();
        Assert.Equal("After", after.GetProperty("siblingTagName").GetString());
        Assert.Equal(2, after.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal(0, after.GetProperty("broadReadCount").GetInt32());
        Assert.Equal(1, after.GetProperty("mutationCount").GetInt32());
        Assert.Equal(2, after.GetProperty("snapshotReadsAtMutation").GetInt32());
        Assert.False(after.GetProperty("targetExists").GetBoolean());
    }

    [Fact]
    public async Task ApplyWriteBatch_DeleteTagTable_ExportDriftFailsWithStateChanged()
    {
        await AssertDriftRejected("tag-safety-delete-table-export-drift", new()
        {
            OperationId = "t1", Operation = "delete_tag_table"
        });
    }

    [Fact]
    public async Task ApplyWriteBatch_CreateUserConstant_ReReadsOnApplyInsteadOfReusingPreviewCache()
    {
        await AssertDriftRejected("tag-safety-reread", new()
        {
            OperationId = "uc1", Operation = "create_user_constant", Name = "DebounceMs",
            DataType = "Int", Value = "50"
        });
    }

    [Fact]
    public async Task ApplyWriteBatch_UpdateTag_StableStateSucceedsOnceThenRejectsReplay()
    {
        using var fixture = await Fixture.Create("tag-safety-authorized-apply");
        var operations = fixture.Operations(new()
        {
            OperationId = "u1", Operation = "update_tag", Name = "Start", NewName = "Start_1"
        });
        var preview = await WriteBatchTools.PreviewWriteBatch(fixture.Client, fixture.Safety, operations);
        var token = ExtractToken(preview);
        var before = await fixture.Observe();
        Assert.Equal(1, before.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal(0, before.GetProperty("mutationCount").GetInt32());

        var apply = await WriteBatchTools.ApplyWriteBatch(fixture.Client, fixture.Safety, operations,
            confirm: true, safetyToken: token);
        AssertSucceeded(apply, "u1");
        var after = await fixture.Observe();
        Assert.Equal(2, after.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal(2, after.GetProperty("snapshotReadsAtMutation").GetInt32());
        Assert.Equal("Start_1", after.GetProperty("targetTagName").GetString());
        Assert.Equal(1, after.GetProperty("mutationCount").GetInt32());

        var replay = await WriteBatchTools.ApplyWriteBatch(fixture.Client, fixture.Safety, operations,
            confirm: true, safetyToken: token);
        using var document = JsonDocument.Parse(replay);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        // The existing cheap envelope rejection returns success/error without a category.
        Assert.Contains("expired, consumed, or unknown", document.RootElement.GetProperty("error").GetString());
        var replayState = await fixture.Observe();
        Assert.Equal(1, replayState.GetProperty("mutationCount").GetInt32());
        Assert.Equal(2, replayState.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal(0, replayState.GetProperty("broadReadCount").GetInt32());
    }

    private static async Task AssertDriftRejected(string scenario, BatchOperationRequest operation)
    {
        using var fixture = await Fixture.Create(scenario);
        var operations = fixture.Operations(operation);
        var preview = await WriteBatchTools.PreviewWriteBatch(fixture.Client, fixture.Safety, operations);
        var token = ExtractToken(preview);
        var before = await fixture.Observe();
        Assert.Equal(1, before.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal(0, before.GetProperty("mutationCount").GetInt32());

        var apply = await WriteBatchTools.ApplyWriteBatch(fixture.Client, fixture.Safety, operations,
            confirm: true, safetyToken: token);

        Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(apply);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        var after = await fixture.Observe();
        Assert.Equal(2, after.GetProperty("snapshotReadCount").GetInt32());
        Assert.Equal(0, after.GetProperty("mutationCount").GetInt32());
        Assert.Equal(0, after.GetProperty("broadReadCount").GetInt32());
    }

    private static string ExtractToken(string preview)
    {
        using var document = JsonDocument.Parse(preview);
        Assert.True(document.RootElement.TryGetProperty("safetyToken", out var token), preview);
        Assert.False(string.IsNullOrWhiteSpace(token.GetString()));
        return token.GetString()!;
    }

    private static void AssertSucceeded(string apply, string operationId)
    {
        using var document = JsonDocument.Parse(apply);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean(), apply);
        var operation = Assert.Single(document.RootElement.GetProperty("operations").EnumerateArray());
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal("succeeded", operation.GetProperty("status").GetString());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempAuditDirectory audit = new();
        private readonly string projectPath;
        public OpennessWorkerClient Client { get; }
        public WriteSafetyService Safety { get; }

        private Fixture(string scenario, ProjectSessionBinding binding)
        {
            projectPath = $@"C:\FakeWorker\{scenario}.ap21";
            Client = new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
            Safety = new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        }

        public static async Task<Fixture> Create(string scenario)
        {
            var binding = new ProjectSessionBinding(null);
            var fixture = new Fixture(scenario, binding);
            try
            {
                await FakeWorkerBinding.BindVerifiedAsync(fixture.Client, binding, fixture.projectPath);
                return fixture;
            }
            catch
            {
                fixture.Dispose();
                throw;
            }
        }

        public BatchOperationRequest[] Operations(BatchOperationRequest operation)
        {
            operation.ProjectPath = projectPath;
            operation.PlcName = "PLC_1";
            operation.TableName = "Inputs";
            return new[] { operation };
        }

        public async Task<JsonElement> Observe()
        {
            var result = await Client.GetProjectStatusAsync(projectPath);
            Assert.True(result.Success, result.Error);
            using var document = JsonDocument.Parse(result.Payload);
            return document.RootElement.Clone();
        }

        public void Dispose()
        {
            Client.Dispose();
            audit.Dispose();
        }
    }
}
