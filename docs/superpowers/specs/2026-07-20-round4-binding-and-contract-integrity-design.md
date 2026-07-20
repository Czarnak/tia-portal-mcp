# Round 4 — Session binding and contract integrity (design)

Date: 2026-07-20
Source: outstanding items in `docs/IMPROVEMENT_PLAN.md` after Phase 3 merged (PR #10, `c467a97`).

## Scope

In scope: **A** session-binding gap, **B** `WriteSafetyService` via DI (item 3.5b, and the audit-trail
pollution it causes), **C** `deviceItemName` declared-but-not-forwarded, **E** an invariant test that
makes that bug class unrepresentable, **H** `TiaJson.Presentation.MakeReadOnly()`.

Out of scope, with reasons:

| Item | Why not now |
|------|-------------|
| **D** — forward `externalAccessible`/`externalVisible`/`externalWritable`/`isSafety` on `create_tag` | Needs real Openness V21 to know whether these are settable at tag-creation time. |
| **I** — `NetworkDeviceConfigurator` "UNVERIFIED SDK CALL" reflection paths | Needs hardware to pin exact method signatures. |
| **F** — 3.3 double-dispatch collapse (~250 lines) | Deliberately deferred once already; the `OptionalFields` work below is a down payment on it. |
| **G** — collapse `BatchPayloadBudget.ReadBatchResponseLength` | The plan itself says measure the extra serialization cost first. |

**D and I are the next round**, gated on TIA machine access. Section C below narrows D's remaining
question to a single yes/no that one hardware session can answer.

## A — Session binding

### Problem

Reproduced live against V21 on 2026-07-20 and confirmed in code:

1. A session that never passes `projectPath` stays unbound forever.
   `ProjectSessionBinding.TryResolve(null)` returns the (null) binding without adopting anything.
   This is the workflow `tia-mcp doctor` actively recommends.
2. The first call that *does* pass an explicit path is adopted unconditionally
   (`ProjectSessionBinding.cs:61-66`), so a mistyped-but-real path silently retargets the session.
3. That path then reaches `WithProject` (`TiaMcpServer.OpennessWorker/Program.cs:573-591`), which
   calls `session.OpenProject(...)` for **every read tool**. `TiaPortalSession.cs:99-107` then opens
   the requested project *alongside* the user's rather than refusing. Only TIA Portal's own
   "Another project is already open" refusal stopped it live.

`TiaPortalSession.cs:61-64` already rejects non-existent paths via `File.Exists`, so a purely
hallucinated path fails. The residual exposure is a **mistyped-but-real** path.

### A1 — The worker reports ground truth

`WorkerResponse` gains:

```csharp
/// <summary>
/// Absolute path of the project the worker actually operated on, or null when no project was
/// attached. Ground truth for session binding: the host binds to this, never to the requested path.
/// </summary>
public string? ResolvedProjectPath { get; set; }
```

It is stamped in **one place** — the dispatch choke point in worker `Program.cs`, which reaches the
static `_sharedSession` — so every method gets it without 22 methods each remembering to set it.

Requires exposing `TiaPortalSession.TryReadCurrentProjectPath` (private today at
`TiaPortalSession.cs:113`) as an internal/public `CurrentProjectPath` property.

### A2 — The host adopts the resolved path, never the requested one

`WorkerCallResult` gains `ResolvedProjectPath` as an **init-only property**, not a fifth positional
parameter. The record is positional (`WorkerCallResult.cs:9-13`); an init-only property leaves the
`Ok`/`Fail` factories and every construction site untouched, and `InvokeWorkerAsync` sets it with a
`with` expression.

`ProjectSessionBinding.TryResolve` becomes **non-mutating**:

| Bound | Requested | Today | After |
|-------|-----------|-------|-------|
| null | null | effective = null, stays unbound | unchanged |
| null | X | **adopts X**, effective = X | effective = X, **stays unbound** |
| A | null | effective = A | unchanged |
| A | A | effective = A | unchanged |
| A | B | error | unchanged |

`SendBoundProjectRequestAsync` binds *after* success:

```csharp
var result = await InvokeWorkerAsync(request);
if (result.Success && sessionWasUnbound && result.ResolvedProjectPath is not null)
{
    _projectSessionBinding.Bind(result.ResolvedProjectPath, forceRebind: true, out _);
}
```

The provisional-binding cleanup at `OpennessWorkerClient.cs:647-652` is **deleted**. Nothing is bound
provisionally any more, so there is nothing to roll back — a net simplification, not an addition.

Payoff: `get_project_status` with no `projectPath` now binds the session to whatever the GUI has
open, so the default recommended workflow is protected from the second call onward. The live repro's
step 2 is then rejected locally by the existing guard and never reaches the worker.

### A3 — Reads never open a project alongside another

The policy is a pure function in `TiaMcpServer.Contracts`, so it is unit-testable from
`TiaMcpServer.Tests` (net8.0, which cannot link Siemens-referencing worker files):

```csharp
public enum ProjectOpenDecision { UseAttached, OpenRequested, Refuse }

public static class ProjectOpenPolicy
{
    public static ProjectOpenDecision Decide(string? currentPath, string? requestedPath);
}
```

| currentPath | requestedPath | Decision |
|-------------|---------------|----------|
| null | null | `UseAttached` (caller then fails with the existing "No project is open" message) |
| null | X | `OpenRequested` — nothing to clobber |
| A | null | `UseAttached` |
| A | A (case-insensitive, full-path-normalized) | `UseAttached` |
| A | B | `Refuse` |

Both call sites use it, replacing two copies of the same unguarded `session.OpenProject(...)`:

- `WithProject` (`Program.cs:573-591`)
- `SearchEquipmentCatalog`'s hand-rolled equivalent (`Program.cs:166-173`)

`Refuse` returns:

> `TIA Portal currently has project 'A' open, but this request targets 'B'. Read operations never
> switch projects. Omit projectPath to use the open project, or call open_project to switch.`

`TiaPortalSession.OpenProject` is **unchanged**. The open-alongside branch stays reachable from
`open_project`, which is token-gated — consistent with the write-safety model, where changing what
TIA Portal has open is a previewed, confirmed operation.

### Resulting behaviour

| Situation | After this change |
|-----------|-------------------|
| Unbound, `SimpleProject` open in GUI, `get_project_status` with no path | Succeeds; session binds to `SimpleProject`. |
| …then `browse_project_tree(path=LibReadTest)` | Rejected locally by the binding guard; never reaches the worker. |
| Unbound, nothing open in GUI, `browse_project_tree(path=LibReadTest)` | Worker opens it (nothing to clobber); session binds to it. |
| …then `browse_project_tree(path=Other)` | Rejected by the binding guard. |
| Any read tool while a *different* project is open | Refused by `ProjectOpenPolicy`, with the message above. |

### Regression risk

Existing tests assert adopt-on-resolve. `ProjectSessionBinding` tests and the provisional-clear
integration test need **rewriting, not tweaking**. This is expected work, called out so it is not
mistaken for breakage during implementation.

## B — `WriteSafetyService` via DI

### Problem

`WriteSafetyService.Shared` is reached statically from 12 call sites — 6 in `ProjectLifecycleTools`
(lines 38, 54, 70, 86, 102, 117), 4 in `BatchTools` (64, 108, 126, 144), 2 in `WriteSafetyTooling`
(32, 61) — plus the registration at `Program.cs:24`. `TiaMcpServer.Tests` links those files and exercises
those tools, so every `dotnet test` run appends real records to `%LOCALAPPDATA%\TiaMcpServer\audit`.
Measured live: 39 of 42 records were produced by the test suite, diluting the forensic record for
PLC-mutating operations to ~7% signal.

The `WriteSafetyService(getUtcNow, tokenLifetime, auditDirectory)` constructor already supports
redirecting the audit directory. No test going through the tool layer can reach it while the tools
resolve `.Shared` statically.

### Change

- `Program.cs:24` becomes `builder.Services.AddSingleton(new WriteSafetyService());`
- **Delete `WriteSafetyService.Shared`.** The deletion is the enforcement: with no static, no test can
  reach the production audit directory by accident. Leaving it in place and merely not using it would
  let the pollution return.
- `WriteSafetyTooling.ValidateForApplyAsync` and `CreatePreview` take `WriteSafetyService safety` as
  their first parameter.
- `WriteSafetyService.NormalizeProjectPath` stays static — it is pure, so `BatchOperationCatalog.cs:128`
  and the three uses inside `WriteSafetyTooling` are untouched.
- The 6 `ProjectLifecycleTools` write tools and `BatchTools.PreviewWriteBatch` / `ApplyWriteBatch` gain
  a `WriteSafetyService safety` parameter after `OpennessWorkerClient workerClient`. The MCP SDK
  injects registered DI services into tool parameters — the same mechanism that already supplies
  `workerClient`, so no new wiring.
- `BatchTools.ExecuteReadBatch` is unaffected (reads write no audit records).

### Testing

Tool-layer tests construct `new WriteSafetyService(() => now, lifetime, tempDir)`. Add one test
asserting that a tool-layer run leaves the default `%LOCALAPPDATA%\TiaMcpServer\audit` directory
untouched.

## C + E — Catalog field surface, rejection, and the echo test

### Problem

`BatchOperationSpec` (`BatchOperationCatalog.cs:11-14`) carries `Name`, `Category`, `RequiredFields`
— there is no declared-optional-field map. Each operation's optional surface exists only as
`[Description]` prose on `BatchOperationRequest` properties, which cannot be checked against what
`BatchWorkerInvoker` actually forwards. Two live instances of the resulting drift:

- `deviceItemName` is described unscoped but only `add_network_device` forwards it
  (`BatchWorkerInvoker.cs:54` vs `:55`).
- `externalAccessible`/`externalVisible`/`externalWritable`/`isSafety` are described as generic tag
  attributes but only `update_tag` forwards them (`BatchWorkerInvoker.cs:49` vs `:48`).

Phase 0.4 found the same bug class with `newName`.

### C1 — `OptionalFields` on the spec

```csharp
public sealed record BatchOperationSpec(
    string Name,
    BatchOperationCategory Category,
    IReadOnlyList<string> RequiredFields,
    IReadOnlyList<string> OptionalFields);
```

Universal fields — `operationId`, `operation`, `projectPath` — are excluded from every per-operation
table and checked once. The per-operation tables are transcribed directly from
`BatchWorkerInvoker.InvokeAsync` (`BatchWorkerInvoker.cs:32-64`), which is one line per operation and
is the authoritative forwarding map. That makes the table cheap to author and easy to review.

### C2 — Reject inapplicable fields

`Validate()` reflects over `BatchOperationRequest` (flat, all-nullable, so `prop.GetValue(op) is not
null` is a sufficient "was it set?" test) and produces an aggregated error for any non-null property
outside `Universal ∪ Required ∪ Optional`:

> `deviceItemName is not valid for configure_network_device. Valid optional fields: ipAddress,
> subnetMask, pnDeviceName, subnetName, ioSystemName.`

This follows 0.2's aggregate-all-errors rule and 0.1's list-the-valid-names precedent. The reflection
result is computed once and cached.

`deviceItemName`'s `[Description]` in `BatchOperationRequest` is scoped to `add_network_device` so
the prose matches the mechanism.

### C3 — Behaviour change this forces on `create_tag`

Setting `externalAccessible`/`externalVisible`/`externalWritable`/`isSafety` on `create_tag` becomes
an **error** rather than a silent drop. That is honest about today's behaviour — those four values
are discarded at `BatchWorkerInvoker.cs:48`. Erroring is strictly better than silent loss, and it is
reversible: item D's hardware round decides whether to move them into `create_tag`'s `OptionalFields`
and forward them.

This narrows D to a single question a hardware session can answer: *does Openness V21 allow setting
these four attributes at tag-creation time?*

### E — Echo test

`TiaMcpServer.FakeWorker` gains an echo mode, selected by the `TIA_FAKEWORKER_MODE=echo` environment
variable so it applies to every method uniformly: the worker returns the received `WorkerRequest`,
serialized, as its payload.

The catalog needs an enumerator over its specs. `BatchOperationCatalog` exposes `ReadOperationNames`,
`WriteOperationNames`, and `TryGetSpec` today but no way to walk every spec, so add
`public static IReadOnlyCollection<BatchOperationSpec> All => Specs.Values;`.

The test drives the real pipeline — `BatchWorkerInvoker` → `OpennessWorkerClient` (constructed with
its existing `workerExecutablePath` override) → fake worker — once per catalog operation:

```
foreach (spec in BatchOperationCatalog.All)
    request  = BuildWithSentinels(spec)          // every Required + Optional field
    echoed   = await BatchWorkerInvoker.InvokeAsync(client, request)
    foreach (field in spec.RequiredFields ∪ spec.OptionalFields)
        Assert.Contains(sentinelFor(field), echoed)
```

Assertions are **by value, not by property name**, so the test survives renaming on either side of
the boundary.

Two implementation details that would otherwise bite:

1. **Sentinels cannot be blind.** `filter`, `blockType`, `language`, `dataType`, and `mode` are
   validated host-side; a `"__sentinel__"` string is rejected before reaching the worker. The builder
   needs a field → valid-sample-value map, defaulting to a distinctive sentinel string for
   unvalidated fields, `true` for booleans, and a distinctive number for integers.
2. **`ResolveDeviceItemName` (`BatchWorkerInvoker.cs:66-67`) is the only value transform** in the
   whole map — it defaults `deviceItemName` to `deviceName`. Distinct sentinels for the two fields
   make it a non-issue.

This test tests behaviour rather than source text, so it survives the deferred 3.3 refactor and will
independently validate it when it lands.

## H — `TiaJson.Presentation.MakeReadOnly()`

`TiaJson.Presentation` is a public mutable `JsonSerializerOptions` whose formatting feeds the
safety-token `requestedInputHash`. A static constructor calls `MakeReadOnly()` after configuration;
one test asserts `IsReadOnly`. This turns the existing "keep this stable" comment into a guarantee.

## Sequencing

1. **H** — isolated, two lines plus a test.
2. **B** — unblocks clean tool-layer testing for everything after it.
3. **A1 + A2**, then **A3** — A3 is independent of A1/A2 but shares the same test surface.
4. **C + E** — C1 first (the spec table), then C2 (rejection), then E (the echo test that proves both).

## Testing

| Change | Coverage |
|--------|----------|
| A1 | Echo/scripted FakeWorker response carries `ResolvedProjectPath`; host reads it. |
| A2 | `ProjectSessionBinding` unit tests for the non-mutating resolve matrix; integration test that an unbound session binds after the first successful pathless call. |
| A3 | `ProjectOpenPolicy.Decide` unit tests over the full decision matrix, including case and path-normalization equivalence. |
| B | Tool-layer tests inject a temp audit directory; one test asserts the default directory is untouched. |
| C1/C2 | Catalog validation tests: inapplicable field rejected, error names the valid optional fields, multiple errors aggregate. |
| E | The echo test itself, one case per catalog operation. |
| H | `Assert.True(TiaJson.Presentation.IsReadOnly)`. |

Existing tests that assert adopt-on-resolve behaviour are rewritten as part of A2.
