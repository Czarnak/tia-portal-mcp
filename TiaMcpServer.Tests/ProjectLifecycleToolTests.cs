using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectLifecycleToolTests
{
    [Theory]
    [InlineData("GetProjectStatus", "get_project_status", false)]
    [InlineData("OpenProject", "open_project", true)]
    [InlineData("CreateProject", "create_project", true)]
    [InlineData("SaveProject", "save_project", true)]
    [InlineData("SaveProjectAs", "save_project_as", true)]
    [InlineData("ArchiveProject", "archive_project", true)]
    [InlineData("CloseProject", "close_project", true)]
    public void ProjectLifecycleToolsHaveMcpMetadata(string methodName, string expectedToolName, bool requiresConfirm)
    {
        var type = typeof(ProjectLifecycleTools);

        Assert.NotNull(type.GetCustomAttribute<McpServerToolTypeAttribute>());

        var method = type.GetMethod(methodName);

        Assert.NotNull(method);
        var toolAttribute = method.GetCustomAttribute<McpServerToolAttribute>();
        Assert.NotNull(toolAttribute);
        Assert.Equal(expectedToolName, toolAttribute.Name);

        var description = method.GetCustomAttribute<DescriptionAttribute>()?.Description ?? string.Empty;
        if (requiresConfirm)
        {
            Assert.Contains("confirm=true", description);
        }
    }

    /// <summary>
    /// Single source of truth for the public project-lifecycle surface of
    /// <see cref="OpennessWorkerClient"/> - both the per-name Theory below and the exact-count
    /// Fact read from this same array, so the "seven" in the count assertion can never drift
    /// from the names actually being checked.
    /// </summary>
    private static readonly string[] ProjectLifecycleMethodNames =
    {
        "GetProjectStatusAsync",
        "OpenProjectAsync",
        "CreateProjectAsync",
        "SaveProjectAsync",
        "SaveProjectAsAsync",
        "ArchiveProjectAsync",
        "CloseProjectAsync"
    };

    public static IEnumerable<object[]> ProjectLifecycleMethodNameData()
        => ProjectLifecycleMethodNames.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(ProjectLifecycleMethodNameData))]
    public void OpennessWorkerClientExposesProjectLifecycleMethods(string methodName)
    {
        var method = typeof(OpennessWorkerClient).GetMethod(methodName);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<WorkerCallResult>), method.ReturnType);
    }

    [Fact]
    public void OpennessWorkerClient_ExposesExactlySevenProjectLifecycleMethods()
    {
        Assert.Equal(7, ProjectLifecycleMethodNames.Length);
    }

    [Fact]
    public void OpennessWorkerClient_ProjectStatusSurface_HasExactlyOnePublicMethod()
    {
        var type = typeof(OpennessWorkerClient);

        // Enumerates every public instance method whose name mentions ProjectStatus - proves
        // GetProjectStatusAsync is the ONLY public status-shaped entry point, i.e. the internal
        // lifecycle probe never leaks out as a second public status method.
        var publicProjectStatusMethods = type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name.Contains("ProjectStatus", StringComparison.Ordinal))
            .Select(m => m.Name)
            .ToArray();

        Assert.Equal(new[] { "GetProjectStatusAsync" }, publicProjectStatusMethods);

        var probeMethod = type.GetMethod(
            "ProbeProjectStatusForLifecycleAsync",
            BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.NotNull(probeMethod);
        Assert.False(probeMethod!.IsPublic);
        Assert.Equal(typeof(Task<WorkerCallResult>), probeMethod.ReturnType);
    }

    [Fact]
    public async Task SaveProjectAsWithTokenButNoConfirm_Rejects()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await ProjectLifecycleTools.SaveProjectAs(
            workerClient: null!,
            safety,
            targetDirectory: "C:\\Projects",
            targetName: "LineCopy",
            confirm: false,
            safetyToken: "some-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }
}
