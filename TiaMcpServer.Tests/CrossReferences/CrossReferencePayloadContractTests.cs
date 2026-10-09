using System.Text.Json;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.CrossReferences;

public class CrossReferencePayloadContractTests
{
    private static string Payload(string access, string referenceType)
    {
        var report = new CrossReferenceReport { IsComplete = true, TotalSourceCount = 1 };
        report.Sources.Add(new CrossReferenceSourceInfo
        {
            Name = "FB1",
            References =
            {
                new CrossReferenceTargetInfo
                {
                    Name = "Tag1",
                    Locations = { new CrossReferenceLocationInfo { Access = access, ReferenceType = referenceType } },
                },
            },
        });
        return WorkerJson.SerializePayload(report);
    }

    [Fact]
    public void ValidPayloadDecodes()
    {
        var report = CrossReferencePayloadContract.Decode(WorkerCallResult.Ok(Payload("Read", "Uses")));

        Assert.Equal("FB1", Assert.Single(report.Sources).Name);
    }

    [Theory]
    [InlineData("NopeAccess", "Uses")]
    [InlineData("Read", "NopeType")]
    public void UnknownAccessNameIsProtocolErrorWithoutEcho(string access, string referenceType)
    {
        var result = WorkerCallResult.Ok(Payload(access, referenceType));

        Assert.Throws<JsonException>(() => CrossReferencePayloadContract.Decode(result));
        var outcome = CrossReferencePayloadContract.Project(result);

        Assert.Equal("failed", outcome.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, outcome.Failure!.Category);
        Assert.DoesNotContain("Nope", outcome.Failure.Message);
        Assert.Null(outcome.Value);
    }
}
