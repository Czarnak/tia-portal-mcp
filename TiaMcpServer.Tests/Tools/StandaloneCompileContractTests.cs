using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Tests.Project;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class StandaloneCompileContractTests
{
    private static CompileCheckReport Report() => new()
    {
        Plcs = [new() { PlcName = "PLC_1", State = "Success", Messages = [new() { Severity = "Information" }] }]
    };

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("nestedMissing")]
    [InlineData("rootNull")]
    [InlineData("rootString")]
    [InlineData("duplicate")]
    [InlineData("wrongCase")]
    [InlineData("nullPlcs")]
    [InlineData("emptyPlcs")]
    [InlineData("nullPlc")]
    [InlineData("nullMessages")]
    [InlineData("nullMessage")]
    [InlineData("nullNotes")]
    [InlineData("nullNote")]
    [InlineData("nullDescription")]
    [InlineData("unknownSeverity")]
    [InlineData("emptyState")]
    [InlineData("emptyPlcName")]
    [InlineData("negativeCount")]
    [InlineData("wrongTotals")]
    [InlineData("successWithErrors")]
    [InlineData("wrongOverallState")]
    [InlineData("wrongScope")]
    [InlineData("plcWithBlockPath")]
    [InlineData("blockWithoutPath")]
    public void InvalidCompilePayload_IsFixedProtocolFailure(string variant)
    {
        var node = JsonSerializer.SerializeToNode(Report(), WorkerJson.ExplicitNullPayload)!.AsObject();
        var plc = node["plcs"]![0]!;
        switch (variant)
        {
            case "missing": node.Remove("blockPath"); break;
            case "extra": node["PRIVATE_COMPILER_MARKER"] = true; break;
            case "nestedMissing": plc.AsObject().Remove("deviceName"); break;
            case "wrongCase": node["Scope"] = "plc"; node.Remove("scope"); break;
            case "nullPlcs": node["plcs"] = null; break;
            case "emptyPlcs": node["plcs"] = new JsonArray(); break;
            case "nullPlc": node["plcs"]![0] = null; break;
            case "nullMessages": plc["messages"] = null; break;
            case "nullMessage": plc["messages"]![0] = null; break;
            case "nullNotes": plc["diagnosticNotes"] = null; break;
            case "nullNote": plc["diagnosticNotes"] = new JsonArray((JsonNode?)null); break;
            case "nullDescription": plc["messages"]![0]!["description"] = null; break;
            case "unknownSeverity": plc["messages"]![0]!["severity"] = "PRIVATE_COMPILER_MARKER"; break;
            case "emptyState": plc["state"] = ""; break;
            case "emptyPlcName": plc["plcName"] = ""; break;
            case "negativeCount": plc["errorCount"] = -1; break;
            case "wrongTotals": node["totalWarningCount"] = 1; break;
            case "successWithErrors": plc["errorCount"] = 1; node["totalErrorCount"] = 1; break;
            case "wrongOverallState": node["overallState"] = "Warning"; break;
            case "wrongScope": node["scope"] = "PRIVATE_COMPILER_MARKER"; break;
            case "plcWithBlockPath": node["blockPath"] = "Main"; break;
            case "blockWithoutPath": node["scope"] = "block"; break;
        }
        var text = variant switch
        {
            "rootNull" => "null", "rootString" => "\"PRIVATE_COMPILER_MARKER\"",
            "duplicate" => node.ToJsonString().Insert(1, "\"scope\":\"plc\","), _ => node.ToJsonString()
        };
        var outcome = StandalonePayloadContract.DecodeCompile(WorkerCallResult.Ok(text));
        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Value);
        Assert.Null(outcome.Omission);
        Assert.Equal("protocol_error", outcome.Failure!.Category);
        Assert.Equal("The worker payload did not match the declared standalone result contract.", outcome.Failure.Message);
    }

    [Fact]
    public void BoundedDiagnostics_KeepCountsAndOmissionNotesWithoutPretendingMessagesAreExhaustive()
    {
        var report = Report();
        report.OverallState = report.Plcs[0].State = "Error";
        report.TotalErrorCount = report.Plcs[0].ErrorCount = 900;
        report.Plcs[0].Messages = [];
        report.Plcs[0].DiagnosticNotes = ["Some compiler diagnostics were omitted or shortened because a report limit was reached or a detail could not be read."];
        var outcome = StandalonePayloadContract.DecodeCompile(WorkerCallResult.Ok(WorkerJson.SerializePayload(report)));
        var root = StandaloneStatusToolTests.Document(StructuredStandaloneResult.Create("compile_check", outcome, ["worker warning"]));
        Assert.Equal("failed", outcome.Status);
        Assert.Null(outcome.Failure);
        var value = root.GetProperty("result").GetProperty("value");
        Assert.Equal(900, value.GetProperty("totalErrorCount").GetInt32());
        Assert.Equal(0, value.GetProperty("plcs")[0].GetProperty("messages").GetArrayLength());
        Assert.Contains("omitted", value.GetProperty("plcs")[0].GetProperty("diagnosticNotes")[0].GetString());
        Assert.Equal("worker warning", root.GetProperty("warnings")[0].GetString());
    }

    [Theory]
    [InlineData("Success", true)]
    [InlineData("Warning", true)]
    [InlineData("Error", false)]
    [InlineData("Cancelled", false)]
    [InlineData("Incomplete", false)]
    public void BlockScope_UnknownOrUnavailableStatesAreNotPassingReports(string state, bool passed)
    {
        var report = Report();
        report.Scope = "block"; report.BlockPath = "Main";
        report.OverallState = report.Plcs[0].State = state;
        var outcome = StandalonePayloadContract.DecodeCompile(WorkerCallResult.Ok(WorkerJson.SerializePayload(report)));
        Assert.Equal(passed ? "succeeded" : "failed", outcome.Status);
        Assert.NotNull(outcome.Value);
        Assert.Null(outcome.Failure);
    }

    [Fact]
    public void FailedWorkerEnvelope_DoesNotDecodePayloadOrLoseWarnings()
    {
        var worker = WorkerCallResult.Fail(WorkerFailureCategories.WorkerTimeout, "Compilation outcome is unknown.", ["inspect before retry"])
            with { Payload = "PRIVATE_COMPILER_MARKER", DispatchState = WorkerDispatchState.Unknown };
        var outcome = StandalonePayloadContract.DecodeCompile(worker);
        var response = StructuredStandaloneResult.Create("compile_check", outcome, worker.Warnings);
        var root = StandaloneStatusToolTests.Document(response);
        Assert.False(response.IsError);
        Assert.Equal("worker_timeout", outcome.Failure!.Category);
        Assert.Equal("inspect before retry", root.GetProperty("warnings")[0].GetString());
        Assert.DoesNotContain("PRIVATE_COMPILER_MARKER", root.GetRawText());
    }
}
