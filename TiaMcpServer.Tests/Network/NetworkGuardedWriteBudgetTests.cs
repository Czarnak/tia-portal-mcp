using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;
using Xunit.Abstractions;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public sealed class NetworkGuardedWriteBudgetTests(ITestOutputHelper output)
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
        // Preserve the original pre-field-addition witness exactly, including its measured size.
        var originalShape = CanonicalJson.ToElement(response).EnumerateObject()
            .Where(property => property.Name != "omission").ToDictionary(p => p.Name, p => p.Value);
        Assert.Equal(245287, CanonicalJson.Serialize(originalShape).Length);
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
        output.WriteLine($"Protected core ({(unicode ? "normal Unicode" : "ASCII 256")}, 50 items): {NetworkWritePayloadBudget.MeasureProtectedCore(operations)}");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InfeasibleIdentities_RejectBeforeBindingOrWorker_WithOneAudit(bool registered)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        var operations = Enumerable.Range(0, 50).Select(i => new NetworkOperationRequest
        {
            OperationId = new string('\u4e00', 255) + (char)('\u4e01' + i), Operation = "delete_subnet",
            Target = new() { Kind = "subnet", SubnetId = "subnet-" + i }
        }).ToArray();
        CallToolResult reply;
        if (registered)
        {
            await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite, audit.Path);
            reply = await harness.Client.CallToolAsync("network_write", new Dictionary<string, object?> { ["operations"] = operations });
        }
        else
        {
            var binding = new ProjectSessionBinding(null);
            using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
            var before = CanonicalJson.Serialize(binding.CaptureSnapshot());
            reply = await NetworkGuardedWriteFixture.CreateRunner(client, audit.Path).RunAsync(new NetworkWriteDomain(client),
                new WriteCall<NetworkOperationRequest>(null, operations, false));
            Assert.Equal(before, CanonicalJson.Serialize(binding.CaptureSnapshot()));
        }
        Assert.Empty(requests.Methods());
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.True(reply.IsError);
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.DoesNotContain(operations[0].OperationId, root.GetRawText());
        Assert.Equal("error", root.GetProperty("phase").GetString());
        var record = CanonicalJson.Deserialize<WriteAuditRecord>(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(Assert.Single(reply.Content.OfType<TextContentBlock>()).Text, record.ResponseText);
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(record.ResponseText), record.ResponseHash);
    }

    [Fact]
    public void ProtectedCore_MeasuresEveryEncodedIdentityCopy_AndBoundsAdversarialFallback()
    {
        var operations = Enumerable.Range(0, 50).Select(i => NetworkGuardedWriteFixture.Delete(new string('a', 253) + i.ToString("D3"))).ToArray();
        var emptyIds = operations.Select(o => NetworkGuardedWriteFixture.Delete("")).ToArray();
        var overhead = NetworkWritePayloadBudget.MeasureProtectedCore(emptyIds);
        var encodedIds = operations.Select(o => CanonicalJson.Serialize(o.OperationId).Length - 2).ToArray();
        var reserve = NetworkWritePayloadBudget.MeasureProtectedCore(operations);
        Assert.Equal(overhead + 6 * encodedIds.Sum() + encodedIds.Max(), reserve);
        Assert.True(reserve <= 180000);
        output.WriteLine($"Reserve proof: fixed model overhead={overhead}; six ID copies plus longest partial-guard ID={reserve - overhead}; total={reserve}.");

        var seed = Report(new string('x', 70000));
        var report = seed with
        {
            Effects = operations.Select(o => seed.Effects[0] with { OperationId = o.OperationId }).ToArray(),
            Guards = operations.SelectMany(o => Enumerable.Repeat(new WriteGuardReport("network_delete_connected_subnet", "info",
                o.OperationId, new string('g', 70000), null), 2)).Append(new("partial_write_no_rollback", "info", operations[0].OperationId, "Partial write.", null)).ToArray(),
            Batch = StructuredOperationBatch.FromItems(operations.Select(o => seed.Batch!.Operations[0] with { OperationId = o.OperationId,
                Operation = o.Operation, Result = CanonicalJson.ToElement(new { value = new string('r', 59000) }),
                Failure = new("worker_operation_failed", new string('f', 70000)), Warnings = new[] { new string('w', 70000) } }).ToArray()),
            Verification = seed.Verification! with { Operations = operations.Select(o => seed.Verification.Operations[0] with { OperationId = o.OperationId, Operation = o.Operation }).ToArray() }
        };
        var bounded = Compose(report);
        var canonical = CanonicalJson.Serialize(bounded);
        Assert.True(canonical.Length <= 180000);
        Assert.False(bounded.Success);
        Assert.Equal(operations.Select(o => o.OperationId), bounded.Effects.Select(e => e.OperationId));
        Assert.Equal(operations.Select(o => o.OperationId), bounded.Batch!.Operations.Select(i => i.OperationId));
        Assert.Equal(operations.Select(o => o.OperationId), bounded.Verification!.Operations.Select(i => i.OperationId));
        Assert.All(bounded.Batch.Operations.Where(i => i.Result is not null), i => Assert.True(CanonicalJson.Serialize(i.Result).Length <= 60000));
        Assert.Equal(canonical.Length, bounded.Batch.Truncation!.PresentedChars);
        Assert.Equal(canonical, CanonicalJson.Serialize(Compose(report)));
    }

    [Theory]
    [InlineData("worker_crashed")]
    [InlineData("invalid_payload")]
    public async Task CrashOrInvalidPayload_RemainsUnverified(string kind)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var domain = new NetworkWriteDomain(fixture.Client);
        var marker = "REJECTED_PAYLOAD_MARKER";
        var result = kind == "invalid_payload" ? WorkerCallResult.Ok("{\"unexpected\":\"" + marker + "\"}")
            : WorkerCallResult.Fail("worker_crashed", "Worker exited after dispatch; outcome unknown.");
        var projected = domain.Project(NetworkGuardedWriteFixture.Delete(), result);
        Assert.Equal("failed", projected.Status);
        Assert.Null(projected.Result);
        Assert.Equal(kind == "invalid_payload" ? "protocol_error" : "worker_crashed", projected.Failure!.Category);
        var verification = await new NetworkWriteVerifier(fixture.Client).VerifyAsync("network-guarded",
            StructuredOperationBatch.FromItems(new[] { projected }), new Dictionary<string, NetworkMutationVerificationInfo>());
        Assert.False(verification.Success);
        Assert.Equal("unverified", Assert.Single(verification.Operations).Status);
        Assert.Contains(verification.FinalChecks, c => c.Status == "unverified");
        Assert.DoesNotContain(marker, CanonicalJson.Serialize(projected));
    }

    [Fact]
    public async Task PostReadFailure_RemainsUnverifiedAndDoesNotReplay()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-postread-failure");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.False(response.Success);
        Assert.Equal("applied", response.Phase);
        Assert.Null(response.Error);
        Assert.False(response.Verification!.Success);
        Assert.Contains(response.Verification.FinalChecks, c => c.Status == "unverified");
        Assert.Equal(1, requests.Methods().Count(m => m == "delete_subnet"));
        Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path));
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
