using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public class TagUpdateSafetyLiveHarnessContractTests
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-test-update-tag-safety.ps1");

    [Fact]
    public void Script_DefaultModeIsReadOnly()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"\[ValidateSet\(\s*'Read'\s*,\s*'PreviewDrift'\s*,\s*'ApplyDrift'\s*,\s*'ProbeUnavailable'\s*\)\]"), text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Read'"), text);
    }

    [Fact]
    public void Script_ApplyDriftRequiresExplicitAuthorizationAndPreflightedReadableFlag()
    {
        var text = ReadScript();
        var main = ExtractTopLevelFunction(text, "Invoke-Main");
        var applyGuard = main.IndexOf(
            "if ($Mode -eq 'ApplyDrift' -and -not $AllowApply)",
            StringComparison.Ordinal);
        var mainTry = main.IndexOf("try {\n        $script:WorkerProcess", StringComparison.Ordinal);

        Assert.True(applyGuard >= 0, "Expected an explicit ApplyDrift AllowApply guard.");
        Assert.True(mainTry > applyGuard, "AllowApply must be checked before the harness starts a child process.");
    }

    [Fact]
    public void Script_InternalSafetyReadCarriesObservedSessionIdentity()
    {
        var text = ReadScript();
        var identityReader = ExtractTopLevelFunction(text, "Get-CompleteSessionIdentity");
        var safetyReader = ExtractTopLevelFunction(text, "Read-UpdateTagSafetySnapshot");
        var main = ExtractTopLevelFunction(text, "Invoke-Main");
        var mainTry = main.IndexOf("try {\n        $script:WorkerProcess", StringComparison.Ordinal);
        var statusCall = main.IndexOf("Get-CompleteSessionIdentity", mainTry, StringComparison.Ordinal);
        var firstSafetyRead = main.IndexOf("Read-UpdateTagSafetySnapshot", statusCall + 1, StringComparison.Ordinal);

        Assert.Contains("get_project_status", identityReader, StringComparison.Ordinal);
        Assert.Contains("$script:WorkerSessionIdentity = $identity", identityReader, StringComparison.Ordinal);
        Assert.Contains("expectedSessionIdentity = $script:WorkerSessionIdentity", safetyReader, StringComparison.Ordinal);
        Assert.DoesNotContain("expectedSessionIdentity = @{", safetyReader, StringComparison.Ordinal);
        Assert.True(statusCall >= 0 && firstSafetyRead > statusCall,
            "The entry point must establish the observed identity before any safety read.");
    }

    [Fact]
    public void Script_OptionalUnavailableProbeUsesSeparateTargetInputs()
    {
        var text = ReadScript();
        var probeGuard = ExtractTopLevelFunction(text, "Assert-OptionalProbeTargetIsDistinct");
        var main = ExtractTopLevelFunction(text, "Invoke-Main");
        var entryPoint = main.IndexOf("if ($Mode -eq 'ProbeUnavailable')", StringComparison.Ordinal);
        var guardCall = main.IndexOf("Assert-OptionalProbeTargetIsDistinct", entryPoint, StringComparison.Ordinal);

        Assert.Contains("$ProbeTableName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$ProbeTagName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$TableName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$TagName", probeGuard, StringComparison.Ordinal);
        Assert.Contains("$PlcName", probeGuard, StringComparison.Ordinal);
        Assert.True(guardCall > entryPoint,
            "ProbeUnavailable must reject a target identical to the mandatory drift target before startup.");

        var probeCase = ExtractSwitchCase(text, "ProbeUnavailable", "}" );
        var mcpTool = ExtractTopLevelFunction(text, "Invoke-McpTool");
        Assert.Contains("[switch]$AllowApplicationError", mcpTool, StringComparison.Ordinal);
        Assert.Contains("-AllowApplicationError", probeCase, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DefinesOptionalProbeGuardBeforeTheEntrypointCanCallIt()
    {
        var text = ReadScript();
        var guardDefinition = text.IndexOf(
            "function Assert-OptionalProbeTargetIsDistinct {",
            StringComparison.Ordinal);
        var mainDefinition = text.IndexOf("function Invoke-Main {", StringComparison.Ordinal);
        var guardCall = text.IndexOf(
            "\n        Assert-OptionalProbeTargetIsDistinct",
            mainDefinition,
            StringComparison.Ordinal);
        var finalMainCall = text.LastIndexOf("\nInvoke-Main", StringComparison.Ordinal);

        Assert.True(guardDefinition >= 0 && guardDefinition < mainDefinition,
            "The optional-probe guard must be defined before the entrypoint function.");
        Assert.True(mainDefinition >= 0 && mainDefinition < guardCall,
            "The entrypoint must call the already-defined optional-probe guard.");
        Assert.True(finalMainCall > mainDefinition,
            "The script must invoke Main only after every function has been defined.");
    }

    [Fact]
    public void Script_ApplyDriftPreplansReconciliationAndVerifiesFinalSnapshot()
    {
        var text = ReadScript();
        var applyCase = ExtractSwitchCase(text, "ApplyDrift", "ProbeUnavailable");
        var firstMutatingApply = applyCase.IndexOf(
            "Invoke-Apply -Operation $originalOperation -SafetyToken $intermediateToken",
            StringComparison.Ordinal);
        var reconciliationPlan = applyCase.IndexOf(
            "New-UpdateTagOperation -Snapshot $snapshot -FlagName $DriftFlagName -Value $currentValue -OperationId 'update-tag-restore-original-flag'",
            StringComparison.Ordinal);

        Assert.True(reconciliationPlan >= 0 && reconciliationPlan < firstMutatingApply,
            "ApplyDrift must prepare reconciliation before its intermediate mutation can be issued.");
        Assert.DoesNotContain("if ($intermediateApplied)", applyCase, StringComparison.Ordinal);
        Assert.Contains("Assert-SnapshotFlagEquals", applyCase, StringComparison.Ordinal);
        Assert.Contains("-ExpectedValue $currentValue", applyCase, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ReadComparesThePublicTagRow()
    {
        var text = ReadScript();
        var publicComparison = ExtractTopLevelFunction(text, "Assert-PublicTagRowMatchesSnapshot");
        var readCase = ExtractSwitchCase(text, "Read", "PreviewDrift");

        Assert.Contains("$Snapshot", publicComparison, StringComparison.Ordinal);
        Assert.Contains("Invoke-McpTool -Name 'execute_read_batch'", readCase, StringComparison.Ordinal);
        Assert.Contains("operation = 'list_tag_tables'", readCase, StringComparison.Ordinal);
        Assert.Contains("Assert-PublicTagRowMatchesSnapshot", readCase, StringComparison.Ordinal);
        Assert.True(
            readCase.IndexOf("Assert-PublicTagRowMatchesSnapshot", StringComparison.Ordinal)
            > readCase.IndexOf("Invoke-McpTool -Name 'execute_read_batch'", StringComparison.Ordinal),
            "Read must compare the public list_tag_tables row after its call succeeds.");
    }

    [Fact]
    public async Task Script_InvokeMcpToolAcceptsOmittedIsErrorAndHonorsExplicitApplicationErrors()
    {
        var text = ReadScript();
        Assert.DoesNotMatch(new Regex(@"\.\s*isError\b", RegexOptions.IgnoreCase), text);
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var invokeMcpTool = ExtractTopLevelFunction(text, "Invoke-McpTool");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            function Invoke-McpRequest {
                param([string]$Method, [hashtable]$Params)
                return $script:NextResult
            }
            {{resultErrorReader}}
            {{invokeMcpTool}}
            $script:NextResult = [pscustomobject]@{
                content = @([pscustomobject]@{ text = '{"success":true}' })
            }
            $success = Invoke-McpTool -Name 'fixture' -Arguments @{}
            if ($null -eq $success.Document -or -not $success.Document.success) {
                throw 'A legal result without isError was not decoded.'
            }
            $script:NextResult = [pscustomobject]@{
                isError = $true
                content = @([pscustomobject]@{ text = '{"success":false}' })
            }
            $rejected = $false
            try {
                $null = Invoke-McpTool -Name 'fixture' -Arguments @{}
            }
            catch {
                if ($_.Exception.Message -notlike '*application error*') { throw }
                $rejected = $true
            }
            if (-not $rejected) { throw 'An explicit isError:true result was not rejected.' }
            $observed = Invoke-McpTool -Name 'fixture' -Arguments @{} -AllowApplicationError
            if ($null -eq $observed.Document -or $observed.Document.success) {
                throw 'AllowApplicationError did not preserve the application-error document.'
            }
            Write-Output 'fixture-ok'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("fixture-ok", result.StandardOutput, StringComparison.Ordinal);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected live harness at {ScriptPath}.");
        return File.ReadAllText(ScriptPath).ReplaceLineEndings("\n");
    }

    private static string ExtractTopLevelFunction(string text, string name)
    {
        var start = text.IndexOf($"function {name} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected function '{name}'.");
        var next = text.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        return next >= 0 ? text[start..next] : text[start..];
    }

    private static string ExtractSwitchCase(string text, string name, string nextName)
    {
        var start = text.IndexOf($"'{name}' {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected '{name}' switch case.");
        if (nextName == "}")
        {
            var finallyStart = text.IndexOf("\n        }\n    }\n    finally", start, StringComparison.Ordinal);
            Assert.True(finallyStart > start, $"Expected switch closing boundary after '{name}'.");
            return text[start..finallyStart];
        }
        var end = text.IndexOf($"'{nextName}' {{", start + 1, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected '{nextName}' after '{name}'.");
        return text[start..end];
    }

    private static async Task<PowerShellResult> RunPowerShellFixtureAsync(string fixture)
    {
        var fixturePath = Path.Combine(Path.GetTempPath(), $"tag-update-harness-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(fixturePath, fixture + Environment.NewLine);
        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(fixturePath);

        try
        {
            using var process = new Process { StartInfo = startInfo };
            Assert.True(process.Start(), "Expected the PowerShell fixture process to start.");
            var standardOutput = await process.StandardOutput.ReadToEndAsync();
            var standardError = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            return new PowerShellResult(process.ExitCode, standardOutput, standardError);
        }
        finally
        {
            File.Delete(fixturePath);
        }
    }

    private sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError);

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
