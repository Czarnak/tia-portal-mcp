# PLC writes: reusable previews and mandatory execution checks

Status: superseded on 2026-11-28 by the [all-mutation-tools design](2026-11-28-write-preview-execution-design.md). Retained for review history; do not implement this PLC-only scope or its single-use retirement policy.
Date: 2026-11-12
Branch: `feat/plc-write-preview-execution`
Plan: [Implementation plan](../plans/2026-11-12-plc-write-preview-execution.md)

## Problem

`plc_write` currently plans every apply against initial state, and dependent items can trigger further planning. This duplicates preview work, makes descriptive inventory a prerequisite for execution, and can reject a later item before earlier independent work executes. After preview, the caller must resend the entire operation array, including large source/XML content. The library domain already demonstrates apply without a descriptive plan, but PLC verification currently depends on planned effects. PLC also has native hazards that must remain protected: silently accepted singleton OB duplicates, silently ignored tag renames, and library-instance imports that can open a modal dialog or silently remove linkage.

## Users

- MCP agents need an explicit, short reference for executing the complete request they previewed.
- Maintainers need one PLC PR with reusable host infrastructure, qualified native protections, and unchanged behavior for other tools.
- Operators need execution results that describe actual attempts, including partial writes, without mistaking preview evidence for current TIA state.

## In scope

- [S-1] Change only `plc_write` to contract `3.0`: preview plans; both full-input apply and reference apply execute without invoking the PLC planner's plan or replan methods.
- [S-2] Add shared, opt-in host infrastructure for immutable preview requests, bounded storage, exact binding checks, and single-use `previewId` application. Adopt it only in PLC in this PR.
- [S-3] Add closed MCP input/schema validation, conditional preview receipt fields, canonical response budgeting, and audit correlation for the two PLC input forms.
- [S-4] Preserve mandatory execution checks, ordered dependencies, native refusal authority, partial-failure behavior, and verification independently of preview effects.
- [S-5] Add a typed library-instance guard shared by PLC preview and worker mutation paths, including protected group descendants.
- [S-6] Add offline/protocol/installed-reference qualification and a bounded live-acceptance definition; correct the singleton evidence discrepancy in durable docs.

## Out of scope

- Migrating Network, HMI, VCI, library, or project lifecycle tools; their defaults, schemas, contracts and confirmation behavior stay unchanged.
- New public MCP tools, argument-free latest-preview application, editing a cached request, selecting a subset, automatic retries, rollback, persistence of preview requests, or recovery across host restarts.
- Treating `previewId` as permission, human approval, a safety token, an idempotency key, or a guarantee that preview effects still match live state.
- Fixing every existing native/property edge case or expanding the retained native collision claims to untested namespaces.
- Performing implementation or live engineering as part of preparing this spec and plan. Future implementation stays on the branch above; remote writes require authorization under repository rules.

## Constraints

- Keep the .NET 10 host / .NET Framework 4.8 worker split; all Siemens calls run in the worker.
- Reuse `Safety/Pipeline`, canonical structured results, the verified binding lease, operation/capability catalogs and audit v2. No parallel write pipeline or token-bound surface.
- A preview never mutates TIA. Apply never runs the descriptive PLC planner, computes content diffs, or simulates the complete batch.
- Mandatory identity, concurrency, crash-prevention and silent-failure protections run against current state beside the affected native action; a prior preview cannot waive them.
- Keep 1-50 ordered unique-ID operations, the 256-character ID limit, stop at first failure, no batch rollback and no automatic replay.
- Keep `plc_read` at `1.0`, read-write/full eligibility, zero PLC elicitation, and standalone/local-session eligibility through `ProjectBase`.
- Use effective `MaxItemChars` / `MaxDocumentChars`; canonical text, structured content and the audited delivered document must remain identical.
- Preserve unrelated work. Other tools retain their current contracts and planning hooks until their separate PRs.

## Interfaces

### Public input and version

`plc_write` accepts exactly one source of operations:

```json
{"operations":[{"operationId":"table","operation":"create_tag_table","plcName":"PLC_1","tableName":"Example"}],"dryRun":true}
```

```json
{"previewId":"wp1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}
```

Full-input `dryRun` remains optional and defaults to `false`. Reference input permits omitted/false `dryRun` only. `operations` and `previewId` are mutually exclusive, even when either is explicitly null. Missing sources, explicit nulls, invalid member types, duplicate/wrongly cased/unknown members and legacy `confirm`/`acknowledge` arguments return canonical `validation_error` with MCP `isError:true`, no worker activity and no audit entry, following the existing argument-shape boundary. The advertised closed schema expresses the same alternatives with root `oneOf`; injected services are absent from it. Array-count, operation-ID and operation-semantic validation remains in the shared entered pipeline and is audited as today.

IDs are case-sensitive opaque strings: `wp1_` plus the 43 unpadded base64url characters encoding 32 cryptographically random bytes. The example above illustrates shape only. The server never reconstructs an ID from request content. Full-input arrays remain supported without a preview.

All PLC write responses, including rejection responses, declare `contractVersion:"3.0"`. The major change covers apply-time failure timing and empty planned effects as well as the new input form. The 14 operation payloads and default formats remain otherwise unchanged.

### Preview receipt

A successful, complete preview adds these conditional root properties:

```json
{"previewId":"wp1_AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","previewExpiresAt":"2026-11-12T22:10:00Z"}
```

They occur together, only on `phase:"preview"`, `success:true`, with no block guard, root error, or omission at any level. Failed, blocked, truncated or diagnostically compacted previews publish neither property and leave no usable reference. Preview may retain the current convention of listing block guards under `phase:"preview"`; such a document is never executable by ID. Apply responses have `effects:[]`, do not echo old effects, and publish no new receipt. An apply through ID executes the exact stored operations, including original IDs, selectors, content, expected hashes, explicit values and order.

### Shared host integration

- Extend the existing shared call/execution/report seams with optional preview-reference state and a domain opt-in that defaults off. Existing callers must preserve their behavior.
- Resolve a saved request before constructing `WriteRun`, whose arrays are sized from the restored item count. Cache lookup failures have an explicit canonical entered-call rejection path, not a misleading empty-operations error.
- Add one shared argument-source validator/decorator usable by later tools; attach it only to PLC. It must inspect raw argument presence before SDK binding loses null/absence distinctions.
- Inject the store through `WriteExecution`, not as an accidental public MCP argument. Use the same store instance for the host/MCP session.
- Let the opted-in domain expose whether its composed, budgeted response is a complete publishable preview. Report success alone is insufficient because `PlcWritePayloadBudget` can change delivered success.
- Add optional `previewId` correlation to audit v2 for preview publication and reference application. Continue recording actual restored operation IDs/statuses and the exact delivered canonical document; do not log another copy of source/XML content.

### Internal PLC check and execution evidence

Add one bound, read-only worker method, `inspect_plc_write_target`, for preview inspection of `update_block_logic`, `update_type_content`, `delete_block` and `delete_block_group`. It accepts a closed operation selector plus the existing block/type path fields, resolves through the existing worker PLC resolvers under `WithEngineeringRoot`, and returns a typed `PlcWriteGuardInfo`. Register it as `Observe` and map it through the shared project-capability catalog to `plc_write`. It is not a public tool and never opens, adopts or changes a project.

Use the canonical worker-payload reader for required members and explicit nulls. The worker-native inspector is also called directly by mutation handlers. A preview of a target created earlier in its simulated request describes the check as deferred until execution, rather than querying a nonexistent live object. This exception never skips the actual worker check.

PLC verification must obtain target identity from execution evidence rather than the planner's `_effects`. Preserve device/PLC alias resolution, folder/type/block identity and rename identities. Add conditional worker-envelope evidence `plcWriteExecution` of typed `PlcWriteExecutionInfo`, with required members `target`, `mutationStarted` and `guard`. `target` is nullable typed canonical device/PLC/folder/block/type identity; `guard` is nullable `PlcWriteGuardInfo`. Reuse the already-resolved locator result; do not add an inventory or identity RPC on apply. Capture identity after resolution, and stamp `mutationStarted:true` immediately before the first native action that can change that target: setter/create/delete/import/source generation. Resolved identity alone never proves mutation began. Temporary external-source registration/cleanup keeps its separate existing import-outcome evidence and is not itself a target-mutation stamp. A valid protected-target refusal has `mutationStarted:false` and its typed guard; preserve any preparation/cleanup outcome rather than claiming that no TIA state at all was touched.

Strictly validate raw evidence before the permissive worker-envelope reader. Unknown, duplicate, wrongly cased, missing or incorrectly typed members, an impossible success/guard combination, or a successful mutation without required identity are `protocol_error`; never echo the rejected evidence. Add a PLC execution scope/decorator to preserve evidence across existing worker wraps without changing other domains' semantics. A successful PLC mutation requires a valid execution envelope; failure may lack it after transport/Portal loss.

Retain shared trusted/content-hash/superseded verification, but allow the verification policy to distinguish non-mutating refusals from possible mutations. Valid `mutationStarted:false` and host `NotSent` refusals cannot supersede earlier writes. Valid started evidence uses its actual target keys. Missing evidence after a possibly sent request means unknown, never false. In that case conservatively replace verification of earlier successful items in the call with `check:"not_verified"`, `success:false` and inspect-before-retry guidance; preserve their actual succeeded execution statuses. Do not fabricate target identity or claim a definitive superseding operation. This deliberate conservative fallback is PLC-only. Add no entry for the failed non-content item itself. Skip unsafe read-back after binding/Portal loss. Exact envelope integration and tests are assigned in the plan.

## Data

### Ownership, limits and lifetime

The host stores only an immutable canonical UTF-8 snapshot of the validated ordered request and its tool/request type, binding snapshot and private session owner. No plan, native object, resolved target handle, diff, domain instance or confirmation is stored. Deserialize a fresh request when resolving a reference; callers cannot mutate the stored bytes. Preserve source/XML text exactly.

Use `TimeProvider`, a 10-minute absolute lifetime from candidate receipt preparation during publication, at most 16 available-or-reserved entries and 16 MiB (16,777,216 bytes) of combined stored/reserved request bytes per host session. Capture one monotonic start timestamp and its UTC display timestamp together; `previewExpiresAt` and store expiration derive from that same origin. The lifetime does not restart after compose, audit, activation or reply delivery. Neither lookup nor apply renews it. Expired entries can be removed on normal store access. Do not silently evict an unexpired request. Capacity failure is canonical `preview_store_full`, leaves no receipt and does not restrict full-input applies. No new CLI/environment configuration is introduced in this PR.

The store can reserve quota and snapshot the validated input before asynchronous preview work. Pending reservations are never retrievable and are released on every denial, cancellation or failed publication. Publication attaches the binding actually pinned by the lease. It uses `ProjectBindingSnapshot.SameBinding`, including worker session/generation and Portal PID as well as binding ID/revision/path/state. A host restart loses the entire store. A worker restart, rebind or invalidated attachment makes the old reference unusable; there is no path-only fallback.

### Publication sequence

1. Validate the request; reserve slot/bytes and take the immutable snapshot used by preview.
2. Enter the normal binding/capability gates and lease; run the descriptive planner and mandatory preview inspections.
3. At finish, prepare candidate ID/expiry metadata under the lease; compose and budget the complete candidate response with that metadata included in size calculations.
4. If the budgeted response is blocked, incomplete or unsuccessful, discard the reservation and remove both receipt fields from that already-budgeted response. Preserve its failure/omission; do not rebuild an unbudgeted original that could become a complete `success:true` response without a receipt. In particular, a document that fits only after receipt removal remains a non-publishable budget failure.
5. For a complete candidate, atomically recheck expiration/cancellation and activate inside the lease before the single final canonical serialization and audit attempt. Capacity cannot fail because quota was reserved. If already expired, release it and compose canonical `preview_unavailable` without receipt before serialization. Release a pending reservation on every pre-activation exception; if final serialization fails after activation, remove that not-delivered entry as cleanup.
6. Serialize the final canonical document once for both MCP representations, attempt the existing audit append, then return before releasing the lease. Never mutate the response after serialization/audit to change receipt fields.

Audit remains best effort as its current sink contract requires; activation does not claim proof of disk persistence or client receipt. Audit/transport delay can consume the remaining lifetime, just as delayed delivery can for any expiring reference. A response lost in transport can leave an unknown-to-client entry until expiry; the server does not resend or apply it automatically.

### Apply and consumption sequence

1. Validate the exclusive source shape, then look up/decode the saved request without consuming it.
2. Re-run its ordinary operation/access/admission validation, verified binding/capability gates and lease. Never trust a previous access decision.
3. Under the pinned lease, atomically compare exact binding/session/tool/type and current expiration, then remove/claim the entry immediately before beginning the ordered execution loop. A prior lookup is not a claim.
4. After claim, every outcome consumes the ID: cancellation, mandatory-check rejection, native refusal, timeout, Portal loss, partial completion, verification failure and reply loss. There is no automatic reinstatement or replay. Duplicates fail before any mutation dispatch.
5. Rejections before claim retain an otherwise valid entry. Invalidated bindings remain invalid even if the same project is rebound later. Ordinary TIA changes do not automatically invalidate a reference; per-item live checks determine whether its request can run.

Remove entries on claim; no replay-result cache or unbounded consumed-ID tombstones. Unknown, expired, consumed, pending, wrong-session, wrong-tool or wrong-request-type IDs return `preview_unavailable` with guidance to inspect state and supply a new request/preview. A known exact-binding mismatch returns `binding_conflict`. Neither category authorizes automatic retry.

## Mandatory execution checks

| Check | Preview | Apply |
| --- | --- | --- |
| Shape, access/capability, exact binding and target uniqueness | Existing validation/gates | Always enforce; no implicit binding or opening |
| Singleton OB multiplicity and free number | Descriptive inventory/guard | Keep the worker's fresh `PickObNumber` check before import, including an earlier OB created in the same call |
| Content hash, supported format, single-object declaration and declared target identity | Show/check current evidence | Keep fresh export/hash and existing strict import/declaration checks before mutation |
| Library instance replacement/deletion | Shared typed inspector | Run the same inspector against the live target immediately beside native mutation |
| Ignored `update_tag` rename | Existing behavior | Preserve rename-first/read-name-back before other setters; mismatch is `postcondition_failed` |
| Import postconditions, source-node cleanup and content re-export | No mutation | Keep existing required checks and report uncertainty truthfully |
| Delete counts/details and address overlap | Keep four informational guards | Do not run descriptive inventory solely for these warnings |

Split `plc_state_unverifiable` by what evidence is required. Do not retain a whole-project inventory or old-value read solely to populate an apply effect. Remove the worker's old constant-value readability prerequisite whose purpose is displaying the replaced value. Keep target resolution, content/hash availability, and group traversal needed for the library deletion check. Group traversal must not silently skip unreadable descendants. Fatal native exceptions retain `portal_lost` precedence. Native name collisions and default-table deletion remain Portal's authority; do not restore retired collision guards.

### Library-instance guard

Use typed `IEngineeringServiceProvider.GetService<LibraryTypeInstanceInfo>()`, already used by `LibraryTypeWriter`. A null service means no linkage. No arbitrary reflection, project-library enumeration, remote query or `Project.ProjectLibrary` cast is needed; standalone and local-session `ProjectBase` roots remain supported.

| Operation | Required behavior |
| --- | --- |
| `update_block_logic`, every supported format | Any non-null instance service blocks replacement, whether test/in-work or committed |
| `update_type_content`, every supported format | Same; changing a library type uses the existing library type-version workflow |
| `delete_block` | Block a linked `LibraryTypeVersionState.InWork` instance; permit deliberate deletion of an unlinked or `Committed` instance subject to ordinary native rules |
| `delete_block_group` | Inspect all live descendant blocks before group deletion; block if any has an in-work instance; do not silently skip unknown descendants |

Content guards need no version-state getter after detecting a link. Deletion checks read `info.LibraryTypeVersion.State`: installed V21 values are `InWork` and `Committed`, not `Released`. A missing version, recoverable service/state failure, unknown enum value or incomplete required traversal blocks as unverifiable. Preserve fatal exceptions instead of converting them into an ordinary guard. An inconsistent plain object may still fail normal export/hash checks; do not infer linkage from compile state or names.

Use blocking IDs `plc_library_instance_edit` and `plc_library_test_instance_delete`; use the narrowed `plc_state_unverifiable` when required inspection cannot complete. Preview reports a typed guard. Apply fails the affected item as `guard_blocked` before native import/delete and includes the stable guard ID in its diagnostic; earlier completed items remain applied. Preserve a block import's existing not-started outcome and do not falsely mark a protected object as changed. Native rejection categories are never selected by matching message text.

Recheck after every re-resolution/recreation and next to the native call. Group deletion still has the documented non-atomic race against concurrent UI changes; a binding lease is not a project-content lock. Do not claim that a host preview makes a future native write race-free.

## Edge cases

- Two concurrent applies of one ID: exactly one atomic claim; the other cannot dispatch mutation.
- Create table/group/block followed by use: no host batch plan on apply; later items resolve current state after earlier success.
- A second singleton OB created in the same apply: first succeeds; second fails its live check; remaining items are skipped.
- Content changes after preview: stored expected hash remains unchanged; live hash mismatch fails before import.
- One item changes a later target's library linkage: the later worker check sees current linkage.
- Unknown reference has no operations to audit: emit an empty entered-call item list, not invented operation IDs or raw request content.
- Budget compaction/omission, maximum IDs, large saved content, cancellation at publication/claim boundaries and worker restarts require explicit tests.
- Apply failure after earlier success uses existing partial-write warnings; failed/skipped non-content items still receive no verification entry.

## Failure modes

| Condition | Observable result |
| --- | --- |
| Invalid input source/shape | Canonical `validation_error`, `isError:true`, no worker/audit |
| Reference unavailable | Canonical entered error `preview_unavailable`, no mutation; one audit attempt |
| Preview capacity unavailable | Canonical entered error `preview_store_full`, no reference, no mutation; full-input apply still works |
| Binding/access/capability refusal before claim | Existing canonical denial; no mutation; do not consume an otherwise valid reference |
| Per-item mandatory check/native failure after admission | `phase:"applied"`, `success:false`, root `error:null`, `isError:false`; failed item then skipped remainder |
| Silent rename or failed required import postcondition | `postcondition_failed`/existing truthful import outcome; no trusted false success |
| Fatal Portal loss/timeout | Preserve classification, binding invalidation and available partial evidence; consumed reference is never replayed |
| Malformed execution/check payload | `protocol_error`; never echo rejected evidence or pretend it is an empty identity |
| Verification or delivery incomplete | Preserve actual attempts/statuses; delivered success can be false; no receipt for an incomplete preview |

## Acceptance criteria

- [ ] [S-1] Planned test `TestAllPlcApplyPathsSkipPlanner` covers all 14 operations through full input and ID; fails if either planner method runs, or preview mutates.
- [ ] [S-1] [S-4] Planned test `TestCreateUseAndLateFailure` checks ordered creation/use, later failure, `effects:[]` and skipped remainder; fails if a later miss prevents an earlier native success or causes rollback/replay.
- [ ] [S-2] Planned test `TestPreviewRequestIsImmutable` changes caller objects after reservation; fails if restored IDs, order, selectors, source/XML, hashes or explicit values differ from the captured request.
- [ ] [S-2] Planned test `TestPreviewAbsoluteExpiryAndQuota` advances fake time and fills entry/byte limits; fails if access renews expiry, reservations escape limits, unexpired entries are evicted or full-input apply is denied by cache capacity.
- [ ] [S-2] Planned test `TestPreviewClaimIsSingleUse` races claims and injects failures/cancellation on both sides of claim; fails if two executions occur, an admitted reference is restored or a pre-claim refusal consumes it.
- [ ] [S-2] Planned test `TestPreviewRequiresExactBinding` changes each binding/session/tool/type discriminator; fails if a same-path replacement, restart or foreign store can execute the saved request.
- [ ] [S-3] Planned test `TestPreviewInputAlternativesOverMcp` checks advertised schema and real protocol requests; fails if malformed source alternatives, explicit nulls, duplicates, wrong case, unknown members or ID dry runs reach worker/audit activity.
- [ ] [S-3] Planned test `TestOnlyCompletePreviewPublishesReceipt` forces guard, omission, capacity, cancellation and compose failures; fails if receipt fields disagree, an incomplete preview activates an entry or canonical MCP/audit documents drift.
- [ ] [S-3] [S-4] Planned test `TestReferenceApplyAuditsRestoredOperations` compares restored operations and optional audit correlation; fails if original IDs/statuses are lost, auditing duplicates or elicitation occurs.
- [ ] [S-4] Planned test `TestMandatoryChecksSurviveUnplannedApply` covers singleton, hash, declaration, cleanup and rename checks; fails if a protected mutation runs, false success is returned or a plain constant update still requires an effects-only old-value read.
- [ ] [S-4] Planned test `TestExecutionEvidenceDrivesVerification` exercises aliases, devices, units, paths, rename chains, failed attempts and lost evidence; fails if identity is fabricated, a pre-mutation refusal supersedes, an unknown sent attempt leaves false trusted verification or returned hashes differ from the next read.
- [ ] [S-5] Planned test `TestLinkedContentWritesNeverMutate` covers in-work/committed FB/FC/UDT and all supported formats with plain controls; fails if linked content reaches native replacement or a plain control is blocked solely as linked.
- [ ] [S-5] Planned test `TestLibraryInstanceDeletionChecks` covers direct/nested groups, committed controls and unreadable metadata/traversal; fails if an in-work/unknown protected target reaches delete or ID apply reuses stale preview clearance.
- [ ] [S-5] Planned test `TestLibraryGuardPreviewAndApplyShareInspector` checks worker routing/capabilities and fatal exceptions; fails if inspection mutates, loses `portal_lost`, requires standalone-only library access or bypasses the common helper.
- [ ] [S-6] Run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`, serial stub/installed-reference builds and triggered packaging checks, each with exit code 0; fails if any required gate fails or another tool's schema/version/planning behavior changes.
- [ ] [S-6] Inspect `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/IMPROVEMENT_LOG.md`, affected ADRs and the new dated acceptance report; fails if native OB82 evidence is called wholly untested, live evidence is overstated or a hazardous unguarded probe is repeated.

## Evidence and qualification limits

- [October 8 OB spike](../acceptance/reports/2026-10-08-ob-creation-spike.md): T4a/T4b duplicate DiagnosticErrorInterrupt OB82 imports succeeded and full compile failed. This is the positive reason to retain multiplicity protection; later claims that all native behavior is unknown overlook this earlier case.
- [October 9 OB acceptance](../acceptance/reports/2026-10-09-ob-creation-live-acceptance.md): guarded singleton refusal; the planning/execution race was offline-only.
- [October 26 PLC native rejections](../acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md): scoped duplicate/default-table refusals, the silent rename defect/fix, stale hashes and partial execution. L6 did not reach native duplicate creation through the guarded tool.
- [Collision directions](../acceptance/reports/inputs/2026-10-26-plc-write-collision-namespace-probes.md): four exact directions, not an exhaustive namespace guarantee.
- [November 7 library-type probes](../acceptance/reports/2026-11-07-library-types-native-probes.md): P12c/P11c modal replacement refusals; P15a direct in-work UDT deletion modal; P15d released UDT import silently unlinked. Direct FB/FC deletion and group deletion are a protective inference requiring qualified controls, not prior live proof.
- Existing source seams: `PlcWriteDomain` / `PlcVerificationPolicy`, worker `BlockMutationService.PickObNumber`, `TagMutationService.UpdateTag`, `PlcWritePreconditions.RequireContentHash`, `LibraryTypeWriter` and `LibraryTypeTargets`.
- New live acceptance uses disposable standalone and local-session fixtures and exercises guarded refusals/plain/committed controls. Do not deliberately repeat an unguarded crash, modal or unlink path. Record any unavailable setup as unqualified; a stub build is not runtime evidence.

## Open questions

<!-- No unresolved questions. Implementation decisions are recorded in the sections above. -->

## Verification boundary

Cache limits, reference lifetime, guard scope, major contract version, publication/consumption timing and the separate-PR boundary are decisions in this spec. Native API availability and new live behavior remain verification requirements, not presumed results. Planned tests above do not exist yet and are not reported as passed.
