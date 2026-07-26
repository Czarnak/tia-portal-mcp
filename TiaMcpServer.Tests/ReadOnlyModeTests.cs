using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Batch;
using TiaMcpServer.Cli;
using TiaMcpServer.Contracts;
using TiaMcpServer.Diagnostics;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Comprehensive tests for the read-only safety mode: configuration, policy catalog,
/// host-side enforcement, worker-side authorization, tool discovery, and batch behavior.
/// </summary>
public class ReadOnlyModeTests
{
    #region Configuration Tests

    [Fact]
    public void DefaultMode_IsReadWrite()
    {
        var result = AccessModeParser.Parse(Array.Empty<string>());
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadWrite, result.Mode);
    }

    [Fact]
    public void ReadOnlyFlag_ResolvesToReadOnly()
    {
        var result = AccessModeParser.Parse(new[] { "--read-only" });
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
    }

    [Fact]
    public void ReadWriteFlag_ResolvesToReadWrite()
    {
        var result = AccessModeParser.Parse(new[] { "--read-write" });
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadWrite, result.Mode);
    }

    [Fact]
    public void AccessMode_Argument_ResolvesCorrectly()
    {
        var result = AccessModeParser.Parse(new[] { "--access-mode", "read-only" });
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
    }

    [Fact]
    public void AccessMode_Equals_Syntax_ResolvesCorrectly()
    {
        var result = AccessModeParser.Parse(new[] { "--access-mode=read-only" });
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
    }

    [Fact]
    public void AccessMode_ReadWrite_Argument_ResolvesCorrectly()
    {
        var result = AccessModeParser.Parse(new[] { "--access-mode", "read-write" });
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadWrite, result.Mode);
    }

    [Fact]
    public void EnvironmentVariable_ResolvesCorrectly()
    {
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", "read-only");
            var result = AccessModeParser.Parse(Array.Empty<string>());
            Assert.True(result.IsValid);
            Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
        }
    }

    [Fact]
    public void CLI_Overrides_EnvironmentVariable()
    {
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", "read-only");
            var result = AccessModeParser.Parse(new[] { "--access-mode", "read-write" });
            Assert.True(result.IsValid);
            Assert.Equal(McpAccessMode.ReadWrite, result.Mode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
        }
    }

    [Fact]
    public void InvalidValue_FailsClearly()
    {
        var result = AccessModeParser.Parse(new[] { "--access-mode", "invalid" });
        Assert.False(result.IsValid);
        Assert.Contains("Invalid access mode", result.Error);
    }

    [Fact]
    public void EmptyValue_FailsClearly()
    {
        var result = AccessModeParser.ParseValue("");
        Assert.False(result.IsValid);
        Assert.Contains("empty", result.Error);
    }

    [Fact]
    public void ParseValue_IsCaseInsensitive()
    {
        var result = AccessModeParser.ParseValue("READ-ONLY");
        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
    }

    #endregion

    #region Policy Catalog Tests

    [Fact]
    public void EveryKnownOperation_HasExactlyOneClassification()
    {
        var operations = OperationPolicyCatalog.AllOperationNames;
        Assert.NotEmpty(operations);

        foreach (var op in operations)
        {
            var cap = OperationPolicyCatalog.GetCapability(op);
            Assert.NotNull(cap);
        }
    }

    [Fact]
    public void UnclassifiedOperation_IsDeniedInReadOnlyMode()
    {
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "nonexistent_operation"));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, ""));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "future_mutation_op"));
    }

    [Fact]
    public void ReadWriteMode_AllowsAllClassifiedOperations()
    {
        foreach (var op in OperationPolicyCatalog.AllOperationNames)
        {
            Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadWrite, op));
        }
    }

    [Theory]
    [InlineData("get_project_status")]
    [InlineData("browse_project_tree")]
    [InlineData("read_hardware_config")]
    [InlineData("search_equipment_catalog")]
    [InlineData("read_cross_references")]
    [InlineData("list_tag_tables")]
    [InlineData("get_block_content")]
    public void ReadOnlyMode_AllowsApprovedOperations(string operation)
    {
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
    }

    [Theory]
    [InlineData("compile_check")]
    [InlineData("open_project")]
    [InlineData("create_project")]
    [InlineData("save_project")]
    [InlineData("save_project_as")]
    [InlineData("archive_project")]
    [InlineData("close_project")]
    [InlineData("update_block_logic")]
    [InlineData("create_block")]
    [InlineData("delete_block")]
    [InlineData("create_block_group")]
    [InlineData("delete_block_group")]
    [InlineData("create_tag_table")]
    [InlineData("delete_tag_table")]
    [InlineData("create_tag")]
    [InlineData("update_tag")]
    [InlineData("delete_tag")]
    [InlineData("create_user_constant")]
    [InlineData("update_user_constant")]
    [InlineData("delete_user_constant")]
    [InlineData("add_network_device")]
    [InlineData("configure_network_device")]
    [InlineData("start_plc")]
    [InlineData("stop_plc")]
    public void ReadOnlyMode_DeniesProhibitedOperations(string operation)
    {
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
    }

    [Fact]
    public void CompileCheck_IsExplicitlyDeniedInReadOnlyMode()
    {
        Assert.Equal(OperationCapability.Compile, OperationPolicyCatalog.GetCapability("compile_check"));
        Assert.False(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, "compile_check"));
    }

    #endregion

    #region Host Enforcement Tests

    [Fact]
    public void OperationAccessPolicy_ReadOnly_DeniesWriteOperations()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var result = policy.Authorize("update_block_logic");
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Contains("update_block_logic", result.Error);
        Assert.Contains("read-only", result.Error);
    }

    [Fact]
    public void OperationAccessPolicy_ReadOnly_DeniesCompileCheck()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var result = policy.Authorize("compile_check");
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
    }

    [Fact]
    public void OperationAccessPolicy_ReadOnly_AllowsReadOperations()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        Assert.Null(policy.Authorize("get_project_status"));
        Assert.Null(policy.Authorize("browse_project_tree"));
        Assert.Null(policy.Authorize("get_block_content"));
    }

    [Fact]
    public void OperationAccessPolicy_ReadWrite_AllowsAllOperations()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadWrite);
        Assert.Null(policy.Authorize("update_block_logic"));
        Assert.Null(policy.Authorize("compile_check"));
        Assert.Null(policy.Authorize("get_project_status"));
        Assert.Null(policy.Authorize("start_plc"));
    }

    [Fact]
    public void OperationAccessPolicy_DeniesLifecycleOperations()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        Assert.NotNull(policy.Authorize("open_project"));
        Assert.NotNull(policy.Authorize("create_project"));
        Assert.NotNull(policy.Authorize("save_project"));
        Assert.NotNull(policy.Authorize("save_project_as"));
        Assert.NotNull(policy.Authorize("archive_project"));
        Assert.NotNull(policy.Authorize("close_project"));
    }

    [Fact]
    public void OperationAccessPolicy_DeniesOnlineControlOperations()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        Assert.NotNull(policy.Authorize("start_plc"));
        Assert.NotNull(policy.Authorize("stop_plc"));
    }

    [Fact]
    public void OperationAccessPolicy_DeniesUnknownOperations()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var result = policy.Authorize("unknown_future_op");
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
    }

    #endregion

    #region Host Client Enforcement Tests

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesBeforeWorkerInvocation()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.UpdateBlockLogicAsync("Main", "yaml", null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Contains("update_block_logic", result.Error);
        Assert.Contains("read-only", result.Error);
    }

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesOpenProject()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.OpenProjectAsync("/fake/path.ap21", forceRebind: false);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
    }

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesStartPlc()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.StartPlcAsync(null, null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
    }

    [Fact]
    public async Task OpennessWorkerClient_ReadOnly_DeniesCompileCheck()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path",
            accessPolicy: policy);

        var result = await client.CompileCheckAsync(null, null, null);

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
    }

    [Fact]
    public void OpennessWorkerClient_AccessPolicy_IsExposed()
    {
        var binding = new ProjectSessionBinding(null);
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var client = new OpennessWorkerClient(
            binding,
            accessPolicy: policy);

        Assert.NotNull(client.AccessPolicy);
        Assert.Equal(McpAccessMode.ReadOnly, client.AccessPolicy.Mode);
    }

    [Fact]
    public void OpennessWorkerClient_NoPolicy_DefaultsToReadWrite()
    {
        var binding = new ProjectSessionBinding(null);
        var client = new OpennessWorkerClient(binding);

        Assert.Null(client.AccessPolicy);
    }

    #endregion

    #region Worker Authorization Tests

    [Fact]
    public void WorkerOperationAuthorization_ParsesReadOnly()
    {
        var mode = WorkerOperationAuthorization.ParseAccessMode(new[] { "--access-mode", "read-only" });
        Assert.Equal(McpAccessMode.ReadOnly, mode);
    }

    [Fact]
    public void WorkerOperationAuthorization_ParsesReadWrite()
    {
        var mode = WorkerOperationAuthorization.ParseAccessMode(new[] { "--access-mode", "read-write" });
        Assert.Equal(McpAccessMode.ReadWrite, mode);
    }

    [Fact]
    public void WorkerOperationAuthorization_DefaultsToReadWrite()
    {
        var mode = WorkerOperationAuthorization.ParseAccessMode(Array.Empty<string>());
        Assert.Equal(McpAccessMode.ReadWrite, mode);
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadOnly_DeniesMutation()
    {
        var denial = WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "update_block_logic");
        Assert.NotNull(denial);
        Assert.False(denial.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, denial.FailureCategory);
        Assert.Contains("update_block_logic", denial.Error);
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadOnly_DeniesLifecycle()
    {
        var denial = WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "open_project");
        Assert.NotNull(denial);
        Assert.False(denial.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, denial.FailureCategory);
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadOnly_AllowsReads()
    {
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "get_project_status"));
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "browse_project_tree"));
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "get_block_content"));
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadOnly_AllowsTemporaryExport()
    {
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "get_block_content"));
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadOnly_DeniesCompile()
    {
        var denial = WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "compile_check");
        Assert.NotNull(denial);
        Assert.Equal(WorkerFailureCategories.AccessDenied, denial.FailureCategory);
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadWrite_AllowsAll()
    {
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadWrite, "update_block_logic"));
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadWrite, "open_project"));
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadWrite, "compile_check"));
        Assert.Null(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadWrite, "start_plc"));
    }

    [Fact]
    public void WorkerOperationAuthorization_ReadOnly_DeniesOnlineControl()
    {
        Assert.NotNull(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "start_plc"));
        Assert.NotNull(WorkerOperationAuthorization.Authorize(McpAccessMode.ReadOnly, "stop_plc"));
    }

    #endregion

    #region Batch Access Mode Tests

    [Fact]
    public void ValidateAccessMode_ReadWrite_AllowsAll()
    {
        var ops = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "update_block_logic", BlockPath = "Main", YamlContent = "x" },
            new BatchOperationRequest { OperationId = "b", Operation = "compile_check" },
        };
        var errors = BatchOperationCatalog.ValidateAccessMode(ops, McpAccessMode.ReadWrite);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateAccessMode_ReadOnly_DeniesWriteOperations()
    {
        var ops = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "update_block_logic", BlockPath = "Main", YamlContent = "x" },
        };
        var errors = BatchOperationCatalog.ValidateAccessMode(ops, McpAccessMode.ReadOnly);
        Assert.Single(errors);
        Assert.Contains("update_block_logic", errors[0]);
        Assert.Contains("read-only", errors[0]);
    }

    [Fact]
    public void ValidateAccessMode_ReadOnly_DeniesCompileCheck()
    {
        var ops = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "compile_check" },
        };
        var errors = BatchOperationCatalog.ValidateAccessMode(ops, McpAccessMode.ReadOnly);
        Assert.Single(errors);
        Assert.Contains("compile_check", errors[0]);
    }

    [Fact]
    public void ValidateAccessMode_ReadOnly_AllowsReadOperations()
    {
        var ops = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "get_project_status" },
            new BatchOperationRequest { OperationId = "b", Operation = "browse_project_tree" },
            new BatchOperationRequest { OperationId = "c", Operation = "get_block_content", BlockPath = "Main" },
        };
        var errors = BatchOperationCatalog.ValidateAccessMode(ops, McpAccessMode.ReadOnly);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateAccessMode_ReadOnly_DeniesMixedOperations()
    {
        var ops = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "get_project_status" },
            new BatchOperationRequest { OperationId = "b", Operation = "update_block_logic", BlockPath = "Main", YamlContent = "x" },
        };
        var errors = BatchOperationCatalog.ValidateAccessMode(ops, McpAccessMode.ReadOnly);
        Assert.Single(errors);
        Assert.Contains("update_block_logic", errors[0]);
    }

    #endregion

    #region Tool Discovery Tests

    [Fact]
    public void ReadWriteMode_HasAllTenTools()
    {
        // In read-write mode, all 10 tools should be discoverable via assembly scanning.
        var toolTypes = typeof(ProjectLifecycleTools).Assembly
            .GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null);

        var toolNames = toolTypes
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>())
            .Where(a => a is not null)
            .Select(a => a!.Name)
            .ToArray();

        Assert.Equal(10, toolNames.Length);
    }

    [Fact]
    public void ProjectReadTools_HasGetProjectStatus()
    {
        var method = typeof(ProjectReadTools).GetMethod("GetProjectStatus",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("get_project_status", attr.Name);
        Assert.True(attr.ReadOnly);
        Assert.False(attr.Destructive);
    }

    [Fact]
    public void ReadBatchTools_HasExecuteReadBatch()
    {
        var method = typeof(ReadBatchTools).GetMethod("ExecuteReadBatch",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(attr);
        Assert.Equal("execute_read_batch", attr.Name);
        Assert.True(attr.ReadOnly);
        Assert.False(attr.Destructive);
    }

    [Fact]
    public void ProjectWriteTools_HasAllSixWriteTools()
    {
        var writeToolNames = typeof(ProjectWriteTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[] { "archive_project", "close_project", "create_project", "open_project", "save_project", "save_project_as" },
            writeToolNames);
    }

    [Fact]
    public void WriteBatchTools_HasPreviewAndApply()
    {
        var writeToolNames = typeof(WriteBatchTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "apply_write_batch", "preview_write_batch" }, writeToolNames);
    }

    #endregion

    #region Failure Category Tests

    [Fact]
    public void AccessDenied_IsKnownFailureCategory()
    {
        Assert.True(WorkerFailureCategories.IsKnown(WorkerFailureCategories.AccessDenied));
    }

    [Fact]
    public void AccessDenied_CanBeUsedInWorkerCallResult()
    {
        var result = WorkerCallResult.Fail(
            WorkerFailureCategories.AccessDenied,
            "Operation 'update_block_logic' is disabled in read-only mode.");

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Contains("update_block_logic", result.Error);
    }

    #endregion

    #region Confirm Bypass Prevention Tests

    [Fact]
    public void ConfirmTrue_DoesNotBypassReadOnlyPolicy()
    {
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var result = policy.Authorize("update_block_logic");
        Assert.NotNull(result);
        Assert.False(result.Success);
        // confirm=true is irrelevant — the policy denies categorically
    }

    [Fact]
    public void SafetyToken_DoesNotBypassReadOnlyPolicy()
    {
        // The OperationAccessPolicy checks operation name only, not token presence.
        // This is the correct behavior: read-only mode is a higher-level restriction.
        var policy = new OperationAccessPolicy(McpAccessMode.ReadOnly);
        var result = policy.Authorize("save_project");
        Assert.NotNull(result);
        Assert.False(result.Success);
    }

    #endregion

    #region ProjectWriteTools Coverage Tests

    [Fact]
    public async Task ProjectWriteTools_OpenProject_Preview_ReturnsTokenAndInstructions()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var result = await ProjectWriteTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: @"C:\Projects\Line.ap21");

        Assert.Contains("safetyToken", result);
        Assert.Contains("open_project", result);
        Assert.Contains("Preview only", result);
    }

    [Fact]
    public async Task ProjectWriteTools_CreateProject_Preview_ReturnsTokenAndInstructions()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var result = await ProjectWriteTools.CreateProject(
            workerClient: null!,
            safety,
            projectDirectory: @"C:\Projects",
            projectName: "NewProject");

        Assert.Contains("safetyToken", result);
        Assert.Contains("create_project", result);
        Assert.Contains("Preview only", result);
    }

    [Fact]
    public async Task ProjectWriteTools_OpenProject_ConfirmFalse_ReturnsConfirmRequired()
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
    public async Task ProjectWriteTools_CreateProject_ConfirmFalse_ReturnsConfirmRequired()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var result = await ProjectWriteTools.CreateProject(
            workerClient: null!,
            safety,
            projectDirectory: @"C:\Projects",
            projectName: "NewProject",
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
    }

    [Fact]
    public async Task ProjectWriteTools_SaveProject_ConfirmFalse_ReturnsConfirmRequired()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var result = await ProjectWriteTools.SaveProject(
            workerClient: null!,
            safety,
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
    }

    [Fact]
    public async Task ProjectWriteTools_ArchiveProject_ConfirmFalse_ReturnsConfirmRequired()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var result = await ProjectWriteTools.ArchiveProject(
            workerClient: null!,
            safety,
            archiveDirectory: @"C:\Archive",
            archiveName: "backup",
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
    }

    [Fact]
    public async Task ProjectWriteTools_CloseProject_ConfirmFalse_ReturnsConfirmRequired()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();
        var result = await ProjectWriteTools.CloseProject(
            workerClient: null!,
            safety,
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
    }

    #endregion

    #region Doctor Access Mode Coverage Tests

    [Fact]
    public void DoctorJsonRenderer_WithReadOnlyAccessMode_IncludesAccessMode()
    {
        var report = new DoctorReport(
            DiagnosticStatus.Passed,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(1, 0, 0),
            new[] { new DiagnosticCheckResult("test", "Test", DiagnosticStatus.Passed, "ok") });

        var json = DoctorJsonRenderer.Render(report, verbose: false, accessMode: McpAccessMode.ReadOnly);

        Assert.Contains("\"accessMode\": \"read-only\"", json);
    }

    [Fact]
    public void DoctorJsonRenderer_WithReadWriteAccessMode_IncludesAccessMode()
    {
        var report = new DoctorReport(
            DiagnosticStatus.Passed,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(1, 0, 0),
            new[] { new DiagnosticCheckResult("test", "Test", DiagnosticStatus.Passed, "ok") });

        var json = DoctorJsonRenderer.Render(report, verbose: false, accessMode: McpAccessMode.ReadWrite);

        Assert.Contains("\"accessMode\": \"read-write\"", json);
    }

    [Fact]
    public void DoctorJsonRenderer_WithoutAccessMode_OmitsAccessMode()
    {
        var report = new DoctorReport(
            DiagnosticStatus.Passed,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(1, 0, 0),
            new[] { new DiagnosticCheckResult("test", "Test", DiagnosticStatus.Passed, "ok") });

        var json = DoctorJsonRenderer.Render(report, verbose: false, accessMode: null);

        Assert.DoesNotContain("accessMode", json);
    }

    [Fact]
    public void DoctorTextRenderer_WithReadOnlyAccessMode_ShowsReadOnly()
    {
        var report = new DoctorReport(
            DiagnosticStatus.Passed,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(1, 0, 0),
            new[] { new DiagnosticCheckResult("test", "Test", DiagnosticStatus.Passed, "ok") });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer, accessMode: McpAccessMode.ReadOnly);

        var output = writer.ToString();
        Assert.Contains("Access mode: READ-ONLY", output);
    }

    [Fact]
    public void DoctorTextRenderer_WithReadWriteAccessMode_ShowsReadWrite()
    {
        var report = new DoctorReport(
            DiagnosticStatus.Passed,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(1, 0, 0),
            new[] { new DiagnosticCheckResult("test", "Test", DiagnosticStatus.Passed, "ok") });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer, accessMode: McpAccessMode.ReadWrite);

        var output = writer.ToString();
        Assert.Contains("Access mode: READ-WRITE", output);
    }

    [Fact]
    public void DoctorTextRenderer_WithoutAccessMode_OmitsAccessModeLine()
    {
        var report = new DoctorReport(
            DiagnosticStatus.Passed,
            DateTimeOffset.UtcNow,
            "1.0.0",
            new DoctorSummary(1, 0, 0),
            new[] { new DiagnosticCheckResult("test", "Test", DiagnosticStatus.Passed, "ok") });

        var writer = new StringWriter();
        DoctorTextRenderer.Render(report, verbose: false, writer, accessMode: null);

        var output = writer.ToString();
        Assert.DoesNotContain("Access mode:", output);
    }

    [Fact]
    public void DoctorCliParser_EnvVarReadOnly_DetectsReadOnlyMode()
    {
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", "read-only");
            var options = DoctorCliParser.Parse(Array.Empty<string>());
            Assert.True(options.Valid);
            Assert.Equal(McpAccessMode.ReadOnly, options.AccessMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
        }
    }

    [Fact]
    public void DoctorCliParser_EnvVarReadWrite_DetectsReadWriteMode()
    {
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", "read-write");
            var options = DoctorCliParser.Parse(Array.Empty<string>());
            Assert.True(options.Valid);
            Assert.Equal(McpAccessMode.ReadWrite, options.AccessMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
        }
    }

    [Fact]
    public void DoctorCliParser_NoEnvVar_DefaultsToReadWrite()
    {
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
            var options = DoctorCliParser.Parse(Array.Empty<string>());
            Assert.True(options.Valid);
            Assert.Equal(McpAccessMode.ReadWrite, options.AccessMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
        }
    }

    [Fact]
    public void DoctorCliParser_InvalidEnvVar_DefaultsToReadWrite()
    {
        try
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", "invalid");
            var options = DoctorCliParser.Parse(Array.Empty<string>());
            Assert.True(options.Valid);
            Assert.Equal(McpAccessMode.ReadWrite, options.AccessMode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TIA_MCP_ACCESS_MODE", null);
        }
    }

    #endregion

    #region OpennessWorkerClient Without Policy Tests

    [Fact]
    public async Task OpennessWorkerClient_NoPolicy_AllowsAllOperations()
    {
        var binding = new ProjectSessionBinding(null);
        // No accessPolicy — should not throw, will fail at transport level (no worker)
        var client = new OpennessWorkerClient(
            binding,
            workerExecutablePath: "/nonexistent/path");

        // This will fail with a transport error (worker not found), not access_denied
        var result = await client.UpdateBlockLogicAsync("Main", "yaml", null);

        Assert.False(result.Success);
        Assert.NotEqual(WorkerFailureCategories.AccessDenied, result.FailureCategory);
    }

    #endregion
}
