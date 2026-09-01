# PR 1 Explicit MCP Tool Annotations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove and ship explicit MCP annotation hints for the registered batch-write and lifecycle-write tools by starting with production-equivalent `tools/list` tests, then adding the minimal production annotations, and finally completing the mandatory live TIA Portal V21 acceptance/reporting gate.

**Architecture:** PR 1 does not change the write-safety model, public tool names, input schemas, or wrapper delegation. It first extends `McpProtocolTestHarness` so tests can start the exact `Program.cs` tool surface in both access modes, then uses `ListToolsAsync()` as the milestone RED/GREEN path for the real client-visible metadata, and only after that adds explicit `McpServerTool` hint values on `WriteBatchTools` and `ProjectWriteTools`. Reflection tests remain supplemental pinning on the registered classes, while the live PowerShell harness launches `TiaMcpServer` twice, captures `tools/list` in read-only and read-write modes, performs one benign project read per run, and writes the durable acceptance report.

**Tech Stack:** C#; .NET 8 host; .NET Framework 4.8 worker; ModelContextProtocol 1.2.0; xUnit; PowerShell 7; the existing FakeWorker-backed MCP protocol harness; serial Windows `dotnet` verification commands.

**Spec:** [`docs/superpowers/specs/2026-09-01-write-safety-hardening-design.md`](../specs/2026-09-01-write-safety-hardening-design.md)

## Global Constraints

- Improve the reviewability, MCP metadata, test fidelity, and state-snapshot precision of the existing write-safety system without weakening its server-enforced guarantees or changing the public write-tool names and input schemas.
- Every pull request in this design must pass both repository verification and scope-specific live TIA Portal V21 acceptance. Offline, stub, and FakeWorker evidence is necessary but never sufficient for completion.
- The public tools remain `preview_write_batch`, `apply_write_batch`, the six lifecycle write tools, and `network_write`. No active milestone renames, combines, or removes a tool.
- Preview remains non-mutating. Apply still requires the unchanged request, `confirm=true`, and the matching single-use safety token.
- Tokens continue to bind tool name, normalized project path, ordered targets, exact requested input, exact current state, and the complete verified project/session binding.
- Add protocol-level tests that call `ListToolsAsync`, not reflection-only tests.
- Assert exact annotations, names, and availability in read-only and read-write server modes.
- Preserve existing input schemas and tool counts.
- Run a live V21 host, complete MCP initialization, call `tools/list`, and record the emitted annotations. The same run must make one benign project read so the report proves that the host is connected to a real TIA session rather than only testing an in-memory SDK server.
- No project mutation is required for this milestone.
- A failed or unavailable live-TIA gate leaves the pull request incomplete. It is not converted into an offline-only acceptance claim.

---

## File Map

- `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`
  Add one reusable production-equivalent startup path for protocol tests:
  `Task<McpProtocolTestHarness> StartAsync(McpAccessMode accessMode, Action<IMcpServerBuilder> registerTools, string? auditDirectory = null, string? startupProjectPath = null)`
  and
  `Task<McpProtocolTestHarness> StartProductionSurfaceAsync(McpAccessMode accessMode, string? auditDirectory = null, string? startupProjectPath = null)`.
  `StartProductionSurfaceAsync` must mirror `Program.cs` exactly:
  read-only registers `ProjectReadTools`, `ReadBatchTools`, `NetworkReadTools`;
  read-write additionally registers `ProjectEngineeringTools`, `ProjectWriteTools`, `WriteBatchTools`, and `NetworkWriteTools`.

- `TiaMcpServer.Tests/Tools/WriteToolMcpAnnotationProtocolTests.cs`
  New protocol-first milestone tests that call `await harness.Client.ListToolsAsync()` against the production-equivalent surface and assert the exact 4-tool read-only surface, exact 14-tool read-write surface, conservative write annotations, and unchanged representative schemas.

- `TiaMcpServer/Batch/WriteBatchTools.cs`
  Add explicit `McpServerTool` hint values to the registered `PreviewWriteBatch` and `ApplyWriteBatch` methods only.

- `TiaMcpServer/Tools/ProjectWriteTools.cs`
  Add explicit `McpServerTool` hint values to the six registered lifecycle write methods only: `OpenProject`, `CreateProject`, `SaveProject`, `SaveProjectAs`, `ArchiveProject`, and `CloseProject`.

- `TiaMcpServer.Tests/Batch/BatchToolsTests.cs`
  Supplemental reflection regression on the registered batch-write class after the protocol milestone is green. This is not the milestone RED path.

- `TiaMcpServer.Tests/Project/ProjectLifecycleToolTests.cs`
  Supplemental reflection regression on the registered lifecycle-write class after the protocol milestone is green. This is not the milestone RED path.

- `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`
  Existing schema authority that must continue to prove the public schema stays unchanged and DI-only parameters do not leak into model-facing tool inputs.

- `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs`
  Existing exact approved-tool-count guard that must remain green. Reuse only if a tiny helper improves consistency with the new protocol tests.

- `TiaMcpServer.Tests/Project/ProjectStandaloneToolTests.cs`
  Existing `compile_check` metadata regression that remains part of the protocol/surface verification bundle because read-write mode must still expose the full 14-tool production surface.

- `scripts/live-test-write-tool-metadata.ps1`
  New live TIA Portal V21 acceptance harness. It launches `TiaMcpServer` in both `--read-only` and `--read-write` modes, performs `initialize`, `notifications/initialized`, `tools/list`, and one benign `get_project_status` call per mode, then writes the acceptance report.

- `TiaMcpServer.Tests/Tools/WriteToolMetadataLiveHarnessContractTests.cs`
  Static contract tests for the live harness source: PowerShell 7 requirement, exact MCP protocol shape, both access-mode launches, report location, no direct worker IPC, and no confirming writes.

- `docs/ARCHITECTURE.md`
  Document that explicit MCP hints are client-facing metadata layered on top of the unchanged server-enforced preview/apply safety flow.

- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`
  Document that lifecycle write tools now advertise conservative mutating hints while remaining self-previewing.

- `docs/IMPROVEMENT_LOG.md`
  Record PR 1 completion, the live acceptance report path, and the still-deferred PLC start/stop investigation.

- `docs/README.md`
  Add the PR 1 live acceptance report to the docs index.

- `docs/superpowers/README.md`
  Add both this plan and the PR 1 live acceptance report to the superpowers index if not already listed.

- `docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`
  Durable acceptance report with exact tool names/counts and emitted annotations for both access modes, the benign read evidence, the tested TIA version/project copy, and the explicit live-evidence boundary.

### Task 1: Production-Equivalent Protocol Coverage and Registered Annotation Hints

**Files:**
- Modify: `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`
- Create: `TiaMcpServer.Tests/Tools/WriteToolMcpAnnotationProtocolTests.cs`
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs`
- Modify: `TiaMcpServer/Tools/ProjectWriteTools.cs`
- Modify: `TiaMcpServer.Tests/Batch/BatchToolsTests.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectLifecycleToolTests.cs`
- Modify: `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs` only if a tiny helper reduces duplication without changing coverage intent
- Modify: `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs` only if a tiny helper reduces duplication without changing coverage intent

**Interfaces:**
- Produces: `public static Task<McpProtocolTestHarness> StartAsync(McpAccessMode accessMode, Action<IMcpServerBuilder> registerTools, string? auditDirectory = null, string? startupProjectPath = null)` in `McpProtocolTestHarness`
- Produces: `public static Task<McpProtocolTestHarness> StartProductionSurfaceAsync(McpAccessMode accessMode, string? auditDirectory = null, string? startupProjectPath = null)` in `McpProtocolTestHarness`
- Consumes: exact production-equivalent read-only tool set: `ProjectReadTools`, `ReadBatchTools`, `NetworkReadTools`
- Consumes: exact production-equivalent read-write tool set: `ProjectReadTools`, `ReadBatchTools`, `NetworkReadTools`, `ProjectEngineeringTools`, `ProjectWriteTools`, `WriteBatchTools`, `NetworkWriteTools`
- Produces: protocol assertions for the exact read-only tool names:
  `browse_project_tree`, `execute_read_batch`, `get_project_status`, `network_read`
- Produces: protocol assertions for the exact read-write tool names:
  `apply_write_batch`, `archive_project`, `browse_project_tree`, `close_project`, `compile_check`, `create_project`, `execute_read_batch`, `get_project_status`, `network_read`, `network_write`, `open_project`, `preview_write_batch`, `save_project`, `save_project_as`
- Produces: explicit registered annotations for `preview_write_batch` -> `ReadOnly=true`, `Destructive=false`, `OpenWorld=false`
- Produces: explicit registered annotations for `apply_write_batch` -> `ReadOnly=false`, `Destructive=true`, `OpenWorld=false`
- Produces: explicit registered annotations for each lifecycle write tool -> `ReadOnly=false`, `Destructive=true`, `OpenWorld=false`

- [ ] **Step 1: Add the reusable production-equivalent protocol harness setup**

  Extend `McpProtocolTestHarness` before writing the milestone tests so the test surface matches `Program.cs` exactly:

  ```csharp
  public static Task<McpProtocolTestHarness> StartAsync(
      McpAccessMode accessMode,
      Action<IMcpServerBuilder> registerTools,
      string? auditDirectory = null,
      string? startupProjectPath = null)

  public static Task<McpProtocolTestHarness> StartProductionSurfaceAsync(
      McpAccessMode accessMode,
      string? auditDirectory = null,
      string? startupProjectPath = null)
      => StartAsync(
          accessMode,
          builder =>
          {
              builder.WithTools<ProjectReadTools>()
                     .WithTools<ReadBatchTools>()
                     .WithTools<NetworkReadTools>();

              if (accessMode == McpAccessMode.ReadWrite)
              {
                  builder.WithTools<ProjectEngineeringTools>()
                         .WithTools<ProjectWriteTools>()
                         .WithTools<WriteBatchTools>()
                         .WithTools<NetworkWriteTools>();
              }
          },
          auditDirectory,
          startupProjectPath);
  ```

  Keep the existing generic overloads as thin delegates over `StartAsync(McpAccessMode.ReadWrite, ...)` so existing tests do not change behavior.

- [ ] **Step 2: Write the failing protocol-first milestone tests**

  Create `WriteToolMcpAnnotationProtocolTests.cs` with the real client-visible RED path:

  ```csharp
  private static readonly string[] ReadOnlyToolNames =
  {
      "browse_project_tree",
      "execute_read_batch",
      "get_project_status",
      "network_read",
  };

  private static readonly string[] ReadWriteToolNames =
  {
      "apply_write_batch",
      "archive_project",
      "browse_project_tree",
      "close_project",
      "compile_check",
      "create_project",
      "execute_read_batch",
      "get_project_status",
      "network_read",
      "network_write",
      "open_project",
      "preview_write_batch",
      "save_project",
      "save_project_as",
  };

  [Fact]
  public async Task ToolsList_ReadWriteProductionSurface_ExposesExactNamesCountsAnnotations_AndRepresentativeSchemas()
  {
      await using var harness = await McpProtocolTestHarness.StartProductionSurfaceAsync(McpAccessMode.ReadWrite);
      var tools = (await harness.Client.ListToolsAsync()).OrderBy(tool => tool.Name).ToArray();
      var byName = tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

      Assert.Equal(ReadWriteToolNames, tools.Select(tool => tool.Name));
      Assert.Equal(14, tools.Length);

      Assert.True(byName["preview_write_batch"].Annotations!.ReadOnlyHint);
      Assert.False(byName["preview_write_batch"].Annotations.DestructiveHint);
      Assert.False(byName["preview_write_batch"].Annotations.OpenWorldHint);

      Assert.False(byName["apply_write_batch"].Annotations!.ReadOnlyHint);
      Assert.True(byName["apply_write_batch"].Annotations.DestructiveHint);
      Assert.False(byName["apply_write_batch"].Annotations.OpenWorldHint);

      Assert.False(byName["open_project"].Annotations!.ReadOnlyHint);
      Assert.True(byName["open_project"].Annotations.DestructiveHint);
      Assert.False(byName["open_project"].Annotations.OpenWorldHint);

      Assert.Contains("\"operations\"", byName["preview_write_batch"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
      Assert.Contains("\"projectPath\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
      Assert.Contains("\"confirm\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
      Assert.Contains("\"safetyToken\"", byName["open_project"].ProtocolTool.InputSchema.GetRawText(), StringComparison.Ordinal);
  }
  ```

  Add a second fact that starts `StartProductionSurfaceAsync(McpAccessMode.ReadOnly)`, asserts the exact `ReadOnlyToolNames` array and count `4`, and proves every write tool plus `compile_check` is absent.

- [ ] **Step 3: Run the milestone RED and confirm the failure is protocol metadata, not reflection**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WriteToolMcpAnnotationProtocolTests"
  ```

  Expected RED: `tools/list` returns the exact production-equivalent tool surface, but the write-tool annotation hints fail because `WriteBatchTools` and `ProjectWriteTools` have not yet declared the explicit mutability values required by the spec.

- [ ] **Step 4: Add the smallest production annotation change across both registered write surfaces**

  Update only the `McpServerTool` attributes on the registered write methods:

  ```csharp
  [McpServerTool(
      Name = "preview_write_batch",
      ReadOnly = true,
      Destructive = false,
      OpenWorld = false)]
  public static Task<string> PreviewWriteBatch(...)

  [McpServerTool(
      Name = "apply_write_batch",
      ReadOnly = false,
      Destructive = true,
      OpenWorld = false)]
  public static Task<string> ApplyWriteBatch(...)
  ```

  and

  ```csharp
  [McpServerTool(
      Name = "open_project",
      ReadOnly = false,
      Destructive = true,
      OpenWorld = false)]
  public static Task<string> OpenProject(...)
  ```

  Apply the same `ReadOnly=false`, `Destructive=true`, `OpenWorld=false` values to `CreateProject`, `SaveProject`, `SaveProjectAs`, `ArchiveProject`, and `CloseProject`.

  Do not change descriptions, parameters, wrapper behavior, token flow, access-mode policy, or worker calls.

- [ ] **Step 5: Re-run the focused protocol bundle and confirm GREEN**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WriteToolMcpAnnotationProtocolTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~ProjectStandaloneToolTests|FullyQualifiedName~NetworkToolsTests"
  ```

  Confirm all of the following in one bundle:

  - `ListToolsAsync()` now emits the exact 14-tool read-write surface and 4-tool read-only surface;
  - `compile_check` remains present only in read-write mode;
  - the explicit write-tool hints are correct;
  - representative protocol-visible schemas stay unchanged; and
  - the existing schema tests still prove `workerClient` and `safety` never leak into model-facing inputs.

- [ ] **Step 6: Add supplemental registered-class reflection regressions**

  After the protocol milestone is green, add direct pinning tests to `BatchToolsTests` and `ProjectLifecycleToolTests`:

  ```csharp
  [Theory]
  [InlineData(nameof(WriteBatchTools.PreviewWriteBatch), "preview_write_batch", true, false, false)]
  [InlineData(nameof(WriteBatchTools.ApplyWriteBatch), "apply_write_batch", false, true, false)]
  public void WriteBatchTools_RegisteredMethodsExposeExplicitMcpAnnotations(...)
  ```

  and

  ```csharp
  [Theory]
  [InlineData(nameof(ProjectWriteTools.OpenProject), "open_project")]
  [InlineData(nameof(ProjectWriteTools.CreateProject), "create_project")]
  [InlineData(nameof(ProjectWriteTools.SaveProject), "save_project")]
  [InlineData(nameof(ProjectWriteTools.SaveProjectAs), "save_project_as")]
  [InlineData(nameof(ProjectWriteTools.ArchiveProject), "archive_project")]
  [InlineData(nameof(ProjectWriteTools.CloseProject), "close_project")]
  public void ProjectWriteTools_RegisteredMethodsExposeExplicitMutatingAnnotations(...)
  ```

  These are regression pins only. They are not the milestone RED evidence.

- [ ] **Step 7: Run the supplemental metadata and safety regressions**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~BatchToolsTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~LifecycleSecondIdentityValidationContractTests|FullyQualifiedName~BatchToolMetadataTests"
  ```

  Confirm only explicit annotation hints changed. Preview/apply behavior, token guidance, safety failures, and wrapper compatibility stay intact.

- [ ] **Step 8: Stop at the commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs TiaMcpServer.Tests/Tools/WriteToolMcpAnnotationProtocolTests.cs TiaMcpServer/Batch/WriteBatchTools.cs TiaMcpServer/Tools/ProjectWriteTools.cs TiaMcpServer.Tests/Batch/BatchToolsTests.cs TiaMcpServer.Tests/Project/ProjectLifecycleToolTests.cs TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs
  git commit -m "feat(write-safety): annotate registered MCP write tools"
  ```

### Task 2: Live V21 Harness, Acceptance Report, and Current Documentation

**Files:**
- Create: `scripts/live-test-write-tool-metadata.ps1`
- Create: `TiaMcpServer.Tests/Tools/WriteToolMetadataLiveHarnessContractTests.cs`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`
- Create: `docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`

**Interfaces:**
- Consumes: real `TiaMcpServer` stdio JSON-RPC startup in both `--read-only` and `--read-write` modes
- Consumes: live calls `initialize`, `notifications/initialized`, `tools/list`, and `tools/call` to `get_project_status`
- Produces: a read-only PowerShell 7 harness that records the exact 4-tool and 14-tool surfaces and the emitted write-tool hints from a live TIA Portal V21 session
- Produces: a durable live report at `docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`

- [ ] **Step 1: Write the failing live-harness contract tests**

  Create `WriteToolMetadataLiveHarnessContractTests.cs` using the same static source-inspection style as `NetworkLiveHarnessContractTests`:

  ```csharp
  [Fact]
  public void Script_LaunchesTheRealMcpHostTwice_AndSpeaksInitializeListCallProtocol()
  {
      var text = ReadScript();

      Assert.Matches(new Regex(@"--read-only"), text);
      Assert.Matches(new Regex(@"--read-write"), text);
      Assert.Contains("'initialize'", text, StringComparison.Ordinal);
      Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
      Assert.Contains("tools/list", text, StringComparison.Ordinal);
      Assert.Contains("tools/call", text, StringComparison.Ordinal);
      Assert.Contains("TiaMcpServer", text, StringComparison.Ordinal);
      Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
  }
  ```

  Add companion facts that prove:

  - `#Requires -Version 7` is present;
  - `-ProjectPath` and `-ReportPath` are mandatory;
  - the script never issues `confirm = $true`;
  - the only `tools/call` target is `get_project_status`;
  - the script records both exact expected tool-name arrays and counts `4` and `14`;
  - the report path is under `docs/superpowers/acceptance/reports`;
  - no ordinary test invokes the script.

- [ ] **Step 2: Run the live-harness contract RED**

  Run:

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~WriteToolMetadataLiveHarnessContractTests"
  ```

  Expected RED: the script file does not exist yet, so the existence and protocol-shape assertions fail.

- [ ] **Step 3: Implement the read-only live acceptance harness**

  Build `scripts/live-test-write-tool-metadata.ps1` as a non-mutating harness:

  ```powershell
  #Requires -Version 7
  [CmdletBinding()]
  param(
      [Parameter(Mandatory)]
      [string]$ProjectPath,

      [Parameter(Mandatory)]
      [string]$ReportPath
  )
  ```

  The script must:

  - launch `TiaMcpServer`, not the worker executable;
  - run one session with `--read-only` and one with `--read-write`;
  - perform `initialize`, `notifications/initialized`, `tools/list`, and one benign `get_project_status` call in each session;
  - capture the exact read-only and read-write tool names/counts and the emitted hints for `preview_write_batch`, `apply_write_batch`, `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, and `close_project`;
  - write the markdown report to `docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`;
  - state explicitly that the run is non-mutating and proves only the tested live host/project/session combination.

- [ ] **Step 4: Re-run the contract tests and review the harness source**

  Run the Step 2 command again.

  Then confirm by source review that the script:

  - never reaches an apply path;
  - never speaks direct worker IPC;
  - never hides the report destination;
  - never claims offline or FakeWorker evidence completes PR 1.

- [ ] **Step 5: Update the current documentation authorities**

  Make the smallest doc changes that keep the repository current:

  - `docs/ARCHITECTURE.md`: explain that explicit MCP hints are untrusted client-facing metadata layered on top of the unchanged server-enforced preview/apply model.
  - `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`: document the conservative mutating hints on lifecycle write tools and reiterate that the first call still previews.
  - `docs/IMPROVEMENT_LOG.md`: record PR 1 completion, the live report path, and that PLC `start_plc` / `stop_plc` remains deferred.
  - `docs/README.md`: add the acceptance report entry.
  - `docs/superpowers/README.md`: ensure both this plan and the acceptance report are indexed.

  Do not broaden scope into wrapper delegation, tag snapshot work, block/type diff evidence, or PLC control changes.

- [ ] **Step 6: Run full offline verification**

  Run serially:

  ```powershell
  dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
  git diff --check
  git status --short
  ```

  Record exact build/test totals. Review the final diff for accidental tool-surface drift, schema drift, write behavior hidden in the live harness, or missing documentation index entries.

- [ ] **Step 7: Run the mandatory live V21 acceptance harness**

  Run only with separate explicit authorization and an approved live `.ap21` project path:

  ```powershell
  pwsh -NoProfile -File scripts/live-test-write-tool-metadata.ps1 -ProjectPath "C:\Path\To\Disposable.ap21" -ReportPath "docs\superpowers\acceptance\reports\2026-09-01-pr1-explicit-mcp-tool-annotations-live.md"
  ```

  The saved report must include:

  - exact TIA Portal V21 version;
  - tested project copy path;
  - exact 4-tool read-only surface and 14-tool read-write surface;
  - emitted annotation hints for all PR 1 write tools;
  - exact benign read call and result summary for each mode;
  - confirmation that no mutation was performed; and
  - the evidence boundary separating live MCP proof from offline/stub/FakeWorker proof.

- [ ] **Step 8: Stop at the final commit boundary**

  Do not commit without explicit authorization. When authorized, use:

  ```powershell
  git add scripts/live-test-write-tool-metadata.ps1 TiaMcpServer.Tests/Tools/WriteToolMetadataLiveHarnessContractTests.cs docs/ARCHITECTURE.md docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md docs/IMPROVEMENT_LOG.md docs/README.md docs/superpowers/README.md docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md
  git commit -m "docs(write-safety): record explicit MCP tool annotations"
  ```

## Deferred and Out of Scope

- Wrapper delegation belongs to PR 2. `BatchTools` and `ProjectLifecycleTools` remain compiled compatibility seams in PR 1.
- `network_write` remains unchanged and serves only as the regression reference for conservative mutating hints.
- No public tool names, tool counts, parameter names, output schemas, safety-token semantics, audit behavior, or access-mode policies change in PR 1.
- PLC `start_plc` / `stop_plc` remains explicitly deferred. This plan must not quarantine, remove, or redesign those tools.
- Offline, stub, and FakeWorker evidence does not complete PR 1. The pull request remains incomplete until the live TIA Portal V21 harness has run and the acceptance report exists.
- No project mutation is authorized or required for PR 1 live acceptance.

## Completion Gate

PR 1 is complete only when all of the following are true:

- [ ] The protocol milestone RED was observed through `ListToolsAsync()` on the production-equivalent surface before any production annotation change.
- [ ] `WriteBatchTools` emits explicit registered hints matching the approved matrix.
- [ ] `ProjectWriteTools` emits explicit registered hints matching the approved matrix.
- [ ] The real MCP protocol `tools/list` surface proves the exact 4-tool read-only surface and 14-tool read-write surface.
- [ ] `compile_check` remains present only in read-write mode.
- [ ] Existing schema tests still prove no injected service parameters leaked into public tool inputs.
- [ ] Supplemental reflection tests pin the registered-class hint values after the protocol milestone is green.
- [ ] Full serial stub build and test verification passed with exact totals recorded.
- [ ] `git diff --check` passed and `git status --short` shows only intended PR 1 files.
- [ ] `scripts/live-test-write-tool-metadata.ps1` exists, passes its static contract tests, and launches the real host over MCP in both access modes.
- [ ] `docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md` exists and records the live TIA V21 surfaces, hints, benign reads, and evidence boundary.
- [ ] The final docs still state that PLC start/stop is deferred and that metadata hints do not replace server-enforced write safety.
- [ ] Any commit happened only after explicit user authorization.
