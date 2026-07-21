using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// The tool layer must never reach a process-wide audit directory. Before dependency injection
/// was introduced, ProjectLifecycleTools resolved a single process-wide WriteSafetyService
/// instance, so 39 of 42 records in a real machine's audit trail came from `dotnet test`.
/// </summary>
public class AuditIsolationTests
{
    /// <summary>
    /// Records are appended as lines to an existing per-day *.jsonl file, so a stray write can
    /// leave the file count in a directory unchanged. Summing line counts across all files is
    /// the only way this assertion can actually detect an unwanted write.
    /// </summary>
    private static int CountAuditLines(string directory)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory).Sum(file => File.ReadAllLines(file).Length)
            : 0;

    [Fact]
    public async Task LifecycleTool_WritesAuditOnlyToTheInjectedDirectory()
    {
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaMcpServer",
            "audit");
        var before = CountAuditLines(defaultDirectory);

        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        using var client = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var preview = await ProjectLifecycleTools.OpenProject(client, safety, projectPath: "ok");
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        await ProjectLifecycleTools.OpenProject(
            client,
            safety,
            projectPath: "ok",
            confirm: true,
            safetyToken: token);

        Assert.True(Directory.Exists(audit.Path));
        Assert.NotEmpty(Directory.GetFiles(audit.Path));

        var after = CountAuditLines(defaultDirectory);
        Assert.Equal(before, after);
    }
}
