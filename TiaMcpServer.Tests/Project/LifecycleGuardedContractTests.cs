using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class LifecycleGuardedContractTests
{
    [Theory]
    [InlineData(nameof(ProjectWriteTools.OpenProject))]
    [InlineData(nameof(ProjectWriteTools.CreateProject))]
    [InlineData(nameof(ProjectWriteTools.SaveProject))]
    [InlineData(nameof(ProjectWriteTools.SaveProjectAs))]
    [InlineData(nameof(ProjectWriteTools.ArchiveProject))]
    [InlineData(nameof(ProjectWriteTools.CloseProject))]
    public void PublicLifecycleMethods_ExposeSingleCallStructuredContract(string methodName)
    {
        var method = typeof(ProjectWriteTools).GetMethod(methodName)!;
        Assert.Equal(typeof(Task<CallToolResult>), method.ReturnType);
        var inputs = method.GetParameters().Select(parameter => parameter.Name).ToArray();
        Assert.Contains("dryRun", inputs);
        Assert.Contains("acknowledge", inputs);
        Assert.DoesNotContain("confirm", inputs);
        Assert.DoesNotContain("safetyToken", inputs);
    }

    [Fact]
    public void LifecyclePayload_WritesRequiredNullMembers()
    {
        Assert.Null(typeof(ProjectLifecycleResultInfo).GetCustomAttribute<LegacyNullOmissionAttribute>());
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "close_project"
        }));
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("projectPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("project").ValueKind);
    }

    [Fact]
    public void NoProjectVerification_IsFullOnly_WithoutRequiringAProjectIdentity()
    {
        Assert.False(OperationPolicyCatalog.RequiresExpectedSessionIdentity("get_basic_project_status"));
        Assert.Equal(WorkerFailureCategories.AccessDenied,
            new OperationAccessPolicy(McpAccessMode.ReadWrite).Authorize("get_basic_project_status")!.FailureCategory);
        Assert.Null(new OperationAccessPolicy(McpAccessMode.Full).Authorize("get_basic_project_status"));
        foreach (var mutation in new[] { "save_project", "save_project_as", "archive_project", "close_project" })
            Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(mutation));
    }
}
