# JSON Contract Normalization Roadmap

Status: Phase 0 (decision and guard) is complete. Phase 1a (worker wire normalization) and
Phase 1b (required-member enforcement) are complete; Phases 2-4 are not started. The three batch tools are excluded from this
roadmap; see [Scope](#scope).

## Objective

Every MCP tool this server registers returns one agent-facing JSON contract: a single canonical
document, delivered identically as the `content` text block and as `structuredContent`, described by
an advertised output schema, with typed payloads and no JSON nested inside strings.

The rules in [AGENTS.md](../../AGENTS.md) ("Structured JSON contract rules") and the seam in
[ARCHITECTURE.md §7a](../ARCHITECTURE.md#7a-the-opt-in-canonical-json-seam-and-the-network-phase-23-structured-contract)
already describe that contract. Today only three tools follow it. This roadmap moves the rest onto
it without inventing a second mechanism.

## Scope

| Tool | Current contract | Plan |
| --- | --- | --- |
| `network_read`, `network_write` | Structured (canonical seam) | Phase 4: align envelope members |
| `browse_project_tree` | Structured (canonical seam, v3 envelope) | Phase 4: align envelope members |
| `get_project_status`, `compile_check` | Legacy worker envelope | Phase 2 |
| `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project` | Legacy lifecycle preview/apply text | Phase 3 |
| `execute_read_batch`, `preview_write_batch`, `apply_write_batch` | Legacy batch text | **Excluded** |

The batch tools are excluded because a separate redesign of them is planned. This roadmap does not
constrain that redesign. Until it lands, they keep their current output, and the legacy machinery
they depend on stays in place: `OperationBatchResult`, `OperationBatchExecutionEngine`,
`OperationBatchPayloadBudget`, `OperationBatchResultFormatter`, the presentation-serializer methods
on `WriteSafetyService` (`CreatePreview`, `ValidateEnvelope`, `ValidateAndConsume`, `AppendAudit`),
and the legacy audit record. If that redesign lands on the structured contract, the batch tools
leave the guard's legacy register like any other migrated tool.

## Current State

Four output families exist today. The findings behind this table were taken at `0862ac9`.

| Family | How the response is built | Main departures from the target |
| --- | --- | --- |
| Structured | `StructuredToolResult` over `CanonicalJson` | Two envelope dialects (below) |
| Batch (excluded) | `TiaJson.Presentation` anonymous objects | Item `result` is a string holding JSON, raw source text, `Error: …` prose, an omission marker, or JSON cut at a character limit |
| Worker envelope | `WorkerCallResult.ToEnvelopeText()` | `payload` is a JSON string; oversized payloads are cut mid-document by `StandaloneToolResultFormatter` |
| Lifecycle | `WriteSafetyService.CreatePreview` and `WriteSafetyTooling.BuildApplyResult` | `toolName` instead of `tool`; `operationResult` and `verification.result` are JSON strings; a failed preview is rendered in the apply-result shape |

Every legacy tool returns a plain string, so the SDK never sets `isError` or `structuredContent`
for them: a handled failure is a successful MCP call carrying `success: false` in text.

Below the tool surface, the host and worker also disagree about JSON:

- Null handling is declared per worker payload contract (Phase 1a). `WorkerJson.SerializePayload`
  (`TiaMcpServer.Contracts/WorkerJson.cs`) writes null members unless the payload root carries
  `[LegacyNullOmission(reason)]`. The network payload roots, `ProjectTreeBrowseResultInfo` and
  `ProjectRebindStateInfo` write explicit nulls (Phase 1b removed the network markers). The
  remaining marked roots carry one of two reasons: `BatchRedesign` (consumed only by the batch
  tools) or `ToolMigration` (returned by a tool still on the legacy contract). The worker,
  `PersistentWorkerTransport` and the FakeWorker all render through `WorkerJson`, so IPC tests see
  the production shape.
- Required members are enforced one way (Phase 1b): the worker-payload reader in `CanonicalJson`
  (`DeserializeWorkerPayload` / `NormalizeWorkerPayload`) makes every settable member required
  unless it is declared conditional with `[JsonIgnore(WhenWritingNull)]`. The hand-written
  member lists are gone. Four conditional members exist, pinned by `ConditionalMemberRegisterTests`.
- The host decodes worker payloads at four strictness levels: strict `CanonicalJson`, untyped
  `GetProperty` lookups, raw pass-through, and the case-insensitive transport. Phase 1a removed
  the fifth, the lenient `JsonSerializerDefaults.Web` decode of the rebind-state payload: both
  rebind-state readers now go through `ProjectRebindStatePayloadContract`.
- The audit JSONL holds two record shapes in the same file (`resultPreview` string versus `result`
  object), with no record discriminator.

## Target Contract

This is the contract every in-scope tool ends on.

### Delivery

- The response is one `CanonicalJson` document, returned through `StructuredToolResult` as both the
  text block and `structuredContent`.
- The tool declares `UseStructuredContent = true` and an `OutputSchemaType` naming its response
  record.
- `isError` is `true` exactly when the tool rejected the call before anything ran (validation,
  access mode, safety token, binding). A call that ran and partly or wholly failed is not a
  protocol error: `isError` is `false` and the envelope says what failed.

### Envelope

| Member | Type | Rule |
| --- | --- | --- |
| `tool` | string | The MCP tool name. |
| `contractVersion` | string | `major.minor` of this tool's output contract. A breaking change bumps the major. |
| `success` | boolean | `true` only when the whole call did everything requested. |
| `error` | `{ category, message }` or null | Tool-level rejection. `category` is a `WorkerFailureCategories` value. Non-null exactly when `isError` is `true`. |
| `warnings` | string array | Always present; empty when there are none. |
| `phase` | string | Only on tools with a preview/apply flow: `preview`, `apply`, or `error`. |
| payload | object or null | Tool-specific and declared in the output schema: `result` for a single result, `batch` for a `StructuredOperationBatch`, `preview` for a typed preview. Null when `error` is set. |

Per-operation items keep the existing `StructuredOperationItem` shape: `operationId`, `operation`,
`status`, `result`, `failure { category, message }`, `omission`, `skipReason`, `warnings`.

### Values

- **No JSON inside strings.** Every result is a real object or array.
- **Typed worker payloads.** Each operation declares exactly one CLR result type. A payload that
  does not decode as that type becomes a `protocol_error` failure and is never echoed back.
- **Explicit nulls.** A member in the contract version is always present; `null` means "no value".
  The one exception is a member that appears only when the caller requested it or when it
  applies, and is otherwise absent so that existing wire shapes stay unchanged. Today those are
  `ioDetails`, `pagination`, `pagination.nextCursor`, and an omission's `subject` and
  `subject.identifier`. Each such member's schema description must say when it appears.
- **Never cut a JSON value to fit a budget.** Omit it whole with an omission record and report the
  truncation, as `StructuredOperationBatchPayloadBudget` already does.
- **Canonical safety binding.** Structured write tools bind tokens and write audit records through
  `CanonicalWriteSafety` (`CreateCanonicalPreview`, `ValidateAndConsumeCanonical`,
  `AppendCanonicalAudit`).

### Where the existing structured tools deviate

| Member | `network_read` | `network_write` | `browse_project_tree` |
| --- | --- | --- | --- |
| `tool` | present | present | missing |
| `contractVersion` | missing | missing | present (`3.0`) |
| `success` | present | present | `status` (`succeeded` / `failed`) instead |
| `error` | present | present | `failure` instead |
| `warnings` | missing (per item only) | missing (per item only) | present |

For Network the fix is additive. For the project tree it is a breaking change, so it waits for the
tree's next major contract version.

## Delivery Phases

### Phase 0: Decision and Guard — Complete

- This roadmap, recording the target contract and the batch exclusion.
- A conformance guard, `TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`, run
  through the real MCP protocol harness:
  - Every tool on the production read-write surface either advertises an output schema or is
    listed in the guard's legacy register with a reason. A migrated tool that stays listed, a
    listed tool that no longer exists, and a new unlisted legacy tool all fail the guard.
  - Every structured tool has a success probe and a rejection probe. Each probe checks one
    canonical document in both representations, no JSON inside strings
    (`StructuredContractInspector`), and the expected `isError`.
- Corrections to documentation that no longer matched the code (`TiaJson.cs`,
  `ARCHITECTURE.md` §7a, `AGENTS.md`, `DEVICES_OPERATIONS_SUMMARY.md`).

No tool output changed.

### Phase 1: Worker Wire Normalization

Not visible to clients unless noted. Decisions (2026-09-28):

- Payload types that only the batch tools consume keep omitting nulls until the batch redesign:
  the batch tools forward raw worker payload text, so explicit nulls would change their output.
- Payload types returned by `get_project_status`, `compile_check` and the lifecycle tools keep
  omitting nulls until those tools migrate (Phases 2-3), so Phase 1 stays invisible to clients.
- Phase 1 ships as two pull requests, 1a and 1b.
- Host requests keep writing null members, so 1a changes no request byte.
- The `inspect_network_object` null-value defect found during 1a is fixed in 1a.

#### Phase 1a: Worker Wire Normalization — Complete

- One wire definition, `TiaMcpServer.Contracts/WorkerJson.cs`: `Envelope` for reading both
  directions and writing responses, `Request` for writing requests (null members kept), and
  `SerializePayload`, which writes null members unless the payload root carries
  `[LegacyNullOmission(reason)]`. The worker, `PersistentWorkerTransport` and the FakeWorker use
  it; the worker's hard-coded per-type switch and every copied options block in the tests are
  gone.
- Every payload root that omitted nulls before carries the marker with its reason, and
  `WorkerPayloadNullPolicyRegisterTests` pins the marked set. The register only shrinks.
- The FakeWorker renders its DTO fixtures through the production policy, so IPC tests no longer
  see explicit nulls that the real worker omits (`FakeWorkerWireParityTests`).
- `probe_open_project_rebind` payloads decode through one strict typed contract,
  `ProjectRebindStatePayloadContract`, which replaced an untyped member check in
  `OpennessWorkerClient` and a lenient `JsonSerializerDefaults.Web` decode in `ProjectWriteTools`.

No tool schema and no host request byte changed. The one real-worker byte change is a bug fix
that the FakeWorker parity exposed: the worker omitted `attributes[].value.value` for a
successfully read CLR null (kind `null`), which the host's inspection contract requires, so
`inspect_network_object` failed with `protocol_error` for any object with a null attribute.
`NetworkAttributeValueInfo.Value` is now always written.

Known divergence left for Phase 2: the FakeWorker `status-with-metadata` fixture returns a bare
`ProjectStatusInfo`, while the real worker wraps it in `ProjectLifecycleResultInfo`.

#### Phase 1b: Required-Member Enforcement — Complete

- One worker-payload reader in `TiaMcpServer/Json/CanonicalJson.cs`: `DeserializeWorkerPayload<T>`
  and `NormalizeWorkerPayload<T>`. It uses the strict read settings plus `RespectNullableAnnotations`
  and a type-info modifier that marks every settable member required, except members declared
  `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. It refuses a root type (or an
  array root's element type) carrying `[LegacyNullOmission]`, because such a payload omits nulls.
  It does not check collection elements, dictionary values or generic arguments, so the typed
  validators keep their null-element checks.
- The network, hardware-page, project-tree and rebind-state decoders read through it. The
  hand-written JSON-shape layers (`RequireMembers`, `ValidateRequiredJsonShape` and friends) are
  deleted; the typed validators keep only semantic rules and null-element checks. The contracts
  stay classes: nothing was converted to records.
- The network `[LegacyNullOmission]` markers are removed in the same change, so the network
  payloads write explicit nulls together with the decoders that read them. The reasons went from
  three to two (`RequiredMemberEnforcement` is gone).
- The conditional members are `DeviceItemInfo.IoDetails`, `HardwareConfigInfo.Pagination`,
  `HardwarePaginationInfo.NextCursor` and `WorkerResponse.BlockImportOutcome`, each described by
  when it appears and pinned by `ConditionalMemberRegisterTests`.
- Rejection is unchanged: a rejected payload is `protocol_error` with the contract's fixed message
  and is never echoed.
- The batch safety snapshots and the cursor decode stay on `CanonicalJson.Deserialize`. Other
  host decodes of worker payloads move to the reader with the tools that consume them (Phases 2-3).

### Phase 2: `get_project_status` and `compile_check`

- Typed results (`ProjectMetadataInfo`, `CompileCheckReport`) decoded through the strict gate.
- Replace the substring truncation in `StandaloneToolResultFormatter` with a whole-value omission.
- Adopt the target envelope and add both tools' probes to the guard.

### Phase 3: Project Lifecycle Tools

- Typed preview through `CreateCanonicalPreview`; typed apply result and typed post-write
  verification instead of `operationResult` and `verification.result` strings.
- Canonical token binding and audit records.
- Decision reserved for Phase 3 design: where a single operation that was attempted but failed
  reports its failure, keeping "rejected before anything ran" (`isError: true`) distinct from
  "ran and failed".

### Phase 4: Align and Retire

- Network: add `contractVersion` and top-level `warnings` (additive).
- Project tree: move to `tool`, `success`, and `error` in its next major contract version.
- Delete legacy pieces that only in-scope tools used: `StandaloneToolResultFormatter`,
  `WorkerCallResult.ToEnvelopeText`, and the lifecycle paths through
  `WriteSafetyTooling.BuildApplyResult` and `WriteSafetyTooling.CreatePreview`. The presentation
  token binding and the legacy audit record stay while the batch tools use them.

Phases 2-4 change what clients receive. `README.md` is also the NuGet readme, so each of those
phases updates it and its release notes.

## Later: Compile-Time Completeness

Not scheduled. The reader enforces required members at decode time; the next step would make a
missing member a compile error where a contract is constructed.

- Adopt C# `required init` members (not positional records), per type, once every decode path of
  that type writes explicit nulls, together with test-data builders for the types that tests
  construct by hand.
- Required-ness belongs to a decode path's null policy, not to a CLR type. `CompileCheckReport` is
  both the `compile_check` root and nested in `BlockImportOutcomeInfo` inside the null-omitting
  worker envelope (`update_block_logic`), so a type-level `required` would couple Phase 2 to the
  batch redesign.
- Unverified: whether System.Text.Json recognizes the netstandard2.0 `RequiredMemberAttribute`
  polyfill that `TiaMcpServer.Contracts` would need. Check it before committing to this direction.

## Migrating a Tool

1. Add the typed response record and declare it with `OutputSchemaType`.
2. Return it through `StructuredToolResult`.
3. Remove the tool from `LegacyTextContractTools` in the conformance guard.
4. Add a success probe and a rejection probe to `StructuredToolProbes`.
5. Update the tool's operation reference under `docs/SupportedOperations/` and `README.md`.
