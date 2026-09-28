using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// probe_open_project_rebind payloads have one strict decoder. It replaced an untyped member
/// check in OpennessWorkerClient and a lenient Web-defaults decode in ProjectWriteTools, which
/// could disagree about the same bytes.
/// </summary>
public sealed class ProjectRebindStatePayloadContractTests
{
    private const string Source = "C:/Projects/A.ap21";
    private const string Destination = "C:/Projects/B.ap21";
    private const string Valid =
        """{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""";

    [Fact]
    public void Decode_AcceptsAStateForThePinnedSourceAndDestination()
    {
        var state = ProjectRebindStatePayloadContract.Decode(Valid, Source, Destination);

        Assert.True(state.WillCloseSource);
        Assert.False(state.SourceIsModified);
    }

    [Theory]
    [InlineData("sourceProjectPath")]
    [InlineData("destinationProjectPath")]
    [InlineData("sourceIsModified")]
    [InlineData("sourceOpenedByWorker")]
    [InlineData("willCloseSource")]
    public void Decode_RejectsAMissingMember(string member)
    {
        var payload = JsonNode.Parse(Valid)!.AsObject();
        payload.Remove(member);

        Assert.ThrowsAny<JsonException>(() =>
            ProjectRebindStatePayloadContract.Decode(payload.ToJsonString(), Source, Destination));
    }

    [Theory]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true,"extra":1}""")]
    [InlineData("""{"SourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":null,"destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":null,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/C.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":true}""")]
    [InlineData("""{"sourceProjectPath":"C:/Projects/A.ap21","destinationProjectPath":"C:/Projects/B.ap21","sourceIsModified":false,"sourceOpenedByWorker":true,"willCloseSource":false}""")]
    public void Decode_RejectsAPayloadThatIsNotThePinnedStrictContract(string payload)
    {
        Assert.ThrowsAny<JsonException>(() =>
            ProjectRebindStatePayloadContract.Decode(payload, Source, Destination));
    }
}
