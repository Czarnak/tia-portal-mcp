using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public class PortalSelectionPayloadContractsTests
{
    [Fact]
    public void NewPayloads_WriteExplicitNulls()
    {
        var list = new TiaPortalProcessListInfo { Processes = new() { new TiaPortalProcessInfo() } };
        using var listJson = JsonDocument.Parse(WorkerJson.SerializePayload(list));
        Assert.Equal(JsonValueKind.Null, listJson.RootElement.GetProperty("attachedProcessId").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            listJson.RootElement.GetProperty("processes")[0].GetProperty("projectPath").ValueKind);

        using var processJson = JsonDocument.Parse(WorkerJson.SerializePayload(new TiaPortalProcessInfo()));
        Assert.Equal(JsonValueKind.Null, processJson.RootElement.GetProperty("projectPath").ValueKind);

        using var selectionJson = JsonDocument.Parse(WorkerJson.SerializePayload(new PortalProjectSelectionInfo()));
        Assert.Equal(JsonValueKind.Null, selectionJson.RootElement.GetProperty("previousProcessId").ValueKind);
        Assert.Equal(JsonValueKind.Null, selectionJson.RootElement.GetProperty("previousProjectPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, selectionJson.RootElement.GetProperty("previousProjectIsModified").ValueKind);
        Assert.False(WorkerJson.OmitsNullMembers(typeof(TiaPortalProcessListInfo)));
        Assert.False(WorkerJson.OmitsNullMembers(typeof(TiaPortalProcessInfo)));
        Assert.False(WorkerJson.OmitsNullMembers(typeof(PortalProjectSelectionInfo)));
        Assert.NotNull(CanonicalJson.DeserializeWorkerPayload<TiaPortalProcessListInfo>(listJson.RootElement.GetRawText()));
        Assert.NotNull(CanonicalJson.DeserializeWorkerPayload<TiaPortalProcessInfo>(processJson.RootElement.GetRawText()));
        Assert.NotNull(CanonicalJson.DeserializeWorkerPayload<PortalProjectSelectionInfo>(selectionJson.RootElement.GetRawText()));
    }

    [Theory]
    [InlineData("list", "attachedProcessId")]
    [InlineData("list", "processes")]
    [InlineData("process", "processId")]
    [InlineData("process", "projectPath")]
    [InlineData("process", "hasUserInterface")]
    [InlineData("process", "attachedByThisWorker")]
    [InlineData("selection", "previousProcessId")]
    [InlineData("selection", "previousProjectPath")]
    [InlineData("selection", "previousProjectWasWorkerOpened")]
    [InlineData("selection", "previousProjectIsModified")]
    [InlineData("selection", "reattached")]
    public void NewPayloads_ReaderRejectsMissingMember(string contract, string member)
    {
        var json = JsonNode.Parse(contract switch
        {
            "list" => """{"attachedProcessId":null,"processes":[]}""",
            "process" => """{"processId":4242,"projectPath":null,"hasUserInterface":true,"attachedByThisWorker":false}""",
            "selection" => """{"previousProcessId":null,"previousProjectPath":null,"previousProjectWasWorkerOpened":false,"previousProjectIsModified":null,"reattached":false}""",
            _ => throw new ArgumentOutOfRangeException(nameof(contract))
        })!.AsObject();
        Assert.True(json.Remove(member));
        var incomplete = json.ToJsonString();

        Assert.Throws<JsonException>(() =>
        {
            switch (contract)
            {
                case "list": CanonicalJson.DeserializeWorkerPayload<TiaPortalProcessListInfo>(incomplete); break;
                case "process": CanonicalJson.DeserializeWorkerPayload<TiaPortalProcessInfo>(incomplete); break;
                case "selection": CanonicalJson.DeserializeWorkerPayload<PortalProjectSelectionInfo>(incomplete); break;
            }
        });
    }
}
