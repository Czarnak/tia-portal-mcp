using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;
using Xunit.Abstractions;
using TiaMcpServer.Contracts.Shared;

namespace TiaMcpServer.Tests.Network;

[Collection("Mcp protocol serial")]
public sealed class NetworkGuardedWriteBudgetTests(ITestOutputHelper output)
{
    [Fact]
    public void AdmittedEscapedOperationIds_InfeasibleProtectedEnvelopeIsRejectedBeforePlanning()
    {
        var operations = Enumerable.Range(0, OperationBatchRules.MaxItems)
            .Select(index => new NetworkOperationRequest
            {
                OperationId = new string('\u4e00', OperationBatchRules.MaxOperationIdChars - 1)
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
        var response = new NetworkGuardedWriteResponse("network_write", "2.0", "applied", false,
            null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            operations.Select(operation => new NetworkWriteEffectPresentation(operation.OperationId, null, null)).ToArray(),
            StructuredOperationBatch.FromItems(operations.Select(operation => new StructuredOperationItem(
                operation.OperationId, operation.Operation, "succeeded", null, null, null, null,
                Array.Empty<string>())).ToArray()),
            new WriteVerification(true, operations.Select(operation => new WriteOperationVerification(
                operation.OperationId, true, "trusted", null, null, null)).ToArray(), null));
        var canonical = CanonicalJson.Serialize(response);

        Assert.True(canonical.Length > 180000);
        // The shared verification shape still exceeds the document limit on identities alone.
        var originalShape = CanonicalJson.ToElement(response).EnumerateObject()
            .Where(property => property.Name != "omission").ToDictionary(p => p.Name, p => p.Value);
        Assert.True(CanonicalJson.Serialize(originalShape).Length > 180000);
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
                new WriteCall<NetworkOperationRequest>(operations, false));
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
        Assert.Equal("sha256:" + ContentHashRules.Sha256Hex(record.ResponseText), record.ResponseHash);
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
            Verification = seed.Verification! with { Operations = operations.Select(o => seed.Verification.Operations[0] with { OperationId = o.OperationId }).ToArray() }
        };
        var bounded = Compose(report);
        var canonical = CanonicalJson.Serialize(bounded);
        Assert.True(canonical.Length <= 180000);
        Assert.False(bounded.Success);
        Assert.Equal(operations.Select(o => o.OperationId), bounded.Effects.Select(e => e.OperationId));
        Assert.Equal(operations.Select(o => o.OperationId), bounded.Batch!.Operations.Select(i => i.OperationId));
        Assert.True(bounded.Verification!.Success);
        Assert.Empty(bounded.Verification.Operations);
        Assert.NotNull(bounded.Verification.Omission);
        Assert.All(bounded.Batch.Operations.Where(i => i.Result is not null), i => Assert.True(CanonicalJson.Serialize(i.Result).Length <= 60000));
        Assert.Equal(canonical.Length, bounded.Batch.Truncation!.PresentedChars);
        Assert.Equal(canonical, CanonicalJson.Serialize(Compose(report)));
    }

    [Theory]
    [InlineData("worker_crashed")]
    [InlineData("invalid_payload")]
    public async Task CrashOrInvalidPayload_HasNoVerificationEntry(string kind)
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded");
        var operation = NetworkGuardedWriteFixture.Delete();
        var marker = "REJECTED_PAYLOAD_MARKER";
        var result = kind == "invalid_payload" ? WorkerCallResult.Ok("{\"unexpected\":\"" + marker + "\"}")
            : WorkerCallResult.Fail("worker_crashed", "Worker exited after dispatch; outcome unknown.");
        var projected = NetworkPayloadContract.Project(operation, result);
        Assert.Equal("failed", projected.Status);
        Assert.Null(projected.Result);
        Assert.Equal(kind == "invalid_payload" ? "protocol_error" : "worker_crashed", projected.Failure!.Category);
        var verification = await new TrustedWriteVerifier<NetworkOperationRequest>(new[] { operation },
            new NetworkVerificationPolicy(new Dictionary<string, NetworkWriteEffect>())).VerifyAsync(StructuredOperationBatch.FromItems(new[] { projected }));
        Assert.True(verification.Success);
        Assert.Empty(verification.Operations);
        Assert.DoesNotContain(marker, CanonicalJson.Serialize(projected));
    }

    [Fact]
    public async Task PostReadFailure_IsNotReadAndDoesNotReplay()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-postread-failure");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        Assert.Equal("applied", response.Phase);
        Assert.Null(response.Error);
        Assert.Equal("trusted", Assert.Single(response.Verification!.Operations).Check);
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
        Assert.True(bounded.Verification!.Success);
        Assert.Equal("trusted", Assert.Single(bounded.Verification.Operations).Check);
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
    [InlineData(100_000, 3_000)]
    [InlineData(1_000, 20_000)]
    public void DiagnosticOmissionReportsEffectiveDocumentLimit(int itemLimit, int documentLimit)
    {
        var response = Compose(Report("small")) with
        {
            Verification = new(true, Enumerable.Range(0, 12).Select(i => new WriteOperationVerification("check" + i, true, "trusted", null, null, new string('x', 300))).ToArray(), null),
        };
        var original = CanonicalJson.Serialize(response);

        var bounded = NetworkWritePayloadBudget.Apply(response, itemLimit, documentLimit);

        Assert.Equal("diagnosticDetailsOmitted", bounded.Omission!.Reason);
        Assert.Equal(documentLimit, bounded.Omission.LimitChars);
        Assert.Equal(original.Length, bounded.Omission.OriginalChars);
        Assert.Equal("network_read", bounded.Omission.RetryTool);
        Assert.Equal(response.Batch!.Counts, bounded.Batch!.Counts);
        Assert.True(bounded.Verification!.Success);
        Assert.False(bounded.Success);
        Assert.Equal(original, CanonicalJson.Serialize(response));
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, documentLimit);
    }

    [Fact]
    public void OrdinaryDiagnosticCapRetainsItsOwnLimit()
    {
        var response = Compose(Report("small")) with { Error = new("protocol_error", new string('x', 600)) };

        var bounded = NetworkWritePayloadBudget.Apply(response, maxDocumentChars: 20_000);

        Assert.Equal(512, bounded.Omission!.LimitChars);
        Assert.Equal("diagnosticDetailsOmitted", bounded.Omission.Reason);
        Assert.Equal("protocol_error", bounded.Error!.Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownOversizedSetting_RefusesBeforeMutation_WithCanonicalAudit(bool registered)
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
                new WriteCall<NetworkOperationRequest>(new[] { operation }, false));
        }
        var root = NetworkGuardedWriteMcpTests.Document(reply);
        var canonical = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("error", root.GetProperty("phase").GetString());
        Assert.True(reply.IsError == true);
        Assert.Equal("validation_error", root.GetProperty("error").GetProperty("category").GetString());
        Assert.True(canonical.Length <= 180000);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("verification").ValueKind);

        var record = CanonicalJson.Deserialize<WriteAuditRecord>(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(canonical, record.ResponseText);
        Assert.Equal("sha256:" + ContentHashRules.Sha256Hex(canonical), record.ResponseHash);
        Assert.Equal("skipped", Assert.Single(record.Items).Status);
        Assert.DoesNotContain("configure_network_device", requests.Methods());
    }

    [Theory]
    [InlineData("network-qualified-budget-long", 50)]
    [InlineData("network-qualified-budget-item", 1)]
    public async Task LongPreparedOwnerPaths_RefuseBeforeFirstMutation(string scenario, int count)
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var operation = await QualifiedConfigure(fixture, scenario, "one");
        var operations = Enumerable.Range(0, count).Select(i => new NetworkOperationRequest
        { OperationId = new string('a', 253) + i.ToString("D3"), Operation = operation.Operation,
            Target = operation.Target, Changes = operation.Changes }).ToArray();
        Assert.All(operations, item => Assert.Equal(256, item.OperationId.Length));
        Assert.True(new NetworkWriteDomain(fixture.Client).Validate(operations, McpAccessMode.ReadWrite).IsValid);
        var reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
            new WriteCall<NetworkOperationRequest>(operations, false));
        Assert.DoesNotContain("configure_network_device", requests.Methods());
        var document = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.True(reply.IsError);
        Assert.Equal("error", document.GetProperty("phase").GetString());
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        AssertQualifiedAudit(reply, audit);
    }

    [Fact]
    public async Task QualifiedRecoveryCore_IsNeverFlattenedOrOmitted()
    {
        using var audit = new TempAuditDirectory();
        const string scenario = "network-qualified-budget-escaped";
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var response = await fixture.RunAsync(false, await QualifiedConfigure(fixture, scenario, "one"));
        var originalPath = CanonicalJson.Serialize(response.Effects[0].Effect!.Target.InterfacePath);
        response = response with { Verification = response.Verification! with { Operations = response.Verification.Operations.Select(o => o with { Message = new string('x', 70000) }).ToArray() } };
        var bounded = NetworkWritePayloadBudget.Apply(response);
        Assert.Equal(originalPath, CanonicalJson.Serialize(bounded.Effects[0].Effect!.Target.InterfacePath));
        Assert.Equal("192.168.12.7", bounded.Batch!.Operations[0].Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.Equal("trusted", Assert.Single(bounded.Verification!.Operations).Check);
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, 180000);
    }

    [Fact]
    public async Task MultipleAffectedE1Nodes_RemainDistinctUnderBudget()
    {
        using var audit = new TempAuditDirectory();
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-qualified-delete");
        var response = await fixture.RunAsync(false, NetworkGuardedWriteFixture.Delete());
        Assert.True(response.Success);
        var nodes = response.Effects[0].Effect!.AffectedNodes;
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.Equal("E1", n.NodeId));
        Assert.NotEqual(NetworkInterfacePathEncoding.Encode(nodes[0].InterfacePath!), NetworkInterfacePathEncoding.Encode(nodes[1].InterfacePath!));
        Assert.Equal("trusted", Assert.Single(response.Verification!.Operations).Check);
    }

    [Fact]
    public async Task EscapedPaths_UseEncodedSize()
    {
        using var audit = new TempAuditDirectory();
        const string scenario = "network-qualified-budget-escaped";
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var operation = await QualifiedConfigure(fixture, scenario, "one");
        operation.Target!.InterfaceName = "PROFINET interface_1";
        var reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
            new WriteCall<NetworkOperationRequest>(new[] { operation }, false));
        var response = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(NetworkGuardedWriteMcpTests.Document(reply).GetRawText());
        Assert.True(response.Success);
        var path = response.Effects[0].Effect!.Target.InterfacePath!;
        Assert.Equal(operation.Target.InterfacePath![0].Name, path[0].Name);
        AssertQualifiedAudit(reply, audit);
    }

    [Fact]
    public async Task LatePartialFailure_PreservesExactRecoveryCore()
    {
        using var audit = new TempAuditDirectory();
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, "network-guarded-rejected-setting");
        var operation = NetworkGuardedWriteFixture.Configure("first", "10.0.0.5");
        operation.Changes = new() { IpAddress = "10.0.0.5", PnDeviceName = "rejected" };
        var reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
            new WriteCall<NetworkOperationRequest>(new[] { operation, NetworkGuardedWriteFixture.Delete("second") }, false));
        var response = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(NetworkGuardedWriteMcpTests.Document(reply).GetRawText());
        Assert.Single(requests.Methods(), m => m == "configure_network_device");
        Assert.DoesNotContain("delete_subnet", requests.Methods());
        Assert.Equal("failed", response.Batch!.Operations[0].Status);
        Assert.Equal("engineering_rejected", response.Batch.Operations[0].Failure!.Category);
        Assert.Equal("10.0.0.5", response.Batch.Operations[0].Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.Equal("skipped", response.Batch.Operations[1].Status);
        Assert.Empty(response.Verification!.Operations);
        AssertQualifiedAudit(reply, audit);
    }

    [Fact]
    public async Task Audit_MatchesDeliveredQualifiedDocument()
    {
        using var audit = new TempAuditDirectory();
        const string scenario = "network-qualified-partial";
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var operation = await QualifiedConfigure(fixture, scenario, "partial");
        operation.Changes = new() { IpAddress = "192.168.12.7", SubnetMask = "255.255.255.0" };
        var reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
            new WriteCall<NetworkOperationRequest>(new[] { operation }, false));
        var response = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(NetworkGuardedWriteMcpTests.Document(reply).GetRawText());
        Assert.True(response.Success);
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.NotNull(response.Effects[0].Effect!.Target.InterfacePath);
        Assert.Equal("192.168.12.7", response.Batch.Operations[0].Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.True(response.Batch.Operations[0].Result!.Value.GetProperty("skippedSettings").TryGetProperty("SubnetMask", out _));
        AssertQualifiedAudit(reply, audit);
    }

    // Supplemental post-implementation boundary checks; these are not the causal RED evidence.

    private static async Task<NetworkOperationRequest> QualifiedConfigure(NetworkGuardedWriteFixture fixture, string scenario, string id)
    {
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client);
        var device = snapshot.State!.Devices[0];
        var owner = device.Items[0].Items[0];
        return new() { OperationId = id, Operation = "configure_network_device", Changes = new() { IpAddress = "192.168.12.7" },
            Target = new() { DeviceName = device.Name, NodeId = "E1",
                InterfacePath = new[] { device.Items[0], owner }.Select(i => new NetworkInterfacePathSegment
                { Name = i.Name!, PositionNumber = i.PositionNumber }).ToArray() } };
    }
    private static void AssertQualifiedAudit(CallToolResult reply, TempAuditDirectory audit)
    {
        var text = Assert.Single(reply.Content.OfType<TextContentBlock>()).Text;
        Assert.InRange(text.Length, 0, 180000);
        Assert.Equal(text, ((JsonElement)reply.StructuredContent!).GetRawText());
        var record = CanonicalJson.Deserialize<WriteAuditRecord>(Assert.Single(NetworkGuardedWriteMcpTests.AuditLines(audit.Path)));
        Assert.Equal(text, record.ResponseText);
        Assert.Equal("sha256:" + ContentHashRules.Sha256Hex(text), record.ResponseHash);
    }
    private static NetworkGuardedWriteResponse Compose(WriteReport<NetworkWriteEffect, WriteVerification> report)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));
        return new NetworkWriteDomain(client).Compose(report);
    }

    private static WriteReport<NetworkWriteEffect, WriteVerification> Report(string value)
    {
        var target = new NetworkWriteTargetEvidence("one", "configure_network_device", value, null,
            Array.Empty<string>(), null, null, "node", null, null, null, null);
        var effect = new NetworkWriteEffect("configure_network_device", target,
            new Dictionary<string, string> { ["PnDeviceName"] = value }, new Dictionary<string, string?>(),
            Array.Empty<NetworkNodeIdentityInfo>(), true);
        return new("applied", true, null, Array.Empty<string>(), Array.Empty<WriteGuardReport>(),
            new[] { new WriteEffect<NetworkWriteEffect>("one", effect) },
            StructuredOperationBatch.FromItems(new[] { new StructuredOperationItem("one", "configure_network_device", "succeeded",
                CanonicalJson.ToElement(new { value }), null, null, null, Array.Empty<string>()) }),
            new WriteVerification(true, new[] { new WriteOperationVerification("one", true, "trusted", null, null, "completed") }, null));
    }
}
