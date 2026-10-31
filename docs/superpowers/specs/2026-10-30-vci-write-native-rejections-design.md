# vci_write native rejections and shared trusted verification — design

**Status:** design approved in discussion on 2026-10-30; measurement and test protocol below. The
implementation plan is written only after Stage 0 (§8 baseline timing, §9.2 baseline behavior,
§9.3 native probes on a throwaway probe build) is recorded and ruled on. Branch
`refactor/vci-write-native-rejections`, cut from `main` at `8713a47` (merge of the HMI
native-rejections PR 203).

**Stage 0 rulings — 2026-11-01:** all six maintainer decisions are settled in the
[Stage 0 record](../acceptance/reports/inputs/2026-10-31-vci-write-stage0.md#maintainer-decisions-and-completion-boundary).
They supersede conflicting expectations below: retain a corrected path-length diagnostic;
remove export collision prevention and leaf filename-rule duplication; retain containment and
directory-name boundary protection before managed filesystem operations. Native overwrite is
accepted as the tool contract, so the original L2/L5 blocked/no-overwrite expectations must be
revised in the implementation plan. Keep the shared-root workspace check and allow nested roots;
retire duplicate workspace-name checking. Use the supplied protected FC and the qualified import
fixtures. Compilation remains a separate explicit `compile_check` call after import. The live
25-failure gap is accepted; automated cap/omission/count/trimming coverage remains required.
Stage 0 evidence does not qualify the unimplemented candidate contract or final live matrix.

**Predecessors:** the [PLC plan](../plans/2026-10-25-plc-write-native-rejections.md), the
[Network design](2026-10-26-network-write-native-rejections-design.md) and the
[HMI design](2026-10-29-hmi-write-native-rejections-design.md), with their acceptance reports. VCI
history: [design](2026-10-15-vci-tools-design.md), [spike](../acceptance/reports/2026-10-15-vci-spike.md),
[live acceptance](../acceptance/reports/2026-10-16-vci-tools-live-acceptance.md). This closes the VCI
halves of the improvement-log follow-ups "HMI and VCI adopt the same error contract" and "HMI and
VCI onto the shared verifier".

## 1. Intent and why

`vci_write` is the last write tool that is not on the shared error and verification contract.

- **Errors are lost.** Every per-leaf Siemens exception in `export`, `sync_to_workspace` and the
  `sync_to_project` transaction is caught and counted (`VciWriter.cs` per-leaf catches); the item
  then fails with "One or more VCI leaves failed; inspect the counts before retrying." No message,
  no `engineeringError`, no leaf name. A top-level Siemens exception (workspace create/delete,
  `ExclusiveAccess`) becomes `worker_operation_failed` with message text only.
  `WorkerExecution.Execute` re-checks Portal disposal only when a wrapped failure carries
  `EngineeringError` or `HmiWriteOutcome`, so a Portal lost inside a VCI wrap stays
  `worker_operation_failed` and the binding stays verified.
- **Verification is VCI-specific.** `VciWriteVerifier` re-plans every `export`/`delete_mapping`
  (scope expansion, one `Find` per leaf, format and collision work) and reads `list_workspaces`
  after create/delete. It has its own check literals (`exists`, `absent`, `mappedLeaves`,
  `notStatusVerified`).
- **Dependent items re-plan.** A later item on the same workspace runs a full `vci_plan` right
  before its mutation.

The goal is **contract unification**: the same error classification, failure evidence, trusted
verification and planning shape as `plc_write`, `network_write` and `hmi_write`. Speed is a side
effect, and it is expected to be modest: native per-leaf work dominates VCI (spike Q5: export
~0.84 s per leaf, `GetStatus` ~0.46 s per mapping, import 1–8 s per block).

Unlike PLC, HMI and Network, most VCI checks guard against Portal behavior that is **not** a
rejection: same-key export overwrites files silently (spike: 12,603 → 257 bytes); a device or
library container and `SimaticSD` on a PLC leaf crash Portal; one throwing leaf makes the import
transaction uncommittable. Those checks stay.

## 2. Maintainer decisions (2026-10-30)

1. **Goal:** unify `vci_write` with the other write tools' error and verification contract. Not a
   performance project.
2. **Leaf failures:** `export` and `sync_to_workspace` keep processing the remaining leaves after a
   failed leaf and report the failure (today's continue-and-count behavior is kept).
3. **Failure list:** every failed leaf is listed, capped; no stop threshold. Counts stay exact.
4. **Dependent re-plans are dropped.** Later items on the same workspace rely on the worker's
   per-leaf checks; their collisions become failed leaves.
5. **Approach A, with the checkpoint moved before the plan** (§6): one PR in the HMI task order,
   but the native probes run in Stage 0 on a throwaway patched build, so the plan is written once on
   known Portal behavior and implementation has no mid-way live stop.
6. The playback in the discussion was accepted without changes: shared verifier keyed by workspace,
   the guard changes in §7.3, the two edge cases in §7.6, probes without crash risk.
7. **The implementation plan is written after Stage 0** and the maintainer's rulings on it.
8. **Probe mechanism:** a throwaway patched build of the tool (§9.3), not a standalone Openness
   harness.

## 3. Goals, non-goals, anti-goals

**Goals**

- Every Siemens exception during a `vci_write` mutation is classified like the other tools:
  `engineering_rejected` for `EngineeringTargetInvocationException`, `worker_operation_failed`
  otherwise, with verbatim `engineeringError`. The VCI outcome (counts, skips, notes,
  `projectModified`) is kept as evidence.
- The outcome lists failed leaves (first 20 in scope order) with their reason, and counts the rest.
- `portal_lost` wins over every VCI wrap and keeps the outcome.
- Verification through the shared `TrustedWriteVerifier`: `trusted`/`superseded` only, no reads.
- No dependent re-plan, no post-write read.
- `vci_write` moves to write contract `2.0`; `vci_read` stays `1.0`.
- Before/after timing (§8), a pre-plan native probe matrix and a final live run (§9) that record
  Portal's real state independently, and documentation written last from that evidence.

**Non-goals**

- The leaves × mappings scan in the export collision barrier (improvement log, O2). It stays as is.
- Library scopes, `connect`, workspace groups, VCI settings, local sessions (all remain open
  follow-ups).
- Changing `vci_read`, the breadcrumb mechanism, the status prefilter, the import transaction, or
  the mirrored path layout.
- Pre-release findings F28 (XML read after a VCI re-import returns `.s7dcl`) and F29 (counters
  appear to carry over between items): recorded in the improvement log, not fixed here.
- New worker methods or public operations.

**Anti-goals**

- Removing a check that protects against a silent overwrite, a Portal crash or an uncommittable
  transaction.
- Any live probe on a device or library container, or `SimaticSD` on a PLC leaf.
- A failed item that hides which leaf failed or why.
- A Siemens exception during planning that surfaces as `engineering_rejected`.

## 4. Constraints

- Preserve the 8/19/19 tool surface, `vci_write(operations, dryRun=false)`, 1–50 unique-ID items,
  caller order, stop after the first failed item, no rollback across items, no server elicitation,
  standalone-only binding, binding lease, access-mode gates, audit v2, and one
  `CanonicalJson.Serialize` call for text and `structuredContent`.
- Worker method names, `VciMethods`, `OperationPolicyCatalog` and `ProjectCapabilityCatalog`
  entries are unchanged.
- New outcome members are required members (no new conditional members); payload null policy
  through `WorkerPayloadNullPolicyRegisterTests`; `output-schemas.json` regenerated with only the
  `vci_write` row changed.
- `TiaMcpServer.Tests.csproj` links host `Vci/` files explicitly: an added or deleted file updates it.
- Build with `-m:1`; `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` after each
  task; packaging tests only if a product `.csproj` changes.
- Commit after each task with a short one-line message; no push until the maintainer asks.
- No TIA project names or paths in repository files or commits. Never dispose `TiaPortalProcess`.
- The worker timeout must cover the longest timed case; record the value used.
- Documentation is the last task, written from the live evidence.

## 5. What exists today (baseline `8713a47`)

| Layer | Where | What it does | Decision |
| --- | --- | --- | --- |
| Host request validation, path policy, project-folder rule | `VciWriteCatalog`, `VciPathPolicy`, `VciWritePlanner.ProjectFolderError` | request contract; no junction escape; root not inside the project folder | keep |
| Host planning | `VciWritePlanner.PlanAsync` | one `vci_plan` for the first item per workspace; later items `DependsOnItem` | keep first-item plan; dependents get no plan (§7.4) |
| Host dependent re-plan | `VciWritePlanner.ReplanAsync` via `WriteRun` | full `vci_plan` before each dependent item | drop |
| Worker planning | `VciPlanner.Plan` | workspace name/root conflict; scope expansion; `Find` per leaf; plan-time format resolution; name check; collision scan; catch-all → `Unverifiable` | keep except §7.3 |
| Worker create recheck | `VciWriter` create branch | re-runs `Plan` + `ValidateRoot`; `state_changed` | follows the `vci_workspace_exists` ruling (§7.3) |
| Worker per-leaf export barriers | `VciWriter` export loop, `ExportDirectory` | `alreadyMapped` skip; format resolve (no `SimaticSD` for PLC); name check; collision check before and after `CreateDirectory` | keep |
| Worker sync | `ShouldSynchronize`, `FindMapping`, import transaction | `GetStatus` prefilter and direction rules; mapping and owner re-resolution; one `ExclusiveAccess` + transaction | keep |
| Worker failure wrapping | per-leaf catches, final throw, top-level catches | count and drop Siemens exceptions; one generic `worker_operation_failed` | classify and list (§7.1, §7.2) |
| Portal-loss interplay | `WorkerExecution.Execute` | disposal re-check only for `EngineeringError`/`HmiWriteOutcome` wraps | extend to VCI (§7.1) |
| Host verification | `VciWriteVerifier` | `list_workspaces` read-back; re-plan for `export`/`delete_mapping`; `notStatusVerified` for syncs | replace with shared verifier (§7.5) |
| Guards | `VciGuardDefinitions` | `vci_file_key_collision`, `vci_name_not_representable`, `vci_workspace_exists`, `vci_state_unverifiable` (block); `vci_files_remain`, `vci_import_unvalidated` (info) | §7.3 |
| Contract | `VciContractVersion.Current = "1.0"` | shared by `vci_read` and `vci_write` | split; write `2.0` |

## 6. Approaches considered

**A. One PR in the HMI task order, with a live checkpoint.** Stage 0 baseline; error surfacing and
failure list; guard changes; **live checkpoint** and rulings; drop dependent re-plans; shared
verifier and contract 2.0; offline qualification; final live run and timing; docs. Trade-off: three
live sessions, no throwaway code; probe evidence is captured through the new error path.

**B. Probe first, then one PR.** Baseline and native probes in one session before any code.
Trade-off: two sessions, but today's tool hides the exceptions (leaf errors are swallowed; the
workspace guard blocks before Portal), so it needs a throwaway probe mechanism, and the evidence is
not in the final contract shape.

**Dropped: two PRs** (error contract, then verifier and re-plans). The contract changes in both
halves (a half-done 2.0 or two bumps), two reviews and two final live runs for one release-notes
line.

**Picked: A's task order with B's timing** (maintainer decision, revised after the builder check).
The checkpoint probes move into Stage 0 so no logic is rewritten after a late finding. The probe
mechanism is a throwaway patched build (§9.3), not a standalone Openness harness, because it makes
the same Siemens calls in the same order as the worker. The contract-shape checks that only the new
code can show (failure list, cap, trusted/superseded) stay in the final live run. Result: two live
sessions (Stage 0, final).

**Stage order:** Stage 0 (T1–T7 and B1–B8 on `main`, then P1–P9 on the probe build) → maintainer
rulings → plan → implementation (error surfacing and failure list; guard changes per rulings; drop
dependent re-plans; shared verifier and 2.0; offline qualification) → final live run and timing →
docs.

## 7. Decided design

### 7.1 Error surfacing in the worker

- Per-leaf catches in `export` and `sync_to_workspace` record the failure (§7.2) instead of only
  counting it, then continue with the next leaf. `NonRecoverableException` still escapes.
- Import transaction: every candidate whose `Synchronize` throws is recorded with its exception (the
  loop keeps going today and still does); rollback accounting is unchanged
  (`failed += candidates`, `succeeded = 0`, `projectModified = false`).
- When `failed > 0`, the item fails with category, message and `engineeringError` taken from the
  **first recorded failure**: for a Siemens exception, `WorkerExecution.Classify(ex, mutates: true)`,
  `WorkerExecution.FailureMessage(ex, category)` and `WorkerExecution.Describe(ex)`; for a
  server-raised leaf failure, `worker_operation_failed`, its message, and no `engineeringError`.
- A top-level Siemens exception (`Workspaces.Create`, `workspace.Delete`, `ExclusiveAccess`,
  transaction disposal, a throwing `Expand`) is classified the same way, with the outcome attached.
  It has no leaf, so it is not listed in `failures`. When it follows recorded leaf failures, **the
  exception that stopped the item decides** the item's category, message and `engineeringError`;
  the earlier leaf failures stay listed (design call).
- An import whose transaction cannot commit although no candidate threw: `worker_operation_failed`,
  a fixed message saying no candidate was imported, no `engineeringError`, empty `failures`
  (design call).
- Worker refusals that wrap no Siemens exception (`validation_error`, `unsupported_capability`,
  `target_not_found` from `FindWorkspace`, the create recheck's `state_changed`) keep their
  categories.
- Server-raised leaf failures keep their text and stay `worker_operation_failed`: "No safe supported
  export format.", the `FirstProblem` name reason, "The export file key is now occupied.",
  "The VCI mapping changed or is ambiguous.", "The VCI mapping no longer belongs to the selected
  leaf.", and the missing-workspace-file status (`FailMissing`, message decided by the builder).
  They are not reclassified as `state_changed`: an item with a non-null outcome may have mutated.
- `WorkerExecution.Execute`: the wrapped-failure disposal re-check also fires when
  `VciWriteOutcome` is set, and copies it onto the `portal_lost` response.
- VCI's own `NonRecoverableException` path is unchanged (`portal_lost`, counts, `lastAttempted`).
- `lastAttempted` keeps its meaning (the leaf being attempted at Portal loss or timeout); the failed
  leaves are named by `failures` (design call).

### 7.2 Failure list contract

`VciWriteOutcomeInfo` gains two required members:

- `failures`: array, possibly empty, of the first 20 failed leaves in processing order. Each entry:
  `path` (selector segments, as `lastAttempted.path`), `format` (nullable), `category`,
  `engineeringErrorType` (Siemens exception type name, null for server-raised failures), `message`
  (verbatim, trimmed to 500 characters).
- `failuresOmitted`: number of failed leaves beyond the cap.

Rules:

- `failures.Count + failuresOmitted <= failed`. Equality does not hold for an import rollback:
  candidates rolled back without their own exception are counted in `failed` but not listed.
- The item-level `engineeringError` is the full shared object (4,000-character message, chain of 8)
  for the first failure; the entries carry only type and trimmed message.
- 20 entries stay well under the 60,000-character value budget; the cap is a constant, not a setting.
- Recovery documented in the tool description: fix the cause and run the same export again;
  already-mapped leaves are skipped (`alreadyMapped`), so the re-run touches only the failed leaves.
- The host projection validates the entries (non-empty path, known category, cap respected,
  inequality above) and rejects a mismatch as `protocol_error` without echoing it.

### 7.3 Retired, narrowed and kept checks

| Check | Decision | Settled by |
| --- | --- | --- |
| `vci_workspace_exists`, duplicate name (host guard and worker create recheck) | disabled in the probe build; retires if Portal rejects cleanly (O1); kept otherwise | P1 |
| `vci_workspace_exists`, shared root | disabled in the probe build; **kept unless** Portal rejects cleanly (O1) | P2, P3 |
| `vci_state_unverifiable` | narrowed (design call). Still fires, with today's messages, for the definite negatives "no safe supported export format" and an invalid resolved relative directory, and for an exception while reading format evidence (`GetSupportedFileFormats`) or collision evidence (`MappedObjects` key enumeration, `Directory.EnumerateFiles`). The catch-all goes: an exception from `FindWorkspace`, `Expand` or `MappedObjects.Find` fails the call before any mutation with the worker's read category (`worker_operation_failed`, never `engineering_rejected`) and message | offline |
| Plan-time format resolution (first item per workspace) | keep: blocks a "no safe format" export before anything is written | — |
| `vci_file_key_collision`, late collision barrier, `vci_name_not_representable`, scope expansion, `SimaticSD` rule, status prefilter and direction rules, `FindMapping`, import transaction, breadcrumbs, `alreadyMapped` skip, project-folder rule | keep (silent overwrite, crash, transaction poisoning, filesystem effects, idempotent re-run) | — |
| `vci_files_remain`, `vci_import_unvalidated` | keep (info) | — |

An O3 finding in any probe reverses that case only, by maintainer ruling; the default for O3 is a
guard or an adjacent worker check, never a silent success.

### 7.4 Planning

- The first item per workspace keeps one `vci_plan` with its guards and effect.
- A later item on the same workspace gets no plan and no evidence-based guards; the dry-run preview
  marks it as depending on the earlier item (as today). No `vci_plan` is sent for it at any time.
  It keeps the operation-only info guards (`vci_files_remain` for deletes, `vci_import_unvalidated`
  for `sync_to_project`), computed on the host without worker evidence, in the response and the
  audit record as today (design call).
- `VciWriteDomain.ReplanAsync` fails loudly, as Network's and HMI's do. The mechanism that lets the
  shared pipeline execute a dependent item without re-planning is left to the builder.
- Accepted consequence: a collision or unrepresentable name in a dependent item is a failed leaf,
  not a block; the remaining leaves of that item still export.

### 7.5 Shared trusted verification

- A VCI `IWriteVerificationPolicy` with `ReadTool = "vci_read"`, `HasReadBack = false`, object key
  `vci\nworkspace\n{NAME}` with the name upper-cased.
- Completed items `trusted`; an item followed by a later attempted item on the same workspace
  `superseded`; a failed item with a non-null outcome supersedes earlier items (HMI rule);
  precondition refusals do not.
- `VciWriteVerifier.cs` is deleted, with the `exists`, `absent`, `mappedLeaves` and
  `notStatusVerified` literals. Accepted consequence: the post-write mapped-count comparison is gone;
  the item's own counts are the evidence. The tool description points to `vci_read list_mappings`
  (with status) after writes.

### 7.6 Edge cases kept as they are

- `sync_to_project` with a missing workspace file: other candidates import and commit; the item
  fails; the missing leaf is listed; `projectModified:true`.
- Re-export after `delete_mapping` into the same directory collides with the files left behind
  (`vci_files_remain`); unchanged.

### 7.7 Contract version and registers

- New `VciWriteContractVersion.Current = "2.0"`; `vci_read` keeps `VciContractVersion` `1.0`.
- `verification` is the shared `{success, operations[{operationId, success, check, expected,
  actual, message}], omission}` with `trusted`/`superseded` only.
- Registers: `ToolOutputContractConformanceTests` probes for 2.0; `output-schemas.json`;
  `WorkerPayloadNullPolicyRegisterTests`; FakeWorker `VciWriteRoundtripScenario` fixtures (§9.1).
- Tool description: classification, failure list and cap, re-run recovery, `trusted`/`superseded`,
  dependent-item behavior, `vci_read` and `compile_check` follow-ups.

## 8. What to measure (timing)

**Protocol** (as PLC, Network, HMI): `full` mode; one disposable standalone project copy; warm
worker; first call after server start discarded; one warm-up; three measured runs; medians of audit
v2 root `durationMs`, sum of item `durationMs`, and remainder (root − item sum), each median computed
independently. The same cases run unchanged on the installed `main` build (`8713a47`, Stage 0) and
on the candidate (final run).

**Record once per session:** TIA Portal build and UI language; tool build commit; worker timeout and
its source; PLC leaf count of the timed scopes; mapping count of the fixture workspace.

**Fixture state** (prepared once, saved in the disposable copy): workspace `base` holding about 200
mappings of one PLC (exported files on disk), plus 10 further leaves left unmapped for the timed
exports.

**Restoration between runs:** close without saving and reopen; delete the run's workspace root or
export subfolder from disk (unsaved workspaces and mappings vanish on close, files do not); restore
the T6 workspace files from a copy; untimed `vci_read list_workspaces` warm-up after reopen.

| Case | Call | Measures |
| --- | --- | --- |
| T1 | `create_workspace` on a new root | fixed overhead; removed `list_workspaces` read-back |
| T2 | `export` of 3 unmapped leaves into `base` (own `relativeDirectory`) | removed verify re-plan; comparable to the 2026-10-16 3-leaf export (11.1 s) |
| T3 | one call: `create_workspace` W; `export` 10 leaves into W; `delete_mapping` of 3 of them; `export` the same 10 leaves into W under another `relativeDirectory` | removed dependent re-plans and verify re-plans |
| T4 | T3 with `dryRun:true` | planning alone (expected flat: dependents were never planned in preview) |
| T5 | `sync_to_workspace` on 10 mapped leaves of `base`, 3 changed in the project (via `plc_write`) | control: status reads dominate |
| T6 | `sync_to_project` on 3 mapped leaves of `base` whose workspace files were changed | control: import transaction |
| T7 | `export` of about 50 unmapped leaves into `base` (about 200 mappings present) | verify re-plan scaling with N; also confirms the unchanged leaves × mappings barrier stays within the timeout |

Expected direction (inference): T1–T3 and T7 lower; T4–T6 roughly flat. A slower case is recorded,
not hidden (Network preview precedent).

## 9. What to test

### 9.1 Offline tests (each task; final qualification)

Every retired behavior gets a test proving its absence.

- **Worker classification:** per-leaf `EngineeringTargetInvocationException` → item
  `engineering_rejected` with `engineeringError`; other `EngineeringException` →
  `worker_operation_failed` with `engineeringError`; a server-raised leaf failure first → item
  category from it, no `engineeringError`; remaining leaves still processed after a failure.
- **Failure list:** order; 20-entry cap; `failuresOmitted`; 500-character trim; exact `failed`;
  import rollback lists only the throwing candidate; `FailMissing` listed with partial commit.
- **Top level:** `Workspaces.Create` / `workspace.Delete` Siemens throws classified with outcome;
  a top-level throw after recorded leaf failures decides the item category; several throwing import
  candidates all listed; a non-committable transaction without a throw gets the fixed message and
  empty `failures`.
- **Planning:** narrowed `vci_state_unverifiable` fires for the listed cases only; a throwing
  `Expand`/`Find`/`FindWorkspace` fails the call before mutation, never `engineering_rejected`;
  dependent items keep the operation-only info guards.
- **Portal loss:** `NonRecoverableException` keeps counts and `lastAttempted`; a recoverable Siemens
  throw with current-execution disposal becomes `portal_lost` and keeps `VciWriteOutcome`
  (`WorkerExecutionTests`).
- **Host planning:** no `vci_plan` for a dependent item (dry run and actual); `ReplanAsync` throws;
  first-item guards unchanged; narrowed `vci_state_unverifiable`.
- **Host verification:** no worker call after mutation (no `list_workspaces`, no `vci_plan`);
  `trusted`/`superseded` keyed by workspace, case-insensitive; failed item with outcome supersedes;
  precondition refusal does not; failed items have no entry.
- **Contract:** write `2.0`, read `1.0`; host projection rejects malformed `failures` as
  `protocol_error`; `output-schemas.json` changes only the `vci_write` row; conformance probes.
- **FakeWorker scenarios:** a rejected export leaf with outcome, `failures` and `engineeringError`;
  a rejected import with rollback counts.
- **Kept barriers stay pinned:** the late collision check before `ExportObject`
  (`VciWriterTests` existing pin), `SimaticSD` rule, status prefilter, `FindMapping` ownership.

### 9.2 Stage 0 — baseline behavior capture on `main` (before the plan)

Run with the installed `main` build, together with the T-case baseline, before the probe build is
installed. Purpose: record what the caller sees today for each probe input, and prove every input
can be built on the disposable copy. The plan's fixtures and tasks are written from this record and
§9.3.

| Case | Input | Today's expected response |
| --- | --- | --- |
| B1 | `export` of a scope with inconsistent blocks mixed with consistent ones | `failed:n`, generic message, no reason |
| B2 | `export` of a know-how protected block | as B1 |
| B3 | `export` of a leaf whose root + relative path + name totals 256–259 characters (under the tool's 259 limit, at or above Siemens' documented 256) | unknown; record files and directories created |
| B4 | `sync_to_workspace` onto a read-only workspace file | as B1 |
| B5 | `sync_to_project` with one corrupted SimaticML file and one valid changed file | expected rollback; first live evidence of a thrown-leaf rollback |
| B6 | `sync_to_project` with one missing workspace file and one valid changed file | partial commit, failed item |
| B7 | `create_workspace` with an existing name, and with an existing root | `vci_workspace_exists` blocks |
| B8 | 25 inconsistent FCs (created with `plc_write`) plus 5 consistent blocks in one `export` | `failed:25`, `succeeded:5` |

If an input cannot be built (for example no way to make Portal throw on import), that is recorded
and the plan adjusts the matrix before execution.

### 9.3 Stage 0 — native probes on the throwaway probe build (before the plan)

**Probe build.** A throwaway branch cut from `8713a47` (for example `spike/vci-native-probes`),
never merged, deleted after Stage 0; its commit is recorded in the Stage 0 record. It changes only:

- the worker's VCI catch sites (per-leaf in `export` and `sync_to_workspace`, each import
  candidate, the top-level catches): each caught exception appends one entry to the outcome's
  `notes` with the leaf path, the full exception type name, the verbatim message, and the type and
  message of each inner exception;
- `VciPlanner.Plan`'s workspace name and root conflict checks, switched off, so P1–P3 reach
  `Workspaces.Create` (this also disables the worker create recheck, which re-runs `Plan`);
- any minimal host change needed for those notes to reach the response unmodified.

Nothing else changes: same scope expansion, barriers, status prefilter, transaction and call order
as `main`. The probe build is installed only for P1–P9; the timing baseline and B1–B8 use the
unmodified `main` build.

**Gate:** after P1–P9 the record goes to the maintainer for rulings (O1/O2/O3, §9.5). The plan is
written from the record and the rulings.

| Case | Input | Settles |
| --- | --- | --- |
| P1 | `create_workspace` with an existing name; and a case variant | duplicate-name half of `vci_workspace_exists` |
| P2 | `create_workspace` with another workspace's root; case variant; trailing separator | shared-root half |
| P3 | `create_workspace` with a root nested inside another workspace's root | record only, unless O3 |
| P4 | B1 | per-leaf exception type (target-invocation or not) and message; other leaves exported |
| P5 | B2 | exception type and message |
| P6 | B3 | Siemens' 256 limit; any file or leftover empty directory (the tool creates the directory before export) |
| P7 | B4 | exception type; file unchanged |
| P8 | B5 and B6 | import rejection type; whether the whole transaction rolls back; `FailMissing` partial commit |
| P9 | B8 | native message for each of the 25 rejected leaves (length and variation inform the 500-character trim) |

Expected exception types feed the FakeWorker fixtures and the offline classification tests; a type
other than `EngineeringTargetInvocationException` on a mutation is recorded, and its item category
stays `worker_operation_failed`.

### 9.4 Final live run (candidate)

The probe inputs whose response the new code shapes, re-run on the candidate to confirm the
contract: P1 (and P2 per ruling), P4, P8, and P9 extended to the contract checks (cap 20,
`failuresOmitted:5`, exact counts; a re-run lists the next failures and skips mapped leaves). Plus:

| Case | Input | Expected |
| --- | --- | --- |
| L1 | happy path with all six operations on one workspace in one call | `applied`; `trusted` for the last item per workspace, `superseded` before it |
| L2 | `export` A into W, then dependent `export` B into W with a stray `<stem>.xml` pre-placed for one of B's leaves | that leaf in `failures` ("file key is now occupied"), others exported, stray file byte-identical |
| L3 | L1 with a rejected export (inconsistent block) in the middle | root `success:false`, `error:null`; earlier items applied; failed item with outcome, `failures`, `engineeringError`; later items skipped |
| L4 | L1 with `dryRun:true` | preview; dependents marked; no mutation; no worker `vci_plan` for dependents |
| L5 | first-item `export` onto a pre-placed stray file | `vci_file_key_collision` blocks the call before any mutation |
| L6 | `sync_to_project` success, then `compile_check` | `vci_import_unvalidated` info; compile result recorded |

Portal loss is covered offline only (crash probes excluded).

### 9.5 Classification and recording

- **O1** Openness throws: typed failure with verbatim `engineeringError`; becomes a FakeWorker
  fixture and a documentation example.
- **O2** Openness accepts; `vci_read list_mappings` with status, or `compile_check` after an import,
  reports it: documented.
- **O3** Openness accepts and neither reports it, or the write lands on the wrong file or object:
  maintainer ruling; default guard or adjacent worker check.

The Stage 0 record (timing, B cases, P cases, build commits) goes under
`docs/superpowers/acceptance/reports/inputs/`, listed in `docs/README.md`, with no project names or
paths. Every case records: the canonical `vci_write` document; independent `vci_read list_workspaces` and
`list_mappings` with status; a before/after file listing with sizes and hashes of every directory the
case could touch; `plc_read` and `compile_check` for imports; Portal build and UI language. Restore
after each mutating case (§8 restoration).

## 10. Documentation (last, from evidence)

- `docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md`: write 2.0, classification with a captured
  example, failure list and recovery, dependent items, `trusted`/`superseded`, outcome table from §9.
- `AGENTS.md`: the VCI bullet and the failure-categories sentence ("VCI wraps … still flatten").
- `docs/ARCHITECTURE.md` §7a and VCI section.
- `docs/IMPROVEMENT_LOG.md`: completed entry with timing; close the VCI halves of the two follow-ups;
  add F28, F29 and any O3 rulings.
- `docs/README.md`: plan and acceptance report rows; `docs/roadmap/json-contract.md` version note.

## 11. Left to the builder

- Mechanism for executing dependent items without re-planning in the shared pipeline (§7.4).
- Placement and names of the verification policy, the contract constant and the failure-entry type.
- `FailMissing` message text.
- Exact T and L selectors on the disposable copy, chosen after Stage 0.
- Test reshaping in `VciWriterTests`, `VciWritePlannerTests`, `VciWriteVerifierTests` (deleted or
  replaced), `VciGuardedWriteDomainTests`, `VciGuardedWriteMcpTests`, `VciWriteBudgetTests`,
  `VciContractDecodeTests`.

## 12. Risks

| Risk | Handling |
| --- | --- |
| Portal accepts a shared workspace root silently and files alias | P2/P3 read file state independently; shared-root half returns by default unless O1 |
| No reliable throwing import exists (2026-10-16 limit) | B5 tries a corrupted SimaticML file; if it does not throw, rollback stays an offline-only claim and the limit is retained |
| One `engineeringError` hides other causes | `failures` lists up to 20 with reasons; counts exact; re-run recovery |
| Probe leaves an empty directory (tool creates it before export) | recorded in P6; documented if observed |
| Probe build behaves differently from the final code | the patch only adds notes and disables two conflict checks; final run re-runs P1, P2 (per ruling), P4, P8, P9 on the candidate |
| A probe-build note exceeds the value budget | notes are diagnostic; if the 60,000-character value budget omits them, rerun the case with a smaller scope |
| Removing dependent re-plans turns a blocked item into partial work | accepted by maintainer decision; L2 proves no overwrite |
| Timing noise dominated by native per-leaf cost | three-run medians; item sum and remainder reported separately |
| Portal loss during a probe | `portal_lost` with outcome; binding invalidated; recorded |
