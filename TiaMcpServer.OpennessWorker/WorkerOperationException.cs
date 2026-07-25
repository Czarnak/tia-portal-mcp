using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Thrown by worker-side validation and Siemens-call-site code to carry a closed failure
/// category (and any warnings gathered before the failure) up to <see cref="Program"/>'s
/// dedicated catch, so the resulting <see cref="WorkerResponse"/> is categorized without
/// <see cref="Program"/> inferring the category from free-text messages. Never thrown with
/// <see cref="WorkerFailureCategories.WorkerTimeout"/> or <see cref="WorkerFailureCategories.WorkerCrashed"/>:
/// those are host-only determinations made by observing the ABSENCE of a worker response, which
/// this exception — thrown BY a responding worker — can never represent.
/// </summary>
public sealed class WorkerOperationException : Exception
{
    /// <summary>One of <see cref="WorkerFailureCategories"/>'s approved values. Set once at construction, never mutated.</summary>
    public string FailureCategory { get; }

    /// <summary>Warnings gathered before the failure. Never null; empty when none were supplied.</summary>
    public IReadOnlyList<string> Warnings { get; }

    public WorkerOperationException(string failureCategory, string message, IReadOnlyList<string>? warnings = null)
        : base(message)
    {
        if (!WorkerFailureCategories.IsKnown(failureCategory))
        {
            throw new ArgumentException(
                $"'{failureCategory}' is not an approved WorkerFailureCategories value.",
                nameof(failureCategory));
        }

        FailureCategory = failureCategory;
        Warnings = warnings is null ? Array.Empty<string>() : new List<string>(warnings).AsReadOnly();
    }
}
