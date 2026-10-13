using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;
namespace TiaMcpServer.OpennessWorker.Openness.Hmi;
public static class HmiTagTableWriter
{
    private static HmiTagTableComposition Tables(HmiSoftware software, string? path)
    {
        var tables = software.TagTables;
        var groups = software.TagTableGroups;
        if (path is not null) foreach (var segment in path.Split('/'))
        {
            var group = groups.Find(segment) ?? throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag table group '{path}' was not found.");
            tables = group.TagTables;
            groups = group.Groups;
        }
        return tables;
    }
    public static HmiWriteOutcomeInfo Create(HmiSoftware software, HmiWriteRequestInfo write)
        => Run(software, write, "Create", tables => tables.Create(write.TableName!));
    public static HmiWriteOutcomeInfo Update(HmiSoftware software, HmiWriteRequestInfo write)
        => Run(software, write, "Name", tables => Find(software, write.TableName!).Name = write.NewName!);
    public static HmiWriteOutcomeInfo Delete(HmiSoftware software, HmiWriteRequestInfo write)
        => Run(software, write, "Delete", tables => Find(software, write.TableName!).Delete());
    private static HmiTagTable Find(HmiSoftware software, string name)
    {
        var matches = AllTables(software.TagTables, software.TagTableGroups)
            .Where(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) throw new WorkerOperationException(WorkerFailureCategories.TargetNotFound, $"Tag table '{name}' was not found.");
        if (matches.Count > 1) throw new WorkerOperationException(WorkerFailureCategories.TargetAmbiguous, $"Several tag tables are named '{name}'.");
        return matches[0];
    }
    private static IEnumerable<HmiTagTable> AllTables(IEnumerable<HmiTagTable> tables, IEnumerable<HmiTagTableGroup> groups)
    {
        foreach (var table in tables) yield return table;
        foreach (var group in groups) foreach (var table in AllTables(group.TagTables, group.Groups)) yield return table;
    }
    private static HmiWriteOutcomeInfo Run(HmiSoftware software, HmiWriteRequestInfo write, string property, Action<HmiTagTableComposition> action)
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = write.NewName ?? write.TableName! };
        try { var tables = Tables(software, write.GroupPath); HmiTagWriter.Set(outcome, property, () => action(tables)); return outcome; }
        catch (EngineeringException ex) { if (property == "Create") outcome.Cleanup = "notNeeded"; throw HmiTagWriter.Failure(ex, outcome); }
    }
}
