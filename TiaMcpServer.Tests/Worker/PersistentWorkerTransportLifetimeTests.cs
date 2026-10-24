using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class PersistentWorkerTransportLifetimeTests
{
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
}
