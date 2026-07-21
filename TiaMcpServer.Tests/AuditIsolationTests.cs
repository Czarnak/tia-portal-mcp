using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// The tool layer must never reach a process-wide audit directory. Before DI this was impossible
/// to assert: ProjectLifecycleTools resolved WriteSafetyService.Shared, so 39 of 42 records in a
/// real machine's audit trail came from `dotnet test`.
/// </summary>
public class AuditIsolationTests
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

    [Fact]
    public async Task LifecycleTool_WritesAuditOnlyToTheInjectedDirectory()
    {
        var auditDirectory = Path.Combine(Path.GetTempPath(), "tia-audit-" + Guid.NewGuid().ToString("N"));
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaMcpServer",
            "audit");
        var before = Directory.Exists(defaultDirectory)
            ? Directory.GetFiles(defaultDirectory).Length
            : 0;

        try
        {
            var safety = new WriteSafetyService(
                () => DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(10),
                auditDirectory);

            using var client = new OpennessWorkerClient(
                new ProjectSessionBinding(null),
                logger: null,
                workerExecutablePath: LocateFakeWorker());

            var preview = await ProjectLifecycleTools.OpenProject(client, safety, projectPath: "ok");
            using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
            var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

            await ProjectLifecycleTools.OpenProject(
                client,
                safety,
                projectPath: "ok",
                confirm: true,
                safetyToken: token);

            Assert.True(Directory.Exists(auditDirectory));
            Assert.NotEmpty(Directory.GetFiles(auditDirectory));

            var after = Directory.Exists(defaultDirectory)
                ? Directory.GetFiles(defaultDirectory).Length
                : 0;
            Assert.Equal(before, after);
        }
        finally
        {
            if (Directory.Exists(auditDirectory))
            {
                Directory.Delete(auditDirectory, recursive: true);
            }
        }
    }
}
