using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.Scripts;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.VersionControl;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciSiemensDoublesTests
{
    [Fact]
    public void ExportRecordsArgumentsAndCreatesAnAbsoluteMapping()
    {
        var service = new VersionControlInterface();
        var workspace = service.WorkspaceGroup.Workspaces.Create("FixtureWorkspace", new DirectoryInfo(Path.GetTempPath()));
        var leaf = new FC { Name = "FixtureLeaf" };
        workspace.SupportedFormats[leaf] = new[] { "SimaticML", "SCL" };
        Assert.Equal(new[] { "SimaticML", "SCL" }, workspace.GetSupportedFileFormats(leaf));

        var mapping = workspace.ExportObject(leaf, new DirectoryInfo(@"\FixtureFolder"), leaf.Name, "SCL");

        Assert.Same(mapping, workspace.MappedObjects.Find(leaf));
        Assert.Equal(Path.Combine(workspace.RootPath.FullName, "FixtureFolder"), mapping.DirectoryPath.FullName);
        Assert.Equal("SCL", mapping.FileFormat);
        Assert.Equal(leaf.Name, mapping.FileNameWithoutExtension);
        Assert.Same(leaf, Assert.Single(workspace.ExportCalls).Object);
        Assert.Same(leaf, Assert.Single(workspace.SupportedFormatCalls));
        Assert.Same(workspace, mapping.Parent);
        Assert.Same(workspace, service.WorkspaceGroup.Workspaces.Find(workspace.Name));
    }

    [Fact]
    public void LeafFailureAndPortalLossRecordOnlyAttemptedCalls()
    {
        var workspace = new Workspace();
        var leaf = new FC { Name = "FixtureLeaf" };
        var failure = new EngineeringException("Synthetic leaf failure.");
        workspace.ThrowOnLeaf[leaf] = failure;
        Assert.Same(failure, Assert.Throws<EngineeringException>(() =>
            workspace.ExportObject(leaf, new DirectoryInfo(@"\FixtureFolder"), leaf.Name, "SimaticML")));
        Assert.Empty(workspace.MappedObjects);
        Assert.Single(workspace.ExportCalls);

        workspace.NonRecoverableOnLeaf.Add(leaf);
        Assert.Throws<NonRecoverableException>(() => workspace.GetSupportedFileFormats(leaf));
        Assert.Single(workspace.SupportedFormatCalls);
    }

    [Fact]
    public void MappingsRecordFreshStatusSynchronizationAndDeletion()
    {
        var workspace = new Workspace();
        var mapping = workspace.ExportObject(new FC(), new DirectoryInfo(@"\FixtureFolder"), "FixtureLeaf", "SimaticML");
        mapping.StatusResult = new IndividualObjectCompareResult
        {
            CompareState = CompareState.Unequal,
            IndividualObjectCompareDetails = IndividualObjectCompareDetails.WorkspaceFileChanged
        };
        Assert.Equal(CompareState.Unequal, mapping.GetStatus().CompareState);
        mapping.Synchronize(SynchronizationMode.WorkspaceToProject);
        Assert.Equal(CompareState.Equal, mapping.GetStatus().CompareState);
        Assert.Equal(2, mapping.GetStatusCalls);
        Assert.Equal(SynchronizationMode.WorkspaceToProject, Assert.Single(mapping.SynchronizeCalls));
        mapping.Delete();
        Assert.Equal(1, mapping.DeleteCalls);
        Assert.Empty(workspace.MappedObjects);
    }

    [Fact]
    public void MappingLookupUsesObjectEquality()
    {
        var mappings = new MappedObjectComposition();
        var mapping = new MappedObject { EngineeringObject = new EqualLeaf { Name = "FixtureLeaf" } };
        mappings.Items.Add(mapping);
        Assert.Same(mapping, mappings.Find(new EqualLeaf { Name = "FixtureLeaf" }));
    }

    [Fact]
    public void ProjectServiceAndTransactionHooksAcceptTheOfflineProject()
    {
        var project = new Siemens.Engineering.Project { VersionControlInterface = new VersionControlInterface() };
        Assert.Same(project.VersionControlInterface, project.GetService<VersionControlInterface>());
        using var portal = new TiaPortal();
        using var access = portal.ExclusiveAccess("Fixture import");
        var transaction = access.Transaction(project, "Fixture transaction");
        Assert.Same(project, transaction.Project);
        transaction.CanCommit = false;
        Assert.Throws<EngineeringException>(() => transaction.CommitOnDispose());
        transaction.Dispose();
        Assert.False(transaction.CommitRequested);
        Assert.False(transaction.Committed);
        portal.TransactionCanCommit = true;
        var committed = access.Transaction(project, "Fixture commit");
        committed.CommitOnDispose();
        committed.Dispose();
        Assert.True(committed.Committed);
        Assert.Equal(2, portal.Transactions.Count);
        Assert.Single(portal.ExclusiveAccessCalls);
    }

    [Fact]
    public void PlcTechnologyAndHmiLeavesHaveReachableParentChains()
    {
        var device = new Device { Name = "FixtureDevice" };
        var item = new DeviceItem { Name = "FixtureItem" };
        device.DeviceItems.Items.Add(item);
        Assert.Same(item, Assert.Single(device.DeviceItems));
        var software = new PlcSoftware { Name = "FixtureSoftware", Parent = item };
        var group = new TechnologicalInstanceDBUserGroup { Name = "FixtureGroup" };
        var leaf = new TechnologicalInstanceDB { Name = "FixtureTechnologyLeaf" };
        software.TechnologicalObjectGroup.Groups.Items.Add(group);
        group.TechnologicalObjects.Items.Add(leaf);
        Assert.Same(group, Assert.Single(software.TechnologicalObjectGroup.Groups));
        Assert.IsAssignableFrom<InstanceDB>(Assert.Single(group.TechnologicalObjects));
        Assert.Same(group, leaf.Parent);
        Assert.Same(software.TechnologicalObjectGroup, group.Parent);
        Assert.Same(software, group.Parent!.Parent);
        Assert.Same(device, software.Parent!.Parent);

        var hmi = new HmiSoftware { Parent = item };
        var script = new HmiScriptModule { Name = "FixtureScript" };
        hmi.Scripts.Items.Add(script);
        Assert.Same(script, Assert.Single(hmi.Scripts));
        Assert.Same(hmi, script.Parent);
        leaf.ParentFailure = new EngineeringObjectDisposedException("Synthetic deleted leaf.");
        Assert.Throws<EngineeringObjectDisposedException>(() => leaf.Parent);
    }

    private sealed class EqualLeaf : NamedObject
    {
        public override bool Equals(object? other) => other is EqualLeaf leaf && leaf.Name == Name;
        public override int GetHashCode() => Name.GetHashCode();
    }
}
