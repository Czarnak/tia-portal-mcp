# JSON Contract Normalization Roadmap

Status: Phase 0 (decision and guard) is complete. Phase 1a (worker wire normalization) and
Phase 1b (required-member enforcement) are complete. Phase 2 is implemented with offline qualification;
live V21 acceptance remains pending. The [offline validation report](../superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md)
records 4,513 passing tests, 93.94% line coverage, and two independent reviews without findings.
Phase 3 is implemented with offline qualification and an installed-V21 reference Rebuild; its
[validation record](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md)
records 4,640 passing tests and the passing 80% gate (Cobertura line rate `0.9386`). Separately
authorized [2026-10-01 lifecycle validation](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md)
passed for its frozen source. The lifecycle-tier/confirmation/binding change invalidated that
evidence for the new candidate; [Task13 on 2026-10-03](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md)
replaced it on `3bb504b`. Full/read-write/read-only functional groups passed, and the maintainer
reported no new read-only TIA dialog, completing Task13 live acceptance. Task14 current
documentation/spec updates are complete. The report bounds the maintainer's worker-lifetime
conclusion because the worker PID changed between read-write and read-only. Offline qualification at `3bb504b`
passed 4,946 tests and 93.24% linked host/contracts line coverage; this is not Siemens coverage.
Phase 4 Network alignment and guarded writes are implemented with focused offline qualification;
final combined gates and fresh live acceptance remain pending. The batch tools are retired
(`plc_write`, 2026-10-07); project-tree alignment remains future work. See the [Network plan](../superpowers/plans/2026-10-03-network-json-guarded-write.md).
On 2026-09-29 the
[write-safety redesign](../superpowers/specs/2026-09-29-write-safety-redesign-design.md) redefined
Phase 3 (lifecycle tools move onto its guarded write pipeline instead of onto canonical safety
tokens) and added the token core to Phase 4. The three batch tools were excluded from this roadmap;
`plc_read` and `plc_write` replaced them, so every registered tool is now structured. See [Scope](#scope).

## Objective

Every MCP tool this server registers returns one agent-facing JSON contract: a single canonical
document, delivered identically as the `content` text block and as `structuredContent`, described by
an advertised output schema, with typed payloads and no JSON nested inside strings.

The rules in [AGENTS.md](../../AGENTS.md) ("Structured JSON contract rules") and the seam in
[ARCHITECTURE.md §7a](../ARCHITECTURE.md#7a-the-opt-in-canonical-json-seam-and-the-network-phase-23-structured-contract)
already describe that contract. All sixteen registered tools now follow it. This roadmap moves the rest onto
it without inventing a second mechanism.

## Scope

| Tool | Current contract | Plan |
| --- | --- | --- |
| `network_read`, `network_write` | Version `1.0`, root warnings/explicit nulls; guarded write effects/batch/verification/omission | Implemented; final combined/live gates pending |
| `browse_project_tree` | Structured (canonical seam, v3 envelope) | Phase 4: align envelope members |
| `get_project_status`, `compile_check` | Structured standalone envelope (`1.0`) | Phase 2 implemented; live acceptance pending |
| `bind_project` | Structured standalone envelope (`1.0`) | Implemented; Task13 live-accepted 2026-10-03 in all three modes, with the separate human read-only dialog observation recorded |
| `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project` | Structured guarded lifecycle envelope (`1.0`) | Phase 3 implemented; current-candidate full/read-write live matrix passed 2026-10-03 |
| `plc_read`, `read_cross_references` | Structured (`1.0`, root warnings/explicit nulls; 60,000-character value and 180,000-character document budgets) | Implemented and live-accepted 2026-10-05; replace `execute_read_batch`, which was retired |
| `plc_write` | Structured guarded write envelope (`1.0`; effects, guards, typed batch and verification, omission) | Implemented and live-accepted 2026-10-07; replaces `preview_write_batch` and `apply_write_batch`, which were retired |

The batch tools were excluded because the
[write-safety redesign](../superpowers/specs/2026-09-29-write-safety-redesign-design.md) split them
into domain read/write tools in the `network_read`/`network_write` shape. `plc_read` and `plc_write`
now cover every operation they offered, so the batch pair, its legacy machinery
(`OperationBatchResult`, `OperationBatchExecutionEngine`, `OperationBatchPayloadBudget`,
`OperationBatchResultFormatter`, the `WriteSafetyService` presentation methods) and the legacy
audit record were removed, and the guard's legacy register is empty.

## Current State

One active output family remains after the batch tools were retired; two remained after Phase 3. The original findings were taken at `0862ac9`.

| Family | How the response is built | Main departures from the target |
| --- | --- | --- |
| Structured | `StructuredToolResult` over `CanonicalJson` | Standalone/lifecycle/Network/PLC tools use the target envelope; project-tree alignment remains |

The legacy batch family (`TiaJson.Presentation` anonymous objects with JSON inside strings) was
removed with the batch tools.

Below the tool surface, the host and worker also disagree about JSON:

- Null handling is declared per worker payload contract (Phase 1a). `WorkerJson.SerializePayload`
  (`TiaMcpServer.Contracts/WorkerJson.cs`) writes null members unless the payload root carries
  `[LegacyNullOmission(reason)]`. The network payload roots, `ProjectTreeBrowseResultInfo` and
  `ProjectRebindStateInfo` write explicit nulls (Phase 1b removed the network markers). No
  production payload root carries the marker any more; the batch-only roots went with the batch
  tools. Lifecycle/basic-status roots now
  write explicit nulls and decode through the worker-payload reader. The worker,
  `PersistentWorkerTransport` and the FakeWorker all render through `WorkerJson`, so IPC tests see
  the production shape.
- Required members are enforced one way (Phase 1b): the worker-payload reader in `CanonicalJson`
  (`DeserializeWorkerPayload` / `NormalizeWorkerPayload`) makes every settable member required
  unless it is declared conditional with
  `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]`. The hand-written
  member lists are gone. The conditional member register pins the current set and each appearance condition.
- The host decodes worker payloads at four strictness levels: strict `CanonicalJson` (which now
  includes the worker-payload reader), untyped
  `GetProperty` lookups, raw pass-through, and the case-insensitive transport. Phase 1a removed
  the fifth, the lenient `JsonSerializerDefaults.Web` decode of the rebind-state payload: both
  rebind-state readers now go through `ProjectRebindStatePayloadContract`.
- The legacy generic-batch token audit stream was retired. Lifecycle, Network and PLC writes use one canonical guarded
  write record per call in the separate `writes-yyyy-MM-dd.jsonl` stream, with a record discriminator,
  exact response/hash, and confirmation/guard satisfaction provenance; blocked calls and dry runs are included.

## Target Contract

This is the contract every in-scope tool ends on.

### Delivery

- The response is one `CanonicalJson` document, returned through `StructuredToolResult` as both the
  text block and `structuredContent`.
- The tool declares `UseStructuredContent = true` and an `OutputSchemaType` naming its response
  record.
- `isError` is `true` exactly when the tool rejected the call before anything ran (validation,
  access mode, binding, or a guard that blocked the write). A call that ran and partly or wholly failed is
  not a protocol error: `isError` is `false` and the envelope says what failed.

### Envelope

| Member | Type | Rule |
| --- | --- | --- |
| `tool` | string | The MCP tool name. |
| `contractVersion` | string | `major.minor` of this tool's output contract. A breaking change bumps the major. |
| `success` | boolean | `true` only when the whole call did everything requested. |
| `error` | `{ category, message }` or null | Tool-level rejection. `category` is a `WorkerFailureCategories` value. Non-null exactly when `isError` is `true`. |
| `warnings` | string array | Always present; empty when there are none. |
| `phase` | string | Only on write tools: `preview` (a `dryRun`), `applied`, `blocked` (a guard refused), or `error`, per write-safety redesign §4.7. Network now uses these guarded phases; attempted partial/verification failures remain `applied`. |
| `guards`, `effects`, `verification` | per write-safety redesign §4.7 | Only on write tools: the guards that fired, what the write did or would do per item, and the typed post-write read. |
| payload | object or null | Tool-specific and declared in the output schema: `result` for a single result, `batch` for a `StructuredOperationBatch`. Network uses `effects`, typed `batch`/`verification`, and explicit-null root `omission`. Null when `error` is set. |

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
  `CanonicalWriteSafety` and every other token binding are retired.

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
status producers use `ProjectStatusResultInfo`. Lifecycle/probe payloads retain their separate root,
which Phase 3 migrates to explicit nulls and strict required-member decoding.

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

- The six lifecycle tools use the structured contract and the guarded write pipeline:
  `dryRun` and mode-derived confirmation instead of public confirmation lists/tokens, all seven lifecycle guards, a
  typed result, and typed post-write verification instead of the `operationResult` and
  `verification.result` strings.
- Read-write requires one form elicitation per actual lifecycle call, even with only info guards
  or none, with `accept` plus boolean `confirm:true`. Unsupported capability, decline, cancel,
  timeout, or transport failure denies with `access_denied`. Full satisfies acknowledge guards by
  policy without server elicitation. Block guards stop every mode; dry runs never prompt. The old
  startup confirmation switch and agent confirmation list are removed. Post-acceptance re-resolution refuses changed
  identity or consequences instead of silently broadening approval.
- Explicit lifecycle binding preparation permits genuinely unbound open/create and retains the
  exact source/destination revision, ownership, recovery, and save-as transition checks. Ordinary
  project writes keep the default verified-binding requirement.
- One canonical audit record per call, through the pipeline's audit sink, including dry runs and
  blocked calls; audit v2 records confirmation by `user`, `policy`, or `none` and guard satisfaction
  by `user`, `policy`, or null.
- The decision reserved here earlier, where an attempted but failed operation reports its failure,
  is settled by redesign §4.7: `isError` is `true` only for rejection before anything ran
  (validation, access mode, binding, `blocked`); a write that ran and failed reports
  `success: false`, top-level `error:null`, and `isError:false`. Typed result and verification
  outcomes preserve mutation success even when verification fails; unknown outcomes need inspection
  before any retry.
- The six tools leave the guard's legacy register and gain production-surface success/rejection,
  schema, typed failure, omission, and elicitation probes. Only the three batch tools remain legacy.
- `ProjectLifecycleTools` and unused lifecycle token paths are retired here. Batch/Network token
  helpers, `SafetyRead`, and their legacy audit support stayed until Phase 4 retired them.
- The [implementation plan](../superpowers/plans/2026-09-30-json-contract-phase3-lifecycle.md) records
  the delivery and gates. The Oct1 run passed on its source; Task13's Oct3 replacement qualified
  six operations, seven guards, accepted/declined/unsupported client behavior, dry runs, persisted
  artifacts, and restoration on the new frozen candidate. The report keeps programmatic acceptance
  separate from recorded human observations. Headless/Multiuser, crash/timeout, archive retrieval,
  PLC and plant acceptance remain unqualified. Offline/FakeWorker evidence is not live acceptance.
- Removed inputs and structured outputs are breaking changes staged for the redesign's final
  major release. No release/tag or completion of Network/batch migration is claimed here.

`bind_project` adds explicit session selection in every mode with a typed standalone result,
non-null before/after binding state and in-call Portal inventory. No request implicitly opens a
project; ordinary reads never bind or switch. Project-tree cursors now reject binding changes as
`cursor_binding_mismatch`. Mode counts are 7/16/16. The
[engineering log](../IMPROVEMENT_LOG.md) records the completed human dialog observation and tracks the
`totally-integrated-claude` plugin's `tia-portal-mcp` skill migration; installed plugin files were
not changed by this documentation task.

### Phase 4: Align and Retire

- Network alignment delivered with guarded writes: version `1.0`, warnings/explicit nulls, effects and typed immediate/final verification; actual-by-default `dryRun=false`, no server elicitation. SDK/wrapper legacy/unknown-root/nonboolean rejection before entry is a normal MCP error with no audit; entered denials use a canonical root error and one audit. Aggregate encoded-ID protected-core admission retains per-ID 256, without a smaller per-ID limit. Whole omissions can report delivery failure while retaining true execution summaries; audit text/hash matches exact delivery. See the [current Network contract](../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#network_write-envelope).
- Project tree: move to `tool`, `success`, and `error` in its next major contract version.
- Delete remaining legacy pieces such as `StandaloneToolResultFormatter` and
  `WorkerCallResult.ToEnvelopeText` only after caller inventory proves they are unused.
  Lifecycle, Network and generic-batch token paths are retired.
- Token core retired with `plc_write`: `CanonicalWriteSafety` went with Network; the batch tools,
  the `WriteSafetyService` token core (including the presentation token binding), the legacy audit
  record, `WriteSafetyTooling`, and `SafetyRead` went with the PLC delivery instead of a later
  Phase 5.

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
