using TiaMcpServer.Cli.Install;
using Xunit;

namespace TiaMcpServer.Tests.Cli;

public class ClientInstallerTests
{
    private static McpLaunchSpec CreateSpec(string name = "tia-portal", string? project = null)
    {
        var args = new List<string> { "--access-mode", "read-only" };
        if (project is not null)
        {
            args.Add("--project");
            args.Add(project);
        }

        return new McpLaunchSpec(name, @"C:\Users\User\.dotnet\tools\tia-mcp.exe", args);
    }

    private static InstallOptions CreateOptions(string accessMode = "read-only", string? project = null, string? serverPath = null, bool json = false, bool dryRun = false)
    {
        return new InstallOptions(true, ClientKind.Codex, "tia-portal", accessMode, project, serverPath, dryRun, json, false, null);
    }

    [Fact]
    public void ClaudeCode_ReadOnly_BuildsCorrectCommand()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Equal("claude", cmd.Executable);
        Assert.False(cmd.Interactive);
        Assert.Equal(new[] { "mcp", "add", "--scope", "user", "tia-portal", "--", @"C:\Users\User\.dotnet\tools\tia-mcp.exe", "--access-mode", "read-only" }, cmd.Arguments);
    }

    [Fact]
    public void ClaudeCode_ReadWrite_BuildsCorrectCommand()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions("read-write");

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Equal("claude", cmd.Executable);
        Assert.Contains("--access-mode", cmd.Arguments);
        Assert.Contains("read-write", cmd.Arguments);
    }

    [Fact]
    public void ClaudeCode_CustomServerName_UsesName()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec("my-tia-server");
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Contains("my-tia-server", cmd.Arguments);
    }

    [Fact]
    public void ClaudeCode_WithTiaProject_IncludesProjectFlag()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec(project: @"C:\Projects\Line.ap21");
        var options = CreateOptions(project: @"C:\Projects\Line.ap21");

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Contains("--project", cmd.Arguments);
        Assert.Contains(@"C:\Projects\Line.ap21", cmd.Arguments);
    }

    [Fact]
    public void ClaudeCode_VerificationCommand_UsesGet()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildVerificationCommand(options, spec);

        Assert.NotNull(cmd);
        Assert.Equal("claude", cmd!.Executable);
        Assert.Equal(new[] { "mcp", "get", "tia-portal" }, cmd.Arguments);
    }

    [Fact]
    public void Codex_ReadOnly_BuildsCorrectCommand()
    {
        var installer = new CodexInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Equal("codex", cmd.Executable);
        Assert.False(cmd.Interactive);
        Assert.Equal(new[] { "mcp", "add", "tia-portal", "--", @"C:\Users\User\.dotnet\tools\tia-mcp.exe", "--access-mode", "read-only" }, cmd.Arguments);
    }

    [Fact]
    public void Codex_ReadWrite_BuildsCorrectCommand()
    {
        var installer = new CodexInstaller();
        var spec = CreateSpec();
        var options = CreateOptions("read-write");

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Equal("codex", cmd.Executable);
        Assert.Contains("read-write", cmd.Arguments);
    }

    [Fact]
    public void Codex_WithTiaProject_IncludesProjectFlag()
    {
        var installer = new CodexInstaller();
        var spec = CreateSpec(project: @"C:\Projects\Line.ap21");
        var options = CreateOptions(project: @"C:\Projects\Line.ap21");

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Contains("--project", cmd.Arguments);
        Assert.Contains(@"C:\Projects\Line.ap21", cmd.Arguments);
    }

    [Fact]
    public void Codex_VerificationCommand_UsesGetJson()
    {
        var installer = new CodexInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildVerificationCommand(options, spec);

        Assert.NotNull(cmd);
        Assert.Equal("codex", cmd!.Executable);
        Assert.Equal(new[] { "mcp", "get", "tia-portal", "--json" }, cmd.Arguments);
    }

    [Fact]
    public void OpenCode_ReadOnly_BuildsCorrectCommand()
    {
        var installer = new OpenCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Equal("opencode", cmd.Executable);
        Assert.False(cmd.Interactive);
        Assert.Equal(new[] { "mcp", "add", "tia-portal", "--", @"C:\Users\User\.dotnet\tools\tia-mcp.exe", "--access-mode", "read-only" }, cmd.Arguments);
    }

    [Fact]
    public void OpenCode_WithTiaProject_IncludesProjectFlag()
    {
        var installer = new OpenCodeInstaller();
        var spec = CreateSpec(project: @"C:\Projects\Line.ap21");
        var options = CreateOptions(project: @"C:\Projects\Line.ap21");

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Contains("--project", cmd.Arguments);
        Assert.Contains(@"C:\Projects\Line.ap21", cmd.Arguments);
    }

    [Fact]
    public void OpenCode_VerificationCommand_UsesList()
    {
        var installer = new OpenCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildVerificationCommand(options, spec);

        Assert.NotNull(cmd);
        Assert.Equal("opencode", cmd!.Executable);
        Assert.Equal(new[] { "mcp", "list" }, cmd.Arguments);
    }

    [Fact]
    public void MiMoCode_BuildsInteractiveCommand()
    {
        var installer = new MiMoCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Equal("mimo", cmd.Executable);
        Assert.True(cmd.Interactive);
        Assert.Equal(new[] { "mcp", "add" }, cmd.Arguments);
    }

    [Fact]
    public void MiMoCode_VerificationCommand_UsesList()
    {
        var installer = new MiMoCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildVerificationCommand(options, spec);

        Assert.NotNull(cmd);
        Assert.Equal("mimo", cmd!.Executable);
        Assert.Equal(new[] { "mcp", "list" }, cmd.Arguments);
    }

    [Fact]
    public void ExecutablePathWithSpaces_IsPreserved()
    {
        var installer = new CodexInstaller();
        var spec = new McpLaunchSpec("tia-portal", @"C:\Program Files\Tools\tia-mcp.exe", new[] { "--access-mode", "read-only" });
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Contains(@"C:\Program Files\Tools\tia-mcp.exe", cmd.Arguments);
    }

    [Fact]
    public void ProjectPathWithSpaces_IsPreserved()
    {
        var installer = new CodexInstaller();
        var projectPath = @"C:\My Projects\TIA Portal\Line.ap21";
        var spec = CreateSpec(project: projectPath);
        var options = CreateOptions(project: projectPath);

        var cmd = installer.BuildInstallCommand(options, spec);

        Assert.Contains(projectPath, cmd.Arguments);
    }

    [Fact]
    public void Registry_AllKinds_ReturnInstaller()
    {
        foreach (ClientKind kind in Enum.GetValues(typeof(ClientKind)))
        {
            var installer = ClientInstallerRegistry.GetInstaller(kind);
            Assert.Equal(kind, installer.Client);
        }
    }

    [Fact]
    public void ClaudeCode_DetectAsync_UsesWhereExe()
    {
        var installer = new ClaudeCodeInstaller();
        Assert.Equal(ClientKind.ClaudeCode, installer.Client);
    }

    [Fact]
    public void Codex_DetectAsync_UsesWhereExe()
    {
        var installer = new CodexInstaller();
        Assert.Equal(ClientKind.Codex, installer.Client);
    }

    [Fact]
    public void OpenCode_DetectAsync_UsesWhereExe()
    {
        var installer = new OpenCodeInstaller();
        Assert.Equal(ClientKind.OpenCode, installer.Client);
    }

    [Fact]
    public void MiMoCode_DetectAsync_UsesWhereExe()
    {
        var installer = new MiMoCodeInstaller();
        Assert.Equal(ClientKind.MiMoCode, installer.Client);
    }
}
