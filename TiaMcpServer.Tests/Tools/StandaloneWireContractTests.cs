using System.Text.Json;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class StandaloneWireContractTests
{
    [Fact]
    public async Task DirectStatusPayload_WritesExplicitNulls_ForNoProject()
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        var result = await client.GetProjectStatusAsync("status-no-project");
        Assert.True(result.Success, result.Error);
        using var direct = JsonDocument.Parse(result.Payload);
        var root = direct.RootElement;
        Assert.Equal("get_project_status", root.GetProperty("operation").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("projectPath").ValueKind);
        Assert.False(root.GetProperty("project").GetProperty("isOpen").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("project").GetProperty("metadata").ValueKind);
    }
    [Fact]
    public void CompileRoot_WritesExplicitNulls_BatchImportEnvelopeKeepsItsNullPolicy()
    {
        var report = new CompileCheckReport { Plcs = [new() { PlcName = "PLC_1", State = "Success" }] };
        using var direct = JsonDocument.Parse(WorkerJson.SerializePayload(report));
        Assert.Equal(JsonValueKind.Null, direct.RootElement.GetProperty("blockPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, direct.RootElement.GetProperty("plcs")[0].GetProperty("deviceName").ValueKind);

        var envelope = new WorkerResponse
        {
            Success = true,
            BlockImportOutcome = new BlockImportOutcomeInfo { CompileReport = report }
        };
        using var nested = JsonDocument.Parse(JsonSerializer.Serialize(envelope, WorkerJson.Envelope));
        var nestedReport = nested.RootElement.GetProperty("blockImportOutcome").GetProperty("compileReport");
        Assert.False(nestedReport.TryGetProperty("blockPath", out _));
        Assert.False(nestedReport.GetProperty("plcs")[0].TryGetProperty("deviceName", out _));
    }

    [Theory]
    [InlineData("open_project")]
    [InlineData("create_project")]
    [InlineData("save_project")]
    [InlineData("save_project_as")]
    [InlineData("archive_project")]
    [InlineData("close_project")]
    public void LifecyclePayloads_WriteExplicitNulls_AndDecodeThroughWorkerPayloadReader(string operation)
    {
        var payload = new ProjectLifecycleResultInfo { Operation = operation };
        var serialized = WorkerJson.SerializePayload(payload);
        using var document = JsonDocument.Parse(serialized);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("projectPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("project").ValueKind);
        var decoded = CanonicalJson.DeserializeWorkerPayload<ProjectLifecycleResultInfo>(serialized);
        Assert.True(decoded.Success);
        Assert.Equal(operation, decoded.Operation);
        Assert.Null(decoded.ProjectPath);
        Assert.Null(decoded.Project);
    }
}
