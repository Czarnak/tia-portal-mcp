using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class LifecycleIdentityContinuityTests
{
    private const string ProjectA = "C:\\Projects\\A.ap21";
    private const string ProjectB = "C:\\Projects\\B.ap21";

    [Theory]
    [InlineData("open", "worker")]
    [InlineData("open", "portal")]
    [InlineData("create", "worker")]
    [InlineData("create", "portal")]
    [InlineData("save-as", "worker")]
    [InlineData("save-as", "portal")]
    public async Task RebindingLifecycle_DifferentWorkerOrPortalFailsWithoutAdoptingResponse(
        string operation,
        string mismatch)
    {
        var responseIdentity = Identity(
            workerSessionId: mismatch == "worker" ? "worker-b" : "worker-a",
            generation: 6,
            portalProcessId: mismatch == "portal" ? 4343 : 4242,
            projectPath: ProjectB);

        var outcome = await InvokeLifecycleAsync(
            operation,
            SuccessfulLifecycleResponse(responseIdentity, ProjectB));

        Assert.False(outcome.Result.Success);
        Assert.NotEqual(ProjectBindingSnapshot.UnboundState, outcome.After.State);
        Assert.False(string.Equals(ProjectB, outcome.After.ProjectPath, StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(responseIdentity.WorkerSessionId, outcome.After.WorkerSessionId);
        Assert.NotEqual(responseIdentity.PortalProcessId, outcome.After.PortalProcessId);
    }

    [Fact]
    public async Task OpenSamePath_GenerationChangeIsRejectedWithoutAdoptingNewGeneration()
    {
        var responseIdentity = Identity("worker-a", generation: 6, portalProcessId: 4242, ProjectA);

        var outcome = await InvokeLifecycleAsync(
            "open-same-path",
            SuccessfulLifecycleResponse(responseIdentity, ProjectA));

        Assert.False(outcome.Result.Success);
        Assert.NotEqual(ProjectBindingSnapshot.UnboundState, outcome.After.State);
        Assert.Equal(ProjectA, outcome.After.ProjectPath, ignoreCase: true);
        Assert.NotEqual(6, outcome.After.SessionGeneration);
    }

    [Fact]
    public async Task OpenDifferentPath_WithoutGenerationIncreaseIsRejectedWithoutAdoptingNewPath()
    {
        var responseIdentity = Identity("worker-a", generation: 5, portalProcessId: 4242, ProjectB);

        var outcome = await InvokeLifecycleAsync(
            "open",
            SuccessfulLifecycleResponse(responseIdentity, ProjectB));

        Assert.False(outcome.Result.Success);
        Assert.NotEqual(ProjectBindingSnapshot.UnboundState, outcome.After.State);
        Assert.False(string.Equals(ProjectB, outcome.After.ProjectPath, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("different-worker")]
    [InlineData("different-portal")]
    [InlineData("generation-not-increased")]
    [InlineData("project-still-present")]
    [InlineData("resolved-path-still-present")]
    public async Task Close_InvalidPostIdentityFailsWithoutClearingBinding(string invalidCase)
    {
        var identity = invalidCase switch
        {
            "different-worker" => Identity("worker-b", 6, 4242, projectPath: null),
            "different-portal" => Identity("worker-a", 6, 4343, projectPath: null),
            "generation-not-increased" => Identity("worker-a", 5, 4242, projectPath: null),
            "project-still-present" => Identity("worker-a", 6, 4242, ProjectA),
            "resolved-path-still-present" => Identity("worker-a", 6, 4242, projectPath: null),
            _ => throw new InvalidOperationException($"Unknown close case '{invalidCase}'.")
        };
        var resolvedPath = invalidCase switch
        {
            "project-still-present" or "resolved-path-still-present" => ProjectA,
            _ => null
        };

        var outcome = await InvokeLifecycleAsync(
            "close",
            SuccessfulLifecycleResponse(identity, resolvedPath));

        Assert.False(outcome.Result.Success);
        Assert.NotEqual(ProjectBindingSnapshot.UnboundState, outcome.After.State);
        Assert.Equal(ProjectA, outcome.After.ProjectPath, ignoreCase: true);
    }

    [Fact]
    public async Task Close_SameWorkerAndPortalWithIncreasedGenerationAndNoProjectClearsBinding()
    {
        var closedIdentity = Identity("worker-a", generation: 6, portalProcessId: 4242, projectPath: null);

        var outcome = await InvokeLifecycleAsync(
            "close",
            SuccessfulLifecycleResponse(closedIdentity, resolvedProjectPath: null));

        Assert.True(outcome.Result.Success, outcome.Result.Error);
        Assert.Equal(ProjectBindingSnapshot.UnboundState, outcome.After.State);
        Assert.Null(outcome.After.ProjectPath);
        Assert.Null(outcome.After.WorkerSessionId);
    }

    private static async Task<LifecycleOutcome> InvokeLifecycleAsync(
        string operation,
        WorkerResponse response)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "tia-lifecycle-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = Path.Combine(tempDirectory, "lifecycle-worker.ps1");
        OpennessWorkerClient? client = null;

        try
        {
            await File.WriteAllTextAsync(scriptPath, LifecycleWorkerScript, new UTF8Encoding(false));

            var binding = new ProjectSessionBinding(null);
            Assert.True(binding.BindVerified(
                Identity("worker-a", generation: 5, portalProcessId: 4242, ProjectA),
                forceRebind: false,
                out var bindError), bindError);
            var before = binding.CaptureSnapshot();

            client = new OpennessWorkerClient(binding, requestTimeout: TimeSpan.FromSeconds(5));
            InjectTransport(client, CreateTransport(scriptPath, response));

            var result = operation switch
            {
                "open" => await client.OpenProjectAsync(ProjectB, forceRebind: true),
                "open-same-path" => await client.OpenProjectAsync(ProjectA, forceRebind: false),
                "create" => await client.CreateProjectAsync(
                    projectDirectory: "C:\\Projects",
                    projectName: "B",
                    author: null,
                    comment: null),
                "save-as" => await client.SaveProjectAsAsync(
                    projectPath: ProjectA,
                    targetDirectory: "C:\\Projects",
                    targetName: "B",
                    rebind: true),
                "close" => await client.CloseProjectAsync(ProjectA, saveBeforeClose: false),
                _ => throw new InvalidOperationException($"Unknown lifecycle operation '{operation}'.")
            };

            return new LifecycleOutcome(result, before, binding.CaptureSnapshot());
        }
        finally
        {
            client?.Dispose();
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static PersistentWorkerTransport CreateTransport(
        string scriptPath,
        WorkerResponse response)
    {
        var powershellPath = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        Assert.True(File.Exists(powershellPath), $"Windows PowerShell was not found at '{powershellPath}'.");

        var responseJson = JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        var responseBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(responseJson));
        var workerArgs = string.Join(
            " ",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy Bypass",
            "-File",
            QuoteArgument(scriptPath),
            "-ResponseBase64",
            QuoteArgument(responseBase64));
        return new PersistentWorkerTransport(
            powershellPath,
            requestTimeout: TimeSpan.FromSeconds(5),
            workerArgs: workerArgs);
    }

    private static WorkerResponse SuccessfulLifecycleResponse(
        WorkerSessionIdentity identity,
        string? resolvedProjectPath)
        => new()
        {
            Success = true,
            Payload = "{}",
            ResolvedProjectPath = resolvedProjectPath,
            SessionIdentity = identity
        };

    private static WorkerSessionIdentity Identity(
        string workerSessionId,
        long generation,
        int portalProcessId,
        string? projectPath)
        => new()
        {
            WorkerSessionId = workerSessionId,
            SessionGeneration = generation,
            PortalProcessId = portalProcessId,
            ProjectPath = projectPath
        };

    private static void InjectTransport(
        OpennessWorkerClient client,
        PersistentWorkerTransport transport)
    {
        var field = typeof(OpennessWorkerClient).GetField(
            "_transport",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(client, transport);
    }

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    private sealed record LifecycleOutcome(
        WorkerCallResult Result,
        ProjectBindingSnapshot Before,
        ProjectBindingSnapshot After);

    private const string LifecycleWorkerScript = """
        param([Parameter(Mandatory = $true)][string]$ResponseBase64)

        $operationResponse = [Text.Encoding]::UTF8.GetString(
            [Convert]::FromBase64String($ResponseBase64))
        while (($line = [Console]::In.ReadLine()) -ne $null) {
            $request = $line | ConvertFrom-Json
            if ($request.method -eq 'hello') {
                $hello = [ordered]@{
                    success = $true
                    payload = '{}'
                    protocolVersion = 'project-binding-v1'
                    capabilities = @(
                        'expected-session-identity',
                        'response-session-identity',
                        'deterministic-project-selection'
                    )
                }
                [Console]::Out.WriteLine(($hello | ConvertTo-Json -Compress -Depth 6))
            }
            else {
                [Console]::Out.WriteLine($operationResponse)
            }
            [Console]::Out.Flush()
        }
        """;
}
