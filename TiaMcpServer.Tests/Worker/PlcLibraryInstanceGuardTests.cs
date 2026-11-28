using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.Library.Types;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts.Plc;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker.Openness.Plc;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class PlcLibraryInstanceGuardTests
{
    [Theory]
    [InlineData("FB", LibraryTypeVersionState.InWork)]
    [InlineData("FB", LibraryTypeVersionState.Committed)]
    [InlineData("FC", LibraryTypeVersionState.InWork)]
    [InlineData("FC", LibraryTypeVersionState.Committed)]
    [InlineData("UDT", LibraryTypeVersionState.InWork)]
    [InlineData("UDT", LibraryTypeVersionState.Committed)]
    public void TestPlcLinkedInstanceGuardsNeverMutate_ContentBlocksWithoutReadingVersion(string kind, LibraryTypeVersionState state)
    {
        var target = Linked(kind, state);
        var link = target.LibraryTypeInstanceInfo!;
        link.ReadLibraryTypeVersion = () => throw new InvalidOperationException("Content protection must not read the version.");
        var mutationCalls = 0;

        var result = PlcLibraryInstanceGuard.InspectContentUpdate(target);
        if (!result.Blocked) mutationCalls++;

        AssertBlocked(result, PlcLibraryInstanceGuard.LibraryInstanceEdit);
        Assert.Equal(0, mutationCalls);
        Assert.Equal(0, link.LibraryTypeVersionReads);
        Assert.Equal(1, target.LibraryTypeInstanceInfoRequests);
    }

    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    [InlineData("UDT")]
    public void PlainObjectsAllowContentAndDeleteWithoutUnrelatedReads(string kind)
    {
        var target = Target(kind);
        target.NameFailure = new EngineeringException("Name is unnecessary for this inspection.");

        AssertClear(PlcLibraryInstanceGuard.InspectContentUpdate(target));
        AssertClear(PlcLibraryInstanceGuard.InspectDelete(target));

        Assert.Equal(2, target.LibraryTypeInstanceInfoRequests);
    }

    [Theory]
    [InlineData("FB", LibraryTypeVersionState.InWork, true)]
    [InlineData("FC", LibraryTypeVersionState.InWork, true)]
    [InlineData("UDT", LibraryTypeVersionState.InWork, true)]
    [InlineData("FB", LibraryTypeVersionState.Committed, false)]
    [InlineData("FC", LibraryTypeVersionState.Committed, false)]
    [InlineData("UDT", LibraryTypeVersionState.Committed, false)]
    public void TestLibraryInstanceDeletionChecks_OnlyInWorkIsProtected(string kind, LibraryTypeVersionState state, bool blocked)
    {
        var target = Linked(kind, state);
        var result = PlcLibraryInstanceGuard.InspectDelete(target);

        if (blocked) AssertBlocked(result, PlcLibraryInstanceGuard.LibraryTestInstanceDelete);
        else AssertClear(result);
        Assert.Equal(1, target.LibraryTypeInstanceInfo!.LibraryTypeVersionReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnreadableServiceFailsClosed(bool deleting)
    {
        var target = Target("FB");
        target.LibraryTypeInstanceInfoFailure = new EngineeringException("Cannot inspect linkage.");

        AssertBlocked(deleting ? PlcLibraryInstanceGuard.InspectDelete(target)
            : PlcLibraryInstanceGuard.InspectContentUpdate(target), PlcLibraryInstanceGuard.StateUnverifiable);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("versionFailure")]
    [InlineData("stateFailure")]
    [InlineData("unknownState")]
    public void DeletionUnavailableOrUnknownVersionFailsClosed(string failure)
    {
        var target = Linked("FC", LibraryTypeVersionState.Committed);
        var info = target.LibraryTypeInstanceInfo!;
        if (failure == "missing") info.LibraryTypeVersion = null!;
        if (failure == "versionFailure") info.ReadLibraryTypeVersion = () => throw new EngineeringException("Cannot read version.");
        if (failure == "stateFailure") info.LibraryTypeVersion.ReadState = () => throw new EngineeringException("Cannot read state.");
        if (failure == "unknownState") info.LibraryTypeVersion.State = (LibraryTypeVersionState)99;

        AssertBlocked(PlcLibraryInstanceGuard.InspectDelete(target), PlcLibraryInstanceGuard.StateUnverifiable);
    }

    [Theory]
    [InlineData("contentService", false)]
    [InlineData("deleteService", false)]
    [InlineData("version", false)]
    [InlineData("state", false)]
    [InlineData("blocks", false)]
    [InlineData("groups", false)]
    [InlineData("contentService", true)]
    [InlineData("deleteService", true)]
    [InlineData("version", true)]
    [InlineData("state", true)]
    [InlineData("blocks", true)]
    [InlineData("groups", true)]
    public void FatalLossEscapesAsOriginalException(string stage, bool wrapped)
    {
        Exception failure = wrapped
            ? new WorkerOperationException(WorkerFailureCategories.PortalLost, "Portal lost.")
            : new NonRecoverableException("Portal lost.");
        var target = Linked("FB", LibraryTypeVersionState.Committed);
        var group = new PlcBlockGroup();
        if (stage.EndsWith("Service", StringComparison.Ordinal)) target.LibraryTypeInstanceInfoFailure = failure;
        if (stage == "version") target.LibraryTypeInstanceInfo!.ReadLibraryTypeVersion = () => throw failure;
        if (stage == "state") target.LibraryTypeInstanceInfo!.LibraryTypeVersion.ReadState = () => throw failure;
        if (stage == "blocks") group.Blocks.EnumerationFailure = failure;
        if (stage == "groups") group.Groups.EnumerationFailure = failure;

        var actual = Record.Exception(() =>
        {
            if (stage == "contentService") PlcLibraryInstanceGuard.InspectContentUpdate(target);
            else if (stage is "blocks" or "groups") PlcLibraryInstanceGuard.InspectDeleteDescendants(group);
            else PlcLibraryInstanceGuard.InspectDelete(target);
        });

        Assert.Same(failure, actual);
    }

    [Fact]
    public void NestedGroupFindsInWorkBlockBeforeAnyDeletion()
    {
        var root = new PlcBlockUserGroup();
        var child = root.Groups.Create("Child");
        var nested = child.Groups.Create("Nested");
        nested.Blocks.Items.Add((PlcBlock)Linked("FC", LibraryTypeVersionState.InWork));

        AssertBlocked(PlcLibraryInstanceGuard.InspectDeleteDescendants(root), PlcLibraryInstanceGuard.LibraryTestInstanceDelete);

        Assert.False(root.Deleted);
        Assert.False(child.Deleted);
        Assert.False(nested.Deleted);
        Assert.False(Assert.Single(nested.Blocks.Items).Deleted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteDescendantEnumerationFailsClosed(bool childGroups)
    {
        var root = new PlcBlockGroup();
        root.Blocks.Items.Add((PlcBlock)Linked("FB", LibraryTypeVersionState.Committed));
        if (childGroups) root.Groups.EnumerationFailure = new EngineeringException("Missing descendants.");
        else root.Blocks.EnumerationFailure = new EngineeringException("Missing descendants.");

        AssertBlocked(PlcLibraryInstanceGuard.InspectDeleteDescendants(root), PlcLibraryInstanceGuard.StateUnverifiable);
    }

    [Fact]
    public void CompleteGroupAllowsPlainAndCommittedDescendants()
    {
        var root = new PlcBlockGroup();
        root.Blocks.Items.Add(new FB());
        var child = root.Groups.Create("Nested");
        child.Blocks.Items.Add((PlcBlock)Linked("FC", LibraryTypeVersionState.Committed));

        AssertClear(PlcLibraryInstanceGuard.InspectDeleteDescendants(root));
        Assert.Equal(1, root.Blocks.YieldedItemCount);
        Assert.Equal(1, child.Blocks.YieldedItemCount);
    }

    [Theory]
    [InlineData(PlcLibraryInstanceGuard.LibraryInstanceEdit)]
    [InlineData(PlcLibraryInstanceGuard.LibraryTestInstanceDelete)]
    [InlineData(PlcLibraryInstanceGuard.StateUnverifiable)]
    public void RequireAllowedRefusesWithStableGuardId(string guardId)
    {
        var error = Assert.Throws<WorkerOperationException>(() => PlcLibraryInstanceGuard.RequireAllowed(new()
        { Blocked = true, GuardId = guardId, Message = "Refused." }));

        Assert.Equal(WorkerFailureCategories.GuardBlocked, error.FailureCategory);
        Assert.Contains(guardId, error.Message);
    }

    [Fact]
    public void ClearEvidenceKeepsRequiredExplicitNulls()
    {
        var clear = PlcLibraryInstanceGuard.InspectContentUpdate(new FB());
        using var document = JsonDocument.Parse(CanonicalJson.Serialize(clear));

        AssertClear(clear);
        Assert.False(document.RootElement.GetProperty("blocked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("guardId").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("message").ValueKind);
        AssertClear(CanonicalJson.DeserializeWorkerPayload<PlcWriteGuardInfo>(document.RootElement.GetRawText())!);
    }

    private static NamedObject Target(string kind) => kind switch
    {
        "FB" => new FB(),
        "FC" => new FC(),
        "UDT" => new PlcType(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static NamedObject Linked(string kind, LibraryTypeVersionState state)
    {
        var target = Target(kind);
        target.LibraryTypeInstanceInfo = new LibraryTypeInstanceInfo
        { LibraryTypeVersion = new LibraryTypeVersion { State = state } };
        return target;
    }

    private static void AssertBlocked(PlcWriteGuardInfo result, string guardId)
    {
        Assert.True(result.Blocked);
        Assert.Equal(guardId, result.GuardId);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    private static void AssertClear(PlcWriteGuardInfo result)
    {
        Assert.False(result.Blocked);
        Assert.Null(result.GuardId);
        Assert.Null(result.Message);
    }
}
