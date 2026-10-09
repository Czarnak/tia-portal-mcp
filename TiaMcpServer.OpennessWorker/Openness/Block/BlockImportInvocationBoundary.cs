namespace TiaMcpServer.OpennessWorker.Openness.Block;

internal enum BlockImportReturnedState
{
    Success,
    NonSuccess
}

internal sealed class BlockImportInvocationSnapshot
{
    public BlockImportInvocationSnapshot(
        string importStage,
        string importResultState,
        bool? targetMutationCommitted)
    {
        ImportStage = importStage;
        ImportResultState = importResultState;
        TargetMutationCommitted = targetMutationCommitted;
    }

    public string ImportStage { get; }
    public string ImportResultState { get; }
    public bool? TargetMutationCommitted { get; }
}

internal sealed class BlockImportInvocationBoundary
{
    private bool _started;
    private bool _returned;
    private BlockImportReturnedState? _result;

    public void BeforeSiemensCall()
    {
        if (_started)
            throw new InvalidOperationException("The Siemens target call was already started.");
        _started = true;
    }

    public void AfterSiemensCallReturned()
    {
        if (!_started || _returned)
            throw new InvalidOperationException("The Siemens target call return marker is out of order.");
        _returned = true;
    }

    public void RecordReturnedResult(BlockImportReturnedState result)
    {
        if (!_returned || _result.HasValue)
            throw new InvalidOperationException("The Siemens target result marker is out of order.");
        _result = result;
    }

    public BlockImportInvocationSnapshot Snapshot()
    {
        if (_returned && !_result.HasValue)
            throw new InvalidOperationException("A returned Siemens target call has no closed result.");

        if (!_started)
            return new BlockImportInvocationSnapshot("not_started", "unavailable", false);
        if (!_returned)
            return new BlockImportInvocationSnapshot("unknown", "unavailable", null);

        return new BlockImportInvocationSnapshot(
            "completed",
            _result == BlockImportReturnedState.Success ? "success" : "non_success",
            true);
    }
}
