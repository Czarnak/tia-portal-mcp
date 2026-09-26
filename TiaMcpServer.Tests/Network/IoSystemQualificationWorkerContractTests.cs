using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using Xunit;
using QualificationEvidence = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence;

namespace TiaMcpServer.Tests.Network;

public class IoSystemQualificationWorkerContractTests
{
    private static string Source => File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Openness/IoSystemQualificationProbeService.cs"));

    [Fact]
    public void MasterPlcSelection_RequiresUniqueDeviceSoftwareAndFreshAncestorIdentity()
    {
        var plc = new object();
        var ancestors = new[] { new object(), new object() };
        var selected = QualificationEvidence.SelectMasterPlcAncestor(
            new[] { plc }, ancestors, item => ReferenceEquals(item, ancestors[1]) ? plc : null,
            _ => true, depth => ancestors[depth - 1]);
        Assert.NotNull(selected);
        Assert.Same(ancestors[1], selected.Item);
        Assert.Equal(2, selected.Depth);

        Assert.Null(QualificationEvidence.SelectMasterPlcAncestor(
            new[] { plc, new object() }, ancestors, _ => plc, _ => true, depth => ancestors[depth - 1]));
        Assert.Null(QualificationEvidence.SelectMasterPlcAncestor(
            new[] { plc }, ancestors, item => ReferenceEquals(item, ancestors[1]) ? plc : null,
            _ => true, _ => new object()));
    }

    [Fact]
    public void MasterPlcSelection_RejectsMissingRoleOrCompileService()
    {
        var plc = new object();
        var ancestor = new object();
        Assert.Null(QualificationEvidence.SelectMasterPlcAncestor(
            Array.Empty<object>(), new[] { ancestor }, _ => plc, _ => true, _ => ancestor));
        Assert.Null(QualificationEvidence.SelectMasterPlcAncestor(
            new[] { plc }, new[] { ancestor }, _ => new object(), _ => true, _ => ancestor));
        Assert.Null(QualificationEvidence.SelectMasterPlcAncestor(
            new[] { plc }, new[] { ancestor }, _ => plc, _ => false, _ => ancestor));
        Assert.Null(QualificationEvidence.SelectMasterPlcAncestor(
            new[] { plc }, new[] { ancestor }, _ => throw new InvalidOperationException(),
            _ => true, _ => ancestor));
    }

    [Fact]
    public void MasterPlcContinuity_AllowsTextRenamesButRejectsIndexedPathDrift()
    {
        var original = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.DeviceItem, DeviceName = "master",
            ItemPath = new List<DeviceItemPathSegmentInfo>
            {
                new() { Index = 0, Name = "rack", PositionNumber = 1, TypeIdentifier = "type-a" },
                new() { Index = 2, Name = "cpu", PositionNumber = 3, TypeIdentifier = "type-b" }
            }
        };
        var renamed = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.DeviceItem, DeviceName = "renamed master",
            ItemPath = new List<DeviceItemPathSegmentInfo>
            {
                new() { Index = 0, Name = "renamed rack", PositionNumber = 1, TypeIdentifier = "type-a" },
                new() { Index = 2, Name = "renamed cpu", PositionNumber = 3, TypeIdentifier = "type-b" }
            }
        };
        Assert.True(QualificationEvidence.SameIndexedDeviceItemPath(original, renamed));
        renamed.ItemPath[1].PositionNumber = 4;
        Assert.False(QualificationEvidence.SameIndexedDeviceItemPath(original, renamed));
        renamed.ItemPath[1].PositionNumber = 3;
        renamed.ItemPath[1].TypeIdentifier = "type-c";
        Assert.False(QualificationEvidence.SameIndexedDeviceItemPath(original, renamed));
    }

    [Fact]
    public void MasterPlcCompile_UsesSeparateProvenAncestorAndReprovesAfterCommit()
    {
        Assert.NotNull(typeof(IoSystemQualificationResultInfo).GetProperty("CompileTarget"));
        Assert.NotNull(typeof(IoSystemQualificationResultInfo).GetProperty("CompileTargetProof"));
        var selection = ExtractMethodBody(Source, "RequireMasterPlcCompilerTarget");
        Ordered(selection, "owner.Ancestors", "SelectMasterPlcAncestor", "ResolveQualificationDeviceItem");
        Assert.Contains("GetService<SoftwareContainer>()", selection);
        Assert.Contains("GetService<ICompilable>()", selection);

        var baseline = ExtractMethodBody(Source, "CompileBaseline");
        Ordered(baseline, "RequireExactOwningDeviceItem(", "RequireMasterPlcCompilerTarget(",
            "ReadFiveAttributeSnapshot(", "RequireSameMasterPlcCompilerTarget(", "CompileHardware(");
        Assert.DoesNotContain("CompileHardware(owner.Item", baseline);

        var mutation = ExtractMethodBody(Source, "SetAndCompile");
        Ordered(mutation, "portal.ExclusiveAccess(", "RequireMasterPlcCompilerTarget(",
            "exclusive.Transaction(project,", "ApplySingleField(", "transaction.CommitOnDispose();",
            "RequireSameMasterPlcCompilerTarget(", "CompileHardware(");
        Assert.DoesNotContain("CompileHardware(owner.Item", mutation);
    }

    [Fact]
    public void Dispatch_ValidatesBeforeSessionAccess_AndRemainsProtected()
    {
        var program = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Program.cs"));
        Assert.Contains("\"probe_io_system_qualification\" => ProbeIoSystemQualification(request)", program);
        var body = ExtractMethodBody(program, "ProbeIoSystemQualification");
        Ordered(body, "IoSystemQualificationProbeValidator.Validate(request)", "WithSession(", "ValidateExpectedAfterProjectResolution(", "IoSystemQualificationProbeService.");
        Assert.Contains("catch (Exception)", body);
        Assert.DoesNotContain("exception.Message", body);
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability("probe_io_system_qualification"));
    }

    [Fact]
    public void OwnerInspection_CannotCompileOrMutate()
    {
        var program = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Program.cs"));
        Assert.Contains("\"inspectOwner\" => IoSystemQualificationProbeService.InspectOwner(session.TiaPortal, session.Project, probe)", program);
        Assert.Contains("InspectOwner(TiaPortal portal, Project project, IoSystemQualificationProbeInfo request)", Source);
        var body = ExtractMethodBody(Source, "InspectOwner");
        Ordered(body, "using var exclusive = portal.ExclusiveAccess();", "RequireExactIoSystem(",
            "RequireExactOwningDeviceItem(", "return result;");
        Assert.DoesNotContain("CompileHardware", body);
        Assert.DoesNotContain("compiler.Compile()", body);
        Assert.DoesNotContain("SetAttribute", body);
        Assert.DoesNotContain("Transaction(", body);
        var owner = ExtractMethodBody(Source, "RequireExactOwningDeviceItem");
        Assert.Contains("IoSystemQualificationEvidence.InspectOwner", owner);
        Assert.Contains("diagnostic.Reason != \"verified\"", owner);
        Assert.DoesNotContain("First", owner);
        Assert.DoesNotContain("ICompilable", owner);
    }

    [Fact]
    public void OwnerInspection_ReadOnlyBaselineReadinessHasTypedAttributesAndCompileServiceProof()
    {
        var body = ExtractMethodBody(Source, "InspectOwner");
        Ordered(body, "using var exclusive = portal.ExclusiveAccess();", "RequireExactIoSystem(",
            "RequireExactOwningDeviceItem(", "ReadAffectedPnDeviceNames(project, target)",
            "diagnostic.Stage = \"attributeSnapshot\"", "diagnostic.Reason = \"attribute_snapshot_unverified\"",
            "result.Before = ReadFiveAttributeSnapshot(target)",
            "diagnostic.Stage = \"compileService\"", "diagnostic.Reason = \"compile_service_unverified\"",
            "GetService<ICompilable>()", "result.HardwareCompileServiceAvailable = compiler is not null",
            "IoSystemQualificationEvidence.FitsResultBudget(result)", "return result;");
        Assert.NotNull(typeof(IoSystemQualificationResultInfo).GetProperty("HardwareCompileServiceAvailable"));
        Assert.Contains("catch (Exception)", body);
        Assert.DoesNotContain("exception.Message", body);
        Assert.DoesNotContain("CompileHardware", body);
        Assert.DoesNotContain("ApplySingleField", body);
        Assert.DoesNotContain("Transaction(", body);
        Assert.DoesNotContain("project.Save(", body);
    }

    [Fact]
    public void OwnerInspection_MissingMasterTargetPreservesBoundedReadOnlyEvidence()
    {
        var body = ExtractMethodBody(Source, "InspectOwner");
        Ordered(body, "RequireExactOwningDeviceItem(project, target, diagnostic)",
            "result.BeforePnDeviceNames = ReadAffectedPnDeviceNames(project, target)",
            "result.Before = ReadFiveAttributeSnapshot(target)",
            "((IEngineeringServiceProvider)owner.Item).GetService<ICompilable>()",
            "diagnostic.Stage = \"masterPlcTarget\"",
            "diagnostic.Reason = \"master_plc_target_unverified\"",
            "RequireMasterPlcCompilerTarget(project, owner)",
            "IoSystemQualificationEvidence.FitsResultBudget(result)", "return result;");
        Assert.Contains("result.HardwareCompileServiceAvailable = compiler is not null", body);
        Assert.Contains("result.CompileTarget = null", body);
        Assert.Contains("result.OriginalCompileTarget = null", body);
        Assert.Contains("Do not compile or mutate", body);
        Assert.DoesNotContain("CompileHardware", body);
        Assert.DoesNotContain("SetAttribute", body);
        Assert.DoesNotContain("OwnerIdentityVerified = false", body);
        var baseline = ExtractMethodBody(Source, "CompileBaseline");
        var mutation = ExtractMethodBody(Source, "SetAndCompile");
        Assert.Contains("CompileHardware(verified.Item, result)", baseline);
        Ordered(mutation, "RequireExactOwningDeviceItem(project, target)", "RequireMasterPlcCompilerTarget(project, preEditOwner)",
            "ApplySingleField(target, request)");
    }

    [Fact]
    public void OwnerInspection_ReportsBoundedCompileCandidatesFromVerifiedPathWithoutCompiling()
    {
        var property = typeof(IoSystemQualificationResultInfo).GetProperty("CompileScopeCandidates");
        Assert.NotNull(property);
        Assert.Equal("List`1", property.PropertyType.Name);
        var candidateType = Assert.Single(property.PropertyType.GetGenericArguments());
        Assert.Equal("IoSystemQualificationCompileScopeCandidateInfo", candidateType.Name);
        Assert.NotNull(candidateType.GetProperty("Kind"));
        Assert.NotNull(candidateType.GetProperty("AncestorPathDepth"));
        Assert.NotNull(candidateType.GetProperty("Status"));
        Assert.NotNull(candidateType.GetProperty("PlcSoftwareCountStatus"));

        var inspect = ExtractMethodBody(Source, "InspectOwner");
        Ordered(inspect, "RequireExactOwningDeviceItem(project, target, diagnostic)",
            "ReadCompileScopeCandidates(project, owner)", "IoSystemQualificationEvidence.FitsResultBudget(result)");
        var candidates = ExtractMethodBody(Source, "ReadCompileScopeCandidates");
        Assert.Contains("owner.Selector.ItemPath", candidates);
        Assert.Contains("owner.Device", candidates);
        Assert.Contains("CountPlcSoftwareInDevice(owner.Device)", candidates);
        Assert.Contains("ResolveQualificationDeviceItem", candidates);
        Assert.Contains("GetService<ICompilable>()", candidates);
        Assert.Contains("unverified", candidates);
        Assert.Contains("absent", candidates);
        Assert.Contains("available", candidates);
        Assert.DoesNotContain(".Compile(", candidates);
        Assert.DoesNotContain("SetAttribute", candidates);
        Assert.DoesNotContain("Transaction(", candidates);
        var plcCount = ExtractMethodBody(Source, "CountPlcSoftwareInDevice");
        Assert.Contains("GetService<SoftwareContainer>()", Source);
        Assert.Contains("unverified", plcCount);
        Assert.Contains("multiple", plcCount);

        var baseline = ExtractMethodBody(Source, "CompileBaseline");
        var mutation = ExtractMethodBody(Source, "SetAndCompile");
        Assert.Contains("CompileHardware(verified.Item, result)", baseline);
        Assert.Contains("CompileHardware(verified.Item, result)", mutation);
    }

    [Fact]
    public void OwnerInspection_ReturnsCompleteBoundedReadOnlyPnSnapshot()
    {
        var body = ExtractMethodBody(Source, "InspectOwner");
        Ordered(body, "using var exclusive = portal.ExclusiveAccess();", "RequireExactIoSystem(",
            "RequireExactOwningDeviceItem(", "result.BeforePnDeviceNames = ReadAffectedPnDeviceNames(project, target)",
            "result.PnDeviceNameEvidenceScope = GetPnDeviceNameEvidenceScope(target)",
            "result.BeforePnDeviceNames.Any(node => !node.Available)",
            "IoSystemQualificationEvidence.FitsResultBudget(result)", "return result;");
        Assert.DoesNotContain("result.AfterPnDeviceNames =", body);
        Assert.DoesNotContain("ApplySingleField", body);
        Assert.DoesNotContain("CompileHardware", body);
    }

    [Fact]
    public void OwnerInspection_SnapshotFailureCannotRetainVerifiedDiagnostic()
    {
        var body = ExtractMethodBody(Source, "InspectOwner");
        Ordered(body, "RequireExactOwningDeviceItem(project, target, diagnostic)",
            "diagnostic.Stage = \"pnSnapshot\"", "diagnostic.Reason = \"snapshot_unverified\"",
            "ReadAffectedPnDeviceNames(project, target)",
            "diagnostic.Stage = \"verification\"", "diagnostic.Reason = \"verified\"",
            "IoSystemQualificationEvidence.FitsResultBudget(result)", "return result;");
        Assert.Contains("OwnerDiagnostics = diagnostic", body);
        Assert.DoesNotContain("OwnerIdentityVerified = true", body);
    }

    [Fact]
    public void Baseline_HoldsExclusiveAccessThroughExactOwnerProofAndCompile()
    {
        var program = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Program.cs"));
        var dispatch = ExtractMethodBody(program, "ProbeIoSystemQualification");
        Assert.Contains("\"compileBaseline\" => IoSystemQualificationProbeService.CompileBaseline(session.TiaPortal, session.Project, probe)", dispatch);
        Assert.Contains("CompileBaseline(TiaPortal portal, Project project, IoSystemQualificationProbeInfo request)", Source);
        var body = ExtractMethodBody(Source, "CompileBaseline");
        Ordered(body, "using var exclusive = portal.ExclusiveAccess();", "RequireExactIoSystem(",
            "RequireExactOwningDeviceItem(", "ReadFiveAttributeSnapshot(", "CompileHardware(", "return result;");
        Assert.DoesNotContain("ApplySingleField", body);
        Assert.DoesNotContain("Transaction(", body);
        var compile = ExtractMethodBody(Source, "CompileHardware");
        Assert.Contains("GetService<ICompilable>()", Source);
        Assert.Contains("compiler.Compile()", compile);
        Assert.Contains("compiler is null", Source);
    }

    [Fact]
    public void Mutation_CommitsBeforeFreshReadAndCompile()
    {
        var body = ExtractMethodBody(Source, "SetAndCompile");
        Ordered(body, "portal.ExclusiveAccess(", "RequireExactIoSystem(",
            "RequireExactOwningDeviceItem(", "RequireMasterPlcCompilerTarget(",
            "ReadFiveAttributeSnapshot(", "RequireExpectedValueAndWritableMetadata(",
            "exclusive.Transaction(project,",
            "ApplySingleField(", "transaction.CommitOnDispose();", "ReadAppliedStateAndNewSelector(", "CompileHardware(");
        Assert.DoesNotContain("project.Save(", Source);
        Assert.DoesNotContain("PlcSoftware", body);
        Assert.DoesNotContain("Download", Source);
        Assert.Contains("MutationCommitted = true", body);
        var post = ExtractMethodBody(Source, "ReadAppliedStateAndNewSelector");
        Assert.Contains("RequireExactIoSystem(", post);
    }

    [Fact]
    public void Mutation_PostCommitFailureCannotReportPreEditOwnerAsCurrent()
    {
        var body = ExtractMethodBody(Source, "SetAndCompile");
        Ordered(body, "transaction.CommitOnDispose();", "result.MutationCommitted = true;",
            "result.OwnerTarget = null;", "result.OwnerIdentityVerified = false;",
            "result.OwnerMatchCount = 0;", "result.CompileTarget = null;",
            "result.CompileTargetProof = null;", "ReadAppliedStateAndNewSelector(project, request)",
            "RequireSameMasterPlcCompilerTarget(project, applied, preEditOwner, preEditCompiler)",
            "result.OwnerTarget = verified.Owner.Selector;",
            "result.OwnerIdentityVerified = true;", "result.OwnerMatchCount = 1;",
            "SetCompileTargetEvidence(result, preEditCompiler, verified, true)", "CompileHardware(verified.Item, result)");
        Assert.Contains("SetCompileTargetEvidence(result, preEditCompiler, preEditCompiler, false)", body);
        Assert.DoesNotContain("result.OriginalCompileTarget = null", body);
        Assert.Contains("result.CompileState = \"postCommitFailure\"", body);
    }

    [Fact]
    public void Mutation_CapturesCompleteAffectedPnNamesOnBothSidesOfCommit()
    {
        var body = ExtractMethodBody(Source, "SetAndCompile");
        Ordered(body, "result.Before = ReadFiveAttributeSnapshot(target)",
            "result.BeforePnDeviceNames = ReadAffectedPnDeviceNames(project, target)",
            "result.BeforePnDeviceNames.Any(node => !node.Available)",
            "ApplySingleField(target, request)", "transaction.CommitOnDispose();",
            "ReadAppliedStateAndNewSelector(project, request)",
            "result.AfterPnDeviceNames = ReadAffectedPnDeviceNames(project, applied)",
            "result.AfterPnDeviceNames.Any(node => !node.Available)",
            "SamePnNodeIdentities(result.BeforePnDeviceNames, result.AfterPnDeviceNames)",
            "CompileHardware(verified.Item, result)");
        Assert.Contains("result.MutationCommitted = true", body);
        Assert.Contains("result.CompileState = \"postCommitFailure\"", body);
        Assert.DoesNotContain("project.Save(", body);
    }

    [Fact]
    public void AffectedPnNameCapture_UsesExactLinksAndBoundedTypedEvidence()
    {
        var capture = ExtractMethodBody(Source, "ReadAffectedPnDeviceNames");
        Assert.Contains("ProjectDeviceEnumerator.EnumerateWithLocations(project)", capture);
        Assert.Contains("IoSystemQualificationEvidence.ClassifyPnAssociation", Source);
        Assert.Contains("IoControllers", Source);
        Assert.Contains("IoConnectors", Source);
        Assert.Contains("ConnectedToIoSystem", Source);
        Assert.Contains("networkInterface.Nodes", Source);
        Assert.Contains("node.NodeId", Source);
        Assert.Contains("PnDeviceName", capture);
        Assert.Contains("ValidatePnDeviceNameSnapshot", capture);
        var dto = typeof(IoSystemQualificationResultInfo);
        Assert.Equal("IoSystemQualificationPnDeviceNameInfo", dto.GetProperty("BeforePnDeviceNames")?.PropertyType.GenericTypeArguments.Single().Name);
        Assert.Equal("IoSystemQualificationPnDeviceNameInfo", dto.GetProperty("AfterPnDeviceNames")?.PropertyType.GenericTypeArguments.Single().Name);
    }

    [Fact]
    public void LinkedPnInterface_WithNoNodesCannotBeHiddenByOtherLinkedNodes()
    {
        var collect = ExtractMethodBody(Source, "CollectAffectedPnDeviceNames");
        Ordered(collect, "if (association is not null)", "var linkedNodeCount = 0",
            "foreach (Node node in networkInterface.Nodes)", "linkedNodeCount++",
            "RequireLinkedPnNodes(linkedNodeCount)");
        var gate = typeof(QualificationEvidence).GetMethod("RequireLinkedPnNodes",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        Assert.NotNull(gate);
        var failure = Assert.Throws<System.Reflection.TargetInvocationException>(
            () => gate!.Invoke(null, new object[] { 0 }));
        Assert.IsType<InvalidOperationException>(failure.InnerException);
        gate!.Invoke(null, new object[] { 1 });
    }

    [Fact]
    public void ExpectedValueAndMetadata_MustMatchExactly()
    {
        var observation = new IoSystemQualificationAttributeInfo
        {
            Name = "Number", Available = true, Writable = true,
            SupportedTypes = new() { "System.Int32" },
            Value = new() { Kind = "integer", IntegerValue = 2 }
        };
        var request = new IoSystemQualificationProbeInfo
        {
            AttributeName = "Number", ExpectedValue = new() { Kind = "integer", IntegerValue = 2 },
            DesiredValue = new() { Kind = "integer", IntegerValue = 3 }
        };
        Assert.Null(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.Writable = false;
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.Writable = true;
        observation.Value.IntegerValue = 9;
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.Value.IntegerValue = 2;
        observation.SupportedTypes = new() { "System.String" };
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.SupportedTypes = new() { "System.Int32" };
        observation.Available = false;
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
    }

    [Fact]
    public void EvidenceBounds_PreserveCommittedOutcome_AndReportOmissions()
    {
        var result = new IoSystemQualificationResultInfo { MutationCommitted = true, CompileState = "Error" };
        for (var i = 0; i < 70; i++)
            TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.AddMessage(result, new string('x', 5000));
        Assert.Equal(32, result.Messages.Count);
        Assert.Equal(38, result.OmittedMessageCount);
        Assert.All(result.Messages, message => Assert.True(message.Length <= 512));
        var serialized = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SerializeBounded(result);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(serialized) <= 65536);
        result.Before.Add(new() { Value = new() { Kind = "string", StringValue = new string('x', 100000) } });
        serialized = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SerializeBounded(result);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(serialized) <= 65536);
        Assert.Contains("\"mutationCommitted\":true", serialized);
        Assert.Contains("\"evidenceOmitted\":true", serialized);
    }
    [Fact]
    public void CompilerEvidence_RedactsPathsAndCredentials_AndKeepsUsefulText()
    {
        var result = new IoSystemQualificationResultInfo();
        TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.AddMessage(result,
            @"Invalid station at C:\private\fixture.ap21 password=secret123 token: abcdef /home/private/project");
        var message = Assert.Single(result.Messages);
        Assert.Contains("Invalid station", message);
        Assert.DoesNotContain("fixture.ap21", message);
        Assert.DoesNotContain("secret123", message);
        Assert.DoesNotContain("abcdef", message);
        Assert.DoesNotContain("/home/private", message);
    }

    [Fact]
    public void ResultBudget_TrimsMessagesBeforeAppliedState()
    {
        var result = new IoSystemQualificationResultInfo { MutationCommitted = true };
        result.After.Add(new() { Name = "Name", Value = new() { Kind = "string", StringValue = new string('a', 54000) } });
        for (var i = 0; i < 32; i++)
            TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.AddMessage(result, new string('x', 512));
        var json = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SerializeBounded(result);
        Assert.Contains(new string('a', 54000), json);
        Assert.Contains("\"evidenceOmitted\":true", json);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= 65536);
    }

    [Fact]
    public void PnAssociation_UsesSiemensObjectEqualityAndRejectsAmbiguousLinks()
    {
        var target = new SystemProxy(7);
        Assert.Equal("controller", QualificationEvidence.ClassifyPnAssociation(
            new[] { new SystemProxy(7) }, Array.Empty<SystemProxy>(), target));
        Assert.Equal("connector", QualificationEvidence.ClassifyPnAssociation(
            Array.Empty<SystemProxy>(), new[] { new SystemProxy(7) }, target));
        Assert.Null(QualificationEvidence.ClassifyPnAssociation(
            new[] { new SystemProxy(8) }, new[] { new SystemProxy(9) }, target));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ClassifyPnAssociation(
            new[] { new SystemProxy(7), new SystemProxy(7) }, Array.Empty<SystemProxy>(), target));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ClassifyPnAssociation(
            new[] { new SystemProxy(7) }, new[] { new SystemProxy(7) }, target));
        var error = Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ClassifyPnAssociation(
            new[] { new ThrowingProxy() }, Array.Empty<ThrowingProxy>(), new ThrowingProxy()));
        Assert.DoesNotContain("private equality detail", error.Message);
    }

    [Fact]
    public void PnNameObservation_DistinguishesAbsentMetadataFromUnreadableValue()
    {
        var missing = QualificationEvidence.ObservePnDeviceName(0, false,
            () => throw new InvalidOperationException("must not read missing attribute"));
        Assert.False(missing.Available);
        Assert.Null(missing.Value);
        var value = QualificationEvidence.ObservePnDeviceName(1, true, () => "pn-name");
        Assert.True(value.Available);
        Assert.Equal("pn-name", value.Value);
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ObservePnDeviceName(2, true, () => "duplicate"));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ObservePnDeviceName(1, false, () => "unsupported"));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ObservePnDeviceName(1, true, () => null));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ObservePnDeviceName(1, true,
            () => throw new InvalidOperationException("private getter detail")));
    }

    [Fact]
    public void PnSnapshot_RequiresUniqueCompleteBoundedNodeIdentity()
    {
        var first = PnNode("node-0");
        QualificationEvidence.ValidatePnDeviceNameSnapshot(new[] { first }, true);
        QualificationEvidence.ValidatePnDeviceNameSnapshot(Array.Empty<IoSystemQualificationPnDeviceNameInfo>(), false);
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(
            Array.Empty<IoSystemQualificationPnDeviceNameInfo>(), true));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(new[] { first }, false));
        var duplicate = PnNode("node-0");
        duplicate.AssociationKind = "connector";
        duplicate.DeviceName = "renamed-device";
        duplicate.ItemPath[0].Name = "renamed-item";
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(new[] { first, duplicate }, true));
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(
            Enumerable.Range(0, 129).Select(i => PnNode($"node-{i}")).ToArray(), true));
        var unreadable = PnNode("node-1");
        unreadable.Available = false;
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(new[] { unreadable }, true));
        var oversized = PnNode("node-2");
        oversized.Value = new string('x', 17000);
        Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(new[] { oversized }, true));
        var privateIdentity = PnNode("private-node");
        privateIdentity.ItemPath[0].Index = -1;
        var error = Assert.Throws<InvalidOperationException>(() => QualificationEvidence.ValidatePnDeviceNameSnapshot(new[] { privateIdentity }, true));
        Assert.DoesNotContain("private-node", error.Message);
    }

    [Fact]
    public void PnSnapshot_IdentityDriftFailsBeforeCompileButDisplayNamesMayChange()
    {
        var before = PnNode("node-0");
        var after = PnNode("node-0");
        after.DeviceName = "renamed-device";
        after.ItemPath[0].Name = "renamed-item";
        after.Value = "renamed-pn-name";
        Assert.True(QualificationEvidence.SamePnNodeIdentities(new[] { before }, new[] { after }));
        after.AssociationKind = "connector";
        Assert.False(QualificationEvidence.SamePnNodeIdentities(new[] { before }, new[] { after }));
        after.AssociationKind = "controller";
        after.ItemPath[0].Index = 1;
        Assert.False(QualificationEvidence.SamePnNodeIdentities(new[] { before }, new[] { after }));
        after.ItemPath[0].Index = 0;
        after.NodeId = "other-node";
        Assert.False(QualificationEvidence.SamePnNodeIdentities(new[] { before }, new[] { after }));
        Assert.False(QualificationEvidence.SamePnNodeIdentities(new[] { before }, Array.Empty<IoSystemQualificationPnDeviceNameInfo>()));
    }

    [Fact]
    public void PnSnapshot_ResultBudgetFailsBeforeQualificationEvidenceIsDropped()
    {
        var result = new IoSystemQualificationResultInfo();
        result.BeforePnDeviceNames.Add(PnNode("node-0"));
        Assert.True(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.FitsResultBudget(result));
        result.Before.Add(new() { Name = "Name", Value = new() { Kind = "string", StringValue = new string('x', 70000) } });
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.FitsResultBudget(result));
    }

    private static IoSystemQualificationPnDeviceNameInfo PnNode(string nodeId)
        => new()
        {
            DeviceLocator = "devices/0", DeviceName = "synthetic-device", NodeId = nodeId,
            ItemPath = new() { new() { Index = 0, Name = "synthetic-item", PositionNumber = 0, TypeIdentifier = null! } },
            AssociationKind = "controller", Available = true, Value = "synthetic-pn-name"
        };

    [Theory]
    [InlineData("inspectOwner", "target_not_found")]
    [InlineData("compileBaseline", "worker_operation_failed")]
    [InlineData("setAndCompile", "worker_operation_failed")]
    public void ConvertedSessionFailure_IsNormalizedWithoutRawDiagnostics(string mode, string category)
    {
        var identity = new WorkerSessionIdentity();
        var response = new WorkerResponse
        {
            Success = false, FailureCategory = category,
            Error = @"C:\private\fixture.ap21 token=private-value " + new string('x', 100000),
            Warnings = new() { "raw private warning" }, Payload = "raw private payload",
            ResolvedProjectPath = "private-protocol-identity", SessionIdentity = identity
        };
        var normalized = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.NormalizeSessionResponse(response, mode);
        Assert.False(normalized.Success);
        Assert.Equal(category, normalized.FailureCategory);
        Assert.NotSame(response, normalized);
        Assert.Null(normalized.Payload);
        Assert.Null(normalized.Warnings);
        Assert.NotNull(normalized.Error);
        Assert.True(normalized.Error.Length <= 512);
        Assert.DoesNotContain("private", normalized.Error);
        Assert.Contains("inspect current state", normalized.Error);
        Assert.Contains("retry", normalized.Error);
        Assert.Equal(response.ResolvedProjectPath, normalized.ResolvedProjectPath);
        Assert.Same(identity, normalized.SessionIdentity);
        if (mode == "setAndCompile") Assert.Contains("may have committed", normalized.Error);
        else Assert.DoesNotContain("may have committed", normalized.Error);
    }

    [Fact]
    public void SuccessfulSessionResponse_PreservesTypedCommittedEvidence()
    {
        var response = new WorkerResponse { Success = true, Payload = "typed-committed-state" };
        Assert.Same(response, TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.NormalizeSessionResponse(response, "setAndCompile"));
    }

    [Fact]
    public void QualificationBoundary_NormalizesReturnedSessionFailures()
    {
        var program = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Program.cs"));
        var body = ExtractMethodBody(program, "ProbeIoSystemQualification");
        Assert.DoesNotContain("return WithSession(", body);
        Ordered(body, "var response = WithSession(", "IoSystemQualificationProbeService.",
            "return IoSystemQualificationEvidence.NormalizeSessionResponse(response, request.IoSystemQualification!.Mode)");
    }
    [Theory]
    [InlineData(0, "no_matches")]
    [InlineData(2, "multiple_matches")]
    public void OwnerDiagnostics_CountMatchesBeforeReadingPaths(int count, string reason)
    {
        var pathCalls = 0;
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => { for (var i = 0; i < count; i++) matches.Add(i); },
            _ => { pathCalls++; return new(); }, _ => { verifyCalls++; return true; });
        Assert.Equal("matching", diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(count, diagnostic.MatchCount);
        Assert.True(diagnostic.TraversalCompleted);
        Assert.Equal(0, pathCalls);
        Assert.Equal(0, verifyCalls);
    }

    [Fact]
    public void OwnerDiagnostics_IncompleteMatchedPathCannotReachSelectorVerification()
    {
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("synthetic-device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "synthetic-item", TypeIdentifier = "", PositionNumber = -1 }
            }), _ => { verifyCalls++; return true; });
        Assert.Equal("pathEvidence", diagnostic.Stage);
        Assert.Equal("incomplete_path", diagnostic.Reason);
        Assert.Equal(1, diagnostic.MatchCount);
        Assert.True(diagnostic.TraversalCompleted);
        Assert.Equal(1, diagnostic.Path!.Depth);
        Assert.Equal(1, diagnostic.Path.BlankTypeIdentifierCount);
        Assert.Equal(1, diagnostic.Path.NegativePositionCount);
        Assert.Equal(0, verifyCalls);
        var serialized = System.Text.Json.JsonSerializer.Serialize(diagnostic);
        Assert.DoesNotContain("synthetic", serialized);
        Assert.True(serialized.Length < 1024);
    }

    [Fact]
    public void OwnerDiagnostics_TraversalExceptionRetainsOnlyClosedReasonAndPartialCount()
    {
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => { matches.Add(1); throw new ArgumentException("private exception text"); },
            _ => throw new InvalidOperationException("Path must not be read"),
            _ => { verifyCalls++; return true; });
        Assert.Equal("traversal", diagnostic.Stage);
        Assert.Equal("traversal_failed", diagnostic.Reason);
        Assert.False(diagnostic.TraversalCompleted);
        Assert.Equal(1, diagnostic.MatchCount);
        Assert.Null(diagnostic.Path);
        Assert.Equal(0, verifyCalls);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(diagnostic));
    }

    [Theory]
    [InlineData(true, "verified")]
    [InlineData(false, "identity_unverified")]
    public void OwnerDiagnostics_CompletePathStillRequiresIdentityProof(bool identityMatches, string reason)
    {
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1), _ => new() { Depth = 1 },
            _ => { verifyCalls++; return identityMatches; });
        Assert.Equal("verification", diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(1, verifyCalls);
    }

    [Theory]
    [InlineData("", 1, "verified")]
    [InlineData("observed-type", 0, "verified")]
    public void OwnerDiagnostics_ObservedTypeEvidenceCanReachExactIdentityProof(string type, int blankCount, string reason)
    {
        var calls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "root", PositionNumber = 0, TypeIdentifier = type },
                new DeviceItemPathSegmentInfo { Index = 0, Name = "owner", PositionNumber = 1, TypeIdentifier = "observed-type" }
            }),
            _ => { calls++; return true; });
        Assert.Equal("verification", diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(blankCount, diagnostic.Path!.BlankTypeIdentifierCount);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    public void OwnerDiagnostics_WhitespaceTypeCannotReachIdentityProof(string type)
    {
        var calls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "owner", PositionNumber = 0, TypeIdentifier = type }
            }),
            _ => { calls++; return true; });
        Assert.Equal("pathEvidence", diagnostic.Stage);
        Assert.Equal("incomplete_path", diagnostic.Reason);
        Assert.NotNull(diagnostic.Path);
        Assert.Equal(1, diagnostic.Path.BlankTypeIdentifierCount);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(true, "verified")]
    [InlineData(false, "identity_unverified")]
    public void OwnerDiagnostics_ObservedNullTypeRequiresFreshIdentityProof(bool identityMatches, string reason)
    {
        var calls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "root", PositionNumber = 0, TypeIdentifier = null! },
                new DeviceItemPathSegmentInfo { Index = 0, Name = "owner", PositionNumber = 1, TypeIdentifier = "observed-type" }
            }),
            _ => { calls++; return identityMatches; });
        Assert.True(diagnostic.TraversalCompleted);
        Assert.Equal(1, diagnostic.MatchCount);
        Assert.Equal(2, diagnostic.Path!.Depth);
        Assert.Equal(1, diagnostic.Path.NullTypeIdentifierCount);
        Assert.Equal(0, diagnostic.Path.WhitespaceTypeIdentifierCount);
        Assert.Equal("verification", diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void OwnerDiagnostics_ObservedNullTypeWithThrowingIdentityProofFailsClosed()
    {
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "root", PositionNumber = 0, TypeIdentifier = null! },
                new DeviceItemPathSegmentInfo { Index = 0, Name = "owner", PositionNumber = 1, TypeIdentifier = "observed-type" }
            }),
            _ => throw new InvalidOperationException("private verification detail"));
        Assert.Equal("verification", diagnostic.Stage);
        Assert.Equal("verification_failed", diagnostic.Reason);
        Assert.Equal(1, diagnostic.Path!.NullTypeIdentifierCount);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void OwnerDiagnostics_MixedTypeClassesHaveBoundedAggregateCounts()
    {
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("private-device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "private-root", PositionNumber = 0, TypeIdentifier = null! },
                new DeviceItemPathSegmentInfo { Index = 0, Name = "private-middle", PositionNumber = 1, TypeIdentifier = "" },
                new DeviceItemPathSegmentInfo { Index = 0, Name = "private-owner", PositionNumber = 2, TypeIdentifier = " \t" },
                new DeviceItemPathSegmentInfo { Index = 0, Name = "private-leaf", PositionNumber = 3, TypeIdentifier = "observed-type" }
            }),
            _ => throw new InvalidOperationException("Identity proof must not run"));
        Assert.Equal("pathEvidence", diagnostic.Stage);
        Assert.Equal("incomplete_path", diagnostic.Reason);
        Assert.Equal(4, diagnostic.Path!.Depth);
        Assert.Equal(3, diagnostic.Path.BlankTypeIdentifierCount);
        var json = System.Text.Json.JsonSerializer.Serialize(diagnostic);
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var path = document.RootElement.GetProperty("Path");
        Assert.Equal(1, path.GetProperty("NullTypeIdentifierCount").GetInt32());
        Assert.Equal(1, path.GetProperty("EmptyTypeIdentifierCount").GetInt32());
        Assert.Equal(1, path.GetProperty("WhitespaceTypeIdentifierCount").GetInt32());
        Assert.DoesNotContain("private-", json);
        Assert.DoesNotContain("observed-type", json);
        Assert.True(json.Length < 1024);
    }

    [Theory]
    [InlineData("devices/0", "direct")]
    [InlineData("deviceGroups/0/devices/0", "grouped")]
    [InlineData("deviceGroups/0/groups/1/devices/0", "grouped")]
    [InlineData("unexpected-private-locator", "unknown")]
    public void OwnerDiagnostics_DeviceLocationIsAClosedCode(string locator, string expected)
    {
        var actual = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ClassifyDeviceLocation(locator);
        Assert.Equal(expected, actual);
        Assert.DoesNotContain("/", actual);
    }

    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { "other" }, 0)]
    [InlineData(new[] { "OWNER" }, 1)]
    [InlineData(new[] { "owner", "OWNER" }, 2)]
    public void OwnerDiagnostics_DirectDeviceLookupCountsOnlyExactNameMatches(string[] names, int expected)
    {
        Assert.Equal(expected, TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .CountDirectDeviceNameMatches(names, "owner"));
    }

    [Fact]
    public void OwnerDiagnostics_ResolutionObservationAloneDoesNotAuthorize()
    {
        var candidate = new EqualProxy();
        var diagnostic = new IoSystemQualificationOwnerDiagnosticInfo();
        TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.RecordOwnerResolution(candidate, null, diagnostic);
        Assert.Equal("unresolved", diagnostic.ResolverOutcome);
        Assert.Null(diagnostic.ResolvedObjectEqualsCandidate);

        TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.RecordOwnerResolution(candidate, new EqualProxy(), diagnostic);
        Assert.Equal("different_reference", diagnostic.ResolverOutcome);
        Assert.True(diagnostic.ResolvedObjectEqualsCandidate);
        Assert.False(diagnostic.Reason == "verified");

        TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.RecordOwnerResolution(candidate, candidate, diagnostic);
        Assert.Equal("same_reference", diagnostic.ResolverOutcome);
        Assert.Null(diagnostic.ResolvedObjectEqualsCandidate);
    }

    [Fact]
    public void OwnerDiagnostics_EqualProxyWithoutControllerLinkDoesNotPassOrRevealLocation()
    {
        var diagnostic = new IoSystemQualificationOwnerDiagnosticInfo();
        diagnostic.OwnerDeviceLocation = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .ClassifyDeviceLocation("deviceGroups/private-group/devices/private-device");
        diagnostic.DirectDeviceNameMatchCount = 0;
        var result = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<EqualProxy>(
            matches => matches.Add(new EqualProxy()), _ => new() { Depth = 1 },
            candidate =>
            {
                var resolved = new EqualProxy();
                TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
                    .RecordOwnerResolution(candidate, resolved, diagnostic);
                return TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
                    .VerifyResolvedOwner(resolved, candidate, "target", _ => Array.Empty<string>());
            }, diagnostic);
        Assert.Equal("identity_unverified", result.Reason);
        Assert.Equal("grouped", result.OwnerDeviceLocation);
        Assert.Equal(0, result.DirectDeviceNameMatchCount);
        Assert.Equal("different_reference", result.ResolverOutcome);
        Assert.True(result.ResolvedObjectEqualsCandidate);
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("private-group", json);
        Assert.DoesNotContain("private-device", json);
        Assert.True(json.Length < 1024);
    }

    [Fact]
    public void OwnerProof_RequiresResolverSuccessAndSiemensObjectEqualityBeforeReadingLinks()
    {
        var linkReads = 0;
        IEnumerable<string> ReadLinks(object _) { linkReads++; return new[] { "target" }; }
        var candidate = new EqualProxy();
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner<object, string>(null, candidate, "target", ReadLinks));
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner<object, string>(new object(), candidate, "target", ReadLinks));
        Assert.Equal(0, linkReads);
        Assert.True(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(new EqualProxy(), candidate, "target", _ =>
            {
                linkReads++;
                return new[] { "target" };
            }));
        Assert.Equal(1, linkReads);
    }

    [Fact]
    public void OwnerProof_RequiresExactlyOneFreshControllerLinkToExactTarget()
    {
        var candidate = new EqualProxy();
        var verified = new EqualProxy();
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, "target", _ => null));
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, "target", _ => Array.Empty<string>()));
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(candidate, candidate, "target", _ => Array.Empty<string>()));
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, "target", _ => new[] { "other" }));
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, "target", _ => new[] { "target", "target" }));
        Assert.True(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, "target", _ => new[] { "other", "target" }));
        Assert.True(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, new SystemProxy(7), _ => new[] { new SystemProxy(7) }));
        Assert.False(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
            .VerifyResolvedOwner(verified, candidate, new SystemProxy(7), _ => new[] { new SystemProxy(7), new SystemProxy(7) }));
    }

    [Fact]
    public void OwnerProof_ThrowingEqualityOrControllerReadFailsClosed()
    {
        var candidate = new EqualProxy();
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<EqualProxy>(
            matches => matches.Add(candidate), _ => new() { Depth = 1 },
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
                .VerifyResolvedOwner<object, string>(new ThrowingProxy(), candidate, "target", _ => new[] { "target" }));
        Assert.Equal("verification_failed", diagnostic.Reason);
        Assert.False(diagnostic.Reason == "verified");

        diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<EqualProxy>(
            matches => matches.Add(candidate), _ => new() { Depth = 1 },
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence
                .VerifyResolvedOwner(new EqualProxy(), candidate, "target", _ =>
                    throw new InvalidOperationException("private controller detail")));
        Assert.Equal("verification_failed", diagnostic.Reason);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(diagnostic));
    }

    [Fact]
    public void OwnerDiagnostics_ThrowingEqualityReturnsUnknownWithoutPrivateText()
    {
        var diagnostic = new IoSystemQualificationOwnerDiagnosticInfo();
        TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.RecordOwnerResolution(
            new EqualProxy(), new ThrowingProxy(), diagnostic);
        Assert.Equal("different_reference", diagnostic.ResolverOutcome);
        Assert.Null(diagnostic.ResolvedObjectEqualsCandidate);
        var json = System.Text.Json.JsonSerializer.Serialize(diagnostic);
        Assert.DoesNotContain("private equality detail", json);
        Assert.True(json.Length < 1024);
    }

    [Fact]
    public void OwnerInspection_RecordsResolutionAndRequiresFreshVerifiedControllerLink()
    {
        var owner = ExtractMethodBody(Source, "RequireExactOwningDeviceItem");
        Assert.Contains("ProjectDeviceEnumerator.EnumerateWithLocations(project)", owner);
        Assert.Contains("OwnerDeviceLocation", owner);
        Assert.Contains("DirectDeviceNameMatchCount", owner);
        Ordered(owner, "ResolveQualificationDeviceItem(project, selector)",
            "RecordOwnerResolution(candidate.Item, verified, diagnostic)",
            "VerifyResolvedOwner(verified, candidate.Item, (IoSystem)target.Value, ReadControllerIoSystems)",
            "owner = new Owner(verified, selector, candidate.Device, candidate.Ancestors)");
        var readLinks = ExtractMethodBody(Source, "ReadControllerIoSystems");
        Assert.Contains("GetService<NetworkInterface>()", readLinks);
        Assert.Contains("IoControllers", readLinks);
        Assert.Contains("IoSystem", readLinks);
    }

    private sealed class EqualProxy
    {
        public override bool Equals(object? obj) => obj is EqualProxy;
        public override int GetHashCode() => 0;
    }

    private sealed class ThrowingProxy
    {
        public override bool Equals(object? obj) => throw new InvalidOperationException("private equality detail");
        public override int GetHashCode() => 0;
    }

    private sealed class SystemProxy(int id)
    {
        public override bool Equals(object? obj) => obj is SystemProxy other && other.Id == Id;
        public override int GetHashCode() => Id;
        private int Id { get; } = id;
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("device", -1, 0)]
    [InlineData("device", 0, -1)]
    public void OwnerDiagnostics_IncompleteNameOrCoordinatesRejectBeforeIdentityProof(string deviceName, int index, int position)
    {
        var calls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath(deviceName, new[]
            {
                new DeviceItemPathSegmentInfo { Index = index, Name = "owner", PositionNumber = position, TypeIdentifier = "" }
            }),
            _ => { calls++; return true; });
        Assert.Equal("incomplete_path", diagnostic.Reason);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void OwnerSelector_UsesDirectObservedPathAndSiemensObjectIdentity()
    {
        var body = ExtractMethodBody(Source, "RequireExactOwningDeviceItem");
        Assert.DoesNotContain("NetworkSelectorFactory.DeviceItem", body);
        Assert.Contains("Kind = NetworkObjectKinds.DeviceItem", body);
        Assert.Contains("DeviceName = candidate.DeviceName", body);
        Assert.Contains("ItemPath = candidate.Path", body);
        Ordered(body, "NetworkObjectSelectorResolver.ResolveQualificationDeviceItem(project, selector)",
            "VerifyResolvedOwner(verified, candidate.Item, (IoSystem)target.Value, ReadControllerIoSystems)",
            "owner = new Owner(verified, selector, candidate.Device, candidate.Ancestors)");
        var proof = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Openness/IoSystemQualificationEvidence.cs"));
        var proofBody = ExtractMethodBody(proof, "VerifyResolvedOwner");
        Assert.Contains("object.Equals(verified, candidate)", proofBody);
        Assert.Contains("object.Equals(system, target)", proofBody);
        Assert.Contains("matchingLinks == 1", proofBody);
        var resolver = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Openness/NetworkObjectSelectorResolver.cs"));
        var qualification = ExtractMethodBody(resolver, "ResolveQualificationDeviceItem");
        Assert.Contains("MatchDeviceItem(project, target)", qualification);
        Assert.DoesNotContain("NetworkSelectorFactory", qualification);
        Assert.Contains("match.Item", qualification);
        Assert.Contains("string.Equals(typeIdentifier, requestedSegment.TypeIdentifier, StringComparison.Ordinal)",
            ExtractMethodBody(resolver, "MatchDeviceItem"));
        var apply = ExtractMethodBody(Source, "SetAndCompile");
        Ordered(apply, "preEditOwner = RequireExactOwningDeviceItem(project, target)",
            "preEditCompiler = RequireMasterPlcCompilerTarget(project, preEditOwner)",
            "ApplySingleField(target, request)", "transaction.CommitOnDispose();",
            "RequireSameMasterPlcCompilerTarget(project, applied, preEditOwner, preEditCompiler)",
            "CompileHardware(verified.Item, result)");
        Assert.DoesNotContain("ReferenceEquals(original, owner.Item)", apply);
        Assert.DoesNotContain("ResolveQualificationDeviceItem(project, originalOwner)", apply);
        var traversal = ExtractMethodBody(Source, "FindOwners");
        Assert.Contains("TypeIdentifier = item.TypeIdentifier", traversal);
        Assert.Contains("FindOwners(item.DeviceItems", traversal);
    }

    [Fact]
    public void OwnerInspection_CollectsBeforeSelectorBuildAndReturnsOnlyAggregateFailure()
    {
        var collect = ExtractMethodBody(Source, "FindOwners");
        Assert.DoesNotContain("NetworkSelectorFactory", collect);
        Assert.Contains("new OwnerCandidate(", collect);
        var inspect = ExtractMethodBody(Source, "InspectOwner");
        Assert.Contains("OwnerDiagnostics = diagnostic", inspect);
        Assert.Contains("((IEngineeringServiceProvider)owner.Item).GetService<ICompilable>()", inspect);
        Assert.DoesNotContain("CompileHardware", inspect);
        Assert.DoesNotContain("SetAttribute", inspect);
        var require = ExtractMethodBody(Source, "RequireExactOwningDeviceItem");
        Assert.Contains("diagnostic.Reason != " + '"' + "verified" + '"', require);
        Assert.Contains("throw Failure(", require);
        Assert.DoesNotContain("CompileHardware", require);
    }
    private static void Ordered(string source, params string[] values)
    {
        var prior = -1;
        foreach (var value in values)
        {
            var next = source.IndexOf(value, prior + 1, StringComparison.Ordinal);
            Assert.True(next > prior, $"Missing or out-of-order {value}");
            prior = next;
        }
    }

    internal static string ExtractMethodBody(string source, string name)
    {
        var match = Regex.Match(source, @"(?:public|private|internal)\s+static\s+[^\r\n]+\s+" + name + @"(?:<[^>]+>)?\s*\(");
        Assert.True(match.Success, $"Missing method {name}");
        var start = source.IndexOf('{', match.Index);
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException();
    }

    private static string Find(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(Path.Combine(dir.FullName, "TiaMcpServer.sln"))) return path;
        }
        throw new InvalidOperationException("Repository not found.");
    }
}
