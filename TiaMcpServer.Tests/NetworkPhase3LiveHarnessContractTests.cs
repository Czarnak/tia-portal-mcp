using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkPhase3LiveHarnessContractTests
{
    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-test-network-phase3.ps1");

    [Fact]
    public void Script_IsAsciiPowerShell7StrictAndExposesOnlyApprovedModes()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected live harness at {ScriptPath}.");
        var bytes = File.ReadAllBytes(ScriptPath);
        Assert.All(bytes, value => Assert.InRange(value, (byte)0, (byte)127));

        var source = Encoding.ASCII.GetString(bytes);
        Assert.Contains("#Requires -Version 7", source, StringComparison.Ordinal);
        Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", source, StringComparison.Ordinal);
        Assert.Matches(
            new Regex(
                @"\[ValidateSet\('Matrix', 'Repeatability', 'MeasureListValue', 'RawProbe'\)\]\s*\r?\n\s*\[string\] \$Mode = 'Matrix'",
                RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void Script_DefaultsFitLargeLiveProjectsWithoutOversizingStructuredPages()
    {
        var source = ReadScript();

        Assert.Matches(@"\[int\] \$TimeoutSeconds = 240", source);
        Assert.Matches(@"\[int\] \$PageSize = 10", source);
    }

    [Fact]
    public void Script_UsesTheReadOnlyMcpProtocolAndDirectWorkerOnlyForRawProbe()
    {
        var source = ReadScript();
        foreach (var token in new[]
        {
            "initialize",
            "notifications/initialized",
            "tools/list",
            "tools/call",
            "network_read",
            "--access-mode",
            "read-only",
            "probe_network_object_attributes",
            "TiaMcpServer.OpennessWorker",
        })
        {
            Assert.Contains(token, source, StringComparison.Ordinal);
        }

        Assert.Contains("function Invoke-RawProbe", source, StringComparison.Ordinal);
        Assert.Contains("function Invoke-NetworkRead", source, StringComparison.Ordinal);
        Assert.Contains("finally", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RecordsTheRequiredMatrixRepeatabilityAndValueMeasurements()
    {
        var source = ReadScript();
        foreach (var token in new[]
        {
            "nestedDeviceItem",
            "networkInterface",
            "ethernetNode",
            "ethernetSubnet",
            "profinetIoSystem",
            "communicationConnection",
            "matrixComplete",
            "coverageGaps",
            "canonicalBytesEqual",
            "canonicalByteCount",
            "elapsedMilliseconds",
            "selectorCount",
            "selectorsComplete",
            "omissions",
            "truncation",
            "requestCount",
            "connectionDiscoveryUsable",
        })
        {
            Assert.Contains(token, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Script_GuardsParsedObjectsBeforeMemberAccess()
    {
        var source = ReadScript();
        Assert.Contains("if ($null -eq $candidate)", source, StringComparison.Ordinal);
        Assert.Contains("if ($null -eq $envelope)", source, StringComparison.Ordinal);
        Assert.Contains("if ($null -eq $item)", source, StringComparison.Ordinal);
        Assert.Contains("if ($null -eq $payloadText)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_AcceptsOnlyAnEthernetBackedIoSystemAsTheProfinetExample()
    {
        var source = ReadScript();
        Assert.Matches(
            new Regex(
                @"'ioSystem'\s*\{(?:(?!'communicationConnection').)*evidence\.networkType -eq 'Ethernet'(?:(?!'communicationConnection').)*observed\.profinetIoSystem",
                RegexOptions.Singleline | RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void Script_RecordsAWholeResultOmissionInsteadOfTreatingItAsAWorkerFailure()
    {
        var source = ReadScript();
        Assert.Contains("if ($item.status -eq 'omitted')", source, StringComparison.Ordinal);
        Assert.Contains("Omission = $item.omission", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_WritesTimestampedEvidenceUnderTheIgnoredArtifactDirectory()
    {
        var source = ReadScript();
        Assert.Contains("artifacts", source, StringComparison.Ordinal);
        Assert.Contains("live-network-phase3", source, StringComparison.Ordinal);
        Assert.Contains("Join-Path", source, StringComparison.Ordinal);
        Assert.Contains("Get-Date -Format", source, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json -Depth", source, StringComparison.Ordinal);
        Assert.Contains("Set-Content", source, StringComparison.Ordinal);

        var gitignore = File.ReadAllText(Path.Combine(RepositoryRoot, ".gitignore"));
        Assert.Contains("artifacts/", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ContainsNoMutationOperationOrMutationMode()
    {
        var source = ReadScript();
        foreach (var forbidden in new[]
        {
            "network_write",
            "preview_write_batch",
            "apply_write_batch",
        })
        {
            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(
            new Regex(@"\b(confirm|save|compile|download)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            source);
        Assert.DoesNotMatch(
            new Regex(@"\[ValidateSet\([^\]]*\b(Preview|Apply|Write)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            source);
    }

    [Fact]
    public void InternalProbe_IsReadOnlyWorkerOnlyAndDoesNotEnterThePublicHostSurface()
    {
        var workerProgram = ReadRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs");
        var policy = ReadRepositoryFile("TiaMcpServer.Contracts", "OperationPolicyCatalog.cs");
        var request = ReadRepositoryFile("TiaMcpServer.Contracts", "WorkerRequest.cs");
        var dto = ReadRepositoryFile("TiaMcpServer.OpennessWorker", "NetworkAttributeProbeInfo.cs");

        Assert.Contains("\"probe_network_object_attributes\" => ProbeNetworkObjectAttributes(request)", workerProgram, StringComparison.Ordinal);
        Assert.Contains("GetAttributeInfos()", workerProgram, StringComparison.Ordinal);
        Assert.Contains("[\"probe_network_object_attributes\"] = OperationCapability.Observe", policy, StringComparison.Ordinal);
        Assert.Contains("probe_network_object_attributes", request, StringComparison.Ordinal);
        foreach (var member in new[]
        {
            "Name",
            "AccessMode",
            "SupportedClrTypeNames",
            "ObservedClrValueType",
            "ExceptionCategory",
        })
        {
            Assert.Contains(member, dto, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("ToString()", dto, StringComparison.Ordinal);

        foreach (var publicHostPath in new[]
        {
            new[] { "TiaMcpServer", "Network", "NetworkOperationCatalog.cs" },
            new[] { "TiaMcpServer", "Network", "NetworkReadTools.cs" },
            new[] { "TiaMcpServer", "Network", "NetworkWorkerInvoker.cs" },
            new[] { "TiaMcpServer", "Worker", "OpennessWorkerClient.cs" },
        })
        {
            Assert.DoesNotContain(
                "probe_network_object_attributes",
                ReadRepositoryFile(publicHostPath),
                StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("live-test-network-phase2.ps1", "Start-McpHost")]
    [InlineData("live-test-network-phase3.ps1", "Start-JsonLineProcess")]
    public void ProcessStarter_DoesNotCrashWhenTheChildWritesToStandardError(
        string scriptName,
        string functionName)
    {
        var result = RunProcessStarterSmokeTest(scriptName, functionName);

        Assert.True(
            result.ExitCode == 0,
            $"Process starter failed with exit code {result.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");
        Assert.Contains("stdout-probe", result.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("stderr-probe", result.StandardError, StringComparison.Ordinal);
        Assert.DoesNotContain("There is no Runspace available", result.StandardError, StringComparison.Ordinal);
    }

    [Fact]
    public void NoOrdinaryTestInvokesTheLiveHarness()
    {
        var references = Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "TiaMcpServer.Tests"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path => !string.Equals(path, GetType().Assembly.Location, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(nameof(NetworkPhase3LiveHarnessContractTests) + ".cs", StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path).Contains("live-test-network-phase3.ps1", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(references);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected live harness at {ScriptPath}.");
        return File.ReadAllText(ScriptPath);
    }

    private static ScriptResult RunProcessStarterSmokeTest(string scriptName, string functionName)
    {
        var smokeScriptPath = Path.Combine(
            Path.GetTempPath(),
            $"network-live-harness-process-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(smokeScriptPath, ProcessStarterSmokeScript, new UTF8Encoding(false));

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(smokeScriptPath);
            startInfo.ArgumentList.Add("-HarnessPath");
            startInfo.ArgumentList.Add(Path.Combine(RepositoryRoot, "scripts", scriptName));
            startInfo.ArgumentList.Add("-FunctionName");
            startInfo.ArgumentList.Add(functionName);

            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start pwsh process.");
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"PowerShell smoke test for {scriptName} did not exit.");
            }

            return new ScriptResult(
                process.ExitCode,
                standardOutput.GetAwaiter().GetResult(),
                standardError.GetAwaiter().GetResult());
        }
        finally
        {
            File.Delete(smokeScriptPath);
        }
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var path = segments.Aggregate(RepositoryRoot, Path.Combine);
        Assert.True(File.Exists(path), $"Expected repository file at {path}.");
        return File.ReadAllText(path);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private const string ProcessStarterSmokeScript = """
        param(
            [Parameter(Mandatory)] [string] $HarnessPath,
            [Parameter(Mandatory)] [string] $FunctionName
        )

        Set-StrictMode -Version Latest
        $ErrorActionPreference = 'Stop'

        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile(
            $HarnessPath,
            [ref] $tokens,
            [ref] $parseErrors)
        if ($parseErrors.Count -ne 0) {
            throw "Harness parsing failed: $($parseErrors[0].Message)"
        }

        $functionAst = $ast.Find({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] `
                    -and $node.Name -eq $FunctionName
            }, $true)
        if ($null -eq $functionAst) {
            throw "Function '$FunctionName' was not found in '$HarnessPath'."
        }
        Invoke-Expression $functionAst.Extent.Text

        $pwshPath = (Get-Command pwsh).Source
        $childCommand = "[Console]::Error.WriteLine('stderr-probe'); [Console]::Out.WriteLine('stdout-probe')"
        if ($FunctionName -eq 'Start-McpHost') {
            $script:HostExecutable = $pwshPath
            $script:HostArguments = @('-NoProfile', '-Command', $childCommand)
            $process = Start-McpHost
        }
        else {
            $process = Start-JsonLineProcess `
                -Executable $pwshPath `
                -Arguments @('-NoProfile', '-Command', $childCommand) `
                -Label 'process-smoke-test'
        }

        $standardOutput = $process.StandardOutput.ReadLine()
        $process.WaitForExit()
        $exitCode = $process.ExitCode
        $process.Dispose()
        if ($exitCode -ne 0) {
            throw "Child process exited with code $exitCode."
        }
        if ($standardOutput -ne 'stdout-probe') {
            throw "Unexpected child stdout: '$standardOutput'."
        }
        [Console]::Out.WriteLine($standardOutput)
        """;

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
