# Phase 0 Reliability Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the TIA Portal MCP server's errors self-recoverable for small-model agents, kill two false-success write paths, and serialize worker access — Phase 0 + items 1.2 and 2.2 of `priv/IMPROVEMENT_PLAN.md`.

**Architecture:** Two-process design stays untouched: `TiaMcpServer` (net8 MCP stdio host) spawns `TiaMcpServer.OpennessWorker` (net48, loads Siemens Openness DLLs) per request, newline-delimited JSON over stdin/stdout. All changes are message/validation/description fixes in the host, two exception-flow fixes in the worker, and one semaphore in the host's worker client.

**Tech Stack:** C# — net8.0 (host, tests), net48 (worker), netstandard2.0 (contracts). xunit 2.9. MCP C# SDK (`ModelContextProtocol` 1.2.0) with attribute-based tool registration.

## Global Constraints

- Build the solution serialized: `dotnet build TiaMcpServer.sln -m:1` (the host project also builds/copies the net48 worker; parallel builds duplicate it).
- Run tests with: `dotnet test TiaMcpServer.Tests --nologo -v q` (146 tests green before this plan starts — verify that first).
- **Worker code (`TiaMcpServer.OpennessWorker/**`) is NOT unit-testable**: `TiaMcpServer.Tests.csproj` links host `.cs` files by `<Compile Include>` and never references the net48 worker (Siemens types don't exist in net8). Tasks 7–8 are verified by solution build + reasoning documented in the task; do not try to add worker unit tests.
- The worker's `Execute()` helper (`TiaMcpServer.OpennessWorker/Program.cs:549-571`) is the single exception→failure mapper: it catches `EngineeringException`, `NonRecoverableException`, `InvalidOperationException`, `IOException` and returns `WorkerResponse.Success=false`. Tasks 7–8 rely on this — throwing those types IS the fix.
- Conventional commits: `<type>: <description>` (feat, fix, docs, test, chore). No attribution footers (disabled in user settings).
- **This repo is actively worked on** (PR #4 merged mid-review on 2026-07-15). Before starting: `git pull`, re-run the test suite, and locate edit targets by the exact code strings given below, NOT by line numbers.
- Message-changing edits must keep these existing substring assertions passing: `"already bound"` (ProjectSessionBindingTests), `"Safety token required"` (BatchToolsTests, WriteToolSafetyTokenTests), `"expired, consumed, or unknown"`, `"expired"`, `"input"`, `"current state"` (WriteSafetyServiceTests), `"teleport_plc"`, `"frobnicate"` (BatchOperationCatalogTests). Every change below appends text; nothing removes those substrings.
- After the last task, run `graphify update .` (project CLAUDE.md requirement, AST-only, no API cost).

## File Structure

Modified (no new production files):

| File | Tasks | Responsibility touched |
|---|---|---|
| `TiaMcpServer/Batch/BatchOperationCatalog.cs` | 1, 2 | validation error messages, error aggregation |
| `TiaMcpServer/Safety/WriteSafetyService.cs` | 3, 6 | token rejection guidance, audit-failure logging |
| `TiaMcpServer/Safety/WriteSafetyTooling.cs` | 3 | pass preview tool name through |
| `TiaMcpServer/Batch/BatchTools.cs` | 3 | pass preview tool name; TTL in description |
| `TiaMcpServer/Tools/ProjectLifecycleTools.cs` | 3 | TTL in 6 preview descriptions |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | 4 | 4 corrected field descriptions |
| `TiaMcpServer.Contracts/ProjectSessionBinding.cs` | 5 | forceRebind hint in rejection |
| `README.md` | 3, 5 | TTL, operation lists, forceRebind |
| `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs` | 7 | false-success fix |
| `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs` | 8 | all-settings-skipped fix |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | 9 | request serialization |

Test files: `BatchOperationCatalogTests.cs` (1, 2), `WriteSafetyServiceTests.cs` (3, 6), `BatchToolMetadataTests.cs` (3), `ProjectSessionBindingTests.cs` (5).

---

### Task 1: Unknown-operation errors list the valid operation names

**Files:**
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs` (method `ResolveSpec`)
- Test: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`

**Interfaces:**
- Consumes: `BatchOperationCatalog.ReadOperationNames` / `WriteOperationNames` (existing public `IReadOnlyList<string>` properties on the same class).
- Produces: no signature changes; only the `Invalid(...)` message text for unknown operations changes.

- [ ] **Step 1: Write the failing tests**

Add to `TiaMcpServer.Tests/BatchOperationCatalogTests.cs` (inside the existing `BatchOperationCatalogTests` class, using its existing `Op` helper):

```csharp
    [Fact]
    public void ValidateReadBatch_UnknownOperationErrorListsValidReadOperations()
    {
        var result = BatchOperationCatalog.ValidateReadBatch(new[] { Op("a", "teleport_plc") });

        Assert.False(result.IsValid);
        Assert.Contains("Valid read operations", result.Error);
        Assert.Contains("browse_project_tree", result.Error);
        Assert.Contains("get_block_content", result.Error);
    }

    [Fact]
    public void ValidateWriteBatch_UnknownOperationErrorListsValidWriteOperations()
    {
        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { Op("a", "frobnicate") });

        Assert.False(result.IsValid);
        Assert.Contains("Valid write operations", result.Error);
        Assert.Contains("update_block_logic", result.Error);
        Assert.Contains("create_tag", result.Error);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~BatchOperationCatalogTests"`
Expected: the 2 new tests FAIL on `Assert.Contains("Valid read operations", ...)`; all others pass.

- [ ] **Step 3: Implement**

In `TiaMcpServer/Batch/BatchOperationCatalog.cs`, method `ResolveSpec`, replace:

```csharp
            return BatchValidationResult.Invalid(
                $"Unknown operation '{op.Operation}' for operationId '{op.OperationId}'.");
```

with:

```csharp
            var validNames = expected == BatchOperationCategory.Read ? ReadOperationNames : WriteOperationNames;
            var categoryLabel = expected == BatchOperationCategory.Read ? "read" : "write";
            return BatchValidationResult.Invalid(
                $"Unknown operation '{op.Operation}' for operationId '{op.OperationId}'. "
                + $"Valid {categoryLabel} operations: {string.Join(", ", validNames)}.");
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~BatchOperationCatalogTests"`
Expected: PASS (all, including the pre-existing `RejectsUnknownOperation` tests — they only assert the typo'd name is contained).

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer/Batch/BatchOperationCatalog.cs TiaMcpServer.Tests/BatchOperationCatalogTests.cs
git commit -m "feat: list valid operation names in unknown-operation batch errors"
```

---

### Task 2: Aggregate all batch validation errors into one response

**Files:**
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs` (method `Validate`)
- Test: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`

**Interfaces:**
- Consumes: Task 1's `ResolveSpec` message (unchanged here).
- Produces: `BatchValidationResult.Error` may now contain multiple `\n`-separated errors. `IsValid` semantics unchanged. No caller changes needed (`BatchTools` passes `validation.Error` through verbatim).

- [ ] **Step 1: Write the failing test**

Add to `BatchOperationCatalogTests.cs`:

```csharp
    [Fact]
    public void ValidateWriteBatch_ReportsAllInvalidItemsAtOnce()
    {
        var operations = new[]
        {
            Op("a", "creat_tag"),
            Op("b", "create_tag", r => r.TableName = "Inputs"),
            Op("c", "get_block_content", r => r.BlockPath = "Main"),
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("creat_tag", result.Error);
        Assert.Contains("dataType", result.Error);
        Assert.Contains("get_block_content", result.Error);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~BatchOperationCatalogTests"`
Expected: `ValidateWriteBatch_ReportsAllInvalidItemsAtOnce` FAILS — current code returns only the first error (`creat_tag`), so `Assert.Contains("dataType", ...)` fails.

- [ ] **Step 3: Implement**

In `BatchOperationCatalog.cs`, replace the whole body of `private static BatchValidationResult Validate(...)` (keep the signature) with:

```csharp
        if (operations is null || operations.Count == 0)
        {
            return BatchValidationResult.Invalid("Batch must contain at least one operation.");
        }

        if (operations.Count > MaxBatchSize)
        {
            return BatchValidationResult.Invalid(
                $"Batch exceeds the maximum of {MaxBatchSize} operations (received {operations.Count}).");
        }

        var errors = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in operations)
        {
            if (op is null)
            {
                errors.Add("Batch contains a null operation.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(op.OperationId))
            {
                errors.Add("Each operation requires a unique operationId.");
                continue;
            }

            if (!seenIds.Add(op.OperationId))
            {
                errors.Add($"Duplicate operationId '{op.OperationId}'.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(op.Operation))
            {
                errors.Add($"Operation name is required for operationId '{op.OperationId}'.");
                continue;
            }

            var categoryResult = ResolveSpec(op, expected, out var spec);
            if (!categoryResult.IsValid)
            {
                errors.Add(categoryResult.Error);
                continue;
            }

            var missing = spec!.RequiredFields.Where(field => !IsFieldPresent(op, field)).ToArray();
            if (missing.Length > 0)
            {
                errors.Add(
                    $"Operation '{op.Operation}' (operationId '{op.OperationId}') is missing required field(s): {string.Join(", ", missing)}.");
            }
        }

        if (expected == BatchOperationCategory.Write)
        {
            var distinctPaths = operations
                .Where(op => op is not null && !string.IsNullOrWhiteSpace(op.ProjectPath))
                .Select(op => WriteSafetyService.NormalizeProjectPath(op!.ProjectPath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (distinctPaths.Count > 1)
            {
                errors.Add("All write operations in a batch must target the same project path.");
            }
        }

        return errors.Count > 0
            ? BatchValidationResult.Invalid(string.Join("\n", errors))
            : BatchValidationResult.Valid();
```

Note the null-guard added to the `distinctPaths` LINQ (`op is not null &&`) — the original dereferenced `op!.ProjectPath` after the loop had already returned on null items; with aggregation the loop no longer returns early, so the guard is now required.

- [ ] **Step 4: Run the full test class**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~BatchOperationCatalogTests"`
Expected: PASS — every pre-existing test asserts via `Assert.Contains`, so multi-error strings keep them green.

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer/Batch/BatchOperationCatalog.cs TiaMcpServer.Tests/BatchOperationCatalogTests.cs
git commit -m "feat: aggregate all batch validation errors into one response"
```

---

### Task 3: Safety-token rejections carry recovery guidance and the 10-minute TTL

**Files:**
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`, `TiaMcpServer/Safety/WriteSafetyTooling.cs`, `TiaMcpServer/Batch/BatchTools.cs`, `TiaMcpServer/Tools/ProjectLifecycleTools.cs`, `README.md`
- Test: `TiaMcpServer.Tests/WriteSafetyServiceTests.cs`, `TiaMcpServer.Tests/BatchToolMetadataTests.cs`

**Interfaces:**
- Produces: `WriteSafetyService.DefaultTokenLifetime` — new `public static readonly TimeSpan`, value `TimeSpan.FromMinutes(10)`.
- Produces: `ValidateAndConsume(...)` gains a trailing optional parameter `string? previewToolName = null`. Existing 6-arg call sites compile unchanged; the two call sites below are updated to pass it.

- [ ] **Step 1: Write the failing tests**

Add to `WriteSafetyServiceTests.cs`:

```csharp
    [Fact]
    public void RejectionIncludesRecoveryGuidanceWithPreviewToolName()
    {
        var safety = new WriteSafetyService(() => DateTimeOffset.UtcNow);

        var result = safety.ValidateAndConsume(
            "unknown-token",
            toolName: "apply_write_batch",
            projectPath: null,
            target: new { },
            requestedInput: new { },
            currentState: "state",
            previewToolName: "preview_write_batch");

        Assert.False(result.IsValid);
        Assert.Contains("single-use", result.Error);
        Assert.Contains("10 minutes", result.Error);
        Assert.Contains("preview_write_batch", result.Error);
    }

    [Fact]
    public void RecoveryGuidanceUsesConfiguredLifetime()
    {
        var safety = new WriteSafetyService(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));

        var result = safety.ValidateAndConsume(
            "unknown-token", "apply_write_batch", null, new { }, new { }, "state");

        Assert.False(result.IsValid);
        Assert.Contains("2 minutes", result.Error);
        Assert.Contains("the matching preview tool", result.Error);
    }
```

Add to `BatchToolMetadataTests.cs` (uses its existing `MethodDescription` helper; add `using TiaMcpServer.Safety;` at the top of the file):

```csharp
    [Fact]
    public void PreviewWriteBatchDescription_StatesTheActualTokenLifetime()
    {
        var expected = $"{WriteSafetyService.DefaultTokenLifetime.TotalMinutes:N0} minutes";
        Assert.Contains(expected, MethodDescription("PreviewWriteBatch"));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~WriteSafetyServiceTests|FullyQualifiedName~BatchToolMetadataTests"`
Expected: the 2 service tests FAIL to compile or fail at `Assert.Contains("single-use", ...)`; the metadata test FAILS to compile (`DefaultTokenLifetime` missing). Compile failure counts as RED here — fix by implementing.

- [ ] **Step 3: Implement `WriteSafetyService` changes**

3a. Add the lifetime constant and use it in the constructors. Replace:

```csharp
    public WriteSafetyService()
        : this(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10))
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow)
        : this(getUtcNow, TimeSpan.FromMinutes(10))
    {
    }
```

with:

```csharp
    public static readonly TimeSpan DefaultTokenLifetime = TimeSpan.FromMinutes(10);

    public WriteSafetyService()
        : this(() => DateTimeOffset.UtcNow, DefaultTokenLifetime)
    {
    }

    public WriteSafetyService(Func<DateTimeOffset> getUtcNow)
        : this(getUtcNow, DefaultTokenLifetime)
    {
    }
```

3b. Replace the whole `ValidateAndConsume` method with (same logic, new optional parameter, every rejection routed through a helper):

```csharp
    public WriteSafetyValidationResult ValidateAndConsume(
        string? safetyToken,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string currentState,
        string? previewToolName = null)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName);
        }

        if (!_tokens.TryRemove(safetyToken, out var entry))
        {
            return Rejected("Safety token expired, consumed, or unknown.", previewToolName);
        }

        if (_getUtcNow() > entry.ExpiresAtUtc)
        {
            return Rejected("Safety token expired.", previewToolName);
        }

        if (!string.Equals(entry.ToolName, toolName, StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different tool.", previewToolName);
        }

        if (!string.Equals(entry.ProjectPath, NormalizeProjectPath(projectPath), StringComparison.OrdinalIgnoreCase))
        {
            return Rejected("Safety token was issued for a different project path.", previewToolName);
        }

        if (!string.Equals(entry.TargetJson, ToStableJson(target), StringComparison.Ordinal))
        {
            return Rejected("Safety token was issued for a different target.", previewToolName);
        }

        var requestedInputHash = HashText(ToStableJson(requestedInput));
        if (!string.Equals(entry.RequestedInputHash, requestedInputHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token input does not match this write request.", previewToolName);
        }

        var currentStateHash = HashText(currentState);
        if (!string.Equals(entry.CurrentStateHash, currentStateHash, StringComparison.Ordinal))
        {
            return Rejected("Safety token current state no longer matches the project.", previewToolName);
        }

        return WriteSafetyValidationResult.Valid(requestedInputHash, currentStateHash);
    }

    private WriteSafetyValidationResult Rejected(string reason, string? previewToolName)
    {
        var previewTool = string.IsNullOrWhiteSpace(previewToolName) ? "the matching preview tool" : previewToolName;
        return WriteSafetyValidationResult.Invalid(
            $"{reason} Safety tokens are single-use and expire after {_tokenLifetime.TotalMinutes:N0} minutes. "
            + $"Call {previewTool} again to get a fresh token, review the new preview, then retry with confirm=true and the new safetyToken.");
    }
```

3c. In `WriteSafetyTooling.cs`, method `ValidateForApplyAsync`, replace the `ValidateAndConsume` call:

```csharp
        var validation = WriteSafetyService.Shared.ValidateAndConsume(
            safetyToken,
            toolName,
            projectPath,
            target,
            requestedInput,
            currentState);
```

with:

```csharp
        var validation = WriteSafetyService.Shared.ValidateAndConsume(
            safetyToken,
            toolName,
            projectPath,
            target,
            requestedInput,
            currentState,
            previewToolName);
```

3d. In `BatchTools.cs`, method `ApplyWriteBatch`, replace:

```csharp
        var tokenValidation = WriteSafetyService.Shared.ValidateAndConsume(
            safetyToken,
            ApplyToolName,
            projectPath,
            targets,
            operations,
            snapshot.CombinedState);
```

with:

```csharp
        var tokenValidation = WriteSafetyService.Shared.ValidateAndConsume(
            safetyToken,
            ApplyToolName,
            projectPath,
            targets,
            operations,
            snapshot.CombinedState,
            PreviewToolName);
```

- [ ] **Step 4: Implement the description updates**

4a. `BatchTools.cs`, `PreviewWriteBatch` description — replace the sentence fragment:

```csharp
    [Description("Preview up to 50 write operations and return one batch-level safetyToken bound to the exact ordered operation list and the combined current state. Pass the token to apply_write_batch after reviewing the preview. All items must target the same project. "
```

with:

```csharp
    [Description("Preview up to 50 write operations and return one batch-level safetyToken bound to the exact ordered operation list and the combined current state. The token is single-use and expires after 10 minutes. Pass the token to apply_write_batch after reviewing the preview. All items must target the same project. "
```

4b. `ProjectLifecycleTools.cs` — six `[Description]` edits, all the same substitution: replace `a short-lived safetyToken` with `a single-use safetyToken that expires after 10 minutes`:

| Tool | Old description start | New description start |
|---|---|---|
| `preview_open_project` | `"Preview opening a TIA Portal project and return a short-lived safetyToken. ...` | `"Preview opening a TIA Portal project and return a single-use safetyToken that expires after 10 minutes. ...` |
| `preview_create_project` | `"Preview creating a new TIA Portal project and return a short-lived safetyToken. ...` | `"Preview creating a new TIA Portal project and return a single-use safetyToken that expires after 10 minutes. ...` |
| `preview_save_project` | `"Preview saving the active TIA Portal project and return a short-lived safetyToken. ...` | `"Preview saving the active TIA Portal project and return a single-use safetyToken that expires after 10 minutes. ...` |
| `preview_save_project_as` | `"Preview saving the active TIA Portal project to a copy and return a short-lived safetyToken. ...` | `"Preview saving the active TIA Portal project to a copy and return a single-use safetyToken that expires after 10 minutes. ...` |
| `preview_archive_project` | `"Preview archiving the active TIA Portal project and return a short-lived safetyToken. ...` | `"Preview archiving the active TIA Portal project and return a single-use safetyToken that expires after 10 minutes. ...` |
| `preview_close_project` | `"Preview closing the active TIA Portal project and return a short-lived safetyToken. ...` | `"Preview closing the active TIA Portal project and return a single-use safetyToken that expires after 10 minutes. ...` |

The `Pass the token to <apply tool> after reviewing the preview.` tail of each description stays unchanged.

4c. `README.md` — in the "Write safety" section, replace:

```markdown
Safety tokens are short-lived, single-use, and bound to the exact tool name, normalized project path, target, requested input, and current project state.
```

with:

```markdown
Safety tokens are single-use, expire 10 minutes after preview, and are bound to the exact tool name, normalized project path, target, requested input, and current project state.
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q`
Expected: full suite PASS. Specifically: the 3 new tests pass; `TokenCannotBeReused` / `TokenExpiresAfterConfiguredLifetime` / `TokenRejectsChangedInputAndChangedCurrentState` still pass (their `Assert.Contains` substrings are preserved as prefixes of the new messages).

- [ ] **Step 6: Commit**

```powershell
git add TiaMcpServer/Safety/WriteSafetyService.cs TiaMcpServer/Safety/WriteSafetyTooling.cs TiaMcpServer/Batch/BatchTools.cs TiaMcpServer/Tools/ProjectLifecycleTools.cs README.md TiaMcpServer.Tests/WriteSafetyServiceTests.cs TiaMcpServer.Tests/BatchToolMetadataTests.cs
git commit -m "feat: add recovery guidance and 10-minute TTL to safety-token rejections"
```

---

### Task 4: Fix schema-facing field descriptions on BatchOperationRequest

**Files:**
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs`
- Test: `TiaMcpServer.Tests/BatchToolMetadataTests.cs`

**Interfaces:**
- Produces: `[Description]` text changes only; no property or type changes. These descriptions surface in the MCP JSON schema via `WithToolsFromAssembly()`, so they ARE the agent-facing contract.

Background (verified against `BatchWorkerInvoker.cs`): `NewName` is forwarded only by `update_tag` — `update_user_constant` calls `client.UpdateUserConstantAsync(...)` without it, so the current "renaming a tag or user constant" description is false. `Filter` is only consumed by `read_cross_references`. `PlcName` is forwarded by `list_tag_tables`, `read_cross_references`, `compile_check`, `start_plc`, `stop_plc`, and all tag/tag-table/user-constant operations. `BlockPath` is required by `get_block_content`, `update_block_logic`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group` and optionally scopes `compile_check`.

- [ ] **Step 1: Write the failing tests**

Add to `BatchToolMetadataTests.cs`:

```csharp
    private static string PropertyDescription(string propertyName)
    {
        var property = typeof(BatchOperationRequest).GetProperty(propertyName);
        Assert.NotNull(property);
        var description = property!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(description);
        return description!.Description;
    }

    [Fact]
    public void FilterDescription_NamesItsOperationAndListsAllValues()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.Filter));
        Assert.Contains("read_cross_references", description);
        Assert.Contains("AllObjects", description);
        Assert.Contains("ObjectsWithReferences", description);
        Assert.Contains("ObjectsWithoutReferences", description);
        Assert.Contains("UnusedObjects", description);
    }

    [Fact]
    public void BlockPathDescription_CoversAllBlockOperationsAndCompileCheck()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.BlockPath));
        Assert.Contains("create_block", description);
        Assert.Contains("delete_block", description);
        Assert.Contains("compile_check", description);
    }

    [Fact]
    public void NewNameDescription_DoesNotClaimUserConstantRename()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.NewName));
        Assert.Contains("update_tag", description);
        Assert.DoesNotContain("user constant", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlcNameDescription_NamesTheOperationsThatHonorIt()
    {
        var description = PropertyDescription(nameof(BatchOperationRequest.PlcName));
        Assert.Contains("list_tag_tables", description);
        Assert.Contains("compile_check", description);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~BatchToolMetadataTests"`
Expected: all 4 new tests FAIL (current descriptions lack the operation names / still claim user-constant rename).

- [ ] **Step 3: Implement the 4 description replacements in `BatchOperationRequest.cs`**

Replace:

```csharp
    [Description("PLC block path, e.g. PLC_1/Main or PLC_1/Blocks/Folder/Block. Required by get_block_content and update_block_logic.")]
```

with:

```csharp
    [Description("PLC block path, e.g. PLC_1/Main or PLC_1/Blocks/Folder/Block. Required by get_block_content, update_block_logic, create_block, delete_block, create_block_group, delete_block_group. Optional for compile_check to compile only that block.")]
```

Replace:

```csharp
    [Description("Optional PLC software name used to scope the operation.")]
```

with:

```csharp
    [Description("Optional PLC software name to scope the operation. Honored by list_tag_tables, read_cross_references, compile_check, start_plc, stop_plc, and the tag, tag-table, and user-constant operations.")]
```

Replace:

```csharp
    [Description("Optional filter, e.g. a cross-reference filter such as ObjectsWithReferences or UnusedObjects.")]
```

with:

```csharp
    [Description("Optional cross-reference filter for read_cross_references. Allowed values: AllObjects, ObjectsWithReferences, ObjectsWithoutReferences, UnusedObjects.")]
```

Replace:

```csharp
    [Description("Optional new name when renaming a tag or user constant.")]
```

with:

```csharp
    [Description("Optional new name when renaming a tag; only update_tag applies it. Renaming user constants is not supported.")]
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~BatchToolMetadataTests"`
Expected: PASS (including pre-existing `KeyRequestFieldsHaveDescriptions`).

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer/Batch/BatchOperationRequest.cs TiaMcpServer.Tests/BatchToolMetadataTests.cs
git commit -m "fix: correct batch field descriptions for blockPath, plcName, filter, newName"
```

---

### Task 5: forceRebind escape hatch in binding errors + README operation-list sync

**Files:**
- Modify: `TiaMcpServer.Contracts/ProjectSessionBinding.cs`, `README.md`
- Test: `TiaMcpServer.Tests/ProjectSessionBindingTests.cs`

**Interfaces:**
- Produces: `TryResolve` rejection message now mentions `forceRebind` (matching the message `Bind` already emits). No signature changes.

- [ ] **Step 1: Write the failing test**

Add to `TiaMcpServer.Tests/ProjectSessionBindingTests.cs` (class `ProjectSessionBindingTests`, namespace `TiaMcpServer.Tests`; `ProjectSessionBinding` comes from the referenced Contracts project):

```csharp
    [Fact]
    public void TryResolve_RejectionMentionsForceRebindEscapeHatch()
    {
        var binding = new ProjectSessionBinding(@"C:\Projects\a.ap21");

        var resolved = binding.TryResolve(@"C:\Projects\b.ap21", out _, out var error);

        Assert.False(resolved);
        Assert.Contains("forceRebind", error);
        Assert.Contains("open_project", error);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~ProjectSessionBindingTests"`
Expected: the new test FAILS at `Assert.Contains("forceRebind", error)`.

- [ ] **Step 3: Implement**

In `ProjectSessionBinding.cs`, method `TryResolve`, replace:

```csharp
        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requested}'. Start a new MCP session for a different TIA project.";
```

with:

```csharp
        error = $"This MCP session is already bound to project '{boundProjectPath}' and cannot use '{requested}'. Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project.";
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~ProjectSessionBindingTests"`
Expected: PASS (pre-existing tests assert `Contains("already bound")` — preserved).

- [ ] **Step 5: Sync README with the actual operation catalog**

Three edits in `README.md`:

5a. Replace:

```markdown
Available read operations (for `execute_read_batch`): `browse_project_tree`, `get_block_content`, `list_tag_tables`, `read_hardware_config`, `read_cross_references`, `search_equipment_catalog`, `compile_check`.
```

with:

```markdown
Available read operations (for `execute_read_batch`): `browse_project_tree`, `get_block_content`, `list_tag_tables`, `read_hardware_config`, `read_cross_references`, `search_equipment_catalog`, `compile_check`, `get_project_status`.
```

5b. Replace:

```markdown
Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`, `add_network_device`, `configure_network_device`.
```

with:

```markdown
Available write operations (for `preview_write_batch` / `apply_write_batch`): `update_block_logic`, `create_block` / `delete_block`, `create_block_group` / `delete_block_group`, `create_tag_table` / `delete_tag_table`, `create_tag` / `update_tag` / `delete_tag`, `create_user_constant` / `update_user_constant` / `delete_user_constant`, `add_network_device`, `configure_network_device`, `start_plc` / `stop_plc`.
```

5c. The sentence `Once a server process is bound to a project path, later tool calls with a different projectPath are rejected. Start a new MCP session for a different customer project.` appears TWICE (sections "Install" and "Local Package Build"). Replace both occurrences with:

```markdown
Once a server process is bound to a project path, later tool calls with a different `projectPath` are rejected. Call `open_project` with `forceRebind=true` to rebind the session, or start a new MCP session for a different customer project.
```

- [ ] **Step 6: Verify no drift remains**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q`
Expected: full suite PASS.

Then manually cross-check: every name in `BatchOperationCatalog.BuildSpecs()` appears in the README lists from step 5, and vice versa.

- [ ] **Step 7: Commit**

```powershell
git add TiaMcpServer.Contracts/ProjectSessionBinding.cs README.md TiaMcpServer.Tests/ProjectSessionBindingTests.cs
git commit -m "fix: point already-bound errors at forceRebind and sync README operation lists"
```

---

### Task 6: Audit-write failures log to stderr instead of vanishing

**Files:**
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`
- Test: `TiaMcpServer.Tests/WriteSafetyServiceTests.cs`

**Interfaces:**
- Produces: the 3-arg constructor becomes `WriteSafetyService(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime, string? auditDirectory = null)` — existing 2-arg calls compile unchanged. `AppendAudit` behavior on failure: still never throws, now writes one line to stderr. (stderr is safe in an MCP stdio server: only stdout carries protocol frames, and `Program.cs` already routes host logging to stderr.)

- [ ] **Step 1: Write the failing tests**

Add to `WriteSafetyServiceTests.cs`:

```csharp
    [Fact]
    public void AppendAudit_WritesJsonlRecordToConfiguredDirectory()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tia-mcp-audit-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var now = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
            var safety = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10), dir);

            safety.AppendAudit("apply_write_batch", null, new { }, new { }, "state", "result");

            var auditPath = Path.Combine(dir, "2026-07-15.jsonl");
            Assert.True(File.Exists(auditPath));
            Assert.Contains("apply_write_batch", File.ReadAllText(auditPath));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void AppendAudit_LogsFailureToStdErrInsteadOfThrowing()
    {
        var blockingFile = Path.GetTempFileName();
        var originalError = Console.Error;
        var capture = new StringWriter();
        Console.SetError(capture);
        try
        {
            var safety = new WriteSafetyService(() => DateTimeOffset.UtcNow, TimeSpan.FromMinutes(10), blockingFile);

            safety.AppendAudit("apply_write_batch", null, new { }, new { }, "state", "result");
        }
        finally
        {
            Console.SetError(originalError);
            File.Delete(blockingFile);
        }

        Assert.Contains("failed to write audit record", capture.ToString());
    }
```

(`blockingFile` is an existing FILE passed as the audit *directory* — `Directory.CreateDirectory` throws `IOException`, forcing the failure path deterministically.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~WriteSafetyServiceTests"`
Expected: both FAIL to compile (no 3-arg ctor with `auditDirectory`). RED confirmed.

- [ ] **Step 3: Implement**

3a. Replace the 2-parameter constructor:

```csharp
    public WriteSafetyService(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime)
    {
        _getUtcNow = getUtcNow;
        _tokenLifetime = tokenLifetime;
    }
```

with:

```csharp
    public WriteSafetyService(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime, string? auditDirectory = null)
    {
        _getUtcNow = getUtcNow;
        _tokenLifetime = tokenLifetime;
        _auditDirectoryOverride = auditDirectory;
    }
```

and add the field next to `_tokenLifetime`:

```csharp
    private readonly string? _auditDirectoryOverride;
```

3b. In `AppendAudit`, replace:

```csharp
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TiaMcpServer",
                "audit");
```

with:

```csharp
            var directory = _auditDirectoryOverride ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TiaMcpServer",
                "audit");
```

3c. Replace the silent catch:

```csharp
        catch
        {
            // Audit failures must not hide the write result from the MCP caller.
        }
```

with:

```csharp
        catch (Exception ex)
        {
            // Audit failures must not hide the write result from the MCP caller,
            // but a broken audit trail must be visible to the operator.
            Console.Error.WriteLine($"TiaMcpServer: failed to write audit record for '{toolName}': {ex.Message}");
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q --filter "FullyQualifiedName~WriteSafetyServiceTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer/Safety/WriteSafetyService.cs TiaMcpServer.Tests/WriteSafetyServiceTests.cs
git commit -m "fix: log audit-write failures to stderr instead of swallowing them"
```

---

### Task 7: add_network_device fails when TIA Portal rejects device creation

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs`

**Interfaces:**
- Consumes: worker `Program.Execute()` catches `EngineeringException` and returns `WorkerResponse.Success=false` with `"TIA Portal operation failed: {message}"` — that is the delivery mechanism for this fix.
- Produces: `Create(...)` now throws `EngineeringException` upward when `CreateWithItem` fails, instead of returning a "successful" `AddDeviceResultInfo` with a buried warning. The `Warnings` list remains for genuinely optional post-creation reads.

No unit test is possible (net48 + Siemens types; see Global Constraints). Verification is build + the documented behavior chain below.

- [ ] **Step 1: Implement**

In `NetworkDeviceCreator.cs`, replace:

```csharp
        Device device;
        try
        {
            device = project.Devices.CreateWithItem(typeIdentifier, deviceName, deviceItemName);
        }
        catch (EngineeringException ex)
        {
            result.Warnings.Add($"TIA Portal could not create device '{deviceName}': {ex.Message}");
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create network device '{deviceName}' from type identifier '{typeIdentifier}': {ex.Message}",
                ex);
        }
```

with:

```csharp
        Device device;
        try
        {
            // EngineeringException propagates on purpose: a failed CreateWithItem must surface as
            // WorkerResponse.Success=false (via Program.Execute), never as a success with a warning.
            device = project.Devices.CreateWithItem(typeIdentifier, deviceName, deviceItemName);
        }
        catch (Exception ex) when (ex is not EngineeringException)
        {
            throw new InvalidOperationException(
                $"Failed to create network device '{deviceName}' from type identifier '{typeIdentifier}': {ex.Message}",
                ex);
        }
```

- [ ] **Step 2: Verify by building the full solution**

Run: `dotnet build TiaMcpServer.sln -m:1`
Expected: `Build succeeded.` with 0 warnings/errors.

Behavior chain to confirm by reading (not running): `CreateWithItem` throws `EngineeringException` → `Program.Execute` catch returns `Failure("TIA Portal operation failed: ...")` → `OpennessWorkerClient` returns `"Error: TIA Portal operation failed: ..."` → `BatchExecutionEngine.IsFailure` sees the `Error:` prefix → apply stops and later items are marked `skipped` → the audit record shows the failure. Every link already exists; this task only stops the exception from being converted to a warning.

- [ ] **Step 3: Run the full test suite (regression check on linked host files)**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q`
Expected: PASS (no host files changed; this confirms the solution still composes).

- [ ] **Step 4: Commit**

```powershell
git add TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs
git commit -m "fix: fail add_network_device when TIA Portal rejects device creation"
```

---

### Task 8: configure_network_device fails when no requested setting was applied

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`

**Interfaces:**
- Consumes: same `Program.Execute()` mapping as Task 7 (`InvalidOperationException` → `WorkerResponse.Success=false`).
- Produces: `Configure(...)` throws `InvalidOperationException` when the caller requested settings but ALL of them were skipped. Partial success (some applied, some skipped) still returns the result with `SkippedSettings` populated.

No unit test possible (net48). Verification is build + reasoning.

- [ ] **Step 1: Implement**

1a. Add this private helper to `NetworkDeviceConfigurator.cs` (place it after the `Configure` method):

```csharp
    /// <summary>
    /// A result where every requested setting was skipped is a failed operation, not a success
    /// with fine print — throw so Program.Execute reports WorkerResponse.Success=false.
    /// </summary>
    private static ConfigureNetworkDeviceResultInfo FinalizeResult(
        ConfigureNetworkDeviceResultInfo result,
        string deviceName)
    {
        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count > 0)
        {
            var reasons = string.Join(" ", result.SkippedSettings.Select(kv => $"{kv.Key}: {kv.Value}"));
            throw new InvalidOperationException(
                $"No requested settings could be applied to device '{deviceName}'. {reasons}");
        }

        return result;
    }
```

(`Select` needs `System.Linq`, which this file already uses — `FirstOrDefault` in `ConnectIoSystem`.)

1b. In `Configure`, replace the early return inside the IO-system branch:

```csharp
                if (subnetRequested)
                {
                    result.SkippedSettings["IoSystemName"] = "Requested subnet was not connected, so IO system lookup was skipped.";
                    return result;
                }
```

with:

```csharp
                if (subnetRequested)
                {
                    result.SkippedSettings["IoSystemName"] = "Requested subnet was not connected, so IO system lookup was skipped.";
                    return FinalizeResult(result, deviceName);
                }
```

1c. Replace the final return of `Configure`:

```csharp
        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count == 0)
        {
            result.Messages.Add("No network settings were provided.");
        }

        return result;
```

with:

```csharp
        if (result.AppliedSettings.Count == 0 && result.SkippedSettings.Count == 0)
        {
            result.Messages.Add("No network settings were provided.");
        }

        return FinalizeResult(result, deviceName);
```

- [ ] **Step 2: Verify by building the full solution**

Run: `dotnet build TiaMcpServer.sln -m:1`
Expected: `Build succeeded.` with 0 warnings/errors.

Case check (by reading): request `ipAddress` only, `SetAttribute` throws → `SkippedSettings["Address"]` set, `AppliedSettings` empty → `FinalizeResult` throws → tool returns `Error:` and the batch stops. Request `ipAddress` + `subnetName` where IP applies but subnet is missing → `AppliedSettings` has 1 entry → normal result with the skip recorded. Zero requested settings → `Messages` note, no throw (both dictionaries empty).

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test TiaMcpServer.Tests --nologo -v q`
Expected: PASS.

- [ ] **Step 4: Commit**

```powershell
git add TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs
git commit -m "fix: fail configure_network_device when no requested setting was applied"
```

---

### Task 9: Serialize worker access with a semaphore

**Files:**
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs` (method `SendAsync`)

**Interfaces:**
- Produces: no signature changes. `SendAsync` (private static) now admits one worker process at a time. Concurrent MCP tool calls queue instead of racing two Openness attachments against the same TIA Portal instance.

No behavioral unit test: `OpennessWorkerClient.cs` is linked into the test project, but exercising `SendAsync` spawns a real worker process — and in this repo checkout `LocateWorkerExecutable` walks parent directories and would find the real net48 worker, attaching to a live TIA Portal. A fake-worker test harness is planned Phase 1/2 work; until then verification is build + suite + review.

- [ ] **Step 1: Implement**

1a. Add the gate as a field of `OpennessWorkerClient` (next to the other private static fields, e.g. directly above `SendAsync`):

```csharp
    // Siemens Openness is not safe for concurrent multi-process access to one TIA Portal
    // instance; serialize every worker invocation until the persistent-worker rework lands.
    private static readonly SemaphoreSlim WorkerGate = new(1, 1);
```

1b. Wrap the body of `SendAsync`. Replace:

```csharp
    private static async Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        var workerPath = LocateWorkerExecutable();
```

with:

```csharp
    private static async Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        await WorkerGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await SendUnguardedAsync(request).ConfigureAwait(false);
        }
        finally
        {
            WorkerGate.Release();
        }
    }

    private static async Task<WorkerResponse> SendUnguardedAsync(WorkerRequest request)
    {
        var workerPath = LocateWorkerExecutable();
```

The rest of the original `SendAsync` body (from `var startInfo = new ProcessStartInfo` through `return response ?? throw ...;` and its closing brace) becomes the body of `SendUnguardedAsync`, character-for-character unchanged.

- [ ] **Step 2: Build and run the full suite**

Run: `dotnet build TiaMcpServer.sln -m:1`
Expected: `Build succeeded.`

Run: `dotnet test TiaMcpServer.Tests --nologo -v q`
Expected: PASS.

- [ ] **Step 3: Review the timeout path for gate leaks**

Confirm by reading the moved code: every exit from `SendUnguardedAsync` (timeout `throw`, empty-response `throw`, JSON `throw`, normal return) unwinds through the `finally` in `SendAsync`, so `WorkerGate.Release()` always runs. The 5-minute `WorkerTimeout` inside the gate means a hung worker blocks later calls for at most that long — acceptable until the persistent worker lands.

- [ ] **Step 4: Commit**

```powershell
git add TiaMcpServer/Worker/OpennessWorkerClient.cs
git commit -m "fix: serialize Openness worker access with a semaphore"
```

---

### Task 10: Final verification pass

**Files:** none modified (verification only; `graphify-out/` refresh is generated).

- [ ] **Step 1: Full build + full suite from a clean slate**

```powershell
dotnet build TiaMcpServer.sln -m:1
dotnet test TiaMcpServer.Tests --nologo -v q
```

Expected: build succeeds with 0 warnings; every test passes (146 pre-existing + 13 added by Tasks 1–6 = 159; adjust if upstream merges add more — PR #4 landed mid-planning and may have grown the baseline).

- [ ] **Step 2: Live smoke test (only if TIA Portal V21 with a disposable project is running)**

Optional but valuable — the two worker fixes (Tasks 7–8) have no unit coverage. Use MCP Inspector per README "Local MCP Sandbox Testing":

1. `preview_write_batch` → `apply_write_batch` with one `add_network_device` item using a bogus `typeIdentifier` (e.g. `OrderNumber:0000/V0.0`) → expect the item to report **failure** (not success-with-warning).
2. `apply_write_batch` with a stale token (wait past preview, modify the project in TIA first) → expect the rejection message to include "single-use and expire after 10 minutes" and the preview tool name.
3. `execute_read_batch` with `{ "operationId": "x", "operation": "teleport_plc" }` → expect the error to list valid read operations.

If TIA Portal is not available, state that explicitly in the completion report — do not claim these were verified.

- [ ] **Step 3: Refresh the knowledge graph**

```powershell
graphify update .
```

Expected: graph rebuilt from current commit (project CLAUDE.md requirement).

- [ ] **Step 4: Update the improvement-plan checklist**

In `priv/IMPROVEMENT_PLAN.md`, mark items 0.1–0.6, 1.2, and 2.2 as done (append `— DONE 2026-07-15` to each row's Why column or strike the rows). Commit:

```powershell
git add priv/IMPROVEMENT_PLAN.md graphify-out
git commit -m "chore: mark phase 0 + false-success + concurrency items done"
```

---

## Out of scope (explicitly)

- Implementing user-constant rename (Task 4 documents its absence instead; implementing it needs worker + client + invoker changes — schedule with Phase 1).
- The persistent worker process (Phase 2.1), payload bounds (2.3), structured error type (1.1), unknown-JSON-property rejection (1.6) — all deliberately excluded from this plan.
- Any push/PR/release action — commits stay local until the user says otherwise.
