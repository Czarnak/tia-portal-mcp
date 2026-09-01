# PR 2 Registered Tool Delegation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `WriteBatchTools` and `ProjectWriteTools` the direct behavioral authority for write safety, then reduce `BatchTools` and `ProjectLifecycleTools` to compatibility wrappers that delegate to the registered read/write classes without changing any public tool name, schema, or safety semantics.

**Architecture:** Keep the registered MCP tool types as the only place that owns write-tool behavior. Migrate existing wrapper-oriented FakeWorker and protocol coverage onto the registered classes first, then replace the duplicated wrapper bodies with thin forwarding calls into `ReadBatchTools`/`WriteBatchTools` and `ProjectReadTools`/`ProjectWriteTools`. Acceptance is incomplete until a separately authorized live V21 MCP harness proves that the real host advertises the registered write surface and can perform one non-mutating generic-batch preview plus one non-mutating self-previewing lifecycle call.

**Tech Stack:** C# 12, .NET 8 host/tests/FakeWorker, .NET Standard 2.0 contracts, .NET Framework 4.8 Openness worker, xUnit, ModelContextProtocol client/server SDK, PowerShell 7, newline-delimited JSON IPC.

**Spec:** [Write-safety preview and registered-surface hardening design](../specs/2026-09-01-write-safety-hardening-design.md)

## Global Constraints

- Preserve the public MCP write-tool names and input schemas exactly: `preview_write_batch`, `apply_write_batch`, `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, and `close_project`.
- `WriteBatchTools` and `ProjectWriteTools` are the behavioral authority after this PR. Delegation flows from `BatchTools` to `ReadBatchTools`/`WriteBatchTools` and from `ProjectLifecycleTools` to `ProjectReadTools`/`ProjectWriteTools`, never the other way around.
- Do not weaken read-only denial, verified-binding requirements, envelope validation before expensive state reads, pinned binding lease behavior, single-use token behavior, token lifetime, current-state hashing, or audit append behavior.
- Keep Siemens Openness calls in the net48 worker only. This PR changes host/wrapper ownership and tests, not the worker architecture.
- Wrapper deletion is deferred. `BatchTools` and `ProjectLifecycleTools` must remain compiled compatibility seams after this PR.
- `start_plc` and `stop_plc` remain explicitly deferred. Do not change their contracts, state binding, or implementation in this PR, and do not use them as evidence that PLC control is now supported.
- Offline, stub, FakeWorker, and protocol tests are required but never sufficient. The PR is not complete until the live V21 harness runs and a dated acceptance report is written.
- Use behavioral TDD where a production change is required: add the focused regression first, observe RED, implement the smallest production change, rerun to GREEN, then widen verification. Characterization tests that already pass on the registered classes are acceptable only before delegation; they do not satisfy acceptance on their own.
- Run Windows .NET verification serially with `--no-restore -m:1 --disable-build-servers`. Use the stub build for repository verification: `/p:UseTiaPortalReferenceStubs=true`.
- Do not rename tools, delete wrappers, change README landing-page promises, or broaden scope into PR 3+ snapshot work.

---

## File / Interface Map

- `TiaMcpServer/Batch/WriteBatchTools.cs`
  Registered generic write MCP tool type. Owns `PreviewWriteBatch(OpennessWorkerClient, WriteSafetyService, BatchOperationRequest[])` and `ApplyWriteBatch(OpennessWorkerClient, WriteSafetyService, BatchOperationRequest[], bool, string?)`.

- `TiaMcpServer/Batch/ReadBatchTools.cs`
  Registered generic read MCP tool type. Owns `ExecuteReadBatch(OpennessWorkerClient, BatchOperationRequest[])`.

- `TiaMcpServer/Batch/BatchTools.cs`
  Compatibility wrapper. After this PR it should contain only the existing descriptions plus thin delegating methods to the registered batch classes.

- `TiaMcpServer/Tools/ProjectWriteTools.cs`
  Registered lifecycle write MCP tool type. Owns `OpenProject`, `CreateProject`, `SaveProject`, `SaveProjectAs`, `ArchiveProject`, and `CloseProject`.

- `TiaMcpServer/Tools/ProjectReadTools.cs`
  Registered lifecycle read MCP tool type. Owns `GetProjectStatus(OpennessWorkerClient, string?)` and `BrowseProjectTree(...)`.

- `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
  Compatibility wrapper. After this PR it should contain only the existing descriptions plus thin delegating methods to `ProjectReadTools` and `ProjectWriteTools`.

- `TiaMcpServer.Tests/Batch/TypeOperationFakeWorkerTests.cs`
  Existing registered-behavior candidate. Today it proves `update_type_content` preview/apply/replay through `BatchTools`; this PR should move that evidence onto `WriteBatchTools`.

- `TiaMcpServer.Tests/Batch/BlockCurrentStateReadTests.cs`
  Existing current-state format regression. Today it proves source-format block reads through `BatchTools`; this PR should move the end-to-end half onto `WriteBatchTools`.

- `TiaMcpServer.Tests/Batch/BatchToolsTests.cs`
  Keep as wrapper-compatibility coverage only. After delegation it should prove metadata and deterministic wrapper forwarding behavior, not remain the primary behavioral authority for registered writes.

- `TiaMcpServer.FakeWorker/Program.cs`
  Extend only as needed for PR 2 test support: add a dedicated generic-batch registered-write scenario that preserves current-state reads, returns one successful write, then one malformed worker response so the registered batch tests can prove request order, skip-on-failure semantics, and fail-closed protocol propagation.

- `TiaMcpServer.Tests/Project/ProjectLifecycleToolTests.cs`
  Keep as wrapper-compatibility coverage only. After delegation it should prove metadata and deterministic forwarding behavior, not remain the primary behavioral authority for registered lifecycle writes.

- `TiaMcpServer.Tests/Safety/WriteToolSafetyTokenTests.cs`
  Existing lifecycle token and surface-count authority. Retarget its direct lifecycle preview/replay/state-change cases from `ProjectLifecycleTools` to `ProjectWriteTools`, and retarget `ProjectReadAndLifecycleSurfaceIsExactlyEightTools` to the registered `ProjectReadTools` + `ProjectWriteTools` pair.

- `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`
  Already contains lifecycle FakeWorker scenarios such as `CollapsedOpenProject_PreviewThenApply_RoundTrips`, `CollapsedOpenProject_PreviewThenApply_WorkerFailureRendersFailureCategoryNeverSuccessShaped`, `SaveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus`, `SaveProjectAs_PreviewAndApply_UseLifecycleProbeNotDirectStatus`, `ArchiveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus`, and `CloseProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus`. Move the tool-call authority in those tests from `ProjectLifecycleTools` to `ProjectWriteTools`.

- `TiaMcpServer.Tests/Safety/ReadOnlyModeTests.cs`
  Already has light registered coverage for `ProjectWriteTools`. Extend it only for targeted read-only/binding guards if a new dedicated test file would be thinner than widening the existing mega-fixture.

- `TiaMcpServer.Tests/Safety/WriteSafetyLeaseConcurrencyTests.cs`
  Existing direct `ProjectWriteTools.SaveProject(...)` lease evidence. Treat it as authoritative registered coverage and keep it green.

- `TiaMcpServer.Tests/Safety/AuditIsolationTests.cs`
  Existing lifecycle audit-isolation coverage. Move its direct behavior assertions from `ProjectLifecycleTools` to `ProjectWriteTools`, and add one registered generic-batch audit-isolation assertion so wrapper parity is no longer the only audit evidence.

- `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`
  In-process real MCP client/server harness over anonymous pipes. Use it for `tools/list` and `tools/call` assertions against the registered tool types.

- `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`
  Existing whole-surface schema enumerator. Extend it so registered `WriteBatchTools` and `ProjectWriteTools` are asserted directly for exact public tool names, production surface counts, and DI-hidden service parameters, while the wrapper schema checks remain only compatibility coverage.

- `scripts/live-test-write-safety-pr2-registered-tools.ps1`
  New separately authorized live V21 MCP harness. Must start the real host, verify the registered surface via `tools/list`, perform a non-mutating lifecycle preview, and perform a non-mutating generic-batch preview against a verified startup binding.

- `TiaMcpServer.Tests/Tools/RegisteredWriteToolsLiveHarnessContractTests.cs`
  New static contract tests for the PR 2 live script. Must read the script text only; never execute it.

- `docs/ARCHITECTURE.md`
  Update the host tool-registration and compatibility-wrapper discussion so the current architecture states that the registered classes are the behavioral authority and the wrappers are compatibility shims.

- `docs/IMPROVEMENT_LOG.md`
  Record the completion of PR 2 and restate the deferred items that still remain out of scope.

- `docs/README.md`
  Add the new acceptance report to the docs index.

- `docs/superpowers/README.md`
  Add the new acceptance report entry in the superpowers historical index.

- `docs/superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md`
  Durable acceptance artifact for the separately authorized live run. Must record exact project copy, exact tool calls, and the explicit non-mutating evidence boundary.

## Deferred And Out Of Scope

- Wrapper deletion stays deferred. This PR ends with delegating wrappers still present and compiled.
- `start_plc` / `stop_plc` state-binding and implementation hardening stay deferred exactly as described in the spec.
- No new mutable-state snapshot fields, preview diff payloads, tag-scope narrowing, or project-tree narrowing belong in this PR.
- No offline-only sign-off. A passing stub build, FakeWorker suite, or MCP protocol suite does not complete PR 2.
- No user-facing tool renames, no schema edits, no token-format changes, no audit-format changes, and no landing-page README rewrite.

### Task 1: Move Generic Batch Behavioral Authority To `WriteBatchTools`

**Files:**

- Create: `TiaMcpServer.Tests/Batch/WriteBatchToolsBehaviorTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/TypeOperationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/BlockCurrentStateReadTests.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Review only: `TiaMcpServer/Batch/WriteBatchTools.cs`

**Interfaces:**

- Consumes: `WriteBatchTools.PreviewWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations) -> Task<string>`
- Consumes: `WriteBatchTools.ApplyWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations, bool confirm = false, string? safetyToken = null) -> Task<string>`
- Reuses unchanged: `FakeWorkerBinding.BindVerifiedAsync(OpennessWorkerClient client, ProjectSessionBinding binding, string projectPath) -> Task`
- Reuses unchanged: `TempAuditDirectory.Path -> string`
- Preserves: `BatchOperationRequest` public shape and every current `WriteBatchTools` description string

- [ ] **Step 1: Add direct read-only and binding-gate characterization tests for the registered batch class**

Create `WriteBatchToolsBehaviorTests.cs` with helpers copied from existing FakeWorker tests:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Batch;

public sealed class WriteBatchToolsBehaviorTests
{
    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static OpennessWorkerClient CreateClient(
        ProjectSessionBinding binding,
        McpAccessMode mode = McpAccessMode.ReadWrite)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(mode));

    private static BatchOperationRequest CreateUserConstantOp(
        string operationId,
        string projectPath = "type-content-roundtrip",
        string name = "Gain") => new()
    {
        OperationId = operationId,
        Operation = "create_user_constant",
        ProjectPath = projectPath,
        TableName = "Constants",
        Name = name,
        DataType = "Int",
        Value = "1",
    };

    [Fact]
    public async Task PreviewWriteBatch_ReadOnlyMode_IsRejectedBeforeTokenIssuance()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, McpAccessMode.ReadOnly);

        var result = await WriteBatchTools.PreviewWriteBatch(
            client,
            CreateSafety(audit, binding),
            new[] { CreateUserConstantOp("op-1") });

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("read-only mode", doc.RootElement.GetProperty("error").GetString());
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task PreviewWriteBatch_UnverifiedBinding_IsRejectedBeforeCurrentStateRead()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);

        var result = await WriteBatchTools.PreviewWriteBatch(
            client,
            CreateSafety(audit, binding),
            new[] { CreateUserConstantOp("op-1") });

        using var doc = JsonDocument.Parse(result);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.BindingConflict,
            doc.RootElement.GetProperty("failureCategory").GetString());
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));
    }
}
```

These are characterization tests: they should pass immediately and capture the current registered behavior before any wrapper work begins.

- [ ] **Step 2: Move the existing type-content preview/apply/replay test onto the registered class**

In `TypeOperationFakeWorkerTests.cs`, replace the three wrapper calls with direct registered calls and rename the methods so the file advertises the correct authority:

```csharp
var result = await WriteBatchTools.PreviewWriteBatch(
    client,
    safety,
    new[] { UpdateTypeContentOp("w1") });

var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
var firstApply = await WriteBatchTools.ApplyWriteBatch(
    client,
    safety,
    operations,
    confirm: true,
    safetyToken: token);
var secondApply = await WriteBatchTools.ApplyWriteBatch(
    client,
    safety,
    operations,
    confirm: true,
    safetyToken: token);
```

Rename:

- `PreviewWriteBatch_UpdateTypeContent_ReturnsTokenAndDescriptivePreview`
- `ApplyWriteBatch_UpdateTypeContent_SucceedsOnceThenRejectsReplayedToken`

Do not leave `BatchTools` as the only tool name in the test text or comments.

- [ ] **Step 3: Move the block source-format current-state regression onto the registered class**

In `BlockCurrentStateReadTests.cs`, keep the invoker-only assertions as they are, but switch the end-to-end write-safety test to `WriteBatchTools`:

```csharp
var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
using var previewDoc = JsonDocument.Parse(preview);
var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

var apply = await WriteBatchTools.ApplyWriteBatch(
    client,
    safety,
    operations,
    confirm: true,
    safetyToken: token);
```

Rename the test to `PreviewAndApplyWriteBatch_UpdateBlockLogicWithSourceFormat_ReadsCurrentStateAsSource_ThroughRegisteredTool`.

- [ ] **Step 4: Run the direct registered batch slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~WriteBatchToolsBehaviorTests|FullyQualifiedName~TypeOperationFakeWorkerTests|FullyQualifiedName~BlockCurrentStateReadTests"
```

Expected: GREEN. If any characterization test is RED here, stop and fix the registered class before touching the wrappers.

- [ ] **Step 5: Add a real MCP protocol test for the registered batch surface**

Create `TiaMcpServer.Tests/Batch/WriteBatchToolsProtocolTests.cs`:

```csharp
using ModelContextProtocol.Protocol;

namespace TiaMcpServer.Tests.Batch;

public sealed class WriteBatchToolsProtocolTests
{
    [Fact]
    public async Task WriteBatchTools_AdvertisePreviewAndApplyOverToolsList()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<WriteBatchTools>();

        var names = (await harness.Client.ListToolsAsync())
            .Select(tool => tool.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "apply_write_batch", "preview_write_batch" }, names);
    }

    [Fact]
    public async Task PreviewWriteBatch_ProtocolCall_RejectsReadOperationsThroughTheRegisteredSurface()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<WriteBatchTools>();

        var result = await harness.Client.CallToolAsync(
            "preview_write_batch",
            new Dictionary<string, object?>
            {
                ["operations"] = new object[]
                {
                    new
                    {
                        operationId = "bad-read",
                        operation = "get_block_content",
                        blockPath = "PLC_1/Blocks/Main"
                    }
                }
            });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("\"success\":false", text, StringComparison.Ordinal);
        Assert.Contains("get_block_content", text, StringComparison.Ordinal);
    }
}
```

The protocol test must go through `tools/list` and `tools/call`; direct method invocation is not enough.

- [ ] **Step 6: Run the protocol slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~WriteBatchToolsProtocolTests"
```

Expected: GREEN.

- [ ] **Step 7: Add direct registered coverage for ordered results, audit isolation, and protocol failure propagation**

Extend `WriteBatchToolsBehaviorTests.cs` with an exact audit helper and one multi-item registered apply regression:

```csharp
private static int CountAuditLines(string directory)
    => Directory.Exists(directory)
        ? Directory.GetFiles(directory).Sum(file => File.ReadAllLines(file).Length)
        : 0;

[Fact]
public async Task ApplyWriteBatch_RegisteredPath_PreservesRequestOrder_StopsOnProtocolFailure_SkipsLaterItems_AndWritesOnlyInjectedAudit()
{
    var defaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TiaMcpServer",
        "audit");
    var defaultBefore = CountAuditLines(defaultDirectory);

    using var audit = new TempAuditDirectory();
    var binding = new ProjectSessionBinding(null);
    using var client = CreateClient(binding);
    var safety = CreateSafety(audit, binding);
    const string scenario = "type-content-ordered-protocol-failure";
    await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

    var operations = new[]
    {
        UpdateTypeContentOp("first", scenario),
        UpdateTypeContentOp("second", scenario),
        UpdateTypeContentOp("third", scenario),
    };

    var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
    using var previewDoc = JsonDocument.Parse(preview);
    var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

    var applied = await WriteBatchTools.ApplyWriteBatch(
        client,
        safety,
        operations,
        confirm: true,
        safetyToken: token);

    using var appliedDoc = JsonDocument.Parse(applied);
    var items = appliedDoc.RootElement.GetProperty("operations");

    Assert.Equal(new[] { "first", "second", "third" }, items.EnumerateArray().Select(i => i.GetProperty("operationId").GetString()).ToArray());
    Assert.Equal("succeeded", items[0].GetProperty("status").GetString());
    Assert.Equal("failed", items[1].GetProperty("status").GetString());
    Assert.Equal(WorkerFailureCategories.ProtocolError, items[1].GetProperty("failureCategory").GetString());
    Assert.DoesNotContain("unexpectedShape", items[1].GetProperty("error").GetString(), StringComparison.Ordinal);
    Assert.Equal("skipped", items[2].GetProperty("status").GetString());

    Assert.True(Directory.Exists(audit.Path));
    Assert.NotEmpty(Directory.GetFiles(audit.Path));
    Assert.Equal(defaultBefore, CountAuditLines(defaultDirectory));
}
```

This test makes the registered class, not `BatchTools`, prove:

- request-order preservation in the returned operation list,
- stop-on-first-failure semantics,
- skip marking for later items,
- `protocol_error` propagation without echoing raw malformed payloads, and
- audit writes isolated to the injected directory.

- [ ] **Step 8: Run the new registered batch regression and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~WriteBatchToolsBehaviorTests.ApplyWriteBatch_RegisteredPath_PreservesRequestOrder_StopsOnProtocolFailure_SkipsLaterItems_AndWritesOnlyInjectedAudit"
```

Expected RED: the dedicated FakeWorker scenario does not exist yet, so the preview/apply path fails against `unknown scenario 'type-content-ordered-protocol-failure'`.

- [ ] **Step 9: Add the dedicated FakeWorker scenario for registered batch ordering and protocol failure**

In `TiaMcpServer.FakeWorker/Program.cs`, add one process-local counter near `updateBlockPostconditionAttempt`:

```csharp
var orderedTypeWriteCount = 0;
```

Then add this scenario next to `type-content-roundtrip`:

```csharp
case "type-content-ordered-protocol-failure":
    Respond(ReadMethod(line) switch
    {
        "get_project_status" => """{"success":true,"payload":"{\"isOpen\":true}"}""",
        "get_type_content" => """{"success":true,"payload":"TYPE AnalogInputSettings STRUCT Value : Real; END_STRUCT END_TYPE"}""",
        "update_type_content" => ++orderedTypeWriteCount switch
        {
            1 => """{"success":true,"payload":"{}"}""",
            2 => "this is not json",
            _ => """{"success":false,"error":"third write should have been skipped"}"""
        },
        _ => $$"""{"success":false,"error":"expected get_project_status, get_type_content, or update_type_content, got '{{ReadMethod(line)}}'"}"""
    });
    break;
```

The third write must never be reached. Returning an explicit failure if it is reached prevents the test from silently passing with broken skip semantics.

- [ ] **Step 10: Run the complete registered batch slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~WriteBatchToolsBehaviorTests|FullyQualifiedName~WriteBatchToolsProtocolTests|FullyQualifiedName~TypeOperationFakeWorkerTests|FullyQualifiedName~BlockCurrentStateReadTests"
```

Expected GREEN.

- [ ] **Step 11: Review checkpoint**

Confirm:

- `WriteBatchTools` now carries direct FakeWorker and protocol evidence for read-only denial, binding gate, preview issuance, apply/replay, request-ordered write results, audit isolation, and fail-closed protocol propagation.
- `BatchTools` is no longer the sole route exercised by type-content or block-write end-to-end tests.
- No production MCP tool behavior changed in this task; only registered-class tests and FakeWorker test support changed.

### Task 2: Move Lifecycle Behavioral Authority To `ProjectWriteTools`

**Files:**

- Create: `TiaMcpServer.Tests/Project/ProjectWriteToolsBehaviorTests.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectWriteToolsProtocolTests.cs`
- Modify: `TiaMcpServer.Tests/Worker/OpennessWorkerClientIntegrationTests.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectLifecycleToolTests.cs`
- Modify: `TiaMcpServer.Tests/Safety/AuditIsolationTests.cs`
- Modify: `TiaMcpServer.Tests/Safety/WriteToolSafetyTokenTests.cs`
- Modify: `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`
- Review only: `TiaMcpServer/Tools/ProjectWriteTools.cs`

**Interfaces:**

- Consumes: `ProjectWriteTools.OpenProject(...) -> Task<string>`
- Consumes: `ProjectWriteTools.CreateProject(...) -> Task<string>`
- Consumes: `ProjectWriteTools.SaveProject(...) -> Task<string>`
- Consumes: `ProjectWriteTools.SaveProjectAs(...) -> Task<string>`
- Consumes: `ProjectWriteTools.ArchiveProject(...) -> Task<string>`
- Consumes: `ProjectWriteTools.CloseProject(...) -> Task<string>`
- Reuses unchanged: `FakeWorkerBinding.BindVerifiedAsync(OpennessWorkerClient client, ProjectSessionBinding binding, string projectPath) -> Task`
- Preserves: `ProjectLifecycleTools` public signatures until the later delegation task

- [ ] **Step 1: Move the deterministic lifecycle safety-token authority onto `ProjectWriteTools`**

First retarget the existing lifecycle safety/token file so it no longer proves registered behavior through the compatibility wrapper:

- keep `SeparatePreviewToolsAreGone`
- change `ProjectReadAndLifecycleSurfaceIsExactlyEightTools` to enumerate `typeof(ProjectReadTools)` + `typeof(ProjectWriteTools)` instead of `ProjectLifecycleTools`
- change `WriteToolWithoutToken_ReturnsPreviewWithTokenAndInstructions` to call `ProjectWriteTools.OpenProject(...)`
- change `WriteToolWithTokenButNoConfirm_RejectsBeforeAnyWork` to call `ProjectWriteTools.CloseProject(...)`
- change `WriteToolWithBadToken_PointsBackAtTheTokenlessCall`, `WriteToolWithChangedProjectPath_RendersBindingConflictEnvelope`, `WriteToolWithChangedInput_RendersValidationErrorEnvelope`, `WriteToolWithUsedToken_RendersValidationErrorEnvelope`, and `WriteToolWithChangedCurrentState_RendersStateChangedEnvelope` to call `ProjectWriteTools.OpenProject(...)`

That should look like:

```csharp
var result = await ProjectWriteTools.OpenProject(
    workerClient: null!,
    safety,
    projectPath: @"C:\Projects\Line.ap21");
```

and:

```csharp
var result = await ProjectWriteTools.CloseProject(
    workerClient: null!,
    safety,
    confirm: false,
    safetyToken: "some-token");
```

Then create `ProjectWriteToolsBehaviorTests.cs` and move the light wrapper-oriented `ProjectLifecycleToolTests` checks onto the registered class:

```csharp
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectWriteToolsBehaviorTests
{
    [Fact]
    public async Task OpenProject_WithTokenButNoConfirm_ReturnsRegisteredConfirmEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await ProjectWriteTools.OpenProject(
            workerClient: null!,
            safety,
            projectPath: @"C:\Projects\Line.ap21",
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }

    [Fact]
    public async Task SaveProjectAs_WithTokenButNoConfirm_ReturnsRegisteredConfirmEnvelope()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var result = await ProjectWriteTools.SaveProjectAs(
            workerClient: null!,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: null,
            rebind: true,
            confirm: false,
            safetyToken: "fake-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }

    [Fact]
    public async Task SaveProjectAs_RebindFalse_RejectsBeforePreviewTokenGeneration_OnRegisteredTool()
    {
        using var audit = new TempAuditDirectory();
        var safety = audit.CreateSafety();

        var response = await ProjectWriteTools.SaveProjectAs(
            workerClient: null!,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: null,
            rebind: false);

        using var doc = JsonDocument.Parse(response);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            doc.RootElement.GetProperty("failureCategory").GetString());
        Assert.False(doc.RootElement.TryGetProperty("safetyToken", out _));
    }

    [Fact]
    public async Task SaveProjectAs_Apply_MissingCopiedPath_PropagatesPostconditionFailedAndWarning()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = new WriteSafetyService(
            binding,
            () => DateTimeOffset.UtcNow,
            WriteSafetyService.DefaultTokenLifetime,
            audit.Path);
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(
            client,
            binding,
            "save-as-uncertain-state");

        var preview = await ProjectWriteTools.SaveProjectAs(
            client,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: "save-as-uncertain-state",
            rebind: true);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectWriteTools.SaveProjectAs(
            client,
            safety,
            targetDirectory: @"C:\Target",
            targetName: "Copy",
            projectPath: "save-as-uncertain-state",
            rebind: true,
            confirm: true,
            safetyToken: token);
        using var appliedDoc = JsonDocument.Parse(applied);

        Assert.False(appliedDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(
            WorkerFailureCategories.PostconditionFailed,
            appliedDoc.RootElement.GetProperty("failureCategory").GetString());
        Assert.Contains(
            "Project state may have changed",
            appliedDoc.RootElement.GetProperty("warnings")[0].GetString(),
            StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Move the FakeWorker lifecycle roundtrip and worker-failure authority onto `ProjectWriteTools`**

In `OpennessWorkerClientIntegrationTests.cs`, switch these exact tests from `ProjectLifecycleTools` to `ProjectWriteTools` without changing their scenario setup or assertions:

- `CollapsedOpenProject_PreviewThenApply_RoundTrips`
- `CollapsedOpenProject_PreviewThenApply_WorkerFailureRendersFailureCategoryNeverSuccessShaped`
- `SaveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus`
- `PostWriteVerification_UsesBasicStatusRead_NotExtendedMetadataRead`
- `SaveProjectAs_PreviewAndApply_UseLifecycleProbeNotDirectStatus`
- `ArchiveProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus`
- `ArchiveProject_PreviewRejectsArchiveDirectoryInsideProjectFolder_WithoutIssuingSafetyToken`
- `CloseProject_PreviewAndApply_UseLifecycleProbeNotDirectStatus`

The archive preview rejection case is mandatory registered authority in this PR: it must continue to prove that `ProjectWriteTools.ArchiveProject(...)` rejects an archive directory inside the project folder before issuing any `safetyToken` and before any audit append occurs.

The only code change inside those methods should be the called tool class:

```csharp
var preview = await ProjectWriteTools.SaveProject(client, safety, projectPath: projectPath);
var applied = await ProjectWriteTools.SaveProject(
    client,
    safety,
    projectPath: projectPath,
    confirm: true,
    safetyToken: token);
```

and:

```csharp
var preview = await ProjectWriteTools.SaveProjectAs(
    client,
    safety,
    targetDirectory: @"C:\Target",
    targetName: "Copy",
    projectPath: projectPath,
    rebind: true);
```

- [ ] **Step 3: Migrate lifecycle audit-isolation tests to the registered class**

In `AuditIsolationTests.cs`, replace every direct `ProjectLifecycleTools` call with the equivalent `ProjectWriteTools` call and rename the first test to `ProjectWriteTool_WritesAuditOnlyToTheInjectedDirectory`.

Keep the exact counting strategy:

```csharp
private static int CountAuditLines(string directory)
    => Directory.Exists(directory)
        ? Directory.GetFiles(directory).Sum(file => File.ReadAllLines(file).Length)
        : 0;
```

After the migration, `AuditIsolationTests` must directly prove all three registered lifecycle behaviors:

- successful `ProjectWriteTools.OpenProject(...)` apply appends only to `audit.Path`,
- safety-rejected `ProjectWriteTools.OpenProject(...)` apply appends nowhere, and
- rejected `ProjectWriteTools.SaveProjectAs(..., rebind: false)` appends nowhere and never leaks to the default directory.

- [ ] **Step 4: Extend `McpToolSchemaTests` so the registered write types are asserted directly**

Keep the existing wrapper theories as compatibility coverage, but add direct registered checks:

- `ProjectWriteTools_SchemaNeverExposesInjectedServiceParameters` with `[InlineData(nameof(ProjectWriteTools.OpenProject))]`, `[InlineData(nameof(ProjectWriteTools.CreateProject))]`, `[InlineData(nameof(ProjectWriteTools.SaveProject))]`, `[InlineData(nameof(ProjectWriteTools.SaveProjectAs))]`, `[InlineData(nameof(ProjectWriteTools.ArchiveProject))]`, and `[InlineData(nameof(ProjectWriteTools.CloseProject))]`
- `WriteBatchTools_SchemaNeverExposesInjectedServiceParameters` with `[InlineData(nameof(WriteBatchTools.PreviewWriteBatch))]` and `[InlineData(nameof(WriteBatchTools.ApplyWriteBatch))]`
- `RegisteredWriteToolSurface_ExposesExactlyEightApprovedTools` over `typeof(ProjectWriteTools)` + `typeof(WriteBatchTools)` with the exact set `apply_write_batch`, `archive_project`, `close_project`, `create_project`, `open_project`, `preview_write_batch`, `save_project`, and `save_project_as`
- `OpenProject_SchemaStillExposesProjectPathAsAModelArgument_OnRegisteredTool` proving the registered type still exposes `projectPath`, `confirm`, and `safetyToken`

Leave `McpToolSurface_ExposesExactlyFourteenApprovedTools()` in place as the full production-surface census, but stop relying on wrapper-only schema theories as the registered authority.

- [ ] **Step 5: Add a real MCP protocol test for the registered lifecycle surface**

Create `ProjectWriteToolsProtocolTests.cs`:

```csharp
using TiaMcpServer.Batch;
using TiaMcpServer.Tools;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectWriteToolsProtocolTests
{
    [Fact]
    public async Task RegisteredWriteTools_ToolsList_AdvertisesExactlyEightWriteTools()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools, WriteBatchTools>();

        var names = (await harness.Client.ListToolsAsync())
            .Select(tool => tool.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "apply_write_batch",
                "archive_project",
                "close_project",
                "create_project",
                "open_project",
                "preview_write_batch",
                "save_project",
                "save_project_as"
            },
            names);
    }

    [Fact]
    public async Task OpenProject_ProtocolPreview_ReturnsSafetyTokenThroughRegisteredTool()
    {
        await using var harness = await McpProtocolTestHarness.StartAsync<ProjectWriteTools>();

        var result = await harness.Client.CallToolAsync(
            "open_project",
            new Dictionary<string, object?>
            {
                ["projectPath"] = @"C:\open\Line.ap21"
            });

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("safetyToken", text, StringComparison.Ordinal);
        Assert.Contains("open_project", text, StringComparison.Ordinal);
        Assert.Contains("Preview only", text, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 6: Run the registered lifecycle slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ProjectWriteToolsBehaviorTests|FullyQualifiedName~ProjectWriteToolsProtocolTests|FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~AuditIsolationTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests.CollapsedOpenProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.SaveProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.SaveProjectAs_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.ArchiveProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.ArchiveProject_PreviewRejectsArchiveDirectoryInsideProjectFolder_WithoutIssuingSafetyToken|FullyQualifiedName~OpennessWorkerClientIntegrationTests.CloseProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.PostWriteVerification_UsesBasicStatusRead_NotExtendedMetadataRead|FullyQualifiedName~WriteSafetyLeaseConcurrencyTests"
```

Expected: GREEN. If a moved test goes RED, fix `ProjectWriteTools` before delegating the wrapper.

- [ ] **Step 7: Re-scope `ProjectLifecycleToolTests.cs` to wrapper-compatibility only**

Keep the metadata theory in place, but rewrite the two direct behavior tests so they describe wrapper compatibility rather than registered authority:

- keep `ProjectLifecycleToolsHaveMcpMetadata`
- move `SaveProjectAsWithTokenButNoConfirm_Rejects` and `SaveProjectAs_RebindFalse_RejectsBeforePreviewTokenGeneration` to `ProjectWriteToolsBehaviorTests`
- replace them later in Task 4 with explicit delegation tests

This step should leave `ProjectLifecycleToolTests.cs` temporarily smaller, not larger.

- [ ] **Step 8: Review checkpoint**

Confirm:

- `WriteToolSafetyTokenTests` now proves lifecycle preview, replay, binding-conflict, changed-input, and state-changed behavior through `ProjectWriteTools`, not `ProjectLifecycleTools`.
- FakeWorker lifecycle preview/apply evidence now executes `ProjectWriteTools`, not `ProjectLifecycleTools`.
- Direct registered lifecycle authority includes the archive-directory guard rejecting before issuing a token, before worker mutation, and before any audit append.
- `AuditIsolationTests` now use `ProjectWriteTools` directly, so injected-audit-path coverage no longer depends on the wrapper.
- `McpToolSchemaTests` now asserts `ProjectWriteTools` and `WriteBatchTools` directly for exact names, counts, and DI-hidden parameters.
- Direct registered lifecycle coverage includes fail-closed `postcondition_failed` propagation with the worker warning preserved.
- The protocol surface is asserted through `tools/list` and `tools/call`.
- `WriteSafetyLeaseConcurrencyTests` still provides direct `ProjectWriteTools.SaveProject(...)` lease evidence.

### Task 3: Replace `BatchTools` Logic With Thin Delegation To Registered Batch Classes

**Files:**

- Create: `TiaMcpServer.Tests/Batch/BatchToolsDelegationTests.cs`
- Modify: `TiaMcpServer/Batch/BatchTools.cs`
- Modify: `TiaMcpServer.Tests/Batch/BatchToolsTests.cs`

**Interfaces:**

- Produces: `BatchTools.ExecuteReadBatch(OpennessWorkerClient workerClient, BatchOperationRequest[] operations) -> Task<string>` delegating to `ReadBatchTools.ExecuteReadBatch(...)`
- Produces: `BatchTools.PreviewWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations) -> Task<string>` delegating to `WriteBatchTools.PreviewWriteBatch(...)`
- Produces: `BatchTools.ApplyWriteBatch(OpennessWorkerClient workerClient, WriteSafetyService safety, BatchOperationRequest[] operations, bool confirm = false, string? safetyToken = null) -> Task<string>` delegating to `WriteBatchTools.ApplyWriteBatch(...)`
- Removes from wrapper: `ReadCombinedCurrentStateAsync(...)`
- Preserves: wrapper method signatures, descriptions, and compatibility comments

- [ ] **Step 1: Add a failing wrapper-delegation regression that exposes the current drift**

Create `BatchToolsDelegationTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Batch;

public sealed class BatchToolsDelegationTests
{
    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static OpennessWorkerClient CreateReadOnlyClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadOnly));

    [Fact]
    public async Task PreviewWriteBatch_WrapperMatchesRegisteredReadOnlyRejection()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = CreateReadOnlyClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "op-1",
                Operation = "create_user_constant",
                TableName = "Constants",
                Name = "Gain",
                DataType = "Int",
                Value = "1",
                ProjectPath = "type-content-roundtrip"
            }
        };

        var registered = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var wrapper = await BatchTools.PreviewWriteBatch(client, safety, operations);

        using var registeredDoc = JsonDocument.Parse(registered);
        using var wrapperDoc = JsonDocument.Parse(wrapper);

        Assert.Equal(
            registeredDoc.RootElement.GetProperty("failureCategory").GetString(),
            wrapperDoc.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal(
            registeredDoc.RootElement.GetProperty("error").GetString(),
            wrapperDoc.RootElement.GetProperty("error").GetString());
        Assert.False(wrapperDoc.RootElement.TryGetProperty("safetyToken", out _));
    }
}
```

Expected RED: `WriteBatchTools` returns the explicit read-only rejection, while `BatchTools` still runs its own stale path and does not match it.

- [ ] **Step 2: Run the failing wrapper batch regression**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~BatchToolsDelegationTests.PreviewWriteBatch_WrapperMatchesRegisteredReadOnlyRejection"
```

Expected RED: assertion mismatch on `error` and possibly `failureCategory`.

- [ ] **Step 3: Replace the wrapper bodies with direct forwarding calls**

In `BatchTools.cs`, keep the class summary and descriptions, but replace the method bodies with thin forwarding only:

```csharp
public static Task<string> ExecuteReadBatch(
    OpennessWorkerClient workerClient,
    BatchOperationRequest[] operations)
    => ReadBatchTools.ExecuteReadBatch(workerClient, operations);

public static Task<string> PreviewWriteBatch(
    OpennessWorkerClient workerClient,
    WriteSafetyService safety,
    BatchOperationRequest[] operations)
    => WriteBatchTools.PreviewWriteBatch(workerClient, safety, operations);

public static Task<string> ApplyWriteBatch(
    OpennessWorkerClient workerClient,
    WriteSafetyService safety,
    BatchOperationRequest[] operations,
    bool confirm = false,
    string? safetyToken = null)
    => WriteBatchTools.ApplyWriteBatch(
        workerClient,
        safety,
        operations,
        confirm,
        safetyToken);
```

Delete the now-unused `ReadCombinedCurrentStateAsync` helper from `BatchTools.cs`.

- [ ] **Step 4: Re-scope `BatchToolsTests.cs` to wrapper compatibility**

Keep the metadata theory and deterministic invalid-input checks, but ensure the file no longer claims wrapper authority. Add one more exact forwarding assertion beside the new read-only regression:

```csharp
[Fact]
public async Task ApplyWriteBatch_WrapperMatchesRegisteredBadTokenEnvelope()
{
    using var audit = new TempAuditDirectory();
    var safety = audit.CreateSafety();
    var operations = new[]
    {
        new BatchOperationRequest
        {
            OperationId = "op-1",
            Operation = "create_user_constant",
            TableName = "Constants",
            Name = "Gain",
            DataType = "Int",
            Value = "1",
            ProjectPath = "type-content-roundtrip"
        }
    };

    var registered = await WriteBatchTools.ApplyWriteBatch(
        workerClient: null!,
        safety,
        operations,
        confirm: true,
        safetyToken: "bogus-token");
    var wrapper = await BatchTools.ApplyWriteBatch(
        workerClient: null!,
        safety,
        operations,
        confirm: true,
        safetyToken: "bogus-token");

    Assert.Equal(registered, wrapper);
}
```

- [ ] **Step 5: Run the batch delegation slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~BatchToolsDelegationTests|FullyQualifiedName~BatchToolsTests|FullyQualifiedName~WriteBatchToolsBehaviorTests|FullyQualifiedName~TypeOperationFakeWorkerTests|FullyQualifiedName~BlockCurrentStateReadTests"
```

Expected GREEN.

- [ ] **Step 6: Review checkpoint**

Confirm:

- `BatchTools.cs` is now a thin compatibility layer with no duplicated write-safety logic.
- The registered batch classes still own every behavioral test that matters.
- The wrapper still exists and still preserves the public static signatures.

### Task 4: Replace `ProjectLifecycleTools` Logic With Thin Delegation To Registered Project Classes

**Files:**

- Create: `TiaMcpServer.Tests/Project/ProjectLifecycleDelegationTests.cs`
- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectLifecycleToolTests.cs`

**Interfaces:**

- Produces: `ProjectLifecycleTools.GetProjectStatus(OpennessWorkerClient workerClient, string? projectPath = null) -> Task<string>` delegating to `ProjectReadTools.GetProjectStatus(...)`
- Produces: `ProjectLifecycleTools.OpenProject(...) -> Task<string>` delegating to `ProjectWriteTools.OpenProject(...)`
- Produces: `ProjectLifecycleTools.CreateProject(...) -> Task<string>` delegating to `ProjectWriteTools.CreateProject(...)`
- Produces: `ProjectLifecycleTools.SaveProject(...) -> Task<string>` delegating to `ProjectWriteTools.SaveProject(...)`
- Produces: `ProjectLifecycleTools.SaveProjectAs(...) -> Task<string>` delegating to `ProjectWriteTools.SaveProjectAs(...)`
- Produces: `ProjectLifecycleTools.ArchiveProject(...) -> Task<string>` delegating to `ProjectWriteTools.ArchiveProject(...)`
- Produces: `ProjectLifecycleTools.CloseProject(...) -> Task<string>` delegating to `ProjectWriteTools.CloseProject(...)`
- Removes from wrapper: `RejectIfArchiveDirectoryWithinProjectFolder`, `ApplyInstructions`, `ConfirmRequired`, `SafetyFailure`, `CreatePinnedPreviewAsync`, `PreviewHint`

- [ ] **Step 1: Add a failing lifecycle wrapper-delegation regression**

Create `ProjectLifecycleDelegationTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tools;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectLifecycleDelegationTests
{
    private static WriteSafetyService CreateSafety(TempAuditDirectory audit, ProjectSessionBinding binding)
        => new(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);

    private static OpennessWorkerClient CreateClient(ProjectSessionBinding binding)
        => new(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

    [Fact]
    public async Task SaveProject_WrapperMatchesRegisteredBindingGate()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        var safety = CreateSafety(audit, binding);
        using var client = CreateClient(binding);

        var registered = await ProjectWriteTools.SaveProject(client, safety, projectPath: null);
        var wrapper = await ProjectLifecycleTools.SaveProject(client, safety, projectPath: null);

        using var registeredDoc = JsonDocument.Parse(registered);
        using var wrapperDoc = JsonDocument.Parse(wrapper);

        Assert.Equal(
            registeredDoc.RootElement.GetProperty("failureCategory").GetString(),
            wrapperDoc.RootElement.GetProperty("failureCategory").GetString());
        Assert.Equal(
            registeredDoc.RootElement.GetProperty("error").GetString(),
            wrapperDoc.RootElement.GetProperty("error").GetString());
        Assert.False(wrapperDoc.RootElement.TryGetProperty("safetyToken", out _));
    }
}
```

Expected RED: `ProjectWriteTools.SaveProject(...)` rejects the unverified binding before preview, while `ProjectLifecycleTools.SaveProject(...)` still follows its duplicated older path.

- [ ] **Step 2: Run the failing lifecycle wrapper regression**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ProjectLifecycleDelegationTests.SaveProject_WrapperMatchesRegisteredBindingGate"
```

Expected RED: assertion mismatch on the returned failure envelope.

- [ ] **Step 3: Replace `ProjectLifecycleTools` with thin forwarding methods**

In `ProjectLifecycleTools.cs`, preserve the summaries and public signatures, but replace each body with a one-line forwarder:

```csharp
public static Task<string> GetProjectStatus(
    OpennessWorkerClient workerClient,
    string? projectPath = null)
    => ProjectReadTools.GetProjectStatus(workerClient, projectPath);

public static Task<string> SaveProject(
    OpennessWorkerClient workerClient,
    WriteSafetyService safety,
    string? projectPath = null,
    bool confirm = false,
    string? safetyToken = null)
    => ProjectWriteTools.SaveProject(
        workerClient,
        safety,
        projectPath,
        confirm,
        safetyToken);
```

Repeat that exact pattern for `OpenProject`, `CreateProject`, `SaveProjectAs`, `ArchiveProject`, and `CloseProject`, forwarding every parameter unchanged and in order.

After the last forwarder lands, remove the private helper methods that no longer have callers.

- [ ] **Step 4: Re-scope `ProjectLifecycleToolTests.cs` to wrapper compatibility**

Keep the metadata theory. Replace the old direct behavior tests with wrapper-vs-registered compatibility checks:

```csharp
[Fact]
public async Task SaveProjectAs_WrapperMatchesRegisteredRebindFalseValidation()
{
    using var audit = new TempAuditDirectory();
    var safety = audit.CreateSafety();

    var registered = await ProjectWriteTools.SaveProjectAs(
        workerClient: null!,
        safety,
        targetDirectory: @"C:\Target",
        targetName: "Copy",
        projectPath: null,
        rebind: false);
    var wrapper = await ProjectLifecycleTools.SaveProjectAs(
        workerClient: null!,
        safety,
        targetDirectory: @"C:\Target",
        targetName: "Copy",
        projectPath: null,
        rebind: false);

    Assert.Equal(registered, wrapper);
}
```

Add a similar read-path forwarding assertion for `GetProjectStatus` using the `status-no-project` FakeWorker scenario and exact text equality. Wrapper schema theories in `McpToolSchemaTests` remain compatibility checks only; they are no longer the registered write-surface authority.

- [ ] **Step 5: Run the lifecycle delegation slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~ProjectLifecycleDelegationTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~ProjectWriteToolsBehaviorTests|FullyQualifiedName~ProjectWriteToolsProtocolTests|FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests.CollapsedOpenProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.SaveProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.SaveProjectAs_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.ArchiveProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.ArchiveProject_PreviewRejectsArchiveDirectoryInsideProjectFolder_WithoutIssuingSafetyToken|FullyQualifiedName~OpennessWorkerClientIntegrationTests.CloseProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.PostWriteVerification_UsesBasicStatusRead_NotExtendedMetadataRead|FullyQualifiedName~WriteSafetyLeaseConcurrencyTests"
```

Expected GREEN.

- [ ] **Step 6: Review checkpoint**

Confirm:

- `ProjectLifecycleTools.cs` no longer owns any lifecycle write-safety behavior.
- The registered project classes remain the only behavioral authority.
- Wrapper compatibility still exists for direct callers and tests.

### Task 5: Add The Mandatory Live V21 Registered-Surface Harness And Durable Acceptance Report

**Files:**

- Create: `scripts/live-test-write-safety-pr2-registered-tools.ps1`
- Create: `TiaMcpServer.Tests/Tools/RegisteredWriteToolsLiveHarnessContractTests.cs`
- Create: `docs/superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`

**Interfaces:**

- Consumes real host startup path support: `dotnet run --project TiaMcpServer -- --project C:\Sandbox\Pr2RegisteredTools.ap21`
- Consumes registered MCP tool name census: `execute_read_batch`, `preview_write_batch`, `apply_write_batch`, `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`
- Produces actual preview-only `tools/call` traffic for `execute_read_batch`, `preview_write_batch`, and tokenless `save_project` only
- Produces durable report sections: runtime/version, disposable target, tool list evidence, generic batch preview evidence, lifecycle preview evidence, and explicit non-mutation boundary
- Preserves: no apply path in this PR 2 live harness

- [ ] **Step 1: Add static contract tests for the live script**

Create `RegisteredWriteToolsLiveHarnessContractTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Tools;

public sealed class RegisteredWriteToolsLiveHarnessContractTests
{
    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(GetRepositoryRoot(), "scripts", "live-test-write-safety-pr2-registered-tools.ps1"));

    [Fact]
    public void Script_RequiresPowerShell7_AndRealMcpProtocol()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("'initialize'", text, StringComparison.Ordinal);
        Assert.Contains("notifications/initialized", text, StringComparison.Ordinal);
        Assert.Contains("'tools/list'", text, StringComparison.Ordinal);
        Assert.Contains("'tools/call'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ListsAllRegisteredWriteTools_ButNeverConstructsAnApplyToolCall()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Contains("apply_write_batch", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Invoke-PreviewToolCall -ToolName 'apply_write_batch'",
            text,
            StringComparison.Ordinal);
        var calls = ExtractPreviewToolCalls(text);

        Assert.Equal(
            new[] { "execute_read_batch", "preview_write_batch", "save_project" },
            calls.Select(call => call.Name).ToArray());
        Assert.All(
            calls,
            call =>
            {
                Assert.DoesNotContain("confirm = $true", call.ArgumentsBlock, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("safetyToken", call.ArgumentsBlock, StringComparison.OrdinalIgnoreCase);
            });
        Assert.DoesNotContain("Read-Host", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_RequiresProjectAndTypePath_AndStartsTheHostWithStartupBinding()
    {
        var text = File.ReadAllText(ScriptPath);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$ProjectPath"), text);
        Assert.Matches(new Regex(@"\[Parameter\(Mandatory\)\]\s*\[string\]\s*\$TypePath"), text);
        Assert.Contains("--project", text, StringComparison.Ordinal);
        Assert.Contains("preview_write_batch", text, StringComparison.Ordinal);
        Assert.Contains("save_project", text, StringComparison.Ordinal);
    }

    private static (string Name, string ArgumentsBlock)[] ExtractPreviewToolCalls(string text)
        => Regex.Matches(
                text,
                @"Invoke-PreviewToolCall\s+-ToolName\s+'(?<name>[^']+)'\s+-Arguments\s+@\{(?<args>.*?)\}",
                RegexOptions.Singleline)
            .Select(match => (match.Groups["name"].Value, match.Groups["args"].Value))
            .ToArray();

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
```

- [ ] **Step 2: Run the contract tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~RegisteredWriteToolsLiveHarnessContractTests"
```

Expected RED: the script and report do not exist yet.

- [ ] **Step 3: Implement the preview-only live harness**

Create `scripts/live-test-write-safety-pr2-registered-tools.ps1` with these fixed properties:

- declare one explicit expected-name list for `tools/list` containing the nine names `execute_read_batch`, `preview_write_batch`, `apply_write_batch`, `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, and `close_project`
- declare one preview-only helper call site with the exact form `Invoke-PreviewToolCall -ToolName '...' -Arguments @{ ... }`, and use it exactly three times: `execute_read_batch`, `preview_write_batch`, and `save_project`
- never construct an `apply_write_batch` `tools/call` payload
- never construct any lifecycle `confirm = $true` or `safetyToken` apply request
- keep the script non-interactive: no `Read-Host`

The presence of `apply_write_batch` in the script must come only from the expected-name list used to validate `tools/list`, never from any `tools/call` helper invocation or payload construction.

```powershell
#Requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ProjectPath,
    [Parameter(Mandatory)] [string] $TypePath,
    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [int] $StartupTimeoutSeconds = 30
)

if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)
}
```

The script must:

1. Start the real `TiaMcpServer` host, never `OpennessWorker.exe`.
2. Speak newline-delimited JSON-RPC over stdio.
3. Call `tools/list` and assert these nine names are present:

```text
execute_read_batch
apply_write_batch
archive_project
close_project
create_project
open_project
preview_write_batch
save_project
save_project_as
```

4. Call `execute_read_batch` with one `get_type_content` operation against `$TypePath` and `$ProjectPath`.
5. Use the returned content verbatim as the `sourceContent` of a one-item `preview_write_batch` request for `update_type_content`.
6. Call `save_project` without a `safetyToken`.
7. Print the returned `safetyToken`, `requestedInputHash`, `currentStateHash`, `instructions`, and a final line stating that no apply call was issued.

Do not add any apply mode, confirm switch, or mutation path to this script.

- [ ] **Step 4: Run the contract tests and verify GREEN**

Run the Step 2 command again.

Expected GREEN: the script is present, PowerShell 7 only, preview only, host-launched, and bound to the registered tool names.

- [ ] **Step 5: Execute the mandatory live V21 harness under the separate authorization gate**

Run only after explicit live authorization and against a disposable or backed-up V21 project copy:

```powershell
pwsh -File scripts/live-test-write-safety-pr2-registered-tools.ps1 -ProjectPath C:\Sandbox\Pr2RegisteredTools.ap21 -TypePath "PLC_1/Types/AnalogInputSettings"
```

Acceptance requirements for the live run:

- `tools/list` shows the eight registered write-tool names from the real host.
- `tools/list` also shows `execute_read_batch`, because the harness uses the real registered read class for the non-mutating baseline read.
- `execute_read_batch` returns the selected type content successfully.
- `preview_write_batch` returns a token and hashes without mutating the project.
- `save_project` without a token returns a self-previewing token and instructions without mutating the project.
- The report states explicitly that no apply call was made.

If the live run cannot be completed, the PR remains incomplete even if every offline test is green.

- [ ] **Step 6: Write the durable acceptance report**

Create `docs/superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md` with this structure and fill it with the actual observed values:

```markdown
# Acceptance Test Report - PR 2 Registered Tool Delegation (live TIA Portal V21)

**Date:** 2026-09-01
**Runtime:** Real TIA Portal V21, read-write MCP host started with the exact `--project` path used for the run
**Harness:** `scripts/live-test-write-safety-pr2-registered-tools.ps1`
**Boundary:** Preview only. No `apply_write_batch`, no lifecycle apply call, no project save, and no PLC mode change were performed.

## Purpose

Document that the real host advertises the registered write surface and that both a generic batch preview and a self-previewing lifecycle call succeed without mutation.

## Tool List Evidence

- exact eight advertised write-tool names
- `execute_read_batch` also present and used only for the baseline non-mutating read

## Generic Batch Preview Evidence

- exact `TypePath`
- exact `operationId`
- token present
- `requestedInputHash`
- `currentStateHash`
- instructions text

## Lifecycle Preview Evidence

- exact tool name (`save_project`)
- token present
- `requestedInputHash`
- `currentStateHash`
- instructions text

## Notes And Limits

- disposable project identity
- whether the project was already open in TIA
- no apply call issued
- no production/plant acceptance claimed
```

Do not leave placeholder markers, blank fields, or omitted hashes in the committed report.

- [ ] **Step 7: Update the current documentation authorities**

Make only the documentation changes this PR actually justifies:

- In `docs/ARCHITECTURE.md`, state that `Program.cs` registers `ProjectReadTools`, `ReadBatchTools`, `ProjectWriteTools`, and `WriteBatchTools`, and that `ProjectLifecycleTools` / `BatchTools` remain compatibility wrappers only.
- In `docs/IMPROVEMENT_LOG.md`, record PR 2 as completed only after the live report exists, and restate that wrapper deletion and PLC start/stop hardening remain deferred.
- In `docs/README.md` and `docs/superpowers/README.md`, add the new acceptance report entry so it is reachable.

Do not change `README.md` unless the implementation uncovered a real landing-page behavior error.

### Task 6: Full Verification And Final Scope Audit

**Files:**

- Review: every file changed by Tasks 1-5

**Verification boundary:**

- Establishes: registered-class behavioral authority, wrapper delegation correctness, protocol surface advertisement, FakeWorker round trips, lease behavior, and live preview-only acceptance.
- Does not establish: PLC control support, PR 3+ snapshot changes, any write apply beyond preview semantics, or physical plant acceptance.

- [ ] **Step 1: Run the complete focused PR 2 suite**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~WriteBatchToolsBehaviorTests|FullyQualifiedName~WriteBatchToolsProtocolTests|FullyQualifiedName~BatchToolsDelegationTests|FullyQualifiedName~TypeOperationFakeWorkerTests|FullyQualifiedName~BlockCurrentStateReadTests|FullyQualifiedName~ProjectWriteToolsBehaviorTests|FullyQualifiedName~ProjectWriteToolsProtocolTests|FullyQualifiedName~ProjectLifecycleDelegationTests|FullyQualifiedName~ProjectLifecycleToolTests|FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~AuditIsolationTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests.CollapsedOpenProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.SaveProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.SaveProjectAs_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.ArchiveProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.ArchiveProject_PreviewRejectsArchiveDirectoryInsideProjectFolder_WithoutIssuingSafetyToken|FullyQualifiedName~OpennessWorkerClientIntegrationTests.CloseProject_|FullyQualifiedName~OpennessWorkerClientIntegrationTests.PostWriteVerification_UsesBasicStatusRead_NotExtendedMetadataRead|FullyQualifiedName~WriteSafetyLeaseConcurrencyTests|FullyQualifiedName~RegisteredWriteToolsLiveHarnessContractTests"
```

Expected GREEN.

- [ ] **Step 2: Run the full serial repository test suite**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo
```

Expected GREEN. Any failure in the moved registered-write paths is in scope and must be fixed before sign-off.

- [ ] **Step 3: Run the stub solution build**

Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers --nologo /p:UseTiaPortalReferenceStubs=true
```

Expected GREEN.

- [ ] **Step 4: Audit whitespace and scope**

Run:

```powershell
git diff --check
git status --short
git diff --stat
```

Confirm all of the following before reporting completion:

- `BatchTools.cs` and `ProjectLifecycleTools.cs` are thin wrappers only.
- `WriteBatchTools` and `ProjectWriteTools` own the direct behavioral tests.
- No public tool name, description, or input schema changed.
- No token format, token lifetime, current-state hash, or audit format changed.
- `start_plc` / `stop_plc` were not modified.
- The live acceptance report exists and states that no apply call was issued.
- `docs/README.md` and `docs/superpowers/README.md` include the new report entry.

- [ ] **Step 5: Final report**

Report:

- which tests were moved from wrapper authority to registered authority;
- which wrapper regressions went RED before delegation and why;
- the focused suite, full suite, stub build, and live harness results;
- the explicit non-mutation boundary of the live acceptance run;
- the deferred items still out of scope: wrapper deletion and PLC start/stop hardening.

Stop before commit, push, merge, or PR comment posting unless separately authorized.
