using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class HardwareDiscoveryEvidenceCapture
{
    public HardwareDiscoveryEvidenceCapture(string scope, Action<string> reportDiagnostic)
    {
        Evidence = new HardwareDiscoveryEvidenceInfo { Scope = scope, Complete = true };
    }

    public HardwareDiscoveryEvidenceInfo Evidence { get; }

    public void RecordFailure(string stage, string message) { }

    public void Traverse<T>(Func<IEnumerable<T>> enumerate, Action<T> materialize,
        string enumerationStage, string materializationStage) { }
}
