# Phase 3 — Simplification (design)

Date: 2026-07-20
Source: `docs/IMPROVEMENT_PLAN.md` Phase 3, re-scoped against the code as it stands after Phases 0–2.

## Scope decision

Phase 3 was six items. Two are already resolved by earlier phases and are dropped:

| # | Original item | Why dropped |
|---|---|---|
| 3.1 | Generic executor for lifecycle preview/apply pairs | Phase 2.4 collapsed `ProjectLifecycleTools.cs` from 374 to 125 lines. The six tools are ~12 lines each and the shared machinery already lives in `WriteSafetyTooling`. What remains is genuinely per-operation: a distinct worker call, description, and `requestedInput` shape. A descriptor table would add indirection without removing duplication. |
| 3.4 | Merge ~90 lines of reflection helpers into `OpennessReflection` | Phase 1.7 already did the merge. `EquipmentCatalogSearcher`'s remaining privates are `HasReadableProperty` (3 lines), `ReadStringProperty` (a 2-line passthrough that already delegates to `OpennessReflection.ReadProperty`), plus `AppendPath` and `Contains`, which are not reflection. ~10 lines of residual value — fold in opportunistically if the file is touched for another reason. |

Four items remain: **3.2, 3.3, 3.5a, 3.6**. All are behavior-preserving.

**3.3 is deferred (decided 2026-07-20).** It is the only item that can silently change per-operation behavior, and it depends on a characterization-test seam that has not been built yet. Its design is retained in full below so it can be picked up as its own unit of work without re-deriving the analysis. **This spec's implementation plan covers 3.6, 3.2, and 3.5a only.**

The `WriteSafetyService` DI conversion (the second half of the original 3.5) is deferred. It would thread the service through the static `WriteSafetyTooling` API and add a parameter to six MCP tool methods, and the testability it buys is already partly available through the existing `WriteSafetyService(getUtcNow, tokenLifetime, auditDirectory)` constructor.

---

## 3.3 — Collapse the double dispatch (DEFERRED)

**Not in scope for this plan.** Retained as a design record; see the scope decision above.

The substantive item. Everything else is mechanical.

### Problem

Each batch operation is described by two independent tables that nothing keeps in sync:

- `BatchOperationCatalog.Specs` — a `BatchOperationSpec` per operation declaring `RequiredFields` as field-name strings, resolved against `BatchOperationRequest` by `IsFieldPresent(op, field)`.
- `BatchWorkerInvoker.InvokeAsync` — a switch mapping operation strings onto 22 `OpennessWorkerClient` wrapper methods, each of which does nothing but set `WorkerRequest` fields and call `SendBoundProjectRequestAsync`.

Drift between them is silent and has already happened: Phase 0.4 found `newName` documented in the schema and accepted by validation, but never forwarded by `BatchWorkerInvoker` — the parameter simply did nothing.

### Approach

Extend `BatchOperationSpec` into the single source of truth for each operation, and extract request construction into a pure function.

```csharp
// BatchOperationCatalog.cs — one entry per operation
new BatchOperationSpec(
    Name:            "create_tag",
    Category:        BatchOperationCategory.Write,
    WorkerMethod:    "create_tag",
    RequiredFields:  ["tableName", "name", "dataType"],
    ForwardedFields: ["plcName", "tableName", "folderPath", "name", "dataType",
                      "logicalAddress", "externalAccessible", "externalVisible",
                      "externalWritable", "isSafety"],
    Flags:           WorkerRequestFlags.Confirm | WorkerRequestFlags.AllowTiaConfirmations,
    EmptyPayload:    "{}")
```

```csharp
// Batch/BatchRequestBuilder.cs — pure, no worker access
public static class BatchRequestBuilder
{
    public static WorkerRequest Build(BatchOperationRequest op, string? effectiveProjectPath);
}
```

The same field-name vocabulary then drives validation, schema descriptions, and forwarding. A field that is declared but not forwarded becomes impossible to express.

`OpennessWorkerClient`'s 22 batch-only wrappers collapse into one:

```csharp
public Task<WorkerCallResult> SendBatchRequestAsync(WorkerRequest request, string emptyPayload);
```

The seven lifecycle wrappers (`OpenProjectAsync`, `CreateProjectAsync`, `SaveProjectAsync`, `SaveProjectAsAsync`, `ArchiveProjectAsync`, `CloseProjectAsync`, `GetProjectStatusAsync`) stay as they are — they are called by `ProjectLifecycleTools` and by `OpennessWorkerClientIntegrationTests`, and they carry per-operation binding side effects that do not fit the batch shape.

Being a pure function, `BatchRequestBuilder` is unit-testable with no worker process, matching the convention `BatchOperationCatalog` already follows.

### Per-operation variation the spec must carry

These were verified against the current wrappers. None of them is uniform, so none can be hardcoded as "all writes do X":

1. **`EmptyPayload`** — `"{}"` for every operation except `get_block_content` and `update_block_logic`, which use `string.Empty`.
2. **Flags** — `create_tag` sets both `Confirm` and `AllowTiaConfirmations`; `update_block_logic` sets only `AllowTiaConfirmations`. Modelled as a `[Flags]` enum on the spec.
3. **Field-name mismatch** — `BatchOperationRequest.Filter` maps to `WorkerRequest.CrossReferenceFilter`. The forwarded-field vocabulary is the `BatchOperationRequest` name; the builder owns the translation.
4. **Per-operation normalization** — `read_cross_references` runs `CrossReferenceFilterNames.TryNormalize` and must do so *before* the session binds, so an invalid filter cannot bind the session. This ordering is load-bearing and must survive the refactor.
5. **Per-operation defaulting** — `configure_network_device` falls back to `deviceName` when `deviceItemName` is absent (currently `BatchWorkerInvoker.ResolveDeviceItemName`).

`create_block` needs no special case: it forwards `language` and `obEventClass` as-is (null included) and the worker applies the defaults.

Points 4 and 5 are expressed as optional per-operation hooks on the spec, not as branches inside the builder.

`BatchWorkerInvoker.ReadCurrentStateAsync` — the map from a write operation to the read that captures its pre-state for the safety token — stays a separate concern and is not folded into the builder.

### Safety mechanism

A characterization test is written **before** any production code changes:

- For every operation in `BatchOperationCatalog`, construct a `BatchOperationRequest` with all fields populated with distinguishable values.
- Capture the `WorkerRequest` the *current* code produces, and assert it field-for-field, including `Method`, flags, and `emptyPayload`.
- Run it green against the current implementation, then refactor until it is green again.

This converts 3.3 from a risky rewrite into a provably behavior-preserving one, and is the condition on which the item is worth doing at all. Capturing the current output requires a seam in `OpennessWorkerClient` to intercept the built `WorkerRequest` without a worker process; the existing `Action<WorkerRequest> configure` parameter of `SendBoundProjectRequestAsync` provides it.

Expected reduction: ~250 lines.

---

## 3.2 — Consolidate the project-path binding checks

`OpennessWorkerClient.CanBind` (line ~710) re-implements the null/whitespace guard, the case-insensitive comparison, and the force-rebind escape of `ProjectSessionBinding.Bind`, and duplicates its error text verbatim.

The predicted drift has already occurred: `ProjectSessionBinding` emits two different messages for the same condition.

- `TryResolve`: "…Call open_project with forceRebind=true to rebind this session, or start a new MCP session for a different TIA project."
- `Bind`: "…Start a new MCP session for a different TIA project or set forceRebind=true."

An agent hitting this error gets different recovery advice depending on which path it took.

**Change:** add a non-mutating `ProjectSessionBinding.CanBind(projectPath, forceRebind, out error)` — a dry run of `Bind`. `OpennessWorkerClient.CanBind` delegates to it. The three message sites collapse to one constant.

The unified wording should keep the actionable `forceRebind=true` instruction from `TryResolve`. Phase 0.5 set out to unify these texts and reached `TryResolve` only; `Bind` and `CanBind` kept the older wording. This item finishes that work rather than reopening the wording question. `ProjectSessionBindingTests` covers this type and must be extended for the new method.

~20 lines removed; the behavior change is limited to two error strings becoming consistent.

---

## 3.5a — Two shared `JsonSerializerOptions`

The original plan called for "a single shared `JsonSerializerOptions` (currently duplicated in 4 files)". That is not accurate: there are five non-test declarations holding **two distinct configurations**, and merging them into one would silently change IPC serialization.

| Configuration | Settings | Sites |
|---|---|---|
| Presentation | `CamelCase`, `WriteIndented = false` | `BatchResultFormatter`, `WriteSafetyService`, `WriteSafetyTooling` — byte-identical |
| Wire / IPC | `CamelCase`, `PropertyNameCaseInsensitive` | `PersistentWorkerTransport`; worker `Program.cs` adds `DefaultIgnoreCondition = WhenWritingNull` |

**Change:** add `TiaJson` to `TiaMcpServer.Contracts` (netstandard2.0, referenced by both host and worker) exposing `TiaJson.Presentation` and `TiaJson.Wire`. The worker keeps its `WhenWritingNull` addition as a separate derived instance rather than pushing it onto the shared wire options, so host and worker read behavior stays identical while only the worker's write behavior omits nulls.

Keeping the two configurations distinct is the point of the item, not an incidental detail.

Test files keep their own local options — they assert on serialization and should not depend on the type under test for their own expectations.

---

## 3.6 — Document `WorkerRequest`

`WorkerRequest` has 47 fields in a flat list with no grouping and no record of which operation reads which field. Splitting it is explicitly out of scope (listed under "Deferred / explicitly not planned" — churn exceeds value while the protocol is stable).

**Change:** group the fields with `#region` per operation family (project lifecycle, block, tag/constant, network device, catalog/tree, cross-reference, PLC control) and add a header comment mapping fields to the operations that read them.

Documentation only, no code change. If 3.3 lands first, the `ForwardedFields` table in the catalog becomes the machine-checked version of this mapping and the comment should point at it rather than restate it.

---

## Sequencing

In scope, in order:

1. **3.6** — documentation only, zero risk. Produces the field→operation inventory that 3.3 will need if it is picked up later.
2. **3.2** — small and self-contained.
3. **3.5a** — mechanical, touches five files.

Each is independent; none blocks another. The order is lowest-risk-first so the suite has been exercised before the item that touches serialization.

Deferred: **3.3**, which when picked up runs internally as characterization test green against current code → extend `BatchOperationSpec` → add `BatchRequestBuilder` → collapse the 22 wrappers → characterization test green again. Nothing in 3.6, 3.2, or 3.5a depends on it, and 3.6's field inventory makes it cheaper to start.

## Testing

- Baseline: confirm the suite is green before starting, and record the count.
- 3.6: none.
- 3.2: extend `ProjectSessionBindingTests` for `CanBind`, including an assertion that the rebind-instruction wording is identical across `TryResolve`, `Bind`, and `CanBind`.
- 3.5a: existing `WorkerResponseJsonTests` and `BatchOperationRequestJsonTests` cover the wire format; no new tests needed beyond confirming they stay green.

Every in-scope item is behavior-preserving, so a green suite before and after is the acceptance criterion — with the two intentional exceptions of the unified binding error text (3.2) and the worker's null-omission staying worker-local (3.5a).

When 3.3 is picked up it additionally needs the characterization test described in its section, plus a catalog invariant test asserting every operation's `RequiredFields` is a subset of its `ForwardedFields` — the assertion that makes the `newName` bug class unrepresentable.
