using System;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class BlockSourceArtifactTracker
{
    private readonly bool _sourceApplicable;
    private bool _createStarted;
    private bool _createReturned;
    private bool _deleteAttempted;
    private bool _removed;

    public BlockSourceArtifactTracker(bool sourceApplicable)
    {
        _sourceApplicable = sourceApplicable;
    }

    public void BeforeCreateFromFile()
    {
        EnsureApplicable();
        if (_createStarted)
            throw new InvalidOperationException("Temporary source creation was already started.");
        _createStarted = true;
    }

    public void AfterCreateFromFileReturned()
    {
        EnsureApplicable();
        if (!_createStarted || _createReturned)
            throw new InvalidOperationException("Temporary source creation return marker is out of order.");
        _createReturned = true;
    }

    public void AfterDeleteAttempt(bool removed)
    {
        EnsureApplicable();
        if (!_createReturned || _deleteAttempted)
            throw new InvalidOperationException("Temporary source deletion marker is out of order.");
        _deleteAttempted = true;
        _removed = removed;
    }

    public string Snapshot()
    {
        if (!_sourceApplicable)
            return "not_applicable";
        if (!_createStarted)
            return "not_created";
        if (!_createReturned || !_deleteAttempted)
            return "unknown";
        return _removed ? "removed" : "residue_possible";
    }

    private void EnsureApplicable()
    {
        if (!_sourceApplicable)
            throw new InvalidOperationException("Temporary source lifecycle is not applicable to XML import.");
    }
}
