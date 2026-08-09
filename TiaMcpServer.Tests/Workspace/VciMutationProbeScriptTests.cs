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
            fixture.Run("Inventory", workerExecutable: Path.Combine(fixture.Root, "missing-worker.exe")),
            "worker_not_found");
        AssertFailureContains(
            fixture.Run("Inventory", extraArguments: new[] { "-WorkerAccessMode", "read-only" }),
            "worker_must_be_read_write");

        Assert.False(File.Exists(fixture.WorkerLogPath));
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
        private static readonly string[] DisposableRoles =
        {
            "lifecycleProjectPath", "mappingProjectPath", "projectToWorkspaceChangedProjectPath",
            "workspaceToProjectBaselineProjectPath", "negativeProjectPath", "transactionProjectPath",
        };

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
        public string InventoryPath => Path.Combine(EvidenceRoot, "inventory.json");
        public string PlanPath => Path.Combine(EvidenceRoot, "plan.json");
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
            string[]? extraArguments = null)
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
            Environment.SetEnvironmentVariable("VCI_MUTATION_TEST_WORKER_LOG", WorkerLogPath);
            try
            {
                return RunPowerShell(arguments.ToArray());
            }
            finally
            {
                Environment.SetEnvironmentVariable("VCI_MUTATION_TEST_WORKER_LOG", previousLog);
            }
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
            while ($null -ne ($line = [Console]::In.ReadLine())) {
                $request = $line | ConvertFrom-Json -Depth 100
                Add-Content -LiteralPath $env:VCI_MUTATION_TEST_WORKER_LOG -Value ($request | ConvertTo-Json -Compress -Depth 100) -Encoding utf8
                $projectName = [IO.Path]::GetFileNameWithoutExtension([string]$request.projectPath)
                $result = [ordered]@{
                    schemaVersion = 'vci-mutation-probe/v1'
                    runId = [string]$request.vciMutationProbe.runId
                    sessionId = [string]$request.vciMutationProbe.sessionId
                    scenarioId = [string]$request.vciMutationProbe.scenarioId
                    caseId = 'P-INVENTORY'
                    caseInstanceId = [string]$request.vciMutationProbe.caseInstanceId
                    invocationLayer = 'worker'
                    inputCategory = 'positive'
                    sanitizedArguments = @()
                    preconditions = @(
                        [ordered]@{ name = 'selected_engineering_object_is_Simulation_DB'; satisfied = $true; detail = $null },
                        [ordered]@{ name = 'exact_SimaticML_supported'; satisfied = $true; detail = $null }
                    )
                    safetyInvariants = @([ordered]@{ name = 'workspace_root_absent_after_inventory'; satisfied = $true; detail = $null })
                    outcome = 'returned'
                    return = [ordered]@{
                        clrTypeName = 'InventoryWorkspaceSelection'; isNull = $false; stringValue = $null
                        members = @(
                            [ordered]@{ name = 'workspace.name'; clrTypeName = 'System.String'; stringValue = 'existing'; isNull = $false; exception = $null },
                            [ordered]@{ name = 'workspace.canonicalRootPath'; clrTypeName = 'System.String'; stringValue = ('C:\existing\' + $projectName); isNull = $false; exception = $null },
                            [ordered]@{ name = 'workspace.groupPath'; clrTypeName = 'System.String'; stringValue = ''; isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.runtimeType'; clrTypeName = 'System.String'; stringValue = 'Siemens.Engineering.SW.Blocks.GlobalDB'; isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.stableIdentifier'; clrTypeName = 'System.String'; stringValue = ('stable-' + $projectName); isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.fingerprint'; clrTypeName = 'System.String'; stringValue = ('fingerprint-' + $projectName); isNull = $false; exception = $null },
                            [ordered]@{ name = 'engineeringObject.structuralPath'; clrTypeName = 'System.String'; stringValue = '0:Device:ET 200SP station_1/0:PlcSoftware:PLC_1/0:BlockFolder:Program blocks/1:GlobalDB:Simulation_DB'; isNull = $false; exception = $null },
                            [ordered]@{ name = 'fileFormat[0]'; clrTypeName = 'System.String'; stringValue = 'SimaticML'; isNull = $false; exception = $null }
                        )
                    }
                    exception = $null
                    before = [ordered]@{ schemaVersion = 'vci-read-probe/v1'; members = @(); omissions = @() }
                    after = [ordered]@{ schemaVersion = 'vci-read-probe/v1'; members = @(); omissions = @() }
                    projectState = [ordered]@{ isModifiedBefore = $false; isModifiedAfter = $false }
                    transaction = [ordered]@{ requested = $false; started = $false; commitRequested = $false; canCommitBeforeDispose = $false; disposed = $false }
                    canary = [ordered]@{ attempted = $false; usable = $false; outcome = '' }
                    uncertainOutcome = $false
                    stopScenarioFamily = $false
                    notObservableReason = $null
                    omissions = @()
                }
                $response = [ordered]@{
                    requestId = [string]$request.requestId
                    success = $true
                    payload = ($result | ConvertTo-Json -Compress -Depth 100)
                }
                [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 100))
                [Console]::Out.Flush()
            }
            """;
    }

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
