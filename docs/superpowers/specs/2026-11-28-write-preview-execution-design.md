# All mutation tools: direct apply and reusable previews

Status: specified for documentation review; implementation and new live acceptance have not run.
Date: 2026-11-28
Source baseline: `5cdc4fd2`; branch `feat/plc-write-preview-execution`.
Plan: [Phased implementation plan](../plans/2026-11-28-write-preview-execution.md).
Supersedes the [PLC-only design](2026-11-12-plc-write-preview-execution-design.md).

## Problem

Apply behavior is inconsistent. Library content/full-mode calls already omit descriptive planning, while PLC, Network, HMI and VCI obtain execution inputs or verification identities from their planners. Lifecycle also mixes execution protections with presentation. Repeating inventory, simulation and diffs increases work and response size, and initial whole-batch planning can reject a later item before an earlier independent item executes. Preview references would avoid resending exact requests, but adding them first would preserve these inconsistencies. Standardize apply across every mutation/lifecycle operation first, then add references across the complete surface.

## Users

MCP clients need predictable direct execution, truthful partial outcomes and compact preview-to-apply calls. Engineers need previews for descriptions, unchanged detailed lifecycle confirmations, and clear evidence of what was attempted. Maintainers need independently reviewable PRs whose release notes describe complete delivered behavior.

## In scope

- [S-1] Standardize all 63 public mutation/lifecycle operations on the guarded write pipeline described in `docs/ARCHITECTURE.md`: previews describe; nonprompting apply executes ordered items directly without descriptive planning; prompting calls retain fresh consequence planning and rechecks.
- [S-2] Preserve required execution checks and add the previously planned PLC library-instance protections at native boundaries. Classify every existing planner check before removing its apply dependency.
- [S-3] Derive execution preparation, canonical identities, possible-mutation evidence and verification from current execution, including patches, partial failures, shared compilation and read-back reporting.
- [S-4] After standardization, add exact immutable preview references to all 63 operations through their existing 11 public tools, using shared session storage and the guarded pipeline.
- [S-5] Enforce original-context validity, expiry, exclusive in-flight use, and the agreed success/two-failure/uncertain-change retirement rules.
- [S-6] Maintain closed MCP input/schema contracts, effective response budgets, one canonical delivered document and audit v2 correlation.
- [S-7] Deliver separate PRs with exhaustive operation coverage, focused/offline/installed-reference checks, explicitly authorized live qualification and measured efficiency boundaries.

## Out of scope

- `compile_check` remains a direct compilation action: no planning, preview requirement, preview reference, new confirmation, or access-mode change. Its existing access/binding checks and result contract remain. Automatic shared compilation inside PLC writes remains supported.
- Read tools and `bind_project` do not gain references. Session selection is a context-changing action that invalidates old references.
- Automatic replay, batch rollback, resuming a failed batch, cached execution results and native mutations based solely on a previous preview are excluded.
- A reference is neither approval nor an idempotency guarantee. Direct requests remain supported without a preview.
- No live call, installation change, remote write, push or PR publication is authorized by this design.

## Constraints

- Keep the .NET 10 host / .NET Framework 4.8 worker split; Siemens calls run in the worker.
- Reuse `Safety/Pipeline`, canonical structured results, the binding lease, operation/capability catalogs and audit v2. Do not create a parallel write pipeline or revive safety tokens.
- Preserve access modes, local-session/container eligibility, native exception classification, zero elicitation for content writes and current lifecycle elicitation rules.
- Preserve caller ordering, 1-50 unique-ID items for batch tools, 256-character IDs, stop at the first failed item, no batch rollback and no automatic replay. Lifecycle stays one command per call.
- Retain fresh request/access, exact binding/owner/target, concurrency/hash, crash-prevention, silent-failure, cleanup and transaction protections. Remove only work whose sole purpose is descriptive inventory, simulation or presentation.
- Previews never mutate. Nonprompting apply never calls a descriptive planner or re-planner. Prompting calls retain fresh consequence planning and post-acceptance comparison.
- Use effective `MaxItemChars` / `MaxDocumentChars`; canonical text, structured content and audited delivered document/hash remain identical.
- Preserve all 63 operations. Compilation remains direct without planning. A test must detect an added writable tool or operation that is neither covered nor explicitly classified as compilation.
- Builds, tests, fixture mutation and Git commits run serially. Preserve unrelated work and commit each implementation/fix step using a conventional message. Remote writes require explicit approval.

## Operation coverage and current evidence

This is a source inventory, not new runtime qualification. The matrix is exhaustive at the baseline; it must be compared with production discovery/catalogs in tests rather than maintained as an unchecked count.

| Surface | Count | Operations |
| --- | ---: | --- |
| `plc_write` | 15 | `update_block_logic`, `patch_block_logic`, `update_type_content`, `create_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `create_user_constant`, `update_user_constant`, `delete_user_constant`, `create_block`, `delete_block`, `create_block_group`, `delete_block_group` |
| `network_write` | 5 | `add_network_device`, `configure_network_device`, `create_subnet`, `update_subnet`, `delete_subnet` |
| `hmi_write` | 9 | `create_tag_table`, `update_tag_table`, `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `create_alarm`, `update_alarm`, `delete_alarm` |
| `vci_write` | 6 | `create_workspace`, `delete_workspace`, `export`, `sync_to_workspace`, `sync_to_project`, `delete_mapping` |
| `library_write` | 22 | `create_global`, `open_global`, `retrieve_global`, `save_global`, `close_global`, `archive_global`, `create_folder`, `delete_folder`, `create_master_copy`, `copy_master_copy`, `instantiate_master_copy`, `delete_master_copy`, `create_type`, `create_type_version`, `release_type_version`, `set_default_type_version`, `synchronize_type`, `instantiate_type_version`, `delete_type`, `delete_type_version`, `set_comments`, `set_languages` |
| Project lifecycle | 6 | `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project` |
| Compilation control | excluded | `compile_check`: direct compilation, never a descriptive planner |

Current owning sources are `Plc/PlcOperationCatalog.cs`, `Network/NetworkWriteTools.cs`, `Hmi/HmiWriteCatalog.cs`, `Vci/VciWriteCatalog.cs`, `Contracts/Library/LibraryWriteMethods.cs` and `Tools/ProjectWriteTools.cs`, under their existing projects. `Tools/ProjectEngineeringTools.cs` implements direct compilation. All-tools discovery and shared operation policy tests must catch drift.

## Interfaces

### One apply contract, domain-specific execution checks

`IWriteDomain.PlansBeforeApply(items)` already exists. Opt a domain out only after its required execution preparation, guards and verification no longer depend on presented effects. The runner continues to plan `dryRun` and read-write prompting calls even when this hook returns false. Do not require a prior preview.

Nonprompting batch apply returns `effects:[]`; nonprompting lifecycle apply returns `effects:null`. Prompting calls retain freshly computed consequences needed for confirmation. Never echo a stored preview's effects as current state. Direct and reference apply have the same execution path and checks.

Apply processes one current item at a time. An execution failure on a later item may follow earlier successes; preserve truthful attempted-failure documents, skipped remainder, partial outcomes and inspect-before-retry guidance. There is no hidden whole-batch inventory, simulation, diff or mandatory preview. Narrow target resolution, required evidence reads and native-adjacent checks are execution work, not a descriptive plan.

| Domain | Required execution protections | Descriptive work omitted from nonprompting apply |
| --- | --- | --- |
| PLC | Live target/owner, fresh full-export hash and patch base, format/declaration/target checks, singleton multiplicity/number, ignored-rename read-back, required import/source cleanup, linked-instance protections | Whole tag/tree inventory solely for effects, content diffs, address-overlap notes, deletion descriptions and batch simulation; old constant reads solely to display old values |
| Network | Live canonical owner/target, required settings checks, recovery-result admission, ignored-rename postcondition; retain complete deletion-consequence protection where currently required | Configuration effect snapshots and connection descriptions; extract required deletion evidence rather than blanket-disabling its block guard |
| HMI | Live owner/target uniqueness, required deletion/reference protection, native writes, identity-checked create cleanup and typed partial outcomes | Full descriptive table/tag/alarm inventory and reference descriptions; do not treat missing required evidence as absence |
| VCI | Project-directory/root containment, path representability/boundaries, exact-root conflicts, live owner/status checks, import transaction and fatal-loss context | Leaf/mapping counts, informational scope/format descriptions and simulated batch effects |
| Library | Existing name/link/collision guards where they protect identity, fresh type-content hash, version ordering, transaction/commit/end-state checks and native results | Existing preview-only contents/dependency/instance descriptions; content/full calls already omit these |
| Lifecycle | Current source ownership/dirty state, destination, preservation/discard and local-session protections, required verification and binding transitions | Full-mode presentation after required execution preparation is separated; read-write prompts keep fresh consequences and rechecks |

An existing check is retained until its execution purpose is explicitly classified. Native collision authority is preserved where already qualified; do not introduce Library's collision rules into PLC/HMI/Network or remove Library's rules merely to make implementations look alike.

### PLC specifics

Extract mandatory patch preparation from `TiaMcpServer/Plc/PlcWritePlanner.cs`: fresh served export, whole-export expected hash, original-text anchor uniqueness/overlap/newline rules and exact internal `update_block_logic` construction. Direct and reference apply resolve the original public patch against current state; never reuse a preview's resolved replacement. Preserve create-then-patch refusal, public item identity and exact attempted `resolvedOperation` audit capture.

Capture actual canonical target identity and mutation provenance in the worker's existing resolution/mutation traversal. Strictly validate conditional `plcWriteExecution` before permissive envelope decoding; preserve it through every result-copy and failure path. A false target mutation stamp is not, by itself, proof that temporary source/filesystem preparation made no changes. Missing possibly-sent evidence stays unknown. Verification cannot invent execution identity from a preview.

Retain source shared compilation, including committed imports whose item later fails, and compile before verification exports. Read-back reporting compares the same submitted execution content, resolved patches included, against the existing verification export. Keep one per-call excerpt allowance, informational comparison/compilation semantics, final reusable full-export hashes and safe loss/cancellation handling.

Use a typed `PlcLibraryInstanceGuard` for preview and native-adjacent execution. Block content replacement of linked FB/FC/UDT instances. Block direct/group deletion of in-work linked instances; retain committed deletion controls. Group deletion inspects all descendants and fails closed on incomplete required inspection. Initial and adjacent checks cover XML, SimaticSD and supported source routes; preserve cleanup and fatal-loss evidence. The November 7 report proves only its recorded modal/unlink cases; FB/FC/group deletion extensions require qualification.

### Network, HMI, VCI and lifecycle specifics

Network must replace planner-owned `_prepared` mutation inputs and recovery-budget reservations with current per-item execution preparation. HMI must resolve its current owner without a planner-populated owner map. Both must replace planned verification identities with execution identities and preserve partial outcomes, strict protocol validation and supersession uncertainty.

VCI moves mandatory path/root/conflict/required-evidence checks out of descriptive planning and initializes request tracking before either path. Do not change its export overwrite behavior, first-workspace preview behavior, native scope traversal, import transaction or failure ordering incidentally.

Lifecycle separates required preparation from presentation. Read-write project calls and Library batches containing global lifecycle operations retain existing prompt counts and fresh consequence comparison after acceptance. Full-mode lifecycle calls omit descriptive planning after required source/destination safeguards are extracted. Actual open/create/save-as/close may change binding; verification follows the resulting context, while reference admission is checked against the original source context. Library's existing nonprompting path is covered for all 22 operations, not rewritten for symmetry.

### Reference input and versions

References ship only after the all-operation standardization gate passes. Full requests and optional `dryRun:false` remain supported. Batch tools accept exactly one of `operations` or `previewId`; reject explicit null sources, both/neither sources, invalid IDs, duplicate/wrong-case/unknown roots and retired arguments before SDK binding. Reference input permits omitted/false `dryRun` only.

Lifecycle references are an alternative to that tool's complete scalar command, not an invented operations array. An ID-only call cannot also supply command arguments, even null/default-valued ones. Preserve valid zero-argument direct calls such as `save_project({})`; absence of `previewId` selects the existing scalar command form. Existing scalar null/default semantics remain where already legal. Store effective command values, exact ordering, IDs, selectors, hashes and content.

Use the closed-tool decoration seam; schemas advertise the same alternatives and keep injected services private. Every write response gets conditional `previewId` and `previewExpiresAt` together only for a publishable preview; apply emits neither. Audit v2 records optional ID correlation with restored requested operations and exact delivered document/hash, without another source/XML copy.

Normalization versions: PLC `2.1` to `3.0`; Network/HMI/VCI `2.0` to `3.0`; lifecycle `1.0` to `2.0`; Library remains `2.0` because its apply rule already exists. The final additive reference PR uses PLC/Network/HMI/VCI `3.1`, lifecycle `2.1`, Library `2.1`. Read and compilation versions remain unchanged. Capture changed output-schema nodes from real production `ListToolsAsync`, rather than hand-generating snapshots.

### Shared host seams

- Extend `WriteCall<TItem>(IReadOnlyList<TItem> Items, bool DryRun, string? PreviewId = null)` and add optional `WritePreviewMetadata(string PreviewId, DateTimeOffset ExpiresAt)` to `WriteReport`.
- Add default-off `SupportsPreviewReferences` and `IsPublishablePreview(TResponse response)` to `IWriteDomain`. Every mutation/lifecycle tool opts in in the final reference PR; compilation does not.
- Add host `WriteMutationDisposition { NoChangeProven, MayHaveChanged }` and `MutationDisposition(TItem item, WorkerCallResult result)`. The default proves no change only for authoritative not-sent dispatch status in `TiaMcpServer/Worker/WorkerCallResult.cs`; sent/unknown is conservative. The runner accumulates whole-attempt evidence and treats unobserved exceptions conservatively.
- Inject `WritePreviewStore(TimeProvider time)` through `WriteExecution`. `TryReserve<TItem>(string toolName, IReadOnlyList<TItem> items, out WritePreviewReservation<TItem>? reservation, out WriteToolError? error)` snapshots publication input. `TryBeginApply<TItem>(string toolName, string previewId, out WritePreviewAttempt<TItem>? attempt, out WriteToolError? error)` atomically returns fresh items plus context and exclusively marks the entry in-flight.
- `WritePreviewContext(long Epoch, ProjectBindingSnapshot Binding)` pins the exact source preparation. `WritePreviewAttempt.Complete(WritePreviewAttemptOutcome outcome)` is idempotent and requires the winning attempt token. Outcomes are `Succeeded`, `FailedNoChange`, `AbandonedNoChange`, `MayHaveChanged`, `ContextChanged`.
- Add closed failure categories `preview_unavailable`, `preview_store_full`, `preview_in_use` with canonical rejection and existing entered-call audit rules. A busy losing request neither prompts nor increments failures.

## Data

### Ownership and lifetime

The host/MCP session owns one private bounded store across all tools. Deep snapshots use canonical UTF-8 JSON and host-selected request types; caller-supplied CLR metadata is forbidden. Never retain mutable caller objects. Capacity includes reserved, available and in-flight entries: 16 entries and 16,777,216 combined request bytes. Never evict an unexpired entry to admit another. Capacity failure does not disable direct apply.

IDs contain 32 cryptographically random bytes encoded as `wp1_` plus 43 unpadded base64url characters; comparisons are Ordinal. Lifetime is an absolute 10 monotonic minutes from receipt preparation, not renewed on reads, failed attempts or declines. Derive displayed expiry and monotonic expiry from one clock pair. No persistent store, tombstones or execution-result cache.

### Context and invalidation

Require both exact source snapshot comparison and a session context epoch. The epoch changes for binding changes, worker restart, Portal attachment/owner change, loss or recovery in every binding state, including unbound. `SameBinding` alone is insufficient because unbound snapshots lack worker/Portal identity. Switching away and back cannot restore old references.

Before publishing a receipt, establish the worker runtime generation with a non-opening hello under the lease, including for cold unbound lifecycle previews. This may launch the worker process but never opens/creates a project or changes Portal attachment solely to publish a receipt. Capture context after successful preview preparation and this runtime grounding. Check it before apply preparation, after preparation and immediately before the first mutation dispatch. Existing direct-call configured-to-verified promotion or lifecycle recovery must not migrate a saved reference into a different context. Unbound open/create receipts remain usable only in their original unchanged unbound session context. A lost/restarted worker is not initial startup; reject the old receipt. Intended native attachment/binding transitions performed by the admitted open/create/save-as/close command are execution, retire the reference and do not retroactively reject successful execution.

### Publication

Reserve and deep-snapshot input before asynchronous preview work, using the restored snapshot for the preview itself. Prepare candidate metadata under the pinned lease and include its encoded overhead in protected-core and final response budgeting. Publish only after semantic success with no block guard and complete request, identity and required safety evidence.

Intentional shortening of diagnostic diffs/excerpts does not prevent publication. Missing required evidence, an omitted execution request/value or failed delivery of protected summaries does prevent publication. Implement an explicit domain predicate that distinguishes these cases; do not reject every nested `OmittedLineCount`, and do not treat missing guard evidence as harmless diagnostics.

Preserve semantic planning/safety success separately from budgeted delivery status. Root `success:true` with a receipt means the request, required evidence and protected summaries were delivered successfully; optional excerpt shortening is recorded in diagnostic omission metadata and does not turn that preview into `success:false`. If the existing budget code conflates these cases, separate them for preview composition. Required-value/protected-summary loss still yields delivery failure and no receipt. Never publish a receipt on an unexplained `success:false` preview, and never use root success alone as the eligibility predicate.

At finalization atomically check cancellation, expiry and context, activate once, serialize one canonical document and audit best-effort. On nonpublication discard the reservation and metadata without reconstructing an unbudgeted success. If metadata removal makes a previously failed budget fit, preserve the budget failure. Serialization failure after activation removes the entry. Audit append failure does not falsify a delivered successful receipt or imply persistence/client receipt.

### Exclusive use and retirement

Atomically move Available to InFlight before asynchronous preparation or confirmation; only that attempt may prompt or dispatch. Concurrent callers receive `preview_in_use`. Restore the exact immutable input, rerun current validation/access/guards and retain existing detailed confirmation/rechecks. The receipt never waives a current check.

| Attempt result | Entry disposition |
| --- | --- |
| Successful apply, including a successful no-op | Retire immediately |
| Any partial change, possible mutation or unknown dispatch/outcome | Retire immediately; inspect current state before a new request |
| Failed apply with affirmative whole-attempt zero-change proof, first failure | Return to Available with failure count 1 |
| Next failed apply with affirmative whole-attempt zero-change proof | Retire; two consecutive failures |
| User decline or cancellation proved before mutation dispatch | Return to Available without incrementing or resetting the failure count |
| Expiry, context mismatch/change or server restart | Retire/invalidate |
| Raw argument-shape rejection or concurrent busy loser | No claim/failure-count change |

Zero-change proof covers the whole attempt's requested engineering state and operation-owned external outputs/preparation effects, including earlier items, temporary source registration/cleanup, output files and lifecycle side effects. Independent audit/diagnostic bookkeeping is excluded from that mutation scope. Failure categories, empty applied-property lists, absence of a typed outcome or target-only `mutationStarted:false` are not proof. Initially permit release only where host/transport evidence proves no mutation request was sent; later strictly validated domain evidence may prove more. A sent failure without such evidence retires the ID.

Finalize the winning attempt on every exception, cancellation, verification, budgeting and serialization path before releasing exclusivity. A mutation followed by failed verification/delivery still retires. Audit failure remains best-effort and never reinstates an ID after mutation. Check expiry again before first mutation after any prompt; expiry does not independently abort an already executing batch. Never automatically retry or resume the saved request.

## Edge cases

- Exercise all 63 operations with direct input and, after references ship, saved input in both writable modes; verify read-only discovery still hides mutation tools. Compile is a direct-only control.
- Cover create/use chains, later missing targets, native refusals after earlier success, rename aliases, duplicate software names, Software Units, nested groups and local-session eligibility.
- Cover patch stale hashes and anchors, repeated writes to one target, shared compilation after partial commit, read-back omissions and binding-loss suppression of unsafe follow-up.
- Cover scalar lifecycle empty direct arguments, mixed sources/nulls/defaults, unbound open/create, switch-away/back, recovery during preparation and worker restart while still unbound.
- Cover in-flight concurrency, two proven-zero-change failures, decline/cancel between failures, success/no-op success, possibly-sent timeout, serialization/audit failure, expiry while waiting for confirmation and byte-exact quota boundaries.
- Cover large full replacements whose diagnostic diff exceeds 40 lines: a bounded excerpt alone must not withhold a receipt. Missing mandatory evidence must still withhold it.

## Failure modes

Keep canonical raw shape rejection distinct from entered validation, admission and attempted failures. Preserve domain-native exception/partial-outcome rules. Unknown or malformed execution evidence is `protocol_error` with rejected data withheld; it cannot prove zero change or revive an ID. Binding/Portal loss invalidates references without replay. Store saturation returns `preview_store_full` while direct apply remains available. Unknown/expired/retired/wrong-tool references return `preview_unavailable`; a known changed original context reports `binding_conflict` and invalidates the entry.

## Acceptance criteria

The named `Test...` cases below are planned tests, not claims that they exist or pass today.

- [ ] [S-1] `TestAllMutationOperationsFollowApplyPolicy` enumerates production catalogs/discovery: exactly the 63 listed operations are covered, every nonprompting direct/reference apply avoids descriptive plan/replan, and compilation never plans.
- [ ] [S-1] `TestPromptingCallsKeepFreshConsequences` covers all six project lifecycle tools and six Library global lifecycle operations in read-write; changed consequences after acceptance deny execution; full-mode calls do not elicit.
- [ ] [S-2] `TestExecutionCheckInventoryIsComplete` assigns every existing planner guard/read to retained execution protection or preview-only description, with native-boundary tests for retained checks and explicit no-call tests for descriptive reads.
- [ ] [S-2] `TestPlcLinkedInstanceGuardsNeverMutate` covers linked FB/FC/UDT formats/states, direct and nested-group deletion, unreadable inspection and plain/committed controls, plus existing singleton/hash/rename/cleanup protections.
- [ ] [S-3] `TestExecutionEvidenceDrivesVerification` covers real canonical identity, nonmutating refusals, started/unknown failures and strict malformed-envelope rejection for affected domains, preserving prior item status and truthful verification uncertainty.
- [ ] [S-3] `TestPatchSharedCompileReadBackSurviveDirectApply` covers fresh patch resolution, exact audit input, dependency restrictions, committed partial imports, compile-before-export, diagnostic read-back and reusable hashes without descriptive planning.
- [ ] [S-4] `TestEveryMutationOperationSupportsReferences` exercises direct/preview/reference sources for all 63 operations through their production tool schemas, with a direct compile exclusion control and zero-argument lifecycle cases.
- [ ] [S-5] `TestPreviewAttemptStateMachine` proves exclusive claims, success retirement, first/second proven-zero-change failures, noncounting declines/cancellations, irreversible partial/unknown retirement and no replay.
- [ ] [S-5] `TestPreviewContextIncludesUnboundEpoch` proves invalidation on rebind, switch-away/back, restart/attachment changes while unbound, and promotion/recovery during preparation.
- [ ] [S-5] `TestPreviewQuotaExpiryAndImmutability` proves snapshots, all-tools quota accounting, no eviction/renewal and expiry at publication/confirmation/claim boundaries.
- [ ] [S-6] `TestBoundedDiagnosticsStillPublishPreview` proves omitted diff excerpts can coexist with a receipt while incomplete required evidence and protected delivery failures cannot; candidate receipt overhead is budgeted.
- [ ] [S-6] `TestReferenceApplyPreservesCanonicalAudit` proves original restored operations, conditional correlation, exact delivered text/structured/audit identity, no rejected payload echo and safe serialization/audit failure retirement.
- [ ] [S-7] Each PR's report linked from `docs/superpowers/README.md` records `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` and stub/installed-reference builds in `TiaMcpServer.slnx`, plus focused failing-then-passing cases and triggered packaging gates; fails if any required gate is unmet or planned tests are presented as existing passes.
- [ ] [S-7] A dated qualification report linked from `docs/superpowers/README.md` records passed/failed/unexecuted rows by operation/container/mode; fails if a sample is generalized to all native paths or a live mutation lacks scoped authorization.
- [ ] [S-7] A measurement report linked from `docs/superpowers/README.md`, using workloads in `scripts/timing/measure_write_timing.py`, records argument bytes, canonical response bytes, worker-call counts, elapsed time and receipt eligibility; fails if model tokens/latency are claimed without direct observation or measurement boundaries are mixed.

## PR sequence and evidence

Standardization precedes references: (1) PLC linked-instance guards; (2) PLC direct apply/execution evidence; (3) Network direct apply; (4) HMI direct apply; (5) VCI direct apply; (6) lifecycle nonprompting direct apply plus Library/all-operation consistency qualification; (7) shared references across every mutation/lifecycle tool. Each implementation PR includes its tests, relevant docs and one concrete release-note sentence. The sixth PR gates the seventh; references are not partially delivered to PLC alone.

Retain the [November 7 library probe limits](../acceptance/reports/2026-11-07-library-types-native-probes.md), [patch measurements](../acceptance/reports/2026-11-26-issue-224-pr3-patch-block-logic-live-verification.md), [encoder measurement limits](../acceptance/reports/2026-11-26-issue-224-pr2-relaxed-json-client-verification.md) and [read-back/shared-compilation qualification](../acceptance/reports/2026-11-27-issue-223-readback-diff-live-verification.md). None proves new execution paths. Use `scripts/timing/` for transient measurements; retain only permitted durable native evidence and dated acceptance reports.

## Open questions

<!-- No unresolved product decisions. Decisions are recorded above. -->

## Verification boundary

This is a documentation-only design revision based on source inspection and the user's decisions. It does not implement, test or authorize runtime mutations. Before merge, migrate durable behavior/decisions to the existing architecture/reference/ADR structure, retain qualified acceptance reports, and prune these transient working documents in each owning PR.

Domain check inventories, exact native qualification and measured improvement remain implementation/verification obligations rather than presumed results. Declines/cancellations proved before dispatch are abandoned attempts, not failed operations; they preserve the failure count. Store limits retain the original defaults and are shared across all tools.
