using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Vci;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciGuardedWriteDomainTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualFailureEnvelopeEmitsNestedNulls(bool withLast)
    {
        var outcome = new VciWriteOutcomeInfo { LastAttempted = withLast ? new() { Path = [new() { NodeType = "Device", Name = "Fixture" }], Operation = "export" } : null };
        var raw = JsonSerializer.Serialize(new WorkerResponse { Success = false, VciWriteOutcome = outcome }, WorkerJson.Envelope);
        var value = JsonDocument.Parse(raw).RootElement.GetProperty("vciWriteOutcome");
        Assert.Equal(withLast ? JsonValueKind.Object : JsonValueKind.Null, value.GetProperty("lastAttempted").ValueKind);
        if (withLast) Assert.Equal(JsonValueKind.Null, value.GetProperty("lastAttempted").GetProperty("format").ValueKind);
    }

    public static TheoryData<string> Malformed => new() { "missingCount", "unknown", "wrongType", "nullSkipped", "missingNull", "nestedMissing", "nestedUnknown", "nestedCase", "nestedDuplicate", "outerCase", "outerDuplicate", "outerCaseDuplicate", "nullOutcome", "overCapFailures" };

    internal static VciLeafFailureInfo Leaf(string marker = "ok", string category = "worker_operation_failed", string? type = null, string? message = null)
        => new() { Path = [new() { NodeType = "Device", Name = marker }], Format = null, Category = category, EngineeringErrorType = type, Message = message ?? marker };

    public static TheoryData<string> BadFailures => new() { "tooMany", "countExceedsFailed", "emptyPath", "badCategory", "noType", "longMessage", "nullEntry" };

    [Theory]
    [MemberData(nameof(BadFailures))]
    public void Failures_OverCapOrCountMismatch_IsProtocolError(string kind)
    {
        const string m = "PRIVATE_MARKER";
        var outcome = new VciWriteOutcomeInfo { Leaves = 30, Failed = 25, Succeeded = 5 };
        switch (kind)
        {
            case "tooMany": outcome.Failures = Enumerable.Range(0, VciWriteOutcomeInfo.MaxFailures + 1).Select(_ => Leaf(m)).ToList(); break;
            case "countExceedsFailed": outcome.Failed = 2; outcome.Failures = [Leaf(m), Leaf(m)]; outcome.FailuresOmitted = 1; break;
            case "emptyPath": { var l = Leaf(m); l.Path = []; outcome.Failures = [l]; break; }
            case "badCategory": outcome.Failures = [Leaf(m, "portal_lost")]; break;
            case "noType": outcome.Failures = [Leaf(m, "engineering_rejected")]; break;
            case "longMessage": outcome.Failures = [Leaf(m, message: m + new string('x', VciLeafFailureInfo.MaxMessageChars))]; break;
            case "nullEntry": outcome.Failures = [Leaf(m), null!]; break;
        }
        var item = VciWritePayloadContract.Project(new() { OperationId = "a", Operation = "export" },
            WorkerCallResult.Fail("worker_operation_failed", "Synthetic") with { VciWriteOutcome = outcome });
        Assert.Equal("protocol_error", item.Failure!.Category);
        Assert.Null(item.Result);
        Assert.DoesNotContain(m, JsonSerializer.Serialize(item));
    }

    [Fact]
    public void Failures_ValidCappedList_IsProjected()
    {
        var outcome = new VciWriteOutcomeInfo { Leaves = 30, Failed = 25, Succeeded = 5, FailuresOmitted = 5,
            Failures = Enumerable.Range(0, VciWriteOutcomeInfo.MaxFailures).Select(i => Leaf("n" + i, "engineering_rejected", "EngineeringTargetInvocationException")).ToList() };
        var item = VciWritePayloadContract.Project(new() { OperationId = "a", Operation = "export" },
            WorkerCallResult.Fail("engineering_rejected", "Synthetic") with { VciWriteOutcome = outcome });
        Assert.Equal("engineering_rejected", item.Failure!.Category);
        Assert.Equal(20, item.Result!.Value.GetProperty("failures").GetArrayLength());
    }

    [Theory]
    [MemberData(nameof(Malformed))]
    public async Task ActualFailureWireRejectsMalformedOutcomeWithoutDefaultsOrLeak(string kind)
    {
        var outcome = JsonSerializer.SerializeToNode(new VciWriteOutcomeInfo(), WorkerJson.ExplicitNullPayload)!.AsObject();
        switch (kind)
        {
            case "missingCount": outcome.Remove("failed"); break;
            case "unknown": outcome["PRIVATE_MARKER"] = "PRIVATE_MARKER"; break;
            case "wrongType": outcome["failed"] = "PRIVATE_MARKER"; break;
            case "nullSkipped": outcome["skipped"] = null; break;
            case "missingNull": outcome.Remove("lastAttempted"); break;
            case "nestedMissing": outcome["skipped"]!.AsObject().Remove("equal"); break;
            case "nestedUnknown": outcome["skipped"]!["PRIVATE_MARKER"] = true; break;
            case "nestedCase": outcome["skipped"]!.AsObject().Remove("equal"); outcome["skipped"]!["Equal"] = 0; break;
            case "overCapFailures":
                outcome["failed"] = 25;
                outcome["failures"] = JsonSerializer.SerializeToNode(new VciWriteOutcomeInfo
                    { Failures = Enumerable.Range(0, VciWriteOutcomeInfo.MaxFailures + 1).Select(_ => Leaf("PRIVATE_MARKER")).ToList() }, WorkerJson.ExplicitNullPayload)!["failures"]!.DeepClone();
                break;
        }
        var payload = outcome.ToJsonString();
        if (kind == "nestedDuplicate") payload = payload.Replace("\"equal\":0", "\"equal\":0,\"equal\":1");
        var name = kind == "outerCase" ? "VciWriteOutcome" : "vciWriteOutcome";
        var member = $"\"{name}\":{(kind == "nullOutcome" ? "null" : payload)}";
        if (kind == "outerDuplicate") member += "," + member;
        if (kind == "outerCaseDuplicate") member += ",\"VciWriteOutcome\":" + payload;
        var raw = "{\"success\":false,\"failureCategory\":\"worker_operation_failed\",\"error\":\"PRIVATE_MARKER\"," + member + "}";
        using var f = new WireFixture(raw);
        var response = await f.Transport.SendAsync(new() { Method = "vci_export" });
        Assert.False(response.Success); Assert.Equal("protocol_error", response.FailureCategory);
        Assert.Null(response.VciWriteOutcome); Assert.DoesNotContain("PRIVATE_MARKER", response.Error);
    }

    [Fact]
    public void FailureBeforeScopeExpansionPreservesUnknownLeafCount()
    {
        var result = VciWritePayloadContract.Project(new() { OperationId = "a", Operation = "export" },
            WorkerCallResult.Fail("portal_lost", "Synthetic pre-expansion failure") with { VciWriteOutcome = new() { Leaves = 0, Failed = 1 } });
        Assert.Equal("portal_lost", result.Failure!.Category);
        Assert.Equal(0, result.Result!.Value.GetProperty("leaves").GetInt32());
    }

    [Fact]
    public async Task GenericFailureWithoutOutcomeRetainsUnknownCounts()
    {
        using var f = new WireFixture("{\"success\":false,\"failureCategory\":\"worker_operation_failed\",\"error\":\"Synthetic generic failure\"}");
        var response = await f.Transport.SendAsync(new() { Method = "vci_export" });
        Assert.Equal("worker_operation_failed", response.FailureCategory); Assert.Null(response.VciWriteOutcome);
        var item = VciWritePayloadContract.Project(new() { OperationId = "a", Operation = "export" },
            WorkerCallResult.Fail(response.FailureCategory!, response.Error!));
        Assert.Null(item.Result); Assert.Equal("failed", item.Status);
    }

    [Fact]
    public async Task ActualFailureWirePreservesValidCountsAndLegacyEnvelopeCompatibility()
    {
        var raw = JsonSerializer.Serialize(new WorkerResponse { Success = false, FailureCategory = "worker_operation_failed", Error = "Synthetic failure",
            VciWriteOutcome = new() { Leaves = 3, Succeeded = 1, Failed = 1, Skipped = new() { Equal = 1 } } }, WorkerJson.ExplicitNullPayload);
        using var f = new WireFixture(raw);
        var result = await f.Transport.SendAsync(new() { Method = "vci_export" });
        Assert.Equal(1, result.VciWriteOutcome!.Failed); Assert.Equal(3, result.VciWriteOutcome.Leaves);
        using var legacy = new WireFixture("{\"Success\":true,\"Payload\":\"legacy\",\"extra\":true}");
        Assert.Equal("legacy", (await legacy.Transport.SendAsync(new() { Method = "get_project_status" })).Payload);
    }

    [Fact]
    public void FailedCountEvenOnSuccessEnvelopeStopsItem()
    {
        var result = VciWritePayloadContract.Project(new() { OperationId = "a", Operation = "export" },
            WorkerCallResult.Ok(WorkerJson.SerializePayload(new VciWriteOutcomeInfo { Leaves = 1, Failed = 1 })));
        Assert.Equal("failed", result.Status); Assert.Equal("worker_operation_failed", result.Failure!.Category);
        Assert.Equal(1, result.Result!.Value.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task ReplanAsync_Throws()
    {
        var domain = new VciWriteDomain(null!, null!);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => domain.ReplanAsync(new() { OperationId = "a", Operation = "export" }));
        Assert.Equal("vci_write plans declare no dependencies; ReplanAsync must never be called.", error.Message);
    }

    private sealed class WireFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "vci-wire-" + Guid.NewGuid().ToString("N"));
        public PersistentWorkerTransport Transport { get; }
        public WireFixture(string response)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "response"), response);
            File.WriteAllText(Path.Combine(directory, "hello"), JsonSerializer.Serialize(new WorkerResponse { Success = true, ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() }, WorkerJson.Envelope));
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), """
                param([string]$Root)
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    $file = if ($r.method -eq 'hello') { 'hello' } else { 'response' }
                    [Console]::Out.WriteLine([IO.File]::ReadAllText((Join-Path $Root $file))); [Console]::Out.Flush()
                }
                """);
            Transport = new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
        }
        public void Dispose() { Transport.Dispose(); Directory.Delete(directory, true); }
    }
}
