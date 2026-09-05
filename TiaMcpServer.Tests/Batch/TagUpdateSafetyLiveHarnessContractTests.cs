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
    public async Task Script_PublicTagComparison_AcceptsDirectArrayResultAndRejectsSnapshotValueMismatches()
    {
        // Catches treating list_tag_tables' JSON array string as a { tables: ... } wrapper,
        // or accepting a selected tag whose data type or logical address differs from the snapshot.
        var text = ReadScript();
        var errorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var comparison = ExtractTopLevelFunction(text, "Assert-PublicTagRowMatchesSnapshot");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            {{errorReader}}
            {{comparison}}
            $responseText = @'
            {
              "success": true,
              "operations": [{
                "operationId": "public-list",
                "operation": "list_tag_tables",
                "status": "succeeded",
                "result": "[{\"name\":\"Outputs\",\"folderPath\":\"\",\"isDefault\":false,\"tags\":[],\"userConstants\":[]},{\"name\":\"Inputs\",\"folderPath\":\"Other\",\"isDefault\":false,\"tags\":[],\"userConstants\":[]},{\"name\":\"Inputs\",\"folderPath\":\"\",\"isDefault\":false,\"tags\":[{\"name\":\"OtherTag\",\"dataType\":\"Int\",\"logicalAddress\":\"%IW2\"},{\"name\":\"DI_Reserve_1_7\",\"dataType\":\"Bool\",\"logicalAddress\":\"%I1.7\"}],\"userConstants\":[]}]"
              }]
            }
            '@
            $toolCall = [pscustomobject]@{
                Result = [pscustomobject]@{ content = @([pscustomobject]@{ type = 'text'; text = $responseText }) }
                Text = $responseText
                Document = $responseText | ConvertFrom-Json -Depth 100
            }
            $snapshot = [pscustomobject]@{
                tableName = 'Inputs'; folderPath = ''; tagName = 'DI_Reserve_1_7'
                dataType = 'Bool'; logicalAddress = '%I1.7'
            }
            Assert-PublicTagRowMatchesSnapshot -ToolCall $toolCall -Snapshot $snapshot
            foreach ($mismatch in @(
                [pscustomobject]@{ tableName = 'Inputs'; folderPath = ''; tagName = 'DI_Reserve_1_7'; dataType = 'Int'; logicalAddress = '%I1.7' },
                [pscustomobject]@{ tableName = 'Inputs'; folderPath = ''; tagName = 'DI_Reserve_1_7'; dataType = 'Bool'; logicalAddress = '%I1.6' }
            )) {
                $rejected = $false
                try {
                    Assert-PublicTagRowMatchesSnapshot -ToolCall $toolCall -Snapshot $mismatch
                }
                catch {
                    if ($_.Exception.Message -notlike '*tag values differ from the strict snapshot*') { throw }
                    $rejected = $true
                }
                if (-not $rejected) { throw 'A public tag value mismatch was accepted.' }
            }
            Write-Output 'public-tag-array-compared'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("public-tag-array-compared", result.StandardOutput, StringComparison.Ordinal);
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

    [Fact]
    public async Task Script_InvokeMcpTool_ApplicationErrorDoesNotLeakReturnedSafetyToken()
    {
        // Catches composing arbitrary application-error content into the default exception path.
        var text = ReadScript();
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var invokeMcpTool = ExtractTopLevelFunction(text, "Invoke-McpTool");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $secret = 'fixture-application-error-secret-token'
            function Invoke-McpRequest {
                param([string]$Method, [hashtable]$Params)
                return [pscustomobject]@{
                    isError = $true
                    content = @([pscustomobject]@{
                        text = '{"success":false,"failureCategory":"validation_error","safetyToken":"fixture-application-error-secret-token"}'
                    })
                }
            }
            {{resultErrorReader}}
            {{invokeMcpTool}}
            $rejected = $false
            try {
                $null = Invoke-McpTool -Name 'preview_write_batch' -Arguments @{}
            }
            catch {
                $message = $_.Exception.Message
                if ($message -notlike '*application error*') { throw }
                if ($message -like "*$secret*") {
                    throw 'Invoke-McpTool leaked the returned safety token.'
                }
                $rejected = $true
            }
            if (-not $rejected) { throw 'An explicit isError:true result was not rejected.' }
            Write-Output 'application-error-token-redacted'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("application-error-token-redacted", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_InvokeMain_DefaultHostArgumentsBindTheExactProjectPath()
    {
        // Catches starting the host without --project or binding a path other than the harness target.
        var main = ExtractTopLevelFunction(ReadScript(), "Invoke-Main");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $Mode = 'Fixture'
            $AllowApply = $false
            $ProjectPath = 'C:\fixture projects\bound target.ap21'
            $TableName = 'Inputs'
            $TagName = 'TargetTag'
            $PlcName = 'PLC_1'
            $HostArguments = $null
            $WorkerExecutable = 'fixture-worker'
            $script:RepositoryRoot = 'C:\fixture repo'
            $script:HostProcess = $null
            $script:WorkerProcess = $null
            function Start-JsonLineProcess { return [pscustomobject]@{ Label = 'worker' } }
            function Connect-Worker {}
            function Get-CompleteSessionIdentity {}
            function Start-McpHost {
                $expectedHost = Join-Path $script:RepositoryRoot 'TiaMcpServer\bin\Debug\net8.0\TiaMcpServer.dll'
                if ($HostArguments.Count -ne 3 -or
                    $HostArguments[0] -ne $expectedHost -or
                    $HostArguments[1] -ne '--project' -or
                    $HostArguments[2] -ne $ProjectPath) {
                    throw "Default host arguments did not bind the exact project: $($HostArguments | ConvertTo-Json -Compress)"
                }
                Write-Output 'default-host-project-bound'
            }
            function Stop-JsonLineProcess {}
            {{main}}
            Invoke-Main
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("default-host-project-bound", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_ProbeUnavailable_AcceptsOnlyDocumentLevelValidationRejectionWithoutToken()
    {
        // Catches requiring MCP isError for the registered validation result, accepting the wrong
        // document shape/category, or dereferencing absent properties under StrictMode.
        var text = ReadScript();
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var main = ExtractTopLevelFunction(text, "Invoke-Main");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $Mode = 'ProbeUnavailable'
            $AllowApply = $false
            $ProjectPath = 'C:\fixture\bound.ap21'
            $TableName = 'Inputs'
            $TagName = 'TargetTag'
            $PlcName = 'PLC_1'
            $ProbeTableName = 'ProbeInputs'
            $ProbeTagName = 'UnavailableTag'
            $ProbeFlagName = 'ExternalVisible'
            $HostArguments = @('fixture-host', '--project', $ProjectPath)
            $WorkerExecutable = 'fixture-worker'
            $script:RepositoryRoot = 'C:\fixture'
            $script:HostProcess = $null
            $script:WorkerProcess = $null
            function Assert-OptionalProbeTargetIsDistinct {}
            function Start-JsonLineProcess { return [pscustomobject]@{ Label = 'worker' } }
            function Connect-Worker {}
            function Get-CompleteSessionIdentity {}
            function Start-McpHost {}
            function Stop-JsonLineProcess {}
            function Read-UpdateTagSafetySnapshot {
                return [pscustomobject]@{
                    plcName = 'PLC_1'; folderPath = '/'; tableName = 'ProbeInputs'; tagName = 'UnavailableTag'
                    dataType = 'Bool'; logicalAddress = '%I0.0'; ExternalVisible = $null
                }
            }
            function New-UpdateTagOperation { return [ordered]@{ operation = 'update_tag' } }
            function Invoke-McpTool { return $script:NextToolCall }
            {{resultErrorReader}}
            {{main}}
            $cases = @(
                [pscustomobject]@{
                    Name = 'expected validation rejection'; Accept = $true
                    Call = [pscustomobject]@{
                        Result = [pscustomobject]@{}
                        Text = '{"success":false,"failureCategory":"validation_error","error":"requested flag is unavailable"}'
                        Document = [pscustomobject]@{ success = $false; failureCategory = 'validation_error'; error = 'requested flag is unavailable' }
                    }
                },
                [pscustomobject]@{
                    Name = 'successful preview'; Accept = $false
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = '{"success":true}'; Document = [pscustomobject]@{ success = $true } }
                },
                [pscustomobject]@{
                    Name = 'wrong failure category'; Accept = $false
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = '{"success":false,"failureCategory":"protocol_error"}'; Document = [pscustomobject]@{ success = $false; failureCategory = 'protocol_error' } }
                },
                [pscustomobject]@{
                    Name = 'missing document'; Accept = $false
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = ''; Document = $null }
                },
                [pscustomobject]@{
                    Name = 'missing success property'; Accept = $false
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = '{"failureCategory":"validation_error"}'; Document = [pscustomobject]@{ failureCategory = 'validation_error' } }
                },
                [pscustomobject]@{
                    Name = 'rejection carrying a token'; Accept = $false
                    Call = [pscustomobject]@{
                        Result = [pscustomobject]@{}
                        Text = '{"success":false,"failureCategory":"validation_error","safetyToken":"unsafe"}'
                        Document = [pscustomobject]@{ success = $false; failureCategory = 'validation_error'; safetyToken = 'unsafe' }
                    }
                }
            )
            foreach ($case in $cases) {
                $script:NextToolCall = $case.Call
                $accepted = $true
                try { Invoke-Main } catch { $accepted = $false }
                if ($accepted -ne $case.Accept) {
                    throw "ProbeUnavailable case '$($case.Name)' acceptance was '$accepted', expected '$($case.Accept)'."
                }
            }
            Write-Output 'probe-result-matrix-enforced'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("probe-result-matrix-enforced", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_ProbeUnavailable_UnexpectedDocumentDoesNotLeakReturnedSafetyToken()
    {
        // Catches composing the raw preview document into optional-probe shape diagnostics.
        var text = ReadScript();
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var main = ExtractTopLevelFunction(text, "Invoke-Main");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $secret = 'fixture-probe-secret-token'
            $Mode = 'ProbeUnavailable'
            $AllowApply = $false
            $ProjectPath = 'C:\fixture\bound.ap21'
            $TableName = 'Inputs'
            $TagName = 'TargetTag'
            $PlcName = 'PLC_1'
            $ProbeTableName = 'ProbeInputs'
            $ProbeTagName = 'UnavailableTag'
            $ProbeFlagName = 'ExternalVisible'
            $HostArguments = @('fixture-host', '--project', $ProjectPath)
            $WorkerExecutable = 'fixture-worker'
            $script:RepositoryRoot = 'C:\fixture'
            $script:HostProcess = $null
            $script:WorkerProcess = $null
            function Assert-OptionalProbeTargetIsDistinct {}
            function Start-JsonLineProcess { return [pscustomobject]@{ Label = 'worker' } }
            function Connect-Worker {}
            function Get-CompleteSessionIdentity {}
            function Start-McpHost {}
            function Stop-JsonLineProcess {}
            function Read-UpdateTagSafetySnapshot {
                return [pscustomobject]@{
                    plcName = 'PLC_1'; folderPath = '/'; tableName = 'ProbeInputs'; tagName = 'UnavailableTag'
                    dataType = 'Bool'; logicalAddress = '%I0.0'; ExternalVisible = $null
                }
            }
            function New-UpdateTagOperation { return [ordered]@{ operation = 'update_tag' } }
            function Invoke-McpTool {
                return [pscustomobject]@{
                    Result = [pscustomobject]@{}
                    Text = '{"failureCategory":"validation_error","safetyToken":"fixture-probe-secret-token"}'
                    Document = [pscustomobject]@{ failureCategory = 'validation_error'; safetyToken = $secret }
                }
            }
            {{resultErrorReader}}
            {{main}}
            $rejected = $false
            try { Invoke-Main }
            catch {
                $message = $_.Exception.Message
                if ($message -notlike '*expected validation document*' -and
                    $message -notlike '*success:false*') { throw }
                if ($message -like "*$secret*") {
                    throw 'ProbeUnavailable leaked the returned safety token.'
                }
                $rejected = $true
            }
            if (-not $rejected) { throw 'The malformed optional-probe document was accepted.' }
            Write-Output 'probe-error-token-redacted'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("probe-error-token-redacted", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_GetPreviewToken_PreservesTokenlessFailureDiagnosticUnderStrictMode()
    {
        // Catches reading Document.safetyToken before proving success and property presence.
        var text = ReadScript();
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var tokenReader = ExtractTopLevelFunction(text, "Get-PreviewToken");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            {{resultErrorReader}}
            {{tokenReader}}
            $diagnostic = '{"success":false,"failureCategory":"validation_error","error":"controlled tokenless preview"}'
            $failedCall = [pscustomobject]@{
                Result = [pscustomobject]@{}
                Text = $diagnostic
                Document = $diagnostic | ConvertFrom-Json -Depth 10
            }
            $rejected = $false
            try { $null = Get-PreviewToken -ToolCall $failedCall }
            catch {
                if ($_.Exception.Message -notlike '*validation_error*' -or $_.Exception.Message -notlike '*controlled tokenless preview*') { throw }
                if ($_.Exception.Message -like "*property 'safetyToken' cannot be found*") { throw }
                $rejected = $true
            }
            if (-not $rejected) { throw 'The tokenless failed preview was accepted.' }
            $successfulCall = [pscustomobject]@{
                Result = [pscustomobject]@{}
                Text = '{"success":true,"safetyToken":"fixture-token"}'
                Document = [pscustomobject]@{ success = $true; safetyToken = 'fixture-token' }
            }
            if ((Get-PreviewToken -ToolCall $successfulCall) -ne 'fixture-token') {
                throw 'The successful preview token was not returned.'
            }
            Write-Output 'tokenless-preview-diagnostic-preserved'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("tokenless-preview-diagnostic-preserved", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_GetPreviewToken_AcceptsRegisteredSuccessShapeWithoutOptionalSuccessMember()
    {
        // Catches treating the optional success member as mandatory when the registered preview
        // has already proved success by returning a nonblank top-level safetyToken.
        var text = ReadScript();
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var tokenReader = ExtractTopLevelFunction(text, "Get-PreviewToken");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            {{resultErrorReader}}
            {{tokenReader}}
            $registeredPreviewText = '{"safetyToken":"registered-preview-token","expiresAt":"2026-09-05T12:00:00Z","summary":"Preview only"}'
            $registeredPreview = [pscustomobject]@{
                Result = [pscustomobject]@{}
                Text = $registeredPreviewText
                Document = $registeredPreviewText | ConvertFrom-Json -Depth 10
            }
            if ((Get-PreviewToken -ToolCall $registeredPreview) -ne 'registered-preview-token') {
                throw 'The registered successful preview token was not returned.'
            }
            Write-Output 'registered-preview-token-accepted'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("registered-preview-token-accepted", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_GetPreviewToken_FailsClosedForInvalidShapesWithoutLeakingToken()
    {
        // Catches accepting application errors, invalid optional success members, or missing/blank
        // tokens, and catches copying a returned token into an exception message.
        var text = ReadScript();
        var resultErrorReader = ExtractTopLevelFunction(text, "Test-McpToolResultIsError");
        var tokenReader = ExtractTopLevelFunction(text, "Get-PreviewToken");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            {{resultErrorReader}}
            {{tokenReader}}
            $secret = 'fixture-secret-token'
            $cases = @(
                [pscustomobject]@{
                    Name = 'MCP application error'; ExpectedDiagnostic = $null
                    Call = [pscustomobject]@{
                        Result = [pscustomobject]@{ isError = $true }
                        Text = '{"safetyToken":"fixture-secret-token"}'
                        Document = [pscustomobject]@{ safetyToken = $secret }
                    }
                },
                [pscustomobject]@{
                    Name = 'null document'; ExpectedDiagnostic = $null
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = $null; Document = $null }
                },
                [pscustomobject]@{
                    Name = 'malformed document'; ExpectedDiagnostic = $null
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = '{not-json fixture-secret-token'; Document = $null }
                },
                [pscustomobject]@{
                    Name = 'explicit failure'; ExpectedDiagnostic = 'validation_error'
                    Call = [pscustomobject]@{
                        Result = [pscustomobject]@{}
                        Text = '{"success":false,"failureCategory":"validation_error","error":"controlled failure","safetyToken":"fixture-secret-token"}'
                        Document = [pscustomobject]@{ success = $false; failureCategory = 'validation_error'; error = 'controlled failure'; safetyToken = $secret }
                    }
                },
                [pscustomobject]@{
                    Name = 'non-boolean success'; ExpectedDiagnostic = $null
                    Call = [pscustomobject]@{
                        Result = [pscustomobject]@{}
                        Text = '{"success":"true","safetyToken":"fixture-secret-token"}'
                        Document = [pscustomobject]@{ success = 'true'; safetyToken = $secret }
                    }
                },
                [pscustomobject]@{
                    Name = 'missing token'; ExpectedDiagnostic = $null
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = '{"summary":"preview"}'; Document = [pscustomobject]@{ summary = 'preview' } }
                },
                [pscustomobject]@{
                    Name = 'blank token'; ExpectedDiagnostic = $null
                    Call = [pscustomobject]@{ Result = [pscustomobject]@{}; Text = '{"safetyToken":"   "}'; Document = [pscustomobject]@{ safetyToken = '   ' } }
                }
            )
            foreach ($case in $cases) {
                $rejected = $false
                try { $null = Get-PreviewToken -ToolCall $case.Call }
                catch {
                    $message = $_.Exception.Message
                    if ($message -notlike '*preview_write_batch*') { throw }
                    if ($message -like "*$secret*") { throw "Case '$($case.Name)' leaked a safety token." }
                    if ($null -ne $case.ExpectedDiagnostic -and $message -notlike "*$($case.ExpectedDiagnostic)*") {
                        throw "Case '$($case.Name)' lost its useful diagnostic."
                    }
                    $rejected = $true
                }
                if (-not $rejected) { throw "Case '$($case.Name)' was accepted." }
            }
            Write-Output 'invalid-preview-token-shapes-rejected'
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("invalid-preview-token-shapes-rejected", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_StartJsonLineProcess_DoesNotRejectTheWorkerStyleEmptyArgumentArrayAtParameterBinding()
    {
        var launcher = ExtractTopLevelFunction(ReadScript(), "Start-JsonLineProcess");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            {{launcher}}
            $child = $null
            try {
                $childExecutable = Join-Path $PSHOME 'pwsh.exe'
                if (-not (Test-Path -LiteralPath $childExecutable -PathType Leaf)) {
                    throw "Expected a harmless local child executable at $childExecutable."
                }
                $child = Start-JsonLineProcess -Executable $childExecutable -Arguments @() -Label 'empty-arguments regression fixture'
                if ($null -eq $child -or $child.HasExited) {
                    throw 'The local child did not start with an empty argument array.'
                }
                Write-Output 'empty-arguments-started'
            }
            finally {
                if ($null -ne $child) {
                    try {
                        if (-not $child.HasExited) {
                            $child.Kill($true)
                            $child.WaitForExit()
                        }
                    }
                    finally {
                        $child.Dispose()
                    }
                }
            }
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("empty-arguments-started", result.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Script_InvokeWorkerRequest_IncludesProtocolVersionOnNonHelloRequests()
    {
        var text = ReadScript();
        var launcher = ExtractTopLevelFunction(text, "Start-JsonLineProcess");
        var stopper = ExtractTopLevelFunction(text, "Stop-JsonLineProcess");
        var sender = ExtractTopLevelFunction(text, "Send-JsonLine");
        var reader = ExtractTopLevelFunction(text, "Read-JsonLine");
        var successAssertion = ExtractTopLevelFunction(text, "Assert-WorkerSuccess");
        var requestInvoker = ExtractTopLevelFunction(text, "Invoke-WorkerRequest");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $TimeoutSeconds = 10
            {{launcher}}
            {{stopper}}
            {{sender}}
            {{reader}}
            {{successAssertion}}
            {{requestInvoker}}
            $workerFixture = @'
            $line = [Console]::In.ReadLine()
            if ($null -eq $line) { exit 1 }
            $request = $line | ConvertFrom-Json -Depth 10
            if ($request.method -ne 'get_project_status') {
                Write-Output (@{ success = $false; failureCategory = 'validation_error'; error = 'controlled child received an unexpected method' } | ConvertTo-Json -Compress)
                exit 0
            }
            if ($request.PSObject.Properties['protocolVersion'] -eq $null -or $request.protocolVersion -ne 'project-binding-v1') {
                Write-Output (@{ success = $false; failureCategory = 'validation_error'; error = 'controlled child: request did not carry protocolVersion project-binding-v1' } | ConvertTo-Json -Compress)
                exit 0
            }
            Write-Output (@{ success = $true; protocolVersion = $request.protocolVersion } | ConvertTo-Json -Compress)
            '@
            $workerPath = Join-Path ([System.IO.Path]::GetTempPath()) ("tag-update-worker-{{Guid.NewGuid():N}}.ps1")
            $script:WorkerProcess = $null
            try {
                [System.IO.File]::WriteAllText($workerPath, $workerFixture, [System.Text.UTF8Encoding]::new($false))
                $script:WorkerProcess = Start-JsonLineProcess -Executable (Join-Path $PSHOME 'pwsh.exe') -Arguments @('-NoProfile', '-NonInteractive', '-File', $workerPath) -Label 'controlled worker protocol fixture'
                $response = Invoke-WorkerRequest -Method 'get_project_status' -Arguments @{ projectPath = 'C:\\fixture\\project.ap21' }
                if ($response.protocolVersion -ne 'project-binding-v1') {
                    throw 'The controlled child did not receive project-binding-v1 on the non-hello request.'
                }
                Write-Output 'worker-protocol-version-forwarded'
            }
            finally {
                Stop-JsonLineProcess -Process $script:WorkerProcess
                Remove-Item -LiteralPath $workerPath -Force -ErrorAction SilentlyContinue
            }
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("worker-protocol-version-forwarded", result.StandardOutput, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Script_InvokeMcpRequest_AcceptsOmittedErrorAndRejectsExplicitJsonRpcError(bool returnError)
    {
        var text = ReadScript();
        var launcher = ExtractTopLevelFunction(text, "Start-JsonLineProcess");
        var stopper = ExtractTopLevelFunction(text, "Stop-JsonLineProcess");
        var sender = ExtractTopLevelFunction(text, "Send-JsonLine");
        var reader = ExtractTopLevelFunction(text, "Read-JsonLine");
        var requestInvoker = ExtractTopLevelFunction(text, "Invoke-McpRequest");
        var fixture = $$"""
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $TimeoutSeconds = 10
            $script:NextRequestId = 0
            {{launcher}}
            {{stopper}}
            {{sender}}
            {{reader}}
            {{requestInvoker}}
            $hostFixture = @'
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $request = [Console]::In.ReadLine() | ConvertFrom-Json -Depth 10
            if ($request.jsonrpc -ne '2.0' -or $request.method -ne 'fixture/read' -or $request.params.target -ne 'fixture-tag') {
                throw 'The controlled child received an unexpected request.'
            }
            Write-Output (@{ jsonrpc = '2.0'; id = ($request.id + 100); result = @{ value = 'uncorrelated' } } | ConvertTo-Json -Compress)
            if (${{returnError.ToString().ToLowerInvariant()}}) {
                Write-Output (@{ jsonrpc = '2.0'; id = $request.id; error = @{ code = -32602; message = 'controlled invalid params'; data = @{ target = 'fixture-tag' } } } | ConvertTo-Json -Compress -Depth 10)
            }
            else {
                Write-Output (@{ jsonrpc = '2.0'; id = $request.id; result = @{ value = 'correlated-success' } } | ConvertTo-Json -Compress)
            }
            $null = [Console]::In.ReadLine()
            '@
            $encodedHost = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($hostFixture))
            $script:HostProcess = $null
            try {
                $script:HostProcess = Start-JsonLineProcess -Executable (Join-Path $PSHOME 'pwsh.exe') -Arguments @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedHost) -Label 'controlled JSON-RPC fixture'
                $rejected = $false
                try {
                    $result = Invoke-McpRequest -Method 'fixture/read' -Params @{ target = 'fixture-tag' }
                    if (${{returnError.ToString().ToLowerInvariant()}}) {
                        throw 'An explicit JSON-RPC error was accepted.'
                    }
                    if ($result.value -ne 'correlated-success') {
                        throw 'The legal success result was not returned with response-ID correlation.'
                    }
                }
                catch {
                    if (-not ${{returnError.ToString().ToLowerInvariant()}}) { throw }
                    $prefix = "MCP request 'fixture/read' failed: "
                    if (-not $_.Exception.Message.StartsWith($prefix)) { throw }
                    $errorDocument = $_.Exception.Message.Substring($prefix.Length) | ConvertFrom-Json -Depth 10
                    if ($errorDocument.code -ne -32602 -or $errorDocument.message -ne 'controlled invalid params' -or $errorDocument.data.target -ne 'fixture-tag') {
                        throw 'JSON-RPC error serialization did not preserve the error document.'
                    }
                    $rejected = $true
                }
                if (${{returnError.ToString().ToLowerInvariant()}} -and -not $rejected) {
                    throw 'The explicit JSON-RPC error was not rejected.'
                }
                Write-Output 'json-rpc-response-handled'
            }
            finally {
                Stop-JsonLineProcess -Process $script:HostProcess
            }
            """;

        var result = await RunPowerShellFixtureAsync(fixture);

        Assert.True(result.ExitCode == 0,
            $"PowerShell fixture failed with exit code {result.ExitCode}: {result.StandardError}");
        Assert.Contains("json-rpc-response-handled", result.StandardOutput, StringComparison.Ordinal);
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
        if (next >= 0)
        {
            return text[start..next];
        }
        var entryPoint = text.IndexOf("\n\nInvoke-Main", start + 1, StringComparison.Ordinal);
        return entryPoint >= 0 ? text[start..entryPoint] : text[start..];
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
