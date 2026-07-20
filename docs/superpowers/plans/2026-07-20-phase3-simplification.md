# Phase 3 Simplification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the three behavior-preserving Phase 3 cleanups — document `WorkerRequest`, consolidate the duplicated project-path binding checks, and dedupe the three identical presentation JSON option declarations.

**Architecture:** Three independent tasks, ordered lowest-risk-first. Task 1 is documentation only. Task 2 makes `ProjectSessionBinding.Bind` a thin wrapper over a new non-mutating `CanBind`, and deletes the copy of that logic in `OpennessWorkerClient`. Task 3 adds a host-only `TiaJson` holder and points three call sites at it. No task depends on another.

**Tech Stack:** C# / .NET 8 (host, tests), netstandard2.0 (`TiaMcpServer.Contracts`), net48 (worker), xunit.

## Global Constraints

- Spec: `docs/superpowers/specs/2026-07-20-phase3-simplification-design.md`. Item 3.3 is **deferred** — do not touch `BatchWorkerInvoker`, `BatchOperationCatalog`, or the `OpennessWorkerClient` batch wrapper methods.
- **Baseline: 336 tests passing.** Verified 2026-07-20 via `dotnet test TiaMcpServer.Tests`. Every task must end at 336 or more passing, 0 failing.
- Build with `-m:1`. Parallel builds race on the worker copy step: `dotnet build TiaMcpServer.sln -m:1`.
- CI builds without TIA Portal installed via `/p:UseTiaPortalReferenceStubs=true`. Nothing in this plan may introduce a dependency on real Siemens assemblies.
- **`TiaMcpServer.Contracts` has zero PackageReferences and must keep zero.** It is netstandard2.0 and has no in-box `System.Text.Json`. Do not add one.
- `TiaMcpServer.Tests` links host `.cs` files individually via `<Compile Include>` rather than a ProjectReference. **Any new file under `TiaMcpServer/` that linked test code depends on must be added to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`**, or the test project fails to compile. `TiaMcpServer.Contracts` is a normal ProjectReference and needs no such entry.
- Commit after each task. Conventional commit format (`refactor:`, `docs:`).

---

### Task 1: Document `WorkerRequest` field→operation mapping (item 3.6)

Documentation only — no logic changes. `WorkerRequest` is a flat 47-field DTO shared by host and worker; splitting it is explicitly out of scope (`IMPROVEMENT_PLAN.md`, "Deferred / explicitly not planned"). This task makes the implicit field→operation contract readable.

The mapping below was derived by reading every `SendBoundProjectRequestAsync` call site in `TiaMcpServer/Worker/OpennessWorkerClient.cs`. It reflects what the code actually forwards, not what the schema descriptions claim.

**Files:**
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs` (whole file, 98 lines)

**Interfaces:**
- Consumes: nothing.
- Produces: nothing. No public surface changes — field names, types, order, and default values are all preserved exactly. Only comments and `#region` markers are added.

- [ ] **Step 1: Replace the body of `TiaMcpServer.Contracts/WorkerRequest.cs`**

Field names, types, and initializers are unchanged. Note `Rebind`, `SaveBeforeArchive`, and `SaveBeforeClose` keep their `= true` defaults.

```csharp
namespace TiaMcpServer.Contracts;

/// <summary>
/// Flat request envelope for one host→worker call, serialized as newline-delimited JSON.
///
/// The shape is deliberately flat rather than one DTO per operation: the protocol is stable
/// and per-operation types would cost more churn than they save. See "Deferred / explicitly
/// not planned" in docs/IMPROVEMENT_PLAN.md.
///
/// <para>
/// Only the fields relevant to <see cref="Method"/> are read; everything else is ignored.
/// Regions below group fields by the operation family that reads them, and each field
/// documents the exact operations that forward it. That list is the contract — a field not
/// named for an operation is silently dropped for that operation.
/// </para>
/// </summary>
public class WorkerRequest
{
    #region Common — read by every operation

    /// <summary>Operation name, dispatched by the worker's switch in Program.cs.</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Target project path. Resolved against the session binding before sending.</summary>
    public string? ProjectPath { get; set; }

    /// <summary>
    /// Set by every write operation EXCEPT update_block_logic, which forwards only
    /// AllowTiaConfirmations. Never set by reads.
    /// </summary>
    public bool Confirm { get; set; }

    /// <summary>Set by every write operation, including update_block_logic. Never set by reads.</summary>
    public bool AllowTiaConfirmations { get; set; }

    #endregion

    #region Block operations

    /// <summary>
    /// Forwarded by: get_block_content, update_block_logic, compile_check (optional, scopes
    /// the compile to one block), create_block, delete_block, create_block_group,
    /// delete_block_group.
    /// </summary>
    public string? BlockPath { get; set; }

    /// <summary>Forwarded by: update_block_logic.</summary>
    public string? YamlContent { get; set; }

    /// <summary>Forwarded by: create_block. Valid values: FB, FC, OB, GlobalDB.</summary>
    public string? BlockType { get; set; }

    /// <summary>
    /// Forwarded by: create_block. Passed through as-is including null — the worker applies
    /// the LAD default, not the host.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Forwarded by: create_block. Passed through as-is including null — the worker applies
    /// the ProgramCycle default, not the host.
    /// </summary>
    public string? OBEventClass { get; set; }

    #endregion

    #region Tag tables, tags, and user constants

    /// <summary>
    /// Forwarded by: read_cross_references, compile_check, list_tag_tables, start_plc,
    /// stop_plc, and every tag-table, tag, and user-constant operation.
    /// </summary>
    public string? PlcName { get; set; }

    /// <summary>Forwarded by: every tag-table, tag, and user-constant operation.</summary>
    public string? TableName { get; set; }

    /// <summary>Forwarded by: every tag-table, tag, and user-constant operation.</summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// Forwarded by: create_tag, update_tag, delete_tag, create_user_constant,
    /// update_user_constant, delete_user_constant.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Forwarded by: update_tag ONLY. Not forwarded by update_user_constant, which has no
    /// rename path despite exposing a similar shape.
    /// </summary>
    public string? NewName { get; set; }

    /// <summary>
    /// Forwarded by: create_tag, update_tag, create_user_constant, update_user_constant.
    /// </summary>
    public string? DataType { get; set; }

    /// <summary>Forwarded by: create_tag, update_tag.</summary>
    public string? LogicalAddress { get; set; }

    /// <summary>Forwarded by: create_user_constant, update_user_constant.</summary>
    public string? Value { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalAccessible { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalVisible { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? ExternalWritable { get; set; }

    /// <summary>Forwarded by: update_tag ONLY. create_tag does not forward it.</summary>
    public bool? IsSafety { get; set; }

    #endregion

    #region Project tree, catalog, and cross-references

    /// <summary>Forwarded by: browse_project_tree.</summary>
    public int? Depth { get; set; }

    /// <summary>Forwarded by: browse_project_tree.</summary>
    public string? StartPath { get; set; }

    /// <summary>Forwarded by: search_equipment_catalog.</summary>
    public string? Query { get; set; }

    /// <summary>Forwarded by: search_equipment_catalog, read_cross_references.</summary>
    public int? MaxResults { get; set; }

    /// <summary>
    /// Forwarded by: read_cross_references. Populated from the batch item's `filter` field —
    /// the names differ — after CrossReferenceFilterNames.TryNormalize validates it. That
    /// validation runs BEFORE the session binds so an invalid filter cannot bind the session.
    /// </summary>
    public string? CrossReferenceFilter { get; set; }

    #endregion

    #region Network devices

    /// <summary>Forwarded by: add_network_device.</summary>
    public string? TypeIdentifier { get; set; }

    /// <summary>Forwarded by: add_network_device, configure_network_device.</summary>
    public string? DeviceName { get; set; }

    /// <summary>
    /// Forwarded by: add_network_device, configure_network_device. Falls back to DeviceName
    /// when the caller omits it.
    /// </summary>
    public string? DeviceItemName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? IpAddress { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? SubnetMask { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? PnDeviceName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? SubnetName { get; set; }

    /// <summary>Forwarded by: configure_network_device.</summary>
    public string? IoSystemName { get; set; }

    #endregion

    #region Project lifecycle

    /// <summary>Forwarded by: create_project.</summary>
    public string? ProjectDirectory { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? ProjectName { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? Author { get; set; }

    /// <summary>Forwarded by: create_project.</summary>
    public string? Comment { get; set; }

    /// <summary>Forwarded by: save_project_as.</summary>
    public string? TargetDirectory { get; set; }

    /// <summary>Forwarded by: save_project_as.</summary>
    public string? TargetName { get; set; }

    /// <summary>Forwarded by: open_project. The session-rebind escape hatch.</summary>
    public bool ForceRebind { get; set; }

    /// <summary>
    /// Forwarded by: save_project_as. Whether the session rebinds to the saved copy.
    /// Distinct from ForceRebind.
    /// </summary>
    public bool Rebind { get; set; } = true;

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveDirectory { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveName { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public string? ArchiveMode { get; set; }

    /// <summary>Forwarded by: archive_project.</summary>
    public bool SaveBeforeArchive { get; set; } = true;

    /// <summary>Forwarded by: close_project.</summary>
    public bool SaveBeforeClose { get; set; } = true;

    #endregion
}
```

- [ ] **Step 2: Build the solution**

Run: `dotnet build TiaMcpServer.sln -m:1`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test TiaMcpServer.Tests`
Expected: `Failed: 0, Passed: 336`. A comment-only change cannot alter behavior; any failure means a field was accidentally renamed, reordered into a different type, or lost its initializer — diff against `git show HEAD:TiaMcpServer.Contracts/WorkerRequest.cs` to find it.

- [ ] **Step 4: Commit**

```bash
git add TiaMcpServer.Contracts/WorkerRequest.cs
git commit -m "docs: group WorkerRequest fields by operation family

Adds #region grouping and per-field documentation of which operations
forward each field. No behavior change."
```

---

### Task 2: Consolidate the project-path binding checks (item 3.2)

`OpennessWorkerClient.CanBind` re-implements `ProjectSessionBinding.Bind`'s guard logic and duplicates its error text. Separately, `ProjectSessionBinding` emits two *different* messages for the same "already bound" condition, so an agent gets different recovery advice depending on which code path rejected it:

- `TryResolve`: `"… Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project."`
- `Bind`: `"… Start a new MCP session for a different TIA project or set forceRebind=true."`

Phase 0.5 set out to unify these and reached `TryResolve` only. This task finishes it, standardizing on the `TryResolve` wording.

**Files:**
- Modify: `TiaMcpServer.Contracts/ProjectSessionBinding.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs` (call site ~line 475; private `CanBind` ~line 710)
- Test: `TiaMcpServer.Tests/ProjectSessionBindingTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public bool ProjectSessionBinding.CanBind(string? projectPath, bool forceRebind, out string? error)` — non-mutating. Returns `true` when a subsequent `Bind(projectPath, forceRebind, out _)` would succeed. On `false`, `error` is non-null. Existing signatures `TryResolve`, `Bind`, `Clear`, and the `BoundProjectPath` property are unchanged.

- [ ] **Step 1: Write the failing tests**

Append these to `TiaMcpServer.Tests/ProjectSessionBindingTests.cs`, inside the existing `ProjectSessionBindingTests` class:

```csharp
    [Fact]
    public void CanBindAllowsFirstBindingWithoutMutating()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.True(binding.CanBind("C:\\Projects\\Line.ap21", forceRebind: false, out var error));

        Assert.Null(error);
        Assert.Null(binding.BoundProjectPath);
    }

    [Fact]
    public void CanBindRejectsDifferentProjectPathWithoutMutating()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.False(binding.CanBind("C:\\Projects\\Other.ap21", forceRebind: false, out var error));

        Assert.Contains("already bound", error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void CanBindAllowsDifferentProjectPathWhenForced()
    {
        var binding = new ProjectSessionBinding("C:\\Projects\\Line.ap21");

        Assert.True(binding.CanBind("C:\\Projects\\Other.ap21", forceRebind: true, out var error));

        Assert.Null(error);
        Assert.Equal("C:\\Projects\\Line.ap21", binding.BoundProjectPath);
    }

    [Fact]
    public void CanBindRejectsBlankProjectPath()
    {
        var binding = new ProjectSessionBinding(null);

        Assert.False(binding.CanBind("   ", forceRebind: false, out var error));

        Assert.Equal("Project path is required.", error);
    }

    [Fact]
    public void AllRejectionPathsGiveIdenticalRebindInstructions()
    {
        const string bound = "C:\\Projects\\Line.ap21";
        const string other = "C:\\Projects\\Other.ap21";

        var forTryResolve = new ProjectSessionBinding(bound);
        forTryResolve.TryResolve(other, out _, out var tryResolveError);

        var forBind = new ProjectSessionBinding(bound);
        forBind.Bind(other, forceRebind: false, out var bindError);

        var forCanBind = new ProjectSessionBinding(bound);
        forCanBind.CanBind(other, forceRebind: false, out var canBindError);

        Assert.NotNull(tryResolveError);
        Assert.Equal(tryResolveError, bindError);
        Assert.Equal(tryResolveError, canBindError);
        Assert.Contains("forceRebind=true", tryResolveError);
        Assert.Contains("open_project", tryResolveError);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectSessionBindingTests"`
Expected: **compile error**, `'ProjectSessionBinding' does not contain a definition for 'CanBind'`. That is the correct failure — the method does not exist yet.

- [ ] **Step 3: Add `CanBind` and the shared message to `ProjectSessionBinding`**

In `TiaMcpServer.Contracts/ProjectSessionBinding.cs`, add these two members directly below the `BoundProjectPath` property:

```csharp
    private const string RebindInstruction =
        "Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project.";

    private static string AlreadyBoundError(string boundProjectPath, string requestedProjectPath)
        => $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requestedProjectPath}'. {RebindInstruction}";

    /// <summary>
    /// Reports whether <see cref="Bind"/> would succeed, without mutating the binding.
    /// Callers that must validate before doing expensive work use this; the error text is
    /// identical to the one <see cref="Bind"/> would produce.
    /// </summary>
    public bool CanBind(string? projectPath, bool forceRebind, out string? error)
    {
        error = null;

        var requested = Normalize(projectPath);
        if (requested is null)
        {
            error = "Project path is required.";
            return false;
        }

        if (_boundProjectPath is null ||
            string.Equals(_boundProjectPath, requested, StringComparison.OrdinalIgnoreCase) ||
            forceRebind)
        {
            return true;
        }

        error = AlreadyBoundError(_boundProjectPath, requested);
        return false;
    }
```

- [ ] **Step 4: Reduce `Bind` to `CanBind` plus the mutation**

Replace the entire existing `Bind` method with:

```csharp
    public bool Bind(string projectPath, bool forceRebind, out string? error)
    {
        if (!CanBind(projectPath, forceRebind, out error))
        {
            return false;
        }

        _boundProjectPath = Normalize(projectPath);
        return true;
    }
```

- [ ] **Step 5: Point `TryResolve` at the shared message**

In `TryResolve`, replace these two lines:

```csharp
        var boundProjectPath = _boundProjectPath ?? string.Empty;
        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requested}'. Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project.";
```

with:

```csharp
        error = AlreadyBoundError(_boundProjectPath ?? string.Empty, requested);
```

- [ ] **Step 6: Run the binding tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ProjectSessionBindingTests"`
Expected: all pass, 0 failed. The pre-existing tests assert on `"already bound"` and on `TryResolve` containing `"forceRebind"`/`"open_project"`; the unified wording satisfies all of them.

- [ ] **Step 7: Delete the duplicate in `OpennessWorkerClient` and delegate**

In `TiaMcpServer/Worker/OpennessWorkerClient.cs`, delete the entire private `CanBind` method (~line 710):

```csharp
    private bool CanBind(string projectPath, bool forceRebind, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            error = "Project path is required.";
            return false;
        }

        var boundProjectPath = _projectSessionBinding.BoundProjectPath;
        if (boundProjectPath is null ||
            forceRebind ||
            string.Equals(boundProjectPath, projectPath.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{projectPath}'. Start a new MCP session for a different TIA project or set forceRebind=true.";
        return false;
    }
```

Then in `OpenProjectAsync` (~line 475), change the call site from:

```csharp
        if (!CanBind(projectPath, forceRebind, out var bindingError))
```

to:

```csharp
        if (!_projectSessionBinding.CanBind(projectPath, forceRebind, out var bindingError))
```

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test TiaMcpServer.Tests`
Expected: `Failed: 0, Passed: 341` (336 baseline + 5 new). `OpennessWorkerClientIntegrationTests` line 184 asserts `"already bound to project 'ok'"`, which the unified message still contains.

- [ ] **Step 9: Commit**

```bash
git add TiaMcpServer.Contracts/ProjectSessionBinding.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Tests/ProjectSessionBindingTests.cs
git commit -m "refactor: consolidate project-path binding checks

Adds a non-mutating ProjectSessionBinding.CanBind and reduces Bind to
CanBind plus the assignment. Deletes the copy of that logic in
OpennessWorkerClient. Unifies the three 'already bound' error texts on
the forceRebind-aware wording Phase 0.5 established, finishing that work."
```

---

### Task 3: Dedupe the presentation JSON options (item 3.5a)

Three byte-identical `JsonSerializerOptions` declarations exist in the host, all `CamelCase` + `WriteIndented = false`.

**Scope limit — read before starting.** The two *wire/IPC* declarations (`TiaMcpServer/Worker/PersistentWorkerTransport.cs` and `TiaMcpServer.OpennessWorker/Program.cs`) are **deliberately excluded**. They live in different processes, they are not identical (the worker adds `DefaultIgnoreCondition = WhenWritingNull`), and sharing them would require a `System.Text.Json` PackageReference on `TiaMcpServer.Contracts`, which must stay dependency-free. Leave both untouched. Test-project option declarations are also out of scope — tests must not derive their expectations from the type under test.

**Files:**
- Create: `TiaMcpServer/Json/TiaJson.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (link the new file)
- Modify: `TiaMcpServer/Batch/BatchResultFormatter.cs:8-12`
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs:10-14`
- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs:9-13`

**Interfaces:**
- Consumes: nothing.
- Produces: `TiaMcpServer.Json.TiaJson.Presentation` — a `public static readonly JsonSerializerOptions` configured `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, `WriteIndented = false`.

- [ ] **Step 1: Create `TiaMcpServer/Json/TiaJson.cs`**

```csharp
using System.Text.Json;

namespace TiaMcpServer.Json;

/// <summary>
/// Shared System.Text.Json configuration for host-process output.
///
/// <para>
/// This covers only text the host renders back to the MCP client. The host↔worker wire
/// format is deliberately NOT shared from here: those options live with each process's
/// transport (TiaMcpServer/Worker/PersistentWorkerTransport.cs and the worker's Program.cs),
/// they differ on purpose — the worker omits nulls when writing — and unifying them would
/// require a System.Text.Json package reference on the dependency-free
/// TiaMcpServer.Contracts assembly.
/// </para>
/// </summary>
public static class TiaJson
{
    /// <summary>
    /// Options for JSON returned to the MCP client. Compact on purpose: responses are
    /// token-budgeted and indentation is pure overhead.
    /// </summary>
    public static readonly JsonSerializerOptions Presentation = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
}
```

- [ ] **Step 2: Link the new file into the test project**

The test project compiles linked host sources rather than referencing the host assembly, and it links all three files this task modifies. Without this entry the test project will not compile.

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, add this line immediately before the existing `WriteSafetyService.cs` line:

```xml
    <Compile Include="..\TiaMcpServer\Json\TiaJson.cs" Link="Host\Json\TiaJson.cs" />
```

- [ ] **Step 3: Point `BatchResultFormatter` at the shared options**

In `TiaMcpServer/Batch/BatchResultFormatter.cs`, delete the private field:

```csharp
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
```

Add `using TiaMcpServer.Json;` to the file's using block, then replace every remaining `JsonOptions` identifier in the file with `TiaJson.Presentation`.

Find them with: `grep -n "JsonOptions" TiaMcpServer/Batch/BatchResultFormatter.cs`
When done that command must return no matches.

- [ ] **Step 4: Point `WriteSafetyService` at the shared options**

In `TiaMcpServer/Safety/WriteSafetyService.cs`, delete the identical private field (lines 10-14), add `using TiaMcpServer.Json;`, and replace every remaining `JsonOptions` identifier with `TiaJson.Presentation`.

Find them with: `grep -n "JsonOptions" TiaMcpServer/Safety/WriteSafetyService.cs`
When done that command must return no matches.

- [ ] **Step 5: Point `WriteSafetyTooling` at the shared options**

In `TiaMcpServer/Safety/WriteSafetyTooling.cs`, delete the identical private field (lines 9-13), add `using TiaMcpServer.Json;`, and replace every remaining `JsonOptions` identifier with `TiaJson.Presentation`.

Find them with: `grep -n "JsonOptions" TiaMcpServer/Safety/WriteSafetyTooling.cs`
When done that command must return no matches.

- [ ] **Step 6: Confirm the wire declarations were left alone**

Run: `grep -rn "JsonSerializerOptions JsonOptions = new" TiaMcpServer/ TiaMcpServer.OpennessWorker/`
Expected: exactly two matches — `TiaMcpServer/Worker/PersistentWorkerTransport.cs` and `TiaMcpServer.OpennessWorker/Program.cs`. If either is missing, a wire declaration was wrongly removed; restore it.

- [ ] **Step 7: Build the solution**

Run: `dotnet build TiaMcpServer.sln -m:1`
Expected: Build succeeded, 0 errors. A `CS0246: The type or namespace name 'TiaJson' could not be found` in the test project means Step 2 was skipped.

- [ ] **Step 8: Run the full test suite**

Run: `dotnet test TiaMcpServer.Tests`
Expected: `Failed: 0, Passed: 341` (or 336 if Task 2 has not been run — the tasks are independent). The three replaced option sets were byte-identical, so serialized output is unchanged; any assertion failure on JSON text means a config value was mistyped.

- [ ] **Step 9: Commit**

```bash
git add TiaMcpServer/Json/TiaJson.cs TiaMcpServer/Batch/BatchResultFormatter.cs TiaMcpServer/Safety/WriteSafetyService.cs TiaMcpServer/Safety/WriteSafetyTooling.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "refactor: share the host presentation JSON options

Replaces three byte-identical JsonSerializerOptions declarations with
TiaJson.Presentation. The host<->worker wire options stay per-process:
they differ deliberately and sharing them would put a System.Text.Json
dependency on the dependency-free Contracts assembly."
```

---

## Closing out

- [ ] **Update `docs/IMPROVEMENT_PLAN.md` Phase 3 table**

Mark 3.2, 3.5, and 3.6 as `— DONE 2026-07-20`. For 3.1 and 3.4, replace the row text with a note that they were resolved by Phases 2.4 and 1.7 respectively and are dropped. Leave 3.3 as-is and add `— DEFERRED 2026-07-20`. Correct the stale "Test suite: 146/146 green" line at the top of the file to the current count.

Also correct the 3.5 row: it claims a single shared `JsonSerializerOptions` across 4 files. Reword to reflect that there are two distinct configurations and only the presentation one was shared.

- [ ] **Commit the plan update**

```bash
git add docs/IMPROVEMENT_PLAN.md
git commit -m "docs: record Phase 3 outcomes in the improvement plan"
```
