using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace TiaMcpServer.Tests.Tools;

public sealed class RegisteredWriteToolsLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-write-safety-pr2-registered-tools.ps1"));

    private static readonly string[] ExpectedRegisteredToolNames =
    {
        "execute_read_batch",
        "apply_write_batch",
        "archive_project",
        "close_project",
        "create_project",
        "open_project",
        "preview_write_batch",
        "save_project",
        "save_project_as",
    };

    private static readonly string[] ExpectedPreviewCallNames =
    {
        "execute_read_batch",
        "preview_write_batch",
        "save_project",
    };

    [Fact]
    public void Script_RequiresPowerShell7_AndLaunchesOnlyTheRealHostWithStartupBinding()
        => AssertHostAndProtocolContract(ReadScript());

    [Fact]
    public void Script_PinsTheExactNineNameRegisteredToolCensus()
        => AssertRegisteredToolCensus(ReadScript());

    [Fact]
    public void Script_HasExactlyThreeLiteralPreviewCalls_AndOneGenericToolsCallRoute()
        => AssertOnlyApprovedCallConstruction(ReadScript());

    [Fact]
    public void Script_HasNoApplyConfirmTokenInputOrInteractivePath()
        => AssertNoApplyOrTokenInputPath(ReadScript());

    [Fact]
    public void Script_ReusesTheReadContentVerbatimInTheOnlyWritePreview()
        => AssertSourceContentReuse(ReadScript());

    [Fact]
    public void Script_RedactsTokensAndPrintsHashesInstructionsAndFinalBoundary()
        => AssertRedactedEvidence(ReadScript());

    [Fact]
    public void Script_AlwaysStopsAndDisposesTheHostInFinally()
        => AssertCleanupContract(ReadScript());

    [Fact]
    public void CompleteContract_RejectsRepresentativeBoundaryBypasses()
    {
        var text = ReadScript();
        var mutations = new[]
        {
            (
                "direct tools/call",
                InsertBeforeFinalBoundary(
                    text,
                    "$unexpected = Invoke-McpRequest -Method 'tools/call' -Params @{ name = 'save_project'; arguments = @{} }")),
            (
                "variable helper tool name",
                InsertBeforeFinalBoundary(
                    text,
                    "$unexpectedName = 'save_project'\n    $unexpected = Invoke-PreviewToolCall -ToolName $unexpectedName -Arguments @{}")),
            (
                "differently formatted helper call",
                ReplaceOnce(
                    text,
                    "$readResult = Invoke-PreviewToolCall -ToolName 'execute_read_batch' -Arguments @{",
                    "$readResult = Invoke-PreviewToolCall `\n        -ToolName 'execute_read_batch' `\n        -Arguments @{")),
            (
                "confirm after nested operation hashtable",
                ReplaceOnce(
                    text,
                    "        )\n    }\n    $batchEvidence",
                    "        )\n        confirm = $true\n    }\n    $batchEvidence")),
            (
                "token input after nested operation hashtable",
                ReplaceOnce(
                    text,
                    "        )\n    }\n    $batchEvidence",
                    "        )\n        safetyToken = 'raw-token'\n    }\n    $batchEvidence")),
            ("tool census drift", ReplaceOnce(text, "    'archive_project'", "    'network_write'")),
            ("source content replacement", ReplaceOnce(text, "                sourceContent = $sourceContent", "                sourceContent = 'replacement'")),
            ("worker launch", ReplaceOnce(text, "'TiaMcpServer'", "'TiaMcpServer.OpennessWorker'")),
            (
                "raw token output",
                ReplaceOnce(
                    text,
                    "Write-Output \"generic batch safetyToken: $($batchEvidence.tokenStatus)\"",
                    "Write-Output \"generic batch safetyToken: $token\"")),
            (
                "missing hash evidence",
                RemoveLine(text, "    Write-Output \"generic batch requestedInputHash: $($batchEvidence.requestedInputHash)\"")),
            (
                "missing final no-apply evidence",
                ReplaceOnce(
                    text,
                    "    Write-Output 'No apply call was issued; this harness performed preview and read calls only.'",
                    "    Write-Output 'Preview complete.'")),
            (
                "missing finally cleanup",
                ReplaceOnce(text, "finally {\n    Stop-McpHost\n}", "finally {\n}")),
        };

        foreach (var (name, mutation) in mutations)
        {
            var failure = Record.Exception(() => AssertCompleteContract(mutation));
            Assert.True(
                failure is XunitException,
                $"Mutation '{name}' was not rejected by the complete static contract.");
        }
    }

    private static void AssertCompleteContract(string text)
    {
        AssertHostAndProtocolContract(text);
        AssertRegisteredToolCensus(text);
        AssertOnlyApprovedCallConstruction(text);
        AssertNoApplyOrTokenInputPath(text);
        AssertSourceContentReuse(text);
        AssertRedactedEvidence(text);
        AssertCleanupContract(text);
    }

    private static void AssertHostAndProtocolContract(string text)
    {
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$TypePath"), text);
        Assert.Contains(
            "$HostArguments = @('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)",
            text,
            StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.ProcessStartInfo]::new()", text, StringComparison.Ordinal);
        Assert.Contains("$startInfo.FileName = $HostExecutable", text, StringComparison.Ordinal);
        Assert.Contains("foreach ($argument in $HostArguments)", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("'tools/list'", text, StringComparison.Ordinal);
        Assert.Contains("'tools/call'", text, StringComparison.Ordinal);
        Assert.Contains("jsonrpc = '2.0'", text, StringComparison.Ordinal);
    }

    private static void AssertRegisteredToolCensus(string text)
    {
        Assert.Equal(ExpectedRegisteredToolNames, ExtractExpectedToolNames(text));
        Assert.Equal(1, CountOccurrences(text, "apply_write_batch"));
    }

    private static void AssertOnlyApprovedCallConstruction(string text)
    {
        var previewCallLines = FindInvocationLines(text, "Invoke-PreviewToolCall");
        Assert.Equal(3, previewCallLines.Length);
        Assert.Equal(
            ExpectedPreviewCallNames,
            previewCallLines.Select(line => ExtractLiteralArgument(line, "Invoke-PreviewToolCall", "ToolName")).ToArray());
        Assert.All(
            previewCallLines,
            line => Assert.Matches(
                new Regex(@"^\s*\$\w+\s*=\s*Invoke-PreviewToolCall\s+-ToolName\s+'[^']+'\s+-Arguments\s+@\{\s*$"),
                line));
        Assert.Equal(
            new[]
            {
                "    $readResult = Invoke-PreviewToolCall -ToolName 'execute_read_batch' -Arguments @{",
                "        operations = @(",
                "            @{",
                "                operationId = 'read-type-content'",
                "                operation   = 'get_type_content'",
                "                typePath    = $TypePath",
                "                projectPath = $resolvedProjectPath",
                "            }",
                "        )",
                "    }",
            },
            ExtractSectionLines(
                text,
                "    $readResult = Invoke-PreviewToolCall -ToolName 'execute_read_batch' -Arguments @{",
                "    $sourceContent = Get-ReadSourceContent -ToolResult $readResult"));
        Assert.Equal(
            new[]
            {
                "    $batchPreviewResult = Invoke-PreviewToolCall -ToolName 'preview_write_batch' -Arguments @{",
                "        operations = @(",
                "            @{",
                "                operationId  = 'preview-update-type-content'",
                "                operation    = 'update_type_content'",
                "                typePath     = $TypePath",
                "                sourceContent = $sourceContent",
                "                projectPath  = $resolvedProjectPath",
                "            }",
                "        )",
                "    }",
            },
            ExtractSectionLines(
                text,
                "    $batchPreviewResult = Invoke-PreviewToolCall -ToolName 'preview_write_batch' -Arguments @{",
                "    $batchEvidence = Get-PreviewEvidence -ToolResult $batchPreviewResult -ToolName 'preview_write_batch'"));
        Assert.Equal(
            new[]
            {
                "    $lifecyclePreviewResult = Invoke-PreviewToolCall -ToolName 'save_project' -Arguments @{",
                "        projectPath = $resolvedProjectPath",
                "    }",
            },
            ExtractSectionLines(
                text,
                "    $lifecyclePreviewResult = Invoke-PreviewToolCall -ToolName 'save_project' -Arguments @{",
                "    $lifecycleEvidence = Get-PreviewEvidence -ToolResult $lifecyclePreviewResult -ToolName 'save_project'"));

        var requestLines = FindInvocationLines(text, "Invoke-McpRequest");
        Assert.Equal(3, requestLines.Length);
        Assert.Equal(
            new[] { "tools/call", "initialize", "tools/list" },
            requestLines.Select(line => ExtractLiteralArgument(line, "Invoke-McpRequest", "Method")).ToArray());

        var toolCallHelper = ExtractTopLevelFunction(text, "Invoke-PreviewToolCall");
        Assert.Contains(
            "$result = Invoke-McpRequest -Method 'tools/call' -Params @{\n        name      = $ToolName\n        arguments = $Arguments\n    }",
            toolCallHelper,
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(text, "'tools/call'"));
        Assert.DoesNotContain(
            "'tools/call'",
            text.Replace(toolCallHelper, string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);

        Assert.Equal(2, FindInvocationLines(text, "Send-McpMessage").Length);
        var sendHelper = ExtractTopLevelFunction(text, "Send-McpMessage");
        Assert.Equal(1, CountOccurrences(text, "StandardInput.WriteLine"));
        Assert.Contains("StandardInput.WriteLine", sendHelper, StringComparison.Ordinal);
    }

    private static void AssertNoApplyOrTokenInputPath(string text)
    {
        Assert.Equal(1, CountOccurrences(text, "apply_write_batch"));
        Assert.DoesNotMatch(new Regex(@"(?im)['\""]?\bconfirm\b['\""]?\s*="), text);
        Assert.DoesNotMatch(new Regex(@"(?im)^\s*['\""]?safetyToken['\""]?\s*="), text);
        Assert.DoesNotMatch(new Regex(@"(?i)\.safetyToken\s*="), text);
        Assert.DoesNotMatch(new Regex(@"(?im)\[(?:switch|bool)\]\s+\$(?:Apply|Confirm)\b"), text);
        Assert.DoesNotContain("Read-Host", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertSourceContentReuse(string text)
    {
        Assert.Contains("operation   = 'get_type_content'", text, StringComparison.Ordinal);
        Assert.Contains("$sourceContent = Get-ReadSourceContent -ToolResult $readResult", text, StringComparison.Ordinal);
        Assert.Contains("operation    = 'update_type_content'", text, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(text, "sourceContent = $sourceContent"));
        Assert.Matches(
            new Regex(
                @"(?s)\$sourceContent\s*=\s*Get-ReadSourceContent\s+-ToolResult\s+\$readResult.*?Invoke-PreviewToolCall\s+-ToolName\s+'preview_write_batch'.*?sourceContent\s*=\s*\$sourceContent"),
            text);
    }

    private static void AssertRedactedEvidence(string text)
    {
        Assert.Equal(1, CountOccurrences(text, "token present (redacted)"));
        Assert.Equal(3, CountOccurrences(text, "safetyToken"));
        Assert.Contains("tokenStatus       = 'token present (redacted)'", text, StringComparison.Ordinal);
        Assert.Contains(
            "Write-Output \"generic batch safetyToken: $($batchEvidence.tokenStatus)\"",
            text,
            StringComparison.Ordinal);
        Assert.Contains(
            "Write-Output \"lifecycle safetyToken: $($lifecycleEvidence.tokenStatus)\"",
            text,
            StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"(?im)^\s*Write-(?:Output|Host).*\$token\b"), text);
        Assert.DoesNotMatch(new Regex(@"(?im)^\s*Write-(?:Output|Host).*\.safetyToken\b"), text);

        foreach (var expectedLine in new[]
                 {
                     "Write-Output \"generic batch requestedInputHash: $($batchEvidence.requestedInputHash)\"",
                     "Write-Output \"generic batch currentStateHash: $($batchEvidence.currentStateHash)\"",
                     "Write-Output \"generic batch instructions: $($batchEvidence.instructions)\"",
                     "Write-Output \"lifecycle requestedInputHash: $($lifecycleEvidence.requestedInputHash)\"",
                     "Write-Output \"lifecycle currentStateHash: $($lifecycleEvidence.currentStateHash)\"",
                     "Write-Output \"lifecycle instructions: $($lifecycleEvidence.instructions)\"",
                 })
        {
            Assert.Contains(expectedLine, text, StringComparison.Ordinal);
        }

        Assert.Equal(
            1,
            CountOccurrences(text, "Write-Output 'No apply call was issued; this harness performed preview and read calls only.'"));
    }

    private static void AssertCleanupContract(string text)
    {
        Assert.Matches(
            new Regex(@"(?s)\ntry\s*\{\s*Start-McpHost\b.*?\}\s*finally\s*\{\s*Stop-McpHost\s*\}\s*$"),
            text);

        var stopHelper = ExtractTopLevelFunction(text, "Stop-McpHost");
        Assert.Contains("StandardInput.Close()", stopHelper, StringComparison.Ordinal);
        Assert.Contains("WaitForExit(5000)", stopHelper, StringComparison.Ordinal);
        Assert.Contains("Kill($true)", stopHelper, StringComparison.Ordinal);
        Assert.Contains("$script:HostProcess.Dispose()", stopHelper, StringComparison.Ordinal);
        Assert.Contains("$script:HostProcess = $null", stopHelper, StringComparison.Ordinal);
    }

    private static string[] ExtractExpectedToolNames(string text)
    {
        const string marker = "$script:ExpectedRegisteredToolNames = @(";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Expected the registered-tool census declaration.");
        var end = text.IndexOf("\n)", start + marker.Length, StringComparison.Ordinal);
        Assert.True(end > start, "Expected the registered-tool census closing delimiter.");

        return Regex.Matches(text[start..end], @"(?m)^\s*'(?<name>[^']+)'\s*$")
            .Select(match => match.Groups["name"].Value)
            .ToArray();
    }

    private static string[] FindInvocationLines(string text, string command)
        => Regex.Matches(
                text,
                $@"(?m)^(?!\s*function\s+)[^\r\n]*\b{Regex.Escape(command)}\b[^\r\n]*$")
            .Select(match => match.Value)
            .ToArray();

    private static string ExtractLiteralArgument(string line, string command, string parameter)
    {
        var match = Regex.Match(
            line,
            $@"\b{Regex.Escape(command)}\s+-{Regex.Escape(parameter)}\s+'(?<value>[^']+)'(?=\s|$)");
        Assert.True(match.Success, $"Expected {command} -{parameter} to use one literal single-quoted value: {line}");
        return match.Groups["value"].Value;
    }

    private static string ExtractTopLevelFunction(string text, string name)
    {
        var start = text.IndexOf($"function {name} {{", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected function '{name}'.");
        var next = text.IndexOf("\nfunction ", start + 1, StringComparison.Ordinal);
        return next >= 0 ? text[start..next] : text[start..];
    }

    private static string[] ExtractSectionLines(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected section start '{startMarker}'.");
        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Expected section end '{endMarker}'.");
        return text[start..end].TrimEnd('\n').Split('\n');
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string InsertBeforeFinalBoundary(string text, string insertion)
        => ReplaceOnce(
            text,
            "    Write-Output 'No apply call was issued; this harness performed preview and read calls only.'",
            $"    {insertion}\n    Write-Output 'No apply call was issued; this harness performed preview and read calls only.'");

    private static string RemoveLine(string text, string line)
        => ReplaceOnce(text, line + "\n", string.Empty);

    private static string ReplaceOnce(string text, string oldValue, string newValue)
    {
        Assert.Equal(1, CountOccurrences(text, oldValue));
        return text.Replace(oldValue, newValue, StringComparison.Ordinal);
    }

    private static string ReadScript()
        => File.ReadAllText(ScriptPath).ReplaceLineEndings("\n");

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
