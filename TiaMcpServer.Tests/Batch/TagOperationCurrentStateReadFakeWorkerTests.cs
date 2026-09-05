using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationCurrentStateReadFakeWorkerTests
{
    [Fact]
    public async Task PreviewWriteBatch_DeleteTag_UsesExactInternalSnapshotRouteInsteadOfListTagTables()
    {
        const string path = @"C:\FakeWorker\tag-safety-route-proof.ap21";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, path);
        var safety = CreateSafety(audit, binding);

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, new[] { DeleteTag(path, "d1") });

        Assert.True(preview.Contains("\"safetyToken\":", StringComparison.Ordinal), preview);
        Assert.DoesNotContain("wrong route: list_tag_tables", preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewWriteBatch_TwoIdenticalDeleteTagSelectors_PerformsOneSnapshotReadAndStillDescribesBothOperations()
    {
        const string path = @"C:\FakeWorker\tag-safety-dedup-proof.ap21";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, path);
        var safety = CreateSafety(audit, binding);
        var operations = new[] { DeleteTag(path, "d1"), DeleteTag(path, "d2") };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.True(preview.Contains("\"safetyToken\":", StringComparison.Ordinal), preview);
        using var document = JsonDocument.Parse(preview);
        var descriptions = string.Join("\n", document.RootElement.GetProperty("target")
            .EnumerateArray().Select(item => item.GetProperty("summary").GetString()));
        Assert.Equal(2, Regex.Matches(descriptions,
            Regex.Escape("Delete PLC tag 'Start' from table 'Inputs'."), RegexOptions.CultureInvariant).Count);
        Assert.Equal(new[] { "d1", "d2" }, document.RootElement.GetProperty("target")
            .EnumerateArray().Select(item => item.GetProperty("operationId").GetString()));
    }

    [Fact]
    public async Task ApplyWriteBatch_DeduplicatedPreview_PerformsFreshSnapshotRead()
    {
        const string path = @"C:\FakeWorker\tag-safety-dedup-proof.ap21";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, path);
        var safety = CreateSafety(audit, binding);
        var operations = new[] { DeleteTag(path, "d1"), DeleteTag(path, "d2") };
        using var preview = JsonDocument.Parse(await WriteBatchTools.PreviewWriteBatch(client, safety, operations));

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true,
            safetyToken: preview.RootElement.GetProperty("safetyToken").GetString());

        Assert.Contains("dedup missing: repeated read_delete_tag_safety_snapshot", apply, StringComparison.Ordinal);
        Assert.Contains("before write", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewWriteBatch_RepeatedPreview_PerformsFreshSnapshotRead()
    {
        const string path = @"C:\FakeWorker\tag-safety-dedup-proof.ap21";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, path);
        var safety = CreateSafety(audit, binding);
        var operations = new[] { DeleteTag(path, "d1") };
        var first = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        Assert.True(first.Contains("\"safetyToken\":", StringComparison.Ordinal), first);

        var second = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Contains("dedup missing: repeated read_delete_tag_safety_snapshot", second, StringComparison.Ordinal);
        Assert.DoesNotContain("\"safetyToken\":", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewWriteBatch_SharedUpdateSelector_ValidatesRequestedFlagsForEveryOperation()
    {
        const string path = "tag-update-snapshot-unavailable-all";
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, path);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "u1", Operation = "update_tag", ProjectPath = path,
                PlcName = "PLC_1", TableName = "Default tag table", Name = "MotorReady", DataType = "DInt" },
            new BatchOperationRequest { OperationId = "u2", Operation = "update_tag", ProjectPath = path,
                PlcName = "PLC_1", TableName = "Default tag table", Name = "MotorReady", ExternalVisible = true }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Contains("u2", preview, StringComparison.Ordinal);
        Assert.Contains("externalVisible", preview, StringComparison.Ordinal);
        Assert.Contains("validation_error", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("\"safetyToken\":", preview, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("create_tag_table")]
    [InlineData("delete_tag_table")]
    [InlineData("create_tag")]
    [InlineData("update_tag")]
    [InlineData("delete_tag")]
    [InlineData("create_user_constant")]
    [InlineData("update_user_constant")]
    [InlineData("delete_user_constant")]
    public async Task PreviewWriteBatch_EachTagOperation_UsesExactTypedRoute(string operation)
    {
        var preview = await PreviewRoute(operation, "tag-safety-all-routes");
        Assert.True(preview.Contains("\"safetyToken\":", StringComparison.Ordinal), preview);
    }

    [Theory]
    [InlineData("create_tag_table")]
    [InlineData("delete_tag_table")]
    [InlineData("create_tag")]
    [InlineData("update_tag")]
    [InlineData("delete_tag")]
    [InlineData("create_user_constant")]
    [InlineData("update_user_constant")]
    [InlineData("delete_user_constant")]
    public async Task PreviewWriteBatch_EachTagOperation_RejectsMalformedSnapshot(string operation)
    {
        var preview = await PreviewRoute(operation, "tag-safety-invalid-routes");
        Assert.Contains("\"failureCategory\":\"protocol_error\"", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("\"safetyToken\":", preview, StringComparison.Ordinal);
    }

    private static async Task<string> PreviewRoute(string operation, string path)
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, path);
        var op = new BatchOperationRequest
        {
            OperationId = "route", Operation = operation, ProjectPath = path,
            PlcName = "PLC_1", TableName = "Inputs", FolderPath = "Area"
        };
        if (!operation.EndsWith("tag_table", StringComparison.Ordinal)) op.Name = "Start";
        if (operation is "create_tag" or "create_user_constant") op.DataType = "Bool";
        if (operation == "create_user_constant") op.Value = "true";
        if (operation is "create_tag" or "update_tag") op.LogicalAddress = "%I1.0";
        if (operation == "update_tag") op.NewName = "Run";
        return await WriteBatchTools.PreviewWriteBatch(client, CreateSafety(audit, binding), new[] { op });
    }

    [Fact]
    public void StateComposer_CanRepeatIdenticalCurrentStateForDifferentOperationsWithoutLosingOrder()
    {
        var combined = OperationBatchStateComposer.CombineCurrentState(new[]
        {
            new OperationBatchCurrentState("u1", "update_tag", "{\"k\":1}"),
            new OperationBatchCurrentState("d1", "delete_tag", "{\"k\":1}")
        });

        Assert.Contains("u1::update_tag", combined, StringComparison.Ordinal);
        Assert.Contains("d1::delete_tag", combined, StringComparison.Ordinal);
        Assert.True(combined.IndexOf("u1::update_tag", StringComparison.Ordinal) <
            combined.IndexOf("d1::delete_tag", StringComparison.Ordinal));
    }

    [Fact]
    public void TokenValidation_TreatsDuplicatedCurrentStateBodiesAsDistinctWhenOperationIdentityDiffers()
    {
        using var audit = new TempAuditDirectory();
        var service = new WriteSafetyService(() => DateTimeOffset.UtcNow,
            WriteSafetyService.DefaultTokenLifetime, audit.Path);
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "u1", Operation = "update_tag" },
            new BatchOperationRequest { OperationId = "d1", Operation = "delete_tag" }
        };
        var states = new[]
        {
            new OperationBatchCurrentState("u1", "update_tag", "{\"snapshot\":1}"),
            new OperationBatchCurrentState("d1", "delete_tag", "{\"snapshot\":1}")
        };
        var combined = OperationBatchStateComposer.CombineCurrentState(states);
        var targets = BatchSafetySnapshot.BuildTargets(operations);
        using var preview = JsonDocument.Parse(service.CreatePreview("apply_write_batch", null,
            targets, "Test ordered state", operations, combined));
        var token = preview.RootElement.GetProperty("safetyToken").GetString();

        var result = service.ValidateAndConsume(token, "apply_write_batch", null, targets, operations, combined);
        Assert.True(result.IsValid, result.Error);
    }

    private static BatchOperationRequest DeleteTag(string path, string id) => new()
    {
        OperationId = id, Operation = "delete_tag", ProjectPath = path,
        PlcName = "PLC_1", TableName = "Inputs", Name = "Start"
    };

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
}
