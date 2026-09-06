# PR 5 Tag Operation Safety Scopes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the blanket `ListTagTablesAsync` batch write-safety selector with operation-specific typed tag-table, tag, and user-constant snapshots that catch proven relevant drift, tolerate unrelated sibling drift, and preserve the existing token semantics.

**Architecture:** Keep Siemens object resolution and deterministic table export inside the net48 worker, but move the host from one broad `list_tag_tables` current-state read to exact per-operation snapshot reads decoded through shared typed contracts. Add a pure selector-key layer in the host for within-phase dedup only, then expand reused payloads back into ordered per-operation state strings before `OperationBatchStateComposer` hashes them. Use FakeWorker to prove the host-side token behavior end to end, but treat it only as offline evidence; mandatory live TIA Portal V21 acceptance on a disposable project copy remains required for PR completion.

**Tech Stack:** C# 12, .NET 8 host/FakeWorker/tests, .NET Standard 2.0 shared contracts, .NET Framework 4.8 Openness worker, xUnit, `System.Text.Json`, canonical JSON helpers, newline-delimited JSON worker IPC, PowerShell 7 live harnesses.

**Spec:** [`docs/superpowers/specs/2026-09-01-write-safety-hardening-design.md`](../specs/2026-09-01-write-safety-hardening-design.md)

**Final-review scope correction (2026-09-05):** The exact destination folder is an identity,
not the boundary of table-name uniqueness. `create_tag_table` must probe matching names throughout
the selected PLC's tag-table hierarchy. Tag/user-constant create and update probes must include
matching PLC tags, user constants, and ordinary program blocks (nested user and system-block
groups), preserving actual kind plus canonical identity. Names compare case-insensitively;
logical-address probes remain tag-only. This corrects any parent-only or same-kind interpretation
of the original plan using Siemens V21's [PLC tag/table naming rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/rules-for-plc-tags/valid-names-of-plc-tags)
and [global constant naming rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/declaring-global-constants/rules-for-global-user-constants).
Software Unit namespace resolution is outside these existing unqualified selectors; unit-local
block names must not be guessed into the CPU-global collision scope. Namespace-aware coverage
remains a design/live follow-up. No public schema, token, audit, or failure-category change is
authorized by this correction.

## Global Constraints

- Implement PR 5 only on top of the approved PR 2 and PR 3 baseline. If the branch still carries the pre-PR2 duplicated write path in `TiaMcpServer/Batch/BatchTools.cs`, merge or rebase the prerequisite first instead of backporting PR 5 into the old wrapper shape.
- Work on the current branch. Do not create or switch branches or worktrees under this plan.
- Scope is exactly PR 5 from the approved design: typed tag-safety snapshots, exact collision probes, deterministic timestamp-free full tag-table export for deletion, and within-phase dedup with ordered expansion.
- Preserve public tool names, public input schemas, token lifetime, single-use behavior, ordered target hashing, apply-time pinned lease behavior, audit format, and failure categories.
- Internal token-minting snapshot reads must consume the PR 3 `OperationCapability.SafetyRead` policy: allowed in read-only mode, but still requiring the verified `ExpectedSessionIdentity` on every request.
- Keep the worker as the only authority for Siemens object resolution. The host may validate, decode, canonicalize, hash, deduplicate, and compose snapshots, but it must not reconstruct target identity from best-effort `list_tag_tables` output.
- `list_tag_tables` remains best-effort and read-only. Do not change its public completeness semantics as part of PR 5.
- `update_tag` already binds the three external-access flags from PR 3. PR 5 must replace the remaining broad table-list composition rather than reintroducing or duplicating that milestone.
- Dedup may reuse only identical selector reads within one preview phase or one apply phase. The key must include normalized project path, selector kind, PLC identity, folder path, table name, target object name, the requested name semantics for that operation, and the effective requested logical address when relevant.
- Dedup must not survive across preview and apply. Apply always performs fresh reads.
- Every reused payload must expand back into one `OperationBatchCurrentState` entry per original operation in the original request order before `BatchSafetySnapshot.CombineCurrentState` runs.
- Explicitly defer: multilingual per-tag comment binding, public `list_tag_tables` completeness changes, broader snapshot narrowing beyond the eight PR 5 operations, and all PLC `start_plc` / `stop_plc` work.
- Follow behavioral TDD. Each task below starts with a focused failing test or contract test, then the smallest production change, then focused green verification.
- Offline and FakeWorker evidence are required but cannot complete the PR. Mandatory live TIA Portal V21 acceptance requires a guarded live harness and report.
- Do not run the live harness without separate explicit authorization for the exact disposable project copy and the exact restore-or-discard strategy for that run.
- Use serial Windows .NET verification commands: `dotnet build ... --no-restore -m:1 --disable-build-servers` and `dotnet test ... --no-restore -m:1 --disable-build-servers`.
- Do not commit, push, post comments, or run live mutation merely because a task reaches its checkpoint. Each checkpoint includes only a suggested commit if separately authorized.

## Deferred / Out Of Scope

- multilingual per-tag comment binding
- public `list_tag_tables` completeness changes
- broader snapshot narrowing beyond the eight PR 5 operations
- PLC `start_plc` / `stop_plc`
- any attempt to treat offline, FakeWorker, or static contract evidence as a substitute for mandatory live TIA Portal V21 acceptance

## File / Interface Map

- Create: `TiaMcpServer.Contracts/TagOperationSafetySnapshotInfo.cs`
  Shared typed snapshot records for the eight tag-related write selectors:
  `CreateTagTableSafetySnapshotInfo`, `DeleteTagTableSafetySnapshotInfo`,
  `CreateTagSafetySnapshotInfo`, `UpdateTagSafetySnapshotInfo`,
  `DeleteTagSafetySnapshotInfo`, `CreateUserConstantSafetySnapshotInfo`,
  `UpdateUserConstantSafetySnapshotInfo`, and `DeleteUserConstantSafetySnapshotInfo`.
- Create: `TiaMcpServer/Batch/TagOperationSafetySnapshotContract.cs`
  Host-side exact payload decoder that rejects malformed worker payloads as `protocol_error`
  and reserializes validated snapshots through one canonical JSON path for hashing.
- Create: `TiaMcpServer/Batch/TagOperationSafetySelector.cs`
  Pure selector-key builder for effective-name/effective-address normalization and within-phase dedup.
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
  Replace the broad `ListTagTablesAsync` current-state read for tag operations with exact per-operation worker snapshot calls.
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs`
  Add the ephemeral dedup map inside `ReadCombinedCurrentStateAsync` and preserve ordered `OperationBatchCurrentState` expansion.
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
  Add internal worker-call methods for each exact tag safety snapshot read.
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
  Register the new internal worker read methods under the existing `SafetyRead` policy so they stay identity-bound without advertising them publicly.
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
  Dispatch the new internal worker methods to the worker reader.
- Create: `TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotBuilder.cs`
  Pure deterministic snapshot shaper and exact-content helper that the test project can link without Siemens assemblies; export determinism comes from the TIA export call options first, with any fallback normalization tightly proven and allowlisted.
- Create: `TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotReader.cs`
  Siemens-dependent resolver/exporter that produces the typed shared snapshot DTOs.
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
  Add deterministic tag-safety scenarios for same-object drift, relevant collision drift, unrelated sibling tolerance, delete-table export drift, and apply re-read proof.
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
  Link any new pure helper source file needed for net8 test coverage.
- Create or modify focused tests:
  `TiaMcpServer.Tests/Batch/TagOperationSafetySnapshotContractTests.cs`,
  `TiaMcpServer.Tests/Batch/TagOperationSafetySelectorTests.cs`,
  `TiaMcpServer.Tests/Batch/TagOperationPrerequisiteBaselineTests.cs`,
  `TiaMcpServer.Tests/Batch/TagOperationSafetyWorkerSourceContractTests.cs`,
  `TiaMcpServer.Tests/Batch/TagOperationCurrentStateReadFakeWorkerTests.cs`,
  `TiaMcpServer.Tests/Batch/TagOperationFakeWorkerTests.cs`,
  `TiaMcpServer.Tests/Worker/TagOperationSafetyClientIdentityTests.cs`,
  `TiaMcpServer.Tests/Worker/TagOperationSafetyIdentityEnforcementTests.cs`,
  `TiaMcpServer.Tests/Safety/TagOperationSafetyReadPolicyTests.cs`,
  `TiaMcpServer.Tests/Batch/BatchSafetySnapshotTests.cs`,
  and the live-harness contract test.
- Create: `scripts/live-test-tag-operation-safety-scopes.ps1`
  Guarded PowerShell 7 live harness with a non-mutating default and explicit mutation modes.
- Create: `TiaMcpServer.Tests/Batch/TagOperationSafetyLiveHarnessContractTests.cs`
  Static execution-free contract tests for the live harness.
- Modify current docs after implementation:
  `docs/ARCHITECTURE.md`,
  `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`,
  `docs/IMPROVEMENT_LOG.md`,
  `docs/README.md`,
  `docs/superpowers/README.md`.
- Create after the authorized live run:
  `docs/superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md`.

---

### Task 0: Prove the PR 2 and PR 3 Baseline Before Any PR 5 RED/GREEN Cycle

**Files:**
- Create: `TiaMcpServer.Tests/Batch/TagOperationPrerequisiteBaselineTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes existing registered write surface: `TiaMcpServer/Program.cs` must keep `.WithTools<WriteBatchTools>()`.
- Consumes existing PR 3 baseline: `OperationCapability.SafetyRead` already exists, and `update_tag` must still forward `externalAccessible`, `externalVisible`, and `externalWritable` through the registered write path.
- Produces a hard stop gate for the rest of this plan: if these proofs fail, do not continue with PR 5 until the branch is rebased/merged onto PR 2 and PR 3.

- [ ] **Step 1: Add the prerequisite baseline tests**

Create `TagOperationPrerequisiteBaselineTests.cs`:

```csharp
namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationPrerequisiteBaselineTests
{
    [Fact]
    public void Program_RegistersWriteBatchTools()
    {
        var text = File.ReadAllText(Source("TiaMcpServer/Program.cs"));

        Assert.Contains(".WithTools<WriteBatchTools>()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateTag_BaselineStillCarriesPr3MutableFlagsThroughTheRegisteredWritePath()
    {
        var capability = File.ReadAllText(Source("TiaMcpServer.Contracts/OperationCapability.cs"));
        var catalog = File.ReadAllText(Source("TiaMcpServer/Batch/BatchOperationCatalog.cs"));
        var invoker = File.ReadAllText(Source("TiaMcpServer/Batch/BatchWorkerInvoker.cs"));

        Assert.Contains("SafetyRead", capability, StringComparison.Ordinal);
        Assert.Contains("\"externalAccessible\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"externalVisible\"", catalog, StringComparison.Ordinal);
        Assert.Contains("\"externalWritable\"", catalog, StringComparison.Ordinal);
        Assert.Contains(
            "client.UpdateTagAsync(op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.DataType, op.LogicalAddress, op.ExternalAccessible, op.ExternalVisible, op.ExternalWritable, op.IsSafety, op.ProjectPath)",
            invoker,
            StringComparison.Ordinal);
    }

    private static string Source(string relative)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative));
}
```

- [ ] **Step 2: Run the prerequisite checkpoint and require GREEN**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationPrerequisiteBaselineTests"
```

Expected GREEN:

- `Program.cs` already exposes the registered `WriteBatchTools` surface from PR 2.
- `OperationCapability.SafetyRead` already exists from PR 3.
- `update_tag` still carries the PR 3 external-access flag baseline through the registered write path.

If this command is RED, stop. Rebase or merge the missing prerequisite before writing any PR 5 test or production change.

- [ ] **Step 3: Review checkpoint**

Confirm:

- PR 5 work will run on the registered PR 2 write surface, not the obsolete wrapper shape.
- PR 3's `SafetyRead` capability and `update_tag` mutable-state flag baseline are still present and must be preserved by the narrower PR 5 snapshot work.

Suggested commit: none. This task is a prerequisite gate, not a deliverable.

---

### Task 1: Define the Shared Snapshot Contracts and Pure Host Decoders

**Files:**
- Create: `TiaMcpServer.Contracts/TagOperationSafetySnapshotInfo.cs`
- Create: `TiaMcpServer/Batch/TagOperationSafetySnapshotContract.cs`
- Create: `TiaMcpServer/Batch/TagOperationSafetySelector.cs`
- Create: `TiaMcpServer.Tests/Batch/TagOperationSafetySnapshotContractTests.cs`
- Create: `TiaMcpServer.Tests/Batch/TagOperationSafetySelectorTests.cs`
- Modify: `TiaMcpServer.Tests/Batch/BatchSafetySnapshotTests.cs`

**Interfaces:**
- Add: `public sealed record TagTableSafetyIdentityInfo(string PlcName, string FolderPath, string TableName, string CanonicalPath);`
- Add: `public sealed record TagSafetyIdentityInfo(string PlcName, string FolderPath, string TableName, string TagName, string CanonicalPath, string DataType, string? LogicalAddress, bool? ExternalAccessible, bool? ExternalVisible, bool? ExternalWritable);`
- Add: `public sealed record UserConstantSafetyIdentityInfo(string PlcName, string FolderPath, string TableName, string ConstantName, string CanonicalPath, string DataType, string Value);`
- Add: `public sealed record TagCollisionProbeInfo(string Kind, string CandidateName, string CanonicalPath, string? LogicalAddress, bool IsTarget);`
- Add one typed record per operation snapshot with only the fields that operation needs.
- Add: `internal sealed record TagOperationSafetyDecodeResult(bool Success, string CanonicalState, string? Error = null, string? FailureCategory = null);`
- Add: `internal sealed record TagOperationSafetySelectorKey(string SelectorKind, string? NormalizedProjectPath, string PlcName, string FolderPath, string TableName, string? ObjectName, string? EffectiveName, string? EffectiveLogicalAddress);`
- Add: `public static TagOperationSafetyDecodeResult Decode(string operation, string payload)` in `TagOperationSafetySnapshotContract`.
- Add: `public static TagOperationSafetySelectorKey Build(BatchOperationRequest op)` in `TagOperationSafetySelector`.
- Add: `public static bool TryBuild(BatchOperationRequest op, out TagOperationSafetySelectorKey key)` in `TagOperationSafetySelector`.

- [ ] **Step 1: Add failing contract and selector tests**

Create `TagOperationSafetySnapshotContractTests.cs` with exact decode coverage:

```csharp
using TiaMcpServer.Batch;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetySnapshotContractTests
{
    [Fact]
    public void UpdateTagSnapshot_PreservesFalseFlagsAndEffectiveRename()
    {
        var payload = """
        {
          "targetTable":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","canonicalPath":"PLC_1/Tag tables/Inputs"},
          "targetTag":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","tagName":"Start","canonicalPath":"PLC_1/Tag tables/Inputs/Start","dataType":"Bool","logicalAddress":"%I0.0","externalAccessible":false,"externalVisible":false,"externalWritable":false},
          "effectiveName":"Start_1",
          "effectiveLogicalAddress":"%I0.1",
          "nameCollisions":[{"kind":"tag-name","candidateName":"Start_1","canonicalPath":"PLC_1/Tag tables/Inputs/Start_1","logicalAddress":"%I0.1","isTarget":false}],
          "addressCollisions":[{"kind":"logical-address","candidateName":"Other","canonicalPath":"PLC_1/Tag tables/Inputs/Other","logicalAddress":"%I0.1","isTarget":false}]
        }
        """;

        var result = TagOperationSafetySnapshotContract.Decode("update_tag", payload);

        Assert.True(result.Success, result.Error);
        Assert.Contains("\"externalAccessible\":false", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"externalVisible\":false", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"externalWritable\":false", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"effectiveName\":\"Start_1\"", result.CanonicalState, StringComparison.Ordinal);
        Assert.Contains("\"effectiveLogicalAddress\":\"%I0.1\"", result.CanonicalState, StringComparison.Ordinal);
    }

    [Fact]
    public void DeleteTagTableSnapshot_RequiresFullExport()
    {
        var payload = """
        {
          "targetTable":{"plcName":"PLC_1","folderPath":"","tableName":"Inputs","canonicalPath":"PLC_1/Tag tables/Inputs"},
          "exportedSimaticMl":"<Document />",
          "exportSha256":"abc123",
          "characterCount":12
        }
        """;

        var result = TagOperationSafetySnapshotContract.Decode("delete_tag_table", payload);

        Assert.True(result.Success, result.Error);
        Assert.Contains("\"exportedSimaticMl\":\"<Document />\"", result.CanonicalState, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedPayload_FailsClosedAsProtocolError()
    {
        var result = TagOperationSafetySnapshotContract.Decode("create_tag", "{\"targetTable\":42}");

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
    }
}
```

Create `TagOperationSafetySelectorTests.cs` with effective-name/effective-address coverage:

```csharp
using TiaMcpServer.Batch;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetySelectorTests
{
    [Fact]
    public void Build_UpdateTag_UsesRequestedRenameAndLogicalAddress()
    {
        var key = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "u1",
            Operation = "update_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start",
            NewName = "Start_1",
            LogicalAddress = "%I0.1"
        });

        Assert.Equal("update_tag", key.SelectorKind);
        Assert.Equal(@"C:\Plant\Demo.ap21", key.NormalizedProjectPath);
        Assert.Equal("Start_1", key.EffectiveName);
        Assert.Equal("%I0.1", key.EffectiveLogicalAddress);
    }

    [Fact]
    public void Build_DeleteTag_DoesNotCollapseIntoUpdateTagKey()
    {
        var update = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "u1",
            Operation = "update_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start"
        });
        var delete = TagOperationSafetySelector.Build(new BatchOperationRequest
        {
            OperationId = "d1",
            Operation = "delete_tag",
            ProjectPath = @"C:\Plant\Demo.ap21",
            PlcName = "PLC_1",
            TableName = "Inputs",
            Name = "Start"
        });

        Assert.NotEqual(update.SelectorKind, delete.SelectorKind);
    }
}
```

Extend `BatchSafetySnapshotTests.cs` so the description and ordered-target assertions cover at least `delete_tag_table` and `create_user_constant`.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationSafetySnapshotContractTests|FullyQualifiedName~TagOperationSafetySelectorTests|FullyQualifiedName~BatchSafetySnapshotTests"
```

Expected RED: the snapshot contract types, decoder, and selector key do not exist yet.

- [ ] **Step 3: Implement the smallest shared DTO set**

Create `TagOperationSafetySnapshotInfo.cs` with the exact operation records. Use one file so every host/worker/test reference stays in the same shared contract surface:

```csharp
namespace TiaMcpServer.Contracts;

public sealed record TagTableSafetyIdentityInfo(
    string PlcName,
    string FolderPath,
    string TableName,
    string CanonicalPath);

public sealed record TagSafetyIdentityInfo(
    string PlcName,
    string FolderPath,
    string TableName,
    string TagName,
    string CanonicalPath,
    string DataType,
    string? LogicalAddress,
    bool? ExternalAccessible,
    bool? ExternalVisible,
    bool? ExternalWritable);

public sealed record UserConstantSafetyIdentityInfo(
    string PlcName,
    string FolderPath,
    string TableName,
    string ConstantName,
    string CanonicalPath,
    string DataType,
    string Value);

public sealed record TagCollisionProbeInfo(
    string Kind,
    string CandidateName,
    string CanonicalPath,
    string? LogicalAddress,
    bool IsTarget);

public sealed record CreateTagTableSafetySnapshotInfo(
    string PlcName,
    string FolderPath,
    string RequestedTableName,
    IReadOnlyList<TagCollisionProbeInfo> TableNameCollisions);

public sealed record DeleteTagTableSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    string ExportedSimaticMl,
    string ExportSha256,
    int CharacterCount);

public sealed record CreateTagSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    string EffectiveName,
    string? EffectiveLogicalAddress,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions,
    IReadOnlyList<TagCollisionProbeInfo> AddressCollisions);

public sealed record UpdateTagSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    TagSafetyIdentityInfo TargetTag,
    string EffectiveName,
    string? EffectiveLogicalAddress,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions,
    IReadOnlyList<TagCollisionProbeInfo> AddressCollisions);

public sealed record DeleteTagSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    TagSafetyIdentityInfo TargetTag);

public sealed record CreateUserConstantSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    string EffectiveName,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions);

public sealed record UpdateUserConstantSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    UserConstantSafetyIdentityInfo TargetConstant,
    string EffectiveName,
    IReadOnlyList<TagCollisionProbeInfo> NameCollisions);

public sealed record DeleteUserConstantSafetySnapshotInfo(
    TagTableSafetyIdentityInfo TargetTable,
    UserConstantSafetyIdentityInfo TargetConstant);
```

The two `Delete*` snapshots intentionally omit collision lists: by design they bind the current exact target object and table identity, not hypothetical future occupancy.

- [ ] **Step 4: Implement the exact decoder and pure selector key**

`TagOperationSafetySnapshotContract.Decode` must deserialize to the exact CLR type for the requested operation, reject a wrong or malformed payload as `protocol_error`, and return canonical JSON using the existing canonical serializer:

```csharp
internal static class TagOperationSafetySnapshotContract
{
    public static TagOperationSafetyDecodeResult Decode(string operation, string payload)
    {
        try
        {
            var canonical = operation switch
            {
                "create_tag_table" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<CreateTagTableSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing create_tag_table snapshot.")),
                "delete_tag_table" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<DeleteTagTableSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing delete_tag_table snapshot.")),
                "create_tag" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<CreateTagSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing create_tag snapshot.")),
                "update_tag" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<UpdateTagSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing update_tag snapshot.")),
                "delete_tag" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<DeleteTagSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing delete_tag snapshot.")),
                "create_user_constant" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<CreateUserConstantSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing create_user_constant snapshot.")),
                "update_user_constant" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<UpdateUserConstantSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing update_user_constant snapshot.")),
                "delete_user_constant" => CanonicalJson.Serialize(
                    JsonSerializer.Deserialize<DeleteUserConstantSafetySnapshotInfo>(payload, CanonicalJson.SerializerOptions)
                    ?? throw new JsonException("Missing delete_user_constant snapshot.")),
                _ => throw new InvalidOperationException($"Unsupported tag safety operation '{operation}'.")
            };

            return new(true, canonical);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return new(false, string.Empty, ex.Message, WorkerFailureCategories.ProtocolError);
        }
    }
}
```

`TagOperationSafetySelector.Build` must normalize:

```csharp
var effectiveName = op.Operation switch
{
    "update_tag" => string.IsNullOrWhiteSpace(op.NewName) ? op.Name : op.NewName,
    "update_user_constant" => op.Name,
    _ => op.Name
};
```

For `create_tag` and `update_tag`, `EffectiveLogicalAddress` is the requested `LogicalAddress`; for every other operation it is `null`.

- [ ] **Step 5: Run focused tests and verify GREEN**

Run the Step 2 command again.

Also run:

```powershell
dotnet build TiaMcpServer.Contracts/TiaMcpServer.Contracts.csproj --no-restore -m:1 --disable-build-servers --nologo
```

Expected GREEN: the shared contracts build and the focused contract/selector tests pass.

- [ ] **Step 6: Review checkpoint**

Confirm:

- every operation has exactly one typed snapshot DTO;
- `false` and `null` remain distinguishable for the three tag external flags;
- no public batch request shape changed;
- the host decoder rejects malformed payloads without echoing them.

Suggested commit if separately authorized:

```bash
git add TiaMcpServer.Contracts/TagOperationSafetySnapshotInfo.cs TiaMcpServer/Batch/TagOperationSafetySnapshotContract.cs TiaMcpServer/Batch/TagOperationSafetySelector.cs TiaMcpServer.Tests/Batch/TagOperationSafetySnapshotContractTests.cs TiaMcpServer.Tests/Batch/TagOperationSafetySelectorTests.cs TiaMcpServer.Tests/Batch/BatchSafetySnapshotTests.cs
git commit -m "feat(batch): define tag operation safety contracts"
```

---

### Task 2: Add the Guarded Internal Worker Snapshot Readers and Deterministic Export Helper

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotBuilder.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotReader.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Create: `TiaMcpServer.Tests/Batch/TagOperationSafetyWorkerSourceContractTests.cs`
- Create: `TiaMcpServer.Tests/Batch/TagOperationSafetyBuilderTests.cs`
- Create: `TiaMcpServer.Tests/Worker/TagOperationSafetyClientIdentityTests.cs`
- Create: `TiaMcpServer.Tests/Worker/TagOperationSafetyIdentityEnforcementTests.cs`
- Create: `TiaMcpServer.Tests/Safety/TagOperationSafetyReadPolicyTests.cs`

**Interfaces:**
- Add: `internal static string PreserveDeterministicTagTableExport(string xml)` in `TagOperationSafetySnapshotBuilder` only if current repository evidence proves one specific unavoidable non-semantic field remains after the timestamp-free export option is used.
- Add: `internal static DeleteTagTableSafetySnapshotInfo BuildDeleteTagTableSnapshot(...);`
- Add worker methods:
  `read_create_tag_table_safety_snapshot`,
  `read_delete_tag_table_safety_snapshot`,
  `read_create_tag_safety_snapshot`,
  `read_update_tag_safety_snapshot`,
  `read_delete_tag_safety_snapshot`,
  `read_create_user_constant_safety_snapshot`,
  `read_update_user_constant_safety_snapshot`,
  `read_delete_user_constant_safety_snapshot`.
- Add client calls:
  `ReadCreateTagTableSafetySnapshotAsync(...)`,
  `ReadDeleteTagTableSafetySnapshotAsync(...)`,
  `ReadCreateTagSafetySnapshotAsync(...)`,
  `ReadUpdateTagSafetySnapshotAsync(...)`,
  `ReadDeleteTagSafetySnapshotAsync(...)`,
  `ReadCreateUserConstantSafetySnapshotAsync(...)`,
  `ReadUpdateUserConstantSafetySnapshotAsync(...)`,
  `ReadDeleteUserConstantSafetySnapshotAsync(...)`.

- [ ] **Step 1: Verify the real tag-table export overload and document-info option before hardcoding it**

Run these checks in order:

```powershell
rg -n "PlcTagTable|DocumentInfoOptions|ExportOptions" ref TiaMcpServer.OpennessWorker
```

If the repository stubs do not expose the three-parameter overload or the exact document-info enum/member, inspect the installed V21 API on the implementation machine before writing the reader test:

```powershell
pwsh -NoProfile -Command "& {
  $tiaDir = 'C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48'
  if (-not (Test-Path $tiaDir)) { throw 'Installed TIA Portal V21 PublicAPI not found. Stop and verify the approved local Siemens package before continuing.' }
  Get-ChildItem $tiaDir -Filter 'Siemens.Engineering*.dll' | ForEach-Object { try { Add-Type -Path $_.FullName -ErrorAction Stop } catch { } }
  [AppDomain]::CurrentDomain.GetAssemblies()
    | ForEach-Object {
        try { $_.GetTypes() } catch [System.Reflection.ReflectionTypeLoadException] { $_.Types | Where-Object { $_ } }
      }
    | Where-Object { $_.FullName -like '*DocumentInfoOptions' -or $_.FullName -like '*PlcTagTable' }
    | ForEach-Object {
        if ($_.Name -eq 'PlcTagTable') {
          $_.GetMethods() | Where-Object Name -eq 'Export' | ForEach-Object ToString
        }
        elseif ($_.IsEnum) {
          $_.FullName
          [Enum]::GetNames($_)
        }
      }
}"
```

Record the exact three-parameter `PlcTagTable.Export(FileInfo, ExportOptions, ...)` signature and the exact document-info enum type/member that suppresses document-info timestamps before coding the reader. If neither repository evidence nor installed V21 inspection can prove the option, do not hardcode `DocumentInfoOptions.None`; plan only a bounded allowlisted normalization fallback backed by observed live export drift.

- [ ] **Step 2: Add failing builder, policy, and source-contract tests**

Create `TagOperationSafetyBuilderTests.cs`:

```csharp
namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyBuilderTests
{
    [Fact]
    public void DeleteTagTableSnapshot_UsesTheVerifiedTimestampFreeExportCallFirst()
    {
        var text = File.ReadAllText(Source("TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotReader.cs"));

        Assert.Contains("ExportOptions.None", text, StringComparison.Ordinal);
        Assert.Contains(".Export(new FileInfo(", text, StringComparison.Ordinal);
    }

    private static string Source(string relative)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative));
}
```

Create `TagOperationSafetyWorkerSourceContractTests.cs`:

```csharp
namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyWorkerSourceContractTests
{
    [Fact]
    public void Program_DispatchesEveryInternalTagSafetyRead()
    {
        var text = File.ReadAllText(Source("TiaMcpServer.OpennessWorker/Program.cs"));

        Assert.Contains("\"read_create_tag_table_safety_snapshot\" => ReadCreateTagTableSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_delete_tag_table_safety_snapshot\" => ReadDeleteTagTableSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_create_tag_safety_snapshot\" => ReadCreateTagSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_update_tag_safety_snapshot\" => ReadUpdateTagSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_delete_tag_safety_snapshot\" => ReadDeleteTagSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_create_user_constant_safety_snapshot\" => ReadCreateUserConstantSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_update_user_constant_safety_snapshot\" => ReadUpdateUserConstantSafetySnapshot(request)", text, StringComparison.Ordinal);
        Assert.Contains("\"read_delete_user_constant_safety_snapshot\" => ReadDeleteUserConstantSafetySnapshot(request)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OperationPolicyCatalog_RegistersTagSafetyReadersAsSafetyReads()
    {
        var text = File.ReadAllText(Source("TiaMcpServer.Contracts/OperationPolicyCatalog.cs"));

        Assert.Contains("[\"read_create_tag_table_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_delete_tag_table_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_create_tag_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_update_tag_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_delete_tag_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_create_user_constant_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_update_user_constant_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
        Assert.Contains("[\"read_delete_user_constant_safety_snapshot\"] = OperationCapability.SafetyRead", text, StringComparison.Ordinal);
    }

    private static string Source(string relative)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relative));
}
```

Create `TagOperationSafetyReadPolicyTests.cs`:

```csharp
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Tests.Safety;

public sealed class TagOperationSafetyReadPolicyTests
{
    [Theory]
    [InlineData("read_create_tag_table_safety_snapshot")]
    [InlineData("read_delete_tag_table_safety_snapshot")]
    [InlineData("read_create_tag_safety_snapshot")]
    [InlineData("read_update_tag_safety_snapshot")]
    [InlineData("read_delete_tag_safety_snapshot")]
    [InlineData("read_create_user_constant_safety_snapshot")]
    [InlineData("read_update_user_constant_safety_snapshot")]
    [InlineData("read_delete_user_constant_safety_snapshot")]
    public void EveryTagSafetyReader_UsesTheSafetyReadIdentityBoundPolicy(string operation)
    {
        Assert.Equal(OperationCapability.SafetyRead, OperationPolicyCatalog.GetCapability(operation));
        Assert.True(OperationPolicyCatalog.RequiresExpectedSessionIdentity(operation));
        Assert.True(OperationPolicyCatalog.IsAllowed(McpAccessMode.ReadOnly, operation));
    }
}
```

Create `TagOperationSafetyClientIdentityTests.cs` using the same request-inspection style the repo already uses for worker-field forwarding tests:

```csharp
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Worker;

public sealed class TagOperationSafetyClientIdentityTests
{
    [Theory]
    [InlineData("create_tag_table")]
    [InlineData("delete_tag_table")]
    [InlineData("create_tag")]
    [InlineData("update_tag")]
    [InlineData("delete_tag")]
    [InlineData("create_user_constant")]
    [InlineData("update_user_constant")]
    [InlineData("delete_user_constant")]
    public async Task EveryTagSafetyClientMethod_SendsExpectedSessionIdentityInTheWorkerRequest(string selectorKind)
    {
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.BindVerified(
            new WorkerSessionIdentity
            {
                WorkerSessionId = "worker-a",
                SessionGeneration = 7,
                PortalProcessId = 4242,
                ProjectPath = @"C:\FakeWorker\tag-safety-request-echo.ap21"
            },
            forceRebind: false,
            out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate(),
            accessPolicy: new OperationAccessPolicy(McpAccessMode.ReadWrite));

        var result = selectorKind switch
        {
            "create_tag_table" => await client.ReadCreateTagTableSafetySnapshotAsync("PLC_1", "Inputs", null, @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "delete_tag_table" => await client.ReadDeleteTagTableSafetySnapshotAsync("PLC_1", "Inputs", null, @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "create_tag" => await client.ReadCreateTagSafetySnapshotAsync("PLC_1", "Inputs", null, "Start", "Bool", "%I0.0", @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "update_tag" => await client.ReadUpdateTagSafetySnapshotAsync("PLC_1", "Inputs", null, "Start", "Start_1", "%I0.1", @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "delete_tag" => await client.ReadDeleteTagSafetySnapshotAsync("PLC_1", "Inputs", null, "Start", @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "create_user_constant" => await client.ReadCreateUserConstantSafetySnapshotAsync("PLC_1", "Inputs", null, "DebounceMs", @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "update_user_constant" => await client.ReadUpdateUserConstantSafetySnapshotAsync("PLC_1", "Inputs", null, "DebounceMs", @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            "delete_user_constant" => await client.ReadDeleteUserConstantSafetySnapshotAsync("PLC_1", "Inputs", null, "DebounceMs", @"C:\FakeWorker\tag-safety-request-echo.ap21"),
            _ => throw new ArgumentOutOfRangeException(nameof(selectorKind))
        };

        using var document = JsonDocument.Parse(result.Payload);
        var expected = document.RootElement.GetProperty("expectedSessionIdentity");

        Assert.Equal("worker-a", expected.GetProperty("workerSessionId").GetString());
        Assert.Equal(7, expected.GetProperty("sessionGeneration").GetInt32());
        Assert.Equal(4242, expected.GetProperty("portalProcessId").GetInt32());
        Assert.Equal(
            @"C:\FakeWorker\tag-safety-request-echo.ap21",
            expected.GetProperty("projectPath").GetString());
    }
}
```

Create `TagOperationSafetyIdentityEnforcementTests.cs` using the existing `PersistentWorkerTransport` pattern:

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Worker;

public sealed class TagOperationSafetyIdentityEnforcementTests
{
    [Theory]
    [InlineData("read_create_tag_table_safety_snapshot")]
    [InlineData("read_delete_tag_table_safety_snapshot")]
    [InlineData("read_create_tag_safety_snapshot")]
    [InlineData("read_update_tag_safety_snapshot")]
    [InlineData("read_delete_tag_safety_snapshot")]
    [InlineData("read_create_user_constant_safety_snapshot")]
    [InlineData("read_update_user_constant_safety_snapshot")]
    [InlineData("read_delete_user_constant_safety_snapshot")]
    public async Task EveryTagSafetyReader_RejectsMissingExpectedSessionIdentity(string method)
    {
        using var transport = new PersistentWorkerTransport(FakeWorkerLocator.Locate(), TimeSpan.FromSeconds(5));
        var observed = await transport.SendAsync(new WorkerRequest
        {
            Method = "read_hardware_config",
            ProjectPath = "tag-safety-request-echo"
        });

        Assert.True(observed.Success, observed.Error);

        var response = await transport.SendAsync(new WorkerRequest
        {
            Method = method,
            ProjectPath = "tag-safety-request-echo"
        });

        Assert.False(response.Success);
        Assert.Equal(WorkerFailureCategories.BindingConflict, response.FailureCategory);
    }
}
```

- [ ] **Step 3: Run the focused tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationSafetyBuilderTests|FullyQualifiedName~TagOperationSafetyWorkerSourceContractTests|FullyQualifiedName~TagOperationSafetyReadPolicyTests|FullyQualifiedName~TagOperationSafetyClientIdentityTests|FullyQualifiedName~TagOperationSafetyIdentityEnforcementTests"
```

Expected RED:

- the reader does not yet make the verified three-parameter `PlcTagTable.Export(...)` call selected from Step 1;
- the eight safety-read worker methods do not yet exist;
- the catalog does not yet route these methods through the existing `SafetyRead` capability/policy from PR 3;
- the client does not yet expose and verify the eight safety-read methods through the existing bound-request seam with a focused request-JSON regression proving `ExpectedSessionIdentity`;
- the worker still allows missing identity for methods that are presently classified as ordinary `Observe`.

- [ ] **Step 4: Implement the pure snapshot builder without inventing XML surgery as the primary mechanism**

`TagOperationSafetySnapshotBuilder.cs` must stay Siemens-free so the test project can compile it directly. Give it deterministic helpers for canonical path normalization, collision ordering, and exact typed snapshot shaping. Do not make broad XML attribute deletion the primary determinism mechanism.

Use the exact Step 1-verified three-parameter export overload and the exact Step 1-verified “no document info / timestamp-free” enum member. If, and only if, current repository evidence after that verified export call proves one unavoidable non-semantic field still drifts, add `PreserveDeterministicTagTableExport(string xml)` with an exact allowlist for that one field and cover it with a focused test plus live proof. Do not plan a generic `GeneratedOn`/`ModifiedDate` scrubber.

- [ ] **Step 5: Implement the Siemens-dependent reader, the guarded policy, the bound client methods, and worker dispatch**

In `TagOperationSafetySnapshotReader.cs`, resolve exact targets through Siemens objects and produce the typed records from Task 1. Follow these rules:

- `create_tag_table`: resolve the exact PLC and destination folder, then traverse that PLC's complete tag-table hierarchy and return only occupancy probes for the requested table name, retaining each matching table's folder identity. Traversal failures must propagate even after a matching table is found.
- `delete_tag_table`: resolve the exact table, export the full table through the Step 1-verified `PlcTagTable.Export(FileInfo, ExportOptions, ...)` overload with the Step 1-verified timestamp-free/document-info-free option, preserve that full Simatic ML content byte-for-byte unless current evidence proves one specific unavoidable non-semantic field remains, hash the exact preserved content, and return that content plus counts.
- `create_tag`: resolve the exact table; probe the requested effective name across tags, user constants, and blocks in the selected PLC's unqualified CPU namespace, and probe the logical address against tags only.
- `update_tag`: resolve the exact current target tag, preserve the PR 3 flag fields, and probe the effective requested rename/address with the same cross-kind name and tag-only address scope. Target marking requires exact kind and identity.
- `delete_tag`: bind the exact current target tag and table identity only.
- `create/update user constant`: resolve the exact table and current constant for update, and probe only the requested/current name across tags, user constants, and ordinary program blocks including nested user/system groups. Preserve each candidate's actual kind and exact identity. Do not add rename semantics to `update_user_constant`.
- `delete_user_constant`: bind only the exact current constant and table identity, without collision probes.

Consume the existing PR 3 `SafetyRead` policy in `OperationPolicyCatalog.cs` so these eight internal methods are not ordinary `Observe` reads:

- `GetCapability("read_*_safety_snapshot") == OperationCapability.SafetyRead`
- `RequiresExpectedSessionIdentity("read_*_safety_snapshot") == true`
- `IsAllowed(McpAccessMode.ReadOnly, "read_*_safety_snapshot") == true`

Implement the eight client methods in `OpennessWorkerClient.cs` on top of the existing bound request path, which already runs under the serialized binding gate and already sets `ExpectedSessionIdentity = bindingBeforeCall.ToWorkerIdentity()` for bound requests regardless of `Observe` capability:

```csharp
public Task<WorkerCallResult> ReadDeleteTagSafetySnapshotAsync(
    string? plcName,
    string tableName,
    string? folderPath,
    string name,
    string? projectPath)
    => SendBoundProjectRequestAsync(
        "read_delete_tag_safety_snapshot",
        projectPath,
        request =>
        {
            request.PlcName = plcName;
            request.TableName = tableName;
            request.FolderPath = folderPath;
            request.Name = name;
        },
        "{}");
```

The task's obligation is to prove that all eight safety snapshot methods actually use that existing seam, that the serialized request includes `expectedSessionIdentity`, and that the worker rejects every missing-identity request before scenario dispatch. Do not invent a duplicate client transport helper; instead change the policy so these methods are guarded reads, not ordinary `Observe`.

- [ ] **Step 6: Run focused tests, then build the worker stub path**

Run the Step 3 test command again.

Then run:

```powershell
dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj --no-restore -m:1 --disable-build-servers --nologo -p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationSafetyBuilderTests|FullyQualifiedName~TagOperationSafetyWorkerSourceContractTests|FullyQualifiedName~TagOperationSafetyReadPolicyTests|FullyQualifiedName~TagOperationSafetyClientIdentityTests|FullyQualifiedName~TagOperationSafetyIdentityEnforcementTests"
```

Expected GREEN: the source contract sees the verified export call and worker dispatch, the policy test proves reuse of the PR 3 `SafetyRead` identity-bound classification, the client request-JSON test proves `ExpectedSessionIdentity` is present on every safety snapshot request, the worker rejection test proves missing identity fails before dispatch, and the net48 worker compiles with stubs.

- [ ] **Step 7: Review checkpoint**

Confirm:

- the internal worker methods are not added to `BatchOperationCatalog`;
- every internal worker method uses the existing PR 3 `SafetyRead` policy rather than ordinary `Observe`;
- delete-table export determinism comes from the verified TIA export options first, with allowlisted normalization only if live evidence proves one unavoidable residual field;
- every safety snapshot request carries the verified `ExpectedSessionIdentity` under the serialized/pinned binding gate, and the worker rejects missing identity before dispatch;
- the worker, not the host, resolves exact tag/table/constant identity.

Suggested commit if separately authorized:

```bash
git add TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotBuilder.cs TiaMcpServer.OpennessWorker/Openness/TagOperationSafetySnapshotReader.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Contracts/OperationPolicyCatalog.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/Batch/TagOperationSafetyWorkerSourceContractTests.cs TiaMcpServer.Tests/Batch/TagOperationSafetyBuilderTests.cs TiaMcpServer.Tests/Safety/TagOperationSafetyReadPolicyTests.cs TiaMcpServer.Tests/Worker/TagOperationSafetyClientIdentityTests.cs TiaMcpServer.Tests/Worker/TagOperationSafetyIdentityEnforcementTests.cs
git commit -m "feat(worker): add exact tag safety snapshot readers"
```

---

### Task 3: Replace Broad Tag State Reads with Exact Per-Operation Reads and Within-Phase Dedup

**Files:**
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs`
- Modify: `TiaMcpServer/Batch/WriteBatchTools.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/Batch/TagOperationCurrentStateReadFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consume: `TagOperationSafetySnapshotContract.Decode(string operation, string payload)`
- Consume: `TagOperationSafetySelector.TryBuild(BatchOperationRequest op, out TagOperationSafetySelectorKey key)`
- Preserve: `OperationBatchStateComposer.CombineCurrentState(IReadOnlyList<OperationBatchCurrentState>)`
- Preserve: `WriteBatchTools.ReadCombinedCurrentStateAsync(OpennessWorkerClient workerClient, BatchOperationRequest[] operations)`
- Prove through the registered path: `WriteBatchTools.PreviewWriteBatch(...)` remains the preview entrypoint whose current-state read lives behind the new dedup logic.
- Change inside `BatchWorkerInvoker.ReadCurrentStateAsync(...)`:
  tag operations no longer call `ListTagTablesAsync(op.PlcName, op.ProjectPath)`.

- [ ] **Step 1: Add failing registered-path routing and dedup regressions**

Create `TagOperationCurrentStateReadFakeWorkerTests.cs` so the RED happens through the registered `WriteBatchTools` path, not only through pure helpers:

```csharp
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationCurrentStateReadFakeWorkerTests
{
    [Fact]
    public async Task PreviewWriteBatch_DeleteTag_UsesExactInternalSnapshotRouteInsteadOfListTagTables()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-route-proof.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "d1",
                Operation = "delete_tag",
                ProjectPath = @"C:\FakeWorker\tag-safety-route-proof.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "Start"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Contains("\"safetyToken\":", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("wrong route: list_tag_tables", preview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PreviewWriteBatch_TwoIdenticalDeleteTagSelectors_PerformsOneSnapshotReadAndStillDescribesBothOperations()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-dedup-proof.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "d1",
                Operation = "delete_tag",
                ProjectPath = @"C:\FakeWorker\tag-safety-dedup-proof.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "Start"
            },
            new BatchOperationRequest
            {
                OperationId = "d2",
                Operation = "delete_tag",
                ProjectPath = @"C:\FakeWorker\tag-safety-dedup-proof.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "Start"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);

        Assert.Contains("\"safetyToken\":", preview, StringComparison.Ordinal);
        Assert.Contains("Delete PLC tag 'Start' from table 'Inputs'.", preview, StringComparison.Ordinal);
        Assert.Equal(
            2,
            Regex.Matches(
                preview,
                Regex.Escape("Delete PLC tag 'Start' from table 'Inputs'."),
                RegexOptions.CultureInvariant).Count);
    }

    [Fact]
    public void StateComposer_CanRepeatIdenticalCurrentStateForDifferentOperationsWithoutLosingOrder()
    {
        var combined = OperationBatchStateComposer.CombineCurrentState(new[]
        {
            new OperationBatchCurrentState("u1", "update_tag", "{\"k\":1}"),
            new OperationBatchCurrentState("d1", "delete_tag", "{\"k\":1}")
        });

        Assert.Contains("u1::update_tag", combined, StringComparison.Ordinal);
        Assert.Contains("d1::delete_tag", combined, StringComparison.Ordinal);
        Assert.True(
            combined.IndexOf("u1::update_tag", StringComparison.Ordinal) <
            combined.IndexOf("d1::delete_tag", StringComparison.Ordinal));
    }

    [Fact]
    public void TokenValidation_TreatsDuplicatedCurrentStateBodiesAsDistinctWhenOperationIdentityDiffers()
    {
        using var audit = new TempAuditDirectory();
        var service = CreateService(audit.Path);
        var operations = new[]
        {
            Op("u1", "update_tag"),
            Op("d1", "delete_tag")
        };
        var sharedState = new[]
        {
            new OperationBatchCurrentState("u1", "update_tag", "{\"snapshot\":1}"),
            new OperationBatchCurrentState("d1", "delete_tag", "{\"snapshot\":1}")
        };

        var token = CreatePreview(service, operations, sharedState);
        var result = Consume(service, token, operations, sharedState);

        Assert.True(result.IsValid, result.Error);
    }
}
```

The first two tests are the actual Task 3 RED gates: today the broad `ListTagTablesAsync` route and lack of within-phase dedup should make them fail. The last two tests are characterization baselines that protect ordered state composition after the route/dedup fix lands; do not use them as the primary RED evidence for this task.

- [ ] **Step 2: Run only the registered-path RED tests and verify failure**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests.PreviewWriteBatch_DeleteTag_UsesExactInternalSnapshotRouteInsteadOfListTagTables|FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests.PreviewWriteBatch_TwoIdenticalDeleteTagSelectors_PerformsOneSnapshotReadAndStillDescribesBothOperations"
```

Expected RED:

- `PreviewWriteBatch_DeleteTag_UsesExactInternalSnapshotRouteInsteadOfListTagTables` fails because the current host path still asks FakeWorker for `list_tag_tables` rather than `read_delete_tag_safety_snapshot`.
- `PreviewWriteBatch_TwoIdenticalDeleteTagSelectors_PerformsOneSnapshotReadAndStillDescribesBothOperations` fails because the current preview path performs two identical snapshot reads in one phase instead of one deduplicated read.

- [ ] **Step 3: Add the FakeWorker route-proof and switch `BatchWorkerInvoker` to exact snapshot reads**

In `TiaMcpServer.FakeWorker/Program.cs`, add two deterministic scenarios before changing the host:

- `tag-safety-route-proof`: `list_tag_tables` returns `wrong route: list_tag_tables`, while `read_delete_tag_safety_snapshot` returns a valid `DeleteTagSafetySnapshotInfo` payload.
- `tag-safety-dedup-proof`: the first `read_delete_tag_safety_snapshot` returns a valid payload, and the second identical read in the same preview returns `dedup missing: repeated read_delete_tag_safety_snapshot`.

Then replace the tag-operation arm in `ReadCurrentStateAsync` with exact worker calls:

```csharp
"create_tag_table" => client.ReadCreateTagTableSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.ProjectPath),
"delete_tag_table" => client.ReadDeleteTagTableSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.ProjectPath),
"create_tag" => client.ReadCreateTagSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.LogicalAddress, op.ProjectPath),
"update_tag" => client.ReadUpdateTagSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.NewName, op.LogicalAddress, op.ProjectPath),
"delete_tag" => client.ReadDeleteTagSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
"create_user_constant" => client.ReadCreateUserConstantSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
"update_user_constant" => client.ReadUpdateUserConstantSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
"delete_user_constant" => client.ReadDeleteUserConstantSafetySnapshotAsync(
    op.PlcName, op.TableName!, op.FolderPath, op.Name!, op.ProjectPath),
```

Each success payload must pass through `TagOperationSafetySnapshotContract.Decode` before the current-state string is returned to the caller. A malformed typed payload becomes the worker call's failure result with `protocol_error`.

- [ ] **Step 4: Add the within-phase dedup map in `WriteBatchTools.ReadCombinedCurrentStateAsync`**

Inside `ReadCombinedCurrentStateAsync`, instantiate an ephemeral dictionary keyed by `TagOperationSafetySelectorKey`:

```csharp
var dedup = new Dictionary<TagOperationSafetySelectorKey, Task<WorkerCallResult>>();
var states = new List<OperationBatchCurrentState>(operations.Length);

foreach (var op in operations)
{
    WorkerCallResult state;
    if (TagOperationSafetySelector.TryBuild(op, out var key))
    {
        if (!dedup.TryGetValue(key, out var task))
        {
            task = BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op);
            dedup.Add(key, task);
        }

        state = await task.ConfigureAwait(false);
    }
    else
    {
        state = await BatchWorkerInvoker.ReadCurrentStateAsync(workerClient, op).ConfigureAwait(false);
    }

    if (!state.Success)
    {
        return (string.Empty, $"Could not read current state for operationId '{op.OperationId}' ({op.Operation}). Error: {state.Error}");
    }

    states.Add(new OperationBatchCurrentState(op.OperationId, op.Operation, state.Payload));
}
```

Do not move the dedup map to a field or service. A local variable is the whole proof that no cross-phase cache exists.

- [ ] **Step 5: Run the route/dedup tests to GREEN, then run the characterization baselines and adjacent regressions**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests.PreviewWriteBatch_DeleteTag_UsesExactInternalSnapshotRouteInsteadOfListTagTables|FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests.PreviewWriteBatch_TwoIdenticalDeleteTagSelectors_PerformsOneSnapshotReadAndStillDescribesBothOperations"
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests.StateComposer_CanRepeatIdenticalCurrentStateForDifferentOperationsWithoutLosingOrder|FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests.TokenValidation_TreatsDuplicatedCurrentStateBodiesAsDistinctWhenOperationIdentityDiffers|FullyQualifiedName~BatchToolsTests"
```

Expected GREEN: the registered preview path uses the exact internal worker method, identical selector keys are read once per phase, the preview still describes both ordered operations, and the characterization baselines confirm that duplicated state payloads do not collapse operation identity.

- [ ] **Step 6: Review checkpoint**

Confirm:

- no tag-related arm in `BatchWorkerInvoker.ReadCurrentStateAsync` still calls `ListTagTablesAsync`;
- the dedup map is local to one `ReadCombinedCurrentStateAsync` invocation;
- the route/dedup REDs were proven through `WriteBatchTools.PreviewWriteBatch`, not only through pure helper tests;
- reused payloads still expand to one ordered `OperationBatchCurrentState` per operation;
- non-tag batch operations are unchanged.

Suggested commit if separately authorized:

```bash
git add TiaMcpServer/Batch/BatchWorkerInvoker.cs TiaMcpServer/Batch/WriteBatchTools.cs TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/Batch/TagOperationCurrentStateReadFakeWorkerTests.cs
git commit -m "feat(batch): narrow tag safety current-state reads"
```

---

### Task 4: Prove Drift Detection, Sibling Tolerance, and Re-Read Semantics with FakeWorker

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/Batch/TagOperationFakeWorkerTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consume unchanged: `WriteBatchTools.PreviewWriteBatch(...)`
- Consume unchanged: `WriteBatchTools.ApplyWriteBatch(...)`
- Preserve unchanged public tool semantics: preview returns token, apply requires `confirm=true` and unchanged operations.
- Extend the Task 3 route/dedup FakeWorker support with deterministic drift and apply scenarios keyed by project path or scenario name for:
  `tag-safety-same-object-drift`,
  `tag-safety-collision-drift`,
  `tag-safety-unrelated-sibling`,
  `tag-safety-delete-table-export-drift`,
  `tag-safety-reread`,
  `tag-safety-authorized-apply`.

- [ ] **Step 1: Add failing end-to-end FakeWorker tests**

Create `TagOperationFakeWorkerTests.cs`:

```csharp
using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationFakeWorkerTests
{
    [Fact]
    public async Task ApplyWriteBatch_UpdateTag_SameObjectDriftFailsWithStateChanged()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-same-object-drift.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "u1",
                Operation = "update_tag",
                ProjectPath = @"C:\FakeWorker\tag-safety-same-object-drift.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "Start",
                NewName = "Start_1"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var token = ExtractToken(preview);
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: token);

        Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWriteBatch_CreateTag_RelevantCollisionDriftFailsWithStateChanged()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-collision-drift.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "c1",
                Operation = "create_tag",
                ProjectPath = @"C:\FakeWorker\tag-safety-collision-drift.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "Start_1",
                DataType = "Bool",
                LogicalAddress = "%I0.1"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: ExtractToken(preview));

        Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWriteBatch_DeleteTag_IgnoresUnrelatedSiblingTableDrift()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-unrelated-sibling.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "d1",
                Operation = "delete_tag",
                ProjectPath = @"C:\FakeWorker\tag-safety-unrelated-sibling.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "Start"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: ExtractToken(preview));

        Assert.Contains("\"status\":\"succeeded\"", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWriteBatch_DeleteTagTable_ExportDriftFailsWithStateChanged()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-delete-table-export-drift.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "t1",
                Operation = "delete_tag_table",
                ProjectPath = @"C:\FakeWorker\tag-safety-delete-table-export-drift.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: ExtractToken(preview));

        Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApplyWriteBatch_CreateUserConstant_ReReadsOnApplyInsteadOfReusingPreviewCache()
    {
        using var audit = new TempAuditDirectory();
        var binding = VerifiedBinding(@"C:\FakeWorker\tag-safety-reread.ap21");
        using var client = CreateClient(binding);
        var safety = CreateSafety(audit, binding);
        var operations = new[]
        {
            new BatchOperationRequest
            {
                OperationId = "uc1",
                Operation = "create_user_constant",
                ProjectPath = @"C:\FakeWorker\tag-safety-reread.ap21",
                PlcName = "PLC_1",
                TableName = "Inputs",
                Name = "DebounceMs",
                DataType = "Int",
                Value = "50"
            }
        };

        var preview = await WriteBatchTools.PreviewWriteBatch(client, safety, operations);
        var apply = await WriteBatchTools.ApplyWriteBatch(client, safety, operations, confirm: true, safetyToken: ExtractToken(preview));

        Assert.Contains("\"failureCategory\":\"state_changed\"", apply, StringComparison.Ordinal);
    }
}
```

The `tag-safety-reread` scenario must return a different exact snapshot on the apply-phase read before any mutation attempt. That is the offline proof that PR 5 did not keep a cross-phase cache.

- [ ] **Step 2: Run the FakeWorker tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationFakeWorkerTests"
```

Expected RED: the FakeWorker cannot yet serve the exact snapshot reader methods or the host still hashes the broad list-tag-table state.
Expected RED after Task 3 is green: the exact route and within-phase dedup already work, but these scenario-specific drift/apply proofs do not exist yet, so preview/apply outcomes do not yet show the required `state_changed` or sibling-tolerance behavior.

- [ ] **Step 3: Add deterministic FakeWorker scenarios for the new internal methods**

Implement one deterministic snapshot payload per new worker method. Use exact typed JSON that matches Task 1's shared records. The scenario rules are:

- `tag-safety-same-object-drift`: preview returns one `UpdateTagSafetySnapshotInfo`; apply returns the same selector with a changed target tag field.
- `tag-safety-collision-drift`: preview sees no collision; apply sees a relevant name or address collision.
- `tag-safety-unrelated-sibling`: preview/apply keep the target table/tag snapshot identical while changing a different sibling table that the old broad `list_tag_tables` path would have noticed.
- `tag-safety-delete-table-export-drift`: preview/apply differ only in the normalized exported Simatic ML string.
- `tag-safety-reread`: preview and apply return different snapshots for the same selector before any mutation executes.
- `tag-safety-authorized-apply`: preview/apply keep the same snapshot so one tag or user-constant operation succeeds once and the replayed token still fails on the second apply attempt.

Do not change existing non-tag FakeWorker scenarios.

- [ ] **Step 4: Run the new integration tests and adjacent batch regressions**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationFakeWorkerTests|FullyQualifiedName~BatchSafetyTokenTests|FullyQualifiedName~BatchToolsTests"
```

Expected GREEN: same-object drift and relevant collision drift fail with `state_changed`, unrelated sibling drift no longer causes a false invalidation, delete-table export drift fails, and apply re-reads instead of reusing preview state.

- [ ] **Step 5: Run the full serial offline suite**

Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers --nologo -p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo
```

Record exact totals. If any tag-safety test fails only on the old broad-selector assumptions, fix that regression inside PR 5. Do not expand into PR 6 tree-scope work.

- [ ] **Step 6: Review checkpoint**

Confirm:

- FakeWorker now serves the exact internal tag snapshot methods;
- same-object drift and relevant collision drift invalidate the token;
- unrelated sibling-table drift no longer invalidates a target whose proven preconditions do not include that sibling;
- apply re-reads before mutation and does not reuse preview-phase state.

Suggested commit if separately authorized:

```bash
git add TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/Batch/TagOperationFakeWorkerTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "test(batch): verify tag safety scope behavior"
```

---

### Task 5: Add the Guarded Live Harness and Update Current Documentation

**Files:**
- Create: `scripts/live-test-tag-operation-safety-scopes.ps1`
- Create: `TiaMcpServer.Tests/Batch/TagOperationSafetyLiveHarnessContractTests.cs`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`
- Modify: `docs/IMPROVEMENT_LOG.md`
- Modify: `docs/README.md`
- Modify: `docs/superpowers/README.md`
- Create after the authorized run: `docs/superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md`

**Interfaces:**
- Harness default mode: non-mutating preview/read checks only.
- Explicit mutation modes:
  `-Mode DriftAndRestore` for same-object drift, collision drift, and unrelated sibling tolerance;
  `-Mode ApplyAndRestore` for one successful explicitly authorized apply followed by restore or discard.
- Harness must launch the host process, not the worker executable directly.
- Harness artifacts must be hygienic: PowerShell 7 + strict mode, dedicated artifact directory, token redaction in persisted evidence, and `finally` cleanup of host/transient files.
- Harness stdout reads must use `StandardOutput.ReadLineAsync()`, never blocking `ReadLine()`, and must recompute the real per-iteration remaining timeout against a deadline.

- [ ] **Step 1: Add failing live-harness contract tests**

Create `TagOperationSafetyLiveHarnessContractTests.cs`:

```csharp
using System.Text.RegularExpressions;

namespace TiaMcpServer.Tests.Batch;

public sealed class TagOperationSafetyLiveHarnessContractTests
{
    [Fact]
    public void Script_IsPresent_RequiresPowerShell7AndStrictMode_AndDefaultsToNonMutatingMode()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Matches(new Regex(@"^\s*#Requires\s+-Version\s+7(\.\d+)?\s*$", RegexOptions.Multiline), text);
        Assert.Contains("Set-StrictMode -Version Latest", text, StringComparison.Ordinal);
        Assert.Contains("$ErrorActionPreference = 'Stop'", text, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('PreviewOnly','DriftAndRestore','ApplyAndRestore')]", text, StringComparison.Ordinal);
        Assert.Contains("$Mode = 'PreviewOnly'", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesTheHostRequiresExplicitMutationModeAndNeverRunsFromOrdinaryTests()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("TiaMcpServer", text, StringComparison.Ordinal);
        Assert.Contains("if ($Mode -eq 'ApplyAndRestore')", text, StringComparison.Ordinal);
        Assert.Contains("disposable project copy", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OpennessWorker.exe", text, StringComparison.Ordinal);

        var testDirectory = Path.Combine(GetRepositoryRoot(), "TiaMcpServer.Tests");
        var thisFile = Path.Combine(testDirectory, "Batch", "TagOperationSafetyLiveHarnessContractTests.cs");
        var offendingFiles = Directory
            .EnumerateFiles(testDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFullPath(path), Path.GetFullPath(thisFile), StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("live-test-tag-operation-safety-scopes.ps1", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(offendingFiles);
    }

    [Fact]
    public void Script_UsesFinallyCleanupArtifactHygieneAndTokenRedaction()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("try {", text, StringComparison.Ordinal);
        Assert.Contains("finally {", text, StringComparison.Ordinal);
        Assert.Contains("Stop-McpHost", text, StringComparison.Ordinal);
        Assert.Contains("artifact", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("failure.json", text, StringComparison.Ordinal);
        Assert.Contains("Redact-SafetyToken", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Host \"safetyToken:", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_UsesAsyncStdoutReadsBoundedByTheRemainingDeadline()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.DoesNotContain("StandardOutput.ReadLine()", text, StringComparison.Ordinal);
        Assert.Contains("StandardOutput.ReadLineAsync()", text, StringComparison.Ordinal);
        Assert.Contains("$remaining = $deadline - (Get-Date)", text, StringComparison.Ordinal);
        Assert.Contains("$readTask.WaitAsync($remaining)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_CoversAllRequiredPr5LiveClaims()
    {
        var text = File.ReadAllText(ScriptPath);

        Assert.Contains("same-object drift", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("relevant collision", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unrelated sibling", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("restore or discard", text, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly string ScriptPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", "live-test-tag-operation-safety-scopes.ps1"));

    private static string GetRepositoryRoot()
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
}
```

- [ ] **Step 2: Run the contract tests and verify RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationSafetyLiveHarnessContractTests"
```

Expected RED: the script and its contract do not exist yet.

- [ ] **Step 3: Implement the guarded PowerShell harness**

The script must:

- require `#Requires -Version 7`, `Set-StrictMode -Version Latest`, and `$ErrorActionPreference = 'Stop'`;
- accept the disposable project copy path, PLC name, target table, sibling table, target tag, collision tag, and user constant names explicitly;
- start the host process and communicate through MCP/NDJSON like the other live harnesses;
- read MCP stdout with `StandardOutput.ReadLineAsync()` only, never `StandardOutput.ReadLine()`, and recompute `$remaining = $deadline - (Get-Date)` on each loop before awaiting the next line;
- create one dedicated artifact directory per run, persist request/response evidence there, and write a failure artifact before rethrowing if a guarded check fails;
- keep any live safety token only in memory for the apply call, and redact it from console output and persisted artifacts through a helper such as `Redact-SafetyToken`;
- in `PreviewOnly`, verify exact selector previews without mutation;
- in `DriftAndRestore`, perform:
  1. preview an exact-target operation,
  2. mutate the exact same object through an explicitly authorized action,
  3. prove stale-token `state_changed`,
  4. restore the project copy or discard it;
- in `DriftAndRestore`, also prove:
  1. relevant name/address collision drift invalidates,
  2. unrelated sibling-table drift does not invalidate the target operation;
- in `ApplyAndRestore`, perform one successful explicitly authorized apply with the unchanged issued token, then restore or discard the copy;
- wrap host startup, transient export files, and restore/discard cleanup in `try/finally` so the host is stopped and transient files are removed even on failure;
- write a transcript/artifact directory describing every MCP call, the mutation performed, the redacted token flow, and the final cleanup result.

Keep the default mode non-mutating.

- [ ] **Step 4: Update the current documentation**

Document the exact PR 5 behavior:

- `docs/ARCHITECTURE.md`: replace the old blanket tag-table selector description with the eight exact selector shapes, the within-phase dedup rule, and the preview/apply no-cross-phase-cache invariant.
- `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`: document that tag-related write safety now binds exact targets plus scoped collision probes rather than the full table-list payload.
- `docs/IMPROVEMENT_LOG.md`: record PR 5 as completed only after the live harness report exists; keep the deferred items visible.
- `docs/README.md` and `docs/superpowers/README.md`: add links to this implementation plan and to the acceptance report once the report exists.

Explicitly restate the deferred items in the docs:

- multilingual per-tag comment binding;
- public `list_tag_tables` completeness changes;
- broader snapshot narrowing;
- PLC `start_plc` / `stop_plc`.

- [ ] **Step 5: Run focused docs and harness checks**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationSafetyLiveHarnessContractTests"
git diff --check
git status --short
```

Expected GREEN: the contract test passes and the doc/script diff is whitespace-clean.

- [ ] **Step 6: Review checkpoint**

Confirm:

- the harness default is non-mutating;
- the mutation modes are explicit and guarded;
- the static contract now enforces PowerShell 7, strict mode, `finally` cleanup, artifact hygiene, token redaction, and "ordinary tests never invoke it";
- the docs say offline/FakeWorker evidence is insufficient;
- the deferred items remain explicitly out of scope.

Suggested commit if separately authorized:

```bash
git add scripts/live-test-tag-operation-safety-scopes.ps1 TiaMcpServer.Tests/Batch/TagOperationSafetyLiveHarnessContractTests.cs docs/ARCHITECTURE.md docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md docs/IMPROVEMENT_LOG.md docs/README.md docs/superpowers/README.md
git commit -m "docs(batch): document tag safety scopes"
```

---

### Task 6: Run the Mandatory Verification Sequence and Write the Acceptance Report

**Files:**
- Review: every file changed by Tasks 1-5
- Create after the live run: `docs/superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md`

**Verification boundary:**
- Establishes: PR 2/PR 3 prerequisite presence, shared contract correctness, guarded safety-read identity policy, exact worker dispatch, host dedup behavior, state-hash drift sensitivity, unrelated sibling tolerance, delete-table export determinism, full serial offline suite status, and guarded live V21 acceptance.
- Does not establish: multilingual tag-comment drift detection, public `list_tag_tables` completeness changes, broader tree-scope narrowing, PLC run/stop control, or plant acceptance.

- [ ] **Step 1: Run the complete focused PR 5 offline set**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationPrerequisiteBaselineTests|FullyQualifiedName~TagOperationSafetySnapshotContractTests|FullyQualifiedName~TagOperationSafetySelectorTests|FullyQualifiedName~TagOperationSafetyBuilderTests|FullyQualifiedName~TagOperationSafetyWorkerSourceContractTests|FullyQualifiedName~TagOperationSafetyReadPolicyTests|FullyQualifiedName~TagOperationSafetyClientIdentityTests|FullyQualifiedName~TagOperationSafetyIdentityEnforcementTests|FullyQualifiedName~TagOperationFakeWorkerTests|FullyQualifiedName~BatchSafetyTokenTests|FullyQualifiedName~OperationBatchKernelTests"
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo --filter "FullyQualifiedName~TagOperationCurrentStateReadFakeWorkerTests"
```

Expected GREEN: the PR 2/PR 3 prerequisite checkpoint passes first, then every new contract, selector, guarded-policy, host, and FakeWorker regression passes.

- [ ] **Step 2: Run the full serial repository verification**

Run:

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 --disable-build-servers --nologo -p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore -m:1 --disable-build-servers --nologo
git diff --check
git status --short
```

Record the exact build/test totals and the final working-tree status.

- [ ] **Step 3: Run the guarded live harness only after explicit authorization**

When, and only when, the user explicitly authorizes the exact disposable target and cleanup strategy, run:

```powershell
pwsh -NoProfile -File .\scripts\live-test-tag-operation-safety-scopes.ps1 `
  -Mode DriftAndRestore `
  -ProjectPath "C:\Path\To\Disposable.ap21" `
  -PlcName "PLC_1" `
  -TableName "Inputs" `
  -SiblingTableName "Auxiliary" `
  -TagName "Start" `
  -CollisionTagName "Start_1" `
  -UserConstantName "DebounceMs"
```

Then run the successful apply proof:

```powershell
pwsh -NoProfile -File .\scripts\live-test-tag-operation-safety-scopes.ps1 `
  -Mode ApplyAndRestore `
  -ProjectPath "C:\Path\To\Disposable.ap21" `
  -PlcName "PLC_1" `
  -TableName "Inputs" `
  -SiblingTableName "Auxiliary" `
  -TagName "Start" `
  -CollisionTagName "Start_1" `
  -UserConstantName "DebounceMs"
```

Expected live evidence:

- same-object drift invalidates the stale token with `state_changed`;
- relevant collision drift invalidates the stale token with `state_changed`;
- unrelated sibling-table drift does not invalidate the target operation;
- one explicitly authorized apply succeeds with the unchanged issued token;
- the disposable project copy is restored to a clean state or explicitly discarded.

- [ ] **Step 4: Write the acceptance report**

Create `docs/superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md` with these sections:

```markdown
# PR 5 Tag Operation Safety Scopes Live Acceptance

## Environment
- TIA Portal version:
- Harness script path:
- Disposable project copy:
- PLC identity:
- Target table / sibling table:
- Target tag / collision tag / user constant:

## Calls Performed
- Preview-only checks:
- Drift-and-restore checks:
- Apply-and-restore checks:

## Evidence
- Same-object drift result:
- Relevant collision result:
- Unrelated sibling tolerance result:
- Successful apply result:
- Restore or discard result:

## Verification Boundary
- Proven:
- Not proven:
```

Populate every field with actual observed values. Do not leave a heading blank.

- [ ] **Step 5: Final diff review**

Inspect the whole PR 5 diff and confirm:

- no public tool schema changed;
- no tag operation still hashes the broad `list_tag_tables` payload;
- no cache crosses preview/apply;
- no internal worker method is publicly exposed;
- the deferred items are documented as deferred, not silently solved.

Suggested final commit only if separately authorized:

```bash
git add TiaMcpServer.Contracts TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests scripts docs
git commit -m "feat(batch): narrow tag operation safety scopes"
```

---

## Deferred and Out of Scope

- Multilingual per-tag comment binding remains deferred and is not solved by PR 5.
- Public `list_tag_tables` completeness or best-effort behavior changes remain deferred and out of scope.
- Broader snapshot narrowing beyond the eight PR 5 tag operations remains deferred.
- PLC `start_plc` / `stop_plc` work remains deferred.

## Completion Gate

PR 5 is complete only when all of the following are true:

- [ ] Every new task-specific RED and GREEN result is recorded.
- [ ] The full stubbed solution build passes serially.
- [ ] The full serial offline test suite passes with exact totals recorded.
- [ ] The exact tag/table/user-constant worker snapshot DTOs are in `TiaMcpServer.Contracts`.
- [ ] No tag-related `BatchWorkerInvoker.ReadCurrentStateAsync` arm still calls `ListTagTablesAsync`.
- [ ] Within-phase dedup reuses identical reads but preserves ordered per-operation state composition.
- [ ] Same-object drift and relevant collision drift are proven to invalidate the token.
- [ ] Unrelated sibling-table drift is proven not to invalidate a narrowly scoped tag operation.
- [ ] Delete-tag-table safety binds deterministic timestamp-free full-table export content.
- [ ] Apply is proven to re-read current state and never reuse preview-phase cached data.
- [ ] The guarded live V21 harness proves same-object drift invalidation, relevant collision invalidation, unrelated sibling tolerance, and one successful authorized apply with restore or discard.
- [ ] The acceptance report is written under `docs/superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md`.
- [ ] The deferred items remain explicitly deferred: multilingual per-tag comment binding, public `list_tag_tables` completeness changes, broader snapshot narrowing, and PLC `start_plc` / `stop_plc`.
- [ ] `git diff --check` passes and the final status contains only intended PR 5 changes.
- [ ] Offline and FakeWorker evidence are reported as necessary but insufficient until the live harness report exists.
