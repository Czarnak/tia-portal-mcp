using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.Library;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.Library;
using TiaMcpServer.OpennessWorker.Openness.Library;
using TiaMcpServer.OpennessWorker.Worker;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed partial class LibraryTypeWriterTests
{
    // Record real linked worker results, then carry them through the existing IPC/domain/pipeline fixture.
    private WorkerResponse Execute(LibraryWriteRequestInfo item, bool plan = false, Func<long>? lossRevision = null)
        => Respond(() => WorkerExecution.Execute(() => new WorkerResponse { Success = true, Payload = plan
            ? WorkerJson.SerializePayload(LibraryWritePlanReader.Plan(portal, project, LibraryWriteMethods.PlanFor(item), item))
            : WorkerJson.SerializePayload(Write(item)) }, lossRevision ?? (() => 0), !plan));

    private static WorkerResponse Respond(Func<WorkerResponse> execute)
    {
        try { return execute(); }
        catch (WorkerOperationException error)
        {
            return new() { Success = false, FailureCategory = error.FailureCategory, Error = error.Message,
                EngineeringError = error.EngineeringError, LibraryWriteOutcome = error.LibraryWriteOutcome };
        }
    }

    private static LibraryWriteItem PublicItem(LibraryWriteRequestInfo item, string id)
    {
        var result = CanonicalJson.Deserialize<LibraryWriteItem>(WorkerJson.SerializePayload(item));
        result.OperationId = id;
        return result;
    }

    private LibraryWriteRequestInfo SaveLibrary()
    {
        portal.GlobalLibraries.Items.Add(new UserGlobalLibrary { Name = "Fixture", Path = new FileInfo(@"C:\Fixture\Library.al21"), SaveAction = () => { } });
        return new() { Operation = "save_global", Scope = "global", LibraryName = "Fixture" };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlannedCreateVersion_StaleHash_StopsBeforeEarlierMutation(bool dryRun)
    {
        var old = VersionImport(false);
        var item = Item("create_type_version"); item.ExpectedContentHash = "xml:sha256:" + new string('a', 64);
        var save = SaveLibrary();
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", Execute(save, plan: true), plan: true);
        f.PutResponse("b", Execute(item, plan: true), plan: true);
        var reply = await f.Run([PublicItem(save, "a"), PublicItem(item, "b")], dryRun, readWrite: true);
        var root = reply.StructuredContent!.Value;
        Assert.False(root.GetProperty("success").GetBoolean(), root.GetRawText());
        Assert.Equal("state_changed", root.GetProperty("error").GetProperty("category").GetString());
        Assert.All(f.LibraryRequests, request => Assert.EndsWith("_plan", request.Method));
        Assert.Equal(0, f.Prompts);
        Assert.Empty(old.TypeObject.Versions.CreateFromDocumentsCalls);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("fatal")]
    [InlineData("current-disposal")]
    public void PlannedCreateVersion_HashEvidenceFailure_PreservesLossPrecedence(string failure)
    {
        var old = VersionImport(false);
        var item = Item("create_type_version"); long revision = 0;
        source.ExportAction = (_, _) =>
        {
            if (failure == "current-disposal") revision++;
            throw failure == "fatal" ? new NonRecoverableException("lost") : new EngineeringNotSupportedException("unreadable");
        };
        source.ExportAsDocumentsAction = (_, _) => throw new EngineeringNotSupportedException("unreadable");
        var response = Execute(item, plan: true, () => revision);
        Assert.Equal(failure == "unreadable" ? "guard_blocked" : "portal_lost", response.FailureCategory);
        if (failure == "unreadable") Assert.Contains("library_state_unverifiable", response.Error);
        else Assert.Null(response.EngineeringError);
        Assert.Null(response.LibraryWriteOutcome);
        Assert.Empty(old.TypeObject.Versions.CreateFromDocumentsCalls);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task PlannedCreateThenRelease_ExistingType_MatchesResolvedProducer(bool dryRun, bool otherType)
    {
        var old = VersionImport(false);
        var create = Item("create_type_version");
        var release = Item("release_type_version"); release.VersionNumber = "2.0.0";
        if (otherType)
        {
            var other = project.ProjectLibrary!.TypeFolder.AddType("Other");
            other.AddVersion("1.0.0", true);
            release.TypeName = null; release.TypeGuid = other.Guid.ToString("D");
        }
        else { release.TypeName = null; release.TypeGuid = old.TypeObject.Guid.ToString("D"); }
        var save = SaveLibrary();
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", Execute(create, plan: true), plan: true);
        f.PutResponse("b", Execute(release, plan: true), plan: true);
        f.PutResponse("c", Execute(save, plan: true), plan: true);
        if (!dryRun && !otherType)
        {
            var created = new CodeBlockLibraryTypeVersion { TypeObject = old.TypeObject, Parent = old.TypeObject,
                Guid = Guid.NewGuid(), VersionNumber = new(2, 0, 0), State = LibraryTypeVersionState.InWork };
            old.TypeObject.Versions.CreateFromDocumentsAction = (_, _, _, _) =>
            { old.TypeObject.Versions.Items.Add(created); return new() { CreatedVersion = created }; };
            f.PutResponse("a", Execute(create));
            f.PutResponse("b", Execute(release));
            f.PutResponse("c", Execute(save));
        }
        var reply = await f.Run([PublicItem(create, "a"), PublicItem(release, "b"), PublicItem(save, "c")], dryRun, readWrite: true);
        var root = reply.StructuredContent!.Value;
        Assert.True(!otherType == root.GetProperty("success").GetBoolean(), root.GetRawText());
        if (otherType)
        {
            Assert.Equal("target_not_found", root.GetProperty("error").GetProperty("category").GetString());
            Assert.Equal(0, f.Prompts);
            Assert.All(f.LibraryRequests, request => Assert.EndsWith("_plan", request.Method));
        }
        else
        {
            Assert.Equal(dryRun ? 0 : 1, f.Prompts);
            Assert.Equal(1, root.GetProperty("effects")[1].GetProperty("effect").GetProperty("createdByItem").GetInt32());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlannedDependentRelease_StillChecksRequestedNumberAgainstReleasedVersions(bool dryRun)
    {
        VersionImport(false);
        var create = Item("create_type_version");
        var release = Item("release_type_version"); release.VersionNumber = "1.0.0";
        var save = SaveLibrary();
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", Execute(create, plan: true), plan: true);
        f.PutResponse("b", Execute(release, plan: true), plan: true);
        f.PutResponse("c", Execute(save, plan: true), plan: true);
        var root = (await f.Run([PublicItem(create, "a"), PublicItem(release, "b"), PublicItem(save, "c")], dryRun, readWrite: true)).StructuredContent!.Value;
        Assert.Equal(dryRun, root.GetProperty("success").GetBoolean());
        Assert.Contains("library_version_not_higher", root.GetRawText());
        Assert.Equal(0, f.Prompts);
        Assert.All(f.LibraryRequests, request => Assert.EndsWith("_plan", request.Method));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("ambiguous")]
    public async Task PlannedRelease_WithoutProducer_PreservesVersionResolutionFailure(string state)
    {
        var version = Version(state == "ambiguous");
        if (state == "ambiguous") version.TypeObject.Versions.Items.Add(new CodeBlockLibraryTypeVersion {
            TypeObject = version.TypeObject, Parent = version.TypeObject, State = LibraryTypeVersionState.InWork, Guid = Guid.NewGuid() });
        var item = Item("release_type_version");
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", Execute(item, plan: true), plan: true);
        var root = (await f.Run([PublicItem(item, "a")], dryRun: true)).StructuredContent!.Value;
        Assert.Equal(state == "missing" ? "target_not_found" : "target_ambiguous", root.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(version.ReleaseCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteSiblingVersionReceipt_RemainsPrecise(bool createSibling)
    {
        var old = VersionImport(false);
        var first = Item("create_type_version");
        var firstResponse = Execute(first);
        Assert.NotNull(firstResponse.Payload);
        var firstType = CanonicalJson.DeserializeWorkerPayload<LibraryWriteResultInfo>(firstResponse.Payload).Type!;
        LibraryWriteRequestInfo later;
        if (createSibling) later = Item("create_type_version");
        else
        {
            // Keep the successfully created sibling in the native collection while deleting the old version.
            old.TypeObject.Versions.Items.Add(new CodeBlockLibraryTypeVersion { TypeObject = old.TypeObject, Parent = old.TypeObject,
                Guid = Guid.Parse(firstType.VersionGuid!), VersionNumber = new(2, 0, 0), State = LibraryTypeVersionState.Committed });
            later = Select("delete_type_version", old);
        }
        var laterResponse = Execute(later);
        Assert.True(laterResponse.Success, laterResponse.Error);
        Assert.NotNull(laterResponse.Payload);
        var receipt = CanonicalJson.DeserializeWorkerPayload<LibraryWriteResultInfo>(laterResponse.Payload).Type!;
        Assert.False(createSibling ? receipt.ReplacedInWork : receipt.TypeDeleted);
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", firstResponse); f.PutResponse("b", laterResponse);
        var root = (await f.Run([PublicItem(first, "a"), PublicItem(later, "b")])).StructuredContent!.Value;
        Assert.Equal("trusted", root.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteNativeTypeReceipt_SupersedesEarlierTypeEvidence(bool replacement)
    {
        var version = Version(replacement);
        var first = Select("set_default_type_version", version);
        var firstResponse = Execute(first);
        Assert.True(firstResponse.Success, firstResponse.Error);
        LibraryWriteRequestInfo later;
        if (replacement)
        {
            later = Item("create_type_version"); later.ReplaceInWork = true;
            version.TypeObject.Versions.CreateFromDocumentsAction = (_, _, _, _) =>
            {
                var created = new CodeBlockLibraryTypeVersion { TypeObject = version.TypeObject, Parent = version.TypeObject,
                    Guid = Guid.NewGuid(), VersionNumber = new(2, 0, 0), ReadState = () => throw new EngineeringNotSupportedException("state read-back") };
                version.TypeObject.Versions.Items.Clear(); version.TypeObject.Versions.Items.Add(created);
                return new() { CreatedVersion = created };
            };
        }
        else
        {
            later = Select("delete_type_version", version);
            version.DeleteAction = () =>
            {
                version.TypeObject.Delete();
                project.ProjectLibrary!.TypeFolder.Types.EnumerationFailure = new EngineeringNotSupportedException("post-delete inventory");
            };
        }
        var failed = Execute(later);
        Assert.False(failed.Success);
        Assert.NotNull(failed.LibraryWriteOutcome!.Type!.VersionGuid);
        Assert.Null(replacement ? failed.LibraryWriteOutcome.Type.ReplacedInWork : failed.LibraryWriteOutcome.Type.TypeDeleted);
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", firstResponse); f.PutResponse("b", failed);
        var root = (await f.Run([PublicItem(first, "a"), PublicItem(later, "b")])).StructuredContent!.Value;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("superseded", root.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());
    }

    [Theory]
    [InlineData("type-names", "recoverable")]
    [InlineData("source-service", "recoverable")]
    [InlineData("released-number", "recoverable")]
    [InlineData("master-copy-names", "recoverable")]
    [InlineData("type-names", "fatal")]
    [InlineData("source-service", "fatal")]
    [InlineData("released-number", "fatal")]
    [InlineData("master-copy-names", "fatal")]
    [InlineData("type-names", "current-disposal")]
    [InlineData("source-service", "current-disposal")]
    [InlineData("released-number", "current-disposal")]
    [InlineData("master-copy-names", "current-disposal")]
    public async Task ApplyGuardEvidenceFailure_IsOutcomeFreeAndNeverMutates(string check, string failure)
    {
        var version = Version(check == "released-number");
        var first = Select("set_default_type_version", version);
        var firstResponse = Execute(first);
        Assert.True(firstResponse.Success);
        var item = Item("create_type");
        version.TypeObject.Name = "Other";
        long revision = 0;
        Exception Error() => failure == "fatal" ? new NonRecoverableException("lost") : new EngineeringTargetInvocationException("unreadable");
        if (check == "type-names") project.ProjectLibrary!.TypeFolder.Types.EnumerationFailure = Error();
        if (check == "source-service") source.LibraryTypeInstanceInfoFailure = Error();
        if (check == "released-number")
        {
            version.TypeObject.Name = "Motor";
            var previous = version.TypeObject.AddVersion("1.0.0");
            previous.ReadVersionNumber = () => throw Error();
            item = Item("release_type_version");
        }
        if (check == "master-copy-names")
        {
            item = new() { Operation = "create_master_copy", Scope = "project", PlcName = "PLC", PlcKind = "block", PlcObjectName = "Motor" };
            project.ProjectLibrary!.MasterCopyFolder.MasterCopies.EnumerationFailure = Error();
        }
        var response = Respond(() => WorkerExecution.Execute(() =>
        {
            try { return new WorkerResponse { Success = true, Payload = WorkerJson.SerializePayload(Write(item)) }; }
            catch { if (failure == "current-disposal") revision++; throw; }
        }, () => revision, true));
        Assert.Equal(failure == "recoverable" ? "guard_blocked" : "portal_lost", response.FailureCategory);
        Assert.Null(response.LibraryWriteOutcome);
        Assert.False(source.Deleted); Assert.Empty(portal.Transactions); Assert.Empty(version.ReleaseCalls);
        if (failure != "recoverable") { Assert.Null(response.EngineeringError); return; }
        Assert.Contains("library_state_unverifiable", response.Error);
        Assert.NotNull(response.EngineeringError);
        using var f = await LibraryGuardedWriteDomainTests.Fixture.Create();
        f.PutResponse("a", firstResponse); f.PutResponse("b", response);
        var root = (await f.Run([PublicItem(first, "a"), PublicItem(item, "b")])).StructuredContent!.Value;
        Assert.Equal("trusted", root.GetProperty("verification").GetProperty("operations")[0].GetProperty("check").GetString());
        Assert.Contains(WriteExecution.PartialWriteNotMutatedMessage, root.GetRawText());
    }
}
