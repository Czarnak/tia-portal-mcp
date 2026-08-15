using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

public sealed class VciMutationProbeScriptTests
{
    private const string Acknowledgement =
        "I_UNDERSTAND_VCI_MUTATES_DISPOSABLE_PROJECTS_AND_WORKSPACE_FILES";

    private static readonly string RepositoryRoot = GetRepositoryRoot();
    private static readonly string ScriptPath = Path.Combine(
        RepositoryRoot,
        "scripts",
        "live-probe-vci-phase1-mutation.ps1");

    [Fact]
    public void Script_IsAsciiPowerShell7StrictAndDefaultsToDescribe()
    {
        var source = ReadScript();

        Assert.Contains("#Requires -Version 7", source, StringComparison.Ordinal);
        Assert.Contains("Set-StrictMode -Version Latest", source, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", source, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('Describe', 'Inventory', 'Apply')]", source, StringComparison.Ordinal);
        Assert.Contains("[string] $Mode = 'Describe'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_EmitsOneCompleteInertDocument()
    {
        var result = RunPowerShell("-File", ScriptPath);

        AssertSuccess(result);
        Assert.DoesNotContain('\n', result.StandardOutput.TrimEnd('\r', '\n'));
        using var document = JsonDocument.Parse(result.StandardOutput);
        var root = document.RootElement;
        Assert.Equal("vci-phase1-mutation-harness/v1", root.GetProperty("schemaVersion").GetString());
        Assert.Equal("vci-phase1-mutation-scenarios/v1", root.GetProperty("manifestSchemaVersion").GetString());
        Assert.Equal("probe_vci_mutation_contract", root.GetProperty("workerOperation").GetString());
        Assert.Equal("read-write", root.GetProperty("workerAccessMode").GetString());
        Assert.True(root.GetProperty("requiresSeparateLiveAuthorization").GetBoolean());
        Assert.Equal(Acknowledgement, root.GetProperty("acknowledgement").GetString());
        Assert.Equal(
            VciMutationProbeContract.CaseIds,
            root.GetProperty("caseIds").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            new[]
            {
                "lifecycle", "mapping", "project_to_workspace", "workspace_to_project",
                "negative", "transaction",
            },
            root.GetProperty("scenarioOrder").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains(
            root.GetProperty("parameters").EnumerateArray(),
            parameter => parameter.GetProperty("name").GetString() == "ScenarioManifestPath");
        Assert.Contains(
            root.GetProperty("stopConditions").EnumerateArray(),
            value => value.GetString()!.Contains("uncertain", StringComparison.Ordinal));
        Assert.Contains(
            root.GetProperty("safetyRules").EnumerateArray(),
            value => value.GetString()!.Contains("no project open", StringComparison.Ordinal));
        Assert.Contains("retain", root.GetProperty("retentionPolicy").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "Describe did not open or create any TIA process or filesystem path.",
            root.GetProperty("inertStatement").GetString());
    }

    [Fact]
    public void Source_HasNoPersistenceCompilationDownloadOrAutomaticRetryEscapeHatch()
    {
        var source = ReadScript();

        Assert.DoesNotContain("Project.Save", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".SaveAs(", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Compile(", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".Download(", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ErrorDataReceived", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginErrorReadLine", source, StringComparison.Ordinal);
        Assert.DoesNotContain("automaticRetry", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("retryCount", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoOrdinaryTestOrCiWorkflowInvokesTheLiveMutationApplyMode()
    {
        var references = Directory.EnumerateFiles(RepositoryRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => new[] { ".cs", ".ps1", ".yml", ".yaml" }
                .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Where(path => !string.Equals(path, ScriptPath, StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.EndsWith(nameof(VciMutationProbeScriptTests) + ".cs", StringComparison.Ordinal))
            .Where(path =>
            {
                var source = File.ReadAllText(path);
                return source.Contains("live-probe-vci-phase1-mutation.ps1", StringComparison.Ordinal) &&
                    source.Contains("-Mode Apply", StringComparison.Ordinal);
            })
            .ToArray();

        Assert.Empty(references);
    }

    [Fact]
    public void Inventory_RejectsInvalidManifestAndUnsafeOrMissingInputsBeforeWorkerInvocation()
    {
        using var fixture = new HarnessFixture();

        var unknownFieldManifest = fixture.WriteManifest(",\"unexpected\":true");
        AssertFailureContains(
            fixture.Run("Inventory", manifestPath: unknownFieldManifest),
            "manifest_unknown_property:unexpected");

        var missingPathManifest = fixture.WriteManifest(
            lifecycleOverride: Path.Combine(fixture.Root, "missing.ap21"));
        AssertFailureContains(
            fixture.Run("Inventory", manifestPath: missingPathManifest),
            "project_path_not_found:lifecycleProjectPath");

        var duplicateManifest = fixture.WriteManifest(
            mappingOverride: fixture.LifecycleProjectPath);
        AssertFailureContains(
            fixture.Run("Inventory", manifestPath: duplicateManifest),
            "disposable_project_paths_not_distinct");

        var originalEqualManifest = fixture.WriteManifest(
            lifecycleOverride: fixture.OriginalProjectPath);
        AssertFailureContains(
            fixture.Run("Inventory", manifestPath: originalEqualManifest),
            "disposable_project_matches_original:lifecycleProjectPath");

        AssertFailureContains(
            fixture.Run("Inventory", workspaceRoot: RepositoryRoot),
            "workspace_root_unsafe");
        AssertFailureContains(
            fixture.Run("Inventory", workspaceRoot: Path.GetDirectoryName(fixture.LifecycleProjectPath)),
            "workspace_root_unsafe:protected_path");
        AssertFailureContains(
            fixture.Run("Inventory", workerExecutable: Path.Combine(fixture.Root, "missing-worker.exe")),
            "worker_not_found");
        AssertFailureContains(
            fixture.Run("Inventory", extraArguments: new[] { "-WorkerAccessMode", "read-only" }),
            "worker_must_be_read_write");

        Assert.False(File.Exists(fixture.WorkerLogPath));
    }

    [Fact]
    public void Inventory_AcceptsTheProductionWorkerEnvelopeWithoutRequestId()
    {
        using var fixture = new HarnessFixture();

        var result = fixture.Run("Inventory");

        AssertSuccess(result);
        Assert.True(File.Exists(fixture.PlanPath));
    }

    [Fact]
    public void Inventory_SendsOnlyProductionWorkerRequestFields()
    {
        using var fixture = new HarnessFixture();

        var result = fixture.Run("Inventory");

        AssertSuccess(result);
        Assert.All(
            File.ReadLines(fixture.WorkerLogPath),
            line => Assert.False(JsonDocument.Parse(line).RootElement.TryGetProperty("requestId", out _)));
    }

    [Fact]
    public void Apply_RejectsAbsentAcknowledgementAndPlanHashMismatchBeforeWorkerInvocation()
    {
        using var fixture = new HarnessFixture();
        AssertSuccess(fixture.Run("Inventory"));
        File.Delete(fixture.WorkerLogPath);

        AssertFailureContains(
            fixture.Run(
                "Apply",
                extraArguments: new[] { "-AllowMutation", "-PlanHash", fixture.PlanHash }),
            "acknowledgement_required");
        AssertFailureContains(
            fixture.Run(
                "Apply",
                extraArguments: new[]
                {
                    "-AllowMutation", "-NonInteractiveAcceptance", "-Acknowledgement", Acknowledgement,
                    "-PlanHash", new string('0', 64),
                }),
            "plan_hash_mismatch");

        Assert.False(File.Exists(fixture.WorkerLogPath));
    }

    [Fact]
    public void Inventory_ReportsCompactDiagnosticWhenWorkerResultIsUnusable()
    {
        using var fixture = new HarnessFixture();

        var result = fixture.Run("Inventory", behavior: "inventory_not_observable");

        AssertFailureContains(result, "inventory_result_not_usable:");
        AssertFailureContains(result, "\"caseInstanceId\":\"inventory-1\"");
        AssertFailureContains(result, "\"outcome\":\"not_observable\"");
        AssertFailureContains(result, "\"notObservableReason\":\"required_fixture_state_not_available\"");
        AssertFailureContains(result, "\"exceptionTypeName\":\"System.InvalidOperationException\"");
        AssertFailureContains(result, "\"preconditionFailures\":[\"fixture_ready\"]");
        AssertFailureContains(result, "\"omissionCount\":1");
        Assert.False(Directory.Exists(fixture.EvidenceRoot));
        Assert.False(Directory.Exists(fixture.WorkspaceRoot));
    }

    [Fact]
    public void Inventory_InvokesOnlyInventoryOncePerDisposableCopyAndWritesHashedPlan()
    {
        using var fixture = new HarnessFixture();

        var result = fixture.Run("Inventory");

        AssertSuccess(result);
        Assert.False(Directory.Exists(fixture.WorkspaceRoot));
        Assert.True(File.Exists(fixture.InventoryPath));
        Assert.True(File.Exists(fixture.PlanPath));

        var requests = File.ReadAllLines(fixture.WorkerLogPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert.Equal(6, requests.Length);
        Assert.Single(
            requests.Select(request => request.GetProperty("fakeWorkerInstanceId").GetString()).Distinct());
        Assert.All(requests, request =>
        {
            Assert.Equal("probe_vci_mutation_contract", request.GetProperty("method").GetString());
            var probe = request.GetProperty("vciMutationProbe");
            Assert.Equal("P-INVENTORY", probe.GetProperty("caseId").GetString());
            Assert.Equal("Inventory", probe.GetProperty("mode").GetString());
            Assert.False(probe.TryGetProperty("workspace", out _));
            Assert.Equal("SimaticML", probe.GetProperty("fileFormat").GetString());
        });

        using var inventory = JsonDocument.Parse(File.ReadAllText(fixture.InventoryPath));
        Assert.Equal("vci-phase1-mutation-inventory/v1", inventory.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(6, inventory.RootElement.GetProperty("projects").GetArrayLength());
        Assert.All(
            inventory.RootElement.GetProperty("projects").EnumerateArray(),
            project => Assert.Equal("Simulation_DB", project.GetProperty("selectedObjectName").GetString()));

        using var plan = JsonDocument.Parse(File.ReadAllText(fixture.PlanPath));
        var planRoot = plan.RootElement;
        Assert.Equal("vci-phase1-mutation-plan-evidence/v1", planRoot.GetProperty("schemaVersion").GetString());
        var canonicalPlan = planRoot.GetProperty("canonicalPlan").GetRawText();
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPlan))).ToLowerInvariant();
        Assert.Equal(expectedHash, planRoot.GetProperty("planHash").GetString());
        Assert.Equal(fixture.WorkspaceRoot, planRoot.GetProperty("canonicalPlan").GetProperty("workspaceRoot").GetString());
        Assert.Equal(
            Acknowledgement,
            planRoot.GetProperty("canonicalPlan").GetProperty("acknowledgement").GetString());
        Assert.Equal(6, planRoot.GetProperty("canonicalPlan").GetProperty("projects").GetArrayLength());
        Assert.Equal(4, planRoot.GetProperty("canonicalPlan").GetProperty("selectedObject").GetProperty("structuralPath").GetArrayLength());

        using var stdout = JsonDocument.Parse(result.StandardOutput);
        Assert.Equal(expectedHash, stdout.RootElement.GetProperty("planHash").GetString());
        Assert.False(stdout.RootElement.GetProperty("workspaceRootExistsAfter").GetBoolean());
    }

    [Fact]
    public void Apply_InvokesEveryPlannedStepInOrderAndWritesTheCompleteEvidenceBundle()
    {
        using var fixture = new HarnessFixture();
        AssertSuccess(fixture.Run("Inventory"));
        File.Delete(fixture.WorkerLogPath);

        var result = fixture.RunApply();

        AssertSuccess(result);
        var expectedSteps = fixture.PlannedSteps;
        var expectedCases = expectedSteps.Select(step => step.CaseId).ToArray();
        var expectedWorkerSteps = expectedSteps
            .Where(step => step.InvocationLayer == "worker")
            .ToArray();
        var requests = File.ReadAllLines(fixture.WorkerLogPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert.Equal(expectedWorkerSteps.Select(step => step.CaseId), requests.Select(request =>
            request.GetProperty("vciMutationProbe").GetProperty("caseId").GetString()));
        Assert.Equal(expectedWorkerSteps.Length, requests.Length);
        Assert.All(
            expectedSteps,
            step => Assert.Single(expectedSteps.Where(candidate => candidate.StepId == step.StepId)));
        Assert.All(
            VciMutationProbeContract.CaseIds.Where(caseId => caseId != "P-INVENTORY"),
            caseId => Assert.Contains(expectedSteps, step => step.CaseId == caseId));
        Assert.Equal(
            expectedWorkerSteps.Select(step => step.StepId),
            requests.Select(request => request.GetProperty("vciMutationProbe").GetProperty("caseInstanceId").GetString()!
                .Split(':', 2, StringSplitOptions.None)[0]));
        Assert.All(requests, request =>
            Assert.Equal(JsonValueKind.Null, request.GetProperty("vciMutationProbe").GetProperty("mapping").ValueKind));
        foreach (var family in expectedWorkerSteps.GroupBy(step => step.Family, StringComparer.Ordinal))
        {
            var instanceIds = requests
                .Where(request => request.GetProperty("vciMutationProbe").GetProperty("sessionId").GetString() == family.Key)
                .Select(request => request.GetProperty("fakeWorkerInstanceId").GetString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.Single(instanceIds);
        }
        Assert.Equal(
            expectedWorkerSteps.Select(step => step.Family).Distinct(StringComparer.Ordinal).Count(),
            requests.Select(request => request.GetProperty("fakeWorkerInstanceId").GetString()).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(
            requests,
            request => request.GetProperty("vciMutationProbe").GetProperty("caseId").GetString() is
                "N-WORKSPACE-PATH-RELATIVE" or
                "N-WORKSPACE-PATH-MISSING-PARENT" or
                "N-WORKSPACE-PATH-CONFLICT" or
                "N-WORKSPACE-PATH-FILE" or
                "N-FILENAME-ABSOLUTE" or
                "N-FILENAME-TRAVERSAL");

        var p2wRequest = Assert.Single(requests.Where(request =>
            request.GetProperty("vciMutationProbe").GetProperty("caseId").GetString() == "M-P2W"));
        var w2pRequest = Assert.Single(requests.Where(request =>
            request.GetProperty("vciMutationProbe").GetProperty("caseId").GetString() == "M-W2P"));
        var p2wProbe = p2wRequest.GetProperty("vciMutationProbe");
        var w2pProbe = w2pRequest.GetProperty("vciMutationProbe");
        Assert.NotEqual(p2wProbe.GetProperty("workspaceName").GetString(), w2pProbe.GetProperty("workspaceName").GetString());
        Assert.Equal("mapping\\expected", p2wProbe.GetProperty("relativeDirectory").GetString());
        Assert.Equal("Simulation_DB_Changed", p2wProbe.GetProperty("fileName").GetString());
        Assert.Equal("mapping\\expected", w2pProbe.GetProperty("relativeDirectory").GetString());
        Assert.Equal("Simulation_DB_Baseline", w2pProbe.GetProperty("fileName").GetString());

        var projectToWorkspace = expectedSteps.Where(step => step.Family == "project_to_workspace").ToArray();
        Assert.Equal(
            new[]
            {
                "workspaceToProjectBaselineProjectPath", "workspaceToProjectBaselineProjectPath",
                "workspaceToProjectBaselineProjectPath", "workspaceToProjectBaselineProjectPath",
                "projectToWorkspaceChangedProjectPath", "projectToWorkspaceChangedProjectPath",
                "projectToWorkspaceChangedProjectPath", "projectToWorkspaceChangedProjectPath",
                "projectToWorkspaceChangedProjectPath", "projectToWorkspaceChangedProjectPath",
                "projectToWorkspaceChangedProjectPath",
            },
            projectToWorkspace.Select(step => step.ProjectRole));
        Assert.Equal("M-P2W", projectToWorkspace[^1].CaseId);
        Assert.Contains(expectedSteps, step => step.Family == "negative" && step.CaseId == "N-SYNC-PROJECT-ONLY");
        Assert.Contains(expectedSteps, step => step.Family == "negative" && step.CaseId == "N-SYNC-WORKSPACE-ONLY");
        Assert.All(
            VciMutationProbeContract.CaseIds.Where(caseId => caseId.StartsWith("M-TX-", StringComparison.Ordinal)),
            caseId => Assert.Single(expectedSteps.Where(step => step.CaseId == caseId)));

        Assert.Equal(
            new[]
            {
                "cases.jsonl", "filesystem-after.json", "filesystem-before.json", "inventory.json",
                "manifest.json", "plan.json", "snapshot-after.json", "snapshot-before.json", "summary.json",
            },
            Directory.EnumerateFiles(fixture.RunEvidenceRoot)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .OrderBy(name => name, StringComparer.Ordinal));
        Assert.NotEqual(fixture.EvidenceRoot, fixture.RunEvidenceRoot);
        Assert.Equal(fixture.EvidenceRoot, Directory.GetParent(fixture.RunEvidenceRoot)!.FullName);
        Assert.True(File.Exists(Path.Combine(fixture.WorkspaceRoot, ".vci-mutation-run.json")));

        var records = File.ReadAllLines(fixture.CasesPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert.Equal(expectedCases.Length, records.Length);
        Assert.Equal(expectedCases, records.Select(record => record.GetProperty("caseId").GetString()));
        Assert.Equal(expectedSteps.Select(step => step.StepId), records.Select(record => record.GetProperty("stepId").GetString()));
        Assert.All(records, record =>
        {
            Assert.True(record.GetProperty("terminal").GetBoolean());
            Assert.True(record.GetProperty("filesystemBeforeSnapshotId").GetString()!.Length == 64);
            Assert.True(record.GetProperty("filesystemAfterSnapshotId").GetString()!.Length == 64);
            if (record.GetProperty("invocationLayer").GetString() == "harness_confinement")
            {
                Assert.Equal("harness_confinement", record.GetProperty("transport").GetProperty("outcome").GetString());
                Assert.Equal(JsonValueKind.Null, record.GetProperty("workerResult").ValueKind);
                Assert.False(record.GetProperty("harnessObservation").GetProperty("workerRequestSent").GetBoolean());
                Assert.Equal(
                    "harness_confinement_rejected_before_worker",
                    record.GetProperty("harnessObservation").GetProperty("notObservableReason").GetString());
            }
            else
            {
                Assert.Equal("response", record.GetProperty("transport").GetProperty("outcome").GetString());
                Assert.Equal(JsonValueKind.Null, record.GetProperty("harnessObservation").ValueKind);
                Assert.True(record.GetProperty("workerResult").GetProperty("canary").GetProperty("attempted").GetBoolean());
                Assert.True(record.GetProperty("workerResult").GetProperty("canary").GetProperty("usable").GetBoolean());
            }
        });

        using var summary = JsonDocument.Parse(File.ReadAllText(fixture.SummaryPath));
        Assert.True(summary.RootElement.GetProperty("overallPass").GetBoolean());
        Assert.Equal(expectedCases.Length, summary.RootElement.GetProperty("requestedCaseCount").GetInt32());
        Assert.Empty(summary.RootElement.GetProperty("stoppedFamilies").EnumerateArray());
        Assert.Empty(summary.RootElement.GetProperty("projectHashMismatches").EnumerateArray());
    }

    [Fact]
    public void Apply_DeclinedInteractiveConfirmationRemovesOnlyTheNewMarkedRootAndDoesNotStartWorker()
    {
        using var fixture = new HarnessFixture();
        AssertSuccess(fixture.Run("Inventory"));
        File.Delete(fixture.WorkerLogPath);

        var result = fixture.Run(
            "Apply",
            extraArguments: new[]
            {
                "-AllowMutation", "-Acknowledgement", Acknowledgement, "-PlanHash", fixture.PlanHash,
            });

        AssertFailureContains(result, "interactive_confirmation_declined");
        Assert.False(Directory.Exists(fixture.WorkspaceRoot));
        Assert.False(File.Exists(fixture.WorkerLogPath));
        Assert.False(File.Exists(fixture.CasesPath));
    }

    [Fact]
    public void Apply_RechecksDisposableProjectHashBeforeCreatingRootOrStartingWorker()
    {
        using var fixture = new HarnessFixture();
        AssertSuccess(fixture.Run("Inventory"));
        File.AppendAllText(fixture.LifecycleProjectPath, "changed-after-inventory", Encoding.UTF8);
        File.Delete(fixture.WorkerLogPath);

        var result = fixture.RunApply();

        AssertFailureContains(result, "plan_project_hash_mismatch:lifecycleProjectPath");
        Assert.False(Directory.Exists(fixture.WorkspaceRoot));
        Assert.False(File.Exists(fixture.WorkerLogPath));
        Assert.False(File.Exists(fixture.CasesPath));
    }

    [Theory]
    [InlineData("process_lost", "process_lost")]
    [InlineData("malformed", "protocol_error")]
    [InlineData("incomplete", "incomplete_evidence")]
    [InlineData("uncertain", "uncertain_mutation")]
    [InlineData("not_observable", "required_step_not_observable")]
    [InlineData("project_file_changed", "project_file_changed")]
    public void Apply_FlushesTerminalFailureStopsOnlyItsFamilyAndNeverInvokesTheCaseAgain(
        string behavior,
        string expectedOutcome)
    {
        using var fixture = new HarnessFixture();
        AssertSuccess(fixture.Run("Inventory"));
        File.Delete(fixture.WorkerLogPath);

        var result = fixture.RunApply(behavior);

        Assert.NotEqual(0, result.ExitCode);
        var records = File.ReadAllLines(fixture.CasesPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        Assert.Equal("M-CANARY", records[0].GetProperty("caseId").GetString());
        var failed = Assert.Single(records.Where(record =>
            record.GetProperty("stepId").GetString() == "lifecycle-group"));
        Assert.Equal(expectedOutcome, failed.GetProperty("transport").GetProperty("outcome").GetString());

        var requestedSteps = File.ReadAllLines(fixture.WorkerLogPath)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("vciMutationProbe").GetProperty("caseInstanceId").GetString()!
                .Split(':', 2, StringSplitOptions.None)[0])
            .ToArray();
        Assert.Single(requestedSteps.Where(stepId => stepId == "lifecycle-group"));
        Assert.DoesNotContain("lifecycle-root", requestedSteps);
        Assert.Contains("mapping-export", requestedSteps);

        using var summary = JsonDocument.Parse(File.ReadAllText(fixture.SummaryPath));
        var stopped = Assert.Single(summary.RootElement.GetProperty("stoppedFamilies").EnumerateArray());
        Assert.Equal("lifecycle", stopped.GetProperty("scenarioId").GetString());
        Assert.Equal("M-GROUP", stopped.GetProperty("caseId").GetString());
    }

    [Fact]
    public void Apply_RecordsCaseTimeoutWithoutRestartingOrReinvokingTheAffectedFamily()
    {
        using var fixture = new HarnessFixture();
        AssertSuccess(fixture.Run("Inventory"));
        File.Delete(fixture.WorkerLogPath);

        var result = fixture.RunApply("timeout");

        Assert.NotEqual(0, result.ExitCode);
        var records = File.ReadAllLines(fixture.CasesPath)
            .Select(line => JsonDocument.Parse(line).RootElement.Clone())
            .ToArray();
        var timedOut = Assert.Single(records.Where(record => record.GetProperty("stepId").GetString() == "lifecycle-group"));
        Assert.Equal("timed_out", timedOut.GetProperty("transport").GetProperty("outcome").GetString());
        var requestedSteps = File.ReadAllLines(fixture.WorkerLogPath)
            .Select(line => JsonDocument.Parse(line).RootElement.GetProperty("vciMutationProbe").GetProperty("caseInstanceId").GetString()!
                .Split(':', 2, StringSplitOptions.None)[0])
            .ToArray();
        Assert.Single(requestedSteps.Where(stepId => stepId == "lifecycle-group"));
        Assert.DoesNotContain("lifecycle-root", requestedSteps);
        Assert.Contains("mapping-export", requestedSteps);
    }

    [Fact]
    public void EquivalentApply_NormalizesVolatileEvidenceAndReportsSemanticDifferences()
    {
        using var first = new HarnessFixture();
        AssertSuccess(first.Run("Inventory"));
        File.Delete(first.WorkerLogPath);
        AssertSuccess(first.RunApply());

        using var equivalent = new HarnessFixture();
        AssertSuccess(equivalent.Run("Inventory"));
        File.Delete(equivalent.WorkerLogPath);
        AssertSuccess(equivalent.RunApply(equivalentEvidenceRoot: first.RunEvidenceRoot));
        using (var summary = JsonDocument.Parse(File.ReadAllText(equivalent.SummaryPath)))
        {
            Assert.Empty(summary.RootElement.GetProperty("normalizedMismatches").EnumerateArray());
        }

        using var changed = new HarnessFixture();
        AssertSuccess(changed.Run("Inventory"));
        File.Delete(changed.WorkerLogPath);
        var changedResult = changed.RunApply("semantic_variant", first.RunEvidenceRoot);
        Assert.NotEqual(0, changedResult.ExitCode);
        using var changedSummary = JsonDocument.Parse(File.ReadAllText(changed.SummaryPath));
        Assert.NotEmpty(changedSummary.RootElement.GetProperty("normalizedMismatches").EnumerateArray());
        Assert.Contains(
            changedSummary.RootElement.GetProperty("normalizedMismatches").EnumerateArray(),
            mismatch => mismatch.GetProperty("caseId").GetString() == "M-CANARY");
    }

    private static string ReadScript()
    {
        Assert.True(File.Exists(ScriptPath), $"Expected VCI mutation probe harness at {ScriptPath}.");
        var bytes = File.ReadAllBytes(ScriptPath);
        Assert.All(bytes, value => Assert.InRange(value, (byte)0, (byte)127));
        return Encoding.ASCII.GetString(bytes);
    }

    private static void AssertSuccess(ScriptResult result)
        => Assert.True(
            result.ExitCode == 0,
            $"Expected success but received {result.ExitCode}.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");

    private static void AssertFailureContains(ScriptResult result, string expected)
    {
        Assert.NotEqual(0, result.ExitCode);
        Assert.True(
            result.StandardError.Contains(expected, StringComparison.Ordinal),
            $"Expected stderr to contain '{expected}'.{Environment.NewLine}" +
            $"stdout:{Environment.NewLine}{result.StandardOutput}{Environment.NewLine}" +
            $"stderr:{Environment.NewLine}{result.StandardError}");
    }

    private static ScriptResult RunPowerShell(params string[] arguments)
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
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pwsh process.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("VCI mutation harness test did not exit.");
        }

        return new ScriptResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string GetRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class HarnessFixture : IDisposable
    {
        public HarnessFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "vci-mutation-harness-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            OriginalProjectPath = CreateProject("Original.ap21");
            LifecycleProjectPath = CreateProject("Lifecycle.ap21");
            MappingProjectPath = CreateProject("Mapping.ap21");
            ChangedProjectPath = CreateProject("Changed.ap21");
            BaselineProjectPath = CreateProject("Baseline.ap21");
            NegativeProjectPath = CreateProject("Negative.ap21");
            TransactionProjectPath = CreateProject("Transaction.ap21");
            WorkspaceRoot = Path.Combine(Root, "workspace-run");
            EvidenceRoot = Path.Combine(Root, "evidence");
            WorkerLogPath = Path.Combine(Root, "worker-requests.jsonl");
            WorkerPath = Path.Combine(Root, "fake-worker.ps1");
            File.WriteAllText(WorkerPath, FakeWorkerScript, new UTF8Encoding(false));
            ManifestPath = WriteManifest();
        }

        public string Root { get; }
        public string OriginalProjectPath { get; }
        public string LifecycleProjectPath { get; }
        public string MappingProjectPath { get; }
        public string ChangedProjectPath { get; }
        public string BaselineProjectPath { get; }
        public string NegativeProjectPath { get; }
        public string TransactionProjectPath { get; }
        public string WorkspaceRoot { get; }
        public string EvidenceRoot { get; }
        public string WorkerLogPath { get; }
        public string WorkerPath { get; }
        public string ManifestPath { get; private set; }
        public string RunEvidenceRoot { get; private set; } = string.Empty;
        public string InventoryPath => Path.Combine(RunEvidenceRoot, "inventory.json");
        public string PlanPath => Path.Combine(RunEvidenceRoot, "plan.json");
        public string CasesPath => Path.Combine(RunEvidenceRoot, "cases.jsonl");
        public string SummaryPath => Path.Combine(RunEvidenceRoot, "summary.json");
        public PlannedStep[] PlannedSteps
        {
            get
            {
                using var plan = JsonDocument.Parse(File.ReadAllText(PlanPath));
                return plan.RootElement.GetProperty("canonicalPlan").GetProperty("scenarios")
                    .EnumerateArray()
                    .SelectMany(scenario => scenario.GetProperty("steps").EnumerateArray()
                        .Select(step => new PlannedStep(
                            scenario.GetProperty("scenarioId").GetString()!,
                            step.GetProperty("stepId").GetString()!,
                            step.GetProperty("caseId").GetString()!,
                            step.GetProperty("projectRole").GetString()!,
                            step.GetProperty("invocationLayer").GetString()!)))
                    .ToArray();
            }
        }
        public string PlanHash
        {
            get
            {
                using var plan = JsonDocument.Parse(File.ReadAllText(PlanPath));
                return plan.RootElement.GetProperty("planHash").GetString()!;
            }
        }

        public ScriptResult Run(
            string mode,
            string? manifestPath = null,
            string? workerExecutable = null,
            string? workspaceRoot = null,
            string[]? extraArguments = null,
            string behavior = "normal")
        {
            var arguments = new List<string>
            {
                "-File", ScriptPath,
                "-Mode", mode,
                "-ScenarioManifestPath", manifestPath ?? ManifestPath,
                "-WorkerExecutable", workerExecutable ?? WorkerPath,
                "-EvidenceRoot", EvidenceRoot,
                "-WorkspaceRoot", workspaceRoot ?? WorkspaceRoot,
                "-TimeoutSeconds", "10",
            };
            if (extraArguments is not null)
            {
                arguments.AddRange(extraArguments);
            }

            var previousLog = Environment.GetEnvironmentVariable("VCI_MUTATION_TEST_WORKER_LOG");
            var previousBehavior = Environment.GetEnvironmentVariable("VCI_MUTATION_TEST_BEHAVIOR");
            Environment.SetEnvironmentVariable("VCI_MUTATION_TEST_WORKER_LOG", WorkerLogPath);
            Environment.SetEnvironmentVariable("VCI_MUTATION_TEST_BEHAVIOR", behavior);
            try
            {
                var result = RunPowerShell(arguments.ToArray());
                if (mode == "Inventory" && result.ExitCode == 0)
                {
                    using var output = JsonDocument.Parse(result.StandardOutput);
                    RunEvidenceRoot = Path.GetDirectoryName(output.RootElement.GetProperty("planPath").GetString())!;
                }
                return result;
            }
            finally
            {
                Environment.SetEnvironmentVariable("VCI_MUTATION_TEST_WORKER_LOG", previousLog);
                Environment.SetEnvironmentVariable("VCI_MUTATION_TEST_BEHAVIOR", previousBehavior);
            }
        }

        public ScriptResult RunApply(
            string behavior = "normal",
            string? equivalentEvidenceRoot = null)
        {
            var arguments = new List<string>
            {
                "-AllowMutation",
                "-NonInteractiveAcceptance",
                "-Acknowledgement", Acknowledgement,
                "-PlanHash", PlanHash,
            };
            if (equivalentEvidenceRoot is not null)
            {
                arguments.Add("-EquivalentEvidenceRoot");
                arguments.Add(equivalentEvidenceRoot);
            }

            return Run("Apply", extraArguments: arguments.ToArray(), behavior: behavior);
        }

        public string WriteManifest(
            string trailingProperty = "",
            string? lifecycleOverride = null,
            string? mappingOverride = null)
        {
            var path = Path.Combine(Root, "manifest-" + Guid.NewGuid().ToString("N") + ".json");
            var json = $$"""
                {
                  "schemaVersion": "vci-phase1-mutation-scenarios/v1",
                  "originalProjectPath": {{JsonSerializer.Serialize(OriginalProjectPath)}},
                  "lifecycleProjectPath": {{JsonSerializer.Serialize(lifecycleOverride ?? LifecycleProjectPath)}},
                  "mappingProjectPath": {{JsonSerializer.Serialize(mappingOverride ?? MappingProjectPath)}},
                  "projectToWorkspaceChangedProjectPath": {{JsonSerializer.Serialize(ChangedProjectPath)}},
                  "workspaceToProjectBaselineProjectPath": {{JsonSerializer.Serialize(BaselineProjectPath)}},
                  "negativeProjectPath": {{JsonSerializer.Serialize(NegativeProjectPath)}},
                  "transactionProjectPath": {{JsonSerializer.Serialize(TransactionProjectPath)}},
                  "selectedObject": {
                    "structuralPath": [
                      { "index": 0, "name": "ET 200SP station_1", "objectType": "Device" },
                      { "index": 0, "name": "PLC_1", "objectType": "PlcSoftware" },
                      { "index": 0, "name": "Program blocks", "objectType": "BlockFolder" },
                      { "index": 1, "name": "Simulation_DB", "objectType": "GlobalDB" }
                    ],
                    "requiredFormat": "SimaticML"
                  }{{trailingProperty}}
                }
                """;
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private string CreateProject(string fileName)
        {
            var path = Path.Combine(Root, fileName);
            File.WriteAllText(path, fileName, new UTF8Encoding(false));
            return path;
        }

        private const string FakeWorkerScript = """
            #Requires -Version 7
            Set-StrictMode -Version Latest
            $ErrorActionPreference = 'Stop'
            $workerInstanceId = [Guid]::NewGuid().ToString('N')
            while ($null -ne ($line = [Console]::In.ReadLine())) {
                $request = $line | ConvertFrom-Json -Depth 100
                $request | Add-Member -NotePropertyName fakeWorkerInstanceId -NotePropertyValue $workerInstanceId
                Add-Content -LiteralPath $env:VCI_MUTATION_TEST_WORKER_LOG -Value ($request | ConvertTo-Json -Compress -Depth 100) -Encoding utf8
                if (@($request.PSObject.Properties.Name) -contains 'requestId') {
                    $response = [ordered]@{
                        success = $false
                        payload = $null
                        error = "root contains unknown field 'requestId'."
                    }
                    [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 100))
                    [Console]::Out.Flush()
                    continue
                }
                $projectName = [IO.Path]::GetFileNameWithoutExtension([string]$request.projectPath)
                $caseId = [string]$request.vciMutationProbe.caseId
                $caseInstanceId = [string]$request.vciMutationProbe.caseInstanceId
                $isInventory = $caseId -eq 'P-INVENTORY'
                $behavior = [string]$env:VCI_MUTATION_TEST_BEHAVIOR
                if (-not $isInventory -and $caseInstanceId.StartsWith('lifecycle-group:', [StringComparison]::Ordinal)) {
                    if ($behavior -eq 'timeout') { Start-Sleep -Seconds 30 }
                    if ($behavior -eq 'process_lost') { exit 73 }
                    if ($behavior -eq 'malformed') {
                        [Console]::Out.WriteLine('{not-json')
                        [Console]::Out.Flush()
                        continue
                    }
                }
                $engineeringObject = [ordered]@{
                    stableIdentifier = ('stable-' + $projectName)
                    structuralPath = @(
                        [ordered]@{ index = 0; name = 'ET 200SP station_1'; objectType = 'Device' },
                        [ordered]@{ index = 0; name = 'PLC_1'; objectType = 'PlcSoftware' },
                        [ordered]@{ index = 0; name = 'Program blocks'; objectType = 'BlockFolder' },
                        [ordered]@{ index = 1; name = 'Simulation_DB'; objectType = 'GlobalDB' }
                    )
                    fingerprint = ('fingerprint-' + $projectName)
                }
                $workspace = [ordered]@{
                    groupPath = @()
                    workspaceName = 'existing'
                    canonicalRootPath = ('C:\existing\' + $projectName)
                }
                $mappingSelector = [ordered]@{
                    workspace = $workspace
                    engineeringObject = $engineeringObject
                    relativeDirectory = 'mapping\export'
                    fileName = 'Simulation_DB'
                    format = 'SimaticML'
                }
                $snapshot = [ordered]@{
                    schemaVersion = 'vci-read-probe/v1'
                    service = [ordered]@{ runtimeTypeName = 'VersionControlInterface'; rootGroupCount = 0; rootWorkspaceCount = 2 }
                    groups = @()
                    workspaces = @()
                    mappings = @([ordered]@{
                        enumerationIndex = 0
                        canonicalKey = ('mapping-' + $projectName)
                        selector = $mappingSelector
                        status = 'Equal'
                        statusProperty = 'Equal'
                        getStatus = 'Equal'
                        childStatus = 'Equal'
                    })
                    members = @()
                    omissions = @()
                }
                $outcome = 'returned'
                $exception = $null
                if ($behavior -eq 'not_observable' -and $caseInstanceId.StartsWith('lifecycle-group:', [StringComparison]::Ordinal)) {
                    $outcome = 'not_observable'
                }
                if (-not $isInventory -and $behavior -eq 'semantic_variant' -and $caseId -eq 'M-CANARY') {
                    $outcome = 'threw'
                    $exception = [ordered]@{
                        exceptionTypeName = 'System.InvalidOperationException'
                        message = 'semantic variant'
                        hResult = -1
                        innerException = $null
                    }
                }
                $isInjectedStep = -not $isInventory -and $caseInstanceId.StartsWith('lifecycle-group:', [StringComparison]::Ordinal)
                $incomplete = $isInjectedStep -and $behavior -eq 'incomplete'
                $uncertain = $isInjectedStep -and $behavior -eq 'uncertain'
                if ($isInjectedStep -and $behavior -eq 'project_file_changed') {
                    [IO.File]::AppendAllText([string]$request.projectPath, 'changed-by-fake-worker')
                }
                $isTransaction = $caseId.StartsWith('M-TX-', [StringComparison]::Ordinal)
                $result = [ordered]@{
                    schemaVersion = 'vci-mutation-probe/v1'
                    runId = [string]$request.vciMutationProbe.runId
                    sessionId = [string]$request.vciMutationProbe.sessionId
                    scenarioId = [string]$request.vciMutationProbe.scenarioId
                    caseId = $caseId
                    caseInstanceId = [string]$request.vciMutationProbe.caseInstanceId
                    invocationLayer = 'worker'
                    inputCategory = $(if ($isInventory) { 'inventory' } elseif ($caseId.StartsWith('N-', [StringComparison]::Ordinal)) { 'negative' } else { 'positive' })
                    sanitizedArguments = $(if ($isInventory) { @() } else { @(
                        [ordered]@{ name = 'workspaceRoot'; category = 'canonical_absolute_path'; value = [string]$request.vciMutationProbe.workspaceRoot },
                        [ordered]@{ name = 'projectPath'; category = 'disposable_project'; value = [string]$request.projectPath }
                    ) })
                    preconditions = $(if ($isInventory) { @(
                            [ordered]@{ name = 'selected_engineering_object_is_Simulation_DB'; satisfied = $true; detail = $null },
                            [ordered]@{ name = 'exact_SimaticML_supported'; satisfied = $true; detail = $null }
                        ) } else { @([ordered]@{ name = 'fixture_ready'; satisfied = $true; detail = $null }) })
                    safetyInvariants = $(if ($isInventory) {
                            @([ordered]@{ name = 'workspace_root_absent_after_inventory'; satisfied = $true; detail = $null })
                        } else {
                            @([ordered]@{ name = 'filesystem_confined'; satisfied = $true; detail = $null })
                        })
                    outcome = $outcome
                    return = [ordered]@{
                        clrTypeName = $(if ($isInventory) { 'InventoryWorkspaceSelection' } else { 'MutationObservation' })
                        isNull = $false
                        stringValue = $(if ($isInventory) { $null } else { $caseId })
                        members = $(if ($isInventory) { @(
                            [ordered]@{ name = 'workspace.name'; clrTypeName = 'System.String'; stringValue = 'existing'; isNull = $false; exception = $null },
                            [ordered]@{ name = 'workspace.canonicalRootPath'; clrTypeName = 'System.String'; stringValue = ('C:\existing\' + $projectName); isNull = $false; exception = $null },
                            [ordered]@{ name = 'workspace.groupPath'; clrTypeName = 'System.String'; stringValue = ''; isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.runtimeType'; clrTypeName = 'System.String'; stringValue = 'Siemens.Engineering.SW.Blocks.GlobalDB'; isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.stableIdentifier'; clrTypeName = 'System.String'; stringValue = ('stable-' + $projectName); isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.fingerprint'; clrTypeName = 'System.String'; stringValue = ('fingerprint-' + $projectName); isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.structuralPath'; clrTypeName = 'System.String'; stringValue = '0:Device:ET 200SP station_1/0:PlcSoftware:PLC_1/0:BlockFolder:Program blocks/1:GlobalDB:Simulation_DB'; isNull = $false; exception = $null },
                            [ordered]@{ name = 'fileFormat[0]'; clrTypeName = 'System.String'; stringValue = 'SimaticML'; isNull = $false; exception = $null }
                        ) } else { @([ordered]@{ name = 'status'; clrTypeName = 'System.String'; stringValue = 'Equal'; isNull = $false; exception = $null }) })
                    }
                    exception = $exception
                    before = $snapshot
                    after = $(if ($incomplete) { $null } else { $snapshot })
                    projectState = [ordered]@{ isModifiedBefore = $false; isModifiedAfter = (-not $isInventory) }
                    transaction = [ordered]@{
                        requested = $isTransaction
                        started = $isTransaction
                        commitRequested = $false
                        canCommitBeforeDispose = $false
                        disposed = $isTransaction
                    }
                    canary = [ordered]@{
                        attempted = (-not $isInventory)
                        usable = (-not $isInventory)
                        outcome = $(if ($isInventory) { '' } else { 'returned' })
                    }
                    uncertainOutcome = $uncertain
                    stopScenarioFamily = $uncertain
                    notObservableReason = $(if ($outcome -eq 'not_observable') { 'required_fixture_state_not_available' } else { $null })
                    omissions = @()
                }
                $response = [ordered]@{
                    success = $true
                    payload = ($result | ConvertTo-Json -Compress -Depth 100)
                }
                [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 100))
                [Console]::Out.Flush()
            }
            """;
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);

    public sealed record PlannedStep(
        string Family,
        string StepId,
        string CaseId,
        string ProjectRole,
        string InvocationLayer);
}
