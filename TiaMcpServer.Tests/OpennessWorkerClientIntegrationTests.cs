using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Spawns the real IPC pipeline against TiaMcpServer.FakeWorker. One class so xunit
/// runs these sequentially; the client's static WorkerGate serializes sends anyway.
/// </summary>
public class OpennessWorkerClientIntegrationTests
{
    private static string LocateFakeWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.FakeWorker", "bin", configuration, "net8.0",
                    "TiaMcpServer.FakeWorker.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent!;
        }

        throw new FileNotFoundException("TiaMcpServer.FakeWorker.exe not found; build the solution first.");
    }

    private static OpennessWorkerClient CreateClient(string? workerPath = null)
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: workerPath ?? LocateFakeWorker());

    [Fact]
    public async Task Success_ReturnsStructuredPayload()
    {
        var result = await CreateClient().GetProjectStatusAsync("ok");

        Assert.True(result.Success);
        Assert.Equal("{\"seq\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task StderrLines_SurfaceAsWarnings()
    {
        var result = await CreateClient().GetProjectStatusAsync("ok-with-stderr");

        Assert.True(result.Success);
        Assert.Single(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("orphan stderr line"));
    }

    [Fact]
    public async Task PayloadStartingWithErrorPrefix_IsNotMisclassified()
    {
        // Regression test for item 1.1: before WorkerCallResult this payload was treated as failure.
        var result = await CreateClient().GetProjectStatusAsync("error-prefix-payload");

        Assert.True(result.Success);
        Assert.StartsWith("Error:", result.Payload);
    }

    [Fact]
    public async Task WorkerReportedError_IsStructuredFailure()
    {
        var result = await CreateClient().GetProjectStatusAsync("worker-error");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
        Assert.Equal("Error: boom", result.ToText());
    }

    [Fact]
    public async Task MalformedResponse_IsFailureNotCrash()
    {
        var result = await CreateClient().GetProjectStatusAsync("malformed");

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SilentExit_SurfacesStderrDetailInError()
    {
        var result = await CreateClient().GetProjectStatusAsync("silent-exit");

        Assert.False(result.Success);
        Assert.Contains("worker crashed during attach", result.Error);
    }

    [Fact]
    public async Task NonExecutableWorkerPath_ProducesActionableWin32Message()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"tia-fake-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(bogus, "not an executable");
        try
        {
            var result = await CreateClient(workerPath: bogus).GetProjectStatusAsync("ok");

            Assert.False(result.Success);
            Assert.Contains(".NET Framework 4.8", result.Error);
            Assert.Contains("openness-worker", result.Error);
        }
        finally
        {
            File.Delete(bogus);
        }
    }
}
