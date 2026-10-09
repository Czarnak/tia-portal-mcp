using TiaMcpServer.Contracts.Network;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

internal sealed class HardwareDiscoveryEvidenceCapture
{
    private readonly Action<string> _reportDiagnostic;

    public HardwareDiscoveryEvidenceCapture(string scope, Action<string> reportDiagnostic)
    {
        _reportDiagnostic = reportDiagnostic;
        Evidence = new HardwareDiscoveryEvidenceInfo { Scope = scope, Complete = true };
    }

    public HardwareDiscoveryEvidenceInfo Evidence { get; }

    public void RecordFailure(string stage, string message)
    {
        Evidence.Complete = false;
        Evidence.Failures.Add(new HardwareDiscoveryFailureInfo { Stage = stage, Message = message });
        _reportDiagnostic(message);
    }

    public void Traverse<T>(Func<IEnumerable<T>> enumerate, Action<T> materialize,
        string enumerationStage, string materializationStage)
    {
        // Acquisition, GetEnumerator, MoveNext, Current and Dispose belong to enumeration.
        // A failed candidate materialization must not hide the remaining candidates.
        try
        {
            foreach (var candidate in enumerate())
            {
                try { materialize(candidate); }
                catch (Exception exception)
                {
                    RecordFailure(materializationStage, $"Hardware {materializationStage} failed: {exception.Message}");
                }
            }
        }
        catch (Exception exception)
        {
            RecordFailure(enumerationStage, $"Hardware {enumerationStage} failed: {exception.Message}");
        }
    }
}
