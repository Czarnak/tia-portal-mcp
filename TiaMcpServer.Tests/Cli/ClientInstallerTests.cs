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

    private static ExecutableResolutionResult DefaultResolve(string exe)
        => new(true, exe, $@"C:\tools\{exe}.exe", ExecutableKind.Native, null);

    [Fact]
    public void ClaudeCode_ReadOnly_BuildsCorrectCommand()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

        Assert.Contains("my-tia-server", cmd.Arguments);
    }

    [Fact]
    public void ClaudeCode_WithTiaProject_IncludesProjectFlag()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec(project: @"C:\Projects\Line.ap21");
        var options = CreateOptions(project: @"C:\Projects\Line.ap21");

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

        Assert.Equal("codex", cmd.Executable);
        Assert.Contains("read-write", cmd.Arguments);
    }

    [Fact]
    public void Codex_WithTiaProject_IncludesProjectFlag()
    {
        var installer = new CodexInstaller();
        var spec = CreateSpec(project: @"C:\Projects\Line.ap21");
        var options = CreateOptions(project: @"C:\Projects\Line.ap21");

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

        ExecutableResolutionResult FakeResolve(string exe)
            => new(true, exe, $@"C:\tools\{exe}.exe", ExecutableKind.Native, null);

        var cmd = installer.BuildInstallCommand(options, spec, FakeResolve);

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

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

        Assert.Contains(@"C:\Program Files\Tools\tia-mcp.exe", cmd.Arguments);
    }

    [Fact]
    public void ProjectPathWithSpaces_IsPreserved()
    {
        var installer = new CodexInstaller();
        var projectPath = @"C:\My Projects\TIA Portal\Line.ap21";
        var spec = CreateSpec(project: projectPath);
        var options = CreateOptions(project: projectPath);

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);

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

    // --- Adapter integration: DetectAsync uses resolver ---

    [Fact]
    public async Task ClaudeCode_DetectAsync_WithCmdResolver_ReturnsCmdKind()
    {
        var installer = new ClaudeCodeInstaller();
        ExecutableResolutionResult CmdResolver(string exe)
            => new(true, exe, @"C:\npm\claude.cmd", ExecutableKind.CommandScript, null);

        var result = await installer.DetectAsync(CmdResolver, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(@"C:\npm\claude.cmd", result.ExecutablePath);
        Assert.Equal(ExecutableKind.CommandScript, result.Kind);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Codex_DetectAsync_WithExeResolver_ReturnsNativeKind()
    {
        var installer = new CodexInstaller();
        ExecutableResolutionResult ExeResolver(string exe)
            => new(true, exe, @"C:\tools\codex.exe", ExecutableKind.Native, null);

        var result = await installer.DetectAsync(ExeResolver, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(@"C:\tools\codex.exe", result.ExecutablePath);
        Assert.Equal(ExecutableKind.Native, result.Kind);
    }

    [Fact]
    public async Task OpenCode_DetectAsync_WithCmdResolver_ReturnsCmdKind()
    {
        var installer = new OpenCodeInstaller();
        ExecutableResolutionResult CmdResolver(string exe)
            => new(true, exe, @"C:\npm\opencode.cmd", ExecutableKind.CommandScript, null);

        var result = await installer.DetectAsync(CmdResolver, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(ExecutableKind.CommandScript, result.Kind);
    }

    [Fact]
    public async Task MiMoCode_DetectAsync_WithCmdResolver_ReturnsCmdKind()
    {
        var installer = new MiMoCodeInstaller();
        ExecutableResolutionResult CmdResolver(string exe)
            => new(true, exe, @"C:\npm\mimo.cmd", ExecutableKind.CommandScript, null);

        var result = await installer.DetectAsync(CmdResolver, CancellationToken.None);

        Assert.True(result.Found);
        Assert.Equal(ExecutableKind.CommandScript, result.Kind);
    }

    [Fact]
    public async Task ClaudeCode_DetectAsync_WithNotFoundResolver_ReturnsNotFound()
    {
        var installer = new ClaudeCodeInstaller();
        ExecutableResolutionResult NotFoundResolver(string exe)
            => new(false, exe, null, ExecutableKind.Native, "not found");

        var result = await installer.DetectAsync(NotFoundResolver, CancellationToken.None);

        Assert.False(result.Found);
        Assert.NotNull(result.Error);
        Assert.Contains("Claude Code was not found", result.Error);
    }

    [Fact]
    public async Task Codex_DetectAsync_WithNotFoundResolver_ReturnsNotFound()
    {
        var installer = new CodexInstaller();
        ExecutableResolutionResult NotFoundResolver(string exe)
            => new(false, exe, null, ExecutableKind.Native, "not found");

        var result = await installer.DetectAsync(NotFoundResolver, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Contains("Codex", result.Error);
    }

    [Fact]
    public async Task OpenCode_DetectAsync_WithNotFoundResolver_ReturnsNotFound()
    {
        var installer = new OpenCodeInstaller();
        ExecutableResolutionResult NotFoundResolver(string exe)
            => new(false, exe, null, ExecutableKind.Native, "not found");

        var result = await installer.DetectAsync(NotFoundResolver, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Contains("OpenCode", result.Error);
    }

    [Fact]
    public async Task MiMoCode_DetectAsync_WithNotFoundResolver_ReturnsNotFound()
    {
        var installer = new MiMoCodeInstaller();
        ExecutableResolutionResult NotFoundResolver(string exe)
            => new(false, exe, null, ExecutableKind.Native, "not found");

        var result = await installer.DetectAsync(NotFoundResolver, CancellationToken.None);

        Assert.False(result.Found);
        Assert.Contains("MiMoCode", result.Error);
    }

    // --- Adapter integration: BuildInstallCommand uses runner with resolved path ---

    [Fact]
    public async Task ClaudeCode_CmdShim_RunnerUsesComspec()
    {
        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        ExecutableResolutionResult CmdResolver(string exe)
            => new(true, exe, @"C:\npm\claude.cmd", ExecutableKind.CommandScript, null);

        var detection = await installer.DetectAsync(CmdResolver, CancellationToken.None);
        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);
        cmd = cmd with { ResolvedPath = detection.ExecutablePath, Kind = detection.Kind };

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        var comspec = Environment.GetEnvironmentVariable("COMSPEC")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(comspec, fileName);
        Assert.Equal("/d", args[0]);
        Assert.Equal("/s", args[1]);
        Assert.Equal("/c", args[2]);
        Assert.Equal(@"C:\npm\claude.cmd", args[3]);
    }

    [Fact]
    public async Task Codex_Exe_RunnerUsesResolvedPath()
    {
        var installer = new CodexInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        ExecutableResolutionResult ExeResolver(string exe)
            => new(true, exe, @"C:\tools\codex.exe", ExecutableKind.Native, null);

        var detection = await installer.DetectAsync(ExeResolver, CancellationToken.None);
        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);
        cmd = cmd with { ResolvedPath = detection.ExecutablePath, Kind = detection.Kind };

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        Assert.Equal(@"C:\tools\codex.exe", fileName);
    }

    // --- Regression test: .cmd shim does not cause file-not-found ---

    [Fact]
    public async Task ClaudeCode_CmdShim_DoesNotFailWithFileNotFound()
    {
        // This test reproduces the original bug:
        // Given: command = "claude", where.exe returns a .cmd path
        // When: tia-mcp install claude-code
        // Then: the process runner uses cmd.exe, not the bare "claude" command

        var installer = new ClaudeCodeInstaller();
        var spec = CreateSpec();
        var options = CreateOptions();

        ExecutableResolutionResult CmdResolver(string exe)
            => new(true, exe, @"C:\Users\allan\AppData\Roaming\npm\claude.cmd",
                   ExecutableKind.CommandScript, null);

        var detection = await installer.DetectAsync(CmdResolver, CancellationToken.None);
        Assert.True(detection.Found);
        Assert.Equal(ExecutableKind.CommandScript, detection.Kind);

        var cmd = installer.BuildInstallCommand(options, spec, DefaultResolve);
        cmd = cmd with { ResolvedPath = detection.ExecutablePath, Kind = detection.Kind };

        var (fileName, args) = NativeProcessRunner.BuildProcessArgs(cmd);

        // Must use cmd.exe, not "claude" directly
        var comspec = Environment.GetEnvironmentVariable("COMSPEC")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        Assert.Equal(comspec, fileName);

        // Script path must be after /c
        Assert.Equal("/d", args[0]);
        Assert.Equal("/s", args[1]);
        Assert.Equal("/c", args[2]);
        Assert.Equal(@"C:\Users\allan\AppData\Roaming\npm\claude.cmd", args[3]);

        // MCP args follow
        Assert.Equal("mcp", args[4]);
        Assert.Equal("add", args[5]);
    }
}
