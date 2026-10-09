using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Contracts.Project;
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
        // This helper is used only by source-present fixtures. Declare that source before the
        // child worker launches; a status request must not create open state on its own.
        using var uiOpen = new FakeWorkerUiOpenProject(canonicalProjectPath);
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
