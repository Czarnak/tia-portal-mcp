# PR 6 Project-Tree Safety Scopes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the broad project-tree snapshot currently bound by registered `WriteBatchTools` for `create_block`, `create_block_group`, and `delete_block_group` with exact, deterministic, operation-specific safety snapshots that preserve software-unit ownership, detect relevant drift, avoid proven-unrelated false invalidations, and keep the existing preview/apply write-safety guarantees unchanged.

**Architecture:** Keep all Siemens object resolution and export work in the net48 worker. Extend the existing `BlockAddress` and `BlockTargetResolver` deterministic PLC-versus-software-unit ownership path so the worker can emit typed exact-scope snapshots for the three structural block operations, then validate and canonicalize those payloads in the net8 host before hashing. Drive the implementation from registered `WriteBatchTools` behavioral REDs first, then add phase-local deduplication that reuses identical snapshot reads only within one preview or one apply state-read pass and still expands back into ordered `OperationBatchCurrentState` entries.

**Tech Stack:** C# 12, .NET 8 host/tests/FakeWorker, .NET Standard 2.0 contracts, .NET Framework 4.8 Openness worker, xUnit, System.Text.Json, PowerShell 7 live harnesses, MCP stdio protocol.

**Spec:** [Write-Safety Preview and Registered-Surface Hardening Design](../specs/2026-09-01-write-safety-hardening-design.md)

## Global Constraints

- Scope is exactly PR 6 from the approved design: narrow current-state reads only for `create_block`, `create_block_group`, and `delete_block_group`.
- Preserve the registered public tool names, batch request shape, preview/apply flow, safety-token lifetime, single-use behavior, exact project/session binding, apply-time pinned binding lease, audit behavior, and read-only/write-only registration surface.
- Behavioral TDD order is mandatory: the first RED must be a focused runtime failure through registered `WriteBatchTools`, not a compile-only or source-contract failure.
- All new end-to-end, drift, and dedup tests must exercise `WriteBatchTools.PreviewWriteBatch` and `WriteBatchTools.ApplyWriteBatch`, not compatibility `BatchTools`.
- Preserve software-unit ownership end to end. Any deterministic path under `PLC/Units/<unit>/Blocks/...` must branch through the current `BlockAddress` and `BlockTargetResolver` unit-aware logic, return the resolved owning unit name and root owner scope, walk that unit’s block group, and canonicalize unit-scoped parent and ancestor paths as `PLC/Units/<unit>/Blocks/...`.
- `create_block` must bind the exact PLC or software-unit owner scope, exact parent group, ancestor chain, requested-name occupancy in that parent, and the exact occupied block export when the requested name is already occupied by a block because the write imports with override semantics.
- `create_block_group` must bind the exact PLC or software-unit owner scope, exact parent group, ancestor chain, and both block and group occupancy for the requested name.
- `delete_block_group` must bind the exact target group, its parent membership, and the complete deterministic content-bearing descendant subtree, including exact block exports for contained blocks, not just names.
- Occupied-block and descendant-subtree exports must be deterministic and must reuse one existing authoritative XML export path instead of inventing a parallel ad hoc serializer.
- Host-side typed snapshot decode must use explicit post-deserialization validators modeled on `NetworkPayloadContract`: missing, null, empty, malformed, or invalid required members and collections must fail closed as `protocol_error`, and the rejected payload must not be echoed.
- Deduplication is phase-local only. Reuse identical selector reads only within one preview or one apply current-state read pass, then expand the reused payload back into ordered per-operation `OperationBatchCurrentState` items before `BatchSafetySnapshot.CombineCurrentState`.
- PR 6 starts only after PR 3's identity-required `OperationCapability.SafetyRead` policy and its worker guard are present. Classify every new internal project-tree snapshot read as `SafetyRead`, never ordinary `Observe`; reuse the existing bound client seam so every request carries `ExpectedSessionIdentity`, and preserve worker-side rejection when that identity is missing.
- The live harness must launch the host with the exact disposable startup binding: `dotnet run --project TiaMcpServer -- --project <DisposableProject.ap21>`.
- Before any live preview, apply, or compile call, the harness must call public `get_project_status` and stop unless the tool succeeds, its decoded payload reports `isOpen:true` with `path` canonically equal to the intended disposable project, and the response envelope's `sessionIdentity.projectPath` canonically matches that same path. Record those public fields; do not assert unavailable `bindingState` or `connectionState` fields.
- Reversible live mutation is required for completion. Any live apply must restore byte-equivalent disposable-project content, prove restoration by re-exporting and comparing the affected deterministic content to the pre-apply baseline, and only then run and record `compile_check` against that restored disposable project. Discarding the project copy is not an alternative to restoration.
- Broader snapshot narrowing, generic tree-pruning heuristics, tag-scope changes, and all `start_plc` or `stop_plc` work remain explicitly out of scope.
- Offline tests and FakeWorker scenarios are necessary but never sufficient for completion. PR 6 is incomplete until the guarded live V21 harness runs and its dated acceptance report is written.
- Use serial Windows .NET verification commands: `dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers -p:UseTiaPortalReferenceStubs=true`, `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers`, `git diff --check`, and `git status --short`.

---

## File And Interface Map

- `TiaMcpServer.Contracts/ProjectTreeSafetySnapshotInfo.cs`
  Shared worker/host records for the three exact tree-snapshot payloads, including owner scope, software unit, ancestor chain, occupancy, occupied block export, and descendant subtree export nodes.
- `TiaMcpServer/Batch/ProjectTreeSafetyPayloadContract.cs`
  Host-side typed decode, required-member validation, and canonical projection for the new snapshot payloads. This is the fail-closed seam that turns worker JSON into deterministic current-state strings.
- `TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs`
  Existing authoritative deterministic block ownership resolver. Extend it rather than introducing a parallel resolver so PLC-scoped and software-unit-scoped paths share one owner-resolution authority.
- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
  Existing authoritative block export surface. Reuse it for deterministic occupied-block and descendant-block XML exports.
- `TiaMcpServer.OpennessWorker/Openness/ProjectTreeSafetySnapshotReader.cs`
  Worker-only exact-scope snapshot reader for `read_create_block_safety_snapshot`, `read_create_block_group_safety_snapshot`, and `read_delete_block_group_safety_snapshot`.
- `TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs`
  Mutation path remains authoritative for the actual write semantics and must reuse the same ownership resolver seam as the snapshot reader.
- `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
  Must consume PR 3's guarded `OperationCapability.SafetyRead` classification for the three new internal worker methods. These reads remain internal and non-mutating, but `RequiresExpectedSessionIdentity(...)` must return `true`; ordinary `Observe` is forbidden for token-minting safety reads.
- `TiaMcpServer.OpennessWorker/Program.cs`
  Must dispatch the three new internal worker methods through `ProjectTreeSafetySnapshotReader` using the existing `WorkerRequest` fields.
- `TiaMcpServer/Worker/OpennessWorkerClient.cs`
  Must add bound helper methods for the three new internal worker snapshot reads and preserve current binding behavior.
- `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
  Must route the three structural write operations to the new internal worker reads, decode them with `ProjectTreeSafetyPayloadContract`, and return `protocol_error` on malformed payloads without raw echo.
- `TiaMcpServer/Batch/WriteBatchTools.cs`
  Must remain the tested runtime path. Add phase-local identical-selector deduplication here and, if needed for exact order assertions, expose an internal current-state-read helper returning ordered `OperationBatchCurrentState` items plus the combined string.
- `TiaMcpServer.FakeWorker/Program.cs`
  Must add exact-scope snapshot scenarios for RED/GREEN behavior, malformed payloads, software-unit paths, and exact per-phase request counters for dedup assertions.
- `TiaMcpServer.Tests/Batch/ProjectTreeSafetyBehaviorTests.cs`
  Registered `WriteBatchTools` end-to-end RED/GREEN tests for occupied-target drift, false invalidation removal, relevant collision drift, and malformed payload handling.
- `TiaMcpServer.Tests/Batch/ProjectTreeSafetyPayloadContractTests.cs`
  Pure typed-decode and validator tests for required members, required arrays, and invalid enum/path/content values.
- `TiaMcpServer.Tests/Batch/ProjectTreeCurrentStateReadTests.cs`
  Request-routing and canonical current-state tests for exact internal worker methods, including root and nested software-unit canonical paths.
- `TiaMcpServer.Tests/Batch/ProjectTreeSafetyDedupTests.cs`
  Exact per-phase internal request-count assertions and ordered `OperationBatchCurrentState` expansion checks.
- `TiaMcpServer.Tests/Project/ProjectTreeSafetySourceContractTests.cs`
  Supplemental source-contract tests pinning internal worker registration, guarded `SafetyRead` policy, worker rejection of missing `ExpectedSessionIdentity`, and reuse of `BlockTargetResolver` rather than a parallel resolver.
- `TiaMcpServer.Tests/Batch/ProjectTreeSafetyLiveHarnessScriptTests.cs`
  Static contract tests for the live PowerShell harness.
- `scripts/live-test-project-tree-safety-scopes.ps1`
  Separately authorized live V21 harness using the public MCP protocol and the public `preview_write_batch`, `apply_write_batch`, `get_project_status`, and `compile_check` routes.
- `docs/superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md`
  Dated live acceptance report for the guarded V21 run.
- `docs/ARCHITECTURE.md`, `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/IMPROVEMENT_LOG.md`, `docs/README.md`, `docs/superpowers/README.md`
  Required documentation updates after implementation and live acceptance complete.

### Task 1: Add The First Registered `WriteBatchTools` Behavioral RED

**Files:**
- Create: `TiaMcpServer.Tests/Batch/ProjectTreeSafetyBehaviorTests.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`

**Interfaces:**
- Exercise existing registered methods only:
  - `WriteBatchTools.PreviewWriteBatch(...)`
  - `WriteBatchTools.ApplyWriteBatch(...)`
- Add FakeWorker scenarios:
  - `tree-safety-create-block-content-drift`
  - `tree-safety-unit-unrelated-sibling-drift`

- [ ] **Step 1: Write the focused runtime regressions through registered `WriteBatchTools`**

Create `ProjectTreeSafetyBehaviorTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyBehaviorTests
{
    [Fact]
    public async Task WriteBatchTools_CreateBlock_OccupiedTargetContentDrift_InvalidatesTheToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-create-block-content-drift");

        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "create",
                Operation = "create_block",
                BlockPath = "PLC_1/Blocks/Main/Mixer",
                BlockType = "FB",
                Language = "SCL",
                ProjectPath = "tree-safety-create-block-content-drift"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);

        Assert.False(applyDoc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("state_changed", applyDoc.RootElement.GetProperty("failureCategory").GetString());
    }

    [Fact]
    public async Task WriteBatchTools_CreateBlockGroup_UnitScopedUnrelatedSiblingDrift_DoesNotInvalidateTheToken()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-unit-unrelated-sibling-drift");

        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "create-group",
                Operation = "create_block_group",
                BlockPath = "PLC_1/Units/Line1/Blocks/Motion/AreaA",
                ProjectPath = "tree-safety-unit-unrelated-sibling-drift"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);

        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);
    }
}
```

- [ ] **Step 2: Implement only the minimum FakeWorker scenarios needed for the RED**

Add two scenario branches:

```csharp
case "tree-safety-create-block-content-drift":
    Respond(CurrentBroadProjectTreePassesPreviewButNotContentDrift(line));
    break;

case "tree-safety-unit-unrelated-sibling-drift":
    Respond(CurrentBroadProjectTreeFalseInvalidatesAcrossUnitSiblings(line));
    break;
```

Make these scenarios reflect the current defect:

- preview succeeds against the broad tree snapshot,
- `create_block` apply does not see occupied-target content drift yet,
- unit-scoped unrelated sibling drift still changes the broad tree snapshot and falsely invalidates.

- [ ] **Step 3: Run the focused registered behavioral tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyBehaviorTests"
```

Expected RED:

- `WriteBatchTools_CreateBlock_OccupiedTargetContentDrift_InvalidatesTheToken` fails because the current broad tree snapshot does not bind occupied block content.
- `WriteBatchTools_CreateBlockGroup_UnitScopedUnrelatedSiblingDrift_DoesNotInvalidateTheToken` fails because the current broad tree snapshot still binds unrelated sibling branches.

- [ ] **Step 4: Review checkpoint**

Confirm the first failures are runtime failures through registered `WriteBatchTools`, not compile-only failures, and do not edit production code yet. Suggested commit if separately authorized: `test: add registered write-batch red for project tree safety scopes`

---

### Task 2: Add Typed Snapshot Contracts And Explicit Host Validators

**Files:**
- Create: `TiaMcpServer.Contracts/ProjectTreeSafetySnapshotInfo.cs`
- Create: `TiaMcpServer/Batch/ProjectTreeSafetyPayloadContract.cs`
- Create: `TiaMcpServer.Tests/Batch/ProjectTreeSafetyPayloadContractTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/ProjectTreeSafetyBehaviorTests.cs`

**Interfaces:**
- Add: `public sealed record ProjectTreeOwnerScopeInfo(string ScopeKind, string PlcName, string? SoftwareUnitName, string RootBlocksPath);`
- Add: `public sealed record ProjectTreeAncestorInfo(string Name, string Path, string Kind);`
- Add: `public sealed record ProjectTreeOccupancyInfo(string Kind, string Name, string Path);`
- Add: `public sealed record ProjectTreeBlockExportInfo(string Name, string Path, string BlockKind, string Format, string ContentSha256, string Content);`
- Add: `public sealed record ProjectTreeGroupDescendantInfo(string Kind, string Name, string Path, string? ContentSha256, string? Content, IReadOnlyList<ProjectTreeGroupDescendantInfo> Children);`
- Add: `public sealed record CreateBlockSafetySnapshotInfo(ProjectTreeOwnerScopeInfo Owner, string ParentPath, IReadOnlyList<ProjectTreeAncestorInfo> Ancestors, IReadOnlyList<ProjectTreeOccupancyInfo> Occupancies, ProjectTreeBlockExportInfo? OccupiedBlock);`
- Add: `public sealed record CreateBlockGroupSafetySnapshotInfo(ProjectTreeOwnerScopeInfo Owner, string ParentPath, IReadOnlyList<ProjectTreeAncestorInfo> Ancestors, IReadOnlyList<ProjectTreeOccupancyInfo> Occupancies);`
- Add: `public sealed record DeleteBlockGroupSafetySnapshotInfo(ProjectTreeOwnerScopeInfo Owner, string ParentPath, string GroupPath, IReadOnlyList<ProjectTreeAncestorInfo> Ancestors, IReadOnlyList<ProjectTreeGroupDescendantInfo> Descendants);`
- Add: `internal static string DecodeCreateBlockAndCanonicalize(string payload)`
- Add: `internal static string DecodeCreateBlockGroupAndCanonicalize(string payload)`
- Add: `internal static string DecodeDeleteBlockGroupAndCanonicalize(string payload)`

- [ ] **Step 1: Write the typed contract and malformed-payload tests**

Create `ProjectTreeSafetyPayloadContractTests.cs`:

```csharp
using System.Text.Json;
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyPayloadContractTests
{
    [Fact]
    public void DecodeCreateBlockAndCanonicalize_RetainsSoftwareUnitOwnerAndOccupiedContent()
    {
        const string payload = """
        {
          "owner": {
            "scopeKind": "SoftwareUnit",
            "plcName": "PLC_1",
            "softwareUnitName": "Line1",
            "rootBlocksPath": "PLC_1/Units/Line1/Blocks"
          },
          "parentPath": "PLC_1/Units/Line1/Blocks/Motion",
          "ancestors": [
            { "name": "Motion", "path": "PLC_1/Units/Line1/Blocks/Motion", "kind": "UserBlockGroup" }
          ],
          "occupancies": [
            { "kind": "FB", "name": "Mixer", "path": "PLC_1/Units/Line1/Blocks/Motion/Mixer" }
          ],
          "occupiedBlock": {
            "name": "Mixer",
            "path": "PLC_1/Units/Line1/Blocks/Motion/Mixer",
            "blockKind": "FB",
            "format": "xml",
            "contentSha256": "abc",
            "content": "<Document>v1</Document>"
          }
        }
        """;

        var canonical = ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize(payload);

        Assert.Contains("\"softwareUnitName\":\"Line1\"", canonical, StringComparison.Ordinal);
        Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks/Motion\"", canonical, StringComparison.Ordinal);
        Assert.Contains("<Document>v1</Document>", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void DecodeCreateBlockGroupAndCanonicalize_RejectsMissingOwner()
    {
        const string payload = """
        {
          "parentPath": "PLC_1/Blocks/Main",
          "ancestors": [],
          "occupancies": []
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(payload));
    }

    [Fact]
    public void DecodeDeleteBlockGroupAndCanonicalize_RejectsBlankDescendantContent()
    {
        const string payload = """
        {
          "owner": {
            "scopeKind": "Plc",
            "plcName": "PLC_1",
            "softwareUnitName": null,
            "rootBlocksPath": "PLC_1/Blocks"
          },
          "parentPath": "PLC_1/Blocks/Main",
          "groupPath": "PLC_1/Blocks/Main/AreaA",
          "ancestors": [],
          "descendants": [
            {
              "kind": "FB",
              "name": "Mixer",
              "path": "PLC_1/Blocks/Main/AreaA/Mixer",
              "contentSha256": "abc",
              "content": "",
              "children": []
            }
          ]
        }
        """;

        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize(payload));
    }

    [Theory]
    [InlineData("""{"owner":{"scopeKind":"Nope","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},"parentPath":"PLC_1/Blocks","ancestors":[],"occupancies":[]}""")]
    [InlineData("""{"owner":{"scopeKind":"Plc","plcName":"","softwareUnitName":null,"rootBlocksPath":"PLC_1/Blocks"},"parentPath":"PLC_1/Blocks","ancestors":[],"occupancies":[]}""")]
    [InlineData("""{"owner":{"scopeKind":"Plc","plcName":"PLC_1","softwareUnitName":null,"rootBlocksPath":""},"parentPath":"PLC_1/Blocks","ancestors":[],"occupancies":[]}""")]
    public void DecodeCreateBlockGroupAndCanonicalize_RejectsInvalidRequiredOwnerValues(string payload)
    {
        Assert.Throws<JsonException>(
            () => ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize(payload));
    }
}
```

- [ ] **Step 2: Run the focused validator tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyPayloadContractTests"
```

Expected RED: the new contract types and `ProjectTreeSafetyPayloadContract` do not exist yet.

- [ ] **Step 3: Implement typed decode plus explicit validators modeled on `NetworkPayloadContract`**

Create `ProjectTreeSafetyPayloadContract.cs` with the same pattern used by `NetworkPayloadContract`:

```csharp
internal static class ProjectTreeSafetyPayloadContract
{
    internal static string DecodeCreateBlockAndCanonicalize(string payload)
    {
        var snapshot = Deserialize<CreateBlockSafetySnapshotInfo>(payload);
        ValidateCreateBlockSnapshot(snapshot, payload);
        return CanonicalJson.Serialize(snapshot);
    }

    private static void ValidateCreateBlockSnapshot(CreateBlockSafetySnapshotInfo snapshot, string payload)
    {
        ValidateOwner(snapshot.Owner, payload);
        RequireNonEmptyPath(snapshot.ParentPath, "parentPath");
        RequireNonNullCollection(snapshot.Ancestors, "ancestors");
        RequireNonNullCollection(snapshot.Occupancies, "occupancies");
        foreach (var ancestor in snapshot.Ancestors)
        {
            ValidateAncestor(ancestor, "ancestors[]");
        }
        foreach (var occupancy in snapshot.Occupancies)
        {
            ValidateOccupancy(occupancy, "occupancies[]");
        }
        if (snapshot.OccupiedBlock is not null)
        {
            ValidateBlockExport(snapshot.OccupiedBlock, "occupiedBlock");
        }
    }
}
```

Required validator behavior:

- `Owner.ScopeKind` is only `Plc` or `SoftwareUnit`.
- `Owner.PlcName` and `Owner.RootBlocksPath` are non-empty.
- `Owner.SoftwareUnitName` must be non-empty when `ScopeKind == "SoftwareUnit"` and must be null when `ScopeKind == "Plc"`.
- Every path field is non-empty and uses deterministic `PLC/...` or `PLC/Units/...` tree form.
- Every `Kind` is from an explicit allow-list such as `UserBlockGroup`, `FB`, `FC`, `OB`, `GlobalDB`, `InstanceDB`, `ArrayDB`.
- Every exported block node has non-empty `Format == "xml"`, `ContentSha256`, and `Content`.
- `Descendants` and descendant `Children` collections must be non-null, and content-bearing descendant block nodes must not carry blank content.

- [ ] **Step 4: Add a malformed worker-payload end-to-end regression through registered `WriteBatchTools`**

Add this test to `ProjectTreeSafetyBehaviorTests.cs`:

```csharp
[Fact]
public async Task WriteBatchTools_CreateBlock_MalformedSnapshotPayload_BecomesProtocolErrorWithoutRawEcho()
{
    using var audit = new TempAuditDirectory();
    var binding = new ProjectSessionBinding(null);
    using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
    var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
    await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-malformed-payload");

    var operations = new[]
    {
        new BatchOperationRequest
        {
            OperationId = "create",
            Operation = "create_block",
            BlockPath = "PLC_1/Blocks/Main/Mixer",
            BlockType = "FB",
            Language = "SCL",
            ProjectPath = "tree-safety-malformed-payload"
        }
    };

    var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
    using var previewDoc = JsonDocument.Parse(preview);

    Assert.False(previewDoc.RootElement.GetProperty("success").GetBoolean());
    Assert.Equal("protocol_error", previewDoc.RootElement.GetProperty("failureCategory").GetString());
    Assert.DoesNotContain("content\":\"", preview, StringComparison.Ordinal);
}
```

- [ ] **Step 5: Run the validator and malformed-payload tests and verify GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyPayloadContractTests|FullyQualifiedName~ProjectTreeSafetyBehaviorTests.WriteBatchTools_CreateBlock_MalformedSnapshotPayload_BecomesProtocolErrorWithoutRawEcho"
```

Expected GREEN: typed decode is explicit and fail-closed, and malformed worker payloads become `protocol_error` without raw payload echo.

- [ ] **Step 6: Review checkpoint**

Confirm the validator step happens after deserialization but before any token issuance, and that `JsonSerializer.Deserialize(...)` is never treated as sufficient validation by itself. Suggested commit if separately authorized: `feat: add validated project tree snapshot payload contract`

---

### Task 3: Preserve Software-Unit Ownership Through `BlockAddress` And `BlockTargetResolver`

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/ProjectTreeSafetySnapshotReader.cs`
- Create: `TiaMcpServer.Tests/Project/ProjectTreeSafetySourceContractTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/ProjectTreeCurrentStateReadTests.cs`
- Modify: `TiaMcpServer.Tests/Block/BlockAddressTests.cs`

**Interfaces:**
- Add in `BlockTargetResolver.cs`: `internal static ResolvedBlockOwner ResolveOwnerForDeterministicPath(PlcSoftware plcSoftware, BlockAddress address)`
- Add in `BlockTargetResolver.cs`: `internal sealed class ResolvedBlockOwner`
- Add in `ProjectTreeSafetySnapshotReader.cs`:
  - `public static CreateBlockSafetySnapshotInfo ReadCreateBlockSnapshot(Project project, string blockPath, string blockType, string? language, string? obEventClass)`
  - `public static CreateBlockGroupSafetySnapshotInfo ReadCreateBlockGroupSnapshot(Project project, string blockPath)`
  - `public static DeleteBlockGroupSafetySnapshotInfo ReadDeleteBlockGroupSnapshot(Project project, string blockPath)`

- [ ] **Step 1: Write the software-unit canonical-path tests**

Add to `ProjectTreeCurrentStateReadTests.cs`:

```csharp
[Fact]
public async Task CreateBlock_CurrentStateRead_CanonicalizesSoftwareUnitRootParentPath()
{
    using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
    var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
    {
        OperationId = "create-root-unit",
        Operation = "create_block",
        BlockPath = "PLC_1/Units/Line1/Blocks/Main",
        BlockType = "FB",
        Language = "SCL",
        ProjectPath = "tree-safety-unit-root"
    });

    Assert.True(result.Success, result.Error);
    Assert.Contains("\"softwareUnitName\":\"Line1\"", result.Payload, StringComparison.Ordinal);
    Assert.Contains("\"rootBlocksPath\":\"PLC_1/Units/Line1/Blocks\"", result.Payload, StringComparison.Ordinal);
    Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks\"", result.Payload, StringComparison.Ordinal);
}

[Fact]
public async Task DeleteBlockGroup_CurrentStateRead_CanonicalizesNestedSoftwareUnitAncestorPath()
{
    using var client = new OpennessWorkerClient(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
    var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
    {
        OperationId = "delete-nested-unit",
        Operation = "delete_block_group",
        BlockPath = "PLC_1/Units/Line1/Blocks/Motion/AreaA",
        ProjectPath = "tree-safety-unit-nested"
    });

    Assert.True(result.Success, result.Error);
    Assert.Contains("\"parentPath\":\"PLC_1/Units/Line1/Blocks/Motion\"", result.Payload, StringComparison.Ordinal);
    Assert.Contains("\"path\":\"PLC_1/Units/Line1/Blocks/Motion/AreaA\"", result.Payload, StringComparison.Ordinal);
}
```

Add to `ProjectTreeSafetySourceContractTests.cs`:

```csharp
[Fact]
public void ProjectTreeSafetySnapshotReader_UsesBlockTargetResolverForDeterministicOwnership()
{
    var source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), "TiaMcpServer.OpennessWorker", "Openness", "ProjectTreeSafetySnapshotReader.cs"));

    Assert.Contains("BlockTargetResolver.ResolveOwnerForDeterministicPath", source, StringComparison.Ordinal);
    Assert.DoesNotContain("SoftwareUnitName = null", source, StringComparison.Ordinal);
}
```

Add to `BlockAddressTests.cs`:

```csharp
[Fact]
public void ParseSupportsSoftwareUnitRootBlockPath()
{
    var address = BlockAddress.Parse("PLC_1/Units/Line1/Blocks/Main");

    Assert.Equal("PLC_1", address.PlcName);
    Assert.Equal("Line1", address.UnitName);
    Assert.Empty(address.FolderPath);
    Assert.Equal("Main", address.BlockName);
    Assert.True(address.UsesSoftwareUnit);
}
```

- [ ] **Step 2: Run the software-unit tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~CreateBlock_CurrentStateRead_CanonicalizesSoftwareUnitRootParentPath|FullyQualifiedName~DeleteBlockGroup_CurrentStateRead_CanonicalizesNestedSoftwareUnitAncestorPath|FullyQualifiedName~ParseSupportsSoftwareUnitRootBlockPath|FullyQualifiedName~ProjectTreeSafetySnapshotReader_UsesBlockTargetResolverForDeterministicOwnership"
```

Expected RED: the current plan baseline cannot yet emit unit-scoped owner metadata and canonical unit paths through the exact current-state read.

- [ ] **Step 3: Extend `BlockTargetResolver` instead of introducing a parallel resolver**

Add an owner model inside `BlockTargetResolver.cs`:

```csharp
internal sealed class ResolvedBlockOwner
{
    public ResolvedBlockOwner(
        string scopeKind,
        string plcName,
        string? softwareUnitName,
        string rootBlocksPath,
        PlcBlockGroup rootBlockGroup,
        PlcExternalSourceSystemGroup externalSourceGroup)
    {
        ScopeKind = scopeKind;
        PlcName = plcName;
        SoftwareUnitName = softwareUnitName;
        RootBlocksPath = rootBlocksPath;
        RootBlockGroup = rootBlockGroup;
        ExternalSourceGroup = externalSourceGroup;
    }

    public string ScopeKind { get; }
    public string PlcName { get; }
    public string? SoftwareUnitName { get; }
    public string RootBlocksPath { get; }
    public PlcBlockGroup RootBlockGroup { get; }
    public PlcExternalSourceSystemGroup ExternalSourceGroup { get; }
}
```

Then add:

```csharp
internal static ResolvedBlockOwner ResolveOwnerForDeterministicPath(PlcSoftware plcSoftware, BlockAddress address)
{
    if (!address.UsesSoftwareUnit)
    {
        return new ResolvedBlockOwner(
            "Plc",
            plcSoftware.Name,
            address.UnitName,
            $"{plcSoftware.Name}/Blocks",
            plcSoftware.BlockGroup,
            plcSoftware.ExternalSourceGroup);
    }

    var unit = FindSoftwareUnit(plcSoftware, address.UnitName!);
    var resolvedSoftwareUnitName = unit.Name;
    return new ResolvedBlockOwner(
        "SoftwareUnit",
        plcSoftware.Name,
        resolvedSoftwareUnitName,
        $"{plcSoftware.Name}/Units/{unit.Name}/Blocks",
        unit.BlockGroup,
        unit.ExternalSourceGroup);
}
```

`BlockMutationService.CreateBlock`, `CreateBlockGroup`, and `DeleteBlockGroup` must continue to resolve through the same owner/resolution seam instead of reimplementing unit detection locally.

- [ ] **Step 4: Implement the worker snapshot reader with exact owner-aware canonical paths**

Implement `ProjectTreeSafetySnapshotReader.cs` so it:

- parses the path with `BlockAddress.Parse(...)`,
- resolves the owning PLC or software unit with `BlockTargetResolver.ResolveOwnerForDeterministicPath(...)`,
- derives `ParentPath` from `owner.RootBlocksPath` plus `address.FolderPath`,
- emits ancestor paths under the same root,
- uses `BlockExporter` for deterministic XML exports of occupied blocks and descendant blocks,
- never hard-codes `SoftwareUnitName = null` for unit-scoped paths.

For example:

```csharp
var address = BlockAddress.Parse(blockPath);
var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
var owner = address.IsDeterministic
    ? BlockTargetResolver.ResolveOwnerForDeterministicPath(plcSoftware, address)
    : throw new InvalidOperationException("Project-tree safety snapshots require deterministic block paths.");
var parentPath = address.FolderPath.Count == 0
    ? owner.RootBlocksPath
    : $"{owner.RootBlocksPath}/{string.Join("/", address.FolderPath)}";
```

- [ ] **Step 5: Run the software-unit tests and the supplemental source-contract tests and verify GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeCurrentStateReadTests|FullyQualifiedName~ProjectTreeSafetySourceContractTests|FullyQualifiedName~BlockAddressTests.ParseSupportsSoftwareUnitBlockPath|FullyQualifiedName~BlockAddressTests.ParseSupportsSoftwareUnitRootBlockPath"
```

Expected GREEN: root and nested unit-scoped paths retain the owning software unit and canonical `PLC/Units/<unit>/Blocks/...` paths end to end.

- [ ] **Step 6: Review checkpoint**

Confirm there is no new `BlockPathResolver` file, no unit path is flattened back to PLC scope, and `BlockTargetResolver` remains the single authority for deterministic owner resolution. Suggested commit if separately authorized: `feat: preserve software-unit ownership in tree safety snapshots`

---

### Task 4: Route Exact Snapshot Reads Through The Registered Write Path And Close The Original RED

**Files:**
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/Batch/ProjectTreeSafetyBehaviorTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/ProjectTreeCurrentStateReadTests.cs`
- Modify: `TiaMcpServer.Tests/Project/ProjectTreeSafetySourceContractTests.cs`
- Create: `TiaMcpServer.Tests/Worker/ProjectTreeSafetyIdentityEnforcementTests.cs`

**Interfaces:**
- Add:
  - `public Task<WorkerCallResult> ReadCreateBlockSafetySnapshotAsync(string blockPath, string blockType, string? language, string? obEventClass, string? projectPath)`
  - `public Task<WorkerCallResult> ReadCreateBlockGroupSafetySnapshotAsync(string blockPath, string? projectPath)`
  - `public Task<WorkerCallResult> ReadDeleteBlockGroupSafetySnapshotAsync(string blockPath, string? projectPath)`
- Classify all three internal method names with PR 3's `OperationCapability.SafetyRead`; `OperationPolicyCatalog.RequiresExpectedSessionIdentity(...)` must return `true` for each.
- Add in `BatchWorkerInvoker.cs`: `private static async Task<WorkerCallResult> DecodeProjectTreeSnapshotAsync(Task<WorkerCallResult> pending, Func<string, string> decode)`

- [ ] **Step 1: Write the exact routing tests**

Add to `ProjectTreeCurrentStateReadTests.cs`:

```csharp
[Fact]
public async Task CreateBlock_CurrentStateRead_UsesExactInternalSnapshotMethod()
{
    const string scenario = "tree-safety-route-create-block";
    var binding = new ProjectSessionBinding(null);
    using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
    await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
    var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
    {
        OperationId = "create",
        Operation = "create_block",
        BlockPath = "PLC_1/Blocks/Main/Mixer",
        BlockType = "FB",
        Language = "SCL",
        ProjectPath = scenario
    });

    Assert.True(result.Success, result.Error);
    Assert.Contains("\"parentPath\":\"PLC_1/Blocks/Main\"", result.Payload, StringComparison.Ordinal);
}

[Fact]
public async Task DeleteBlockGroup_CurrentStateRead_DoesNotUseBrowseProjectTree()
{
    const string scenario = "tree-safety-route-delete-block-group";
    var binding = new ProjectSessionBinding(null);
    using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
    await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);
    var result = await BatchWorkerInvoker.ReadCurrentStateAsync(client, new BatchOperationRequest
    {
        OperationId = "delete",
        Operation = "delete_block_group",
        BlockPath = "PLC_1/Blocks/Main/AreaA",
        ProjectPath = scenario
    });

    Assert.True(result.Success, result.Error);
    Assert.Contains("\"groupPath\":\"PLC_1/Blocks/Main/AreaA\"", result.Payload, StringComparison.Ordinal);
}
```

Add the equivalent `create_block_group` case using scenario `tree-safety-route-create-block-group`, operation path `PLC_1/Blocks/Main/AreaA`, and an assertion for canonical `parentPath:"PLC_1/Blocks/Main"`. After allowing the initial `get_project_status` binding probe, each route scenario must fail unless the FakeWorker receives its one expected internal snapshot method, then return a valid typed payload; therefore `BatchWorkerInvoker.ReadCurrentStateAsync` success is runtime evidence that it did not fall back to `browse_project_tree`.

Add an identity-carrying request regression after establishing a verified FakeWorker binding. The `echo` scenario returns the serialized `WorkerRequest`, so the assertion proves the production client seam—not a hand-built request—sent the complete identity:

```csharp
[Fact]
public async Task CreateBlock_CurrentStateRead_SendsExpectedSessionIdentity()
{
    var binding = new ProjectSessionBinding(null);
    using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
    await FakeWorkerBinding.BindVerifiedAsync(client, binding, "echo");

    var result = await client.ReadCreateBlockSafetySnapshotAsync(
        blockPath: "PLC_1/Blocks/Main/Mixer",
        blockType: "FB",
        language: "SCL",
        obEventClass: null,
        projectPath: "echo");

    Assert.True(result.Success, result.Error);
    using var request = JsonDocument.Parse(result.Payload);
    var identity = request.RootElement.GetProperty("expectedSessionIdentity");
    Assert.False(string.IsNullOrWhiteSpace(identity.GetProperty("workerSessionId").GetString()));
    Assert.True(string.Equals(
        binding.CaptureSnapshot().ProjectPath,
        identity.GetProperty("projectPath").GetString(),
        StringComparison.OrdinalIgnoreCase));
}
```

Add executable worker-guard coverage in `ProjectTreeSafetyIdentityEnforcementTests.cs` so the same identity-required policy is proven beyond source inspection:

```csharp
[Theory]
[InlineData("read_create_block_safety_snapshot")]
[InlineData("read_create_block_group_safety_snapshot")]
[InlineData("read_delete_block_group_safety_snapshot")]
public async Task InternalTreeSafetyRead_RejectsMissingExpectedSessionIdentity(string method)
{
    using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));
    var observed = await transport.SendAsync(new WorkerRequest
    {
        Method = "read_hardware_config",
        ProjectPath = "tree-safety-request-echo"
    });

    Assert.True(observed.Success, observed.Error);

    var response = await transport.SendAsync(new WorkerRequest
    {
        Method = method,
        ProjectPath = "tree-safety-request-echo"
    });

    Assert.False(response.Success);
    Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
}
```

Add to `ProjectTreeSafetySourceContractTests.cs` a table-driven policy/worker-guard regression for all three internal method names:

```csharp
[Theory]
[InlineData("read_create_block_safety_snapshot")]
[InlineData("read_create_block_group_safety_snapshot")]
[InlineData("read_delete_block_group_safety_snapshot")]
public void InternalTreeSafetyRead_IsIdentityRequiredSafetyRead(string method)
{
    Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(method));
    Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
}

[Fact]
public void WorkerGuard_RejectsMissingExpectedSessionIdentity_ForSafetyReads()
{
    var program = ReadRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs");
    var policy = ReadRepositoryFile("TiaMcpServer.Contracts", "OperationPolicyCatalog.cs");

    Assert.Contains("AllowsMissingExpectedSessionIdentity(request.Method)", program, StringComparison.Ordinal);
    Assert.Contains("!OperationPolicyCatalog.RequiresExpectedSessionIdentity(method)", program, StringComparison.Ordinal);
    Assert.Contains("OperationCapability.SafetyRead", policy, StringComparison.Ordinal);
    foreach (var method in new[]
    {
        "read_create_block_safety_snapshot",
        "read_create_block_group_safety_snapshot",
        "read_delete_block_group_safety_snapshot"
    })
    {
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(method));
    }
}
```

The executable transport test proves the worker actually rejects a missing `ExpectedSessionIdentity` with `binding_conflict`; the source-contract test pins why that happens by keeping each method on the shared `SafetyRead` guard path. Do not weaken `RequiresExpectedSessionIdentity(...)` or classify these methods as ordinary `Observe` merely because they are non-mutating.

- [ ] **Step 2: Run the focused routing, identity, and registered-behavior tests and confirm RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeCurrentStateReadTests|FullyQualifiedName~ProjectTreeSafetyIdentityEnforcementTests|FullyQualifiedName~ProjectTreeSafetySourceContractTests|FullyQualifiedName~ProjectTreeSafetyBehaviorTests"
```

Expected RED: the Task 1 registered behavioral failures are already captured; this slice additionally fails because the exact client methods are absent, because all three methods are not yet guarded `SafetyRead` operations, and because the worker still permits missing identity for reads that have not yet been reclassified onto that shared guard. Do not write production code before recording this focused RED.

- [ ] **Step 3: Add worker-client methods and route `BatchWorkerInvoker.ReadCurrentStateAsync` to them**

Implement:

```csharp
public Task<WorkerCallResult> ReadCreateBlockSafetySnapshotAsync(
    string blockPath,
    string blockType,
    string? language,
    string? obEventClass,
    string? projectPath)
{
    return SendBoundProjectRequestAsync(
        "read_create_block_safety_snapshot",
        projectPath,
        request =>
        {
            request.BlockPath = blockPath;
            request.BlockType = blockType;
            request.Language = language;
            request.OBEventClass = obEventClass;
        },
        "{}");
}
```

Register the three method names as `OperationCapability.SafetyRead` using the capability and worker guard delivered by PR 3. Do not add a parallel identity exception or transport path. `SendBoundProjectRequestAsync(...)` must remain the only client path so `ExpectedSessionIdentity = bindingBeforeCall.ToWorkerIdentity()` is populated by the existing core request builder, and the worker must evaluate the same catalog before dispatch.

Implement the decode shim:

```csharp
private static async Task<WorkerCallResult> DecodeProjectTreeSnapshotAsync(
    Task<WorkerCallResult> pending,
    Func<string, string> decode)
{
    var result = await pending.ConfigureAwait(false);
    if (!result.Success)
    {
        return result;
    }

    try
    {
        return WorkerCallResult.Ok(decode(result.Payload), result.Warnings);
    }
    catch (JsonException)
    {
        return WorkerCallResult.Fail(
            WorkerFailureCategories.ProtocolError,
            "The project-tree safety snapshot payload did not match its declared contract.",
            result.Warnings);
    }
}
```

Then route:

```csharp
"create_block" => DecodeProjectTreeSnapshotAsync(
    client.ReadCreateBlockSafetySnapshotAsync(
        op.BlockPath!, op.BlockType!, op.Language, op.ObEventClass, op.ProjectPath),
    ProjectTreeSafetyPayloadContract.DecodeCreateBlockAndCanonicalize),
"create_block_group" => DecodeProjectTreeSnapshotAsync(
    client.ReadCreateBlockGroupSafetySnapshotAsync(op.BlockPath!, op.ProjectPath),
    ProjectTreeSafetyPayloadContract.DecodeCreateBlockGroupAndCanonicalize),
"delete_block_group" => DecodeProjectTreeSnapshotAsync(
    client.ReadDeleteBlockGroupSafetySnapshotAsync(op.BlockPath!, op.ProjectPath),
    ProjectTreeSafetyPayloadContract.DecodeDeleteBlockGroupAndCanonicalize),
```

- [ ] **Step 4: Expand the FakeWorker scenarios to reflect exact-scope behavior**

Update the scenarios introduced in Task 1 and add:

- `tree-safety-route-create-block`
- `tree-safety-route-create-block-group`
- `tree-safety-route-delete-block-group`
- `tree-safety-create-group-collision-drift`
- `tree-safety-delete-group-descendant-drift`
- `tree-safety-malformed-payload`

Green behavior must be:

- after its required `get_project_status` binding probe, each `tree-safety-route-*` scenario rejects every worker method except its exact `read_*_safety_snapshot` method and returns a valid typed snapshot only for that method,
- `create_block` stale token is rejected when only occupied target content changes,
- `create_block_group` stale token is rejected when only same-parent name occupancy changes,
- `delete_block_group` stale token is rejected when only one descendant block’s content changes,
- unit-scoped unrelated sibling drift no longer invalidates the target write,
- malformed exact snapshot payload becomes `protocol_error`.

- [ ] **Step 5: Run the registered behavioral suite and verify GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyBehaviorTests|FullyQualifiedName~ProjectTreeCurrentStateReadTests|FullyQualifiedName~ProjectTreeSafetySourceContractTests"
```

Expected GREEN:

- the Task 1 REDs are now closed through registered `WriteBatchTools`,
- exact internal worker methods are used,
- every internal snapshot request carries `ExpectedSessionIdentity`, and the worker guard rejects a missing identity,
- `delete_block_group` now binds descendant block content,
- malformed payloads fail as `protocol_error`.

- [ ] **Step 6: Review checkpoint**

Confirm the broad `BrowseProjectTreeAsync(op.ProjectPath)` binding path is gone only for these three operations, all three internal reads are guarded `SafetyRead` operations rather than `Observe`, the production bound request seam is the only transport path, and the tests still execute `WriteBatchTools`, not compatibility `BatchTools`. Suggested commit if separately authorized: `feat: route structural block writes through exact tree snapshots`

---

### Task 5: Add Exact Per-Phase Dedup Assertions And Ordered State Expansion Checks

**Files:**
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/Batch/ProjectTreeSafetyDedupTests.cs`

**Interfaces:**
- Add in `WriteBatchTools.cs`:
  - `private readonly record struct ProjectTreeSelectorKey(string Operation, string? ProjectPath, string BlockPath, string? BlockType, string? Language, string? ObEventClass);`
  - `internal static async Task<(IReadOnlyList<OperationBatchCurrentState> States, string CombinedState, string? Error)> ReadCurrentStatesForTestingAsync(OpennessWorkerClient workerClient, BatchOperationRequest[] operations)`
- Preserve private caller surface:
  - `PreviewWriteBatch(...)`
  - `ApplyWriteBatch(...)`

- [ ] **Step 1: Write the exact dedup tests**

Create `ProjectTreeSafetyDedupTests.cs`:

```csharp
using System.Linq;
using System.Text.Json;
using TiaMcpServer.Batch;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyDedupTests
{
    [Fact]
    public async Task ReadCurrentStatesForTestingAsync_ExpandsTwoIdenticalSelectorsBackIntoOrderedOperationStates()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-dedup");

        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "first", Operation = "create_block_group", BlockPath = "PLC_1/Blocks/Main/AreaA", ProjectPath = "tree-safety-dedup" },
            new BatchOperationRequest { OperationId = "second", Operation = "create_block_group", BlockPath = "PLC_1/Blocks/Main/AreaA", ProjectPath = "tree-safety-dedup" }
        };

        var snapshot = await WriteBatchTools.ReadCurrentStatesForTestingAsync(client, operations);

        Assert.Null(snapshot.Error);
        Assert.Equal(new[] { "first", "second" }, snapshot.States.Select(state => state.OperationId).ToArray());
        Assert.Equal(new[] { "create_block_group", "create_block_group" }, snapshot.States.Select(state => state.Operation).ToArray());
        Assert.Equal(snapshot.States[0].CurrentState, snapshot.States[1].CurrentState);
    }

    [Fact]
    public async Task WriteBatchTools_DeduplicatesOnePreviewReadButPerformsAFreshApplyRead()
    {
        using var audit = new TempAuditDirectory();
        var binding = new ProjectSessionBinding(null);
        using var client = new OpennessWorkerClient(binding, logger: null, workerExecutablePath: FakeWorkerLocator.Locate());
        var safety = new WriteSafetyService(binding, () => DateTimeOffset.UtcNow, WriteSafetyService.DefaultTokenLifetime, audit.Path);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "tree-safety-dedup");

        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "first", Operation = "create_block_group", BlockPath = "PLC_1/Blocks/Main/AreaA", ProjectPath = "tree-safety-dedup" },
            new BatchOperationRequest { OperationId = "second", Operation = "create_block_group", BlockPath = "PLC_1/Blocks/Main/AreaA", ProjectPath = "tree-safety-dedup" }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        using var previewDoc = JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);
        using var applyDoc = JsonDocument.Parse(apply);
        Assert.True(applyDoc.RootElement.GetProperty("success").GetBoolean(), apply);

        var counters = FakeWorkerCounterProbe.Read("tree-safety-dedup");
        Assert.Equal(1, counters["read_create_block_group_safety_snapshot.preview"]);
        Assert.Equal(1, counters["read_create_block_group_safety_snapshot.apply"]);
    }
}
```

- [ ] **Step 2: Run the dedup tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyDedupTests"
```

Expected RED:

- identical selectors still trigger two internal reads during preview,
- there is no exact per-phase request count yet,
- ordered expansion cannot yet be asserted through an exposed helper.

- [ ] **Step 3: Implement phase-local deduplication and exact count observability**

Change `WriteBatchTools.cs` so the current-state read helper becomes:

```csharp
internal static async Task<(IReadOnlyList<OperationBatchCurrentState> States, string CombinedState, string? Error)>
    ReadCurrentStatesForTestingAsync(OpennessWorkerClient workerClient, BatchOperationRequest[] operations)
{
    var states = new List<OperationBatchCurrentState>(operations.Length);
    var cache = new Dictionary<ProjectTreeSelectorKey, string>();

    foreach (var op in operations)
    {
        if (TryBuildProjectTreeSelectorKey(op, out var key))
        {
            if (!cache.TryGetValue(key, out var currentState))
            {
                var workerResult = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
                if (!workerResult.Success)
                {
                    return (Array.Empty<OperationBatchCurrentState>(), string.Empty, $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {workerResult.Error}");
                }

                currentState = workerResult.Payload;
                cache.Add(key, currentState);
            }

            states.Add(new OperationBatchCurrentState(op.OperationId, op.Operation, currentState));
            continue;
        }

        var fallback = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
        if (!fallback.Success)
        {
            return (Array.Empty<OperationBatchCurrentState>(), string.Empty, $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {fallback.Error}");
        }

        states.Add(new OperationBatchCurrentState(op.OperationId, op.Operation, fallback.Payload));
    }

    return (states, BatchSafetySnapshot.CombineCurrentState(states), null);
}
```

Make `PreviewWriteBatch` and `ApplyWriteBatch` call that helper internally. Add FakeWorker phase-specific counters keyed by method plus `preview` or `apply`.

- [ ] **Step 4: Run the exact dedup tests and neighbouring registered behavior tests and verify GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyDedupTests|FullyQualifiedName~ProjectTreeSafetyBehaviorTests"
```

Expected GREEN:

- preview issues exactly one internal snapshot read for two identical selector keys,
- apply issues one fresh internal snapshot read rather than reusing the preview cache,
- `OperationBatchCurrentState` expansion preserves original operation IDs and order.

- [ ] **Step 5: Review checkpoint**

Confirm the dedup cache lives only inside one helper invocation, the per-phase counters distinguish preview from apply, and no dedup path changes ordered state composition. Suggested commit if separately authorized: `feat: deduplicate identical tree safety reads per phase`

---

### Task 6: Add The Guarded Live Harness With Exact Startup Binding Verification

**Files:**
- Create: `scripts/live-test-project-tree-safety-scopes.ps1`
- Create: `TiaMcpServer.Tests/Batch/ProjectTreeSafetyLiveHarnessScriptTests.cs`

**Interfaces:**
- Harness modes: `Inventory`, `Preview`, `Apply`
- Default host launch arguments: `@('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)`
- Public MCP route only: `initialize`, `notifications/initialized`, `tools/list`, `tools/call`, `get_project_status`, `preview_write_batch`, `apply_write_batch`, `compile_check`
- Exact mutation gate: `-AllowMutation` and `-Acknowledgement 'OVERRIDE BLOCKS AND DELETE GROUPS'`

- [ ] **Step 1: Write the static harness contract tests first**

Create `ProjectTreeSafetyLiveHarnessScriptTests.cs`:

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Batch;

public sealed class ProjectTreeSafetyLiveHarnessScriptTests
{
    [Fact]
    public void Script_DefaultsToInventoryAndExactStartupProjectBinding()
    {
        var text = ReadScript();
        Assert.Matches(new Regex(@"\[ValidateSet\('Inventory', 'Preview', 'Apply'\)\]"), text);
        Assert.Matches(new Regex(@"\[string\]\s+\$Mode\s*=\s*'Inventory'"), text);
        Assert.Contains("@('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_VerifiesStartupBindingBeforePreviewApplyOrCompile()
    {
        var text = ReadScript();
        Assert.Contains("function Assert-VerifiedStartupBinding", text, StringComparison.Ordinal);
        Assert.Contains("get_project_status", text, StringComparison.Ordinal);
        Assert.Contains("$status.success", text, StringComparison.Ordinal);
        Assert.Contains("$statusPayload.isOpen", text, StringComparison.Ordinal);
        Assert.Contains("$statusPayload.path", text, StringComparison.Ordinal);
        Assert.Contains("$status.sessionIdentity.projectPath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("bindingState", text, StringComparison.Ordinal);
        Assert.DoesNotContain("connectionState", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesOnlyPublicRoutesAndNeverLifecycleMutation()
    {
        var text = ReadScript();
        Assert.Contains("preview_write_batch", text, StringComparison.Ordinal);
        Assert.Contains("apply_write_batch", text, StringComparison.Ordinal);
        Assert.Contains("compile_check", text, StringComparison.Ordinal);
        Assert.DoesNotContain("open_project", text, StringComparison.Ordinal);
        Assert.DoesNotContain("save_project", text, StringComparison.Ordinal);
        Assert.DoesNotContain("close_project", text, StringComparison.Ordinal);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_ApplyRequiresAllowMutationAndExactAcknowledgement()
    {
        var text = ReadScript();
        Assert.Contains("[switch] $AllowMutation", text, StringComparison.Ordinal);
        Assert.Contains("$script:RequiredAcknowledgement = 'OVERRIDE BLOCKS AND DELETE GROUPS'", text, StringComparison.Ordinal);
        Assert.Contains("-cne $script:RequiredAcknowledgement", text, StringComparison.Ordinal);
        Assert.Equal(1, Regex.Matches(text, @"confirm\s*=\s*\$true").Count);
    }

    [Fact]
    public void Script_RestoresAndProvesByteEquivalentContentBeforeFinalCompile()
    {
        var text = ReadScript();
        Assert.Contains("function Restore-ByteEquivalentProjectContent", text, StringComparison.Ordinal);
        Assert.Contains("function Assert-ByteEquivalentProjectContent", text, StringComparison.Ordinal);
        Assert.Contains("preApplyContentSha256", text, StringComparison.Ordinal);
        Assert.Contains("restoredContentSha256", text, StringComparison.Ordinal);
        Assert.Contains("$script:MutationStarted", text, StringComparison.Ordinal);
        Assert.Contains("finally", text, StringComparison.Ordinal);
        Assert.DoesNotContain("discard", text, StringComparison.OrdinalIgnoreCase);
        Assert.Matches(
            new Regex(@"Assert-ByteEquivalentProjectContent[\s\S]*Invoke-CompileCheck", RegexOptions.CultureInvariant),
            text);
    }
}
```

- [ ] **Step 2: Run the static harness tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyLiveHarnessScriptTests"
```

Expected RED: the script does not exist yet.

- [ ] **Step 3: Implement the live harness**

Create `scripts/live-test-project-tree-safety-scopes.ps1` with these fixed startup semantics:

```powershell
#Requires -Version 7
[CmdletBinding()]
param(
    [ValidateSet('Inventory', 'Preview', 'Apply')]
    [string] $Mode = 'Inventory',
    [Parameter(Mandatory)] [string] $ProjectPath,
    [switch] $AllowMutation,
    [string] $Acknowledgement,
    [string] $HostExecutable = 'dotnet',
    [string[]] $HostArguments,
    [int] $StartupTimeoutSeconds = 120
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:RequiredAcknowledgement = 'OVERRIDE BLOCKS AND DELETE GROUPS'
if (-not $HostArguments -or $HostArguments.Count -eq 0) {
    $HostArguments = @('run', '--project', 'TiaMcpServer', '--', '--project', $ProjectPath)
}
```

The script must:

- call `get_project_status` immediately after MCP initialize,
- decode the public tool envelope and its JSON `payload`, then stop unless `success == true`, `payload.isOpen == true`, `payload.path` canonically equals the intended disposable project, and `sessionIdentity.projectPath` canonically equals that same path,
- canonicalize all three paths with resolved full-path semantics and compare them with `OrdinalIgnoreCase`; a basename or raw input-string comparison is insufficient,
- record the successful status call, `payload.isOpen`, `payload.path`, and `sessionIdentity.projectPath` before any preview/apply/compile; never invent or assert unavailable `bindingState` or `connectionState` fields,
- keep `Inventory` and `Preview` non-mutating,
- use only public `preview_write_batch` / `apply_write_batch` / `compile_check` routes for write acceptance,
- redact every persisted safety token from the JSON artifact,
- before Apply, capture the exact deterministic exported bytes and SHA-256 values needed to reconstruct and verify every affected object,
- set a mutation-started guard before the first confirmed apply and wrap the mutation sequence in `try`/`finally`; after any mutation attempt, the `finally` path must restore the affected disposable-project content through the same public preview/apply path, re-export it, and byte-compare it to the pre-Apply baseline even when a later probe fails,
- stop on any restoration mismatch and do not call final `compile_check` until byte-equivalent restoration has been proven,
- call `get_project_status` again and require the same successful `isOpen`/payload-path/session-identity-path proof immediately before final `compile_check`.

- [ ] **Step 4: Run the static harness tests and verify GREEN**

Run the Step 2 command.

Expected GREEN: the harness uses only public observable status evidence for exact startup binding, verifies it before any preview/apply/compile, does not add a lifecycle mutation, keeps Apply hard-gated, and cannot compile or report success before byte-equivalent restoration.

- [ ] **Step 5: Review checkpoint**

Confirm the script records only successful `get_project_status` evidence (`isOpen`, payload path, and `sessionIdentity.projectPath`) before any guarded write preview, never mutates lifecycle state merely to establish binding, uses the single `confirm:$true` apply call site for both the authorized mutation and deterministic restoration, and refuses final compile on a content-byte mismatch. Suggested commit if separately authorized: `test: add exact-startup-bound live tree safety harness`

---

### Task 7: Run Live V21 Acceptance, Restore The Disposable Project, And Update Current Docs

**Files:**
- Create: `docs/superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`

**Interfaces:**
- Acceptance report sections:
  - branch and commit
  - exact startup host command
  - disposable project path
  - successful `get_project_status`, payload `isOpen`/`path`, and envelope `sessionIdentity.projectPath` before preview
  - occupied-target content-drift rejection evidence
  - descendant block-content-drift rejection evidence
  - relevant collision rejection evidence
  - unrelated drift non-false-invalidation evidence
  - reversible apply plus pre/post export hashes and byte-equivalent restoration evidence
  - final `compile_check` evidence
  - explicit deferred items

- [ ] **Step 1: Run the full offline suite before touching live TIA**

Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers -p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
```

Expected GREEN: the repository passes before live acceptance begins.

- [ ] **Step 2: Run the non-mutating live inventory and preview passes**

Run:

```powershell
pwsh -NoProfile -File scripts/live-test-project-tree-safety-scopes.ps1 -ProjectPath C:\Sandbox\Pr6TreeScopes.ap21
pwsh -NoProfile -File scripts/live-test-project-tree-safety-scopes.ps1 -ProjectPath C:\Sandbox\Pr6TreeScopes.ap21 -Mode Preview
```

Expected GREEN:

- `get_project_status` succeeds before preview,
- its decoded payload reports `isOpen:true` and a canonical `path` equal to the exact startup `--project` path,
- the response envelope's `sessionIdentity.projectPath` canonically matches that same disposable project,
- inventory identifies the disposable occupied block target, collision target, and delete subtree.

- [ ] **Step 3: Run the separately authorized reversible apply pass**

Run:

```powershell
pwsh -NoProfile -File scripts/live-test-project-tree-safety-scopes.ps1 `
  -ProjectPath C:\Sandbox\Pr6TreeScopes.ap21 `
  -Mode Apply `
  -AllowMutation `
  -Acknowledgement 'OVERRIDE BLOCKS AND DELETE GROUPS'
```

Expected GREEN:

- stale `create_block` token is rejected after only occupied-target content drift,
- stale `delete_block_group` token is rejected after only descendant block-content drift,
- stale `create_block_group` token is rejected after only relevant same-parent occupancy drift,
- unrelated sibling-tree drift does not invalidate the target write,
- the authorized target-mutation apply sequence succeeds and the separately previewed restoration apply sequence succeeds through the same guarded call site,
- the script restores the affected disposable-project content and proves the restored deterministic export bytes match the pre-Apply baseline exactly,
- the repeated public status proof still reports the same open disposable project immediately before compile,
- a final `compile_check` succeeds against the restored disposable project.

- [ ] **Step 4: Write the dated acceptance report and update the current documentation authorities**

Write `docs/superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md` with these exact sections, populated from the actual run:

```markdown
# Acceptance Test Report — PR 6 Project-Tree Safety Scopes

## Environment
- Branch:
- Commit:
- Startup host command:
- Disposable project:
- TIA Portal version:
- `get_project_status` success before preview:
- Payload `isOpen` before preview:
- Payload `path` before preview:
- Envelope `sessionIdentity.projectPath` before preview:
- Repeated `get_project_status` success before final compile:
- Repeated payload `isOpen` before final compile:
- Repeated payload `path` before final compile:
- Repeated envelope `sessionIdentity.projectPath` before final compile:

## Restoration
- Pre-Apply deterministic export SHA-256:
- Restored deterministic export SHA-256:
- Byte comparison result:

## Evidence
1. Occupied-target content drift invalidated `create_block` before mutation.
2. Descendant block-content drift invalidated `delete_block_group` before mutation.
3. Relevant collision drift invalidated `create_block_group`.
4. Unrelated sibling-tree drift did not invalidate the token.
5. The authorized target-mutation and restoration apply sequences both succeeded through the guarded public flow.
6. Pre-Apply and restored deterministic export SHA-256 values matched, proving byte-equivalent content restoration.
7. Public status evidence still identified the same open disposable project after restoration.
8. Final `compile_check` passed on the restored disposable project.

## Deferred Items
- Broader snapshot narrowing: unchanged and out of scope.
- `start_plc` / `stop_plc`: unchanged and out of scope.
```

Then update:

- `docs/ARCHITECTURE.md` with the exact narrowed project-tree selector architecture,
- `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` with the exact current-state binding behavior for the three structural operations,
- `docs/IMPROVEMENT_LOG.md` with the completed PR 6 scope and remaining deferrals,
- `docs/README.md` and `docs/superpowers/README.md` so the new report is indexed.

- [ ] **Step 5: Review checkpoint**

Confirm the report distinguishes repository-auditable artifacts from live-only observations, records only the successful public status evidence (`isOpen`, payload path, and `sessionIdentity.projectPath`) for project binding, records the restoration hash comparison before compile, and repeats both deferrals verbatim. Suggested commit if separately authorized: `docs: record live acceptance for project tree safety scopes`

---

### Task 8: Final Verification And Scope Review

**Files:**
- Review: every file changed in Tasks 1-7 only

**Verification boundary:**
- Establishes: registered `WriteBatchTools` behavioral RED closure, explicit typed payload validation, software-unit ownership preservation, exact routing to identity-required `SafetyRead` snapshots, client-sent and worker-enforced `ExpectedSessionIdentity`, deterministic occupied-block and descendant exports, phase-local dedup with exact per-phase request counts, public-status-proven live startup binding, byte-equivalent restoration, and final compile success.
- Does not establish: broader snapshot narrowing beyond these three operations, any tag-scope change, PLC start/stop behavior, plant acceptance, or physical-hardware commissioning.

- [ ] **Step 1: Run the focused PR 6 regression slice**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~ProjectTreeSafetyBehaviorTests|FullyQualifiedName~ProjectTreeSafetyPayloadContractTests|FullyQualifiedName~ProjectTreeCurrentStateReadTests|FullyQualifiedName~ProjectTreeSafetyDedupTests|FullyQualifiedName~ProjectTreeSafetySourceContractTests|FullyQualifiedName~ProjectTreeSafetyLiveHarnessScriptTests"
```

Expected GREEN: every new PR 6 regression passes.

- [ ] **Step 2: Run the full serial repository verification**

Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers -p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
git diff --check
git status --short
```

Expected GREEN: clean whitespace, passing suite, and a diff limited to the planned files.

- [ ] **Step 3: Perform the final scope audit**

Check the diff against this list:

```text
Must exist:
- TiaMcpServer.Contracts/ProjectTreeSafetySnapshotInfo.cs
- TiaMcpServer/Batch/ProjectTreeSafetyPayloadContract.cs
- TiaMcpServer.OpennessWorker/Openness/ProjectTreeSafetySnapshotReader.cs
- TiaMcpServer.Tests/Batch/ProjectTreeSafetyBehaviorTests.cs
- TiaMcpServer.Tests/Batch/ProjectTreeSafetyPayloadContractTests.cs
- TiaMcpServer.Tests/Batch/ProjectTreeCurrentStateReadTests.cs
- TiaMcpServer.Tests/Batch/ProjectTreeSafetyDedupTests.cs
- TiaMcpServer.Tests/Project/ProjectTreeSafetySourceContractTests.cs
- TiaMcpServer.Tests/Batch/ProjectTreeSafetyLiveHarnessScriptTests.cs
- scripts/live-test-project-tree-safety-scopes.ps1
- docs/superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md

Must be modified:
- TiaMcpServer.Contracts/OperationPolicyCatalog.cs
- TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs
- TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs
- TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs
- TiaMcpServer.OpennessWorker/Program.cs
- TiaMcpServer/Worker/OpennessWorkerClient.cs
- TiaMcpServer/Batch/BatchWorkerInvoker.cs
- TiaMcpServer/Batch/WriteBatchTools.cs
- TiaMcpServer.FakeWorker/Program.cs

Must remain out of scope:
- Any public tool rename or batch request DTO field addition
- Any compatibility-path-only test as primary evidence
- Any new parallel block ownership resolver outside `BlockTargetResolver`
- Any change to `start_plc` or `stop_plc`
- Broader tag or project-tree snapshot narrowing outside these three operations
```

- [ ] **Step 4: Final report**

Report:

- the first registered `WriteBatchTools` RED and how it was closed,
- the exact new snapshot contract and validation seams,
- the software-unit ownership preservation evidence,
- the guarded `SafetyRead` classification plus client-send and worker-rejection identity evidence,
- the exact per-phase dedup counts and ordered-state expansion evidence,
- the public `get_project_status` startup-binding evidence, byte-equivalent restoration evidence, and final compile evidence,
- the explicit remaining deferrals.

Stop before merge, push, or PR comment unless separately authorized.
