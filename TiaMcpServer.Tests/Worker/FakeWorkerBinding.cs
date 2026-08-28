using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Worker;

internal static class FakeWorkerBinding
{
    public static async Task BindVerifiedAsync(
        OpennessWorkerClient client,
        ProjectSessionBinding binding,
        string projectPath)
        => await BindVerifiedAsync(
            client.GetProjectStatusAsync,
            binding,
            projectPath);

    private static async Task BindVerifiedAsync(
        Func<string?, Task<WorkerCallResult>> identityOptionalRead,
        ProjectSessionBinding binding,
        string projectPath)
    {
        var canonicalProjectPath = ProjectPathNormalization.Canonicalize(projectPath)
            ?? throw new InvalidOperationException("A project path is required for FakeWorker binding.");
        var observed = await identityOptionalRead(canonicalProjectPath);
        if (!observed.Success || observed.SessionIdentity is null)
        {
            throw new InvalidOperationException(observed.Error);
        }

        var reportedProjectPath = ProjectPathNormalization.Canonicalize(observed.SessionIdentity.ProjectPath);
        if (!string.Equals(canonicalProjectPath, reportedProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"FakeWorker reported project '{observed.SessionIdentity.ProjectPath}' instead of '{canonicalProjectPath}'.");
        }

        if (!binding.BindVerified(observed.SessionIdentity, forceRebind: false, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }
}
