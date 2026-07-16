# Phase 2 — Structural Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep one worker process alive across requests (single TIA attach, managed project open/close, restart-on-crash), bound read payloads with `depth`/`startPath`/`maxResults` plus a server-side byte budget, collapse the six lifecycle preview/apply tool pairs into six self-previewing tools (16 → 10 tools), and make safety tokens evict on expiry and get validated before the expensive pre-apply state read.

**Architecture:** The host (`TiaMcpServer`, net8.0) talks to a net48 worker over stdin/stdout JSON lines. The worker already loops over stdin and reuses one `TiaPortalSession` — today the host kills it by closing stdin after every request. We introduce `PersistentWorkerTransport` (host) that starts the worker once, keeps stdin open, serializes requests behind an instance `SemaphoreSlim`, and restarts on crash/timeout/desync. Per-request degradation messages move from racy stderr capture into a new `Warnings` field on `WorkerResponse` (the worker captures its own `Console.Error` per request); the real stderr stream becomes log-and-crash-diagnostics only. Read bounding is split into pure logic (`ProjectTreeFilter` in Contracts, `BatchPayloadBudget` in host — both fully unit-testable) and thin worker plumbing. The lifecycle collapse reuses the existing token machinery unchanged: a write tool called *without* a token returns the preview + token; called *with* token + `confirm=true` it applies.

**Tech Stack:** C# — net8.0 host + tests (xunit), netstandard2.0 contracts, net48 worker (Siemens Openness V21, stub references for CI builds). System.Text.Json everywhere.

**Covers IMPROVEMENT_PLAN.md items:** 2.1, 2.3, 2.4, 2.5 (2.2 already done). Also closes the two testing gaps deferred from Phase 1 (timeout path, persistent-worker restart logic).

## Global Constraints

- Target frameworks are fixed: host/tests net8.0, `TiaMcpServer.Contracts` netstandard2.0, `TiaMcpServer.OpennessWorker` net48 (`LangVersion=latest`, nullable enabled — modern syntax is fine in all projects).
- The test project compiles host sources via `<Compile Include>` links (NOT a ProjectReference) — every NEW host file consumed by linked files must also be linked into `TiaMcpServer.Tests.csproj`. Contracts files need no link (ProjectReference).
- Tests must never require Siemens DLLs or a running TIA Portal. Worker-project changes are verified by `dotnet build TiaMcpServer.sln` only (stub references auto-selected when TIA Portal V21 is absent).
- Host must never write to stdout except MCP protocol — all logging goes to stderr.
- Agent-facing failure text keeps the `"Error: "` prefix rendering via `WorkerCallResult.ToText()`.
- Baseline: **179 green tests** (verified 2026-07-16). Every task ends with the full suite green.
- Build: `dotnet build TiaMcpServer.sln`. Test: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Commit format: `<type>: <description>` (feat, fix, refactor, test, chore, docs). Work happens on branch `26-07-16-improvement-phase2`.
- **Anchor edits by code strings, not line numbers** — line numbers in this plan are orientation only; the files move as tasks land.
- Immutability: transforms return new objects (`record with`, new lists); never mutate inputs.

## Scoping decisions (read before executing)

1. **Warnings ride the protocol, not stderr.** In a persistent worker, correlating stderr lines to a specific request is inherently racy (TIA notifications fire between requests). The worker swaps `Console.Error` for a per-request buffer and returns captured lines as `WorkerResponse.Warnings`. Real stderr is still pumped by the host — for `ILogger` and for crash diagnostics — but never becomes per-request warnings anymore.
2. **No automatic retry after a mid-request crash.** A request that dies without a response fails with an actionable error; the *next* request gets a fresh process. Auto-retrying could double-apply a write.
3. **Managed project close only closes projects the worker itself opened.** `TiaPortalSession` tracks `_projectOpenedByWorker`; a project the user already had open in TIA Portal (picked up by `Connect()`'s `FirstOrDefault()`) is never closed by us.
4. **2.4 subsumes the Phase 3.1 drift bomb for lifecycle ops.** With preview and apply collapsed into one method, `target`/`requestedInput` are built exactly once per tool — no duplicated hand-built objects left to drift. No separate descriptor infrastructure is needed (YAGNI).
5. **Existing safety tokens stay compatible.** Previews always stored `toolName = "open_project"` etc. (the apply tool's name), so collapsing does not change token binding; only the human-facing "call preview_X again" hints change.
6. **Bounds enforcement is layered:** `maxResults`/`depth`/`startPath` reduce work in the worker; `BatchPayloadBudget` is the host-side backstop that guarantees a bounded response even when the caller asks for everything. Only `execute_read_batch` gets the budget (write results are small confirmations).
7. **New request fields are validated for scope**: `depth`/`startPath` only on `browse_project_tree`, `maxResults` only on `search_equipment_catalog`/`read_cross_references`. A misplaced field is a hard aggregated validation error, not a silent ignore (consistent with items 0.2/1.6).
8. **2.5 ordering inside apply:** cheap envelope validation (token exists, not expired, same tool/project/target/input — *without consuming*) runs before the N-call current-state read; the existing atomic `ValidateAndConsume` still runs after the read, unchanged, so consume-with-state semantics stay intact.

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `TiaMcpServer/Safety/WriteSafetyService.cs` | Modify | Evict expired tokens on `CreatePreview`; new non-consuming `ValidateEnvelope`; optional `instructions` in preview JSON; `ActiveTokenCount` (internal, for tests) |
| `TiaMcpServer/Batch/BatchTools.cs` | Modify | Envelope precheck before state read; budget wiring; `instructions` on batch preview |
| `TiaMcpServer.Contracts/WorkerResponse.cs` | Modify | Add `Warnings` |
| `TiaMcpServer.OpennessWorker/Program.cs` | Modify | Per-request stderr capture → `Warnings`; pass new read-bound fields |
| `TiaMcpServer/Worker/PersistentWorkerTransport.cs` | Create | Long-lived process, request serialization, timeout/crash/desync restart, stderr pump |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | Modify | Use transport; `IDisposable`; configurable timeout; warnings from response; new read params |
| `TiaMcpServer.FakeWorker/Program.cs` | Modify | Loop over stdin; scenarios for reuse/crash/hang/warnings |
| `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs` | Modify | Rewrite for persistent transport (reuse, restart, timeout, warnings) |
| `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs` | Modify | Reuse already-open project; close only worker-opened projects; stale-handle recovery |
| `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs` | Modify | Use `MarkProjectClosed()` |
| `TiaMcpServer.Contracts/ProjectTreeFilter.cs` | Create | Pure startPath/depth filtering of `ProjectTreeNode` trees |
| `TiaMcpServer.Contracts/WorkerRequest.cs` | Modify | Add `Depth`, `StartPath`, `MaxResults` |
| `TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs` | Modify | `maxResults` cap (default 50) + truncation notice |
| `TiaMcpServer.OpennessWorker/Openness/CrossReferenceReader.cs` | Modify | `maxResults` source cap + per-PLC truncation message |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | Modify | Add `Depth`, `StartPath`, `MaxResults` with load-bearing descriptions |
| `TiaMcpServer/Batch/BatchOperationCatalog.cs` | Modify | Scope + range validation for the new fields |
| `TiaMcpServer/Batch/BatchWorkerInvoker.cs` | Modify | Pass new fields through |
| `TiaMcpServer/Batch/BatchPayloadBudget.cs` | Create | Pure per-item truncation + batch byte budget |
| `TiaMcpServer/Batch/BatchOperationResult.cs` | Modify | Add `Omitted` status |
| `TiaMcpServer/Batch/BatchResultFormatter.cs` | Modify | `omitted` count; `success` accounts for omissions |
| `TiaMcpServer/Safety/WriteSafetyTooling.cs` | Modify | Pass `instructions` through `CreatePreview` |
| `TiaMcpServer/Tools/ProjectLifecycleTools.cs` | Modify | Delete 6 preview tools; 6 write tools become self-previewing |
| `TiaMcpServer.Tests/*` | Create/Modify | New: `ProjectTreeFilterTests`, `BatchPayloadBudgetTests`; updates to safety/batch/lifecycle/integration tests |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Modify | Link `PersistentWorkerTransport.cs`, `BatchPayloadBudget.cs` |
| `README.md`, `docs/IMPROVEMENT_PLAN.md` | Modify | 10-tool surface, new flow, new params, mark items DONE |

---

### Task 1: Safety-token eviction sweep (item 2.5a)

**Files:**
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`
- Test: `TiaMcpServer.Tests/WriteSafetyServiceTests.cs` (append)

**Interfaces:**
- Consumes: existing `WriteSafetyService` constructor `(Func<DateTimeOffset> getUtcNow, TimeSpan tokenLifetime, string? auditDirectory = null)`.
- Produces: `internal int ActiveTokenCount { get; }` and eviction-on-`CreatePreview` behavior. (Tests compile host sources into the test assembly, so `internal` is directly visible to tests.)

- [ ] **Step 1: Create the branch**

```powershell
git checkout main && git pull && git checkout -b 26-07-16-improvement-phase2
```

- [ ] **Step 2: Write the failing tests**

Append to `TiaMcpServer.Tests/WriteSafetyServiceTests.cs` (inside the existing test class):

```csharp
    [Fact]
    public void CreatePreview_EvictsExpiredTokens()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));

        service.CreatePreview("apply_write_batch", null, new { a = 1 }, "s", new { b = 1 }, "state-1");
        service.CreatePreview("apply_write_batch", null, new { a = 2 }, "s", new { b = 2 }, "state-2");
        Assert.Equal(2, service.ActiveTokenCount);

        now = now.AddMinutes(11);
        service.CreatePreview("apply_write_batch", null, new { a = 3 }, "s", new { b = 3 }, "state-3");

        // The two expired tokens were swept; only the fresh one remains.
        Assert.Equal(1, service.ActiveTokenCount);
    }

    [Fact]
    public void CreatePreview_KeepsUnexpiredTokens()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));

        service.CreatePreview("apply_write_batch", null, new { a = 1 }, "s", new { b = 1 }, "state-1");
        now = now.AddMinutes(5);
        service.CreatePreview("apply_write_batch", null, new { a = 2 }, "s", new { b = 2 }, "state-2");

        Assert.Equal(2, service.ActiveTokenCount);
    }
```

Note: the existing tests in this file construct the service the same way; match the file's existing `using` block (it already imports `TiaMcpServer.Safety` and `Xunit`).

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~WriteSafetyServiceTests" -v q`
Expected: FAIL — `'WriteSafetyService' does not contain a definition for 'ActiveTokenCount'` (compile error counts as the RED step).

- [ ] **Step 4: Implement eviction**

In `TiaMcpServer/Safety/WriteSafetyService.cs`, inside `CreatePreview`, add the sweep as the first statement (anchor: the line `var token = CreateToken();`):

```csharp
    public string CreatePreview(
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        string currentState,
        string? diff = null)
    {
        EvictExpiredTokens();

        var token = CreateToken();
```

Then add these members after the `ValidateAndConsume` method:

```csharp
    /// <summary>Number of live (unconsumed, possibly expired) tokens. Test hook.</summary>
    internal int ActiveTokenCount => _tokens.Count;

    /// <summary>
    /// Drops expired tokens so an abandoned preview cannot grow memory forever.
    /// Swept on every CreatePreview — no timer needed; expiry is still re-checked on consume.
    /// </summary>
    private void EvictExpiredTokens()
    {
        var now = _getUtcNow();
        foreach (var pair in _tokens)
        {
            if (now > pair.Value.ExpiresAtUtc)
            {
                _tokens.TryRemove(pair.Key, out _);
            }
        }
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (181 tests).

- [ ] **Step 6: Commit**

```powershell
git add TiaMcpServer/Safety/WriteSafetyService.cs TiaMcpServer.Tests/WriteSafetyServiceTests.cs
git commit -m "feat: evict expired safety tokens on preview creation"
```

---

### Task 2: Envelope validation before the expensive state read (item 2.5b)

**Files:**
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs`
- Modify: `TiaMcpServer/Batch/BatchTools.cs`
- Test: `TiaMcpServer.Tests/WriteSafetyServiceTests.cs`, `TiaMcpServer.Tests/BatchToolsTests.cs` (append)

**Interfaces:**
- Consumes: `WriteSafetyService.Shared`, `BatchSafetySnapshot.BuildTargets(operations)`, `BatchSafetySnapshot.ResolveProjectPath(operations)` (both already used in `BatchTools`).
- Produces: `public WriteSafetyValidationResult ValidateEnvelope(string? safetyToken, string toolName, string? projectPath, object target, object requestedInput, string? previewToolName = null)` — same rejection messages as `ValidateAndConsume`, but does NOT remove the token and does NOT check `currentState`.

- [ ] **Step 1: Write the failing tests**

Append to `TiaMcpServer.Tests/WriteSafetyServiceTests.cs`:

```csharp
    [Fact]
    public void ValidateEnvelope_AcceptsMatchingTokenWithoutConsumingIt()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));
        var previewJson = service.CreatePreview("apply_write_batch", "C:\\p.ap21", new { t = 1 }, "s", new { i = 1 }, "state");
        var token = ReadToken(previewJson);

        var first = service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 });
        var second = service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 });

        Assert.True(first.IsValid);
        Assert.True(second.IsValid);
        Assert.Equal(1, service.ActiveTokenCount);

        // The full consume still works afterwards.
        var consume = service.ValidateAndConsume(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 }, "state");
        Assert.True(consume.IsValid);
        Assert.Equal(0, service.ActiveTokenCount);
    }

    [Fact]
    public void ValidateEnvelope_RejectsUnknownExpiredAndMismatchedTokens()
    {
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var service = new WriteSafetyService(() => now, TimeSpan.FromMinutes(10));
        var previewJson = service.CreatePreview("apply_write_batch", "C:\\p.ap21", new { t = 1 }, "s", new { i = 1 }, "state");
        var token = ReadToken(previewJson);

        Assert.False(service.ValidateEnvelope("bogus", "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "other_tool", "C:\\p.ap21", new { t = 1 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\other.ap21", new { t = 1 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 2 }, new { i = 1 }).IsValid);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 2 }).IsValid);

        now = now.AddMinutes(11);
        Assert.False(service.ValidateEnvelope(token, "apply_write_batch", "C:\\p.ap21", new { t = 1 }, new { i = 1 }).IsValid);
    }

    private static string ReadToken(string previewJson)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(previewJson);
        return doc.RootElement.GetProperty("safetyToken").GetString()!;
    }
```

Append to `TiaMcpServer.Tests/BatchToolsTests.cs` (inside the existing class; it already imports `TiaMcpServer.Batch`):

```csharp
    [Fact]
    public async Task ApplyWriteBatch_RejectsBadTokenBeforeReadingCurrentState()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "op-1", Operation = "start_plc" }
        };

        // workerClient is null: if the token envelope were checked AFTER the state read,
        // this call would throw NullReferenceException instead of returning the token error.
        var result = await BatchTools.ApplyWriteBatch(
            workerClient: null!,
            operations,
            confirm: true,
            safetyToken: "bogus-token");

        Assert.Contains("Safety token", result);
        Assert.Contains("preview_write_batch", result);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "ValidateEnvelope|ApplyWriteBatch_RejectsBadToken" -v q`
Expected: FAIL — no `ValidateEnvelope` member; the BatchTools test dies with `NullReferenceException`.

- [ ] **Step 3: Implement `ValidateEnvelope`**

In `TiaMcpServer/Safety/WriteSafetyService.cs`, add after `ValidateAndConsume`:

```csharp
    /// <summary>
    /// Cheap pre-check of everything a token binds EXCEPT current project state: existence,
    /// expiry, tool, project path, target, and requested input. Does not consume the token.
    /// Callers still must run <see cref="ValidateAndConsume"/> (which re-checks everything
    /// atomically) after reading current state; this exists so a dead token is rejected
    /// before the expensive pre-apply state read.
    /// </summary>
    public WriteSafetyValidationResult ValidateEnvelope(
        string? safetyToken,
        string toolName,
        string? projectPath,
        object target,
        object requestedInput,
        string? previewToolName = null)
    {
        if (string.IsNullOrWhiteSpace(safetyToken))
        {
            return Rejected("Safety token required.", previewToolName);
        }

        if (!_tokens.TryGetValue(safetyToken, out var entry))
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

        return WriteSafetyValidationResult.Valid(requestedInputHash, entry.CurrentStateHash);
    }
```

- [ ] **Step 4: Wire the precheck into `ApplyWriteBatch`**

In `TiaMcpServer/Batch/BatchTools.cs`, `ApplyWriteBatch`: move the `targets`/`projectPath` computation ABOVE the state read and insert the envelope check. Replace this block (anchor: `var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);` inside `ApplyWriteBatch`, and the two lines computing `targets`/`projectPath` below it):

```csharp
        var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);
        if (snapshot.Error is not null)
        {
            return BatchResultFormatter.Error(ApplyToolName, $"Could not read current state before write. {snapshot.Error}");
        }

        var targets = BatchSafetySnapshot.BuildTargets(operations);
        var projectPath = BatchSafetySnapshot.ResolveProjectPath(operations);
```

with:

```csharp
        var targets = BatchSafetySnapshot.BuildTargets(operations);
        var projectPath = BatchSafetySnapshot.ResolveProjectPath(operations);

        // Reject dead/mismatched tokens BEFORE the expensive per-item current-state read.
        var envelope = WriteSafetyService.Shared.ValidateEnvelope(
            safetyToken,
            ApplyToolName,
            projectPath,
            targets,
            operations,
            PreviewToolName);
        if (!envelope.IsValid)
        {
            return BatchResultFormatter.Error(ApplyToolName, envelope.Error);
        }

        var snapshot = await ReadCombinedCurrentStateAsync(workerClient, operations).ConfigureAwait(false);
        if (snapshot.Error is not null)
        {
            return BatchResultFormatter.Error(ApplyToolName, $"Could not read current state before write. {snapshot.Error}");
        }
```

The later `ValidateAndConsume` call stays exactly as it is (it is the atomic consume; the envelope check is only a fast-fail).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (184 tests).

- [ ] **Step 6: Commit**

```powershell
git add TiaMcpServer/Safety/WriteSafetyService.cs TiaMcpServer/Batch/BatchTools.cs TiaMcpServer.Tests/WriteSafetyServiceTests.cs TiaMcpServer.Tests/BatchToolsTests.cs
git commit -m "feat: validate safety-token envelope before pre-apply state read"
```

---

### Task 3: Protocol warnings channel — `WorkerResponse.Warnings` + worker stderr capture (item 2.1, protocol half)

**Files:**
- Modify: `TiaMcpServer.Contracts/WorkerResponse.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`

**Interfaces:**
- Consumes: nothing new.
- Produces: `WorkerResponse.Warnings` (`List<string>?`, camelCase `warnings` on the wire, omitted when null). The worker attaches every non-empty line written to `Console.Error` *during* request handling; lines written between requests (TIA notifications, attach messages) still go to real stderr.

No host-side unit test is possible yet (net48 worker); the behavior is covered end-to-end in Task 5's integration tests via the fake worker. Verification here is build-only.

- [ ] **Step 1: Add `Warnings` to the contract**

Replace the body of `TiaMcpServer.Contracts/WorkerResponse.cs` with:

```csharp
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

public class WorkerResponse
{
    public bool Success { get; set; }

    public string? Payload { get; set; }

    public string? Error { get; set; }

    /// <summary>
    /// Non-fatal degradation notes captured from the worker's Console.Error while THIS
    /// request was being handled (e.g. "Skipping device X: access denied"). Null when none.
    /// </summary>
    public List<string>? Warnings { get; set; }
}
```

- [ ] **Step 2: Capture per-request stderr in the worker**

In `TiaMcpServer.OpennessWorker/Program.cs`, replace the `Main` method (anchor: `private static void Main()`):

```csharp
    private static void Main()
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        string? line;
        while ((line = Console.In.ReadLine()) is not null)
        {
            var response = HandleLineWithCapturedStderr(line);
            Console.Out.WriteLine(JsonSerializer.Serialize(response, JsonOptions));
            Console.Out.Flush();
        }
    }

    /// <summary>
    /// Redirects Console.Error to a per-request buffer so degradation lines ("Skipping X…")
    /// become structured response warnings instead of racy stderr in the persistent worker.
    /// Async TIA events that fire BETWEEN requests still hit the real stderr stream.
    /// </summary>
    private static WorkerResponse HandleLineWithCapturedStderr(string line)
    {
        var originalError = Console.Error;
        var buffer = new System.IO.StringWriter();
        // TIA events can write from other threads while a request runs; synchronize the buffer.
        Console.SetError(System.IO.TextWriter.Synchronized(buffer));

        WorkerResponse response;
        try
        {
            response = HandleLine(line);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var captured = SplitWarningLines(buffer.ToString());
        if (captured.Count > 0)
        {
            response.Warnings = captured;
        }

        return response;
    }

    private static List<string> SplitWarningLines(string captured)
    {
        var lines = new List<string>();
        foreach (var raw in captured.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0)
            {
                lines.Add(trimmed);
            }
        }

        return lines;
    }
```

Note: `HandleLine` itself is unchanged — its catch-all `Console.Error.WriteLine(ex)` now lands in the buffer and rides back as warnings next to the `Failure(...)` response, which is exactly the diagnostic we want.

- [ ] **Step 3: Build the solution**

Run: `dotnet build TiaMcpServer.sln`
Expected: Build succeeded (the pre-existing `ArchiveModeNames.cs` CS8602 warning is known and unrelated).

- [ ] **Step 4: Run the full suite (guard against contract regressions)**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (184 tests).

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer.Contracts/WorkerResponse.cs TiaMcpServer.OpennessWorker/Program.cs
git commit -m "feat: carry per-request worker warnings in the response protocol"
```

---

### Task 4: FakeWorker becomes persistent-capable (loop + new scenarios)

**Files:**
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs` (two payload asserts only)

**Interfaces:**
- Consumes: request JSON lines whose `projectPath` field encodes the scenario (existing convention).
- Produces, per scenario (the still-current single-shot client works against the looping fake — it closes stdin after one line, so the fake's next `ReadLine` returns null and it exits cleanly):
  - `"ok"` → `{"success":true,"payload":"{\"seq\":N}"}` where N counts requests handled by THIS process (1, 2, 3…). Proves process reuse/restart later.
  - `"ok-with-warnings"` → success + `warnings` array in the response JSON (the new protocol channel).
  - `"ok-with-stderr"` → writes 2 stderr lines, then plain success WITHOUT response warnings (proves stderr no longer becomes warnings in Task 5).
  - `"error-prefix-payload"`, `"worker-error"`, `"malformed"`, `"malformed-request"` → unchanged semantics.
  - `"silent-exit"` → writes stderr, exits the whole process without responding.
  - `"hang"` → never responds (blocks forever) — drives the timeout path.

- [ ] **Step 1: Rewrite the fake worker**

Replace the entire content of `TiaMcpServer.FakeWorker/Program.cs` with:

```csharp
using System.Text.Json;

// Scripted stand-in for TiaMcpServer.OpennessWorker used by IPC integration tests.
// Mirrors the real worker's request loop: one JSON line in, one JSON line out, until
// stdin closes. The test encodes the scenario in the request's projectPath field.
var seq = 0;
string? line;
while ((line = Console.In.ReadLine()) is not null)
{
    seq++;
    string? scenario = null;
    try
    {
        using var doc = JsonDocument.Parse(line);
        if (doc.RootElement.TryGetProperty("projectPath", out var p))
        {
            scenario = p.GetString();
        }
    }
    catch (JsonException)
    {
        scenario = "malformed-request";
    }

    switch (scenario)
    {
        case "ok":
            // seq proves whether two requests hit the same process (2.1 reuse/restart tests).
            Respond($$"""{"success":true,"payload":"{\"seq\":{{seq}}}"}""");
            break;
        case "ok-with-warnings":
            Respond("""{"success":true,"payload":"{\"hello\":true}","warnings":["Skipping device 'X' while reading hardware configuration: access denied.","Skipping subnet 'Y' while reading hardware configuration: not supported."]}""");
            break;
        case "ok-with-stderr":
            // Stderr between/during requests is host-log-only now; it must NOT surface as warnings.
            Console.Error.WriteLine("orphan stderr line: attach diagnostics");
            Console.Error.Flush();
            Respond("""{"success":true,"payload":"{\"hello\":true}"}""");
            break;
        case "error-prefix-payload":
            Respond("""{"success":true,"payload":"Error: literal payload text, not a failure"}""");
            break;
        case "worker-error":
            Respond("""{"success":false,"error":"boom"}""");
            break;
        case "malformed":
            Console.Out.WriteLine("this is not json");
            Console.Out.Flush();
            break;
        case "silent-exit":
            Console.Error.WriteLine("worker crashed during attach");
            Console.Error.Flush();
            return;
        case "hang":
            Thread.Sleep(Timeout.Infinite);
            break;
        default:
            Respond($$"""{"success":false,"error":"unknown scenario '{{scenario}}'"}""");
            break;
    }
}

void Respond(string json)
{
    Console.Out.WriteLine(json);
    Console.Out.Flush();
}
```

- [ ] **Step 2: Update the two payload asserts that change**

In `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`:

In `Success_ReturnsStructuredPayload`, replace:

```csharp
        Assert.Equal("{\"hello\":true}", result.Payload);
```

with:

```csharp
        Assert.Equal("{\"seq\":1}", result.Payload);
```

`StderrLines_SurfaceAsWarnings` currently expects the 2 stderr lines the old fake emitted. The reworked fake's `"ok-with-stderr"` emits ONE stderr line with new text (the still-current single-shot client turns stderr into warnings, so the scenario keeps working until Task 5 flips that). Update the test to:

```csharp
    [Fact]
    public async Task StderrLines_SurfaceAsWarnings()
    {
        var result = await CreateClient().GetProjectStatusAsync("ok-with-stderr");

        Assert.True(result.Success);
        Assert.Single(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("orphan stderr line"));
    }
```

(This test is rewritten again in Task 5 when warnings switch to the response channel.)

- [ ] **Step 3: Run the suite**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (184 tests) — the old client tolerates the looping fake because closing stdin ends its loop.

- [ ] **Step 4: Commit**

```powershell
git add TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs
git commit -m "test: make the fake worker loop like the real one and add persistence scenarios"
```

---

### Task 5: `PersistentWorkerTransport` + client flip (item 2.1, host half)

**Files:**
- Create: `TiaMcpServer/Worker/PersistentWorkerTransport.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (link the new file)
- Test: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs` (rewrite)

**Interfaces:**
- Consumes: `WorkerRequest`, `WorkerResponse` (with `Warnings` from Task 3), `WorkerCallResult`.
- Produces:
  - `public sealed class PersistentWorkerTransport : IDisposable` with ctor `(string workerExecutablePath, TimeSpan requestTimeout, ILogger? logger = null)` and `Task<WorkerResponse> SendAsync(WorkerRequest request)`. Throws: `Win32Exception` (launch), `TimeoutException` (no response in time; process killed), `InvalidOperationException` (crash/empty response; process killed), `JsonException` (protocol desync; process killed), `IOException` (broken pipe; process killed).
  - `OpennessWorkerClient` ctor becomes `(ProjectSessionBinding projectSessionBinding, ILogger<OpennessWorkerClient>? logger = null, string? workerExecutablePath = null, TimeSpan? requestTimeout = null)`; class implements `IDisposable`. All 30+ public `*Async` wrapper methods keep their exact signatures.
  - Production stays serialized: one singleton client → one transport → one `SemaphoreSlim(1,1)` gate. The old static `WorkerGate` and `SendAsync`/`SendUnguardedAsync`/`SplitStderrLines`/`TryKill` members are deleted.

- [ ] **Step 1: Write the failing integration tests**

Replace the entire content of `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs` with:

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Drives the real persistent IPC pipeline against TiaMcpServer.FakeWorker. Each test owns
/// its client (and therefore its worker process); clients are disposed so no fake worker
/// outlives its test. One class so xunit runs these sequentially.
/// </summary>
public class OpennessWorkerClientIntegrationTests
{
    private static string LocateFakeWorker()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            foreach (var configuration in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(
                    directory.FullName,
                    "TiaMcpServer.FakeWorker", "bin", configuration, "net8.0",
                    "TiaMcpServer.FakeWorker.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            directory = directory.Parent!;
        }

        throw new FileNotFoundException("TiaMcpServer.FakeWorker.exe not found; build the solution first.");
    }

    private static OpennessWorkerClient CreateClient(string? workerPath = null, TimeSpan? requestTimeout = null)
        => new(
            new ProjectSessionBinding(null),
            logger: null,
            workerExecutablePath: workerPath ?? LocateFakeWorker(),
            requestTimeout: requestTimeout);

    [Fact]
    public async Task Success_ReturnsStructuredPayload()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("ok");

        Assert.True(result.Success);
        Assert.Equal("{\"seq\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task PersistentWorker_ReusesOneProcessAcrossRequests()
    {
        using var client = CreateClient();

        var first = await client.GetProjectStatusAsync("ok");
        var second = await client.GetProjectStatusAsync("ok");

        Assert.Equal("{\"seq\":1}", first.Payload);
        // seq=2 can only happen if the SAME fake worker process handled both requests.
        Assert.Equal("{\"seq\":2}", second.Payload);
    }

    [Fact]
    public async Task CrashedWorker_FailsTheRequestAndRestartsForTheNext()
    {
        using var client = CreateClient();

        var crashed = await client.GetProjectStatusAsync("silent-exit");
        var recovered = await client.GetProjectStatusAsync("ok");

        Assert.False(crashed.Success);
        Assert.Contains("worker crashed during attach", crashed.Error);
        Assert.True(recovered.Success);
        // A fresh process restarts its request counter.
        Assert.Equal("{\"seq\":1}", recovered.Payload);
    }

    [Fact]
    public async Task HangingWorker_TimesOutAndRestartsForTheNext()
    {
        using var client = CreateClient(requestTimeout: TimeSpan.FromSeconds(2));

        var timedOut = await client.GetProjectStatusAsync("hang");
        var recovered = await client.GetProjectStatusAsync("ok");

        Assert.False(timedOut.Success);
        Assert.Contains("did not respond", timedOut.Error);
        Assert.True(recovered.Success);
        Assert.Equal("{\"seq\":1}", recovered.Payload);
    }

    [Fact]
    public async Task ResponseWarnings_SurfaceOnTheResult()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("ok-with-warnings");

        Assert.True(result.Success);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("Skipping device 'X'"));
    }

    [Fact]
    public async Task OrphanStderr_DoesNotBecomeWarnings()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("ok-with-stderr");

        Assert.True(result.Success);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task PayloadStartingWithErrorPrefix_IsNotMisclassified()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("error-prefix-payload");

        Assert.True(result.Success);
        Assert.StartsWith("Error:", result.Payload);
    }

    [Fact]
    public async Task WorkerReportedError_IsStructuredFailure()
    {
        using var client = CreateClient();
        var result = await client.GetProjectStatusAsync("worker-error");

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
        Assert.Equal("Error: boom", result.ToText());
    }

    [Fact]
    public async Task MalformedResponse_FailsAndRestartsForTheNext()
    {
        using var client = CreateClient();

        var malformed = await client.GetProjectStatusAsync("malformed");
        var recovered = await client.GetProjectStatusAsync("ok");

        Assert.False(malformed.Success);
        Assert.NotNull(malformed.Error);
        // The desynced process was killed; a fresh one serves the next request.
        Assert.True(recovered.Success);
        Assert.Equal("{\"seq\":1}", recovered.Payload);
    }

    [Fact]
    public async Task NonExecutableWorkerPath_ProducesActionableWin32Message()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"tia-fake-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(bogus, "not an executable");
        try
        {
            using var client = CreateClient(workerPath: bogus);
            var result = await client.GetProjectStatusAsync("ok");

            Assert.False(result.Success);
            Assert.Contains(".NET Framework 4.8", result.Error);
            Assert.Contains("openness-worker", result.Error);
        }
        finally
        {
            File.Delete(bogus);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify the new ones fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~OpennessWorkerClientIntegrationTests" -v q`
Expected: FAIL — compile error (`OpennessWorkerClient` has no `requestTimeout` parameter and is not `IDisposable`).

- [ ] **Step 3: Create the transport**

Create `TiaMcpServer/Worker/PersistentWorkerTransport.cs`:

```csharp
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.Worker;

/// <summary>
/// Owns ONE long-lived worker process and the stdin/stdout line protocol against it.
/// Requests are serialized behind an instance gate (Siemens Openness is not safe for
/// concurrent access to one TIA Portal). A crash, timeout, or protocol desync kills the
/// process; the next request transparently starts a fresh one. Stderr is pumped in the
/// background for logging and crash diagnostics — per-request warnings arrive structurally
/// on <see cref="WorkerResponse.Warnings"/> instead.
/// </summary>
public sealed class PersistentWorkerTransport : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private const int RecentStderrCapacity = 30;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _workerExecutablePath;
    private readonly TimeSpan _requestTimeout;
    private readonly ILogger? _logger;
    private readonly ConcurrentQueue<string> _recentStderr = new();

    private Process? _process;
    private Task? _stderrPump;
    private bool _disposed;

    public PersistentWorkerTransport(string workerExecutablePath, TimeSpan requestTimeout, ILogger? logger = null)
    {
        _workerExecutablePath = workerExecutablePath;
        _requestTimeout = requestTimeout;
        _logger = logger;
    }

    public async Task<WorkerResponse> SendAsync(WorkerRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnsureProcessStarted();
            var process = _process!;

            try
            {
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions))
                    .ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                // Broken pipe: the worker died since the last request. Fail this request;
                // the next one restarts the process.
                KillProcess();
                throw;
            }

            var responseLineTask = process.StandardOutput.ReadLineAsync();
            using var timeout = new CancellationTokenSource(_requestTimeout);
            var completed = await Task.WhenAny(responseLineTask, Task.Delay(Timeout.InfiniteTimeSpan, timeout.Token))
                .ConfigureAwait(false);

            if (completed != responseLineTask)
            {
                KillProcess();
                throw new TimeoutException(
                    $"TIA Openness worker did not respond within {_requestTimeout.TotalSeconds:N0} seconds. "
                    + "The worker process was terminated and will restart on the next request; retry it.");
            }

            var responseLine = await responseLineTask.ConfigureAwait(false);
            if (responseLine is null)
            {
                var detail = await CaptureCrashDetailAsync().ConfigureAwait(false);
                throw new InvalidOperationException($"TIA Openness worker exited without a response. {detail}");
            }

            WorkerResponse? response;
            try
            {
                response = JsonSerializer.Deserialize<WorkerResponse>(responseLine, JsonOptions);
            }
            catch (JsonException)
            {
                // Protocol desync: any leftover bytes would corrupt the next request too.
                KillProcess();
                throw;
            }

            return response ?? throw new InvalidOperationException("TIA Openness worker returned an empty response.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureProcessStarted()
    {
        if (_process is { HasExited: false })
        {
            return;
        }

        KillProcess();
        while (_recentStderr.TryDequeue(out _))
        {
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = _workerExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(_workerExecutablePath) ?? AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Failed to start the TIA Openness worker process.");
        _process = process;
        _stderrPump = Task.Run(() => PumpStderrAsync(process));
        _logger?.LogInformation("Started TIA Openness worker process (pid {Pid}).", process.Id);
    }

    private async Task PumpStderrAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardError.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                _logger?.LogWarning("TIA Openness worker stderr: {Line}", trimmed);
                _recentStderr.Enqueue(trimmed);
                while (_recentStderr.Count > RecentStderrCapacity)
                {
                    _recentStderr.TryDequeue(out _);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Stream teardown while the process is being killed/disposed — expected noise.
        }
    }

    private async Task<string> CaptureCrashDetailAsync()
    {
        var pump = _stderrPump;
        KillProcess();
        if (pump is not null)
        {
            // Give the pump a moment to drain the dying process's final stderr lines.
            await Task.WhenAny(pump, Task.Delay(TimeSpan.FromSeconds(2))).ConfigureAwait(false);
        }

        var lines = _recentStderr.ToArray();
        return lines.Length == 0 ? "No response was written." : string.Join(" | ", lines);
    }

    private void KillProcess()
    {
        var process = _process;
        _process = null;
        _stderrPump = null;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Already exited or already gone — nothing to clean up.
        }

        process.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        var process = _process;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    // Closing stdin lets the worker's request loop end and TIA detach cleanly.
                    process.StandardInput.Close();
                    if (!process.WaitForExit(2000))
                    {
                        process.Kill();
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or Win32Exception)
            {
                // Best-effort shutdown; the process is exiting anyway.
            }

            process.Dispose();
            _process = null;
        }

        _gate.Dispose();
    }
}
```

Link it in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` — add next to the existing `OpennessWorkerClient.cs` link:

```xml
    <Compile Include="..\TiaMcpServer\Worker\PersistentWorkerTransport.cs" Link="Host\PersistentWorkerTransport.cs" />
```

- [ ] **Step 4: Flip the client onto the transport**

In `TiaMcpServer/Worker/OpennessWorkerClient.cs`:

**4a.** Replace the class declaration, fields, and constructor (anchor: `public class OpennessWorkerClient` down to the closing brace of the constructor):

```csharp
public class OpennessWorkerClient : IDisposable
{
    private static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromMinutes(5);

    private readonly ProjectSessionBinding _projectSessionBinding;
    private readonly ILogger<OpennessWorkerClient>? _logger;
    private readonly string? _workerExecutablePathOverride;
    private readonly TimeSpan _requestTimeout;
    private readonly object _transportLock = new();
    private PersistentWorkerTransport? _transport;

    public OpennessWorkerClient(
        ProjectSessionBinding projectSessionBinding,
        ILogger<OpennessWorkerClient>? logger = null,
        string? workerExecutablePath = null,
        TimeSpan? requestTimeout = null)
    {
        _projectSessionBinding = projectSessionBinding;
        _logger = logger;
        _workerExecutablePathOverride = workerExecutablePath;
        _requestTimeout = requestTimeout ?? DefaultRequestTimeout;
    }
```

**4b.** Replace `InvokeWorkerAsync` (anchor: `private async Task<WorkerCallResult> InvokeWorkerAsync(WorkerRequest request)`):

```csharp
    private async Task<WorkerCallResult> InvokeWorkerAsync(WorkerRequest request)
    {
        try
        {
            var response = await GetOrCreateTransport().SendAsync(request).ConfigureAwait(false);
            var warnings = CapWarnings(response.Warnings);
            foreach (var warning in warnings)
            {
                _logger?.LogWarning("TIA Openness worker warning: {Line}", warning);
            }

            return response.Success
                ? WorkerCallResult.Ok(response.Payload ?? string.Empty, warnings)
                : WorkerCallResult.Fail(
                    response.Error ?? "The TIA Openness worker failed without an error message.",
                    warnings);
        }
        catch (Win32Exception ex)
        {
            return WorkerCallResult.Fail(
                $"Failed to launch the TIA Openness worker process ({ex.Message}). "
                + "Verify that .NET Framework 4.8 is installed and that the 'openness-worker' folder "
                + "beside the MCP server executable is complete; rebuild or reinstall if files are missing.");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException or JsonException)
        {
            return WorkerCallResult.Fail(ex.Message);
        }
    }

    private PersistentWorkerTransport GetOrCreateTransport()
    {
        lock (_transportLock)
        {
            _transport ??= new PersistentWorkerTransport(
                _workerExecutablePathOverride ?? LocateWorkerExecutable(),
                _requestTimeout,
                _logger);
            return _transport;
        }
    }

    public void Dispose()
    {
        lock (_transportLock)
        {
            _transport?.Dispose();
            _transport = null;
        }
    }
```

**4c.** The old `JsonOptions` and `WorkerTimeout` fields disappear with the 4a replacement (only `TryReadProjectPath` still parses JSON, via `JsonDocument.Parse` — no options needed; keep the `using System.Text.Json;` directive). Additionally delete these members lower in the file (they moved into the transport or are obsolete):
- the `WorkerGate` field and the "Siemens Openness is not safe for concurrent…" comment above it,
- `private async Task<(WorkerResponse Response, IReadOnlyList<string> StderrLines)> SendAsync(WorkerRequest request)`,
- `private async Task<(WorkerResponse Response, IReadOnlyList<string> StderrLines)> SendUnguardedAsync(WorkerRequest request)`,
- `private const int MaxStderrWarningLines = 20;` and `private static IReadOnlyList<string> SplitStderrLines(string stderr)` (replaced by `CapWarnings` in 4d),
- `private static void TryKill(Process process)`.

**4d.** Add the warnings cap (replaces `SplitStderrLines`), next to `LocateWorkerExecutable` (which stays unchanged):

```csharp
    // A degraded read of a large project can emit hundreds of "Skipping X" lines; cap what
    // reaches the agent so warnings cannot flood a small model's context.
    private const int MaxWarningLines = 20;

    private static IReadOnlyList<string> CapWarnings(IReadOnlyList<string>? warnings)
    {
        if (warnings is null || warnings.Count == 0)
        {
            return Array.Empty<string>();
        }

        var lines = warnings
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

        if (lines.Count > MaxWarningLines)
        {
            var dropped = lines.Count - MaxWarningLines;
            lines = lines.Take(MaxWarningLines).ToList();
            lines.Add($"(+{dropped} more worker warnings truncated)");
        }

        return lines;
    }
```

**4e.** Check the `using` directives: `System.Diagnostics` and `System.Text` are no longer used by this file — remove them; keep `System.ComponentModel` (Win32Exception), `System.Text.Json`, `Microsoft.Extensions.Logging`, `TiaMcpServer.Contracts`.

Note on DI: `Program.cs` needs no change — the singleton is created via a factory lambda, so the container owns it and calls `Dispose` on host shutdown, which now closes the worker's stdin for a clean TIA detach.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (187 tests — the rewritten integration class has 10 tests, 3 more than before). The timeout test adds ~2s wall clock; that is expected.

- [ ] **Step 6: Commit**

```powershell
git add TiaMcpServer/Worker/PersistentWorkerTransport.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs
git commit -m "feat: keep one persistent TIA Openness worker process across requests"
```

---

### Task 6: Managed project open/close in the worker session (item 2.1, worker half)

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs`

**Interfaces:**
- Consumes: `Project.Path` (`FileInfo`), `Project.Close()`, `Projects.Open(FileInfo)` from Siemens Openness.
- Produces: `TiaPortalSession.OpenProject(string)` becomes idempotent for the already-open project; `internal void MarkProjectClosed()` replaces raw `session.Project = null` assignments in lifecycle code.

Why this matters now: in the process-per-request world every request got a fresh session, so the unconditional `Projects.Open` was only reached once per process. With a persistent worker, the second request for the same project would call `Projects.Open` on an already-open project (an Openness error), and a rebind to another project would leak the old handle. No unit test is possible (net48 + Siemens SDK); verification is build + code review, and behavior is exercised on the TIA machine per the "Deferred" note in IMPROVEMENT_PLAN.md.

- [ ] **Step 1: Rework `TiaPortalSession.OpenProject`**

In `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs`, add a tracking field next to `private bool _disposed;`:

```csharp
    private bool _projectOpenedByWorker;
```

Replace the `OpenProject` method (anchor: `public void OpenProject(string projectPath)`):

```csharp
    public void OpenProject(string projectPath)
    {
        ThrowIfDisposed();

        if (!IsConnected)
        {
            Connect();
        }

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("TIA Portal project file was not found.", projectPath);
        }

        var requestedPath = Path.GetFullPath(projectPath);
        var currentPath = TryReadCurrentProjectPath();
        if (currentPath is not null &&
            string.Equals(currentPath, requestedPath, StringComparison.OrdinalIgnoreCase))
        {
            // Persistent session: the requested project is already open — reuse it.
            return;
        }

        if (Project is not null)
        {
            if (_projectOpenedByWorker)
            {
                Console.Error.WriteLine($"Closing project '{currentPath ?? "(unknown)"}' before opening '{requestedPath}'.");
                try
                {
                    Project.Close();
                }
                catch (EngineeringException ex)
                {
                    Console.Error.WriteLine($"Could not close the previous project: {ex.Message}");
                }
            }
            else
            {
                // The user opened this project in the TIA Portal UI; it is not ours to close.
                Console.Error.WriteLine($"Leaving user-opened project '{currentPath ?? "(unknown)"}' open; opening '{requestedPath}' alongside it.");
            }

            Project = null;
            _projectOpenedByWorker = false;
        }

        Project = _tiaPortal!.Projects.Open(new FileInfo(requestedPath));
        _projectOpenedByWorker = true;
    }

    private string? TryReadCurrentProjectPath()
    {
        if (Project is null)
        {
            return null;
        }

        try
        {
            return Project.Path?.FullName;
        }
        catch (EngineeringException)
        {
            // Stale handle: the project was closed in the TIA Portal UI since we opened it.
            Project = null;
            _projectOpenedByWorker = false;
            return null;
        }
    }

    internal void MarkProjectClosed()
    {
        Project = null;
        _projectOpenedByWorker = false;
    }
```

- [ ] **Step 2: Route lifecycle closes through `MarkProjectClosed`**

In `TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs` there are exactly two `session.Project = null;` assignments; replace both.

In `SaveProjectAs` (anchor: the `if (rebind)` block):

```csharp
            project.Close();
            session.MarkProjectClosed();
            session.OpenProject(copiedProjectPath!);
```

In `CloseProject` (anchor: `project.Close();` followed by `session.Project = null;`):

```csharp
        project.Close();
        session.MarkProjectClosed();
```

- [ ] **Step 3: Build and run the suite**

Run: `dotnet build TiaMcpServer.sln`
Expected: Build succeeded.
Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (187 tests, unchanged — worker-only edit).

- [ ] **Step 4: Commit**

```powershell
git add TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs TiaMcpServer.OpennessWorker/Openness/ProjectLifecycleService.cs
git commit -m "feat: reuse the open project and close only worker-opened projects in the persistent session"
```

---

### Task 7: `ProjectTreeFilter` — pure startPath/depth filtering (item 2.3a)

**Files:**
- Create: `TiaMcpServer.Contracts/ProjectTreeFilter.cs`
- Test: `TiaMcpServer.Tests/ProjectTreeFilterTests.cs` (new)

**Interfaces:**
- Consumes: `ProjectTreeNode` (`Name`, `NodeType`, `Details` — `Dictionary<string,string>?`, `Children` — `List<ProjectTreeNode>?`).
- Produces: `public static List<ProjectTreeNode> ProjectTreeFilter.Apply(List<ProjectTreeNode> roots, string? startPath, int? depth)`. Never mutates the input tree; pruned nodes are fresh copies. `startPath` matches a node's `Details["Path"]` (OrdinalIgnoreCase); no match throws `InvalidOperationException` with a recovery hint. `depth` counts returned levels: `depth: 1` returns the selected roots with `Children` emptied and `Details["ChildrenOmitted"]` set to the omitted count. Lives in Contracts so the net48 worker uses it and net8 tests cover it (Contracts is a ProjectReference — no csproj link needed).

- [ ] **Step 1: Write the failing tests**

Create `TiaMcpServer.Tests/ProjectTreeFilterTests.cs`:

```csharp
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests;

public class ProjectTreeFilterTests
{
    private static ProjectTreeNode Node(string name, string path, params ProjectTreeNode[] children)
        => new()
        {
            Name = name,
            NodeType = "Folder",
            Details = new Dictionary<string, string> { ["Path"] = path },
            Children = children.Length == 0 ? new List<ProjectTreeNode>() : new List<ProjectTreeNode>(children)
        };

    private static List<ProjectTreeNode> SampleTree()
        => new()
        {
            Node("PLC_1", "PLC_1",
                Node("Blocks", "PLC_1/Blocks",
                    Node("Main", "PLC_1/Blocks/Main"),
                    Node("Motors", "PLC_1/Blocks/Motors",
                        Node("Motor_1", "PLC_1/Blocks/Motors/Motor_1"))),
                Node("TagTables", "PLC_1/TagTables",
                    Node("Default", "PLC_1/TagTables/Default")))
        };

    [Fact]
    public void NoFilters_ReturnsTreeUnchanged()
    {
        var tree = SampleTree();

        var result = ProjectTreeFilter.Apply(tree, startPath: null, depth: null);

        Assert.Same(tree, result);
    }

    [Fact]
    public void StartPath_SelectsTheMatchingSubtree()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_1/Blocks", depth: null);

        var root = Assert.Single(result);
        Assert.Equal("Blocks", root.Name);
        Assert.Equal(2, root.Children!.Count);
    }

    [Fact]
    public void StartPath_IsCaseInsensitive()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "plc_1/blocks/motors", depth: null);

        Assert.Equal("Motors", Assert.Single(result).Name);
    }

    [Fact]
    public void UnknownStartPath_ThrowsWithRecoveryHint()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_9/Nope", depth: null));

        Assert.Contains("PLC_9/Nope", ex.Message);
        Assert.Contains("browse_project_tree", ex.Message);
    }

    [Fact]
    public void Depth1_ReturnsRootsWithChildrenOmittedMarker()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: null, depth: 1);

        var root = Assert.Single(result);
        Assert.Empty(root.Children!);
        Assert.Equal("2", root.Details!["ChildrenOmitted"]);
    }

    [Fact]
    public void Depth2_KeepsOneLevelOfChildren()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: null, depth: 2);

        var root = Assert.Single(result);
        Assert.Equal(2, root.Children!.Count);
        var blocks = root.Children![0];
        Assert.Empty(blocks.Children!);
        Assert.Equal("2", blocks.Details!["ChildrenOmitted"]);
    }

    [Fact]
    public void Depth_DoesNotMutateTheInputTree()
    {
        var tree = SampleTree();

        ProjectTreeFilter.Apply(tree, startPath: null, depth: 1);

        Assert.Equal(2, tree[0].Children!.Count);
        Assert.False(tree[0].Details!.ContainsKey("ChildrenOmitted"));
    }

    [Fact]
    public void StartPathAndDepth_Compose()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_1/Blocks", depth: 1);

        var root = Assert.Single(result);
        Assert.Equal("Blocks", root.Name);
        Assert.Empty(root.Children!);
        Assert.Equal("2", root.Details!["ChildrenOmitted"]);
    }

    [Fact]
    public void LeafNodes_GetNoOmittedMarker()
    {
        var result = ProjectTreeFilter.Apply(SampleTree(), startPath: "PLC_1/Blocks/Main", depth: 1);

        var leaf = Assert.Single(result);
        Assert.False(leaf.Details!.ContainsKey("ChildrenOmitted"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~ProjectTreeFilterTests" -v q`
Expected: FAIL — `ProjectTreeFilter` does not exist.

- [ ] **Step 3: Implement the filter**

Create `TiaMcpServer.Contracts/ProjectTreeFilter.cs` (netstandard2.0 — no target-typed `new`, no index/range operators):

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Pure post-processing for browse_project_tree: subtree selection via startPath and
/// depth limiting. Lives in Contracts so the net48 worker applies it after walking the
/// full Openness tree while the net8 test suite covers the logic without Siemens DLLs.
/// Never mutates the input tree.
/// </summary>
public static class ProjectTreeFilter
{
    public static List<ProjectTreeNode> Apply(List<ProjectTreeNode> roots, string? startPath, int? depth)
    {
        // netstandard2.0 BCL has no NotNullWhen annotation on IsNullOrWhiteSpace — the ! is required.
        var selected = string.IsNullOrWhiteSpace(startPath)
            ? roots
            : new List<ProjectTreeNode> { FindByPath(roots, startPath!.Trim()) };

        if (depth is null)
        {
            return selected;
        }

        if (depth.Value < 1)
        {
            throw new InvalidOperationException("depth must be 1 or greater; 1 returns only the selected root nodes.");
        }

        return selected.Select(node => Prune(node, depth.Value)).ToList();
    }

    private static ProjectTreeNode FindByPath(List<ProjectTreeNode> roots, string startPath)
    {
        var stack = new Stack<ProjectTreeNode>(roots);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Details != null &&
                node.Details.TryGetValue("Path", out var path) &&
                string.Equals(path, startPath, StringComparison.OrdinalIgnoreCase))
            {
                return node;
            }

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    stack.Push(child);
                }
            }
        }

        throw new InvalidOperationException(
            $"startPath '{startPath}' does not match any node's Path in the project tree. "
            + "Call browse_project_tree without startPath (optionally with a small depth) to discover valid paths.");
    }

    private static ProjectTreeNode Prune(ProjectTreeNode node, int remainingDepth)
    {
        var children = node.Children;
        if (children == null || children.Count == 0)
        {
            return node;
        }

        if (remainingDepth <= 1)
        {
            var details = node.Details == null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string>(node.Details);
            details["ChildrenOmitted"] = children.Count.ToString();

            return new ProjectTreeNode
            {
                Name = node.Name,
                NodeType = node.NodeType,
                Details = details,
                Children = new List<ProjectTreeNode>()
            };
        }

        return new ProjectTreeNode
        {
            Name = node.Name,
            NodeType = node.NodeType,
            Details = node.Details,
            Children = children.Select(child => Prune(child, remainingDepth - 1)).ToList()
        };
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (196 tests — this task adds 9).

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer.Contracts/ProjectTreeFilter.cs TiaMcpServer.Tests/ProjectTreeFilterTests.cs
git commit -m "feat: add pure startPath/depth filtering for project trees"
```

---

### Task 8: Plumb `depth`/`startPath`/`maxResults` host → worker, with scope validation (item 2.3b)

**Files:**
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs` (3 method signatures)
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs`, `BatchOperationCatalog.cs`, `BatchWorkerInvoker.cs`, `BatchTools.cs` (descriptions)
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`, `Openness/EquipmentCatalogSearcher.cs`, `Openness/CrossReferenceReader.cs`
- Test: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`, `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs` (append)

**Interfaces:**
- Consumes: `ProjectTreeFilter.Apply` (Task 7), `WorkerResponse.Warnings` (Task 3 — truncation notices ride it).
- Produces:
  - `WorkerRequest`: `public int? Depth { get; set; }`, `public string? StartPath { get; set; }`, `public int? MaxResults { get; set; }`.
  - `OpennessWorkerClient.BrowseProjectTreeAsync(string? projectPath, int? depth = null, string? startPath = null)`, `SearchEquipmentCatalogAsync(string query, string? projectPath, int? maxResults = null)`, `ReadCrossReferencesAsync(string? projectPath, string? plcName, string? filter, int? maxResults = null)`.
  - `BatchOperationRequest`: `Depth`, `StartPath`, `MaxResults` properties.
  - `EquipmentCatalogSearcher.Search(TiaPortal tiaPortal, string query, int? maxResults = null)` with `public const int DefaultMaxResults = 50`.
  - `CrossReferenceReader.Read(Project project, string? plcName, string filterName, int? maxResults = null)`.

- [ ] **Step 1: Write the failing validation tests**

Append to `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`:

```csharp
    [Fact]
    public void Validate_RejectsDepthAndStartPathOnNonTreeOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "list_tag_tables", Depth = 2 },
            new BatchOperationRequest { OperationId = "b", Operation = "get_project_status", StartPath = "PLC_1" }
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'depth' is only valid for browse_project_tree", result.Error);
        Assert.Contains("'startPath' is only valid for browse_project_tree", result.Error);
        Assert.Contains("operationId 'a'", result.Error);
        Assert.Contains("operationId 'b'", result.Error);
    }

    [Fact]
    public void Validate_RejectsMaxResultsOnUnsupportedOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", MaxResults = 10 }
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'maxResults' is only valid for search_equipment_catalog and read_cross_references", result.Error);
    }

    [Fact]
    public void Validate_RejectsOutOfRangeBounds()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", Depth = 0 },
            new BatchOperationRequest { OperationId = "b", Operation = "search_equipment_catalog", Query = "cpu", MaxResults = 0 }
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.False(result.IsValid);
        Assert.Contains("'depth' must be 1 or greater", result.Error);
        Assert.Contains("'maxResults' must be 1 or greater", result.Error);
    }

    [Fact]
    public void Validate_AcceptsBoundsOnTheirOperations()
    {
        var operations = new[]
        {
            new BatchOperationRequest { OperationId = "a", Operation = "browse_project_tree", Depth = 2, StartPath = "PLC_1/Blocks" },
            new BatchOperationRequest { OperationId = "b", Operation = "search_equipment_catalog", Query = "cpu", MaxResults = 10 },
            new BatchOperationRequest { OperationId = "c", Operation = "read_cross_references", MaxResults = 100 }
        };

        var result = BatchOperationCatalog.ValidateReadBatch(operations);

        Assert.True(result.IsValid);
    }
```

Append to `TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs` (match the file's existing deserialize helper/options usage — it already round-trips camelCase JSON):

```csharp
    [Fact]
    public void Deserializes_BoundingFields()
    {
        var json = """{"operationId":"a","operation":"browse_project_tree","depth":3,"startPath":"PLC_1/Blocks","maxResults":25}""";

        var request = System.Text.Json.JsonSerializer.Deserialize<BatchOperationRequest>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal(3, request.Depth);
        Assert.Equal("PLC_1/Blocks", request.StartPath);
        Assert.Equal(25, request.MaxResults);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BatchOperationCatalogTests|FullyQualifiedName~BatchOperationRequestJsonTests" -v q`
Expected: FAIL — `BatchOperationRequest` has no `Depth`/`StartPath`/`MaxResults`.

- [ ] **Step 3: Add the fields to both request DTOs**

`TiaMcpServer.Contracts/WorkerRequest.cs` — append inside the class:

```csharp
    public int? Depth { get; set; }

    public string? StartPath { get; set; }

    public int? MaxResults { get; set; }
```

`TiaMcpServer/Batch/BatchOperationRequest.cs` — append inside the class (descriptions are the load-bearing contract):

```csharp
    [Description("Optional maximum tree depth for browse_project_tree; 1 returns only top-level nodes and marks pruned nodes with a ChildrenOmitted count. Combine with startPath to narrow large projects.")]
    public int? Depth { get; set; }

    [Description("Optional subtree root for browse_project_tree, matching a node's Path detail exactly (case-insensitive), e.g. PLC_1/Blocks. Errors if no node matches.")]
    public string? StartPath { get; set; }

    [Description("Optional result cap. Valid for search_equipment_catalog (default 50 when omitted) and read_cross_references (unlimited when omitted; a truncation message is added when the cap is hit).")]
    public int? MaxResults { get; set; }
```

- [ ] **Step 4: Validate scope and range in the catalog**

In `TiaMcpServer/Batch/BatchOperationCatalog.cs`, inside `Validate`, after the `missing` required-fields check (anchor: the closing brace of `if (missing.Length > 0) { ... }` inside the `foreach`), add:

```csharp
            foreach (var boundsError in ValidateBounds(op))
            {
                errors.Add($"Operation '{op.Operation}' (operationId '{op.OperationId}'): {boundsError}");
            }
```

Then add the helper after `IsFieldPresent`:

```csharp
    private static IEnumerable<string> ValidateBounds(BatchOperationRequest op)
    {
        var isTree = string.Equals(op.Operation, "browse_project_tree", StringComparison.Ordinal);
        var takesMaxResults =
            string.Equals(op.Operation, "search_equipment_catalog", StringComparison.Ordinal) ||
            string.Equals(op.Operation, "read_cross_references", StringComparison.Ordinal);

        if (op.Depth is not null && !isTree)
        {
            yield return "'depth' is only valid for browse_project_tree.";
        }

        if (op.StartPath is not null && !isTree)
        {
            yield return "'startPath' is only valid for browse_project_tree.";
        }

        if (op.MaxResults is not null && !takesMaxResults)
        {
            yield return "'maxResults' is only valid for search_equipment_catalog and read_cross_references.";
        }

        if (op.Depth is < 1)
        {
            yield return "'depth' must be 1 or greater.";
        }

        if (op.MaxResults is < 1)
        {
            yield return "'maxResults' must be 1 or greater.";
        }
    }
```

- [ ] **Step 5: Thread the fields through client and invoker**

`TiaMcpServer/Worker/OpennessWorkerClient.cs` — replace the three read wrappers:

```csharp
    public Task<WorkerCallResult> BrowseProjectTreeAsync(string? projectPath, int? depth = null, string? startPath = null)
    {
        return SendBoundProjectRequestAsync(
            "browse_project_tree",
            projectPath,
            request =>
            {
                request.Depth = depth;
                request.StartPath = startPath;
            },
            "[]");
    }
```

```csharp
    public Task<WorkerCallResult> SearchEquipmentCatalogAsync(string query, string? projectPath, int? maxResults = null)
    {
        return SendBoundProjectRequestAsync(
            "search_equipment_catalog",
            projectPath,
            request =>
            {
                request.Query = query;
                request.MaxResults = maxResults;
            },
            "[]");
    }
```

```csharp
    public Task<WorkerCallResult> ReadCrossReferencesAsync(string? projectPath, string? plcName, string? filter, int? maxResults = null)
    {
        // Validate the filter before TryResolve so an invalid filter does not bind the session.
        if (!CrossReferenceFilterNames.TryNormalize(filter, out var normalizedFilter, out var filterError))
        {
            return Task.FromResult(WorkerCallResult.Fail(filterError!));
        }

        return SendBoundProjectRequestAsync(
            "read_cross_references",
            projectPath,
            request =>
            {
                request.PlcName = plcName;
                request.CrossReferenceFilter = normalizedFilter;
                request.MaxResults = maxResults;
            },
            "{}");
    }
```

`TiaMcpServer/Batch/BatchWorkerInvoker.cs` — update the three read arms of `InvokeAsync`:

```csharp
        "browse_project_tree" => client.BrowseProjectTreeAsync(op.ProjectPath, op.Depth, op.StartPath),
        "read_hardware_config" => client.ReadHardwareConfigAsync(op.ProjectPath),
        "search_equipment_catalog" => client.SearchEquipmentCatalogAsync(op.Query!, op.ProjectPath, op.MaxResults),
        "read_cross_references" => client.ReadCrossReferencesAsync(op.ProjectPath, op.PlcName, op.Filter, op.MaxResults),
```

(`ReadCurrentStateAsync`'s `BrowseProjectTreeAsync(op.ProjectPath)` call keeps defaults — safety snapshots must see the full tree.)

- [ ] **Step 6: Apply the bounds in the worker**

`TiaMcpServer.OpennessWorker/Program.cs` — replace the three handlers:

```csharp
    private static WorkerResponse BrowseProjectTree(WorkerRequest request)
    {
        return WithProject(request, project =>
        {
            var tree = new ProjectTreeWalker().Walk(project);
            return Success(ProjectTreeFilter.Apply(tree, request.StartPath, request.Depth));
        });
    }
```

```csharp
    private static WorkerResponse SearchEquipmentCatalog(WorkerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Failure("Query is required.");
        }

        return WithSession(request, session =>
        {
            session.EnsureConnected();

            if (!string.IsNullOrEmpty(request.ProjectPath))
            {
                session.OpenProject(request.ProjectPath!);
            }

            if (session.TiaPortal is null)
            {
                return Failure("No TIA Portal session is connected. Please start TIA Portal and try again.");
            }

            return Success(EquipmentCatalogSearcher.Search(session.TiaPortal, request.Query!, request.MaxResults));
        });
    }
```

```csharp
    private static WorkerResponse ReadCrossReferences(WorkerRequest request)
    {
        if (!CrossReferenceFilterNames.TryNormalize(
                request.CrossReferenceFilter,
                out var filter,
                out var filterError))
        {
            return Failure(filterError ?? "Invalid cross-reference filter.");
        }

        return WithProject(request, project => Success(
            CrossReferenceReader.Read(project, request.PlcName, filter, request.MaxResults)));
    }
```

`TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs` — change the entry point (anchor: `public static List<CatalogEntryInfo> Search(TiaPortal tiaPortal, string query)`):

```csharp
    /// <summary>Hard default so an unbounded catalog search can never flood the response.</summary>
    public const int DefaultMaxResults = 50;

    public static List<CatalogEntryInfo> Search(TiaPortal tiaPortal, string query, int? maxResults = null)
    {
        var limit = maxResults ?? DefaultMaxResults;
        var results = new List<CatalogEntryInfo>();
        if (string.IsNullOrWhiteSpace(query))
        {
            return results;
        }

        query = query.Trim();
        var seenEntries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddMatchesFromHardwareCatalogFind(tiaPortal, query, results, seenEntries, limit);

        var visited = new HashSet<int>();
        foreach (var catalog in FindCatalogRoots(tiaPortal))
        {
            if (results.Count >= limit)
            {
                break;
            }

            try
            {
                Traverse(catalog, string.Empty, query, results, seenEntries, visited, limit);
            }
            catch (Exception ex) when (ex is EngineeringException or TargetInvocationException)
            {
                Console.Error.WriteLine($"Skipping hardware catalog root while searching equipment catalog: {ex.Message}");
            }
        }

        if (results.Count >= limit)
        {
            // Rides back as a response warning via the per-request stderr capture.
            Console.Error.WriteLine(
                $"search_equipment_catalog: returned the first {limit} matches; more may exist. "
                + "Refine the query or raise maxResults.");
        }

        return results;
    }
```

Thread `limit` through the two private walkers: `AddMatchesFromHardwareCatalogFind(..., int limit)` gets `if (results.Count >= limit) { return; }` as the first statement of its `foreach` body; `Traverse(..., int limit)` gets `if (results.Count >= limit) { return; }` as its first statement and passes `limit` on its recursive call. Their call sites are only the ones shown above plus the recursion.

`TiaMcpServer.OpennessWorker/Openness/CrossReferenceReader.cs` — change the entry point and cap sources across PLCs (anchor: `public static CrossReferenceReport Read(Project project, string? plcName, string filterName)`):

```csharp
    public static CrossReferenceReport Read(Project project, string? plcName, string filterName, int? maxResults = null)
    {
        var filter = ToOpennessFilter(filterName);
        var report = new CrossReferenceReport
        {
            Filter = filterName
        };

        var remaining = maxResults;
        foreach (var plc in PlcSoftwareLocator.FindAll(project, plcName))
        {
            var plcInfo = ReadPlc(plc.DeviceName, plc.Software, filter, remaining);
            report.Plcs.Add(plcInfo);

            if (remaining is not null)
            {
                remaining = Math.Max(0, remaining.Value - plcInfo.Sources.Count);
            }
        }
```

(the rest of `Read` — the empty-PLC guard and the three total sums — stays unchanged.)

In `ReadPlc`, change the signature to `ReadPlc(string deviceName, PlcSoftware plcSoftware, CrossReferenceFilter filter, int? maxSources)` and guard the source loop (anchor: `foreach (SourceObject source in crossReferenceResult.Sources)`):

```csharp
        foreach (SourceObject source in crossReferenceResult.Sources)
        {
            if (maxSources is not null && result.Sources.Count >= maxSources.Value)
            {
                result.Messages.Add(
                    $"Truncated: maxResults limit reached while reading sources for PLC '{deviceName}'. "
                    + "Narrow with plcName or filter, or raise maxResults.");
                break;
            }
```

(the existing `try { result.Sources.Add(ReadSource(source, result.Messages)); }` body under the loop is unchanged.)

- [ ] **Step 7: Update the load-bearing descriptions**

In `TiaMcpServer/Batch/BatchTools.cs`, `execute_read_batch`'s `[Description]` — replace the sentence listing valid operations so it ends with the bounding hint:

```csharp
    [Description("Run up to 50 read operations in one call. Each item is { operationId (unique), operation, ...that operation's parameters }; projectPath is optional on every item. Reads run independently, so a failing item does not stop the others. "
        + "Valid operations (parentheses list required fields): browse_project_tree, read_hardware_config, read_cross_references, search_equipment_catalog (query), get_block_content (blockPath), list_tag_tables, compile_check, get_project_status. "
        + "Large projects: bound payloads with depth/startPath (browse_project_tree) and maxResults (search_equipment_catalog, read_cross_references); oversized responses are truncated server-side with an explicit marker.")]
```

- [ ] **Step 8: Run the suite and build the worker**

Run: `dotnet build TiaMcpServer.sln`
Expected: Build succeeded.
Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (201 tests — this task adds 5).

- [ ] **Step 9: Commit**

```powershell
git add TiaMcpServer.Contracts/WorkerRequest.cs TiaMcpServer/Worker/OpennessWorkerClient.cs TiaMcpServer/Batch/BatchOperationRequest.cs TiaMcpServer/Batch/BatchOperationCatalog.cs TiaMcpServer/Batch/BatchWorkerInvoker.cs TiaMcpServer/Batch/BatchTools.cs TiaMcpServer.OpennessWorker/Program.cs TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs TiaMcpServer.OpennessWorker/Openness/CrossReferenceReader.cs TiaMcpServer.Tests/BatchOperationCatalogTests.cs TiaMcpServer.Tests/BatchOperationRequestJsonTests.cs
git commit -m "feat: bound tree, catalog, and cross-reference reads with depth, startPath, and maxResults"
```

---

### Task 9: Server-side byte budget for read batches (item 2.3c)

**Files:**
- Create: `TiaMcpServer/Batch/BatchPayloadBudget.cs`
- Modify: `TiaMcpServer/Batch/BatchOperationResult.cs` (new status), `TiaMcpServer/Batch/BatchResultFormatter.cs`, `TiaMcpServer/Batch/BatchTools.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (link new file)
- Test: `TiaMcpServer.Tests/BatchPayloadBudgetTests.cs` (new), `TiaMcpServer.Tests/BatchResultFormatterTests.cs` (append)

**Interfaces:**
- Consumes: `BatchOperationResult` record, `BatchOperationStatus` constants.
- Produces:
  - `BatchOperationStatus.Omitted = "omitted"`.
  - `public static class BatchPayloadBudget` with `public const int MaxItemChars = 60_000;`, `public const int MaxBatchChars = 180_000;`, `public static IReadOnlyList<BatchOperationResult> Apply(IReadOnlyList<BatchOperationResult> results)` and an overload `Apply(results, int maxItemChars, int maxBatchChars)` for tests. Pure — returns new records, never mutates.
  - `BatchResultFormatter.ReadBatch` envelope gains `omitted` count and `success` becomes `failed == 0 && omitted == 0` (an omission means the agent must re-run narrower — surfacing that as non-success is the point).

- [ ] **Step 1: Write the failing tests**

Create `TiaMcpServer.Tests/BatchPayloadBudgetTests.cs`:

```csharp
using TiaMcpServer.Batch;
using Xunit;

namespace TiaMcpServer.Tests;

public class BatchPayloadBudgetTests
{
    private static BatchOperationResult Ok(string id, string payload)
        => new(id, "browse_project_tree", BatchOperationStatus.Succeeded, payload);

    [Fact]
    public void SmallResults_PassThroughUnchanged()
    {
        var results = new[] { Ok("a", "short"), Ok("b", "also short") };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1000);

        Assert.Equal("short", budgeted[0].Result);
        Assert.Equal("also short", budgeted[1].Result);
        Assert.All(budgeted, r => Assert.Equal(BatchOperationStatus.Succeeded, r.Status));
    }

    [Fact]
    public void OversizedItem_IsTruncatedWithTrailer()
    {
        var results = new[] { Ok("a", new string('x', 150)) };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 10_000);

        Assert.StartsWith(new string('x', 100), budgeted[0].Result);
        Assert.Contains("TRUNCATED", budgeted[0].Result);
        Assert.Contains("startPath", budgeted[0].Result);
        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[0].Status);
    }

    [Fact]
    public void ItemsBeyondTheBatchBudget_AreOmitted()
    {
        var results = new[]
        {
            Ok("a", new string('x', 90)),
            Ok("b", new string('y', 90)),
            Ok("c", "tiny")
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 100);

        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[0].Status);
        Assert.Equal(BatchOperationStatus.Omitted, budgeted[1].Status);
        Assert.Contains("OMITTED", budgeted[1].Result);
        Assert.Contains("execute_read_batch", budgeted[1].Result);
        // "tiny" still fits the remaining budget — omission is per item, not a hard stop.
        Assert.Equal(BatchOperationStatus.Succeeded, budgeted[2].Status);
        Assert.Equal("tiny", budgeted[2].Result);
    }

    [Fact]
    public void FailedItems_KeepTheirErrorText()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "compile_check", BatchOperationStatus.Failed, "Error: boom")
        };

        var budgeted = BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1000);

        Assert.Equal("Error: boom", budgeted[0].Result);
        Assert.Equal(BatchOperationStatus.Failed, budgeted[0].Status);
    }

    [Fact]
    public void InputList_IsNotMutated()
    {
        var original = Ok("a", new string('x', 150));
        var results = new[] { original };

        BatchPayloadBudget.Apply(results, maxItemChars: 100, maxBatchChars: 1000);

        Assert.Equal(new string('x', 150), original.Result);
    }

    [Fact]
    public void DefaultLimits_AreGenerousButFinite()
    {
        Assert.Equal(60_000, BatchPayloadBudget.MaxItemChars);
        Assert.Equal(180_000, BatchPayloadBudget.MaxBatchChars);
    }
}
```

Append to `TiaMcpServer.Tests/BatchResultFormatterTests.cs`:

```csharp
    [Fact]
    public void ReadBatch_CountsOmittedItemsAndClearsSuccess()
    {
        var results = new[]
        {
            new BatchOperationResult("a", "browse_project_tree", BatchOperationStatus.Succeeded, "{}"),
            new BatchOperationResult("b", "read_hardware_config", BatchOperationStatus.Omitted, "[OMITTED]")
        };

        var json = BatchResultFormatter.ReadBatch(results);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, doc.RootElement.GetProperty("omitted").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("succeeded").GetInt32());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BatchPayloadBudget|ReadBatch_CountsOmitted" -v q`
Expected: FAIL — `BatchPayloadBudget` and `BatchOperationStatus.Omitted` do not exist.

- [ ] **Step 3: Implement status, budget, formatter, and wiring**

`TiaMcpServer/Batch/BatchOperationResult.cs` — add to `BatchOperationStatus`:

```csharp
    /// <summary>Read succeeded but its payload was dropped by the batch byte budget.</summary>
    public const string Omitted = "omitted";
```

Create `TiaMcpServer/Batch/BatchPayloadBudget.cs`:

```csharp
namespace TiaMcpServer.Batch;

/// <summary>
/// Host-side backstop that keeps execute_read_batch responses bounded no matter what the
/// caller asked for: each item's payload is capped, and once the whole batch exceeds its
/// budget, remaining oversized payloads are replaced with an explicit omission marker.
/// Pure and unit-testable; never mutates its input.
/// </summary>
public static class BatchPayloadBudget
{
    public const int MaxItemChars = 60_000;
    public const int MaxBatchChars = 180_000;

    public static IReadOnlyList<BatchOperationResult> Apply(IReadOnlyList<BatchOperationResult> results)
        => Apply(results, MaxItemChars, MaxBatchChars);

    public static IReadOnlyList<BatchOperationResult> Apply(
        IReadOnlyList<BatchOperationResult> results,
        int maxItemChars,
        int maxBatchChars)
    {
        var budgeted = new List<BatchOperationResult>(results.Count);
        var used = 0;

        foreach (var item in results)
        {
            var text = item.Result ?? string.Empty;
            var truncated = false;
            if (text.Length > maxItemChars)
            {
                text = text.Substring(0, maxItemChars) + TruncationTrailer(maxItemChars);
                truncated = true;
            }

            if (used + text.Length > maxBatchChars)
            {
                budgeted.Add(item with
                {
                    Status = BatchOperationStatus.Omitted,
                    Result = OmissionMarker(maxBatchChars)
                });
                continue;
            }

            budgeted.Add(truncated ? item with { Result = text } : item);
            used += text.Length;
        }

        return budgeted;
    }

    public static string TruncationTrailer(int maxItemChars)
        => $"\n[TRUNCATED — this item's payload exceeded {maxItemChars} characters. "
            + "Narrow the read (plcName, filter, startPath, depth, maxResults) or split the batch.]";

    public static string OmissionMarker(int maxBatchChars)
        => $"[OMITTED — the combined batch response exceeded {maxBatchChars} characters. "
            + "Re-run this operationId in its own execute_read_batch call, narrowed with "
            + "plcName/filter/startPath/depth/maxResults.]";
}
```

`TiaMcpServer/Batch/BatchResultFormatter.cs` — replace `ReadBatch`:

```csharp
    public static string ReadBatch(IReadOnlyList<BatchOperationResult> results)
    {
        var failed = Count(results, BatchOperationStatus.Failed);
        var omitted = Count(results, BatchOperationStatus.Omitted);
        return JsonSerializer.Serialize(
            new
            {
                tool = "execute_read_batch",
                success = failed == 0 && omitted == 0,
                operationCount = results.Count,
                succeeded = Count(results, BatchOperationStatus.Succeeded),
                failed,
                omitted,
                operations = Project(results)
            },
            JsonOptions);
    }
```

`TiaMcpServer/Batch/BatchTools.cs`, `ExecuteReadBatch` — replace the return (anchor: `return BatchResultFormatter.ReadBatch(results);`):

```csharp
        return BatchResultFormatter.ReadBatch(BatchPayloadBudget.Apply(results));
```

`TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` — add next to the other Batch links:

```xml
    <Compile Include="..\TiaMcpServer\Batch\BatchPayloadBudget.cs" Link="Host\Batch\BatchPayloadBudget.cs" />
```

- [ ] **Step 4: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (208 tests — this task adds 7).

- [ ] **Step 5: Commit**

```powershell
git add TiaMcpServer/Batch/BatchPayloadBudget.cs TiaMcpServer/Batch/BatchOperationResult.cs TiaMcpServer/Batch/BatchResultFormatter.cs TiaMcpServer/Batch/BatchTools.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj TiaMcpServer.Tests/BatchPayloadBudgetTests.cs TiaMcpServer.Tests/BatchResultFormatterTests.cs
git commit -m "feat: cap read-batch payloads with a server-side byte budget and explicit markers"
```

---

### Task 10: Collapse the lifecycle preview/apply pairs — 16 tools become 10 (item 2.4)

**Files:**
- Modify: `TiaMcpServer/Safety/WriteSafetyService.cs` (`instructions` in preview JSON)
- Modify: `TiaMcpServer/Safety/WriteSafetyTooling.cs` (pass-through)
- Modify: `TiaMcpServer/Batch/BatchTools.cs` (batch preview gets instructions too)
- Modify: `TiaMcpServer/Tools/ProjectLifecycleTools.cs` (full rewrite)
- Test: `TiaMcpServer.Tests/WriteToolSafetyTokenTests.cs` (rewrite), `TiaMcpServer.Tests/ProjectLifecycleToolTests.cs` (update), `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs` (append round-trip)

**Interfaces:**
- Consumes: `WriteSafetyService.ValidateAndConsume` (unchanged), `WriteSafetyTooling.ValidateForApplyAsync` (unchanged signature), `WorkerCallResult`, the fake worker (round-trip test).
- Produces:
  - `WriteSafetyService.CreatePreview(..., string? diff = null, string? instructions = null)` — `instructions` serialized into the preview JSON (like `diff`, null when absent).
  - `WriteSafetyTooling.CreatePreview(..., string? diff = null, string? instructions = null)` pass-through.
  - `ProjectLifecycleTools` exposes exactly 7 `[McpServerTool]` methods: `get_project_status`, `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`. The six `Preview*` methods are deleted. Write-tool behavior:
    1. **No `safetyToken`** (whatever `confirm` says) → return the preview JSON + fresh token + `instructions`.
    2. **`safetyToken` but `confirm=false`** → plain-text rejection telling the agent to set `confirm=true` or drop the token for a fresh preview.
    3. **`safetyToken` + `confirm=true`** → validate & apply (existing flow, byte-identical `target`/`requestedInput` — now built exactly once per method).
  - Token compatibility: previews always stored the APPLY tool's name (`"open_project"` etc.), so nothing changes in the token entries; only the recovery-hint text (`previewToolName`) becomes `"<tool> (without safetyToken)"`.

- [ ] **Step 1: Write the failing tests**

Replace the entire content of `TiaMcpServer.Tests/WriteToolSafetyTokenTests.cs` with:

```csharp
using System.Reflection;
using ModelContextProtocol.Server;
using TiaMcpServer.Tools;
using Xunit;

namespace TiaMcpServer.Tests;

public class WriteToolSafetyTokenTests
{
    [Theory]
    [InlineData("PreviewOpenProject")]
    [InlineData("PreviewCreateProject")]
    [InlineData("PreviewSaveProject")]
    [InlineData("PreviewSaveProjectAs")]
    [InlineData("PreviewArchiveProject")]
    [InlineData("PreviewCloseProject")]
    public void SeparatePreviewToolsAreGone(string methodName)
    {
        Assert.Null(typeof(ProjectLifecycleTools).GetMethod(methodName, BindingFlags.Public | BindingFlags.Static));
    }

    [Fact]
    public void LifecycleSurfaceIsExactlySevenTools()
    {
        var toolNames = typeof(ProjectLifecycleTools)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Select(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "archive_project", "close_project", "create_project", "get_project_status",
                "open_project", "save_project", "save_project_as"
            },
            toolNames);
    }

    [Fact]
    public async Task WriteToolWithoutToken_ReturnsPreviewWithTokenAndInstructions()
    {
        // open_project's preview state is local filesystem metadata — no worker involved.
        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            projectPath: "C:\\Projects\\Line.ap21");

        using var doc = System.Text.Json.JsonDocument.Parse(result);
        Assert.Equal("open_project", doc.RootElement.GetProperty("toolName").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("safetyToken").GetString()));
        Assert.Contains("confirm=true", doc.RootElement.GetProperty("instructions").GetString());
    }

    [Fact]
    public async Task WriteToolWithTokenButNoConfirm_RejectsBeforeAnyWork()
    {
        // workerClient is null: reaching any worker call would throw NullReferenceException.
        var result = await ProjectLifecycleTools.CloseProject(
            workerClient: null!,
            confirm: false,
            safetyToken: "some-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }

    [Fact]
    public async Task WriteToolWithBadToken_PointsBackAtTheTokenlessCall()
    {
        var result = await ProjectLifecycleTools.OpenProject(
            workerClient: null!,
            projectPath: "C:\\Projects\\Line.ap21",
            confirm: true,
            safetyToken: "bogus-token");

        Assert.Contains("Safety token", result);
        Assert.Contains("open_project (without safetyToken)", result);
    }
}
```

In `TiaMcpServer.Tests/ProjectLifecycleToolTests.cs`, delete the two facts `OpenProjectRejectsUnconfirmedRequests` and `SaveProjectAsRejectsUnconfirmedRequests` (their premise — tokenless calls are errors — is gone) and append:

```csharp
    [Fact]
    public async Task SaveProjectAsWithTokenButNoConfirm_Rejects()
    {
        var result = await ProjectLifecycleTools.SaveProjectAs(
            workerClient: null!,
            targetDirectory: "C:\\Projects",
            targetName: "LineCopy",
            confirm: false,
            safetyToken: "some-token");

        Assert.Contains("confirm=true", result);
        Assert.Contains("without safetyToken", result);
    }
```

(the two `[Theory]` blocks in this file — MCP metadata and client method presence — stay as they are; the seven tool names and `confirm=true` descriptions both still hold.)

Append to `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs` the full collapsed-flow round trip (the fake worker answers `open_project` and `get_project_status` for scenario `"ok"`; `DescribePathState("ok")` is deterministic between the two calls because the file never exists):

```csharp
    [Fact]
    public async Task CollapsedOpenProject_PreviewThenApply_RoundTrips()
    {
        using var client = CreateClient();

        var preview = await ProjectLifecycleTools.OpenProject(client, projectPath: "ok");
        using var previewDoc = System.Text.Json.JsonDocument.Parse(preview);
        var token = previewDoc.RootElement.GetProperty("safetyToken").GetString();

        var applied = await ProjectLifecycleTools.OpenProject(
            client,
            projectPath: "ok",
            confirm: true,
            safetyToken: token);

        using var appliedDoc = System.Text.Json.JsonDocument.Parse(applied);
        Assert.Equal("open_project", appliedDoc.RootElement.GetProperty("toolName").GetString());
        Assert.True(appliedDoc.RootElement.GetProperty("success").GetBoolean());
    }
```

Also add `using TiaMcpServer.Tools;` to the top of `OpennessWorkerClientIntegrationTests.cs`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~WriteToolSafetyTokenTests|FullyQualifiedName~ProjectLifecycleToolTests|CollapsedOpenProject" -v q`
Expected: FAIL — preview methods still exist; tokenless `OpenProject` returns "Operation not confirmed" instead of a preview.

- [ ] **Step 3: Add `instructions` to the preview JSON**

In `TiaMcpServer/Safety/WriteSafetyService.cs`, change `CreatePreview`'s signature and payload (anchor: `string? diff = null)` and the serialized anonymous object):

```csharp
    public string CreatePreview(
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        string currentState,
        string? diff = null,
        string? instructions = null)
```

and include the field in the returned JSON:

```csharp
        return JsonSerializer.Serialize(
            new
            {
                toolName,
                target,
                summary,
                currentStateHash,
                requestedInputHash,
                expiresAtUtc,
                safetyToken = token,
                diff,
                instructions
            },
            JsonOptions);
```

In `TiaMcpServer/Safety/WriteSafetyTooling.cs`, extend the pass-through the same way (anchor: `public static string CreatePreview(`):

```csharp
    public static string CreatePreview(
        string toolName,
        string? projectPath,
        object target,
        string summary,
        object requestedInput,
        WorkerCallResult currentState,
        string? diff = null,
        string? instructions = null)
    {
        if (!currentState.Success)
        {
            return $"Could not read current state before preview. Error: {currentState.Error}";
        }

        return WriteSafetyService.Shared.CreatePreview(
            toolName,
            projectPath,
            target,
            summary,
            requestedInput,
            currentState.Payload,
            diff,
            instructions);
    }
```

In `TiaMcpServer/Batch/BatchTools.cs`, `PreviewWriteBatch` — extend the final call (anchor: `return WriteSafetyService.Shared.CreatePreview(`):

```csharp
        return WriteSafetyService.Shared.CreatePreview(
            ApplyToolName,
            projectPath,
            targets,
            summary,
            operations,
            snapshot.CombinedState,
            diff: null,
            instructions: "Preview only — nothing was changed. To apply, call apply_write_batch with the identical operations list, confirm=true, and this safetyToken.");
```

If any existing test asserts the exact set of preview-JSON properties, extend it with the (null) `instructions` field rather than weakening the assert.

- [ ] **Step 4: Rewrite `ProjectLifecycleTools`**

Replace the entire content of `TiaMcpServer/Tools/ProjectLifecycleTools.cs` with:

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;
using TiaMcpServer.Safety;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Tools
{
    /// <summary>
    /// Project lifecycle tools. Every write tool is self-previewing: call it WITHOUT a
    /// safetyToken to get a preview plus a single-use token, then call it again with the
    /// same arguments plus confirm=true and the token to apply. target/requestedInput are
    /// built exactly once per method so preview and apply can never drift apart.
    /// </summary>
    [McpServerToolType]
    public static class ProjectLifecycleTools
    {
        private const string SafetyFlowDescription =
            "Two-step safety flow in one tool: call WITHOUT safetyToken to get a preview and a single-use token "
            + "(expires after 10 minutes), review it, then call again with the same arguments plus confirm=true and the safetyToken.";

        [McpServerTool(Name = "get_project_status")]
        [Description("Get status and metadata for the active TIA Portal project.")]
        public static async Task<string> GetProjectStatus(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null)
        {
            return (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText();
        }

        [McpServerTool(Name = "open_project")]
        [Description("Open a TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> OpenProject(
            OpennessWorkerClient workerClient,
            [Description("Path to the .ap21 project file to open.")] string projectPath,
            [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false,
            [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null,
            [Description("Set true to allow rebinding this MCP session from a previously bound project.")] bool forceRebind = false)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, forceRebind };

            if (string.IsNullOrWhiteSpace(safetyToken))
            {
                return WriteSafetyTooling.CreatePreview(
                    "open_project",
                    projectPath,
                    target,
                    $"Open and bind TIA Portal project '{projectPath}'.",
                    requestedInput,
                    WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)),
                    diff: null,
                    instructions: ApplyInstructions("open_project"));
            }

            if (!confirm)
            {
                return ConfirmRequired("open_project");
            }

            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                PreviewHint("open_project"),
                "open_project",
                projectPath,
                target,
                requestedInput,
                () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribePathState(projectPath)))).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.OpenProjectAsync(projectPath, forceRebind).ConfigureAwait(false);
            var status = result.Success
                ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText()
                : null;

            WriteSafetyService.Shared.AppendAudit("open_project", projectPath, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("open_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "create_project")]
        [Description("Create a new TIA Portal project and bind this MCP session to it. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> CreateProject(
            OpennessWorkerClient workerClient,
            [Description("Directory where the project folder should be created.")] string projectDirectory,
            [Description("Name of the new TIA Portal project.")] string projectName,
            [Description("Optional project author metadata.")] string? author = null,
            [Description("Optional project comment metadata.")] string? comment = null,
            [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false,
            [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectDirectory, projectName };
            var requestedInput = new { projectDirectory, projectName, author, comment };

            if (string.IsNullOrWhiteSpace(safetyToken))
            {
                return WriteSafetyTooling.CreatePreview(
                    "create_project",
                    null,
                    target,
                    $"Create TIA Portal project '{projectName}' in '{projectDirectory}'.",
                    requestedInput,
                    WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)),
                    diff: null,
                    instructions: ApplyInstructions("create_project"));
            }

            if (!confirm)
            {
                return ConfirmRequired("create_project");
            }

            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                PreviewHint("create_project"),
                "create_project",
                null,
                target,
                requestedInput,
                () => Task.FromResult(WorkerCallResult.Ok(WriteSafetyTooling.DescribeProjectCreationState(projectDirectory, projectName)))).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CreateProjectAsync(projectDirectory, projectName, author, comment)
                .ConfigureAwait(false);
            var status = result.Success
                ? (await workerClient.GetProjectStatusAsync(null).ConfigureAwait(false)).ToText()
                : null;

            WriteSafetyService.Shared.AppendAudit("create_project", null, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("create_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "save_project")]
        [Description("Save the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> SaveProject(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false,
            [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath };

            if (string.IsNullOrWhiteSpace(safetyToken))
            {
                var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
                return WriteSafetyTooling.CreatePreview(
                    "save_project",
                    projectPath,
                    target,
                    "Save the active TIA Portal project.",
                    requestedInput,
                    currentState,
                    diff: null,
                    instructions: ApplyInstructions("save_project"));
            }

            if (!confirm)
            {
                return ConfirmRequired("save_project");
            }

            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                PreviewHint("save_project"),
                "save_project",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.SaveProjectAsync(projectPath).ConfigureAwait(false);
            var status = result.Success
                ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText()
                : null;

            WriteSafetyService.Shared.AppendAudit("save_project", projectPath, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "save_project_as")]
        [Description("Save the active TIA Portal project to a copy directory. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> SaveProjectAs(
            OpennessWorkerClient workerClient,
            [Description("Parent directory for the copied project.")] string targetDirectory,
            [Description("Name of the copied project directory.")] string targetName,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set true to bind this MCP session to the copied project path after save-as.")] bool rebind = true,
            [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false,
            [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath, targetDirectory, targetName };
            var requestedInput = new { projectPath, targetDirectory, targetName, rebind };

            if (string.IsNullOrWhiteSpace(safetyToken))
            {
                var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
                return WriteSafetyTooling.CreatePreview(
                    "save_project_as",
                    projectPath,
                    target,
                    $"Save active project as '{targetName}' in '{targetDirectory}'.",
                    requestedInput,
                    currentState,
                    diff: null,
                    instructions: ApplyInstructions("save_project_as"));
            }

            if (!confirm)
            {
                return ConfirmRequired("save_project_as");
            }

            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                PreviewHint("save_project_as"),
                "save_project_as",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.SaveProjectAsAsync(projectPath, targetDirectory, targetName, rebind)
                .ConfigureAwait(false);
            var status = result.Success
                ? (await workerClient.GetProjectStatusAsync(rebind ? null : projectPath).ConfigureAwait(false)).ToText()
                : null;

            WriteSafetyService.Shared.AppendAudit("save_project_as", projectPath, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("save_project_as", result, "get_project_status", status);
        }

        [McpServerTool(Name = "archive_project")]
        [Description("Archive the active TIA Portal project. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> ArchiveProject(
            OpennessWorkerClient workerClient,
            [Description("Directory where the archive should be written.")] string archiveDirectory,
            [Description("Archive file name, with or without extension.")] string archiveName,
            [Description("Archive mode: None, DiscardRestorableData, Compressed, or DiscardRestorableDataAndCompressed.")] string? mode = null,
            [Description("Save the project before archiving.")] bool saveBeforeArchive = true,
            [Description("Optional path to a .ap21 project file. If omitted, uses the project currently open in TIA Portal.")] string? projectPath = null,
            [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false,
            [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath, archiveDirectory, archiveName };
            var requestedInput = new { projectPath, archiveDirectory, archiveName, mode, saveBeforeArchive };

            if (string.IsNullOrWhiteSpace(safetyToken))
            {
                var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
                return WriteSafetyTooling.CreatePreview(
                    "archive_project",
                    projectPath,
                    target,
                    $"Archive active project to '{archiveDirectory}\\{archiveName}'.",
                    requestedInput,
                    currentState,
                    diff: null,
                    instructions: ApplyInstructions("archive_project"));
            }

            if (!confirm)
            {
                return ConfirmRequired("archive_project");
            }

            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                PreviewHint("archive_project"),
                "archive_project",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.ArchiveProjectAsync(
                projectPath,
                archiveDirectory,
                archiveName,
                mode,
                saveBeforeArchive).ConfigureAwait(false);
            var status = result.Success
                ? (await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false)).ToText()
                : null;

            WriteSafetyService.Shared.AppendAudit("archive_project", projectPath, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("archive_project", result, "get_project_status", status);
        }

        [McpServerTool(Name = "close_project")]
        [Description("Close the active TIA Portal project and clear this MCP session binding. Requires confirm=true and a safetyToken. " + SafetyFlowDescription)]
        public static async Task<string> CloseProject(
            OpennessWorkerClient workerClient,
            [Description("Optional path to a .ap21 project file. If omitted, closes the currently bound/open project.")] string? projectPath = null,
            [Description("Save the project before closing it.")] bool saveBeforeClose = true,
            [Description("Set to true together with safetyToken to apply. Ignored on the preview call.")] bool confirm = false,
            [Description("Safety token from this tool's preview call. Omit to get a preview + token.")] string? safetyToken = null)
        {
            var target = new { projectPath };
            var requestedInput = new { projectPath, saveBeforeClose };

            if (string.IsNullOrWhiteSpace(safetyToken))
            {
                var currentState = await workerClient.GetProjectStatusAsync(projectPath).ConfigureAwait(false);
                return WriteSafetyTooling.CreatePreview(
                    "close_project",
                    projectPath,
                    target,
                    "Close the active TIA Portal project.",
                    requestedInput,
                    currentState,
                    diff: null,
                    instructions: ApplyInstructions("close_project"));
            }

            if (!confirm)
            {
                return ConfirmRequired("close_project");
            }

            var safety = await WriteSafetyTooling.ValidateForApplyAsync(
                safetyToken,
                PreviewHint("close_project"),
                "close_project",
                projectPath,
                target,
                requestedInput,
                () => workerClient.GetProjectStatusAsync(projectPath)).ConfigureAwait(false);
            if (!safety.IsValid)
            {
                return safety.Error!;
            }

            var result = await workerClient.CloseProjectAsync(projectPath, saveBeforeClose).ConfigureAwait(false);

            WriteSafetyService.Shared.AppendAudit("close_project", projectPath, target, requestedInput, safety.CurrentState, result.ToText());
            return WriteSafetyTooling.BuildApplyResult("close_project", result, "get_project_status", null);
        }

        private static string ApplyInstructions(string toolName)
            => $"Preview only — nothing was changed. To apply, call {toolName} again with the same arguments plus confirm=true and this safetyToken.";

        private static string ConfirmRequired(string toolName)
            => $"Safety token provided but confirm=false. Set confirm=true and resend the safetyToken to apply, "
                + $"or call {toolName} without safetyToken for a fresh preview.";

        private static string PreviewHint(string toolName)
            => $"{toolName} (without safetyToken)";
    }
}
```

- [ ] **Step 5: Run the full suite**

Run: `dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q`
Expected: PASS (211 tests — net +3: the rewritten token-test class grows from 7 to 10 cases, 2 lifecycle facts are deleted, 1 lifecycle fact and 1 integration test are added).

- [ ] **Step 6: Commit**

```powershell
git add TiaMcpServer/Safety/WriteSafetyService.cs TiaMcpServer/Safety/WriteSafetyTooling.cs TiaMcpServer/Batch/BatchTools.cs TiaMcpServer/Tools/ProjectLifecycleTools.cs TiaMcpServer.Tests/WriteToolSafetyTokenTests.cs TiaMcpServer.Tests/ProjectLifecycleToolTests.cs TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs
git commit -m "feat: collapse lifecycle preview/apply pairs into self-previewing tools (16 -> 10)"
```

---

### Task 11: Documentation, improvement-plan bookkeeping, final verification

**Files:**
- Modify: `README.md`, `docs/IMPROVEMENT_PLAN.md`

**Interfaces:** none — documentation only, but treat the README as part of the tool contract (three-sources-of-truth rule from item 0.5).

- [ ] **Step 1: Update README.md**

Anchored by current text (line numbers drift):
- `"The server currently exposes 16 tools."` → `"The server currently exposes 10 tools."`
- The "Project tools" bullet listing `preview_open_project / preview_create_project / …` → replace with:

```markdown
- `open_project` / `create_project` / `save_project` / `save_project_as` / `archive_project` / `close_project` - project lifecycle writes. These stay single-tool only (not batchable) and are self-previewing: call the tool WITHOUT `safetyToken` to get a preview plus a single-use token, then call it again with `confirm=true` and the token to apply.
```

- The write-safety section sentence starting `"Every MCP write operation uses a preview-then-apply workflow. Call the matching `preview_*` tool first, …"` → replace with:

```markdown
Every MCP write operation uses a preview-then-apply workflow. Batch data writes preview with `preview_write_batch` and apply with `apply_write_batch`. Project lifecycle writes are self-previewing: call the write tool WITHOUT `safetyToken` to get the preview (summary, `currentStateHash`, `requestedInputHash`, a fresh single-use `safetyToken`, and `instructions`), review it, then call the same tool again with the same arguments plus `confirm=true` and the `safetyToken`.
```
- The Inspector step `"Click `List Tools` and verify the 16 tools appear."` → `10 tools`.
- The lifecycle example near `"Project lifecycle writes remain single-tool and use their own preview-then-apply flow"` and `"<token from preview_open_project>"` → show the collapsed flow: first call `open_project` with only `projectPath` (returns preview + token), then the same call plus `confirm=true` and `"safetyToken": "<token from the preview call>"`.
- In the batch section (`execute_read_batch` description area), add one sentence: bound large reads with `depth`/`startPath` (`browse_project_tree`) and `maxResults` (`search_equipment_catalog` default 50, `read_cross_references`); oversized batch responses are truncated/omitted server-side with explicit markers.
- Performance note wherever the README explains the worker (search for "worker"): the server now keeps one persistent net48 worker process attached to TIA Portal; it restarts automatically after a crash or timeout, and requests are serialized.

- [ ] **Step 2: Update docs/IMPROVEMENT_PLAN.md**

- Mark 2.1, 2.3, 2.4, 2.5 rows `— DONE 2026-07-16` (keep 2.2's existing DONE note).
- In "Testing gaps to close alongside", update the fake-worker sentence: timeout path and persistent-worker restart logic are now covered by `OpennessWorkerClientIntegrationTests` — DONE 2026-07-16.
- In "Phase 3", note on 3.1 that 2.4 removed the duplicated `target`/`requestedInput` construction (drift bomb resolved); remaining value is only the per-op descriptor extraction, reassess before starting.

- [ ] **Step 3: Final verification**

```powershell
dotnet build TiaMcpServer.sln
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -v q
```

Expected: build succeeded; all tests green (211). Then refresh the knowledge graph:

```powershell
graphify update .
```

- [ ] **Step 4: Commit**

```powershell
git add README.md docs/IMPROVEMENT_PLAN.md graphify-out
git commit -m "docs: document the 10-tool surface, bounded reads, and persistent worker"
```

- [ ] **Step 5: Push and open the PR**

Stop here and confirm with the user before pushing (external action):

```powershell
git push -u origin 26-07-16-improvement-phase2
```

PR body: summarize 2.1/2.3/2.4/2.5, list the tool-surface change (16 → 10) as a **breaking change for MCP clients**, note the new `warnings`/`omitted`/`instructions` response fields, and flag that worker-side behavior (persistent session, managed open/close) still needs a manual smoke test on the TIA Portal machine (no Siemens DLLs in CI).

---

## Execution-order notes

- Tasks 1–2 (2.5) are independent pure logic — safe warm-up.
- Tasks 3→4→5 (2.1) must run in order: protocol first, fake second, client flip last. Task 6 (worker session) can land any time after Task 3 but before real-hardware testing.
- Task 7 (filter) is independent; Task 8 depends on 3 (warnings channel for truncation notices) and 7; Task 9 is independent of 7–8 but lands after them so the trailer text references parameters that exist.
- Task 10 (2.4) is independent of 2.1/2.3 except for the integration round-trip test, which needs Task 5's fake-worker client.
- Manual smoke test on the TIA machine after merge: persistent attach across calls, same-project reuse, forceRebind close-and-switch, TIA-Portal-closed-mid-session recovery (worker restarts and reattaches on next request).

