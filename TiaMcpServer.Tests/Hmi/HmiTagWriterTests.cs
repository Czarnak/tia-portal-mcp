using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;
namespace TiaMcpServer.Tests.Hmi;
[Collection(HmiSnapshotStoreCollection.Name)]
public class HmiTagWriterTests
{
    private static HmiWriteRequestInfo Request() => new() { HmiName = "Runtime", TagName = "T", TableName = "Table" };
    [Fact] public void CreateTag_SetsInSpecOrder()
    {
        var software = new HmiSoftware();
        var request = Request(); request.Connection = "Link"; request.PlcTag = "DB.Member"; request.AcquisitionCycle = "T1s";
        request.Comment = new() { new("en-US", "Comment") }; request.DisplayName = new() { new("en-US", "Display") };
        var result = HmiTagWriter.Create(software, request);
        Assert.Equal(new[] { "Create", "Connection", "PlcTag", "AcquisitionCycle", "Comment", "DisplayName" }, result.AppliedProperties);
        Assert.Equal(new[] { "Connection", "PlcTag", "AcquisitionCycle" }, software.Tags.Find("T")!.Writes);
        Assert.Equal("Comment", software.Tags.Find("T")!.Comment.Items.Single().Text);
    }
    [Fact] public void CreateTag_InternalSetsDataType()
    {
        var software = new HmiSoftware(); var request = Request(); request.DataType = "Bool";
        var result = HmiTagWriter.Create(software, request);
        Assert.Equal(new[] { "Create", "DataType" }, result.AppliedProperties);
        Assert.Equal("Bool", software.Tags.Find("T")!.DataType);
        Assert.DoesNotContain("Connection", software.Tags.Find("T")!.Writes);
    }
    [Fact] public void UpdateTag_RenamesFirst()
    {
        var software = new HmiSoftware(); var tag = software.Tags.Create("T", "Table");
        tag.SetFailures["Connection"] = new EngineeringException("Rejected");
        var request = Request(); request.NewName = "Renamed"; request.Connection = "Link";
        var error = Assert.Throws<WorkerOperationException>(() => HmiTagWriter.Update(software, request));
        Assert.Equal("Renamed", tag.Name); Assert.Equal(new[] { "Name" }, error.HmiWriteOutcome!.AppliedProperties);
        Assert.Equal("Connection", error.HmiWriteOutcome.FailedProperty);
    }
    [Theory]
    [InlineData("deleted")]
    [InlineData("notNeeded")]
    [InlineData("failed")]
    [InlineData("identity")]
    public void CreateTag_PlcTagRejected_CleansOnlyCreatedObject(string cleanup)
    {
        var software = new HmiSoftware(); var replacement = new HmiTag { Name = "T" }; var deletedReplacement = false;
        replacement.DeleteAction = () => deletedReplacement = true;
        software.Tags.Created = tag =>
        {
            tag.SetFailures["PlcTag"] = new EngineeringException("PLC tag rejected");
            if (cleanup == "notNeeded") software.Tags.FindOverride = _ => null;
            if (cleanup == "failed") tag.DeleteAction = () => throw new EngineeringException("Cleanup rejected");
            if (cleanup == "identity") software.Tags.FindOverride = _ => replacement;
        };
        var request = Request(); request.Connection = "Link"; request.PlcTag = "Missing";
        var error = Assert.Throws<WorkerOperationException>(() => HmiTagWriter.Create(software, request));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.Equal("PlcTag", error.HmiWriteOutcome!.FailedProperty);
        Assert.Equal(cleanup == "identity" ? "failed" : cleanup, error.HmiWriteOutcome.Cleanup);
        Assert.Equal(new[] { "Create", "Connection" }, error.HmiWriteOutcome.AppliedProperties);
        Assert.False(deletedReplacement);
        if (cleanup == "deleted") Assert.Empty(software.Tags.Items);
        if (cleanup is "failed" or "identity") Assert.NotNull(error.HmiWriteOutcome.CleanupMessage);
    }
    [Fact] public void CreateCollision_DoesNotDeleteExistingTag()
    {
        var software = new HmiSoftware(); var existing = software.Tags.Create("T", "Table");
        var error = Assert.Throws<WorkerOperationException>(() => HmiTagWriter.Create(software, Request()));
        Assert.Same(existing, software.Tags.Find("T")); Assert.Equal("notNeeded", error.HmiWriteOutcome!.Cleanup);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void Write_DropsThatHmisSnapshots_EvenAfterPartialFailure(bool fails)
    {
        HmiSoftwareLocator.ResetCache();
        var software = Unified("Runtime"); var other = Unified("Other");
        var project = ProjectWith(DeviceWith("Panel", software), DeviceWith("OtherPanel", other));
        var id = HmiSnapshotStore.Shared.Put(project, software, "hmi_list_tags", new object[] { "T" });
        var retained = HmiSnapshotStore.Shared.Put(project, other, "hmi_list_tags", new object[] { "X" });
        if (fails) software.Tags.Created = tag => tag.SetFailures["PlcTag"] = new EngineeringException("Rejected");
        var request = Request(); request.PlcTag = "DB.Member";
        if (fails) Assert.Throws<WorkerOperationException>(() => HmiWriteDispatch.Write(project, "hmi_create_tag", request));
        else HmiWriteDispatch.Write(project, "hmi_create_tag", request);
        Assert.Throws<WorkerOperationException>(() => HmiSnapshotStore.Shared.Take(project, id, "hmi_list_tags"));
        Assert.Same(other, HmiSnapshotStore.Shared.Take(project, retained, "hmi_list_tags").Software);
    }
    [Fact] public void Delete_RemovesTag()
    {
        var software = new HmiSoftware(); software.Tags.Create("T", "Table");
        HmiTagWriter.Delete(software, Request()); Assert.Empty(software.Tags.Items);
    }
}
