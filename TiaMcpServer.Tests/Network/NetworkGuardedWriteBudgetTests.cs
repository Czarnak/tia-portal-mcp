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
                new WriteCall<NetworkOperationRequest>("network-guarded", new[] { operation }, false));
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
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(canonical), record.ResponseHash);
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
            new WriteCall<NetworkOperationRequest>(scenario, operations, false));
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
        var operation = await QualifiedConfigure(fixture, scenario, "one");
        var response = await fixture.RunAsync(false, operation);
        var identity = CanonicalJson.Serialize(response.Verification!.Operations[0].Evidence!.Identity);
        var originalPath = CanonicalJson.Serialize(response.Effects[0].Effect!.Target.InterfacePath);
        // Huge worker diagnostic text is removable; the entire exact identity/check core is not.
        response.Verification.Operations[0].Evidence!.Message = new string('x', 70000);
        response.Verification.Operations[0].Evidence!.Checks[0].Message = new string('x', 70000);
        var bounded = NetworkWritePayloadBudget.Apply(response);
        Assert.NotNull(bounded.Verification!.Operations[0].Evidence);
        Assert.Equal(identity, CanonicalJson.Serialize(bounded.Verification.Operations[0].Evidence!.Identity));
        Assert.Equal(originalPath, CanonicalJson.Serialize(bounded.Effects[0].Effect!.Target.InterfacePath));
        Assert.Equal("192.168.12.7", bounded.Verification.Operations[0].Evidence!.Checks[0].Expected);
        Assert.Equal("192.168.12.7", bounded.Verification.Operations[0].Evidence!.Checks[0].Observed);
        Assert.Contains(bounded.Verification.FinalChecks, c => c.Name.Contains(NetworkInterfacePathEncoding.Encode(bounded.Verification.Operations[0].Evidence!.Identity.InterfacePath!), StringComparison.Ordinal));
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, 180000);
        Assert.InRange(CanonicalJson.Serialize(bounded.Verification.Operations[0].Evidence).Length, 0, 60000);
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
        Assert.All(nodes, n => Assert.Contains(response.Verification!.FinalChecks,
            c => c.Name.Contains(NetworkInterfacePathEncoding.Encode(n.InterfacePath!), StringComparison.Ordinal) && c.Name.EndsWith("/removedSubnet:subnet-1")));
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
            new WriteCall<NetworkOperationRequest>(scenario, new[] { operation }, false));
        var response = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(NetworkGuardedWriteMcpTests.Document(reply).GetRawText());
        Assert.True(response.Success);
        var path = response.Effects[0].Effect!.Target.InterfacePath!;
        Assert.Equal(operation.Target.InterfacePath![0].Name, path[0].Name);
        Assert.Equal(CanonicalJson.Serialize(path), CanonicalJson.Serialize(response.Verification!.Operations[0].Evidence!.Identity.InterfacePath));
        Assert.Equal("PROFINET interface_1", response.Verification.Operations[0].Evidence!.Identity.InterfaceName);
        AssertQualifiedAudit(reply, audit);
    }

    [Fact]
    public async Task LatePartialFailure_PreservesExactRecoveryCore()
    {
        using var audit = new TempAuditDirectory();
        const string scenario = "network-qualified-budget-late-growth";
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var operation = await QualifiedConfigure(fixture, scenario, "first");
        var reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
            new WriteCall<NetworkOperationRequest>(scenario, new[] { operation, NetworkGuardedWriteFixture.Delete("second"), new NetworkOperationRequest { OperationId = "third", Operation = operation.Operation, Target = operation.Target, Changes = operation.Changes } }, false));
        var response = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(NetworkGuardedWriteMcpTests.Document(reply).GetRawText());
        Assert.Equal(1, requests.Methods().Count(m => m == "configure_network_device"));
        Assert.DoesNotContain("delete_subnet", requests.Methods());
        Assert.Equal("succeeded", response.Batch!.Operations[0].Status);
        Assert.Equal("failed", response.Batch.Operations[1].Status);
        Assert.Equal("validation_error", response.Batch.Operations[1].Failure!.Category);
        Assert.Equal("skipped", response.Batch.Operations[2].Status);
        Assert.Equal("192.168.12.7", response.Batch.Operations[0].Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.Equal(CanonicalJson.Serialize(response.Effects[0].Effect!.Target.InterfacePath),
            CanonicalJson.Serialize(response.Verification!.Operations[0].Evidence!.Identity.InterfacePath));
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
            new WriteCall<NetworkOperationRequest>(scenario, new[] { operation }, false));
        var response = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(NetworkGuardedWriteMcpTests.Document(reply).GetRawText());
        Assert.False(response.Success);
        Assert.Equal("failed", response.Batch!.Operations[0].Status);
        Assert.NotNull(response.Effects[0].Effect!.Target.InterfacePath);
        Assert.Equal("192.168.12.7", response.Batch.Operations[0].Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.True(response.Batch.Operations[0].Result!.Value.GetProperty("skippedSettings").TryGetProperty("SubnetMask", out _));
        AssertQualifiedAudit(reply, audit);
    }

    // Supplemental post-implementation boundary checks; these are not the causal RED evidence.
    [Fact]
    public async Task Supplemental_SmallDiagnosticsTogetherOverflowItem_KeepExactCore()
    {
        using var audit = new TempAuditDirectory();
        const string scenario = "network-qualified-read";
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var response = await fixture.RunAsync(false, await QualifiedConfigure(fixture, scenario, "one"));
        var evidence = response.Verification!.Operations[0].Evidence!;
        var identity = CanonicalJson.Serialize(evidence.Identity);
        var itemLimit = new[] { CanonicalJson.Serialize(response.Effects[0].Effect).Length,
            CanonicalJson.Serialize(response.Batch!.Operations[0].Result).Length,
            CanonicalJson.Serialize(response.Verification.FinalChecks).Length,
            CanonicalJson.Serialize(evidence).Length }.Max() + 1;
        evidence.Message = new string('m', 450);
        evidence.Checks[0].Message = new string('c', 450);
        Assert.True(CanonicalJson.Serialize(evidence).Length > itemLimit);
        var bounded = NetworkWritePayloadBudget.Apply(response, maxItemChars: itemLimit);
        Assert.NotNull(bounded.Verification!.Operations[0].Evidence);
        Assert.Equal(identity, CanonicalJson.Serialize(bounded.Verification.Operations[0].Evidence!.Identity));
        Assert.Equal("192.168.12.7", bounded.Verification.Operations[0].Evidence!.Checks[0].Expected);
        Assert.InRange(CanonicalJson.Serialize(bounded.Verification.Operations[0].Evidence).Length, 0, itemLimit);
        Assert.NotNull(bounded.Omission);
        Assert.False(bounded.Success);
    }

    [Fact]
    public async Task Supplemental_WorkerGeneratedIdentityOverflow_RetainsKnownQualifiedRecoveryAndExecutionStatus()
    {
        using var audit = new TempAuditDirectory();
        const string scenario = "network-qualified-read";
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var response = await fixture.RunAsync(false, await QualifiedConfigure(fixture, scenario, "one"));
        var originalPath = CanonicalJson.Serialize(response.Effects[0].Effect!.Target.InterfacePath);
        var operation = new NetworkOperationRequest { OperationId = "generated", Operation = "create_subnet",
            Subnet = new() { Name = "created", NetworkType = "Ethernet" } };
        var generatedId = new string('s', 70000);
        var generatedEvidence = new NetworkMutationVerificationInfo { Status = "passed",
            Identity = new() { SubnetId = generatedId }, Checks = new()
            {
                new() { Name = "subnetIdentity", Expected = generatedId, Observed = generatedId, Status = "passed" },
                new() { Name = "Name", Expected = "created", Observed = "created", Status = "passed" },
                new() { Name = "TypeIdentifier", Expected = "System:Subnet.Ethernet", Observed = "System:Subnet.Ethernet", Status = "passed" },
                new() { Name = "networkDeviceCountUnchanged", Expected = "1", Observed = "1", Status = "passed" }
            } };
        var typed = new SubnetLifecycleResultInfo { SubnetId = generatedId, Name = "created",
            NetworkDeviceCount = 1, NetworkDeviceCountUnchanged = true, Verification = generatedEvidence };
        var projected = NetworkPayloadContract.Project(operation, WorkerCallResult.Ok(WorkerJson.SerializePayload(typed)), requireVerification: true);
        Assert.Equal("succeeded", projected.Status);
        response = response with
        {
            Batch = StructuredOperationBatch.FromItems(response.Batch!.Operations.Append(projected).ToArray()),
            Verification = response.Verification! with
            {
                Operations = response.Verification.Operations.Append(new("generated", "create_subnet", "passed", generatedEvidence, null)).ToArray(),
                FinalChecks = response.Verification.FinalChecks.Append(new() { Name = $"subnet/{generatedId}///exists",
                    Expected = "true", Observed = "true", Status = "passed" }).ToArray()
            }
        };
        var bounded = NetworkWritePayloadBudget.Apply(response);
        Assert.False(bounded.Success);
        Assert.Equal("applied", bounded.Phase);
        Assert.NotNull(bounded.Effects[0].Effect);
        Assert.Equal(originalPath, CanonicalJson.Serialize(bounded.Effects[0].Effect!.Target.InterfacePath));
        Assert.Equal("192.168.12.7", bounded.Batch!.Operations[0].Result!.Value.GetProperty("appliedSettings").GetProperty("Address").GetString());
        Assert.Equal("succeeded", bounded.Batch.Operations[1].Status);
        Assert.NotNull(bounded.Batch.Operations[1].Omission);
        Assert.NotNull(bounded.Verification!.Operations[1].Omission);
        Assert.InRange(CanonicalJson.Serialize(bounded).Length, 0, 180000);
    }
    [Fact]
    public async Task KnownPriorValues_FinalItemOverflow_RefusesBeforeMutation()
    {
        using var audit = new TempAuditDirectory();
        Directory.CreateDirectory(audit.Path);
        using var requests = new FakeWorkerRequestLog(audit.Path);
        const string scenario = "network-qualified-budget-known-observations";
        using var fixture = await NetworkGuardedWriteFixture.CreateAsync(audit, scenario);
        var operation = await QualifiedConfigure(fixture, scenario, "known");
        operation.Changes = new() { IpAddress = new string('b', 333),
            SubnetMask = new string('n', 333), PnDeviceName = new string('q', 334) };
        var plan = await new NetworkWritePlanner(fixture.Client).PlanAsync(scenario, new[] { operation });
        Assert.True(plan.Success);
        var effect = plan.Items[0].Effect!;
        var encodedOwner = NetworkInterfacePathEncoding.Encode(effect.Target.InterfacePath!);
        // Feed a valid failed immediate observation through the production final verifier;
        // preparation's exact old scalar values remain in the ordinary FakeWorker state.
        var immediate = new NetworkMutationVerificationInfo { Status = "failed",
            Identity = new() { DeviceName = effect.Target.DeviceName!, NodeId = effect.Target.NodeId!,
                InterfacePath = effect.Target.InterfacePath!.ToList() }, Checks = effect.RequestedSettings.Select(pair => new NetworkVerificationCheckInfo
            { Name = pair.Key, Expected = pair.Value,
                Observed = effect.CurrentSettings[pair.Key].Value!.Value!.ToString(),
                Status = "failed", Message = "The attempted setting retained its known old value." }).ToList() };
        var typedResult = new ConfigureNetworkDeviceResultInfo { DeviceName = effect.Target.DeviceName!,
            AppliedSettings = new(effect.RequestedSettings), Verification = immediate };
        var prepared = NetworkIdentityResolver.BindPreparedTarget(operation, effect.Target);
        var projected = NetworkPayloadContract.Project(prepared, WorkerCallResult.Ok(WorkerJson.SerializePayload(typedResult)),
            requireVerification: true);
        Assert.Equal("failed", projected.Status);
        var observations = new Dictionary<string, NetworkWriteEffect> { [operation.OperationId] = effect };
        var verification = await new NetworkWriteVerifier(fixture.Client, observations, observations).VerifyAsync(scenario,
            StructuredOperationBatch.FromItems(new[] { projected }),
            new Dictionary<string, NetworkMutationVerificationInfo> { [operation.OperationId] = immediate });
        var effectChars = CanonicalJson.Serialize(effect).Length;
        var evidenceChars = CanonicalJson.Serialize(immediate).Length;
        var resultChars = CanonicalJson.Serialize(typedResult).Length;
        var finalChars = CanonicalJson.Serialize(verification.FinalChecks).Length;
        var reservation = NetworkWritePayloadBudget.MeasurePreparedCore(new[] { operation }, plan.Items);
        output.WriteLine($"Encoded owner={CanonicalJson.Serialize(encodedOwner).Length}; effect={effectChars}; result={resultChars}; immediate={evidenceChars}; finalChecks={finalChars}; reservation={reservation}.");
        Assert.InRange(effectChars, 0, 60000);
        Assert.InRange(resultChars, 0, 60000);
        Assert.InRange(evidenceChars, 0, 60000);
        Assert.True(finalChars > 60000);
        Assert.All(verification.FinalChecks.Where(c => c.Name.EndsWith("/Address") || c.Name.EndsWith("/SubnetMask") || c.Name.EndsWith("/PnDeviceName")),
            c => Assert.Equal(7000, c.Observed!.Length));
        var reply = await fixture.Runner.RunAsync(new NetworkWriteDomain(fixture.Client),
            new WriteCall<NetworkOperationRequest>(scenario, new[] { operation }, false));
        var deliveredDocument = NetworkGuardedWriteMcpTests.Document(reply);
        output.WriteLine($"Delivered phase={deliveredDocument.GetProperty("phase").GetString()}; writes={requests.Methods().Count(m => m == "configure_network_device")}.");
        if (deliveredDocument.GetProperty("phase").GetString() == "applied")
        {
            // This witnesses the old implementation's actual post-mutation failure in RED.
            var delivered = CanonicalJson.Deserialize<NetworkGuardedWriteResponse>(deliveredDocument.GetRawText());
            Assert.Equal("failed", delivered.Batch!.Operations[0].Status);
            Assert.All(delivered.Verification!.Operations[0].Evidence!.Checks, c => Assert.Equal(7000, c.Observed!.Length));
            Assert.NotNull(delivered.Verification.Omission);
            Assert.Empty(delivered.Verification.FinalChecks);
            var after = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, scenario);
            var oldNode = after.State!.Devices[0].Items[0].Items[0].NetworkInterfaces[0].Nodes[0];
            Assert.Equal(7000, oldNode.IpAddress!.Length);
            Assert.Equal(7000, oldNode.SubnetMask!.Length);
            Assert.Equal(7000, oldNode.PnDeviceName!.Length);
            output.WriteLine("Actual attempted item failed; all three known old values remain 7000 characters; required final checks were omitted after mutation.");
        }
        Assert.DoesNotContain("configure_network_device", requests.Methods());
        Assert.True(reply.IsError);
        var document = NetworkGuardedWriteMcpTests.Document(reply);
        Assert.Equal("validation_error", document.GetProperty("error").GetProperty("category").GetString());
        AssertQualifiedAudit(reply, audit);
    }
    private static async Task<NetworkOperationRequest> QualifiedConfigure(NetworkGuardedWriteFixture fixture, string scenario, string id)
    {
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(fixture.Client, scenario);
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
        Assert.Equal("sha256:" + ContentHashes.Sha256Hex(text), record.ResponseHash);
    }
    private static NetworkGuardedWriteResponse Compose(WriteReport<NetworkWriteEffect, NetworkWriteVerification> report)
    {
        using var client = new OpennessWorkerClient(new ProjectSessionBinding(null));
        return new NetworkWriteDomain(client).Compose(report);
    }

    private static WriteReport<NetworkWriteEffect, NetworkWriteVerification> Report(string value)
    {
        var evidence = new NetworkMutationVerificationInfo { Status = "passed",
            Identity = new() { DeviceName = value }, Checks = new() { new() { Name = "setting", Expected = value, Observed = value, Status = "passed" } } };
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
