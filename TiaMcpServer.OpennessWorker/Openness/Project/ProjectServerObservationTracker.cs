using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Endpoint observations from explicit operations; never associates an active owner.</summary>
internal sealed class ProjectServerObservationTracker
{
    private readonly Dictionary<string, (string Host, int Port, string State)> _states = new(StringComparer.Ordinal);
    private long _attachmentRevision = -1;

    public ProjectServerConnectionObservation Observe(MultiuserRemoteIdentity identity, long attachmentRevision,
        string state, string source)
    {
        EnsureAttachment(attachmentRevision);
        string? previous = null;
        if (_states.TryGetValue(identity.ServerAlias, out var old)
            && old.Host == identity.Host && old.Port == identity.Port)
            previous = old.State;
        _states[identity.ServerAlias] = (identity.Host!, identity.Port!.Value, state);
        return new ProjectServerConnectionObservation
        {
            State = state,
            PreviousState = previous,
            Transition = previous is not null && previous != state,
            ObservedAt = DateTimeOffset.UtcNow,
            ObservationSource = source
        };
    }

    public void RetainConfiguredEndpoints(IEnumerable<MultiuserRemoteIdentity> identities, long attachmentRevision)
    {
        EnsureAttachment(attachmentRevision);
        var current = identities.ToList();
        foreach (var alias in _states.Keys.ToArray())
        {
            var old = _states[alias];
            if (!current.Any(identity => identity.ServerAlias == alias
                && identity.Host == old.Host && identity.Port == old.Port))
                _states.Remove(alias);
        }
    }

    public void Reset()
    {
        _states.Clear();
        _attachmentRevision = -1;
    }

    private void EnsureAttachment(long revision)
    {
        if (_attachmentRevision == revision) return;
        _states.Clear();
        _attachmentRevision = revision;
    }
}
