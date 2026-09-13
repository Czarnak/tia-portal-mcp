using System.Xml.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ReleaseWorkflowTests
{
    [Fact]
    public void StandalonePublish_UsesResolvedPackageVersion()
    {
        var publishStep = ReadWorkflowStep("Publish Standalone Binary", "Zip Standalone Binary");

        Assert.Contains("/p:Version=$env:PACKAGE_VERSION", publishStep, StringComparison.Ordinal);
        Assert.Contains("/p:PackageVersion=$env:PACKAGE_VERSION", publishStep, StringComparison.Ordinal);
        Assert.Contains("/p:InformationalVersion=$env:PACKAGE_VERSION", publishStep, StringComparison.Ordinal);
        Assert.Contains("/p:IncludeSourceRevisionInInformationalVersion=false", publishStep, StringComparison.Ordinal);
    }

    [Fact]
    public void NuGetPack_UsesExactResolvedInformationalVersion()
    {
        var packStep = ReadWorkflowStep("Build and Pack", "Verify tool package contents");

        Assert.Contains("/p:Version=$env:PACKAGE_VERSION", packStep, StringComparison.Ordinal);
        Assert.Contains("/p:PackageVersion=$env:PACKAGE_VERSION", packStep, StringComparison.Ordinal);
        Assert.Contains("/p:InformationalVersion=$env:PACKAGE_VERSION", packStep, StringComparison.Ordinal);
        Assert.Contains("/p:IncludeSourceRevisionInInformationalVersion=false", packStep, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolAndStandalonePublish_KeepDistinctDeploymentModels()
    {
        var packStep = ReadWorkflowStep("Build and Pack", "Verify tool package contents");
        Assert.DoesNotContain("-r win-x64", packStep, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--self-contained true", packStep, StringComparison.OrdinalIgnoreCase);

        var publishStep = ReadWorkflowStep("Publish Standalone Binary", "Zip Standalone Binary");
        Assert.Contains("-r win-x64", publishStep, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--self-contained true", publishStep, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/p:PackAsTool=false", publishStep, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NuGetToolProject_UsesNet10FrameworkDependentToolLayout()
    {
        var projectPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "TiaMcpServer",
            "TiaMcpServer.csproj"));
        var project = XDocument.Load(projectPath);
        var properties = project.Root?.Element("PropertyGroup");

        Assert.NotNull(properties);
        Assert.Equal("net10.0", properties!.Element("TargetFramework")?.Value);
        Assert.Equal("true", properties.Element("PackAsTool")?.Value, ignoreCase: true);
    }

    private static string ReadWorkflowStep(string stepName, string followingStepName)
    {
        var workflowPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            ".github",
            "workflows",
            "publish.yml"));
        var workflow = File.ReadAllText(workflowPath);
        var stepStart = workflow.IndexOf($"- name: {stepName}", StringComparison.Ordinal);
        var stepEnd = workflow.IndexOf($"- name: {followingStepName}", stepStart, StringComparison.Ordinal);

        Assert.True(stepStart >= 0, $"{stepName} step is missing.");
        Assert.True(stepEnd > stepStart, $"{followingStepName} step must follow {stepName}.");
        return workflow[stepStart..stepEnd];
    }
}
