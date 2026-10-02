using System.Reflection;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Microsoft.Extensions.Logging;

namespace TiaMcpServer.Tests.TestSupport;

/// <summary>Deliberately malformed worker replies for host identity validation tests.
/// Replies are fixed independently of requests; this does not change FakeWorker source-state gates.</summary>
internal sealed class ScriptedStatusTransport : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tia-status-transport-" + Guid.NewGuid().ToString("N"));
    private readonly string _requestLog;
    private readonly PersistentWorkerTransport _transport;
    private readonly DiagnosticLogger _logger = new();

    public ScriptedStatusTransport(OpennessWorkerClient client, params WorkerResponse[] responses)
    {
        Directory.CreateDirectory(_directory);
        _requestLog = Path.Combine(_directory, "requests.log");
        var scriptPath = Path.Combine(_directory, "worker.ps1");
        File.WriteAllText(scriptPath, Script, new UTF8Encoding(false));
        var json = JsonSerializer.Serialize(responses, WorkerJson.Envelope);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        _transport = new PersistentWorkerTransport(powershell, TimeSpan.FromSeconds(15), logger: _logger,
            workerArgs: $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" -ResponsesBase64 {encoded} -RequestLog \"{_requestLog}\"");
        typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(client, _transport);
    }

    public string[] Requests => File.ReadAllLines(_requestLog);
    public string Diagnostics => string.Join(Environment.NewLine, _logger.Lines) +
        (File.Exists(_requestLog + ".responses") ? File.ReadAllText(_requestLog + ".responses") : "");

    public static WorkerResponse Success(string projectPath, params string[] warnings) => new()
    {
        Success = true,
        Payload = "{}",
        ResolvedProjectPath = projectPath,
        SessionIdentity = new WorkerSessionIdentity
        {
            WorkerSessionId = "scripted-status-worker",
            SessionGeneration = 1,
            PortalProcessId = 4242,
            ProjectPath = projectPath
        },
        Warnings = warnings.ToList()
    };

    public void Dispose()
    {
        _transport.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private sealed class DiagnosticLogger : ILogger
    {
        public System.Collections.Concurrent.ConcurrentQueue<string> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Lines.Enqueue(formatter(state, exception));
    }

    private static string Script => $$"""
        param([string]$ResponsesBase64, [string]$RequestLog)
        $responses = ConvertFrom-Json -InputObject ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($ResponsesBase64)))
        $next = 0
        while (($line = [Console]::In.ReadLine()) -ne $null) {
            $request = $line | ConvertFrom-Json
            [IO.File]::AppendAllText($RequestLog, $request.method + [Environment]::NewLine)
            if ($request.method -eq 'hello') {
                $response = [ordered]@{
                    success = $true
                    payload = '{}'
                    protocolVersion = '{{WorkerProtocol.Version}}'
                    capabilities = @({{string.Join(",", WorkerProtocol.RequiredCapabilities.Select(c => $"'{c}'"))}})
                }
            } elseif ($request.method -eq 'get_project_status' -and $next -lt $responses.Count) {
                $response = $responses[$next]
                $next++
            } else {
                $response = @{ success = $false; failureCategory = 'protocol_error'; error = 'Unexpected scripted request.' }
            }
            $responseJson = $response | ConvertTo-Json -Compress -Depth 10
            [IO.File]::AppendAllText($RequestLog + '.responses', $responseJson + [Environment]::NewLine)
            [Console]::Out.WriteLine($responseJson)
            [Console]::Out.Flush()
        }
        """;
}
