using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Plc;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Plc;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class PlcLibraryInstancePreviewTests
{
    private const string InspectMethod = "inspect_plc_write_target";
    private const string EditGuard = "plc_library_instance_edit";
    private const string DeleteGuard = "plc_library_test_instance_delete";

    [Theory]
    [InlineData("update_block_logic")]
    [InlineData("update_type_content")]
    [InlineData("patch_block_logic")]
    public async Task LinkedContentPreviewPublishesBlockGuardWithoutMutation(string operation)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-library-edit-blocked");
        var item = await ContentRequestAsync(fixture, operation);

        var response = await fixture.RunAsync(true, item);

        Assert.Equal("preview", response.Phase);
        var guard = Assert.Single(response.Guards, entry => entry.Id == EditGuard);
        Assert.Equal(("write", "block"), (guard.OperationId, guard.Severity));
        Assert.Single(requests.Methods(), method => method == InspectMethod);
        Assert.Null(response.Batch);
        Assert.DoesNotContain(requests.Methods(), method => PlcOperationCatalog.WriteOperationNames.Contains(method));
    }

    [Theory]
    [InlineData("delete_block", "Station_1/Blocks/Motors/FB_Motor")]
    [InlineData("delete_block_group", "Station_1/Blocks/Motors")]
    public async Task InWorkDeletionPreviewPublishesBlockGuardWithoutMutation(string operation, string path)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-library-delete-blocked");

        var response = await fixture.RunAsync(true, new PlcOperationRequest
        {
            OperationId = "delete", Operation = operation, BlockPath = path
        });

        Assert.Equal("preview", response.Phase);
        var guard = Assert.Single(response.Guards, entry => entry.Id == DeleteGuard);
        Assert.Equal(("delete", "block"), (guard.OperationId, guard.Severity));
        Assert.Single(requests.Methods(), method => method == InspectMethod);
        Assert.Null(response.Batch);
        Assert.DoesNotContain(requests.Methods(), method => PlcOperationCatalog.WriteOperationNames.Contains(method));
    }

    [Fact]
    public async Task SimulatedCreatedGroupDefersInspectionUntilItsActualExecution()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-library-delete-blocked");
        const string path = "PLC_2/Blocks/PreviewOnly";

        var response = await fixture.RunAsync(true,
            new PlcOperationRequest { OperationId = "create", Operation = "create_block_group", BlockPath = path },
            new PlcOperationRequest { OperationId = "delete", Operation = "delete_block_group", BlockPath = path });

        Assert.Equal("preview", response.Phase);
        Assert.True(response.Success);
        Assert.Equal("create", response.Effects[1].Effect!.DependsOn);
        Assert.DoesNotContain(response.Guards, entry => entry.Id == DeleteGuard);
        Assert.DoesNotContain(InspectMethod, requests.Methods());
    }

    [Fact]
    public async Task UnreadableLibraryInspectionPublishesStateUnverifiableGuard()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-library-inspection-unreadable");
        var item = await ContentRequestAsync(fixture, "update_block_logic");

        var response = await fixture.RunAsync(true, item);

        var guard = Assert.Single(response.Guards, entry => entry.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.Equal(("write", "block"), (guard.OperationId, guard.Severity));
        Assert.Single(requests.Methods(), method => method == InspectMethod);
        Assert.Null(response.Batch);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnrelatedOrCollidingCreationDoesNotDeferExistingTargetInspection(bool groupCollision)
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, "plc-write-library-delete-blocked");
        var creation = new PlcOperationRequest
        {
            OperationId = "create", Operation = groupCollision ? "create_block_group" : "create_block",
            BlockPath = groupCollision ? "Station_1/Blocks/Motors" : "Station_1/Blocks/PreviewOnly/FB_Motor",
            BlockType = groupCollision ? null : "FB"
        };
        var deletion = new PlcOperationRequest
        {
            OperationId = "delete", Operation = groupCollision ? "delete_block_group" : "delete_block",
            BlockPath = groupCollision ? "Station_1/Blocks/Motors" : "Station_1/Blocks/Motors/FB_Motor"
        };

        var response = await fixture.RunAsync(true, creation, deletion);

        Assert.Equal("preview", response.Phase);
        Assert.Contains(response.Guards, guard => guard.Id == DeleteGuard && guard.OperationId == "delete");
        Assert.Single(requests.Methods(), method => method == InspectMethod);
    }

    [Theory]
    [InlineData("plc-write-library-inspection-malformed", WorkerFailureCategories.ProtocolError)]
    [InlineData("plc-write-library-inspection-lost", WorkerFailureCategories.PortalLost)]
    public async Task InvalidOrFatalInspectionFailsWithoutPublishingOrdinaryGuard(string scenario, string category)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await PlcGuardedWriteFixture.CreateAsync(audit, scenario);
        var item = await ContentRequestAsync(fixture, "update_block_logic");

        var response = await fixture.RunAsync(true, item);

        Assert.Equal("error", response.Phase);
        Assert.Equal(category, response.Error?.Category);
        Assert.DoesNotContain(response.Guards, entry => entry.Id == PlcGuardDefinitions.StateUnverifiable);
        Assert.Null(response.Batch);
    }

    [Theory]
    [InlineData(McpAccessMode.ReadOnly)]
    [InlineData(McpAccessMode.ReadWrite)]
    [InlineData(McpAccessMode.Full)]
    public void InspectionIsBoundObserveWithProjectBaseEligibility(McpAccessMode mode)
    {
        Assert.Equal(OperationCapability.Observe, OperationPolicyCatalog.GetCapability(InspectMethod));
        Assert.True(OperationPolicyCatalog.IsAllowed(mode, InspectMethod));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(InspectMethod));
        Assert.Equal("plc_write", ProjectCapabilityCatalog.OperationFor(InspectMethod));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.StandaloneProject, InspectMethod));
        Assert.True(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.LocalSession, InspectMethod));
        Assert.False(ProjectCapabilityCatalog.Supports(ProjectContainerKinds.ServerProject, InspectMethod));
    }

    private static async Task<PlcOperationRequest> ContentRequestAsync(PlcGuardedWriteFixture fixture, string operation)
    {
        var format = operation == "patch_block_logic" ? "source" : "xml";
        var block = operation != "update_type_content";
        const string blockPath = "PLC_2/Main";
        const string typePath = "Station_1/Types/UDT_Settings";
        var export = block
            ? await fixture.Client.GetBlockContentAsync(blockPath, format)
            : await fixture.Client.GetTypeContentAsync(typePath, format);
        Assert.True(export.Success, export.Error);
        var request = new PlcOperationRequest
        {
            OperationId = "write", Operation = operation, BlockPath = block ? blockPath : null,
            TypePath = block ? null : typePath, Format = format,
            ExpectedContentHash = ContentHashes.Compute(format, export.Payload),
            Content = operation == "patch_block_logic" ? null : export.Payload
        };
        if (operation == "patch_block_logic")
            request.Edits = [new PlcTextEdit { OldText = export.Payload, NewText = "// linked preview\n" + export.Payload }];
        return request;
    }
}
