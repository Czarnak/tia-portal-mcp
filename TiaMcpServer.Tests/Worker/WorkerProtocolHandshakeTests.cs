using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class WorkerProtocolHandshakeTests
{
    [Fact]
    public async Task CurrentFakeWorker_CompletesHelloBeforeFirstEngineeringRequest()
    {
        using var transport = new PersistentWorkerTransport(
            FakeWorkerLocator.Locate(),
            requestTimeout: TimeSpan.FromSeconds(5));

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = "get_project_status",
            ProjectPath = "ok"
        });

        Assert.True(response.Success, response.Error);
        // FakeWorker excludes hello from its engineering sequence. seq=1 therefore proves the
        // first real request ran successfully after, rather than in place of, the handshake.
        Assert.Equal("{\"seq\":1}", response.Payload);
        Assert.NotNull(response.SessionIdentity);
    }

    [Theory]
    [InlineData("missing-hello-contract")]
    [InlineData("wrong-protocol-version")]
    public async Task LegacyWorker_HandshakeFailsBeforeOriginalEngineeringMethodIsSent(string legacyMode)
    {
        var tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "tia-worker-handshake-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = Path.Combine(tempDirectory, "legacy-worker.ps1");
        var requestLogPath = Path.Combine(tempDirectory, "requests.ndjson");

        try
        {
            await File.WriteAllTextAsync(scriptPath, LegacyWorkerScript, new UTF8Encoding(false));

            var helloResponse = legacyMode switch
            {
                "missing-hello-contract" => JsonSerializer.Serialize(new
                {
                    success = true,
                    payload = "{}"
                }),
                "wrong-protocol-version" => JsonSerializer.Serialize(new
                {
                    success = true,
                    payload = "{}",
                    protocolVersion = "legacy-project-binding-v0",
                    capabilities = WorkerProtocol.RequiredCapabilities
                }),
                _ => throw new InvalidOperationException($"Unknown legacy mode '{legacyMode}'.")
            };
            var encodedHelloResponse = Convert.ToBase64String(Encoding.UTF8.GetBytes(helloResponse));
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
                "-LogPath",
                QuoteArgument(requestLogPath),
                "-HelloResponseBase64",
                QuoteArgument(encodedHelloResponse));

            using var transport = new PersistentWorkerTransport(
                powershellPath,
                requestTimeout: TimeSpan.FromSeconds(5),
                workerArgs: workerArgs);

            var exception = await Assert.ThrowsAsync<PersistentWorkerTransport.WorkerProtocolMismatchException>(() =>
                transport.SendAsync(new WorkerRequest
                {
                    Method = "browse_project_tree",
                    ProjectPath = "C:\\Projects\\MustNeverBeSent.ap21"
                }));

            Assert.Contains("protocol handshake failed before any Siemens operation", exception.Message);
            var sentLines = await ReadRequestLogAsync(requestLogPath);
            Assert.Single(sentLines);

            using var helloDocument = JsonDocument.Parse(sentLines[0]);
            Assert.Equal("hello", helloDocument.RootElement.GetProperty("method").GetString());
            Assert.Equal(
                WorkerProtocol.Version,
                helloDocument.RootElement.GetProperty("protocolVersion").GetString());
            Assert.DoesNotContain("browse_project_tree", sentLines[0]);
            Assert.DoesNotContain("MustNeverBeSent", sentLines[0]);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private static async Task<string[]> ReadRequestLogAsync(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!File.Exists(path) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        return File.Exists(path)
            ? await File.ReadAllLinesAsync(path)
            : Array.Empty<string>();
    }

    private static string QuoteArgument(string value)
        => $"\"{value.Replace("\"", "\\\"")}\"";

    private const string LegacyWorkerScript = """
        param(
            [Parameter(Mandatory = $true)][string]$LogPath,
            [Parameter(Mandatory = $true)][string]$HelloResponseBase64
        )

        $hello = [Console]::In.ReadLine()
        [IO.File]::AppendAllText($LogPath, $hello + [Environment]::NewLine)

        $helloResponse = [Text.Encoding]::UTF8.GetString(
            [Convert]::FromBase64String($HelloResponseBase64))
        [Console]::Out.WriteLine($helloResponse)
        [Console]::Out.Flush()

        # A correct host stops after rejecting the hello response. If it regresses and sends the
        # engineering request, record it and answer so the test fails promptly instead of timing out.
        $next = [Console]::In.ReadLine()
        if ($null -ne $next) {
            [IO.File]::AppendAllText($LogPath, $next + [Environment]::NewLine)
            [Console]::Out.WriteLine('{"success":true,"payload":"unexpected engineering request"}')
            [Console]::Out.Flush()
        }
        """;
}
