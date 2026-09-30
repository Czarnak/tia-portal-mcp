# JSON Contract Normalization Roadmap

Status: Phase 0 (decision and guard) is complete. Phase 1a (worker wire normalization) and
Phase 1b (required-member enforcement) are complete. Phase 2 is implemented with offline qualification;
live V21 acceptance remains pending. Phases 3-4 are not started. On 2026-09-29 the
[write-safety redesign](../superpowers/specs/2026-09-29-write-safety-redesign-design.md) redefined
Phase 3 (lifecycle tools move onto its guarded write pipeline instead of onto canonical safety
tokens) and added the token core to Phase 4. The three batch tools are excluded from this roadmap;
the redesign's Phase 4 resolves that exclusion. See [Scope](#scope).

## Objective

Every MCP tool this server registers returns one agent-facing JSON contract: a single canonical
document, delivered identically as the `content` text block and as `structuredContent`, described by
an advertised output schema, with typed payloads and no JSON nested inside strings.

The rules in [AGENTS.md](../../AGENTS.md) ("Structured JSON contract rules") and the seam in
[ARCHITECTURE.md §7a](../ARCHITECTURE.md#7a-the-opt-in-canonical-json-seam-and-the-network-phase-23-structured-contract)
already describe that contract. Five tools now follow it. This roadmap moves the rest onto
it without inventing a second mechanism.

## Scope

| Tool | Current contract | Plan |
| --- | --- | --- |
| `network_read`, `network_write` | Structured (canonical seam) | Phase 4: align envelope members |
| `browse_project_tree` | Structured (canonical seam, v3 envelope) | Phase 4: align envelope members |
| `get_project_status`, `compile_check` | Structured standalone envelope (`1.0`) | Phase 2 implemented; live acceptance pending |
| `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project` | Legacy lifecycle preview/apply text | Phase 3 (delivered as write-safety redesign Phase 2) |
| `execute_read_batch`, `preview_write_batch`, `apply_write_batch` | Legacy batch text | **Excluded**; retired by write-safety redesign Phase 4 |

The batch tools are excluded because a separate redesign splits them into domain read/write tools
(`block_read`, `tag_write`, and so on) in the `network_read`/`network_write` shape. The
[write-safety redesign](../superpowers/specs/2026-09-29-write-safety-redesign-design.md) sets what
those write tools must do (its §4.9) and, in its Phase 4, retires the batch pair once the domain
tools cover every operation it offers. This roadmap does not otherwise constrain that redesign.
Until it lands, the batch tools keep their current output, and the legacy machinery they depend on
stays in place: `OperationBatchResult`, `OperationBatchExecutionEngine`,
`OperationBatchPayloadBudget`, `OperationBatchResultFormatter`, the presentation-serializer methods
on `WriteSafetyService` (`CreatePreview`, `ValidateEnvelope`, `ValidateAndConsume`, `AppendAudit`),
and the legacy audit record. The domain tools are built on the structured contract, and the batch
tools leave the guard's legacy register when they are retired.

## Current State

Three active output families remain after Phase 2. The original findings were taken at `0862ac9`.

| Family | How the response is built | Main departures from the target |
| --- | --- | --- |
| Structured | `StructuredToolResult` over `CanonicalJson` | Standalone tools use the target envelope; Network/tree departures are listed below |
| Batch (excluded) | `TiaJson.Presentation` anonymous objects | Item `result` is a string holding JSON, raw source text, `Error: …` prose, an omission marker, or JSON cut at a character limit |
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
  unless it is declared conditional with
  `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. The hand-written
  member lists are gone. Four conditional members exist, pinned by `ConditionalMemberRegisterTests`.
- The host decodes worker payloads at four strictness levels: strict `CanonicalJson` (which now
  includes the worker-payload reader), untyped
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
  access mode, binding, or a guard that blocked the write). Until a tool leaves the token flow, a
  rejected safety token is also such a rejection. A call that ran and partly or wholly failed is
  not a protocol error: `isError` is `false` and the envelope says what failed.

### Envelope

| Member | Type | Rule |
| --- | --- | --- |
| `tool` | string | The MCP tool name. |
| `contractVersion` | string | `major.minor` of this tool's output contract. A breaking change bumps the major. |
| `success` | boolean | `true` only when the whole call did everything requested. |
| `error` | `{ category, message }` or null | Tool-level rejection. `category` is a `WorkerFailureCategories` value. Non-null exactly when `isError` is `true`. |
| `warnings` | string array | Always present; empty when there are none. |
| `phase` | string | Only on write tools: `preview` (a `dryRun`), `applied`, `blocked` (a guard refused), or `error`, per write-safety redesign §4.7. `network_write` reports `preview`, `apply`, or `error` until it moves onto the guarded pipeline. |
| `guards`, `effects`, `verification` | per write-safety redesign §4.7 | Only on write tools: the guards that fired, what the write did or would do per item, and the typed post-write read. |
| payload | object or null | Tool-specific and declared in the output schema: `result` for a single result, `batch` for a `StructuredOperationBatch`. `network_write` also carries `preview` until the guarded pipeline replaces it with `effects`. Null when `error` is set. |

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
- **Guarded writes, no tokens.** Structured write tools run through the guarded write pipeline and
  write one canonical audit record through its audit sink (write-safety redesign §4.1 and §4.8).
  `network_write` binds tokens through `CanonicalWriteSafety` (`CreateCanonicalPreview`,
  `ValidateAndConsumeCanonical`, `AppendCanonicalAudit`) until the redesign's Phase 3 removes them;
  no other tool adopts that binding.

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

The earlier FakeWorker `status-with-metadata` root divergence is resolved in Phase 2: both direct
status producers now use `ProjectStatusResultInfo`, while lifecycle/probe payloads keep their old root.

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
- Rejection is unchanged, with one documented exception: an explicit `"ioDetails": null` on a read
  that did not request IO details is now accepted (previously any member named `ioDetails` there
  was rejected). A rejected payload is `protocol_error` with the contract's fixed message and is
  never echoed.
- The batch safety snapshots and the cursor decode stay on `CanonicalJson.Deserialize`. Other
  host decodes of worker payloads move to the reader with the tools that consume them (Phases 2-3).

### Phase 2: `get_project_status` and `compile_check` — Implemented, Live Acceptance Pending

- Strict worker roots are `ProjectStatusResultInfo` and `CompileCheckReport`; public values are the full `ProjectStatusInfo` (including `ProjectMetadataInfo`) and compiler report.
- Concrete output schemas use `tool`, `contractVersion: "1.0"`, `success`, rejection-only `error`, `warnings`, and `result: { status, value, failure, omission }` with explicit nulls.
- Whole-value omission replaces substring truncation on these tools: 60,000 canonical value characters and 180,000 document characters, with measured omission metadata and retry guidance. The legacy formatter is retained until Phase 4.
- Compilation success means compilation passed; compiler errors retain typed diagnostics with `success:false`, `error:null`, and `isError:false`. Attempted worker/protocol/identity failures use `result.failure`; pre-operation rejection sets top-level error and `isError:true`.
- Both tools leave the legacy register, with success/rejection and malformed/error/omission probes through the real MCP SDK. Inputs, access ceilings, and binding behavior remain; compile authorization runs before binding reads.
- The [implementation plan](../superpowers/plans/2026-09-30-json-contract-phase2-standalone-tools.md) records execution. Installed-V21 producer compatibility must be qualified on a separately authorized disposable fixture before claiming live acceptance. No package release/tag is made during the write-safety transition.

### Phase 3: Lifecycle Onto the Guarded Write Pipeline

Redefined 2026-09-29 and delivered as Phase 2 of the
[write-safety redesign](../superpowers/specs/2026-09-29-write-safety-redesign-design.md). The
earlier plan (typed preview through `CreateCanonicalPreview`, canonical token binding) would have
built lifecycle tokens only for the redesign to delete them.

- The six lifecycle tools move onto the structured contract and the guarded write pipeline:
  `dryRun` and `acknowledge` instead of `confirm` and `safetyToken`, the six lifecycle guards, a
  typed result, and typed post-write verification instead of the `operationResult` and
  `verification.result` strings.
- One canonical audit record per call, through the pipeline's audit sink.
- The decision reserved here earlier, where an attempted but failed operation reports its failure,
  is settled by redesign §4.7: `isError` is `true` only for rejection before anything ran
  (validation, access mode, binding, `blocked`); a write that ran and failed reports
  `success: false` with `isError: false`.
- The six tools leave the guard's legacy register and gain success and rejection probes.

### Phase 4: Align and Retire

- Network: add `contractVersion` and top-level `warnings` (additive).
- Project tree: move to `tool`, `success`, and `error` in its next major contract version.
- Delete legacy pieces that only in-scope tools used: `StandaloneToolResultFormatter`,
  `WorkerCallResult.ToEnvelopeText`, and the lifecycle paths through
  `WriteSafetyTooling.BuildApplyResult` and `WriteSafetyTooling.CreatePreview`.
- Retire the token core with the write-safety redesign: `CanonicalWriteSafety` goes in its Phase 3
  (network); the batch token paths go in its Phase 4, when the batch tools are retired; the
  `WriteSafetyService` token core (including the presentation token binding), the legacy audit
  record, `WriteSafetyTooling`, and `SafetyRead` go in its Phase 5.

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
