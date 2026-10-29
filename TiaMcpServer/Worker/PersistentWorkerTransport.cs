using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Worker;

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
    private const int RecentStderrCapacity = 30;
    private static readonly TimeSpan DisposeGateTimeout = TimeSpan.FromSeconds(2);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _workerExecutablePath;
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger? _logger;
    private readonly string? _workerArgs;
    private readonly ConcurrentQueue<string> _recentStderr = new();

    private Process? _process;
    private Task? _stderrPump;
    private bool _handshakeComplete;
    private volatile bool _disposed;

    public PersistentWorkerTransport(string workerExecutablePath, TimeSpan requestTimeout, ILogger? logger = null, string? workerArgs = null)
    {
        _workerExecutablePath = workerExecutablePath;
        _requestTimeout = requestTimeout;
        _logger = logger;
        _workerArgs = workerArgs;
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            EnsureProcessStarted();
            if (_disposed)
            {
                // Dispose ran while the worker was starting and could not see it; nothing was sent.
                KillProcess();
                throw new WorkerTransportDisposedException();
            }

            var process = _process!;
            if (!_handshakeComplete)
            {
                var hello = await ExchangeAsync(
                    process,
                    new WorkerRequest
                    {
                        Method = "hello",
                        ProtocolVersion = WorkerProtocol.Version
                    }).ConfigureAwait(false);
                ValidateHello(hello);
                _handshakeComplete = true;
            }

            request.ProtocolVersion = WorkerProtocol.Version;
            return await ExchangeAsync(process, request).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WorkerResponse> ExchangeAsync(Process process, WorkerRequest request)
    {
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, WorkerJson.Request))
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

        string? responseLine;
        try
        {
            responseLine = await process.StandardOutput.ReadLineAsync().WaitAsync(_requestTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            KillProcess();
            throw new TimeoutException(
                $"TIA Openness worker did not respond within {_requestTimeout.TotalSeconds:N0} seconds. "
                + "The worker process was terminated and will restart on the next request; retry it.");
        }

        if (responseLine is null)
        {
            var detail = await CaptureCrashDetailAsync().ConfigureAwait(false);
            throw new InvalidOperationException($"TIA Openness worker exited without a response. {detail}");
        }

        WorkerResponse? response;
        try
        {
            using var document = JsonDocument.Parse(responseLine);
            try
            {
                Vci.VciWritePayloadContract.ValidateEnvelopeOutcome(document.RootElement);
            }
            catch (JsonException)
            {
                return new WorkerResponse
                {
                    Success = false,
                    FailureCategory = WorkerFailureCategories.ProtocolError,
                    Error = "The worker VCI outcome did not match its declared contract. Inspect current state before retrying."
                };
            }
            try
            {
                Network.NetworkPayloadContract.ValidateEnvelopeOutcome(document.RootElement);
            }
            catch (JsonException)
            {
                return new WorkerResponse
                {
                    Success = false,
                    FailureCategory = WorkerFailureCategories.ProtocolError,
                    Error = "The worker configure outcome did not match its declared contract. Inspect current state before retrying."
                };
            }
            response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, WorkerJson.Envelope);
        }
        catch (JsonException)
        {
            // Protocol desync: any leftover bytes would corrupt the next request too.
            KillProcess();
            throw;
        }

        if (response is null)
        {
            KillProcess();
            throw new InvalidOperationException("TIA Openness worker returned an empty response.");
        }

        try
        {
            ProjectContextPayloadContract.ValidateEnvelopeIdentity(responseLine, response);
        }
        catch (JsonException)
        {
            // A completed response with malformed local identity is a post-send protocol
            // failure. Keep the worker available for explicit state inspection; never replay.
            return new WorkerResponse
            {
                Success = false,
                FailureCategory = WorkerFailureCategories.ProtocolError,
                Error = "The worker local-session identity did not match its declared contract. Inspect current state before retrying."
            };
        }

        return response;
    }

    private void ValidateHello(WorkerResponse response)
    {
        var capabilities = response.Capabilities ?? new List<string>();
        var missing = WorkerProtocol.RequiredCapabilities
            .Where(required => !capabilities.Contains(required, StringComparer.Ordinal))
            .ToArray();
        if (!response.Success ||
            !string.Equals(response.ProtocolVersion, WorkerProtocol.Version, StringComparison.Ordinal) ||
            missing.Length != 0)
        {
            var message =
                "TIA Openness worker protocol handshake failed before any Siemens operation. "
                + $"Expected version '{WorkerProtocol.Version}' and capabilities "
                + $"[{string.Join(", ", WorkerProtocol.RequiredCapabilities)}]; received version "
                + $"'{response.ProtocolVersion ?? "(missing)"}', missing [{string.Join(", ", missing)}]. "
                + $"Worker error: {response.Error ?? "(none)"}";
            KillProcess();
            throw new WorkerProtocolMismatchException(message);
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
        _handshakeComplete = false;
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

        // Far below the 5-60 minute request timeout: shutdown must not wait out a long request.
        if (!_gate.Wait(DisposeGateTimeout))
        {
            // A request is in flight. Kill its process so its read ends; that request still owns
            // the gate and _process and cleans both up itself.
            try
            {
                _process?.Kill();
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // Already exited or already disposed by the in-flight request.
            }

            return;
        }

        try
        {
            ShutDownProcess();
        }
        finally
        {
            // _gate is deliberately never disposed: a request queued behind Dispose still calls Release().
            _gate.Release();
        }
    }

    private void ShutDownProcess()
    {
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
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new WorkerTransportDisposedException();
        }
    }

    public sealed class WorkerProtocolMismatchException : InvalidOperationException
    {
        public WorkerProtocolMismatchException(string message)
            : base(message)
        {
        }
    }

    /// <summary>The transport was disposed before this request was written to the worker.</summary>
    public sealed class WorkerTransportDisposedException : ObjectDisposedException
    {
        public WorkerTransportDisposedException()
            : base(nameof(PersistentWorkerTransport), "The TIA Openness worker transport is shut down; the request was not sent.")
        {
        }
    }
}
