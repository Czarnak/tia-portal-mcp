using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Worker;

/// <summary>
/// Owns ONE long-lived worker process and the stdin/stdout line protocol against it.
/// Requests are serialized behind an instance gate (Siemens Openness is not safe for
/// concurrent access to one TIA Portal). A crash, timeout, or protocol desync kills the
/// process; the next request transparently starts a fresh one. Stderr is pumped in the
/// background for logging and crash diagnostics — per-request warnings arrive structurally
/// on <see cref="WorkerResponse.Warnings"/> instead.
/// </summary>
public sealed class PersistentWorkerTransport : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const int RecentStderrCapacity = 30;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _workerExecutablePath;
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger? _logger;
    private readonly string? _workerArgs;
    private readonly ConcurrentQueue<string> _recentStderr = new();

    private Process? _process;
    private Task? _stderrPump;
    private bool _disposed;

    public PersistentWorkerTransport(string workerExecutablePath, TimeSpan requestTimeout, ILogger? logger = null, string? workerArgs = null)
    {
        _workerExecutablePath = workerExecutablePath;
        _requestTimeout = requestTimeout;
        _logger = logger;
        _workerArgs = workerArgs;
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureProcessStarted();
            var process = _process!;

            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions))
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Broken pipe: the worker died since the last request. Fail this request;
                // the next one restarts the process.
                KillProcess();
                throw;
            }

            var responseLineTask = process.StandardOutput.ReadLineAsync();
            using var timeout = new CancellationTokenSource(_requestTimeout);
            var completed = await Task.WhenAny(responseLineTask, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token))
                .ConfigureAwait(false);

            if (completed != responseLineTask)
            {
                KillProcess();
                throw new TimeoutException(
                    $"TIA Openness worker did not respond within {_requestTimeout.TotalSeconds:N0} seconds. "
                    + "The worker process was terminated and will restart on the next request; retry it.");
            }

            var responseLine = await responseLineTask.ConfigureAwait(false);
            if (responseLine is null)
            {
                var detail = await CaptureCrashDetailAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"TIA Openness worker exited without a response. {detail}");
            }

            WorkerResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
            }
            catch (JsonException)
            {
                // Protocol desync: any leftover bytes would corrupt the next request too.
                KillProcess();
                throw;
            }

            if (response is null)
            {
                // A JSON null is just as invalid as malformed protocol data; do not reuse it.
                KillProcess();
                throw new InvalidOperationException("TIA Openness worker returned an empty response.");
            }

            return response;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureProcessStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        KillProcess();
        while (_recentStderr.TryDequeue(out _))
        {
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _workerExecutablePath,
            Arguments = _workerArgs ?? string.Empty,
            WorkingDirectory = Path.GetDirectoryName(_workerExecutablePath) ?? AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the TIA Openness worker process.");
        _process = process;
        _stderrPump = Task.Run(() => PumpStderrAsync(process));
        _logger?.LogInformation("Started TIA Openness worker process (pid {Pid}).", process.Id);
    }

    private async Task PumpStderrAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                _logger?.LogWarning("TIA Openness worker stderr: {Line}", trimmed);
                _recentStderr.Enqueue(trimmed);
                while (_recentStderr.Count > RecentStderrCapacity)
                {
                    _recentStderr.TryDequeue(out _);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Stream teardown while the process is being killed/disposed — expected noise.
        }
    }

    private async Task<string> CaptureCrashDetailAsync()
    {
        var pump = _stderrPump;
        KillProcess();
        if (pump is not null)
        {
            // Give the pump a moment to drain the dying process's final stderr lines.
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }

        var lines = _recentStderr.ToArray();
        return lines.Length == 0 ? "No response was written." : string.Join(" | ", lines);
    }

    private void KillProcess()
    {
        var process = _process;
        _process = null;
        _stderrPump = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited or already gone — nothing to clean up.
        }

        process.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    // Closing stdin lets the worker's request loop end and TIA detach cleanly.
                    process.StandardInput.Close();
                    if (!process.WaitForExit(2000))
                    {
                        process.Kill();
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception)
            {
                // Best-effort shutdown; the process is exiting anyway.
            }

            process.Dispose();
            _process = null;
        }

        _gate.Dispose();
    }
}
