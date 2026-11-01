using Siemens.Engineering;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Vci;

/// <summary>
/// Records per-leaf failures on a <see cref="VciWriteOutcomeInfo"/> (capped list, exact counts stay with the caller)
/// and builds the item failure from the first recorded one, so a server-raised failure that comes first is never
/// relabelled as a Siemens rejection.
/// </summary>
internal sealed class VciFailureLog(VciWriteOutcomeInfo outcome)
{
    private Exception? _firstException;
    private string? _firstMessage;

    public bool HasFailure { get; private set; }

    public void Record(VciLastAttemptedInfo attempt, Exception ex)
    {
        var category = ex is EngineeringException siemens
            ? WorkerExecution.Classify(siemens, true) : WorkerFailureCategories.WorkerOperationFailed;
        Add(attempt, category, ex is EngineeringException ? ex.GetType().Name : null, ex.Message, ex);
    }

    public void Record(VciLastAttemptedInfo attempt, string message)
        => Add(attempt, WorkerFailureCategories.WorkerOperationFailed, null, message, null);

    public WorkerOperationException ItemFailure()
        => _firstException is not null ? Stopped(_firstException, outcome)
            : new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, _firstMessage!, vciWriteOutcome: outcome);

    public static WorkerOperationException Stopped(Exception ex, VciWriteOutcomeInfo outcome)
    {
        if (ex is not EngineeringException siemens)
            return new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed, ex.Message, vciWriteOutcome: outcome);
        var category = WorkerExecution.Classify(siemens, true);
        return new WorkerOperationException(category, WorkerExecution.FailureMessage(siemens, category),
            vciWriteOutcome: outcome, engineeringError: WorkerExecution.Describe(siemens));
    }

    private void Add(VciLastAttemptedInfo attempt, string category, string? type, string message, Exception? ex)
    {
        if (!HasFailure) { HasFailure = true; _firstException = ex; _firstMessage = message; }
        if (outcome.Failures.Count >= VciWriteOutcomeInfo.MaxFailures) { outcome.FailuresOmitted++; return; }
        outcome.Failures.Add(new VciLeafFailureInfo
        {
            Path = new List<TiaMcpServer.Contracts.Project.ProjectTreeSelectorSegment>(attempt.Path),
            Format = string.IsNullOrWhiteSpace(attempt.Format) ? null : attempt.Format,
            Category = category,
            EngineeringErrorType = type,
            Message = message.Length <= VciLeafFailureInfo.MaxMessageChars
                ? message : message.Substring(0, VciLeafFailureInfo.MaxMessageChars),
        });
    }
}
