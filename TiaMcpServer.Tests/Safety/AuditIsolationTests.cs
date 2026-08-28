using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

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
        var binding = new ProjectSessionBinding(null);
        var safety = audit.CreateSafety(projectSessionBinding: binding);

        using var client = new OpennessWorkerClient(
            binding,
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

    [Fact]
    public async Task SafetyRejectedApply_WritesNoAuditAndIsNotSuccess()
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

        // Real token from a preview, then apply against a DIFFERENT project path: rejected as
        // binding_conflict before any worker call or audit append. A safety-rejected apply must
        // never be audit-recorded nor rendered as success.
        var preview = await ProjectLifecycleTools.OpenProject(client, safety, projectPath: "C:\\open\\Line.ap21");
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.OpenProject(
            client, safety, projectPath: "C:\\other\\Line.ap21", confirm: true, safetyToken: token);
        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);

        Assert.False(appliedDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.BindingConflict,
            appliedDoc.RootElement.GetProperty("failureCategory").GetString());

        // No audit line in the injected directory, and nothing leaked to the process-wide default.
        Assert.Equal(0, CountAuditLines(audit.Path));
        Assert.Equal(before, CountAuditLines(defaultDirectory));
    }

    [Fact]
    public async Task RejectedSaveProjectAs_RebindFalse_WritesNoAuditAnywhere()
    {
        var defaultDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TiaMcpServer",
            "audit");
        var before = CountAuditLines(defaultDirectory);

        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        // rebind=false is rejected as validation_error before any audit append. workerClient: null!
        // additionally proves the worker was never invoked (any call would NullReferenceException).
        var response = await ProjectLifecycleTools.SaveProjectAs(
            workerClient: null!,
            safety,
            targetDirectory: "C:\\Target",
            targetName: "Copy",
            rebind: false);

        using var doc = System.Text.Json.JsonDocument.Parse(response);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());

        // No audit lines in the injected directory, and nothing leaked to the process-wide default.
        Assert.Equal(0, CountAuditLines(audit.Path));
        Assert.Equal(before, CountAuditLines(defaultDirectory));
    }
}
