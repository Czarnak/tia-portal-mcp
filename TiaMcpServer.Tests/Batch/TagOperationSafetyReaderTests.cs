using System.Text.Json;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker.Openness;
using TiaMcpServer.Safety;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyReaderTests
{
    public static TheoryData<string, string, bool> SymbolDriftCases()
    {
        var cases = new TheoryData<string, string, bool>();
        foreach (var operation in new[] { "create_tag", "update_tag", "create_user_constant", "update_user_constant" })
        foreach (var kind in new[] { operation.EndsWith("user_constant") ? "tag" : "constant", "block" })
        foreach (var relevant in new[] { true, false })
            cases.Add(operation, kind, relevant);
        return cases;
    }

    [Theory]
    [MemberData(nameof(SymbolDriftCases))]
    public void NameDriftAcrossSymbolKinds_ChangesTokenOnlyForTheRequestedName(
        string operation, string kind, bool relevant)
    {
        var fixture = new Fixture(operation);
        var result = ValidateAfterDrift(fixture, () =>
            fixture.AddSymbol(kind, relevant ? "rEQUESTED" : "Unrelated"));
        Assert.Equal(!relevant, result.IsValid);
        if (relevant)
            Assert.Equal(WorkerFailureCategories.StateChanged, result.FailureCategory);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void CreateTable_SiblingOrNestedOccupancy_ChangesTokenOnlyForRequestedName(bool nested, bool relevant)
    {
        var fixture = new Fixture("create_tag_table");
        var folder = new PlcTagTableGroup { Name = "Elsewhere" };
        fixture.Plc.TagTableGroup.Groups.Items.Add(folder);
        if (nested)
        {
            var child = new PlcTagTableGroup { Name = "Nested" };
            folder.Groups.Items.Add(child);
            folder = child;
        }
        var result = ValidateAfterDrift(fixture, () =>
            folder.TagTables.Items.Add(new PlcTagTable { Name = relevant ? "rEQUESTED" : "Unrelated" }));
        Assert.Equal(!relevant, result.IsValid);
        if (relevant)
        {
            Assert.Equal(WorkerFailureCategories.StateChanged, result.FailureCategory);
            var snapshot = (CreateTagTableSafetySnapshotInfo)fixture.Read();
            Assert.Equal("/Destination", snapshot.FolderPath);
            Assert.Equal(nested ? "PLC_1/Tag tables/Elsewhere/Nested/rEQUESTED" :
                "PLC_1/Tag tables/Elsewhere/rEQUESTED", Assert.Single(snapshot.TableNameCollisions).CanonicalPath);
        }
    }

    [Theory]
    [InlineData("user")]
    [InlineData("system")]
    [InlineData("nested-system")]
    public void NestedBlockNameDrift_InvalidatesToken(string hierarchy)
    {
        var fixture = new Fixture("create_tag");
        var group = new PlcBlockGroup { Name = "Area" };
        fixture.Plc.BlockGroup.Groups.Items.Add(group);
        var system = new PlcSystemBlockGroup { Name = "System" };
        fixture.Plc.BlockGroup.SystemBlockGroups.Items.Add(system);
        var child = new PlcSystemBlockGroup { Name = "Nested" };
        system.Groups.Items.Add(child);
        var blocks = hierarchy switch
        {
            "user" => group.Blocks,
            "system" => system.Blocks,
            _ => child.Blocks
        };
        var result = ValidateAfterDrift(fixture, () => blocks.Items.Add(new PlcBlock { Name = "Requested" }));
        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.FailureCategory);
    }

    [Fact]
    public void NameProbes_PreserveKindsAndMarkOnlyTheExactTargetWhileAddressesRemainTagOnly()
    {
        var fixture = new Fixture("update_user_constant");
        fixture.Target.Tags.Items.Add(new PlcTag { Name = "Requested" });
        fixture.AddSymbol("block", "Requested");
        var snapshot = (UpdateUserConstantSafetySnapshotInfo)fixture.Read();
        Assert.Equal(new[] { "block-name", "tag-name", "user-constant-name" },
            snapshot.NameCollisions.Select(x => x.Kind).OrderBy(x => x, StringComparer.Ordinal));
        var target = Assert.Single(snapshot.NameCollisions.Where(x => x.IsTarget));
        Assert.Equal("user-constant-name", target.Kind);

        var tagFixture = new Fixture("create_tag");
        tagFixture.AddSymbol("constant", "Requested");
        tagFixture.AddSymbol("block", "Requested");
        tagFixture.AddSymbol("tag", "AddressOccupant");
        var tagSnapshot = (CreateTagSafetySnapshotInfo)tagFixture.Read();
        var address = Assert.Single(tagSnapshot.AddressCollisions);
        Assert.Equal("AddressOccupant", address.CandidateName);
        Assert.Equal("logical-address", address.Kind);
    }

    [Theory]
    [InlineData("create_tag_table", "tables")]
    [InlineData("create_tag", "constants")]
    [InlineData("create_user_constant", "tags")]
    [InlineData("create_tag", "blocks")]
    [InlineData("create_user_constant", "system-blocks")]
    public void CollisionTraversalFailure_PropagatesAfterRelevantEvidence(string operation, string failureAt)
    {
        var fixture = new Fixture(operation);
        fixture.AddSymbol("tag", "Requested");
        fixture.AddSymbol("constant", "Requested");
        fixture.AddSymbol("block", "Requested");
        var failure = new IOException("Cannot complete collision discovery.");
        switch (failureAt)
        {
            case "tables":
                fixture.Sibling.Name = "Requested";
                fixture.Plc.TagTableGroup.Groups.EnumerationFailure = failure;
                break;
            case "constants": fixture.Sibling.UserConstants.EnumerationFailure = failure; break;
            case "tags": fixture.Sibling.Tags.EnumerationFailure = failure; break;
            case "blocks": fixture.Plc.BlockGroup.Blocks.EnumerationFailure = failure; break;
            default: fixture.Plc.BlockGroup.SystemBlockGroups.EnumerationFailure = failure; break;
        }
        Assert.Same(failure, Assert.Throws<IOException>(() => fixture.Read()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void PlcResolution_RejectsMissingAndAmbiguousMatches(int count)
    {
        var fixture = new Fixture("create_tag_table");
        fixture.Project.Devices.Items.Clear();
        for (var i = 0; i < count; i++)
            fixture.AddPlc(new PlcSoftware { Name = "PLC_1" });
        Assert.Throws<InvalidOperationException>(() => fixture.Read());
    }

    [Theory]
    [InlineData("create_tag_table")]
    [InlineData("delete_tag")]
    [InlineData("delete_user_constant")]
    public void SelectorsWithoutSymbolNameProbes_DoNotReadTheBlockRoot(string operation)
    {
        var fixture = new Fixture(operation);
        fixture.Plc.BlockGroupFailure = new IOException("Block state is unrelated to this selector.");
        Assert.NotNull(fixture.Read());
    }

    [Fact]
    public void PlcResolution_PropagatesDiscoveryFailureAfterOneMatch()
    {
        var fixture = new Fixture("create_tag_table");
        var failure = new IOException("Cannot inspect remaining devices.");
        fixture.Project.DeviceGroups.EnumerationFailure = failure;
        Assert.Same(failure, Assert.Throws<IOException>(() => fixture.Read()));
    }

    private static WriteSafetyValidationResult ValidateAfterDrift(Fixture fixture, Action drift)
    {
        var safety = new WriteSafetyService();
        var before = Decode(fixture);
        using var preview = JsonDocument.Parse(safety.CreatePreview("apply_write_batch", null,
            fixture.Request.TableName!, fixture.Operation, fixture.Request, before));
        var token = preview.RootElement.GetProperty("safetyToken").GetString();
        drift();
        return safety.ValidateAndConsume(token, "apply_write_batch", null,
            fixture.Request.TableName!, fixture.Request, Decode(fixture));
    }

    private static string Decode(Fixture fixture)
    {
        var decoded = TiaMcpServer.Batch.TagOperationSafetySnapshotContract.Decode(
            fixture.Operation, CanonicalJson.Serialize(fixture.Read()));
        Assert.True(decoded.Success, decoded.Error);
        return decoded.CanonicalState;
    }

    private sealed class Fixture
    {
        public string Operation { get; }
        public Siemens.Engineering.Project Project { get; } = new();
        public PlcSoftware Plc { get; } = new() { Name = "PLC_1" };
        public PlcTagTable Target { get; } = new() { Name = "Inputs" };
        public PlcTagTable Sibling { get; } = new() { Name = "OtherTable" };
        public WorkerRequest Request { get; }

        public Fixture(string operation)
        {
            Operation = operation;
            AddPlc(Plc);
            var destination = new PlcTagTableGroup { Name = "Destination" };
            Plc.TagTableGroup.Groups.Items.Add(destination);
            destination.TagTables.Items.Add(Target);
            Plc.TagTableGroup.TagTables.Items.Add(Sibling);
            Target.Tags.Items.Add(new PlcTag { Name = "Original", LogicalAddress = "%I0.0" });
            if (operation is "update_user_constant" or "delete_user_constant")
                Target.UserConstants.Items.Add(new PlcUserConstant { Name = "Requested" });
            Request = new WorkerRequest
            {
                PlcName = "PLC_1", FolderPath = "/Destination",
                TableName = operation == "create_tag_table" ? "Requested" : "Inputs",
                Name = operation is "update_tag" or "delete_tag" ? "Original" : "Requested",
                NewName = operation == "update_tag" ? "Requested" : null,
                DataType = "Bool", LogicalAddress = "%I0.1"
            };
        }

        public void AddPlc(PlcSoftware software)
        {
            var device = new Device { Name = "Device" };
            device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = software } });
            Project.Devices.Items.Add(device);
        }

        public void AddSymbol(string kind, string name)
        {
            switch (kind)
            {
                case "tag": Sibling.Tags.Items.Add(new PlcTag { Name = name, LogicalAddress = "%I0.1" }); break;
                case "constant": Sibling.UserConstants.Items.Add(new PlcUserConstant { Name = name }); break;
                default: Plc.BlockGroup.Blocks.Items.Add(new PlcBlock { Name = name }); break;
            }
        }

        public object Read() => Operation switch
        {
            "create_tag_table" => TagOperationSafetySnapshotReader.ReadCreateTagTable(Project, Request),
            "create_tag" => TagOperationSafetySnapshotReader.ReadCreateTag(Project, Request),
            "update_tag" => TagOperationSafetySnapshotReader.ReadUpdateTag(Project, Request),
            "delete_tag" => TagOperationSafetySnapshotReader.ReadDeleteTag(Project, Request),
            "create_user_constant" => TagOperationSafetySnapshotReader.ReadCreateUserConstant(Project, Request),
            "update_user_constant" => TagOperationSafetySnapshotReader.ReadUpdateUserConstant(Project, Request),
            "delete_user_constant" => TagOperationSafetySnapshotReader.ReadDeleteUserConstant(Project, Request),
            _ => throw new InvalidOperationException(Operation)
        };
    }
}
