using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tests.Project;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class StandaloneStatusContractTests
{
    private static ProjectStatusResultInfo Payload() => new()
    {
        Operation = "get_project_status", ProjectPath = @"C:\Ground\Ground.ap21",
        Project = new()
        {
            IsOpen = true, Path = @"C:\Ground\Ground.ap21", Metadata = new()
            {
                LanguageSettings = new() { Languages = [], ActiveLanguages = [] },
                HistoryEntries = [], HistoryTruncated = false, UsedProducts = []
            }
        }
    };

    [Theory]
    [InlineData("missing")]
    [InlineData("nestedMissing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("wrongCase")]
    [InlineData("rootArray")]
    [InlineData("rootNull")]
    [InlineData("malformed")]
    [InlineData("nullProject")]
    [InlineData("nullElements")]
    [InlineData("wrongOperation")]
    [InlineData("falseSuccess")]
    [InlineData("differentPath")]
    [InlineData("closedWithPath")]
    [InlineData("negativeSize")]
    [InlineData("oversizedMalformed")]
    public void InvalidSuccessPayload_IsFixedProtocolFailureWithoutRawEcho(string variant)
    {
        var node = JsonSerializer.SerializeToNode(Payload(), WorkerJson.ExplicitNullPayload)!.AsObject();
        switch (variant)
        {
            case "missing": node.Remove("project"); break;
            case "nestedMissing": node["project"]!.AsObject().Remove("metadata"); break;
            case "extra": node["PRIVATE_PAYLOAD_MARKER"] = true; break;
            case "wrongCase": node["Success"] = node["success"]!.DeepClone(); node.Remove("success"); break;
            case "nullProject": node["project"] = null; break;
            case "nullElements": node["project"]!["metadata"]!["historyEntries"] = new JsonArray((JsonNode?)null); break;
            case "wrongOperation": node["operation"] = "probe_project_status_for_lifecycle"; break;
            case "falseSuccess": node["success"] = false; break;
            case "differentPath": node["projectPath"] = @"C:\Other.ap21"; break;
            case "closedWithPath": node["project"]!["isOpen"] = false; break;
            case "negativeSize": node["project"]!["size"] = -1; break;
        }
        var json = variant switch
        {
            "duplicate" => node.ToJsonString().Insert(1, "\"success\":true,"),
            "rootArray" => "[]", "rootNull" => "null", "malformed" => "PRIVATE_PAYLOAD_MARKER",
            "oversizedMalformed" => "{\"PRIVATE_PAYLOAD_MARKER\":\"" + new string('x', 70000) + "\"}",
            _ => node.ToJsonString()
        };
        var outcome = StandalonePayloadContract.DecodeStatus(WorkerCallResult.Ok(json));
        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Value);
        Assert.Null(outcome.Omission);
        Assert.Equal("protocol_error", outcome.Failure!.Category);
        Assert.Equal("The worker payload did not match the declared standalone result contract.", outcome.Failure.Message);
    }

    [Fact]
    public void Metadata_NullAndUnavailableRemainDistinctFromEmptyValues()
    {
        var payload = Payload();
        var outcome = StandalonePayloadContract.DecodeStatus(WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));
        var root = StandaloneStatusToolTests.Document(StructuredStandaloneResult.Create("get_project_status", outcome, []));
        var metadata = root.GetProperty("result").GetProperty("value").GetProperty("metadata");
        Assert.Equal(0, metadata.GetProperty("historyEntries").GetArrayLength());
        Assert.False(metadata.GetProperty("historyTruncated").GetBoolean());
        Assert.Equal(JsonValueKind.Null, metadata.GetProperty("comment").ValueKind);
        payload.Project!.Metadata!.HistoryEntries = null;
        payload.Project.Metadata.HistoryTruncated = null;
        outcome = StandalonePayloadContract.DecodeStatus(WorkerCallResult.Ok(WorkerJson.SerializePayload(payload)));
        Assert.Null(outcome.Value!.Metadata!.HistoryEntries);
        Assert.Null(outcome.Value.Metadata.HistoryTruncated);
    }

    [Theory]
    [InlineData(59999, "succeeded")]
    [InlineData(60000, "succeeded")]
    [InlineData(60001, "omitted")]
    public void ValueBudget_UsesCanonicalCharacterBoundary(int chars, string expectedStatus)
    {
        var value = new ProjectStatusInfo { Author = "" };
        value.Author = new string('x', chars - CanonicalJson.Serialize(value).Length);
        var outcome = new StandaloneToolOutcome<ProjectStatusInfo>("succeeded", value, null, null);
        var root = StandaloneStatusToolTests.Document(StructuredStandaloneResult.Create("get_project_status", outcome, []));
        Assert.Equal(expectedStatus, root.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal(expectedStatus == "succeeded", root.GetProperty("success").GetBoolean());
    }

    [Fact]
    public void ValueBudget_AccountsForEscapedStrings()
    {
        var value = new ProjectStatusInfo { Author = new string('"', 11000) };
        var outcome = new StandaloneToolOutcome<ProjectStatusInfo>("succeeded", value, null, null);
        var root = StandaloneStatusToolTests.Document(StructuredStandaloneResult.Create("get_project_status", outcome, []));
        Assert.Equal("omitted", root.GetProperty("result").GetProperty("status").GetString());
    }

    [Fact]
    public void DocumentBudget_DropsWholeWarningsAndDisclosesTheirCount()
    {
        var outcome = new StandaloneToolOutcome<ProjectStatusInfo>("succeeded", new(), null, null);
        var root = StandaloneStatusToolTests.Document(StructuredStandaloneResult.Create("get_project_status", outcome,
            ["keep", new string('x', 180000), "whole last warning"]));
        Assert.True(root.GetRawText().Length <= 180000);
        Assert.Equal("omitted", root.GetProperty("result").GetProperty("status").GetString());
        Assert.Equal("responseExceededDocumentCharLimit", root.GetProperty("result").GetProperty("omission").GetProperty("reason").GetString());
        Assert.Equal("keep", root.GetProperty("warnings")[0].GetString());
        Assert.Equal("2 warning entries were omitted to fit the response budget.", root.GetProperty("warnings")[1].GetString());
    }

    [Fact]
    public void RejectionAndAttemptedFailure_AreSeparateAndRemainBounded()
    {
        var failure = new StructuredOperationFailure("worker_operation_failed", new string('x', 180000));
        var outcome = new StandaloneToolOutcome<ProjectStatusInfo>("failed", null, failure, null);
        var attempted = StructuredStandaloneResult.Create("get_project_status", outcome, []);
        var root = StandaloneStatusToolTests.Document(attempted);
        Assert.False(attempted.IsError);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.Contains("message shortened", root.GetProperty("result").GetProperty("failure").GetProperty("message").GetString());
        var rejected = StructuredStandaloneResult.Create<ProjectStatusInfo>("get_project_status", null, [], failure);
        root = StandaloneStatusToolTests.Document(rejected);
        Assert.True(rejected.IsError);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("result").ValueKind);
        Assert.True(root.GetRawText().Length <= 180000);
    }

    [Fact]
    public void Renderer_RefusesUnknownToolOrInvalidPair()
    {
        var outcome = new StandaloneToolOutcome<ProjectStatusInfo>("succeeded", new(), null, null);
        Assert.Throws<ArgumentException>(() => StructuredStandaloneResult.Create("compile_check", outcome, []));
        Assert.Throws<ArgumentException>(() => StructuredStandaloneResult.Create("unknown", outcome, []));
        Assert.Throws<ArgumentException>(() => StructuredStandaloneResult.Create<ProjectStatusInfo>("get_project_status", null, []));
        Assert.Throws<ArgumentException>(() => StructuredStandaloneResult.Create("get_project_status", outcome, [], new("validation_error", "rejected")));
    }
}
