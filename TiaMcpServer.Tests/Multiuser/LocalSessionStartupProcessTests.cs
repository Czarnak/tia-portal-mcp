using System.Diagnostics;
using System.Security.Cryptography;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Mcp protocol serial")]
public sealed class LocalSessionStartupProcessTests
{
    private const string Amc = "C:/Projects/Local.amc21";
    private const string Als = "C:/Projects/Local.als21";

    [Theory]
    [InlineData("split-amc", null, true)]
    [InlineData("equals-amc", null, true)]
    [InlineData("environment-amc", Amc, true)]
    [InlineData("cli-overrides-environment", Als, true)]
    [InlineData("split-als", null, false)]
    [InlineData("environment-als", Als, false)]
    [InlineData("als-overrides-environment", Amc, false)]
    public async Task BuiltHost_ConfiguredAssertionUsesAmcAndRejectsAls(
        string scenario, string? environmentPath, bool expectedSuccess)
    {
        using var bundle = new TempAuditDirectory();
        var info = StartInfo(bundle.Path, scenario switch
        {
            "split-amc" or "cli-overrides-environment" => ["--project", Amc],
            "equals-amc" => [$"--project={Amc}"],
            "split-als" or "als-overrides-environment" => ["--project", Als],
            _ => []
        }, environmentPath);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Built host did not start.");
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(
                serverInput: process.StandardInput.BaseStream,
                serverOutput: process.StandardOutput.BaseStream), cancellationToken: timeout.Token);
            var result = await client.CallToolAsync("bind_project", new Dictionary<string, object?>());
            var document = result.StructuredContent!.Value;
            Assert.Equal(expectedSuccess, document.GetProperty("success").GetBoolean());
            if (expectedSuccess)
            {
                var binding = document.GetProperty("result").GetProperty("value").GetProperty("binding");
                Assert.Equal(ProjectPathNormalization.Canonicalize(Amc), binding.GetProperty("projectPath").GetString());
                Assert.Equal(ProjectContainerKinds.LocalSession,
                    binding.GetProperty("context").GetProperty("containerKind").GetString());
            }
            else
                Assert.Equal(WorkerFailureCategories.ValidationError,
                    document.GetProperty("error").GetProperty("category").GetString());

            var methods = File.Exists(LogPath(bundle.Path)) ? File.ReadAllLines(LogPath(bundle.Path)) : [];
            Assert.DoesNotContain("open_project", methods);
            if (!expectedSuccess) Assert.Empty(methods);
        }
        catch (Exception ex)
        {
            await StopOwnChildAsync(process);
            var startupError = await stderr;
            var failureKind = startupError.Contains("FileNotFoundException", StringComparison.Ordinal)
                ? "missing_dependency"
                : startupError.Contains("Unhandled exception.", StringComparison.Ordinal)
                    ? "startup_exception"
                    : "none";
            throw new InvalidOperationException(
                $"Built host exited={process.HasExited}, code={process.ExitCode}; startup failure={failureKind}", ex);
        }
        finally
        {
            await StopOwnChildAsync(process);
        }
    }

    [Fact]
    public async Task BuiltHost_DuplicateProjectOptionsExitBeforeMcpOrWorker()
    {
        using var bundle = new TempAuditDirectory();
        var info = StartInfo(bundle.Path, ["--project", Amc, $"--project={Als}"], null);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Built host did not start.");
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(1, process.ExitCode);
            Assert.Contains("--project may be specified only once", await process.StandardError.ReadToEndAsync(),
                StringComparison.Ordinal);
            Assert.False(File.Exists(LogPath(bundle.Path)));
        }
        finally
        {
            await StopOwnChildAsync(process);
        }
    }

    private static ProcessStartInfo StartInfo(string directory, string[] projectArguments, string? environmentPath)
    {
        Directory.CreateDirectory(directory);
        var fakeExe = FakeWorkerLocator.Locate();
        var fakeDirectory = Path.GetDirectoryName(fakeExe)!;
        var repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var hostDirectory = Path.Combine(repository, "TiaMcpServer", "bin", "Debug", "net10.0");
        var hostExe = Path.Combine(hostDirectory, "TiaMcpServer.exe");
        Assert.True(File.Exists(hostExe), "Build the host stub project before running the built-host process tests.");
        Assert.True(File.Exists(Path.Combine(fakeDirectory, "TiaMcpServer.FakeWorker.dll")));
        Assert.True(File.Exists(Path.Combine(fakeDirectory, "TiaMcpServer.FakeWorker.runtimeconfig.json")));
        Assert.True(File.Exists(Path.Combine(fakeDirectory, "TiaMcpServer.FakeWorker.deps.json")));

        foreach (var source in Directory.GetFiles(hostDirectory))
        {
            Assert.False(Path.GetFileName(source).StartsWith("Siemens.", StringComparison.OrdinalIgnoreCase));
            File.Copy(source, Path.Combine(directory, Path.GetFileName(source)));
        }
        // The host dependency manifest resolves Windows-specific logging assemblies
        // from runtimes/win, so preserve that subtree as well as top-level assets.
        var runtimeDirectory = Path.Combine(hostDirectory, "runtimes");
        Assert.True(Directory.Exists(runtimeDirectory));
        foreach (var source in Directory.GetFiles(runtimeDirectory, "*", SearchOption.AllDirectories))
        {
            Assert.False(Path.GetFileName(source).StartsWith("Siemens.", StringComparison.OrdinalIgnoreCase));
            var target = Path.Combine(directory, Path.GetRelativePath(hostDirectory, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
        }
        Assert.True(File.Exists(Path.Combine(directory, "runtimes", "win", "lib", "net10.0",
            "System.Diagnostics.EventLog.dll")));
        var workerDirectory = Path.Combine(directory, "openness-worker");
        Directory.CreateDirectory(workerDirectory);
        foreach (var source in Directory.GetFiles(fakeDirectory))
        {
            Assert.False(Path.GetFileName(source).StartsWith("Siemens.", StringComparison.OrdinalIgnoreCase));
            File.Copy(source, Path.Combine(workerDirectory, Path.GetFileName(source)));
        }
        var bundledWorker = Path.Combine(workerDirectory, "TiaMcpServer.OpennessWorker.exe");
        File.Copy(fakeExe, bundledWorker);
        Assert.True(File.Exists(bundledWorker));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(fakeExe)),
            SHA256.HashData(File.ReadAllBytes(bundledWorker)));
        Assert.False(File.Exists(Path.Combine(workerDirectory, "TiaMcpServer.OpennessWorker.dll")));

        var info = new ProcessStartInfo(Path.Combine(directory, "TiaMcpServer.exe"))
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = directory
        };
        info.ArgumentList.Add("--read-only");
        foreach (var argument in projectArguments) info.ArgumentList.Add(argument);
        if (environmentPath is null) info.Environment.Remove("TIA_MCP_PROJECT_PATH");
        else info.Environment["TIA_MCP_PROJECT_PATH"] = environmentPath;
        info.Environment["TIA_MCP_FAKE_WORKER_PORTALS"] = $"42|{ProjectPathNormalization.Canonicalize(Amc)}|ui|0|false";
        info.Environment["TIA_MCP_FAKE_WORKER_UI_OPEN_PROJECT"] = Amc;
        info.Environment["TIA_MCP_FAKE_WORKER_REQUEST_LOG"] = LogPath(directory);
        return info;
    }

    private static string LogPath(string directory) => Path.Combine(directory, "worker-requests.log");

    private static async Task StopOwnChildAsync(Process process)
    {
        if (process.HasExited) return;
        try { process.StandardInput.Close(); }
        catch (ObjectDisposedException) { }
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }
}
