using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectWriteToolsBehaviorTests
{
    [Fact]
    public async Task OpenProject_WithTokenButNoConfirm_ReturnsRegisteredConfirmEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await ProjectWriteTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: @"C:\Projects\Line.ap21",
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }

    [Fact]
    public async Task SaveProjectAs_WithTokenButNoConfirm_ReturnsRegisteredConfirmEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await ProjectWriteTools.SaveProjectAs(
            workerClient: null!,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: null,
            rebind: true,
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }

    [Fact]
    public async Task SaveProjectAs_RebindFalse_RejectsBeforePreviewTokenGeneration_OnRegisteredTool()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var response = await ProjectWriteTools.SaveProjectAs(
            workerClient: null!,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: null,
            rebind: false);

        using var doc = JsonDocument.Parse(response);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            doc.RootElement.GetProperty("failureCategory").GetString());
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task SaveProjectAs_Apply_MissingCopiedPath_PropagatesPostconditionFailedAndWarning()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = new WriteSafetyService(
            binding,
            () => DateTimeOffset.UtcNow,
            WriteSafetyService.DefaultTokenLifetime,
            audit.Path);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(
            client,
            binding,
            "save-as-uncertain-state");

        var preview = await ProjectWriteTools.SaveProjectAs(
            client,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: "save-as-uncertain-state",
            rebind: true);
        using var previewDoc = JsonDocument.Parse(preview);
        Assert.True(
            previewDoc.RootElement.TryGetProperty("safetyToken", out var tokenElement),
            preview);
        var token = tokenElement.GetString();

        var applied = await ProjectWriteTools.SaveProjectAs(
            client,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: "save-as-uncertain-state",
            rebind: true,
            confirm: true,
            safetyToken: token);
        using var appliedDoc = JsonDocument.Parse(applied);

        Assert.False(appliedDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.PostconditionFailed,
            appliedDoc.RootElement.GetProperty("failureCategory").GetString());
        Assert.Contains(
            "Project state may have changed",
            appliedDoc.RootElement.GetProperty("warnings")[0].GetString(),
            StringComparison.Ordinal);
    }
}
