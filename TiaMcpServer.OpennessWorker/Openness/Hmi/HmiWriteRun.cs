using Siemens.Engineering;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

internal static class HmiWriteRun
{
    internal static HmiWriteOutcomeInfo Create<T>(string objectName, string objectKind,
        Func<T> create, Func<T?> find, Action<T> delete, Action<T, HmiWriteOutcomeInfo> apply) where T : class
    {
        var outcome = new HmiWriteOutcomeInfo { ObjectName = objectName };
        T? created = null;
        try
        {
            outcome.FailedProperty = "Create";
            created = create();
            outcome.AppliedProperties.Add("Create");
            apply(created, outcome);
            return outcome;
        }
        catch (Exception ex) when (ex is EngineeringException or WorkerOperationException)
        {
            outcome.Cleanup = HmiCleanupStates.NotNeeded;
            if (created is not null)
            {
                try
                {
                    var found = find();
                    if (found is not null)
                    {
                        if (!object.Equals(found, created)) throw new InvalidOperationException($"Created {objectKind} identity changed; cleanup refused.");
                        delete(found);
                        outcome.Cleanup = HmiCleanupStates.Deleted;
                    }
                }
                catch (Exception cleanup) { outcome.Cleanup = HmiCleanupStates.Failed; outcome.CleanupMessage = cleanup.Message; }
            }
            throw Failure(ex, outcome);
        }
    }

    internal static void Set(HmiWriteOutcomeInfo outcome, string property, Action action)
    {
        outcome.FailedProperty = property;
        action();
        outcome.AppliedProperties.Add(property);
        outcome.FailedProperty = null;
    }

    internal static WorkerOperationException Failure(Exception ex, HmiWriteOutcomeInfo outcome)
        => new(ex is WorkerOperationException failure ? failure.FailureCategory : WorkerFailureCategories.WorkerOperationFailed,
            ex.Message, (ex as WorkerOperationException)?.Warnings, hmiWriteOutcome: outcome);
}
