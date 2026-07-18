using TiaMcpServer.Diagnostics;

namespace TiaMcpServer.Worker;


public sealed record WorkerCandidate(string Path, bool Exists);

public sealed record WorkerLocationResult(
    bool Found,
    string? SelectedPath,
    IReadOnlyList<WorkerCandidate> Candidates);

/// <summary>
/// Resolves the TIA Openness worker executable. Extracted from
/// <see cref="OpennessWorkerClient"/> so the doctor command and the runtime share one
/// resolution path. The runtime throws on miss; the doctor command treats a miss as a
/// failed check.
/// </summary>
public static class OpennessWorkerLocator
{
    private const string WorkerExecutableName = "TiaMcpServer.OpennessWorker.exe";
    private const string WorkerFolderName = "openness-worker";

    public static WorkerLocationResult Locate(string baseDirectory, IFileSystemService fileSystem)
    {
        var candidates = new List<WorkerCandidate>();

        var packagedPath = Path.Combine(baseDirectory, WorkerFolderName, WorkerExecutableName);
        candidates.Add(new WorkerCandidate(packagedPath, fileSystem.FileExists(packagedPath)));
        if (fileSystem.FileExists(packagedPath))
        {
            return new WorkerLocationResult(true, packagedPath, candidates);
        }

        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidatePath = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.OpennessWorker",
                    "bin",
                    configuration,
                    "net48",
                    WorkerExecutableName);

                candidates.Add(new WorkerCandidate(candidatePath, fileSystem.FileExists(candidatePath)));
                if (fileSystem.FileExists(candidatePath))
                {
                    return new WorkerLocationResult(true, candidatePath, candidates);
                }
            }

            directory = directory.Parent;
        }

        return new WorkerLocationResult(false, null, candidates);
    }

    public static string LocateOrThrow(string baseDirectory, IFileSystemService fileSystem)
    {
        var result = Locate(baseDirectory, fileSystem);
        if (result.Found)
        {
            return result.SelectedPath!;
        }

        var inspected = string.Join("; ", result.Candidates.Select(c => c.Path));
        throw new FileNotFoundException(
            "TIA Openness worker executable was not found. Build the solution and ensure the openness-worker folder is beside the MCP server executable. Inspected: " + inspected,
            result.Candidates.FirstOrDefault()?.Path ?? WorkerExecutableName);
    }
}
