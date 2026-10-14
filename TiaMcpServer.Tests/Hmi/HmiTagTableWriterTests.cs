using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;
namespace TiaMcpServer.Tests.Hmi;
public class HmiTagTableWriterTests
{
    [Fact] public void Table_CreateInGroup_Rename_Delete()
    {
        var software = new HmiSoftware(); var group = new HmiTagTableGroup { Name = "Group" }; software.TagTableGroups.Items.Add(group);
        var request = new HmiWriteRequestInfo { TableName = "Table", GroupPath = "Group" };
        HmiTagTableWriter.Create(software, request); Assert.Single(group.TagTables.Items); Assert.Empty(software.TagTables.Items);
        request.GroupPath = null; request.NewName = "Renamed"; HmiTagTableWriter.Update(software, request); Assert.Equal("Renamed", group.TagTables.Items.Single().Name);
        request.TableName = "Renamed"; request.NewName = null; HmiTagTableWriter.Delete(software, request); Assert.Empty(group.TagTables.Items);
    }
    [Fact] public void DefaultTableDelete_ReportsOpennessMessage()
    {
        var software = new HmiSoftware(); var table = software.TagTables.Create("Default");
        table.DeleteAction = () => throw new EngineeringException("Hmi tag table could not be deleted");
        var error = Assert.Throws<WorkerOperationException>(() => HmiTagTableWriter.Delete(software, new() { TableName = "Default" }));
        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, error.FailureCategory);
        Assert.Equal("Hmi tag table could not be deleted", error.Message); Assert.Equal("Delete", error.HmiWriteOutcome!.FailedProperty);
    }
}
