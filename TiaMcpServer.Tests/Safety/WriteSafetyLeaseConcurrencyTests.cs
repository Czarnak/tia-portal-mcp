using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

public sealed class WriteSafetyLeaseConcurrencyTests
{
    [Fact]
    public async Task ConcurrentApplies_SecondReReadsStateInsideLeaseAndDoesNotExecuteMutation()
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "tia-safety-lease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = Path.Combine(tempDirectory, "stateful-worker.ps1");
        var mutationLogPath = Path.Combine(tempDirectory, "mutations.log");
        var projectPath = Path.Combine(tempDirectory, "Line.ap21");
        OpennessWorkerClient? client = null;

        try
        {
            await File.WriteAllTextAsync(scriptPath, StatefulWorkerScript, new UTF8Encoding(false));

            var identity = new WorkerSessionIdentity
            {
                WorkerSessionId = "stateful-test-worker",
                SessionGeneration = 1,
                PortalProcessId = 4242,
                ProjectPath = projectPath
            };
            var binding = new ProjectSessionBinding(null);
            Assert.True(binding.BindVerified(identity, forceRebind: false, out var bindError), bindError);

            using var audit = new TempAuditDirectory();
            var safety = audit.CreateSafety(projectSessionBinding: binding);
            client = new OpennessWorkerClient(binding, requestTimeout: TimeSpan.FromSeconds(5));
            InjectTransport(
                client,
                CreateStatefulTransport(scriptPath, mutationLogPath, projectPath));

            var target = new { projectPath };
            var requestedInput = new { projectPath };
            const string initialState = "{\"revision\":0}";
            var firstToken = ReadToken(safety.CreatePreview(
                "save_project",
                projectPath,
                target,
                "Save project from revision zero.",
                requestedInput,
                initialState));
            var secondToken = ReadToken(safety.CreatePreview(
                "save_project",
                projectPath,
                target,
                "Save project from revision zero.",
                requestedInput,
                initialState));

            // Hold the client's shared binding lease while both apply calls enqueue. In the
            // correct implementation each whole apply is queued here. In the vulnerable
            // implementation only each fresh-state read is queued, allowing BOTH reads of
            // revision=0 to finish before either mutation reacquires the lease.
            var blockerEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBlocker = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var blocker = client.ExecuteWithPinnedBindingAsync(
                binding.CaptureSnapshot(),
                async () =>
                {
                    blockerEntered.TrySetResult(true);
                    await releaseBlocker.Task;
                    return WorkerCallResult.Ok("{}");
                });
            await blockerEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var firstApply = ProjectWriteTools.SaveProject(
                client,
                safety,
                projectPath,
                confirm: true,
                safetyToken: firstToken);
            var secondApply = ProjectWriteTools.SaveProject(
                client,
                safety,
                projectPath,
                confirm: true,
                safetyToken: secondToken);

            releaseBlocker.TrySetResult(true);
            var blockerResult = await blocker.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(blockerResult.Success);

            var firstJson = await firstApply.WaitAsync(TimeSpan.FromSeconds(10));
            var secondJson = await secondApply.WaitAsync(TimeSpan.FromSeconds(10));
            using var firstDocument = JsonDocument.Parse(firstJson);
            using var secondDocument = JsonDocument.Parse(secondJson);
            var first = firstDocument.RootElement;
            var second = secondDocument.RootElement;

            Assert.True(first.GetProperty("success").GetBoolean());
            Assert.False(second.GetProperty("success").GetBoolean());
            Assert.Equal(
                WorkerFailureCategories.StateChanged,
                second.GetProperty("failureCategory").GetString());

            var mutations = File.Exists(mutationLogPath)
                ? await File.ReadAllLinesAsync(mutationLogPath)
                : Array.Empty<string>();
            Assert.Single(mutations);
            Assert.Equal("save_project", mutations[0]);
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

    private static PersistentWorkerTransport CreateStatefulTransport(
        string scriptPath,
        string mutationLogPath,
        string projectPath)
    {
        var powershellPath = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        Assert.True(File.Exists(powershellPath), $"Windows PowerShell was not found at '{powershellPath}'.");

        var workerArgs = string.Join(
            " ",
            "-NoLogo",
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy Bypass",
            "-File",
            QuoteArgument(scriptPath),
            "-MutationLogPath",
            QuoteArgument(mutationLogPath),
            "-ProjectPath",
            QuoteArgument(projectPath));
        return new PersistentWorkerTransport(
            powershellPath,
            requestTimeout: TimeSpan.FromSeconds(5),
            workerArgs: workerArgs);
    }

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

    private static string ReadToken(string previewJson)
    {
        using var document = JsonDocument.Parse(previewJson);
        return document.RootElement.GetProperty("safetyToken").GetString()!;
    }

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    private const string StatefulWorkerScript = """
        param(
            [Parameter(Mandatory = $true)][string]$MutationLogPath,
            [Parameter(Mandatory = $true)][string]$ProjectPath
        )

        $revision = 0
        $capabilities = @(
            'expected-session-identity',
            'response-session-identity',
            'deterministic-project-selection'
        )

        while (($line = [Console]::In.ReadLine()) -ne $null) {
            $request = $line | ConvertFrom-Json
            if ($request.method -eq 'hello') {
                $hello = [ordered]@{
                    success = $true
                    payload = '{}'
                    protocolVersion = 'project-binding-v1'
                    capabilities = $capabilities
                }
                [Console]::Out.WriteLine(($hello | ConvertTo-Json -Compress -Depth 6))
                [Console]::Out.Flush()
                continue
            }

            $identity = [ordered]@{
                workerSessionId = 'stateful-test-worker'
                sessionGeneration = 1
                portalProcessId = 4242
                projectPath = $ProjectPath
            }

            switch ($request.method) {
                'probe_project_status_for_lifecycle' {
                    $payload = if ($revision -eq 0) { '{"revision":0}' } else { '{"revision":1}' }
                    $response = [ordered]@{
                        success = $true
                        payload = $payload
                        resolvedProjectPath = $ProjectPath
                        sessionIdentity = $identity
                    }
                }
                'save_project' {
                    [IO.File]::AppendAllText(
                        $MutationLogPath,
                        'save_project' + [Environment]::NewLine)
                    $revision = 1
                    $response = [ordered]@{
                        success = $true
                        payload = '{}'
                        resolvedProjectPath = $ProjectPath
                        sessionIdentity = $identity
                    }
                }
                'get_basic_project_status' {
                    $response = [ordered]@{
                        success = $true
                        payload = '{"revision":1}'
                        resolvedProjectPath = $ProjectPath
                        sessionIdentity = $identity
                    }
                }
                default {
                    $response = [ordered]@{
                        success = $false
                        failureCategory = 'worker_operation_failed'
                        error = 'unexpected method ' + $request.method
                    }
                }
            }

            [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 8))
            [Console]::Out.Flush()
        }
        """;
}
