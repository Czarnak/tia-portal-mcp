using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class ProjectBindingPayloadContractTests
{
    [Fact]
    public void Decode_MalformedPayload_Rejects() => Assert.Throws<JsonException>(() => ProjectBindingPayloadContract.DecodeProcessList("{\"untrustedMarker\":true}"));

    [Theory]
    [InlineData(42, 43, false)]
    [InlineData(42, 42, true)]
    public void DecodeProcessList_InconsistentAttachment_Rejects(int attached, int process, bool duplicate)
    {
        var list = new TiaPortalProcessListInfo { AttachedProcessId = attached, Processes = new() { new() { ProcessId = process, AttachedByThisWorker = true } } };
        if (duplicate) list.Processes.Add(new() { ProcessId = 44, AttachedByThisWorker = true });
        Assert.Throws<JsonException>(() => ProjectBindingPayloadContract.DecodeProcessList(WorkerJson.SerializePayload(list)));
    }

    [Fact]
    public void DecodeSelection_PreviousFieldsDisagree_Rejects()
    {
        var before = ProjectBindingDecisionTests.Snapshot(ProjectBindingSnapshot.VerifiedState);
        var selection = new PortalProjectSelectionInfo { PreviousProcessId = 42, PreviousProjectPath = "C:/Other.ap21", PreviousProjectIsModified = false };
        Assert.Throws<JsonException>(() => ProjectBindingPayloadContract.DecodeSelection(WorkerJson.SerializePayload(selection), before));
    }

    [Fact]
    public void DecodeSelection_PreviousVerifiedState_RequiresModifiedFlag()
    {
        var before = ProjectBindingDecisionTests.Snapshot(ProjectBindingSnapshot.VerifiedState);
        var selection = new PortalProjectSelectionInfo { PreviousProcessId = 42, PreviousProjectPath = before.ProjectPath };
        Assert.Throws<JsonException>(() => ProjectBindingPayloadContract.DecodeSelection(WorkerJson.SerializePayload(selection), before));
    }
}
