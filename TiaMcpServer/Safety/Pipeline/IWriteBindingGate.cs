using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Safety;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Safety.Pipeline;

/// <summary>The outcome of running an operation under the pinned project-binding lease.</summary>
public sealed record WriteLeaseResult<T>(bool Success, T? Value, WriteToolError? Error)
    where T : class
{
    public static WriteLeaseResult<T> Ok(T value) => new(true, value, Error: null);

    public static WriteLeaseResult<T> Fail(WriteToolError error) => new(false, Value: null, error);
}

/// <summary>The access mode, verified-binding gate, and pinned lease a guarded write runs under.</summary>
public interface IWriteBindingGate
{
    McpAccessMode AccessMode { get; }

    ProjectBindingSnapshot CurrentBinding { get; }

    /// <summary>Fails closed unless the session holds a verified binding for <paramref name="projectPath"/>.</summary>
    Task<WorkerCallResult> RequireVerifiedWriteBindingAsync(string? projectPath);

    /// <summary>
    /// Runs <paramref name="operation"/> while holding the lease on <paramref name="binding"/>; a
    /// binding that no longer matches is refused without running the operation. Cancellation is
    /// observed before entry; implementations must never abandon a running mutation.
    /// </summary>
    Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(
        ProjectBindingSnapshot binding, Func<Task<T>> operation, CancellationToken cancellationToken = default)
        where T : class;
}

/// <summary>
/// <see cref="IWriteBindingGate"/> over <see cref="OpennessWorkerClient"/>: its access policy,
/// its verified-binding gate, and <c>ExecuteWithPinnedBindingAsync</c>.
/// </summary>
public sealed class OpennessWriteBindingGate : IWriteBindingGate
{
    private readonly OpennessWorkerClient _workerClient;

    public OpennessWriteBindingGate(OpennessWorkerClient workerClient)
    {
        _workerClient = workerClient ?? throw new ArgumentNullException(nameof(workerClient));
    }

    public McpAccessMode AccessMode => _workerClient.AccessPolicy.Mode;

    public ProjectBindingSnapshot CurrentBinding => _workerClient.BindingSnapshot;

    public Task<WorkerCallResult> RequireVerifiedWriteBindingAsync(string? projectPath)
        => _workerClient.RequireVerifiedBindingAsync(projectPath);

    public async Task<WriteLeaseResult<T>> RunUnderLeaseAsync<T>(
        ProjectBindingSnapshot binding,
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
        where T : class
    {
        var execution = await _workerClient
            .ExecuteWithPinnedBindingAsync(binding, operation, cancellationToken)
            .ConfigureAwait(false);
        return execution.Success
            ? WriteLeaseResult<T>.Ok(execution.Value!)
            : WriteLeaseResult<T>.Fail(new WriteToolError(
                WorkerFailureCategories.BindingConflict,
                execution.Failure?.Error ?? "The project binding changed before the write could run."));
    }
}
