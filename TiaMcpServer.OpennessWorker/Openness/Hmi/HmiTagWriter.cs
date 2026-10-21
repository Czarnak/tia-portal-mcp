using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.OpennessWorker.Openness.Hmi.HmiWriteRun;
namespace TiaMcpServer.OpennessWorker.Openness.Hmi;
public static class HmiTagWriter
{
    public static HmiWriteOutcomeInfo Create(HmiSoftware software, HmiWriteRequestInfo write)
        => HmiWriteRun.Create(write.TagName!, "tag",
            () => software.Tags.Create(write.TagName!, write.TableName!),
            () => software.Tags.Find(write.TagName!), tag => tag.Delete(),
            (tag, outcome) => Apply(tag, write, outcome));
    public static HmiWriteOutcomeInfo Update(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var tag = HmiTagLookup.FindUnique(software, write.TagName!);
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.NewName ?? write.TagName! };
        try
        {
            if (write.NewName is not null) Set(outcome, "Name", () => tag.Name = write.NewName);
            Apply(tag, write, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }
    public static HmiWriteOutcomeInfo Delete(HmiSoftware software, HmiWriteRequestInfo write)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.TagName! };
        var tag = HmiTagLookup.FindUnique(software, write.TagName!);
        try { Set(outcome, "Delete", tag.Delete); return outcome; }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException) { throw Failure(ex, outcome); }
    }
    private static void Apply(HmiTag tag, HmiWriteRequestInfo write, HmiWriteOutcomeInfo outcome)
    {
        if (write.Connection is not null) Set(outcome, "Connection", () => tag.Connection = write.Connection);
        if (write.PlcTag is not null) Set(outcome, "PlcTag", () => tag.PlcTag = write.PlcTag);
        if (write.DataType is not null) Set(outcome, "DataType", () => tag.DataType = write.DataType);
        if (write.AcquisitionCycle is not null) Set(outcome, "AcquisitionCycle", () => tag.AcquisitionCycle = write.AcquisitionCycle);
        if (write.Comment is not null) Set(outcome, "Comment", () => HmiTextMapper.Write(tag.Comment, write.Comment));
        if (write.DisplayName is not null) Set(outcome, "DisplayName", () => HmiTextMapper.Write(tag.DisplayName, write.DisplayName));
        outcome.FailedProperty = null;
    }
}
