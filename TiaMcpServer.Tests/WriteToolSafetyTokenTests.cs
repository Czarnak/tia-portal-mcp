using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

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
        // Check both the old and new classes — neither should have separate preview tools.
        Assert.Null(typeof(ProjectLifecycleTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static));
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
    public async Task WriteToolWithoutToken_ReturnsPreviewWithTokenAndInstructions()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: "C:\\Projects\\Line.ap21");

        using var doc = JsonDocument.Parse(result);
        Assert.Equal("open_project", doc.RootElement.GetProperty("toolName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("safetyToken").GetString()));
        Assert.Contains("confirm=true", doc.RootElement.GetProperty("instructions").GetString());
    }

    [Fact]
    public void PreviewCurrentStateReadFailure_ReturnsCategorizedEnvelopeAndWarnings()
    {
        using var audit = new TempAuditDirectory();

        var result = WriteSafetyTooling.CreatePreview(
            audit.CreateSafety(),
            "save_project",
            "C:\\Projects\\Line.ap21",
            new { projectPath = "C:\\Projects\\Line.ap21" },
            "Save the active TIA Portal project.",
            new { projectPath = "C:\\Projects\\Line.ap21" },
            WorkerCallResult.Fail(
                WorkerFailureCategories.WorkerTimeout,
                "Timed out while reading current project state.",
                new[] { "Worker stderr was captured." }));

        using var document = JsonDocument.Parse(result);
        var root = document.RootElement;
        Assert.Equal("save_project", root.GetProperty("toolName").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.WorkerTimeout, root.GetProperty("failureCategory").GetString());
        Assert.Equal("Timed out while reading current project state.", root.GetProperty("error").GetString());
        Assert.Equal("Worker stderr was captured.", root.GetProperty("warnings")[0].GetString());
    }

    [Fact]
    public async Task WriteToolWithTokenButNoConfirm_RejectsBeforeAnyWork()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        // confirm=false is caller input error: it must render as a categorized validation_error
        // envelope (never a raw string), so a small model reads success/category, not prose.
        var result = await ProjectLifecycleTools.CloseProject(
            workerClient: null!,
            safety,
            confirm: false,
            safetyToken: "some-token");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("close_project", root.GetProperty("toolName").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
        Assert.Contains("confirm=true", root.GetProperty("error").GetString());
        Assert.Contains("without safetyToken", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WriteToolWithBadToken_PointsBackAtTheTokenlessCall()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        // An unknown token is a validation_error: it must render as a categorized envelope whose
        // error still points back at the tokenless preview call.
        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: "C:\\Projects\\Line.ap21",
            confirm: true,
            safetyToken: "bogus-token");

        using var doc = JsonDocument.Parse(result);
        var root = doc.RootElement;
        Assert.Equal("open_project", root.GetProperty("toolName").GetString());
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
        Assert.Contains("Safety token", root.GetProperty("error").GetString());
        Assert.Contains("open_project (without safetyToken)", root.GetProperty("error").GetString());
    }

    [Fact]
    public async Task WriteToolWithChangedProjectPath_RendersBindingConflictEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        // Token issued for project A; applying against project B is a project-path mismatch. That
        // is binding_conflict (reason 5), rejected before any worker call (workerClient null!).
        var preview = await ProjectLifecycleTools.OpenProject(
            workerClient: null!, safety, projectPath: "C:\\Projects\\A.ap21");
        var token = ReadToken(preview);

        var applied = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: "C:\\Projects\\B.ap21",
            confirm: true,
            safetyToken: token);

        using var doc = JsonDocument.Parse(applied);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.BindingConflict, root.GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task WriteToolWithChangedInput_RendersValidationErrorEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        // Same project path and target, but a changed non-path input field (forceRebind flips
        // false -> true) is a reordered/changed-input mismatch (reason 7): validation_error.
        var preview = await ProjectLifecycleTools.OpenProject(
            workerClient: null!, safety, projectPath: "C:\\Projects\\A.ap21");
        var token = ReadToken(preview);

        var applied = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: "C:\\Projects\\A.ap21",
            confirm: true,
            safetyToken: token,
            forceRebind: true);

        using var doc = JsonDocument.Parse(applied);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task WriteToolWithUsedToken_RendersValidationErrorEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        using var client = new OpennessWorkerClient(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        // "C:\\open\\Line.ap21" reports itself back as the resolved path, so the first apply
        // succeeds and consumes the token. Replaying the SAME token is a consumed-token mismatch
        // (reason 2): validation_error, rendered as an envelope, worker never re-invoked.
        const string projectPath = "C:\\open\\Line.ap21";
        var token = ReadToken(await ProjectLifecycleTools.OpenProject(client, safety, projectPath: projectPath));

        var firstApply = await ProjectLifecycleTools.OpenProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using (var firstDoc = JsonDocument.Parse(firstApply))
        {
            Assert.True(firstDoc.RootElement.GetProperty("success").GetBoolean());
        }

        var secondApply = await ProjectLifecycleTools.OpenProject(
            client, safety, projectPath: projectPath, confirm: true, safetyToken: token);
        using var secondDoc = JsonDocument.Parse(secondApply);
        var root = secondDoc.RootElement;
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal(WorkerFailureCategories.ValidationError, root.GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task WriteToolWithChangedCurrentState_RendersStateChangedEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        // open_project's current-state read is DescribePathState(projectPath): a filesystem snapshot.
        // Preview while the path is absent, then create it before apply -> the current state hash no
        // longer matches the token -> state_changed (reason 8), rejected before any worker call.
        var projectPath = Path.Combine(Path.GetTempPath(), $"tia-state-{Guid.NewGuid():N}.ap21");
        try
        {
            var token = ReadToken(await ProjectLifecycleTools.OpenProject(
                workerClient: null!, safety, projectPath: projectPath));

            await File.WriteAllTextAsync(projectPath, "the project now exists on disk");

            var applied = await ProjectLifecycleTools.OpenProject(
                workerClient: null!, safety, projectPath: projectPath, confirm: true, safetyToken: token);

            using var doc = JsonDocument.Parse(applied);
            var root = doc.RootElement;
            Assert.False(root.GetProperty("success").GetBoolean());
            Assert.Equal(WorkerFailureCategories.StateChanged, root.GetProperty("failureCategory").GetString());
        }
        finally
        {
            if (File.Exists(projectPath))
            {
                File.Delete(projectPath);
            }
        }
    }

    // --- Exhaustive structural category mapping for every ValidateAndConsume rejection reason ---
    // Categories are carried structurally on WriteSafetyValidationResult, never inferred from the
    // human error text. One token is issued for tool "open_project" against project A / state A,
    // then each rejection reason is provoked in isolation and asserted to its mapped category.

    private const string ProjectA = "C:\\Projects\\A.ap21";
    private const string StateA = "STATE-A";

    private static object TargetA => new { projectPath = ProjectA };
    private static object InputA => new { projectPath = ProjectA, forceRebind = false };

    private static string IssueOpenProjectToken(WriteSafetyService safety)
    {
        var previewJson = safety.CreatePreview(
            toolName: "open_project",
            projectPath: ProjectA,
            target: TargetA,
            summary: "Open project A.",
            requestedInput: InputA,
            currentState: StateA);
        return ReadToken(previewJson);
    }

    [Fact]
    public void ValidateAndConsume_MissingToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = safety.ValidateAndConsume(null, "open_project", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_UnknownToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = safety.ValidateAndConsume("bogus-token", "open_project", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_ExpiredToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var now = new DateTimeOffset(2026, 7, 23, 10, 0, 0, TimeSpan.Zero);
        var safety = audit.CreateSafety(() => now, TimeSpan.FromMinutes(10));
        var token = IssueOpenProjectToken(safety);

        now = now.AddMinutes(11);
        var result = safety.ValidateAndConsume(token, "open_project", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_DifferentTool_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var token = IssueOpenProjectToken(safety);

        var result = safety.ValidateAndConsume(token, "save_project", ProjectA, TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_DifferentProjectPath_IsBindingConflict()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var token = IssueOpenProjectToken(safety);

        // Only the projectPath argument differs; the project-path mismatch (reason 5) fires before
        // the target check and maps to binding_conflict, never validation_error.
        var result = safety.ValidateAndConsume(token, "open_project", "C:\\Projects\\B.ap21", TargetA, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_DifferentTarget_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var token = IssueOpenProjectToken(safety);

        // projectPath matches (reason 5 passes) but the target JSON differs (reason 6).
        var result = safety.ValidateAndConsume(
            token, "open_project", ProjectA, new { projectPath = ProjectA, extra = 1 }, InputA, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_ChangedInput_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var token = IssueOpenProjectToken(safety);

        // projectPath and target match; only the requested input differs (reason 7).
        var result = safety.ValidateAndConsume(
            token, "open_project", ProjectA, TargetA, new { projectPath = ProjectA, forceRebind = true }, StateA);

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
    }

    [Fact]
    public void ValidateAndConsume_ChangedCurrentState_IsStateChanged()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var token = IssueOpenProjectToken(safety);

        // Everything matches except the current project state (reason 8) -> state_changed.
        var result = safety.ValidateAndConsume(token, "open_project", ProjectA, TargetA, InputA, "STATE-B");

        Assert.False(result.IsValid);
        Assert.Equal(WorkerFailureCategories.StateChanged, result.FailureCategory);
    }

    [Fact]
    public async Task ValidateForApplyAsync_MissingToken_IsValidationError()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var context = await WriteSafetyTooling.ValidateForApplyAsync(
            safety, safetyToken: null, "open_project (without safetyToken)", "open_project",
            ProjectA, TargetA, InputA,
            () => Task.FromResult(WorkerCallResult.Ok(StateA)));

        Assert.False(context.IsValid);
        Assert.Equal(WorkerFailureCategories.ValidationError, context.FailureCategory);
    }

    [Fact]
    public async Task ValidateForApplyAsync_CurrentStateReadFailure_CarriesTheReadFailureCategory()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var token = IssueOpenProjectToken(safety);

        // The pre-write current-state read itself fails with an uncertain-outcome category; the
        // apply context must carry that real category through, not invent a new one.
        var context = await WriteSafetyTooling.ValidateForApplyAsync(
            safety, token, "open_project (without safetyToken)", "open_project",
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
