using System.Diagnostics;

namespace TiaMcpServer.Diagnostics;

public sealed class ProcessEnumerationService : IProcessEnumerationService
{
    public static readonly ProcessEnumerationService Instance = new();

    public IReadOnlyList<ProcessInfo> ListProcesses()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<ProcessInfo>();
        }

        try
        {
            return Process.GetProcesses()
                .Select(p =>
                {
                    using (p)
                    {
                        try
                        {
                            return new ProcessInfo(p.ProcessName, p.Id);
                        }
                        catch
                        {
                            return null;
                        }
                    }
                })
                .OfType<ProcessInfo>()
                .ToList();
        }
        catch
        {
            return Array.Empty<ProcessInfo>();
        }
    }
}
