using System.Text.Json;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using TiaMcpServer.ProjectTree;
using TiaMcpServer.Tests.TestUtilities;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ProjectTreeWorkerProducerContractTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData(2, false)]
    [InlineData(1, false)]
    [InlineData(null, true)]
    [InlineData(1, true)]
    public void DefaultAndShallowSnapshots_RealWorkerSerializationPassesStrictDecoder(int? depth, bool selected)
    {
        var selector = selected ? Selector(("Device", "plc_1")) : null;
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(ProjectWithLeaves(includeLeaves: false), selector, depth);

        var observation = Decode(snapshot, selector, depth);

        Assert.Equal("PLC_1", Assert.Single(observation.Roots).Name);
        if (depth == 1)
            Assert.Equal("1", observation.Roots[0].Details!["ChildrenOmitted"]);
        else
            Assert.Null(observation.Roots[0].Details);
        Assert.Equal(depth, observation.Depth);
        if (selected)
            Assert.Equal("PLC_1", Assert.Single(observation.CanonicalStartSelector!).Name);
        else
            Assert.Null(observation.CanonicalStartSelector);
    }

    [Theory]
    [InlineData("BlockFolder", "Program blocks", "FB", "Motor", false)]
    [InlineData("TagTableFolder", "PLC tags", "TagTable", "Signals", false)]
    [InlineData("TypeFolder", "PLC data types", "Type", "State", false)]
    [InlineData("BlockFolder", "Program blocks", "FB", "Motor", true)]
    [InlineData("TagTableFolder", "PLC tags", "TagTable", "Signals", true)]
    [InlineData("TypeFolder", "PLC data types", "Type", "State", true)]
    public void FullAndLeafSelectedSnapshots_AllLeafFamiliesPassStrictDecoder(
        string folderType, string folderName, string leafType, string leafName, bool selected)
    {
        var selector = selected
            ? Selector(("Device", "PLC_1"), ("PlcSoftware", "PLC"), (folderType, folderName), (leafType, leafName))
            : null;
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(ProjectWithLeaves(), selector, depth: null);

        // Check the producer first so a missing children array is diagnosed independently
        // of the separate null-member serialization defect.
        var leaf = Descendants(snapshot.Roots).Single(node => node.NodeType == leafType);
        Assert.NotNull(leaf.Children);
        Assert.Empty(leaf.Children);
        var observation = Decode(snapshot, selector, depth: null);
        var decodedLeaf = Descendants(observation.Roots).Single(node => node.NodeType == leafType);
        Assert.Equal(leafName, decodedLeaf.Name);
        Assert.Empty(decodedLeaf.Children!);
        if (selected)
            Assert.Equal(leafName, Assert.Single(observation.Roots).Name);
    }

    [Fact]
    public void BlockHeaders_PopulatedValuesCrossStrictBoundaryWithoutChangingIdentity()
    {
        var project = ProjectWithLeaves(
            userBlock: new FB
            {
                Name = "Motor",
                Number = 1,
                HeaderAuthor = " \tExample Controls Ltd. ",
                HeaderVersion = new Version(2, 3),
                HeaderFamily = "Motion",
                HeaderName = "Reusable motor control"
            },
            systemBlock: new FC
            {
                Name = "SystemCycle",
                Number = 2,
                HeaderAuthor = "Siemens",
                HeaderVersion = new Version(1, 0),
                HeaderFamily = "System",
                HeaderName = "System cycle header"
            });
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(project, startSelector: null, depth: null);

        var observation = Decode(snapshot, selector: null, depth: null);
        var userBlock = Descendants(observation.Roots).Single(node => node.Name == "Motor");
        var systemBlock = Descendants(observation.Roots).Single(node => node.Name == "SystemCycle");

        Assert.Equal("Motor", userBlock.Name);
        Assert.Equal(
            new[]
            {
                "Number", "ProgrammingLanguage", "HeaderAuthor", "HeaderVersion",
                "HeaderFamily", "HeaderName"
            },
            userBlock.Details!.Keys);
        Assert.Equal(" \tExample Controls Ltd. ", userBlock.Details["HeaderAuthor"]);
        Assert.Equal("2.3", userBlock.Details["HeaderVersion"]);
        Assert.Equal("Motion", userBlock.Details["HeaderFamily"]);
        Assert.Equal("Reusable motor control", userBlock.Details["HeaderName"]);
        Assert.False(userBlock.Details.ContainsKey("IsSystemBlock"));

        Assert.Equal("SystemCycle", systemBlock.Name);
        Assert.Equal(ProjectTreeNodeTypes.Fc, systemBlock.NodeType);
        Assert.Equal("Siemens", systemBlock.Details!["HeaderAuthor"]);
        Assert.Equal("1.0", systemBlock.Details["HeaderVersion"]);
        Assert.Equal("System", systemBlock.Details["HeaderFamily"]);
        Assert.Equal("System cycle header", systemBlock.Details["HeaderName"]);
        Assert.Equal("true", systemBlock.Details["IsSystemBlock"]);
    }

    [Fact]
    public void BlockHeaders_SoftwareUnitBlockRetainsContextAcrossStrictBoundary()
    {
        var unit = new PlcUnit { Name = "MotionUnit" };
        unit.BlockGroup.Name = "Program blocks";
        unit.TagTableGroup.Name = "PLC tags";
        unit.TypeGroup.Name = "PLC data types";
        unit.BlockGroup.Blocks.Items.Add(new FB
        {
            Name = "UnitMotor",
            Number = 3,
            HeaderAuthor = "Example Controls Ltd.",
            HeaderVersion = new Version(4, 5),
            HeaderFamily = "Motion",
            HeaderName = "Unit motor header"
        });
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(
            ProjectWithLeaves(softwareUnit: unit), startSelector: null, depth: null);

        var observation = Decode(snapshot, selector: null, depth: null);
        var decodedUnit = Descendants(observation.Roots).Single(node => node.Name == "MotionUnit");
        Assert.Equal(ProjectTreeNodeTypes.SoftwareUnit, decodedUnit.NodeType);
        var block = Descendants(decodedUnit.Children!).Single(node => node.Name == "UnitMotor");

        Assert.Equal(ProjectTreeNodeTypes.Fb, block.NodeType);
        Assert.Equal(
            new[]
            {
                "Number", "ProgrammingLanguage", "HeaderAuthor", "HeaderVersion",
                "HeaderFamily", "HeaderName", "SoftwareUnit"
            },
            block.Details!.Keys);
        Assert.Equal("3", block.Details["Number"]);
        Assert.Equal("SCL", block.Details["ProgrammingLanguage"]);
        Assert.Equal("Example Controls Ltd.", block.Details["HeaderAuthor"]);
        Assert.Equal("4.5", block.Details["HeaderVersion"]);
        Assert.Equal("Motion", block.Details["HeaderFamily"]);
        Assert.Equal("Unit motor header", block.Details["HeaderName"]);
        Assert.Equal("MotionUnit", block.Details["SoftwareUnit"]);
        Assert.False(block.Details.ContainsKey("IsSystemBlock"));
    }

    [Fact]
    public void BlockHeaders_NullEmptyAndWhitespaceValuesAreOmitted()
    {
        var project = ProjectWithLeaves(userBlock: new FB
        {
            Name = "Motor",
            Number = 1,
            HeaderAuthor = null,
            HeaderVersion = null,
            HeaderFamily = string.Empty,
            HeaderName = " \t "
        });
        var snapshot = new ProjectTreeSnapshotWalker().WalkSnapshot(project, startSelector: null, depth: null);

        var observation = Decode(snapshot, selector: null, depth: null);
        var block = Descendants(observation.Roots).Single(node => node.Name == "Motor");

        Assert.Equal("1", block.Details!["Number"]);
        Assert.Equal("SCL", block.Details["ProgrammingLanguage"]);
        Assert.DoesNotContain("HeaderAuthor", block.Details.Keys);
        Assert.DoesNotContain("HeaderVersion", block.Details.Keys);
        Assert.DoesNotContain("HeaderFamily", block.Details.Keys);
        Assert.DoesNotContain("HeaderName", block.Details.Keys);
    }

    [Fact]
    public void MarkedPayloadContracts_KeepOmittingNullMembers()
    {
        Assert.Equal(
            """{"success":true,"operation":"start_plc","plcName":"PLC_1"}""",
            WorkerSerializationHarness.Serialize(new PlcOnlineResultInfo { Operation = "start_plc", PlcName = "PLC_1" }).Payload);
    }

    [Fact]
    public void UnmarkedPayloadContracts_WriteNullMembers()
    {
        Assert.Equal(
            """{"name":"unchanged","optional":null}""",
            WorkerSerializationHarness.Serialize(new { Name = "unchanged", Optional = (string?)null }).Payload);

        Assert.Equal(
            """{"success":true,"operation":"save_project","projectPath":null,"project":null}""",
            WorkerSerializationHarness.Serialize(new ProjectLifecycleResultInfo { Operation = "save_project" }).Payload);

        // CatalogEntryInfo no longer carries [LegacyNullOmission(RequiredMemberEnforcement)]
        // (Task 3): its null members are now written explicitly, like any unmarked contract.
        Assert.Equal(
            """[{"typeName":"CPU","articleNumber":null,"version":null,"typeIdentifier":"OrderNumber:X","typeIdentifierNormalized":null,"catalogPath":null,"description":null}]""",
            WorkerSerializationHarness.Serialize(new List<CatalogEntryInfo>
            {
                new() { TypeName = "CPU", TypeIdentifier = "OrderNumber:X" }
            }).Payload);
    }

    [Fact]
    public void NetworkObjectList_StillPreservesRequiredNullCursor()
    {
        var response = WorkerSerializationHarness.Serialize(new NetworkObjectListInfo());
        using var json = JsonDocument.Parse(response.Payload!);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("nextCursor").ValueKind);
    }

    private static ProjectTreeObservation Decode(ProjectTreeSelectionResult snapshot,
        IReadOnlyList<ProjectTreeSelectorSegment>? selector, int? depth)
    {
        var response = WorkerSerializationHarness.Serialize(new ProjectTreeBrowseResultInfo
        {
            StartSelector = snapshot.CanonicalStartSelector?.ToList(),
            Depth = depth,
            Roots = snapshot.Roots.ToList()
        });
        Assert.True(response.Success);
        var worker = WorkerCallResult.Ok(response.Payload!) with
        {
            ResolvedProjectPath = @"C:\Projects\Plant.ap21"
        };
        return ProjectTreeWorkerPayloadContract.Decode(worker, selector, depth);
    }

    private static Siemens.Engineering.Project ProjectWithLeaves(
        bool includeLeaves = true,
        PlcBlock? userBlock = null,
        PlcBlock? systemBlock = null,
        PlcUnit? softwareUnit = null)
    {
        var project = new Siemens.Engineering.Project();
        var device = new Device { Name = "PLC_1" };
        var plc = new PlcSoftware { Name = "PLC" };
        plc.BlockGroup.Name = "Program blocks";
        plc.TagTableGroup.Name = "PLC tags";
        plc.TypeGroup.Name = "PLC data types";
        if (includeLeaves)
        {
            plc.BlockGroup.Blocks.Items.Add(userBlock ?? new FB { Name = "Motor", Number = 1 });
            plc.TagTableGroup.TagTables.Items.Add(new PlcTagTable { Name = "Signals" });
            plc.TypeGroup.Types.Items.Add(new PlcType { Name = "State" });
        }

        if (systemBlock is not null)
        {
            var systemGroup = new PlcSystemBlockGroup { Name = "System blocks" };
            systemGroup.Blocks.Items.Add(systemBlock);
            plc.BlockGroup.SystemBlockGroups.Items.Add(systemGroup);
        }
        if (softwareUnit is not null)
        {
            plc.UnitProvider = new PlcUnitProvider();
            plc.UnitProvider.UnitGroup.Units.Items.Add(softwareUnit);
        }
        device.DeviceItems.Items.Add(new DeviceItem
        {
            Container = new SoftwareContainer { Software = plc }
        });
        project.Devices.Items.Add(device);
        return project;
    }

    private static List<ProjectTreeSelectorSegment> Selector(params (string Type, string Name)[] segments)
        => segments.Select(segment => new ProjectTreeSelectorSegment { NodeType = segment.Type, Name = segment.Name }).ToList();

    private static IEnumerable<ProjectTreeNode> Descendants(IEnumerable<ProjectTreeNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in Descendants(node.Children ?? Enumerable.Empty<ProjectTreeNode>()))
                yield return child;
        }
    }
}
