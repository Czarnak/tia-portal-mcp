using TiaMcpServer.Worker;

namespace TiaMcpServer.Diagnostics.Checks;

public sealed class HostWorkerVersionCheck : IDiagnosticCheck
{
    private readonly IApplicationInfoService _appInfo;
    private readonly IFileSystemService _fileSystem;

    public HostWorkerVersionCheck(IApplicationInfoService appInfo, IFileSystemService fileSystem)
    {
        _appInfo = appInfo;
        _fileSystem = fileSystem;
    }

    public string Id => "host-worker-version";
    public string Name => "Host and worker compatibility";

    public DiagnosticCheckResult Run()
    {
        var evidence = Evidence.Empty();
        var hostVersion = _appInfo.HostVersion;
        evidence["hostVersion"] = hostVersion;

        var workerLocation = OpennessWorkerLocator.Locate(_appInfo.BaseDirectory, _fileSystem);
        if (!workerLocation.Found || workerLocation.SelectedPath is null)
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Warning,
                "Worker executable not found; host/worker compatibility could not be checked.",
                null,
                evidence);
        }

        var workerVersion = _fileSystem.GetFileVersion(workerLocation.SelectedPath);
        evidence["workerPath"] = workerLocation.SelectedPath;
        evidence["workerVersion"] = workerVersion;

        if (string.IsNullOrWhiteSpace(workerVersion))
        {
            return new DiagnosticCheckResult(
                Id,
                Name,
                DiagnosticStatus.Warning,
                "Worker file version could not be determined.",
                null,
                evidence);
        }

        if (TryParseMajor(hostVersion, out var hostMajor) && TryParseMajor(workerVersion, out var workerMajor))
        {
            evidence["hostMajor"] = hostMajor.ToString();
            evidence["workerMajor"] = workerMajor.ToString();

            if (hostMajor != workerMajor)
            {
                return new DiagnosticCheckResult(
                    Id,
                    Name,
                    DiagnosticStatus.Failed,
                    $"Host version {hostVersion} and worker version {workerVersion} have different major versions.",
                    "Rebuild the solution so the host and worker share the same version, or reinstall the tia-mcp global tool.",
                    evidence);
            }
        }

        return new DiagnosticCheckResult(
            Id,
            Name,
            DiagnosticStatus.Passed,
            $"Host {hostVersion}, worker {workerVersion}.",
            null,
            evidence);
    }

    private static bool TryParseMajor(string? version, out int major)
    {
        major = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var span = version.AsSpan();
        var dot = span.IndexOf('.');
        var majorSpan = dot < 0 ? span : span[..dot];
        return int.TryParse(majorSpan, out major);
    }
}
