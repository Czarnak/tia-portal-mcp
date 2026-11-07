using TiaMcpServer.Contracts.Library;
using TiaMcpServer.Library;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Library;

public sealed class LibraryVerificationPolicyTests
{
    [Fact]
    public async Task Verification_KeysFromResolvedIdentity()
    {
        var a = new LibraryWriteItem { OperationId = "a", Operation = "set_default_type_version", Scope = "project", TypeName = "Alias" };
        var b = new LibraryWriteItem { OperationId = "b", Operation = "delete_type", Scope = "project", TypeGuid = Guid.NewGuid().ToString() };
        var effect = Effect(scope: "project") with { Target = null, Type = new() { TypeGuid = b.TypeGuid, TypeName = "Actual" } };
        Assert.Equal(new[] { "superseded", "trusted" }, await Verify((a, effect), (b, effect)));
    }

    [Fact]
    public async Task Verification_AttemptedWithoutOutcome_SupersedesLibrary()
    {
        var a = Item("a"); a.LibraryName = "Fixture";
        var b = Item("b"); b.LibraryName = "Fixture";
        var verification = await new TrustedWriteVerifier<LibraryWriteItem>([a, b],
            new LibraryVerificationPolicy(new Dictionary<string, LibraryWriteEffect> { ["a"] = Effect() }))
            .VerifyAsync(StructuredOperationBatch.FromItems([Succeeded(a),
                LibraryWritePayloadContract.Project(b, WorkerCallResult.Fail("worker_operation_failed", "failed"))]));
        Assert.Equal("superseded", Assert.Single(verification.Operations).Check);
    }
    [Fact]
    public async Task CanonicalLibraryIdentityUnifiesNameAndPathAliases()
    {
        var first = Item("a"); first.LibraryName = "Fixture";
        var second = Item("b"); second.LibraryPath = @"c:\Libraries\sub\..\Fixture.al21";
        Assert.Equal(["superseded", "trusted"], await Verify((first, Effect()), (second, Effect(path: @"c:\libraries\Fixture.al21"))));
        Assert.Equal(["trusted", "trusted"], await Verify((first, Effect()), (second, Effect(path: @"C:\Other\Fixture.al21"))));
        Assert.Equal(["trusted", "trusted"], await Verify((first, Effect(scope: "project")), (second, Effect())));
    }

    [Fact]
    public async Task ResolvedCreateTargetUsesActualSourceName()
    {
        var create = Item("a", "create_master_copy"); create.PlcObjectName = "source";
        var delete = Item("b"); delete.Name = "Source";
        Assert.Equal(["superseded", "trusted"], await Verify((create, Effect(name: "Source")), (delete, Effect(name: "Source"))));
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("leaf-case", true)]
    [InlineData("folder-case", false)]
    [InlineData("sibling", false)]
    [InlineData("folder-kind", false)]
    [InlineData("object-kind", false)]
    [InlineData("literal-slash", false)]
    [InlineData("literal-newline", false)]
    public async Task KeysRespectKindsAndSegmentBoundaries(string scenario, bool superseded)
    {
        var first = Effect(folder: ["A"], name: "Copy");
        var second = scenario switch
        {
            "leaf-case" => Effect(folder: ["A"], name: "COPY"),
            "folder-case" => Effect(folder: ["a"]),
            "sibling" => Effect(folder: ["AB"]),
            "folder-kind" => Effect(folder: ["A"], folderKind: "types"),
            "object-kind" => Effect(folder: ["A", "Copy"], name: "Copy", kind: "folder"),
            "literal-slash" => Effect(folder: ["A/Copy"], kind: "folder"),
            "literal-newline" => Effect(folder: ["A\nmasterCopy\nCOPY"], kind: "folder"),
            _ => Effect(folder: ["A"]),
        };
        var checks = await Verify((Item("a"), first), (Item("b"), second));
        Assert.Equal(superseded ? "superseded" : "trusted", checks[0]);
    }

    [Fact]
    public async Task LaterParentSupersedesDescendantsEvenWithoutContents()
    {
        var leaf = Effect(folder: ["A", "Child"]);
        var parent = Effect(folder: ["A"], name: "A", kind: "folder") with { ContentsTruncated = true };
        Assert.Equal(["superseded", "trusted"], await Verify((Item("a"), leaf), (Item("b", "delete_folder"), parent)));
        Assert.Equal(["trusted", "trusted"], await Verify((Item("a", "create_folder"), parent), (Item("b"), leaf)));
    }

    [Fact]
    public async Task CopyOwnsOnlyResolvedDestination()
    {
        var first = Item("a", "copy_master_copy"); first.SourceLibraryName = "Source";
        var second = Item("b", "copy_master_copy"); second.SourceLibraryName = "Source";
        Assert.Equal(["trusted", "trusted"], await Verify((first, Effect()), (second, Effect(path: @"C:\Other\Fixture.al21"))));
        second.SourceLibraryName = "AnotherSource";
        Assert.Equal(["superseded", "trusted"], await Verify((first, Effect()), (second, Effect())));
    }

    [Theory]
    [InlineData("same", true)]
    [InlineData("case", true)]
    [InlineData("plc", false)]
    [InlineData("kind", false)]
    [InlineData("folder", false)]
    [InlineData("parent", false)]
    [InlineData("child", false)]
    [InlineData("device-alias", false)]
    public async Task InstantiateOwnsOnlyRequestedPlcDestinationContainer(string scenario, bool superseded)
    {
        var first = Instantiate("a");
        var second = Instantiate("b");
        second.LibraryName = "DifferentSource"; second.Name = "DifferentMasterCopy";
        if (scenario == "case") { second.PlcName = "PLC"; second.PlcFolderPath = ["GROUP"]; }
        if (scenario == "plc") second.PlcName = "Other";
        if (scenario == "kind") second.PlcKind = "typeGroup";
        if (scenario == "folder") second.PlcFolderPath = ["Other"];
        if (scenario == "parent") second.PlcFolderPath = [];
        if (scenario == "child") second.PlcFolderPath = ["Group", "Child"];
        if (scenario == "device-alias") second.PlcName = "OwningDevice";
        var checks = await Verify((first, Effect()), (second, Effect(path: @"C:\Other\Source.al21")));
        Assert.Equal(superseded ? "superseded" : "trusted", checks[0]);
        Assert.Equal(["trusted", "trusted"], await Verify((first, Effect()), (Item("b", "close_global"), Effect())));
    }

    [Fact]
    public async Task UnknownIdentitiesNeverCollideAndOpenUsesActualFilePath()
    {
        var empty = new LibraryWriteEffect(null, null, null, [], false);
        Assert.Equal(["trusted", "trusted"], await Verify((Item("a", "create_global"), empty), (Item("b", "retrieve_global"), empty)));
        var open = Item("a", "open_global"); open.FilePath = @"c:\Libraries\sub\..\Fixture.al21";
        var archive = Item("b", "archive_global"); archive.Directory = @"C:\Other"; archive.Name = "Other";
        Assert.Equal(["superseded", "trusted"], await Verify((open, empty), (archive, Effect())));
        var pathless = Effect(path: null);
        Assert.Equal(["trusted", "trusted"], await Verify((Item("a"), pathless), (Item("b"), pathless)));
    }

    [Theory]
    [InlineData("worker_operation_failed", false, "superseded")]
    [InlineData("target_not_found", false, "trusted")]
    [InlineData("target_not_found", true, "superseded")]
    public async Task FailedAttemptSupersedesOnlyWhenMutationIsPossible(string category, bool receipt, string check)
    {
        var items = new[] { Item("a"), Item("b") };
        var result = WorkerCallResult.Fail(category, "failed") with { LibraryWriteOutcome = receipt ? new() { ActualName = "Copy" } : null };
        var verification = await new TrustedWriteVerifier<LibraryWriteItem>(items,
            new LibraryVerificationPolicy(items.ToDictionary(i => i.OperationId, _ => Effect())))
            .VerifyAsync(StructuredOperationBatch.FromItems([Succeeded(items[0]), LibraryWritePayloadContract.Project(items[1], result)]));
        Assert.Equal(check, Assert.Single(verification.Operations).Check);
        Assert.True(verification.Success);
    }

    [Theory]
    [InlineData("delete_master_copy")]
    [InlineData("instantiate_master_copy")]
    public async Task FailedAttemptRecoveryGuidanceDistinguishesLibraryObjectsAndPlcDestinations(string operation)
    {
        var items = operation == "instantiate_master_copy"
            ? new[] { Instantiate("a"), Instantiate("b") }
            : new[] { Item("a"), Item("b") };
        var verification = await new TrustedWriteVerifier<LibraryWriteItem>(items,
            new LibraryVerificationPolicy(items.ToDictionary(i => i.OperationId, _ => Effect())))
            .VerifyAsync(StructuredOperationBatch.FromItems([Succeeded(items[0]),
                LibraryWritePayloadContract.Project(items[1], WorkerCallResult.Fail("worker_operation_failed", "failed"))]));

        var superseded = Assert.Single(verification.Operations);
        Assert.Equal("superseded", superseded.Check);
        Assert.Contains("library_read for library objects", superseded.Message);
        Assert.Contains("plc_read for PLC destinations", superseded.Message);
        Assert.Contains("final state is not verified", superseded.Message);
        Assert.True(verification.Success);
    }

    [Fact]
    public async Task PolicyNeverReadsBack()
    {
        var policy = new LibraryVerificationPolicy(new Dictionary<string, LibraryWriteEffect>());
        Assert.False(policy.HasReadBack(Item("a")));
        await Assert.ThrowsAsync<NotSupportedException>(() => policy.ReadBackAsync(Item("a"), null));
    }

    private static LibraryWriteItem Instantiate(string id) => new()
        { OperationId = id, Operation = "instantiate_master_copy", PlcName = "Plc", PlcKind = "blockGroup", PlcFolderPath = ["Group"] };
    private static LibraryWriteItem Item(string id, string operation = "delete_master_copy") => new()
        { OperationId = id, Operation = operation, Scope = "global", Name = "Copy" };
    private static LibraryWriteEffect Effect(string? path = @"C:\Libraries\Fixture.al21", string scope = "global",
        string[]? folder = null, string name = "Copy", string kind = "masterCopy", string folderKind = "masterCopies")
        => new(new() { Scope = scope, LibraryPath = path, LibraryName = "Fixture", Kind = "library", Name = "Fixture" },
            new() { Scope = scope, LibraryPath = path, LibraryName = "Fixture", FolderPath = folder?.ToList() ?? [], FolderKind = folderKind, Kind = kind, Name = name },
            null, [], false);
    private static StructuredOperationItem Succeeded(LibraryWriteItem item)
        => new(item.OperationId, item.Operation, "succeeded", null, null, null, null, []);
    private static async Task<string[]> Verify(params (LibraryWriteItem Item, LibraryWriteEffect Effect)[] steps)
    {
        var items = steps.Select(s => s.Item).ToArray();
        var verification = await new TrustedWriteVerifier<LibraryWriteItem>(items,
            new LibraryVerificationPolicy(steps.ToDictionary(s => s.Item.OperationId, s => s.Effect)))
            .VerifyAsync(StructuredOperationBatch.FromItems(items.Select(Succeeded).ToArray()));
        Assert.True(verification.Success);
        return verification.Operations.Select(v => v.Check).ToArray();
    }
}
