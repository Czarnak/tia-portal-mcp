using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Safety.Pipeline;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class WriteSafetyLeaseConcurrencyTests
{
    [Fact]
    public async Task ConcurrentLifecycleCalls_SecondPlansFromTheFirstVerifiedStateInsideTheLease()
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

            client = new OpennessWorkerClient(binding, requestTimeout: TimeSpan.FromSeconds(5),
                accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
            InjectTransport(
                client,
                CreateStatefulTransport(scriptPath, mutationLogPath, projectPath));

            var execution = LifecycleTestCalls.Execution(client, audit);

            // Enqueue two calls behind the shared lease. The whole first call must plan,
            // mutate, verify, and audit before the second call observes its resulting state.
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

            var firstApply = ProjectWriteTools.SaveProject(client, execution, projectPath);
            var secondApply = ProjectWriteTools.SaveProject(client, execution, projectPath);

            releaseBlocker.TrySetResult(true);
            var blockerResult = await blocker.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(blockerResult.Success);

            var firstResult = await firstApply.WaitAsync(TimeSpan.FromSeconds(10));
            var secondResult = await secondApply.WaitAsync(TimeSpan.FromSeconds(10));
            var first = LifecycleTestCalls.Document(firstResult);
            var second = LifecycleTestCalls.Document(secondResult);
            LifecycleTestCalls.Succeeded(first);
            LifecycleTestCalls.Succeeded(second);

            var events = await File.ReadAllLinesAsync(mutationLogPath);
            var firstSave = Array.IndexOf(events, "save:1");
            var firstVerify = Array.IndexOf(events, "verify:1");
            var secondSave = Array.IndexOf(events, "save:2");
            var secondVerify = Array.IndexOf(events, "verify:2");
            Assert.True(firstSave >= 0 && firstVerify > firstSave);
            Assert.True(secondSave > firstVerify && secondVerify > secondSave,
                "The first call's mutation and verification must finish before the second call mutates.");
            Assert.DoesNotContain(events.Take(firstVerify), entry => entry == "probe:1");
            Assert.Contains(events.Skip(firstVerify + 1).Take(secondSave - firstVerify - 1),
                entry => entry == "probe:1");
            Assert.Equal(2, LifecycleTestCalls.AuditCount(audit));
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

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string StatefulWorkerScript => $$"""
        param(
            [Parameter(Mandatory = $true)][string]$MutationLogPath,
            [Parameter(Mandatory = $true)][string]$ProjectPath
        )

        $revision = 0
        $capabilities = @(
        {{string.Join("," + Environment.NewLine, WorkerProtocol.RequiredCapabilities.Select(capability => $"            '{capability}'"))}}
        )

        while (($line = [Console]::In.ReadLine()) -ne $null) {
            $request = $line | ConvertFrom-Json
            if ($request.method -eq 'hello') {
                $hello = [ordered]@{
                    success = $true
                    payload = '{}'
                    protocolVersion = '{{WorkerProtocol.Version}}'
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

            $status = [ordered]@{
                isOpen = $true
                name = 'Line'
                path = $ProjectPath
                version = $null
                author = $null
                isModified = ($revision -eq 0)
                creationTime = $null
                lastModified = $null
                lastModifiedBy = $null
                size = $null
                metadata = $null
            }
            $statusPayload = [ordered]@{
                success = $true
                operation = 'get_project_status'
                projectPath = $ProjectPath
                project = $status
            }
            switch ($request.method) {
                'probe_project_status_for_lifecycle' {
                    [IO.File]::AppendAllText($MutationLogPath, 'probe:' + $revision + [Environment]::NewLine)
                    $statusPayload.operation = 'probe_project_status_for_lifecycle'
                    $payload = $statusPayload | ConvertTo-Json -Compress -Depth 8
                    $response = [ordered]@{
                        success = $true
                        payload = $payload
                        resolvedProjectPath = $ProjectPath
                        sessionIdentity = $identity
                    }
                }
                'save_project' {
                    if ($request.confirm -ne $true) { throw 'Worker mutation fence missing.' }
                    $revision += 1
                    [IO.File]::AppendAllText($MutationLogPath, 'save:' + $revision + [Environment]::NewLine)
                    $status.isModified = $false
                    $lifecycle = [ordered]@{
                        success = $true
                        operation = 'save_project'
                        projectPath = $ProjectPath
                        project = $status
                    }
                    $response = [ordered]@{
                        success = $true
                        payload = ($lifecycle | ConvertTo-Json -Compress -Depth 8)
                        resolvedProjectPath = $ProjectPath
                        sessionIdentity = $identity
                    }
                }
                'get_basic_project_status' {
                    [IO.File]::AppendAllText($MutationLogPath, 'verify:' + $revision + [Environment]::NewLine)
                    $response = [ordered]@{
                        success = $true
                        payload = ($statusPayload | ConvertTo-Json -Compress -Depth 8)
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
