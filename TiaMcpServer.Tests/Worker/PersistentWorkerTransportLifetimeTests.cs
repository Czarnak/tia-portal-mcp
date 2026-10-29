using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Network;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class PersistentWorkerTransportLifetimeTests
{
    [Theory]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\"}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{}}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[],\"PRIVATE_MARKER\":true}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"DeviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[],\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"DeviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":null,\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":null}")]
    [InlineData("\"ConfigureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":null,\"configureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[]}")]
    [InlineData("\"configureNetworkDeviceOutcome\":null,\"ConfigureNetworkDeviceOutcome\":{\"deviceName\":\"PLC_1\",\"appliedSettings\":{},\"messages\":[]}")]
    public async Task MalformedConfigureOutcome_FailsClosedWithoutReplayOrWorkerRestart(string member)
    {
        using var wire = new ConfigureWireFixture("{\"success\":false,\"failureCategory\":\"engineering_rejected\",\"error\":\"PRIVATE_MARKER\",\"warnings\":[\"PRIVATE_MARKER\"]," + member + "}");

        var response = await wire.Transport.SendAsync(new() { Method = "configure_network_device" });

        Assert.False(response.Success);
        Assert.Equal("protocol_error", response.FailureCategory);
        Assert.Null(response.ConfigureNetworkDeviceOutcome);
        Assert.Null(response.Payload);
        Assert.Null(response.EngineeringError);
        Assert.Null(response.Warnings);
        Assert.Null(response.SessionIdentity);
        Assert.Null(response.ResolvedProjectPath);
        Assert.DoesNotContain("PRIVATE_MARKER", response.Error);
        var item = NetworkPayloadContract.Project(new() { OperationId = "configure", Operation = "configure_network_device" },
            WorkerCallResult.Fail(response.FailureCategory!, response.Error!));
        Assert.Null(item.Result);
        Assert.Equal("protocol_error", item.Failure!.Category);

        var followup = await wire.Transport.SendAsync(new() { Method = "get_project_status" });
        Assert.True(followup.Success, followup.Error);
        Assert.Equal("2", followup.Payload);
        Assert.Equal(new[] { "configure_network_device", "get_project_status" }, File.ReadAllLines(wire.RequestLog));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfigureFailure_AbsentOrNullOutcomeKeepsUnknownAppliedState(bool explicitNull)
    {
        using var wire = new ConfigureWireFixture("{\"success\":false,\"failureCategory\":\"worker_operation_failed\",\"error\":\"Synthetic failure\""
            + (explicitNull ? ",\"configureNetworkDeviceOutcome\":null}" : "}"));
        var response = await wire.Transport.SendAsync(new() { Method = "configure_network_device" });
        Assert.Equal("worker_operation_failed", response.FailureCategory);
        Assert.Null(response.ConfigureNetworkDeviceOutcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConfigureFailure_LiveL11cAndL13miniOutcomesPreserveCapturedSubset(bool partial)
    {
        // Exact result and Siemens messages from the committed 2026-10-29 rerun inputs.
        var outcome = partial
            ? """{"appliedSettings":{"Address":"192.168.203.42","SubnetMask":"255.255.254.0"},"deviceName":"S7-1500/ET200MP station_2","messages":[]}"""
            : """{"appliedSettings":{},"deviceName":"S7-1500/ET200MP station_2","messages":[]}""";
        var message = partial
            ? "Error when calling method 'ConnectToSubnet' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nAssignment to subnet failed."
            : "Error when calling method 'set_Address' of type 'Siemens.Engineering.HW.Node'.\r\n\r\nnot-an-ip is not a valid IP address";
        var raw = "{\"success\":false,\"failureCategory\":\"engineering_rejected\",\"error\":"
            + JsonSerializer.Serialize("TIA Portal rejected the operation: " + message)
            + ",\"engineeringError\":{\"type\":\"EngineeringTargetInvocationException\",\"message\":" + JsonSerializer.Serialize(message)
            + ",\"inner\":[]},\"configureNetworkDeviceOutcome\":" + outcome + "}";
        using var wire = new ConfigureWireFixture(raw);

        var response = await wire.Transport.SendAsync(new() { Method = "configure_network_device" });

        Assert.False(response.Success);
        Assert.Equal("engineering_rejected", response.FailureCategory);
        Assert.Equal(message, response.EngineeringError!.Message);
        Assert.Equal("EngineeringTargetInvocationException", response.EngineeringError.Type);
        Assert.Empty(response.EngineeringError.Inner);
        Assert.Equal("S7-1500/ET200MP station_2", response.ConfigureNetworkDeviceOutcome!.DeviceName);
        Assert.Empty(response.ConfigureNetworkDeviceOutcome.Messages);
        if (partial)
        {
            Assert.Equal(2, response.ConfigureNetworkDeviceOutcome.AppliedSettings.Count);
            Assert.Equal("192.168.203.42", response.ConfigureNetworkDeviceOutcome.AppliedSettings["Address"]);
            Assert.Equal("255.255.254.0", response.ConfigureNetworkDeviceOutcome.AppliedSettings["SubnetMask"]);
        }
        else Assert.Empty(response.ConfigureNetworkDeviceOutcome.AppliedSettings);
    }

    [Fact]
    public async Task SendAfterDispose_ThrowsDisposedBeforeStartingAWorker()
    {
        // A missing executable would surface as Win32Exception if a start were attempted.
        var transport = new PersistentWorkerTransport(
            Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid().ToString("N") + ".exe"),
            requestTimeout: TimeSpan.FromSeconds(5));
        transport.Dispose();

        await Assert.ThrowsAsync<PersistentWorkerTransport.WorkerTransportDisposedException>(() =>
            transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectPath = "ok" }));
    }

    [Fact]
    public async Task DisposeDuringInFlightRequest_EndsItWithoutObjectDisposedException()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var requests = new FakeWorkerRequestLog(directory.Path);
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("ok");
        using var transport = new PersistentWorkerTransport(
            FakeWorkerLocator.Locate(),
            requestTimeout: TimeSpan.FromMinutes(5));

        var inFlight = transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectDirectory = "hang" });
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!WorkerReceived(requests, "get_project_status") && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }
        Assert.True(WorkerReceived(requests, "get_project_status"));

        transport.Dispose();

        var exception = await Record.ExceptionAsync(() => inFlight.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.NotNull(exception);
        Assert.IsNotAssignableFrom<ObjectDisposedException>(exception);
        Assert.IsNotType<TimeoutException>(exception);
    }

    [Fact]
    public async Task RequestLogHeldByReader_WorkerWaitsInsteadOfCrashing()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var requests = new FakeWorkerRequestLog(directory.Path);
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("ok");
        using var transport = new PersistentWorkerTransport(
            FakeWorkerLocator.Locate(),
            requestTimeout: TimeSpan.FromSeconds(30));
        File.WriteAllText(requests.Path, "");

        Task<WorkerResponse> send;
        // Opened the way File.ReadAllLines opens it, as a polling test does mid-request.
        using (new FileStream(requests.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            send = transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectPath = "ok" });
            await Task.WhenAny(send, Task.Delay(TimeSpan.FromSeconds(1)));
        }

        var response = await send;
        Assert.True(response.Success, response.Error);
        Assert.Contains("get_project_status", requests.Methods());
    }

    [Fact]
    public async Task HangingRequest_TimesOutKillsWorkerAndNextRequestStartsFresh()
    {
        using var uiOpen = FakeWorkerUiOpenProject.ForWorkerRelativePath("ok");
        using var transport = new PersistentWorkerTransport(
            FakeWorkerLocator.Locate(),
            requestTimeout: TimeSpan.FromSeconds(2));
        Assert.True((await transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectPath = "ok" })).Success);

        var timeout = await Assert.ThrowsAsync<TimeoutException>(() =>
            transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectDirectory = "hang" }));
        var recovered = await transport.SendAsync(new WorkerRequest { Method = "get_project_status", ProjectPath = "ok" });

        Assert.Contains("did not respond within 2 seconds", timeout.Message);
        Assert.True(recovered.Success, recovered.Error);
        Assert.Equal("{\"seq\":1}", recovered.Payload);
    }

    private static bool WorkerReceived(FakeWorkerRequestLog requests, string method)
    {
        try
        {
            return requests.Methods().Contains(method);
        }
        catch (IOException)
        {
            return false; // The worker is appending to the log right now.
        }
    }

    private sealed class ConfigureWireFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "configure-wire-" + Guid.NewGuid().ToString("N"));
        public PersistentWorkerTransport Transport { get; }
        public string RequestLog => Path.Combine(directory, "requests");
        public ConfigureWireFixture(string response)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "response"), response);
            File.WriteAllText(Path.Combine(directory, "hello"), JsonSerializer.Serialize(new WorkerResponse
            { Success = true, ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() }, WorkerJson.Envelope));
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), """
                param([string]$Root)
                $sequence = 0
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    if ($r.method -eq 'hello') { $response = [IO.File]::ReadAllText((Join-Path $Root 'hello')) }
                    else {
                        $sequence++
                        [IO.File]::AppendAllText((Join-Path $Root 'requests'), $r.method + [Environment]::NewLine)
                        $response = if ($r.method -eq 'get_project_status') { '{"success":true,"payload":"' + $sequence + '"}' }
                            else { [IO.File]::ReadAllText((Join-Path $Root 'response')) }
                    }
                    [Console]::Out.WriteLine($response); [Console]::Out.Flush()
                }
                """);
            Transport = new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
                TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
        }
        public void Dispose() { Transport.Dispose(); Directory.Delete(directory, true); }
    }
}
