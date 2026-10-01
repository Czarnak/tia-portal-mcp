using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Safety;

public class WriteToolSafetyTokenTests
{
    [Theory]
    [InlineData("PreviewOpenProject")]
    [InlineData("PreviewCreateProject")]
    [InlineData("PreviewSaveProject")]
    [InlineData("PreviewSaveProjectAs")]
    [InlineData("PreviewArchiveProject")]
    [InlineData("PreviewCloseProject")]
    public void SeparatePreviewToolsAreGone(string methodName)
    {
        Assert.Null(typeof(ProjectWriteTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance));
    }

    [Fact]
    public void ProjectReadAndLifecycleSurfaceIsExactlyEightTools()
    {
        // ProjectReadTools exposes two reads; ProjectWriteTools exposes six lifecycle writes.
        var readToolNames = typeof(ProjectReadTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToArray();

        var writeToolNames = typeof(ProjectWriteTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToArray();

        var allToolNames = readToolNames.Concat(writeToolNames).OrderBy(name => name).ToArray();

        Assert.Equal(
            new[]
            {
                "archive_project", "browse_project_tree", "close_project", "create_project", "get_project_status",
                "open_project", "save_project", "save_project_as"
            },
            allToolNames);
    }

    [Fact]
    public void PreviewCurrentStateReadFailure_ReturnsCategorizedEnvelopeAndWarnings()
    {
        using var audit = new TempAuditDirectory();

        var result = WriteSafetyTooling.CreatePreview(
            audit.CreateSafety(),
            "apply_write_batch",
            "C:\\Projects\\Line.ap21",
            new { projectPath = "C:\\Projects\\Line.ap21" },
            "Apply edits to the active TIA Portal project.",
            new { projectPath = "C:\\Projects\\Line.ap21" },
            WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerTimeout,
                "Timed out while reading current project state.",
                new[] { "Worker stderr was captured." }));

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        Assert.Equal("apply_write_batch", root.GetProperty("toolName").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, root.GetProperty("failureCategory").GetString());
        Assert.Equal("Timed out while reading current project state.", root.GetProperty("error").GetString());
        Assert.Equal("Worker stderr was captured.", root.GetProperty("warnings")[0].GetString());
    }

    // --- Exhaustive structural category mapping for every ValidateAndConsume rejection reason ---
    // Categories are carried structurally on WriteSafetyValidationResult, never inferred from the
    // human error text. One token is issued for tool "apply_write_batch" against project A / state A,
    // then each rejection reason is provoked in isolation and asserted to its mapped category.

    private const string ProjectA = "C:\\Projects\\A.ap21";
    private const string StateA = "STATE-A";

    private static object TargetA => new { projectPath = ProjectA };
    private static object InputA => new { projectPath = ProjectA, operation = "update_tag" };

    private static string IssueBatchToken(WriteSafetyService safety)
    {
        var previewJson = safety.CreatePreview(
            toolName: "apply_write_batch",
            projectPath: ProjectA,
            target: TargetA,
            summary: "Apply batch to project A.",
            requestedInput: InputA,
            currentState: StateA);
        return ReadToken(previewJson);
    }

    [Fact]
    public void ValidateAndConsume_MissingToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);

        var result = safety.ValidateAndConsume(null, "apply_write_batch", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_UnknownToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);

        var result = safety.ValidateAndConsume("bogus-token", "apply_write_batch", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_ExpiredToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var now = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        var safety = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));
        var token = IssueBatchToken(safety);

        now = now.AddMinutes(11);
        var result = safety.ValidateAndConsume(token, "apply_write_batch", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_DifferentTool_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        var token = IssueBatchToken(safety);

        var result = safety.ValidateAndConsume(token, "network_write", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_DifferentProjectPath_IsBindingConflict()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        var token = IssueBatchToken(safety);

        // Only the projectPath argument differs; the project-path mismatch (reason 5) fires before
        // the target check and maps to binding_conflict, never validation_error.
        var result = safety.ValidateAndConsume(token, "apply_write_batch", "C:\\Projects\\B.ap21", TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_DifferentTarget_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        var token = IssueBatchToken(safety);

        // projectPath matches (reason 5 passes) but the target JSON differs (reason 6).
        var result = safety.ValidateAndConsume(
            token, "apply_write_batch", ProjectA, new { projectPath = ProjectA, extra = 1 }, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_ChangedInput_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        var token = IssueBatchToken(safety);

        // projectPath and target match; only the requested input differs (reason 7).
        var result = safety.ValidateAndConsume(
            token, "apply_write_batch", ProjectA, TargetA, new { projectPath = ProjectA, operation = "delete_tag" }, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_ChangedCurrentState_IsStateChanged()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        var token = IssueBatchToken(safety);

        // Everything matches except the current project state (reason 8) -> state_changed.
        var result = safety.ValidateAndConsume(token, "apply_write_batch", ProjectA, TargetA, InputA, "STATE-B");

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.FailureCategory);
    }

    [Fact]
    public async Task ValidateForApplyAsync_MissingToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);

        var context = await WriteSafetyTooling.ValidateForApplyAsync(
            safety, safetyToken: null, "preview_write_batch", "apply_write_batch",
            ProjectA, TargetA, InputA,
            () => Task.FromResult(WorkerCallResult.Ok(StateA)));

        Assert.False(context.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, context.FailureCategory);
    }

    [Fact]
    public async Task ValidateForApplyAsync_CurrentStateReadFailure_CarriesTheReadFailureCategory()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, workerExecutablePath: "worker-must-not-start.exe", accessPolicy: new OperationAccessPolicy(McpAccessMode.Full));
        var safety = audit.CreateSafety(projectSessionBinding: binding);
        var token = IssueBatchToken(safety);

        // The pre-write current-state read itself fails with an uncertain-outcome category; the
        // apply context must carry that real category through, not invent a new one.
        var context = await WriteSafetyTooling.ValidateForApplyAsync(
            safety, token, "preview_write_batch", "apply_write_batch",
            ProjectA, TargetA, InputA,
            () => Task.FromResult(WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerTimeout,
                "The write outcome is unknown. Inspect current project state before retrying.")));

        Assert.False(context.IsValid);
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, context.FailureCategory);
    }

    private static string ReadToken(string previewJson)
    {
        using var doc = JsonDocument.Parse(previewJson);
        return doc.RootElement.GetProperty("safetyToken").GetString()!;
    }
}
