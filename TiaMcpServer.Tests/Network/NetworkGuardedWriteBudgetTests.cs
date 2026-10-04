using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public sealed class NetworkGuardedWriteBudgetTests
{
    [Fact]
    public void AdmittedEscapedOperationIds_InfeasibleProtectedEnvelopeIsRejectedBeforePlanning()
    {
        var operations = Enumerable.Range(0, NetworkOperationCatalog.MaxBatchSize)
            .Select(index => new NetworkOperationRequest
            {
                OperationId = new string('\u4e00', NetworkOperationCatalog.MaxOperationIdLength - 1)
                    + (char)('\u4e01' + index),
                Operation = "delete_subnet",
                Target = new NetworkObjectTarget { Kind = "subnet", SubnetId = "subnet-" + index },
            }).ToArray();
        var validation = NetworkOperationCatalog.ValidateWrite(operations);
        Assert.True(validation.IsValid, validation.Error);
        Assert.Equal(50, operations.Select(operation => operation.OperationId).Distinct().Count());
        Assert.All(operations, operation => Assert.Equal(256, operation.OperationId.Length));

        // A strict lower bound: no effect/result/evidence, omission metadata, diagnostics,
        // guards, warnings, truncation metadata, or final checks. Only required roots,
        // exact identities, and trusted execution/verification summaries remain.
        var response = new NetworkGuardedWriteResponse("network_write", "1.0", "applied", false,
            null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            operations.Select(operation => new NetworkWriteEffectPresentation(operation.OperationId, null, null)).ToArray(),
            StructuredOperationBatch.FromItems(operations.Select(operation => new StructuredOperationItem(
                operation.OperationId, operation.Operation, "succeeded", null, null, null, null,
                Array.Empty<string>())).ToArray()),
            new NetworkWriteVerification(true, operations.Select(operation => new NetworkOperationVerification(
                operation.OperationId, operation.Operation, "passed", null, null)).ToArray(),
                Array.Empty<NetworkVerificationCheckInfo>(), null));
        var canonical = CanonicalJson.Serialize(response);

        Assert.True(canonical.Length > 180000);
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));
        var domainValidation = new NetworkWriteDomain(client).Validate(operations, McpAccessMode.ReadWrite);
        Assert.False(domainValidation.IsValid);
        Assert.Equal("validation_error", domainValidation.Error!.Category);
        Assert.DoesNotContain(operations[0].OperationId, domainValidation.Error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FeasibleFiftyIdentities_AreAdmitted(bool unicode)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));
        var operations = Enumerable.Range(0, 50).Select(i => NetworkGuardedWriteFixture.Delete(
            unicode ? "操作-" + i : new string('a', 253) + i.ToString("D3"))).ToArray();
        Assert.True(new NetworkWriteDomain(client).Validate(operations, McpAccessMode.ReadWrite).IsValid);
    }

    [Fact]
    public void WholeEnvelope_IncludesEffectsGuardsAndVerification()
    {
        var input = Report(new string('x', 70000));
        var before = CanonicalJson.Serialize(input);
        var bounded = Compose(input);
        Assert.True(CanonicalJson.Serialize(bounded).Length <= 180000);
        Assert.False(bounded.Success);
        Assert.Null(bounded.Effects[0].Effect);
        Assert.NotNull(bounded.Effects[0].Omission);
        Assert.Null(bounded.Verification!.Operations[0].Evidence);
        Assert.NotNull(bounded.Verification.Operations[0].Omission);
        Assert.Empty(bounded.Verification.FinalChecks);
        Assert.NotNull(bounded.Verification.Omission);
        Assert.True(bounded.Verification.Success);
        Assert.Equal("passed", bounded.Verification.Operations[0].Status);
        Assert.Equal(before, CanonicalJson.Serialize(input));
    }

    [Fact]
    public void FailedPartialResult_OmissionRetainsFailure()
    {
        var input = Report(new string('x', 70000));
        var failed = input.Batch!.Operations[0] with { Status = "failed",
            Failure = new("worker_operation_failed", "Requested IO setting was skipped.") };
        var skipped = failed with { OperationId = "later", Status = "skipped", Result = null,
            Failure = null, SkipReason = "earlierOperationFailed" };
        input = input with { Success = false, Batch = StructuredOperationBatch.FromItems(new[] { failed, skipped }) };
        var bounded = Compose(input);
        var omittedPartial = bounded.Batch!.Operations[0];
        Assert.Equal("failed", omittedPartial.Status);
        Assert.Equal("worker_operation_failed", omittedPartial.Failure!.Category);
        Assert.NotNull(omittedPartial.Omission);
        Assert.Null(omittedPartial.Result);
        Assert.Equal("skipped", bounded.Batch.Operations[1].Status);
        Assert.Equal("earlierOperationFailed", bounded.Batch.Operations[1].SkipReason);
        Assert.Equal(1, bounded.Batch.Counts.Failed);
        Assert.Equal(1, bounded.Batch.Counts.Skipped);
        Assert.False(bounded.Success);
    }

    [Fact]
    public void LargeDiagnostic_IsBoundedWithoutPayloadEcho()
    {
        var marker = "REJECTED_PAYLOAD_MARKER";
        var diagnostic = marker + new string('\u4e00', 70000);
        var report = Report("small") with { Phase = "error", Success = false,
            Error = new("protocol_error", diagnostic), Warnings = new[] { diagnostic },
            Guards = new[] { new WriteGuardReport("network_state_unverifiable", "block", "one", diagnostic, null) },
            Batch = null, Verification = null, Effects = Array.Empty<WriteEffect<NetworkWriteEffect>>() };
        var bounded = Compose(report);
        var canonical = CanonicalJson.Serialize(bounded);
        Assert.True(canonical.Length <= 180000);
        Assert.DoesNotContain(marker, canonical);
        Assert.Equal("protocol_error", bounded.Error!.Category);
        Assert.Equal("block", Assert.Single(bounded.Guards).Severity);
        Assert.Equal("one", bounded.Guards[0].OperationId);
        using var doc = JsonDocument.Parse(canonical);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("omission").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturnedCanonicalHash_EqualsAuditHash_AndOmissionNeverRequiresWriteReplay(bool registered)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var operation = NetworkGuardedWriteFixture.Configure("large");
        operation.Changes = new() { PnDeviceName = new string('x', 61000) };
        CallToolResult reply;
        if (registered)
        {
            await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite,
                audit.Path, "network-guarded");
            reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?> { ["operations"] = new[] { operation } });
        }
        else
        {
            using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
            reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
                new WriteCall<NetworkOperationRequest>("network-guarded", new[] { operation }, false));
        }
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        var canonical = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("applied", root.GetProperty("phase").GetString());
        Assert.False(reply.IsError == true);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("error").ValueKind);
        Assert.True(canonical.Length <= 180000);
        Assert.True(root.GetProperty("verification").GetProperty("success").GetBoolean());
        Assert.Equal("passed", root.GetProperty("verification").GetProperty("operations")[0].GetProperty("status").GetString());
        var record = CanonicalJson.Deserialize<WriteAuditRecord>(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(canonical, record.ResponseText);
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(canonical), record.ResponseHash);
        Assert.Equal("succeeded", Assert.Single(record.Items).Status);
        Assert.Equal(1, requests.Methods().Count(method => method == "configure_network_device"));
    }

    private static NetworkGuardedWriteResponse Compose(WriteReport<NetworkWriteEffect, NetworkWriteVerification> report)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));
        return new NetworkWriteDomain(client).Compose(report);
    }

    private static WriteReport<NetworkWriteEffect, NetworkWriteVerification> Report(string value)
    {
        var evidence = new NetworkMutationVerificationInfo { Status = "passed",
            Identity = new() { ["deviceName"] = value }, Checks = new() { new() { Name = "setting", Expected = value, Observed = value, Status = "passed" } } };
        var target = new NetworkWriteTargetEvidence("one", "configure_network_device", value, null,
            Array.Empty<string>(), null, null, "node", null, null, null, null);
        var effect = new NetworkWriteEffect("configure_network_device", target,
            new Dictionary<string, string> { ["PnDeviceName"] = value }, new Dictionary<string, NetworkAttributeInfo>(),
            Array.Empty<NetworkNodeIdentityInfo>(), true, 1);
        return new("applied", true, null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            new[] { new WriteEffect<NetworkWriteEffect>("one", effect) },
            StructuredOperationBatch.FromItems(new[] { new StructuredOperationItem("one", "configure_network_device", "succeeded",
                CanonicalJson.ToElement(new { value }), null, null, null, Array.Empty<string>()) }),
            new NetworkWriteVerification(true, new[] { new NetworkOperationVerification("one", "configure_network_device", "passed", evidence, null) },
                evidence.Checks, null));
    }
}
