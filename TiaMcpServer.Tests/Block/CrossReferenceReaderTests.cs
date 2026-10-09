using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts.CrossReferences;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.CrossReferences;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Block;

[Collection("Cross-reference console")]
public class CrossReferenceReaderTests
{
    private static readonly CrossReferenceSelectorInfo PlcPath = Path(
        (ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_DP"));

    [Fact]
    public void Harness_PreservesNestedProjectionAndFilter()
    {
        var (project, plc) = Fixture();
        var service = Service("root");
        var root = service.Result.Sources.Items[0];
        var child = new SourceObject { Name = "child" };
        var reference = new ReferenceObject { Name = "target", Path = "target/path" };
        reference.Locations.Items.Add(new Location { Name = "network", Access = Access.Read, ReferencedAsName = "tag" });
        child.References.Items.Add(reference);
        root.Children.Items.Add(child);
        plc.CrossReferenceService = service;
        plc.BlockGroup.Blocks.Items.Add(new FC { Name = "owner", CrossReferenceService = service });

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.UnusedObjects);

        var source = Assert.Single(report.Sources);
        Assert.Equal("root", source.Name);
        Assert.Equal("target/path", Assert.Single(Assert.Single(source.Children).References).Path);
        Assert.Equal(2, report.TotalSourceCount);
        Assert.Equal(1, report.TotalReferenceCount);
        Assert.Equal(1, report.TotalLocationCount);
        Assert.Equal(0, report.OmittedSourceCount);
        Assert.Equal(CrossReferenceFilter.UnusedObjects, Assert.Single(service.Queries));
    }

    [Fact]
    public void Read_QueriesEveryOwnerAcrossGroupsAndUnits()
    {
        var (project, plc) = Fixture();
        var owners = new List<NamedObject>();
        T Owner<T>(T owner, string name) where T : NamedObject
        {
            owner.Name = name;
            owner.CrossReferenceService = Service(name);
            owners.Add(owner);
            return owner;
        }
        plc.BlockGroup.Blocks.Items.Add(Owner(new OB(), "rootOB"));
        // A block class outside OB/FB/FC/DB kinds is still an owner (spec §5.2).
        plc.BlockGroup.Blocks.Items.Add(Owner(new PlcBlock(), "nonStandardBlock"));
        var nested = new PlcBlockGroup();
        plc.BlockGroup.Groups.Items.Add(new PlcBlockGroup { Groups = { Items = { nested } } });
        nested.Blocks.Items.Add(Owner(new FB(), "nestedFB"));
        nested.Blocks.Items.Add(Owner(new FC(), "nestedFC"));
        var system = new PlcSystemBlockGroup();
        plc.BlockGroup.SystemBlockGroups.Items.Add(new PlcSystemBlockGroup { Groups = { Items = { system } } });
        system.Blocks.Items.Add(Owner(new GlobalDB(), "systemDB"));
        system.Blocks.Items.Add(Owner(new InstanceDB(), "systemIDB"));
        var table = new PlcTagTable { Name = "table", CrossReferenceService = Service("forbiddenTable") };
        table.UserConstants.Items.Add(Owner(new PlcUserConstant(), "userConstant"));
        table.Tags.Items.Add(Owner(new PlcTag(), "tag"));
        table.SystemConstants.Items.Add(Owner(new PlcSystemConstant(), "systemConstant"));
        plc.TagTableGroup.Groups.Items.Add(new PlcTagTableGroup { TagTables = { Items = { table } } });
        plc.TypeGroup.Groups.Items.Add(new PlcTypeGroup { Types = { Items = { Owner(new PlcType(), "nestedUDT") } } });
        var unit = new PlcUnit { Name = "Unit1", CrossReferenceService = Service("forbiddenUnit") };
        plc.UnitProvider = new PlcUnitProvider();
        plc.UnitProvider.UnitGroup.Units.Items.Add(unit);
        unit.BlockGroup.Blocks.Items.Add(Owner(new ArrayDB(), "unitDB"));
        unit.TypeGroup.Types.Items.Add(Owner(new PlcType(), "unitUDT"));
        var unitTable = new PlcTagTable();
        unitTable.Tags.Items.Add(Owner(new PlcTag(), "unitTag"));
        unitTable.SystemConstants.Items.Add(Owner(new PlcSystemConstant(), "unitConstant"));
        unit.TagTableGroup.TagTables.Items.Add(unitTable);
        plc.CrossReferenceService = Service("forbiddenSoftware");

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);

        Assert.Equal(owners.Select(o => o.Name).OrderBy(n => n), report.Sources.Select(s => s.Name).OrderBy(n => n));
        Assert.Equal(owners.Count, report.OwnerQueryCount);
        Assert.Equal(owners.Count, report.SuccessfulOwnerQueryCount);
        Assert.True(report.IsComplete);
        Assert.All(owners, o => Assert.Equal(1, o.CrossReferenceServiceRequests));
        Assert.Equal(0, plc.CrossReferenceServiceRequests);
        Assert.Equal(0, table.CrossReferenceServiceRequests);
        Assert.Equal(0, unit.CrossReferenceServiceRequests);
        Assert.All(owners, o => Assert.Equal(CrossReferenceFilter.AllObjects, Assert.Single(o.CrossReferenceService!.Queries)));
    }

    [Fact]
    public void SweepCountsNonStandardBlockWithoutServiceAsIncomplete()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { Name = "fc", CrossReferenceService = Service("fcSource") });
        var system = new PlcSystemBlockGroup();
        plc.BlockGroup.SystemBlockGroups.Items.Add(system);
        system.Blocks.Items.Add(new PlcBlock { Name = "noService" });

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);

        Assert.Equal(2, report.OwnerQueryCount);
        Assert.Equal(1, report.SuccessfulOwnerQueryCount);
        Assert.Equal("fcSource", Assert.Single(report.Sources).Name);
        Assert.False(report.IsComplete);
        Assert.NotEmpty(report.Messages);
    }

    [Fact]
    public void Read_EchoesCanonicalTargetAndFilter()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("source") });

        var report = CrossReferenceReader.Read(project,
            Path((ProjectTreeNodeTypes.Device, "STATION_1"), (ProjectTreeNodeTypes.PlcSoftware, "plc_dp")),
            CrossReferenceFilterNames.AllObjects);

        Assert.Equal(new[] { "Station_1", "PLC_DP" }, report.Target.Path.Select(s => s.Name));
        Assert.Null(report.Target.Member);
        Assert.Equal(CrossReferenceFilterNames.AllObjects, report.Filter);
    }

    [Fact]
    public void LeafTargetQueriesOneOwner()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Name = "Program blocks";
        var leaf = new FC { Name = "Leaf", CrossReferenceService = Service("leafSource") };
        var sibling = new FC { Name = "Sibling", CrossReferenceService = Service("siblingSource") };
        plc.BlockGroup.Blocks.Items.Add(leaf);
        plc.BlockGroup.Blocks.Items.Add(sibling);

        var report = CrossReferenceReader.Read(project, Path((ProjectTreeNodeTypes.Device, "Station_1"),
            (ProjectTreeNodeTypes.PlcSoftware, "PLC_DP"), (ProjectTreeNodeTypes.BlockFolder, "Program blocks"),
            (ProjectTreeNodeTypes.Fc, "leaf")), CrossReferenceFilterNames.ObjectsWithReferences);

        Assert.Equal("leafSource", Assert.Single(report.Sources).Name);
        Assert.Equal(1, report.OwnerQueryCount);
        Assert.Equal(1, report.SuccessfulOwnerQueryCount);
        Assert.True(report.IsComplete);
        Assert.Equal(CrossReferenceFilter.ObjectsWithReferences, Assert.Single(leaf.CrossReferenceService!.Queries));
        Assert.Equal(0, sibling.CrossReferenceServiceRequests);
    }

    [Fact]
    public void LeafTargetWhoseQueryFailsIsWorkerOperationFailed()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Name = "Program blocks";
        plc.BlockGroup.Blocks.Items.Add(new FC
        {
            Name = "Leaf",
            CrossReferenceService = new CrossReferenceService { Failure = new EngineeringException("private") }
        });

        var error = Assert.Throws<WorkerOperationException>(() => CrossReferenceReader.Read(project,
            Path((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_DP"),
                (ProjectTreeNodeTypes.BlockFolder, "Program blocks"), (ProjectTreeNodeTypes.Fc, "Leaf")),
            CrossReferenceFilterNames.AllObjects));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public void TagTableContainerFansOutOverTags()
    {
        var (project, plc) = Fixture();
        plc.TagTableGroup.Name = "PLC tags";
        var table = new PlcTagTable { Name = "Motors", CrossReferenceService = Service("forbiddenTable") };
        table.Tags.Items.Add(new PlcTag { Name = "Start", CrossReferenceService = Service("Start") });
        table.Tags.Items.Add(new PlcTag { Name = "Stop", CrossReferenceService = Service("Stop") });
        table.UserConstants.Items.Add(new PlcUserConstant { Name = "Stages", CrossReferenceService = Service("Stages") });
        table.SystemConstants.Items.Add(new PlcSystemConstant { Name = "HwId", CrossReferenceService = Service("HwId") });
        var other = new PlcTagTable { Name = "Other" };
        other.Tags.Items.Add(new PlcTag { Name = "Elsewhere", CrossReferenceService = Service("Elsewhere") });
        plc.TagTableGroup.TagTables.Items.Add(table);
        plc.TagTableGroup.TagTables.Items.Add(other);

        var report = CrossReferenceReader.Read(project, Path((ProjectTreeNodeTypes.Device, "Station_1"),
            (ProjectTreeNodeTypes.PlcSoftware, "PLC_DP"), (ProjectTreeNodeTypes.TagTableFolder, "PLC tags"),
            (ProjectTreeNodeTypes.TagTable, "Motors")), CrossReferenceFilterNames.AllObjects);

        Assert.Equal(new[] { "HwId", "Stages", "Start", "Stop" }, report.Sources.Select(s => s.Name).OrderBy(n => n));
        Assert.Equal(4, report.OwnerQueryCount);
        Assert.Equal(4, report.SuccessfulOwnerQueryCount);
        Assert.Equal(0, table.CrossReferenceServiceRequests);
        Assert.Equal(0, other.Tags.Items[0].CrossReferenceServiceRequests);
        Assert.True(report.IsComplete);
    }

    [Theory]
    [InlineData(ProjectTreeNodeTypes.BlockFolder, "Program blocks", "blockFB")]
    [InlineData(ProjectTreeNodeTypes.SystemBlockFolder, "System blocks", "systemDB")]
    [InlineData(ProjectTreeNodeTypes.TagTableFolder, "PLC tags", "tag")]
    [InlineData(ProjectTreeNodeTypes.TypeFolder, "PLC data types", "udt")]
    [InlineData(ProjectTreeNodeTypes.SoftwareUnit, "Unit1", "unitFC")]
    public void FolderContainersFanOutOverOwnersBeneathOnly(string nodeType, string name, string expectedSource)
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Name = "Program blocks";
        plc.TagTableGroup.Name = "PLC tags";
        plc.TypeGroup.Name = "PLC data types";
        plc.BlockGroup.Blocks.Items.Add(new FB { Name = "fb", CrossReferenceService = Service("blockFB") });
        var system = new PlcSystemBlockGroup { Name = "System blocks" };
        system.Blocks.Items.Add(new GlobalDB { Name = "db", CrossReferenceService = Service("systemDB") });
        plc.BlockGroup.SystemBlockGroups.Items.Add(system);
        var table = new PlcTagTable { Name = "t" };
        table.Tags.Items.Add(new PlcTag { Name = "tag", CrossReferenceService = Service("tag") });
        plc.TagTableGroup.TagTables.Items.Add(table);
        plc.TypeGroup.Types.Items.Add(new PlcType { Name = "udt", CrossReferenceService = Service("udt") });
        var unit = new PlcUnit { Name = "Unit1" };
        unit.BlockGroup.Blocks.Items.Add(new FC { Name = "fc", CrossReferenceService = Service("unitFC") });
        plc.UnitProvider = new PlcUnitProvider();
        plc.UnitProvider.UnitGroup.Units.Items.Add(unit);
        var path = PlcPath.Path.Append(new ProjectTreeSelectorSegment { NodeType = nodeType, Name = name }).ToList();
        if (nodeType == ProjectTreeNodeTypes.SystemBlockFolder)
            path.Insert(2, new ProjectTreeSelectorSegment { NodeType = ProjectTreeNodeTypes.BlockFolder, Name = "Program blocks" });

        var report = CrossReferenceReader.Read(project, new CrossReferenceSelectorInfo { Path = path },
            CrossReferenceFilterNames.AllObjects);

        var expected = nodeType == ProjectTreeNodeTypes.BlockFolder ? new[] { "blockFB", "systemDB" } : new[] { expectedSource };
        Assert.Equal(expected, report.Sources.Select(s => s.Name));
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void MemberTargetQueriesOnlyThatMember()
    {
        var (project, plc) = Fixture();
        plc.TagTableGroup.Name = "PLC tags";
        var table = new PlcTagTable { Name = "Motors" };
        table.Tags.Items.Add(new PlcTag { Name = "Start", CrossReferenceService = Service("tagSource") });
        table.UserConstants.Items.Add(new PlcUserConstant { Name = "Start", CrossReferenceService = Service("constantSource") });
        plc.TagTableGroup.TagTables.Items.Add(table);
        var selector = Path((ProjectTreeNodeTypes.Device, "Station_1"), (ProjectTreeNodeTypes.PlcSoftware, "PLC_DP"),
            (ProjectTreeNodeTypes.TagTableFolder, "PLC tags"), (ProjectTreeNodeTypes.TagTable, "Motors"));
        selector.Member = new CrossReferenceMemberSelectorInfo { Kind = CrossReferenceMemberKinds.UserConstant, Name = "START" };

        var report = CrossReferenceReader.Read(project, selector, CrossReferenceFilterNames.AllObjects);

        Assert.Equal("constantSource", Assert.Single(report.Sources).Name);
        Assert.Equal(CrossReferenceMemberKinds.UserConstant, report.Target.Member!.Kind);
        Assert.Equal("Start", report.Target.Member.Name);
        Assert.Equal(0, table.Tags.Items[0].CrossReferenceServiceRequests);
    }

    [Fact]
    public void EmptyContainerIsCompleteSuccess()
    {
        var (project, _) = Fixture();

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.UnusedObjects);

        Assert.True(report.IsComplete);
        Assert.Empty(report.Sources);
        Assert.Empty(report.Messages);
        Assert.Equal(0, report.OwnerQueryCount);
        Assert.Equal(0, report.SuccessfulOwnerQueryCount);
    }

    [Fact]
    public void Read_SuccessfulEmptyQueryIsCompleteZero()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service() });
        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.UnusedObjects);
        Assert.True(report.IsComplete);
        Assert.Empty(report.Sources);
        Assert.Empty(report.Messages);
        Assert.Equal(1, report.OwnerQueryCount);
        Assert.Equal(1, report.SuccessfulOwnerQueryCount);
        Assert.Equal(0, report.TotalSourceCount);
        Assert.Equal(0, report.TotalReferenceCount);
        Assert.Equal(0, report.TotalLocationCount);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("query")]
    [InlineData("service")]
    public void OwnersExistButNoneSucceedFails(string failure)
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(FailingOwner(failure));
        var error = Assert.Throws<WorkerOperationException>(() =>
            CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.DoesNotContain("private", error.Message);
        Assert.True(error.Message.Length < 300);
    }

    [Fact]
    public void ReferencedAsMapsNameAndType()
    {
        var (project, plc) = Fixture();
        var service = Service("source");
        var reference = new ReferenceObject { Name = "target" };
        reference.Locations.Items.Add(new Location { Name = "resolved", ReferencedAs = new PlcTag { Name = "Start" } });
        reference.Locations.Items.Add(new Location { Name = "none", ReferencedAs = null });
        service.Result.Sources.Items[0].References.Items.Add(reference);
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);

        var locations = Assert.Single(Assert.Single(report.Sources).References).Locations;
        Assert.Equal("Start", locations[0].ReferencedAs!.Name);
        Assert.Equal("PlcTag", locations[0].ReferencedAs!.TypeName);
        Assert.Null(locations[1].ReferencedAs);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void UnreadableReferencedAsNameKeepsTypeAndIsIncomplete()
    {
        var (project, plc) = Fixture();
        var service = Service("source");
        var reference = new ReferenceObject { Name = "target" };
        reference.Locations.Items.Add(new Location
        {
            Name = "kept",
            ReferencedAs = new PlcTag { NameFailure = new EngineeringException("private") }
        });
        service.Result.Sources.Items[0].References.Items.Add(reference);
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);

        var location = Assert.Single(Assert.Single(Assert.Single(report.Sources).References).Locations);
        Assert.Equal("kept", location.Name);
        Assert.Equal("PlcTag", location.ReferencedAs!.TypeName);
        Assert.Equal(string.Empty, location.ReferencedAs.Name);
        Assert.False(report.IsComplete);
        Assert.NotEmpty(report.Messages);
        Assert.DoesNotContain("private", string.Join("", report.Messages));
    }

    [Fact]
    public void AccessAndReferenceTypeUseClosedNames()
    {
        var (project, plc) = Fixture();
        var service = Service("source");
        var reference = new ReferenceObject { Name = "target" };
        reference.Locations.Items.Add(new Location { Access = Access.ReadWriteAndSymbol, ReferenceType = ReferenceType.UsedBy });
        reference.Locations.Items.Add(new Location { Access = Access.CreateReferenceAndSymbol, ReferenceType = ReferenceType.Scope });
        reference.Locations.Items.Add(new Location { Access = (Access)999, ReferenceType = (ReferenceType)999 });
        service.Result.Sources.Items[0].References.Items.Add(reference);
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });

        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);

        var locations = Assert.Single(Assert.Single(report.Sources).References).Locations;
        Assert.Equal(new[] { "ReadWriteAndSymbol", "CreateReferenceAndSymbol", "Unknown" }, locations.Select(l => l.Access));
        Assert.Equal(new[] { "UsedBy", "Scope", "Unknown" }, locations.Select(l => l.ReferenceType));
        Assert.All(locations, l => Assert.Contains(l.Access, CrossReferenceAccessNames.All));
        Assert.All(locations, l => Assert.Contains(l.ReferenceType, CrossReferenceTypeNames.All));
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("query")]
    [InlineData("service")]
    [InlineData("rootProjection")]
    [InlineData("targetProjection")]
    [InlineData("childProjection")]
    [InlineData("sourceEnumeration")]
    [InlineData("ownerEnumeration")]
    public void Read_PartialCoverageRetainsDataAndSanitizesWarnings(string failure)
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("retained") });
        if (failure == "ownerEnumeration")
            plc.BlockGroup.Blocks.EnumerationFailure = new EngineeringException("private");
        else if (failure is "unavailable" or "query" or "service")
            plc.BlockGroup.Blocks.Items.Add(FailingOwner(failure));
        else
        {
            var service = Service("projected");
            var source = service.Result.Sources.Items[0];
            if (failure == "rootProjection") source.TypeNameFailure = new EngineeringException("private");
            if (failure == "targetProjection")
                source.References.Items.Add(new ReferenceObject { TypeNameFailure = new EngineeringException("private") });
            if (failure == "childProjection")
                source.Children.Items.Add(new SourceObject { TypeNameFailure = new EngineeringException("private") });
            if (failure == "sourceEnumeration") service.Result.Sources.EnumerationFailure = new EngineeringException("private");
            plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });
        }
        var (report, warning) = ReadCapturingWarnings(project, CrossReferenceFilterNames.UnusedObjects);
        Assert.Contains(report.Sources, s => s.Name == "retained");
        Assert.False(report.IsComplete);
        Assert.NotEmpty(report.Messages);
        Assert.DoesNotContain("private", string.Join("", report.Messages));
        Assert.DoesNotContain("private", warning);
        Assert.Single(warning.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(warning.Length < 300);
        Assert.Equal(failure == "ownerEnumeration" ? 1 : 2, report.OwnerQueryCount);
        Assert.Equal(failure is "unavailable" or "query" or "service" or "ownerEnumeration" ? 1 : 2,
            report.SuccessfulOwnerQueryCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Read_MaxResultsIsReportWideRootLimit(int maximum, bool complete)
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("first") });
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("second") });
        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects, maximum);
        Assert.Equal(maximum, report.TotalSourceCount);
        Assert.Equal(complete, report.IsComplete);
        Assert.Equal(2, report.SuccessfulOwnerQueryCount);
    }

    [Fact]
    public void Read_ManyFailedOwnersHaveBoundedSanitizedMessages()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service() });
        for (var i = 0; i < 250; i++) plc.BlockGroup.Blocks.Items.Add(FailingOwner("query"));
        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);
        Assert.Equal(251, report.OwnerQueryCount);
        Assert.Equal(1, report.SuccessfulOwnerQueryCount);
        Assert.False(report.IsComplete);
        Assert.InRange(string.Join("", report.Messages).Length, 1, 1024);
        Assert.DoesNotContain("private", string.Join("", report.Messages));
    }

    [Theory]
    [InlineData("session", false)]
    [InlineData("io", false)]
    [InlineData("cancel", false)]
    [InlineData("session", true)]
    [InlineData("io", true)]
    [InlineData("cancel", true)]
    public void Read_InfrastructureFailurePropagates(string kind, bool serviceAccess)
    {
        Exception failure = kind switch
        {
            "session" => new NonRecoverableException("session"),
            "io" => new IOException("I/O"),
            _ => new OperationCanceledException("cancel")
        };
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC
        {
            CrossReferenceServiceFailure = serviceAccess ? failure : null,
            CrossReferenceService = new CrossReferenceService { Failure = serviceAccess ? null : failure }
        });
        Assert.Same(failure, Assert.ThrowsAny<Exception>(() =>
            CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects)));
    }

    [Theory]
    [InlineData(CrossReferenceFilterNames.AllObjects, CrossReferenceFilter.AllObjects)]
    [InlineData(CrossReferenceFilterNames.ObjectsWithReferences, CrossReferenceFilter.ObjectsWithReferences)]
    [InlineData(CrossReferenceFilterNames.ObjectsWithoutReferences, CrossReferenceFilter.ObjectsWithoutReferences)]
    [InlineData(CrossReferenceFilterNames.UnusedObjects, CrossReferenceFilter.UnusedObjects)]
    public void Read_ForwardsEveryFilterUnchanged(string name, CrossReferenceFilter expected)
    {
        var (project, plc) = Fixture();
        var service = Service();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });
        var report = CrossReferenceReader.Read(project, PlcPath, name);
        Assert.Equal(name, report.Filter);
        Assert.Equal(expected, Assert.Single(service.Queries));
    }

    [Fact]
    public void Read_TraversalFailureRetainsIndependentBranchesAndMarksIncomplete()
    {
        var (project, plc) = Fixture();
        plc.BlockGroupFailure = new EngineeringException("private");
        var table = new PlcTagTable();
        table.Tags.Items.Add(new PlcTag { CrossReferenceService = Service("tag") });
        plc.TagTableGroup.TagTables.Items.Add(table);
        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);
        Assert.Equal("tag", Assert.Single(report.Sources).Name);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void Read_DoesNotDeduplicateRootsFromDifferentOwners()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("same") });
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("same") });
        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects);
        Assert.Equal(2, report.TotalSourceCount);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void Read_StopsEachRootEnumerationAtFirstOmittedResult()
    {
        var (project, plc) = Fixture();
        var service = Service("retained", "omitted", "mustNotEnumerate");
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });
        var report = CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects, 1);
        Assert.Equal(1, report.TotalSourceCount);
        Assert.False(report.IsComplete);
        Assert.Equal(2, service.Result.Sources.YieldedItemCount);
    }

    [Fact]
    public void Read_DeviceContainerSweepsEveryPlcInTheDevice()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("first") });
        var second = new PlcSoftware { Name = "PLC_2" };
        second.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("second") });
        project.Devices.Items[0].DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = second } });

        var report = CrossReferenceReader.Read(project, Path((ProjectTreeNodeTypes.Device, "Station_1")),
            CrossReferenceFilterNames.AllObjects);

        Assert.Equal(new[] { "first", "second" }, report.Sources.Select(s => s.Name));
        Assert.True(report.IsComplete);
    }

    [Theory]
    [InlineData("service")]
    [InlineData("query")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("location")]
    public void Read_ExactInvalidOperationRetainsPartialDataAndSanitizesDiagnostics(string stage)
    {
        const string privateDetail = @"C:\private\fixture.ap21: adapter detail";
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("retained") });
        plc.BlockGroup.Blocks.Items.Add(AdapterFailureOwner(stage, new InvalidOperationException(privateDetail)));
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("later") });
        var (report, warning) = ReadCapturingWarnings(project, CrossReferenceFilterNames.UnusedObjects);

        Assert.Contains(report.Sources, s => s.Name == "retained");
        Assert.Contains(report.Sources, s => s.Name == "later");
        Assert.Equal(3, report.OwnerQueryCount);
        Assert.Equal(stage is "service" or "query" ? 2 : 3, report.SuccessfulOwnerQueryCount);
        Assert.Equal(stage is "target" or "location" ? 3 : 2, report.TotalSourceCount);
        Assert.Equal(stage == "location" ? 1 : 0, report.TotalReferenceCount);
        Assert.Equal(0, report.TotalLocationCount);
        Assert.False(report.IsComplete);
        Assert.InRange(Assert.Single(report.Messages).Length, 1, 300);
        Assert.DoesNotContain(privateDetail, report.Messages[0]);
        Assert.DoesNotContain("private", report.Messages[0]);
        Assert.Single(warning.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.InRange(warning.Length, 1, 300);
        Assert.DoesNotContain("private", warning);
        Assert.DoesNotContain("adapter detail", warning);
    }

    public static IEnumerable<object[]> UnexpectedAdapterFailures()
    {
        foreach (var stage in new[] { "service", "query", "source", "target", "location" })
            foreach (var kind in new[] { "session", "derivedInvalidOperation", "io", "cancel", "unexpected" })
                yield return new object[] { stage, kind };
    }

    [Theory]
    [MemberData(nameof(UnexpectedAdapterFailures))]
    public void Read_UnexpectedAdapterFailurePropagatesEvenAfterEarlierSuccess(string stage, string kind)
    {
        Exception failure = kind switch
        {
            "session" => new NonRecoverableException("session lost"),
            "derivedInvalidOperation" => new ObjectDisposedException("disposed adapter"),
            "io" => new IOException("transport lost"),
            "cancel" => new OperationCanceledException("interrupted"),
            _ => new FormatException("unexpected adapter fault")
        };
        var (project, plc) = Fixture();
        var first = Service("retained-before-fault");
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = first });
        plc.BlockGroup.Blocks.Items.Add(AdapterFailureOwner(stage, failure));

        var propagated = Assert.ThrowsAny<Exception>(() =>
            CrossReferenceReader.Read(project, PlcPath, CrossReferenceFilterNames.AllObjects));

        Assert.Same(failure, propagated);
        Assert.Single(first.Queries);
        Assert.Equal(1, first.Result.Sources.YieldedItemCount);
    }

    private static (CrossReferenceReport Report, string Warning) ReadCapturingWarnings(
        Siemens.Engineering.Project project, string filter)
    {
        var savedError = Console.Error;
        using var warning = new StringWriter();
        try
        {
            Console.SetError(warning);
            var report = CrossReferenceReader.Read(project, PlcPath, filter);
            return (report, warning.ToString());
        }
        finally { Console.SetError(savedError); }
    }

    private static FC AdapterFailureOwner(string stage, Exception failure)
    {
        var service = Service("partially-projected");
        var owner = new FC { CrossReferenceService = service };
        if (stage == "service") owner.CrossReferenceServiceFailure = failure;
        else if (stage == "query") service.Failure = failure;
        else
        {
            var source = service.Result.Sources.Items[0];
            if (stage == "source") source.TypeNameFailure = failure;
            else
            {
                var target = new ReferenceObject { Name = "target" };
                source.References.Items.Add(target);
                if (stage == "target") target.TypeNameFailure = failure;
                else if (stage == "location")
                    target.Locations.Items.Add(new Location { TypeNameFailure = failure });
                else throw new ArgumentOutOfRangeException(nameof(stage));
            }
        }
        return owner;
    }

    private static FC FailingOwner(string failure) => new()
    {
        Name = "private" + new string('x', 3000),
        CrossReferenceServiceFailure = failure == "service" ? new EngineeringException("private") : null,
        CrossReferenceService = failure == "unavailable" ? null : new CrossReferenceService
        {
            Failure = new EngineeringException("private")
        }
    };

    private static (Siemens.Engineering.Project Project, PlcSoftware Software) Fixture()
    {
        var project = new Siemens.Engineering.Project();
        var software = new PlcSoftware { Name = "PLC_DP" };
        var device = new Device { Name = "Station_1" };
        device.DeviceItems.Items.Add(new DeviceItem { Container = new SoftwareContainer { Software = software } });
        project.Devices.Items.Add(device);
        return (project, software);
    }

    private static CrossReferenceSelectorInfo Path(params (string NodeType, string Name)[] segments)
        => CrossReferenceTargetResolverTests.Selector(segments);

    private static CrossReferenceService Service(params string[] names)
    {
        var service = new CrossReferenceService();
        foreach (var name in names) service.Result.Sources.Items.Add(new SourceObject { Name = name });
        return service;
    }
}

[CollectionDefinition("Cross-reference console", DisableParallelization = true)]
public class CrossReferenceConsoleCollection { }
