using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class PortalInventoryEntry
{
    public PortalInventoryEntry(TiaPortalProcess process, TiaPortalProcessCandidate candidate)
    {
        Process = process;
        Candidate = candidate;
    }

    public TiaPortalProcess Process { get; }
    public TiaPortalProcessCandidate Candidate { get; }
}

internal static class TiaPortalProcessInventory
{
    public static IReadOnlyList<PortalInventoryEntry> Read()
        => TiaPortal.GetProcesses().Select(process => new PortalInventoryEntry(process,
            new TiaPortalProcessCandidate(process.Id, TryReadAdvertisedProjectPath(process), TryReadHasUserInterface(process))))
            .ToList();

    private static string? TryReadAdvertisedProjectPath(TiaPortalProcess process)
    {
        try { return process.ProjectPath?.FullName; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read advertised project path for TIA Portal PID {process.Id}: {ex.Message}");
            return null;
        }
    }

    internal static bool TryReadHasUserInterface(TiaPortalProcess process)
    {
        try { return process.Mode == TiaPortalMode.WithUserInterface; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read mode for TIA Portal PID {process.Id}: {ex.Message}");
            return false;
        }
    }
}
