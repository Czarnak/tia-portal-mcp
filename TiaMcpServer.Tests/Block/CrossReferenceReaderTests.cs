using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

[Collection("Cross-reference console")]
public class CrossReferenceReaderTests
{
    [Fact]
    public void Harness_PreservesNestedProjectionAndFilter()
    {
        var (project, plc) = Fixture();
        var service = Service("root");
        var root = service.Result.Sources.Items[0];
        var child = new SourceObject { Name = "child" };
        var reference = new ReferenceObject { Name = "target", Path = "target/path" };
        reference.Locations.Items.Add(new Location { Name = "network", Access = "Read", ReferencedAsName = "tag" });
        child.References.Items.Add(reference);
        root.Children.Items.Add(child);
        // Same fixture characterizes projection before and after owner discovery changes.
        plc.CrossReferenceService = service;
        plc.BlockGroup.Blocks.Items.Add(new FC { Name = "owner", CrossReferenceService = service });

        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.UnusedObjects);

        var source = Assert.Single(Assert.Single(report.Plcs).Sources);
        Assert.Equal("root", source.Name);
        Assert.Equal("target/path", Assert.Single(Assert.Single(source.Children).References).Path);
        Assert.Equal(2, report.TotalSourceCount);
        Assert.Equal(1, report.TotalReferenceCount);
        Assert.Equal(1, report.TotalLocationCount);
        Assert.Equal(CrossReferenceFilter.UnusedObjects, Assert.Single(service.Queries));
    }

    [Fact]
    public void Read_QueriesOnlySupportedOwnersAcrossGroupsAndUnits()
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
        var nested = new PlcBlockGroup();
        plc.BlockGroup.Groups.Items.Add(new PlcBlockGroup { Groups = { Items = { nested } } });
        nested.Blocks.Items.Add(Owner(new FB(), "nestedFB"));
        nested.Blocks.Items.Add(Owner(new FC(), "nestedFC"));
        var system = new PlcSystemBlockGroup();
        plc.BlockGroup.SystemBlockGroups.Items.Add(new PlcSystemBlockGroup { Groups = { Items = { system } } });
        system.Blocks.Items.Add(Owner(new GlobalDB(), "systemDB"));
        system.Blocks.Items.Add(Owner(new InstanceDB(), "systemIDB"));
        var table = new PlcTagTable { Name = "table", CrossReferenceService = Service("forbiddenTable") };
        var userConstant = new PlcUserConstant { CrossReferenceService = Service("forbiddenUserConstant") };
        table.UserConstants.Items.Add(userConstant);
        table.Tags.Items.Add(Owner(new PlcTag(), "tag"));
        table.SystemConstants.Items.Add(Owner(new PlcSystemConstant(), "systemConstant"));
        plc.TagTableGroup.Groups.Items.Add(new PlcTagTableGroup { TagTables = { Items = { table } } });
        plc.TypeGroup.Groups.Items.Add(new PlcTypeGroup { Types = { Items = { Owner(new PlcType(), "nestedUDT") } } });
        var unit = new PlcUnit { Name = "Unit1" };
        plc.UnitProvider = new PlcUnitProvider();
        plc.UnitProvider.UnitGroup.Units.Items.Add(unit);
        unit.BlockGroup.Blocks.Items.Add(Owner(new ArrayDB(), "unitDB"));
        unit.TypeGroup.Types.Items.Add(Owner(new PlcType(), "unitUDT"));
        var unitTable = new PlcTagTable();
        unitTable.Tags.Items.Add(Owner(new PlcTag(), "unitTag"));
        unitTable.SystemConstants.Items.Add(Owner(new PlcSystemConstant(), "unitConstant"));
        unit.TagTableGroup.TagTables.Items.Add(unitTable);
        plc.CrossReferenceService = Service("forbiddenSoftware");

        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects);

        var result = Assert.Single(report.Plcs);
        Assert.Equal(owners.Select(o => o.Name).OrderBy(n => n), result.Sources.Select(s => s.Name).OrderBy(n => n));
        Assert.Equal(owners.Count, result.OwnerQueryCount);
        Assert.Equal(owners.Count, result.SuccessfulOwnerQueryCount);
        Assert.All(owners, o => Assert.Equal(1, o.CrossReferenceServiceRequests));
        Assert.Equal(0, plc.CrossReferenceServiceRequests);
        Assert.Equal(0, table.CrossReferenceServiceRequests);
        Assert.Equal(0, userConstant.CrossReferenceServiceRequests);
        Assert.All(owners, o => Assert.Equal(CrossReferenceFilter.AllObjects, Assert.Single(o.CrossReferenceService!.Queries)));
    }

    [Fact]
    public void Read_SuccessfulEmptyQueryIsCompleteZero()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service() });
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.UnusedObjects);
        var result = Assert.Single(report.Plcs);
        Assert.True(report.IsComplete);
        Assert.True(result.IsComplete);
        Assert.Empty(result.Sources);
        Assert.Empty(result.Messages);
        Assert.Equal(1, result.OwnerQueryCount);
        Assert.Equal(1, result.SuccessfulOwnerQueryCount);
        Assert.Equal(0, report.TotalSourceCount);
        Assert.Equal(0, report.TotalReferenceCount);
        Assert.Equal(0, report.TotalLocationCount);
    }

    [Theory]
    [InlineData("noOwners")]
    [InlineData("unavailable")]
    [InlineData("query")]
    [InlineData("service")]
    public void Read_NoSuccessfulQueriesFailsCategorically(string failure)
    {
        var (project, plc) = Fixture();
        if (failure != "noOwners") plc.BlockGroup.Blocks.Items.Add(FailingOwner(failure));
        var error = Assert.Throws<WorkerOperationException>(() =>
            CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.DoesNotContain("private", error.Message);
        Assert.True(error.Message.Length < 300);
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
        var savedError = Console.Error;
        using var warning = new StringWriter();
        CrossReferenceReport report;
        try
        {
            Console.SetError(warning);
            report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.UnusedObjects);
        }
        finally { Console.SetError(savedError); }
        var result = Assert.Single(report.Plcs);
        Assert.Contains(result.Sources, s => s.Name == "retained");
        Assert.False(report.IsComplete);
        Assert.False(result.IsComplete);
        Assert.NotEmpty(result.Messages);
        Assert.DoesNotContain("private", string.Join("", result.Messages));
        Assert.DoesNotContain("private", warning.ToString());
        Assert.Single(warning.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.True(warning.ToString().Length < 300);
        Assert.Equal(failure == "ownerEnumeration" ? 1 : 2, result.OwnerQueryCount);
        Assert.Equal(failure is "unavailable" or "query" or "service" or "ownerEnumeration" ? 1 : 2,
            result.SuccessfulOwnerQueryCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void Read_MaxResultsIsReportWideRootLimit(int maximum, bool complete)
    {
        var (project, first) = Fixture();
        var (secondProject, second) = Fixture();
        second.Name = "PLC_2";
        project.Devices.Items.Add(secondProject.Devices.Items[0]);
        first.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("first") });
        second.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("second") });
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects, maximum);
        Assert.Equal(maximum, report.TotalSourceCount);
        Assert.Equal(complete, report.IsComplete);
        Assert.Equal(complete, report.Plcs[1].IsComplete);
        Assert.All(report.Plcs, p => Assert.Equal(1, p.SuccessfulOwnerQueryCount));
    }

    [Fact]
    public void Read_OnePlcWithoutOwnersDoesNotEraseAnotherSuccessfulEmptyQuery()
    {
        var (project, first) = Fixture();
        var (other, _) = Fixture();
        project.Devices.Items.Add(other.Devices.Items[0]);
        first.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service() });
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects);
        Assert.False(report.IsComplete);
        Assert.True(report.Plcs[0].IsComplete);
        Assert.False(report.Plcs[1].IsComplete);
        Assert.Equal(0, report.Plcs[1].OwnerQueryCount);
        Assert.NotEmpty(report.Plcs[1].Messages);
    }

    [Fact]
    public void Read_ManyFailedOwnersHaveBoundedSanitizedMessages()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service() });
        for (var i = 0; i < 250; i++) plc.BlockGroup.Blocks.Items.Add(FailingOwner("query"));
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects);
        var result = Assert.Single(report.Plcs);
        Assert.Equal(251, result.OwnerQueryCount);
        Assert.Equal(1, result.SuccessfulOwnerQueryCount);
        Assert.False(result.IsComplete);
        Assert.InRange(string.Join("", result.Messages).Length, 1, 1024);
        Assert.DoesNotContain("private", string.Join("", result.Messages));
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
            CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects)));
    }

    [Theory]
    [InlineData("PLC_DP")]
    [InlineData("Station_1")]
    public void Read_ReportsSoftwareAndDeviceIdentityForEitherSelector(string selector)
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("source") });
        var result = Assert.Single(CrossReferenceReader.Read(project, selector, CrossReferenceFilterNames.AllObjects).Plcs);
        Assert.Equal("PLC_DP", result.PlcName);
        Assert.Equal("Station_1", result.DeviceName);
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
        var report = CrossReferenceReader.Read(project, null, name);
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
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects);
        Assert.Equal("tag", Assert.Single(Assert.Single(report.Plcs).Sources).Name);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void Read_DoesNotDeduplicateRootsFromDifferentOwners()
    {
        var (project, plc) = Fixture();
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("same") });
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = Service("same") });
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects);
        Assert.Equal(2, report.TotalSourceCount);
        Assert.True(report.IsComplete);
    }

    [Fact]
    public void Read_StopsEachRootEnumerationAtFirstOmittedResult()
    {
        var (project, plc) = Fixture();
        var service = Service("retained", "omitted", "mustNotEnumerate");
        plc.BlockGroup.Blocks.Items.Add(new FC { CrossReferenceService = service });
        var report = CrossReferenceReader.Read(project, null, CrossReferenceFilterNames.AllObjects, 1);
        Assert.Equal(1, report.TotalSourceCount);
        Assert.False(report.IsComplete);
        Assert.Equal(2, service.Result.Sources.YieldedItemCount);
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

    private static CrossReferenceService Service(params string[] names)
    {
        var service = new CrossReferenceService();
        foreach (var name in names) service.Result.Sources.Items.Add(new SourceObject { Name = name });
        return service;
    }
}

[CollectionDefinition("Cross-reference console", DisableParallelization = true)]
public class CrossReferenceConsoleCollection { }
