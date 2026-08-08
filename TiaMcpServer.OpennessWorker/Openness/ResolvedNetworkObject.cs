using Siemens.Engineering;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public sealed class ResolvedNetworkObject
{
    public ResolvedNetworkObject(
        string kind,
        object value,
        NetworkObjectSelectorInfo target,
        NetworkObjectEvidenceInfo evidence,
        IReadOnlyList<string>? messages = null)
    {
        Kind = kind;
        Value = value;
        EngineeringObject = (IEngineeringObject)value;
        Target = target;
        Evidence = evidence;
        Messages = messages ?? Array.Empty<string>();
    }

    public string Kind { get; }
    public object Value { get; }
    public IEngineeringObject EngineeringObject { get; }
    public NetworkObjectSelectorInfo Target { get; }
    public NetworkObjectEvidenceInfo Evidence { get; }
    public IReadOnlyList<string> Messages { get; }
}

public sealed class NetworkObjectSelectionResult
{
    private NetworkObjectSelectionResult(
        ResolvedNetworkObject? resolved,
        string? failureCategory,
        string? error)
    {
        Resolved = resolved;
        FailureCategory = failureCategory;
        Error = error;
    }

    public bool Success => Resolved is not null;
    public ResolvedNetworkObject? Resolved { get; }
    public string? FailureCategory { get; }
    public string? Error { get; }

    public static NetworkObjectSelectionResult Ok(ResolvedNetworkObject resolved)
        => new(resolved, null, null);

    public static NetworkObjectSelectionResult Fail(string failureCategory, string error)
        => new(null, failureCategory, error);
}
