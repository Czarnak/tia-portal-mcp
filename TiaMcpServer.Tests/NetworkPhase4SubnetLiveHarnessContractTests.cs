using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Static, execution-free contract tests for
/// <c>scripts/live-test-network-phase4-subnets.ps1</c> -- the separately authorized live-TIA
/// MCP-protocol acceptance harness for the Network Operations Phase 4 subnet lifecycle contract
/// (<c>create_subnet</c>, <c>update_subnet</c>, <c>delete_subnet</c>, exposed as operations of
/// <c>network_write</c>).
///
/// <para>
/// None of these tests execute the script. It requires a live TIA Portal V21 attachment and its
/// own separate authorization gate (Task 10 of
/// <c>docs/superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md</c>), so every
/// assertion here reads the script's own source text and proves invariants a reviewer can check
/// without running it: PowerShell 7 is required, the default mode never mutates, Preview can
/// never reach a confirming apply call, Apply is double-gated behind an explicit switch plus an
/// exact acknowledgement string, an explicit <c>.ap21</c> path is required, output is a
/// timestamped JSON artifact with safety tokens redacted, process cleanup happens in
/// <c>finally</c>, the harness speaks the real MCP protocol through the public
/// <c>network_read</c>/<c>network_write</c> route rather than the internal mutation probe, and it
/// never calls project save or compile.
/// </para>
/// </summary>
public class NetworkPhase4SubnetLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-network-phase4-subnets.ps1"));

    [Fact]
    public void Script_Exists()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
    }

    [Fact]
    public void Script_RequiresPowerShell7AndStrictErrorHandling()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("Set-StrictMode -Version Latest", text, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DefaultModeIsInventoryAndReadOnly()
    {
        var text = ReadScript();
        Assert.Matches(
            new Regex(@"\[ValidateSet\(\s*'Inventory'\s*,\s*'Preview'\s*,\s*'Apply'\s*\)\]"),
            text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Mode\s*=\s*'Inventory'"), text);

        // The Inventory case must never call network_write.
        var inventoryCaseStart = text.IndexOf("'Inventory' { Invoke-Inventory }", StringComparison.Ordinal);
        Assert.True(inventoryCaseStart >= 0, "Expected an 'Inventory' dispatch case.");

        var inventoryFunctionStart = text.IndexOf("function Invoke-Inventory", StringComparison.Ordinal);
        Assert.True(inventoryFunctionStart >= 0, "Expected a function named Invoke-Inventory.");
        var nextFunctionMatch = Regex.Match(
            text[(inventoryFunctionStart + 1)..],
            @"^function\s+\S+",
            RegexOptions.Multiline);
        var inventoryFunctionEnd = nextFunctionMatch.Success
            ? inventoryFunctionStart + 1 + nextFunctionMatch.Index
            : text.Length;
        var inventoryFunctionText = text[inventoryFunctionStart..inventoryFunctionEnd];
        Assert.DoesNotContain("network_write", inventoryFunctionText, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-NetworkWriteApply", inventoryFunctionText, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-NetworkWritePreview", inventoryFunctionText, StringComparison.Ordinal);
        Assert.Contains("Read-HardwareConfig", inventoryFunctionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_PreviewModeNeverReachesAConfirmingApplyCall()
    {
        var text = ReadScript();

        // Exactly one confirm:$true call site must exist anywhere in the script (Apply must be
        // reachable at all), and it must live inside Invoke-NetworkWriteApply.
        var confirmTrueSites = Regex.Matches(text, @"confirm\s*=\s*\$true");
        Assert.True(
            confirmTrueSites.Count == 1,
            $"Expected exactly one confirm:$true call site (in Invoke-NetworkWriteApply); found {confirmTrueSites.Count}.");

        var applyFunctionStart = text.IndexOf("function Invoke-NetworkWriteApply", StringComparison.Ordinal);
        Assert.True(applyFunctionStart >= 0, "Expected a function named Invoke-NetworkWriteApply.");
        var nextFunctionMatch = Regex.Match(
            text[(applyFunctionStart + 1)..],
            @"^function\s+\S+",
            RegexOptions.Multiline);
        var applyFunctionEnd = nextFunctionMatch.Success
            ? applyFunctionStart + 1 + nextFunctionMatch.Index
            : text.Length;
        Assert.InRange(confirmTrueSites[0].Index, applyFunctionStart, applyFunctionEnd);

        // The Invoke-Preview function itself must never contain a confirming call.
        var previewFunctionStart = text.IndexOf("function Invoke-Preview", StringComparison.Ordinal);
        Assert.True(previewFunctionStart >= 0, "Expected a function named Invoke-Preview.");
        var previewNextFunctionMatch = Regex.Match(
            text[(previewFunctionStart + 1)..],
            @"^function\s+\S+",
            RegexOptions.Multiline);
        var previewFunctionEnd = previewNextFunctionMatch.Success
            ? previewFunctionStart + 1 + previewNextFunctionMatch.Index
            : text.Length;
        var previewFunctionText = text[previewFunctionStart..previewFunctionEnd];
        Assert.DoesNotMatch(new Regex(@"confirm\s*=\s*\$true"), previewFunctionText);
        Assert.DoesNotContain("Invoke-NetworkWriteApply", previewFunctionText, StringComparison.Ordinal);
        Assert.Contains("Invoke-NetworkWritePreview", previewFunctionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyModeRequiresAllowMutationSwitchPlusExactAcknowledgementString()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"\[switch\]\s*\$AllowMutation"), text);
        Assert.Matches(new Regex(@"\[string\]\s*\$Acknowledgement"), text);
        Assert.Contains(
            "$script:RequiredAcknowledgement = 'DELETE SUBNETS AND KEEP DEVICES'",
            text,
            StringComparison.Ordinal);

        Assert.Matches(new Regex(@"if\s*\(\s*\$Mode\s*-eq\s*'Apply'\s*\)"), text);
        Assert.Matches(new Regex(@"if\s*\(\s*-not\s*\$AllowMutation\s*\)"), text);

        // The acknowledgement comparison must be case-sensitive exact ('-cne'), not the default
        // case-insensitive '-ne' -- "no shortcut" means a differently-cased string must fail too.
        Assert.Matches(
            new Regex(@"\$Acknowledgement\s+-cne\s+\$script:RequiredAcknowledgement"),
            text);
        Assert.DoesNotMatch(new Regex(@"\$Acknowledgement\s+-eq\s"), text);

        // Both gates are validated before the MCP host is ever launched.
        var allowMutationCheckIndex = text.IndexOf("if (-not $AllowMutation)", StringComparison.Ordinal);
        var acknowledgementCheckIndex = text.IndexOf("-cne $script:RequiredAcknowledgement", StringComparison.Ordinal);
        var connectIndex = text.IndexOf("function Connect-McpHost", StringComparison.Ordinal);
        Assert.True(allowMutationCheckIndex >= 0 && allowMutationCheckIndex < connectIndex);
        Assert.True(acknowledgementCheckIndex >= 0 && acknowledgementCheckIndex < connectIndex);
    }

    [Fact]
    public void Script_RequiresAnExplicitAp21ProjectPathForEveryModeIncludingMutation()
    {
        var text = ReadScript();

        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
        Assert.Matches(
            new Regex(@"\$ProjectPath\.EndsWith\(\s*'\.ap21'\s*,\s*\[System\.StringComparison\]::OrdinalIgnoreCase\s*\)"),
            text);

        // The .ap21 check runs unconditionally before mode-specific gating, so it is required
        // for the mutation path too.
        var ap21CheckIndex = text.IndexOf(".EndsWith('.ap21'", StringComparison.Ordinal);
        var applyGateIndex = text.IndexOf("if ($Mode -eq 'Apply')", StringComparison.Ordinal);
        Assert.True(ap21CheckIndex >= 0 && ap21CheckIndex < applyGateIndex);
    }

    [Fact]
    public void Script_RequiresExactConnectedSubnetIdsNeverNamesForPreviewAndApply()
    {
        var text = ReadScript();
        Assert.Contains("$ConnectedEthernetSubnetId", text, StringComparison.Ordinal);
        Assert.Contains("$ConnectedProfibusSubnetId", text, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"if\s*\(\s*\$Mode\s*-ne\s*'Inventory'\s*\)"), text);
        Assert.Contains("A name is never accepted here", text, StringComparison.Ordinal);

        // Delete operations target subnetId, never a name field.
        Assert.Matches(
            new Regex(@"New-DeleteSubnetOperation[\s\S]{0,200}-SubnetId \$ConnectedEthernetSubnetId"),
            text);
        Assert.Matches(
            new Regex(@"New-DeleteSubnetOperation[\s\S]{0,200}-SubnetId \$ConnectedProfibusSubnetId"),
            text);
    }

    [Fact]
    public void Script_WritesATimestampedJsonArtifactUnderTheIgnoredPhase4ArtifactDirectory()
    {
        var text = ReadScript();
        Assert.Contains("artifacts", text, StringComparison.Ordinal);
        Assert.Contains("live-network-phase4", text, StringComparison.Ordinal);
        Assert.Contains("Join-Path", text, StringComparison.Ordinal);
        Assert.Contains("Get-Date -Format", text, StringComparison.Ordinal);
        Assert.Contains("ConvertTo-Json -Depth", text, StringComparison.Ordinal);
        Assert.Contains("Set-Content", text, StringComparison.Ordinal);

        var gitignore = File.ReadAllText(Path.Combine(GetRepositoryRoot(), ".gitignore"));
        Assert.Contains("artifacts/", gitignore, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RedactsSafetyTokensFromEveryPersistedPreviewRecord()
    {
        var text = ReadScript();

        Assert.Contains("function Get-RedactedPreviewRecord", text, StringComparison.Ordinal);
        Assert.Contains("'REDACTED-NOT-PERSISTED'", text, StringComparison.Ordinal);
        Assert.Contains(
            "The real safetyToken is NEVER placed here",
            text,
            StringComparison.Ordinal);

        // Get-RedactedPreviewRecord itself must not read the raw token value into its output.
        var functionStart = text.IndexOf("function Get-RedactedPreviewRecord", StringComparison.Ordinal);
        var nextFunctionMatch = Regex.Match(
            text[(functionStart + 1)..],
            @"^function\s+\S+",
            RegexOptions.Multiline);
        var functionEnd = nextFunctionMatch.Success
            ? functionStart + 1 + nextFunctionMatch.Index
            : text.Length;
        var functionText = text[functionStart..functionEnd];
        Assert.DoesNotContain("Preview.preview.safetyToken", functionText, StringComparison.Ordinal);

        // Every $evidence-bound record comes from Get-RedactedPreviewRecord, never straight from
        // the raw preview response.
        Assert.DoesNotMatch(new Regex(@"\$evidence(\[|\.)[^\n=]*=\s*\$preview\.preview"), text);
    }

    [Fact]
    public void Script_ProcessCleanupHappensInAFinallyBlock()
    {
        var text = ReadScript();

        var tryIndex = text.IndexOf("$evidence = $null\ntry {", StringComparison.Ordinal);
        Assert.True(tryIndex >= 0, "Expected the main dispatch to be wrapped in a try block.");
        var finallyIndex = text.IndexOf("finally {\n    Stop-McpHost\n}", StringComparison.Ordinal);
        Assert.True(finallyIndex > tryIndex, "Expected Stop-McpHost inside a finally block after the main try.");
    }

    [Fact]
    public void Script_UsesOnlyThePublicNetworkReadWriteRouteAndNeverTheInternalMutationProbe()
    {
        var text = ReadScript();

        foreach (var token in new[]
        {
            "'initialize'",
            "notifications/initialized",
            "tools/call",
            "jsonrpc",
            "network_read",
            "network_write",
            "TiaMcpServer",
        })
        {
            Assert.Contains(token, text, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("probe_subnet_lifecycle_mutations", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SubnetLifecycleMutationProbeService", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RecordsServerCommitTiaVersionRequestedOperationsAndDeviceCounts()
    {
        var text = ReadScript();

        foreach (var token in new[]
        {
            "function Get-ServerCommit",
            "git -C $script:RepositoryRoot rev-parse HEAD",
            "serverCommit",
            "function Get-TiaVersion",
            "get_project_status",
            "tiaVersion",
            "requestedOperations",
            "deviceCountBefore",
            "postReadRootDeviceCount",
            "finalRootDeviceCount",
            "rootDeviceCountUnchanged",
        })
        {
            Assert.Contains(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Script_RecordsPreviewsApplyResultsAndPostReadsPerLifecycleGroup()
    {
        var text = ReadScript();

        foreach (var token in new[]
        {
            "function Invoke-LifecycleGroupAndVerify",
            "applyResults",
            "postReadSubnetIds",
            "operationId",
            "networkDeviceCountUnchanged",
        })
        {
            Assert.Contains(token, text, StringComparison.Ordinal);
        }

        // Every successful item's result is checked against the exact four-member minimal shape.
        Assert.Matches(
            new Regex(@"'name'\s*,\s*'networkDeviceCount'\s*,\s*'networkDeviceCountUnchanged'\s*,\s*'subnetId'"),
            text);
    }

    [Fact]
    public void Script_ApplyCoversTheThreeVerbsAndConnectedDeletionWithoutDeletingDevices()
    {
        var text = ReadScript();

        foreach (var token in new[]
        {
            "create_subnet",
            "update_subnet",
            "delete_subnet",
            "create-isolated-subnets",
            "update-isolated-subnets",
            "delete-isolated-subnets",
            "delete-connected-subnets",
            "connected subnet deletion must never remove devices",
            "expectedProjectStateEffects",
        })
        {
            Assert.Contains(token, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Script_NeverCallsProjectSaveOrCompile()
    {
        var text = ReadScript();

        foreach (var forbidden in new[]
        {
            "save_project",
            "compile_check",
            "Project.Save",
            "archive_project",
            "close_project",
        })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Script_EveryApplyCallReusesTheUnchangedOrderedRequestAndTokenFromItsOwnPreview()
    {
        var text = ReadScript();

        var functionStart = text.IndexOf("function Invoke-LifecycleGroupAndVerify", StringComparison.Ordinal);
        Assert.True(functionStart >= 0, "Expected a function named Invoke-LifecycleGroupAndVerify.");
        var nextFunctionMatch = Regex.Match(
            text[(functionStart + 1)..],
            @"^function\s+\S+",
            RegexOptions.Multiline);
        var functionEnd = nextFunctionMatch.Success
            ? functionStart + 1 + nextFunctionMatch.Index
            : text.Length;
        var functionText = text[functionStart..functionEnd];

        Assert.Matches(
            new Regex(@"Invoke-NetworkWritePreview\s+-Operations\s+\$Operations"),
            functionText);
        Assert.Matches(
            new Regex(@"Invoke-NetworkWriteApply\s+-Operations\s+\$Operations\s+-SafetyToken\s+\$token"),
            functionText);
        Assert.Contains("never rebuilt", functionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ReadMcpResponseUsesAnAsyncReadWithARealPerIterationTimeoutInsteadOfABlockingReadLine()
    {
        var text = ReadScript();
        var functionText = ExtractFunctionBody(text, "Read-McpResponse");

        // A bare synchronous ReadLine() blocks the calling thread until a line arrives, so the
        // surrounding deadline loop never gets a chance to observe the timeout while a call is in
        // flight -- StartupTimeoutSeconds could never actually interrupt a hung host. This must be
        // replaced with an async read the loop can wait on with a bounded, re-computed timeout.
        Assert.DoesNotContain("StandardOutput.ReadLine()", functionText, StringComparison.Ordinal);
        Assert.Contains("StandardOutput.ReadLineAsync()", functionText, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\.Wait\(\s*\$remainingMs\s*\)"), functionText);

        // The remaining budget must be recomputed from the deadline every iteration (not read once
        // before the loop), otherwise the timeout could never shrink as time passes.
        Assert.Matches(new Regex(@"\$remainingMs\s*=.*\$deadline"), functionText);
    }

    [Fact]
    public void Script_GetExistingSubnetNameDoesNotShadowTheAutomaticMatchesVariable()
    {
        var text = ReadScript();
        var functionText = ExtractFunctionBody(text, "Get-ExistingSubnetName");

        // $matches is a PowerShell automatic variable populated by the -match operator; using it
        // as an ordinary local variable name is legal but risks silently colliding with a future
        // -match usage in the same scope.
        Assert.DoesNotMatch(new Regex(@"\$matches\b"), functionText);
    }

    [Fact]
    public void Script_PreviewIncludesThreeNonMutatingNegativeCasesUsingTheNonThrowingHelper()
    {
        var text = ReadScript();
        var previewFunctionText = ExtractFunctionBody(text, "Invoke-Preview");

        // A new non-throwing helper is required because Invoke-McpToolCall throws on isError:true
        // -- the wrong behavior for a case that is EXPECTED to fail.
        Assert.Contains("function Invoke-McpToolCallExpectingError", text, StringComparison.Ordinal);
        var helperFunctionText = ExtractFunctionBody(text, "Invoke-McpToolCallExpectingError");
        Assert.Matches(new Regex(@"if\s*\(\s*-not\s*\$result\.isError\s*\)"), helperFunctionText);
        Assert.Contains("throw", helperFunctionText, StringComparison.Ordinal);

        // 1. Invalid transmission-speed symbol -- rejected by NetworkOperationCatalog.ValidateWrite
        //    before any hardware state is even read.
        Assert.Contains("NotARealBaudRate", previewFunctionText, StringComparison.Ordinal);

        // 2. PROFIBUS-only field on an Ethernet target -- rejected during preview's target
        //    resolution (NetworkIdentityResolver.ResolveExistingSubnet).
        Assert.Matches(
            new Regex(@"-SubnetId\s+\$ConnectedEthernetSubnetId[\s\S]{0,200}highestAddress\s*=\s*10"),
            previewFunctionText);

        // 3. A bogus subnetId, synthesized deterministically from a real one and defensively
        //    checked to be absent before use -- rejected during preview's target resolution's
        //    exact-one-match check.
        Assert.Matches(new Regex(@"\$bogusSubnetId\s*=\s*""\$ConnectedEthernetSubnetId-does-not-exist"""), previewFunctionText);
        Assert.Matches(new Regex(@"\.Count\s*-ne\s*0\s*\)\s*\{\s*\r?\n\s*throw"), previewFunctionText);

        // All three negative cases must use the non-throwing helper, never the throwing one, and
        // every negative case's asserted error category must be checked explicitly.
        var negativeCaseCallSites = Regex.Matches(previewFunctionText, @"Invoke-McpToolCallExpectingError");
        Assert.True(negativeCaseCallSites.Count >= 3, $"Expected at least 3 uses of Invoke-McpToolCallExpectingError in Invoke-Preview; found {negativeCaseCallSites.Count}.");
        Assert.Contains("'validation_error'", previewFunctionText, StringComparison.Ordinal);
        Assert.Contains("'postcondition_failed'", previewFunctionText, StringComparison.Ordinal);

        // The negative cases are recorded in the evidence artifact.
        Assert.Contains("negativeCases", previewFunctionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_NegativeCasesNeverAppearInApplyOrCallInvokeNetworkWriteApply()
    {
        var text = ReadScript();
        var previewFunctionText = ExtractFunctionBody(text, "Invoke-Preview");
        var applyFunctionText = ExtractFunctionBody(text, "Invoke-Apply");

        // The three negative cases stay entirely within Invoke-Preview, confirm:false only -- they
        // must never reach Invoke-NetworkWriteApply, and Invoke-Apply must never reference them.
        Assert.DoesNotContain("Invoke-NetworkWriteApply", previewFunctionText, StringComparison.Ordinal);
        Assert.DoesNotContain("Invoke-McpToolCallExpectingError", applyFunctionText, StringComparison.Ordinal);
        Assert.DoesNotContain("NotARealBaudRate", applyFunctionText, StringComparison.Ordinal);
        Assert.DoesNotContain("-does-not-exist", applyFunctionText, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_DescriptionMentionsTheNonMutatingNegativePreviewCases()
    {
        var text = ReadScript();
        var descriptionEnd = text.IndexOf(".PARAMETER", StringComparison.Ordinal);
        Assert.True(descriptionEnd > 0, "Expected a .PARAMETER section after .DESCRIPTION.");
        var descriptionText = text[..descriptionEnd];

        Assert.Contains("negative", descriptionText, StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractFunctionBody(string text, string functionName)
    {
        var functionStart = text.IndexOf($"function {functionName}", StringComparison.Ordinal);
        Assert.True(functionStart >= 0, $"Expected a function named {functionName}.");
        var nextFunctionMatch = Regex.Match(
            text[(functionStart + 1)..],
            @"^function\s+\S+",
            RegexOptions.Multiline);
        var functionEnd = nextFunctionMatch.Success
            ? functionStart + 1 + nextFunctionMatch.Index
            : text.Length;
        return text[functionStart..functionEnd];
    }

    [Fact]
    public void NoOrdinaryTestInvokesThePhase4LiveHarnessScript()
    {
        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "NetworkPhase4SubnetLiveHarnessContractTests.cs");

        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(
                Path.GetFullPath(path),
                Path.GetFullPath(thisFile),
                StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains(
                "live-test-network-phase4-subnets.ps1", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected the live harness at '{ScriptPath}'.");
        return File.ReadAllText(ScriptPath);
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }
}
