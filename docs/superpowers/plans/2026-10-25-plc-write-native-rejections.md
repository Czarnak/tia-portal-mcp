# PLC Write Native Rejections Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Draft approved in discussion on 2026-10-25; not yet implemented. The maintainer agreed the three scope decisions below and asked that documentation be the last task, after live verification. Branch: `refactor/plc-write-native-rejections`.

**Goal:** Stop re-implementing TIA Portal's own rejections in `plc_write`. Let Openness reject name collisions, existing targets and protected objects, and surface each native rejection as a typed, verbatim engineering error in the canonical document. Keep the guards and checks that Openness cannot provide: delete consequences, address overlap and optimistic content concurrency.

**Architecture:** The guarded single-call pipeline (`Safety/Pipeline`) is unchanged: validate, verified binding, resolve targets, guards, `dryRun`, mutate, verify, audit. Inside the PLC domain, planning shrinks to the reads consequence guards need, the worker's duplicate preconditions go, and verification keeps only the content-hash re-export. A new `engineering_rejected` failure category carries an `engineeringError` object built from the Openness exception type and message chain, never from message parsing.

**Tech Stack:** C#/.NET 10 host, netstandard2.0 contracts, net48 worker, xUnit, FakeWorker; Windows/PowerShell for live steps. No new dependencies.

**Analysis baseline:** `main` at `7c04b44` (merge of PR 191), inspected on 2026-10-25. Line numbers below are navigation aids; revalidate before editing.

## Why

The PLC write domain checks the same facts three times: a host planner guard (`plc_name_collision`, `plc_block_exists`, `plc_default_tag_table`), a worker precondition (`PlcWritePreconditions`) and TIA Portal itself. The first two layers cost a full tag inventory and a project-tree page per touched PLC before any mutation, 572 lines of in-call state tracking (`PlcWorkingState`), 309 lines of worker preconditions and a post-write re-read that mostly confirms what a non-throwing Openness call already proved. The third layer is authoritative and free.

What Openness does not do, and the server must keep doing:

- Tell a caller what a delete removes (`plc_deletes_block`, `plc_deletes_group_contents`, `plc_deletes_table_contents`).
- Warn that a logical address overlaps another tag (`plc_address_overlap`); Portal permits the overlap.
- Reject a content write planned against stale content (`expectedContentHash` → `state_changed`).
- Report the normalized content hash after an import so the next `expectedContentHash` is correct.

## Approved scope decisions

1. **Error surfacing.** A Siemens exception during a `plc_write` item becomes a typed failure. `EngineeringTargetInvocationException` maps to the new closed category `engineering_rejected`; every other `EngineeringException` stays `worker_operation_failed`. Both carry a conditional `engineeringError` object with the exception type, the verbatim message and the inner-exception chain. Categories derive from the exception type only. Messages are localized by the Portal UI language and are never parsed.
2. **Pre-flight target resolution.** Planning keeps resolving the targets of update and delete operations, so a mistyped path still fails the whole call as `target_not_found` before any mutation, and the delete-consequence guards get the read they need. Planning stops resolving collision state for creates; Portal rejects a duplicate at mutation time.
3. **`plc_ob_singleton_exists`.** Expected to be a duplicate of Portal's own duplicate-block-number rejection. It is retired only after Task 6 confirms the native rejection live; until then it stays.

## Global constraints

- Preserve the 8/19/19 tool surface, `plc_write` arguments, the `dryRun` flag, binding leases, access-mode gates, stop-at-first-failure, no rollback, audit v2 and the canonical text/structured equality rule.
- `ProjectCapabilityCatalog.OperationFor` and `OperationPolicyCatalog` are unchanged: no worker method is added or removed.
- New conditional JSON members are registered in `ConditionalMemberRegisterTests`; new worker payload members write explicit nulls and go through the worker-payload reader.
- Block creation never passes the Openness import override option, so no guard is needed to prevent overwrites. Any code path that could pass it is a defect in this plan's scope.
- Do not widen into Network, HMI or VCI. Their equivalents are later plans that reuse the error contract from Task 1.
- Build with `-m:1`. Run `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` after each task. Packaging tests only if a product `.csproj` changes.
- Commit after each task with the task name in the message. Push to this branch only.
- Documentation (Task 7) is last and is written from the live evidence of Task 6, not before it.
- Task 0 runs before any code change and Task 6 repeats it unchanged on the candidate; a timing comparison measured on different projects, modes or cold workers is not evidence.

## Task 0: Baseline call timing (maintainer's machine, before any code change)

Measured on `main` at the analysis baseline, in `full` mode, against the same disposable project copy Task 6 will use, so the before and after numbers compare like with like.

**Metric source:** the audit v2 record each call appends under `%LOCALAPPDATA%\TiaMcpServer\audit\writes-yyyy-MM-dd.jsonl`. `durationMs` at the record root is the whole guarded call from pipeline entry to the audited document; `items[].durationMs` is each item's mutation (including any replan). Planning plus verification is the root value minus the sum of the item values. The MCP Inspector's observed wall-clock per call is recorded alongside as a secondary figure, since it includes host validation and transport that the audit does not.

**Protocol:**

| Case | Call | Why |
| --- | --- | --- |
| T1 | `plc_write` with one `create_tag` | smallest write; isolates fixed planning cost |
| T2 | `plc_write` with 10 items: create table, 5 tags, 2 constants, 1 block group, 1 block | typical multi-item call; shows per-item planning and verification |
| T3 | `plc_write` with one `update_block_logic` (fresh `expectedContentHash`) | content path; export, hash and diff stay after the change |
| T4 | `plc_write` with one `delete_block_group` holding the T2 block | delete-consequence path; its reads stay after the change |
| T5 | T2 with `dryRun:true` | planning alone |

- [ ] Record the project size: PLC count, tag count, block count, so the numbers can be interpreted.
- [ ] Discard the first call after server start (worker warm-up), then run each case three times and take the median of the audit root `durationMs`, the item sum and the Inspector wall-clock.
- [ ] Save the raw audit records and a results table in `docs/superpowers/acceptance/reports/inputs/2026-xx-xx-plc-write-timing-before.md` (or the inputs location the repository uses at the time).
- [ ] Commit "Task 0: baseline plc_write timing" on this branch before Task 1 starts.

## Task 1: Engineering error contract

**Files:**
- Create: `TiaMcpServer.Contracts/Worker/EngineeringErrorInfo.cs`
- Modify: `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs` (add `EngineeringRejected = "engineering_rejected"`, register in `Known`)
- Modify: `TiaMcpServer.Contracts/Worker/WorkerResponse.cs` (add `EngineeringError`)
- Modify: `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs` (optional `EngineeringError`)
- Modify: `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` (type-based mapping, build the info from the chain)
- Modify: `TiaMcpServer/OperationBatches/StructuredOperationBatch.cs` (`StructuredOperationFailure.EngineeringError`, conditional)
- Modify: `TiaMcpServer/Plc/PlcWritePayloadContract.cs` or the item-failure projection the PLC domain uses, so the member reaches the item failure
- Tests: `TiaMcpServer.Tests/Worker/WorkerExecutionTests.cs` (or nearest existing), `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`, `TiaMcpServer.Tests/Plc/PlcGuardedWriteMcpTests.cs`

**Shape:**

```json
"failure": {
  "category": "engineering_rejected",
  "message": "Operation 'op-3': TIA Portal rejected create_tag.",
  "engineeringError": {
    "type": "EngineeringTargetInvocationException",
    "message": "<verbatim Siemens text>",
    "inner": [ { "type": "...", "message": "..." } ]
  }
}
```

- [ ] Add `EngineeringErrorInfo { Type, Message, Inner[] }` with explicit-null serialization; `Inner` is an empty array, never null.
- [ ] Add `engineering_rejected` to the closed category list and its documentation comment; add it to the host-side category tests that enumerate `Known`.
- [ ] `WorkerExecution.Execute`: on `EngineeringTargetInvocationException` return `engineering_rejected`; on other `EngineeringException` keep `worker_operation_failed`; in both cases attach `EngineeringError` built from `ex` and `ex.InnerException` chain. Portal-loss classification is unchanged and still wins when the disposal revision moved.
- [ ] `WorkerOperationException` gains an optional `EngineeringError` so call sites that wrap a Siemens exception today (for example the content importers) can forward it instead of flattening it into text.
- [ ] Host: `StructuredOperationFailure` gets `[JsonIgnore(WhenWritingNull)] EngineeringErrorInfo? EngineeringError`; the PLC item-failure projection copies it from `WorkerResponse`. Register the member in `ConditionalMemberRegisterTests` with "present only when a Siemens exception caused the item failure".
- [ ] Confirm whether the shared item-failure projection already carries `engineeringError` into `network_write`, `hmi_write` and `vci_write` documents; record the answer for Task 7 and the follow-ups section.
- [ ] Offline tests: type-to-category mapping for each stub exception type; inner chain ordering; `engineeringError` absent on host-side failures (validation, binding, guard); the text block and `structuredContent` still come from one `CanonicalJson.Serialize` call.
- [ ] Build with stubs, run the default test filter, commit "Task 1: engineering error contract".

## Task 2: Retire duplicate guards and worker preconditions

**Files:**
- Modify: `TiaMcpServer/Plc/PlcGuardDefinitions.cs` (remove `NameCollision`, `BlockExists`, `DefaultTagTable`, `AttributeUnreadable`)
- Modify: `TiaMcpServer/Plc/PlcWorkingState.cs` (remove the collision and existence evaluation; OB singleton stays until Task 6)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Plc/PlcWritePreconditions.cs` (remove `RequireCpuNameFree`, `RequireTableNameFree`, `RequireBlockAbsent`, `RequireGroupNameFree`, `RequireReadableFlags`)
- Modify call sites: `TagMutationService.cs` lines 21, 58, 85, 89, 154; `BlockMutationService.cs` lines 31, 32, 122
- Tests: `PlcGuardDefinitionsTests.cs`, `PlcWorkingStateTests.cs`, `PlcGuardedWriteDomainTests.cs`, `PlcWritePlannerTests.cs`

**Kept in `PlcWritePreconditions`:** `RequireContentHash` (adjacent optimistic concurrency), `RequireReadableDescendants` (delete-group consequence evidence). `RequireReadableValue` on constant update/delete stays only if the effect reports the old value; otherwise it goes too. Decide while editing and note the outcome in the execution record.

- [ ] Remove the four guard ids and their definitions; update the guard-catalog tests that pin the id list.
- [ ] Remove the worker name-free and absence checks; the Openness call is now the first and only collision check.
- [ ] Confirm `create_block` never passes the import override option; add a test on the import options if one does not exist.
- [ ] Replace tests that asserted `guard_blocked` for collisions with tests that assert the worker response is forwarded as `engineering_rejected` (FakeWorker scripted failure).
- [ ] Build, test, commit "Task 2: retire duplicate PLC guards and preconditions".

## Task 3: Shrink planning

**Files:**
- Modify: `TiaMcpServer/Plc/PlcWritePlanner.cs`, `TiaMcpServer/Plc/PlcWorkingState.cs`, `TiaMcpServer/Plc/PlcWriteDomain.cs`, `TiaMcpServer/Plc/PlcWriteModels.cs`
- Modify: `TiaMcpServer/Safety/Pipeline/WriteExecution.cs` only if the replan hook becomes unused by every domain; otherwise the PLC domain returns a no-op replan
- Tests: `PlcWritePlannerTests.cs`, `PlcWorkingStateTests.cs`, `PlcGuardedWriteDomainTests.cs`

Planning reads per operation kind, lazily and once per PLC per call:

| Operation kind | Read | Purpose |
| --- | --- | --- |
| any | PLC inventory (`list_tag_tables` root) | resolve the PLC selector (`target_not_found`/`target_ambiguous`) |
| update/delete tag, constant, table | tag inventory of that PLC | resolve the target; `plc_deletes_table_contents`; `plc_address_overlap` on update |
| create tag | tag inventory of that PLC | `plc_address_overlap` only |
| delete block, delete group | project-tree page of that PLC | resolve the target; `plc_deletes_block`, `plc_deletes_group_contents` |
| create block, create group | none | Portal rejects a duplicate |
| content update | export in the write's format | hash check, diff evidence |

- [ ] Remove in-call collision tracking and `dependsOn` from `PlcWorkingState`; keep the selector resolution and the consequence enumeration.
- [ ] Remove `ReplanAsync` from the PLC domain (or make it a no-op if the pipeline interface keeps it); later items that target objects created earlier in the call are resolved by the worker at mutation time.
- [ ] `plc_state_unverifiable` narrows to: incomplete PLC inventory, unreadable delete consequences, unexportable content. Update its definition text.
- [ ] Preserve `dryRun` output: effects, guards and the content diff still appear in the preview.
- [ ] Build, test, commit "Task 3: plan only what consequence guards need".

## Task 4: Shrink verification

**Files:**
- Modify: `TiaMcpServer/Plc/PlcWriteVerifier.cs`, `TiaMcpServer/Plc/PlcWriteModels.cs`, `TiaMcpServer/Plc/PlcContractVersion.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/Block/BlockPostconditionVerifier.cs`, `TiaMcpServer.OpennessWorker/Openness/Plc/PlcTypePostconditionVerifier.cs` only where they re-read presence; the hash re-export stays
- Tests: `PlcWriteVerifierTests.cs`, `PlcWritePayloadContractTests.cs`, `PlcGuardedWriteMcpTests.cs`

- [ ] Verification keeps the `contentHash` check for `update_block_logic` and `update_type_content`; every other succeeded item reports check `trusted` with a message that the Openness call completed without rejection. `superseded` stays for an object a later item changed again.
- [ ] Remove the post-write inventory and tree reads from the verifier.
- [ ] Contract version: `PlcContractVersion.Current` is shared by `plc_read`. Introduce `PlcWriteContractVersion = "2.0"` for `plc_write` and leave the read version at `1.0`; update the conformance probes.
- [ ] Build, test, commit "Task 4: verify content hashes only".

## Task 5: Offline qualification

- [ ] `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` clean.
- [ ] `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` green; record the test count.
- [ ] `ToolOutputContractConformanceTests` and `ConditionalMemberRegisterTests` green with the new member.
- [ ] Review the diff adversarially against the constraints above; in particular confirm no code path reaches Siemens before the binding gate and that a Siemens exception in planning (reads) still maps through the existing read failure categories, not `engineering_rejected`.
- [ ] Record line counts removed per file in the execution record. Commit "Task 5: offline qualification".

## Task 6: Live verification (maintainer's machine, TIA Portal V21)

Run in `full` mode against a disposable copy of a project. Capture the exact `engineeringError` document for each case; these become test fixtures and documentation examples.

| Case | Operation | Expected |
| --- | --- | --- |
| L1 | `create_tag` with an existing tag name | item `engineering_rejected`, `type: EngineeringTargetInvocationException`, project unchanged |
| L2 | `create_tag_table` with an existing table name in another folder | `engineering_rejected`, project unchanged |
| L3 | `create_block` where the block exists | `engineering_rejected`, existing block content unchanged (hash equal before and after) |
| L4 | `create_block_group` with an existing group name | `engineering_rejected` |
| L5 | `delete_tag_table` on the default table | `engineering_rejected` |
| L6 | `create_block` OB with a number already used | `engineering_rejected`; if Portal accepts it, keep `plc_ob_singleton_exists` |
| L7 | `update_tag` renaming to a colliding name | `engineering_rejected`, original tag unchanged |
| L8 | happy path: create table, tag, block, group; update tag; delete all four | `applied`, `trusted` checks, info guards on the deletes |
| L9 | `update_block_logic` with a stale `expectedContentHash` | `state_changed`, no import |
| L10 | multi-item call where item 2 collides | item 1 applied, item 2 `engineering_rejected`, item 3 `skipped`, `success:false`, `error:null`, `isError:false` |
| L11 | `dryRun:true` on L10's call | phase `preview`, no mutation, no `engineering_rejected` (collisions are not planned) |

- [ ] Run L1–L11 and save the responses under `docs/superpowers/acceptance/reports/` inputs (final report in Task 7).
- [ ] For each L1–L7, re-read the target with `plc_read` and confirm the project is unchanged.
- [ ] Decide `plc_ob_singleton_exists` from L6: retire it (and its `PlcWorkingState` code and tests) in a follow-up commit if Portal rejected the duplicate.
- [ ] Note the Portal UI language used; the captured messages are in that language.
- [ ] Re-run the Task 0 protocol T1–T5 unchanged (same project copy, same mode, warm worker, three runs, median) on the Task 5 candidate; save the raw records and results table next to the baseline as `...-timing-after.md`.
- [ ] Pin the captured messages as FakeWorker fixtures in `PlcGuardedWriteMcpTests` so the document shape is regression-tested offline. Commit "Task 6: live verification fixtures".

## Task 7: Documentation (last)

Written only from Task 6 evidence.

- [ ] `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`: guard table (remaining guards only), the `engineering_rejected` failure, `engineeringError` shape with one captured example, verification semantics (`trusted`, `contentHash`, `superseded`), write contract `2.0`.
- [ ] `AGENTS.md`: the PLC writes bullet (guard list, verification, the new category); the write-safety line that names categories.
- [ ] `docs/ARCHITECTURE.md`: §7a failure categories and the PLC write seam.
- [ ] If Task 1 showed the other three write tools already carry `engineering_rejected`, add one sentence to each of the Network, HMI and VCI operation references naming the category and the `engineeringError` member, and leave their guard tables unchanged.
- [ ] `docs/IMPROVEMENT_LOG.md`: completed entry with removed line counts, the before/after timing table from Tasks 0 and 6 (root `durationMs`, item sum, planning-plus-verification remainder, Inspector wall-clock, per case) and retained limits; open follow-up for Network, HMI and VCI to adopt the same error contract.
- [ ] Acceptance report `docs/superpowers/acceptance/reports/2026-xx-xx-plc-write-native-rejections-live-verification.md`, including the timing comparison and the project size it was measured on, and its `docs/README.md` row; update this plan's status and the index row.
- [ ] Commit "Task 7: documentation".

## Review focus

| Condition | Required behavior |
| --- | --- |
| Siemens rejects item N of M | Items before N `applied`, N `engineering_rejected` with `engineeringError`, later items `skipped`; `success:false`, `error:null`, `isError:false`; one audit record with actual statuses. |
| Siemens throws during a planning read | Existing read category (`worker_operation_failed`, `target_not_found`, …) at the call level; never `engineering_rejected`. |
| Portal disposed during the item | `portal_lost` wins over `engineering_rejected`; binding and cursors invalidated. |
| Stale `expectedContentHash` | `state_changed` before any import, in planning and again adjacent to the import. |
| `dryRun:true` | Preview with effects, info guards and content diff; no Siemens mutation call. |
| Block guard in a dry run | Phase stays `preview` per current behavior; unchanged by this plan. |

## Follow-ups: Network, HMI and VCI

Separate plans, each after this one is merged. They reuse Task 1's contract and the Task 0/6 timing protocol; none of them starts before the PLC live verification has shown that letting Portal reject is sound in practice.

**Shared consequence of Task 1.** The type-based mapping lives in the worker's shared catch (`WorkerExecution.Execute`), so from Task 1 on every write tool's worker response can carry `engineering_rejected` with an `engineeringError`. Task 1 must confirm whether the shared item-failure projection from the D-4 refactor already copies the member into `network_write`, `hmi_write` and `vci_write` item failures; if it does, Task 7 documents the category for all four tools, and the follow-ups below are guard and verification work only.

Classification from the 2026-10-25 analysis, to be re-verified at each follow-up's own baseline:

| Domain | Duplicates Portal (retire) | Portal cannot provide (keep) | Evidence guard (narrow to what kept guards read) | Verification |
| --- | --- | --- | --- | --- |
| Network (`NetworkWritePlanner` 209, `NetworkWriteVerifier` 286, worker `NetworkMutationVerifier` 194) | none declared as a guard; check whether planning pre-resolves device-name or subnet-name uniqueness that Portal enforces | `network_connected_delete` (info); the rule that an incomplete consequence inventory blocks every mode | `network_unverifiable` | Largest candidate: identity, final checks and preservation checks re-read what a non-throwing call already proved; keep the preservation check after a requested skip, which is a contract promise |
| HMI (`HmiWritePlanner` 348, `HmiWriteVerifier` 277) | `hmi_name_collision` | `hmi_deletes_table_contents`, `hmi_deletes_referenced_tag` (Portal leaves references dangling silently); failed-create identity-checked cleanup and `appliedProperties`/`failedProperty` on failed updates, which are partial-mutation reporting, not re-checking | `hmi_state_unverifiable`, still including the 200-query cross-reference cap for delete planning | Keep snapshot invalidation after mutation; drop presence re-reads of created objects |
| VCI (`VciWritePlanner` 89, `VciWriteVerifier` 65) | `vci_workspace_exists`; `vci_file_key_collision` only if live capture shows Portal rejects a duplicate key rather than silently remapping | `vci_files_remain`, `vci_import_unvalidated` (info); the no-status-read rule in planning and the adjacent owner recheck for sync | `vci_state_unverifiable` | Already small; `vci_name_not_representable` is a filesystem check, not a Portal one, and stays because an export that fails halfway leaves files behind |

Order: HMI first, because its collision guard and planner are the closest match to the PLC change and its live acceptance fixture exists; then Network, where the win is verification rather than guards; VCI last, as it has the least to gain and the export crash risk makes live runs expensive.

Each follow-up plan records: its own baseline timing, the guards retired with the live case that proved the native rejection, the verification checks dropped, and the contract version bump of its write tool.

## Out of scope

- Network, HMI and VCI guard and verification reviews (see the follow-ups section; separate plans).
- Any change to lifecycle confirmation, elicitation or audit.
- Changing the `dryRun` preview phase when a block guard fires.

## Execution record

Line counts for `534937a..HEAD` (`git diff --numstat`), removed / added.

Production files (total 492 removed, 270 added):

| File | Removed | Added |
| --- | --- | --- |
| `TiaMcpServer.OpennessWorker/Openness/Plc/PlcWritePreconditions.cs` | 209 | 8 |
| `TiaMcpServer/Plc/PlcWriteVerifier.cs` | 101 | 37 |
| `TiaMcpServer/Plc/PlcWorkingState.cs` | 74 | 64 |
| `TiaMcpServer.Contracts/Plc/PlcNameRules.cs` | 34 | 2 |
| `TiaMcpServer/Plc/PlcWritePlanner.cs` | 14 | 32 |
| `TiaMcpServer.OpennessWorker/Openness/Plc/TagMutationService.cs` | 19 | 0 |
| `TiaMcpServer.OpennessWorker/Openness/Block/BlockMutationService.cs` | 16 | 1 |
| `TiaMcpServer/Safety/Pipeline/WriteRun.cs` | 1 | 11 |
| `TiaMcpServer/Plc/PlcGuardDefinitions.cs` | 9 | 1 |
| `TiaMcpServer.OpennessWorker/Program.cs` | 5 | 11 |
| `TiaMcpServer.OpennessWorker/Worker/WorkerExecution.cs` | 4 | 32 |
| `TiaMcpServer/Plc/PlcWriteDomain.cs` | 1 | 5 |
| `TiaMcpServer.Contracts/Worker/EngineeringErrorInfo.cs` (new) | 0 | 32 |
| `TiaMcpServer.Contracts/Worker/WorkerFailureCategories.cs` | 0 | 8 |
| `TiaMcpServer/Plc/PlcContractVersion.cs` | 1 | 7 |
| `TiaMcpServer.OpennessWorker/Worker/WorkerOperationException.cs` | 1 | 6 |
| `TiaMcpServer.Contracts/Worker/WorkerResponse.cs` | 0 | 4 |
| `TiaMcpServer/Worker/WorkerCallResult.cs` | 0 | 3 |
| `TiaMcpServer/OperationBatches/StructuredOperationBatch.cs` | 1 | 3 |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | 0 | 1 |
| `TiaMcpServer/Plc/PlcWritePayloadBudget.cs` | 1 | 1 |
| `TiaMcpServer/Plc/PlcWriteTools.cs` | 1 | 1 |

Tests and test support: `TiaMcpServer.Tests` 260 removed / 546 added (plus 576 added in the regenerated `output-schemas.json` snapshot); `TiaMcpServer.FakeWorker` 6 removed / 27 added.

`RequireReadableValue`: kept for `update_user_constant`, because its planned effect reports the value it replaces and verification needs it; removed for `delete_user_constant`.

Final default test count: 7711 passed, 0 failed (`dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`).

Builds: the stub build and the full-solution build against the installed TIA V21 assemblies both end with 0 errors and 8 warnings. Seven of the eight (Hmi tests, `NetworkObjectSelectorResolver.cs:548/569`) also appear when the base `534937a` is built with stubs; the eighth, a nullable dereference at `PlcWorkingStateTests.cs:468`, was introduced by Task 3 (`7a483ac`) and is left open.

Controller rulings (R1-R11) are listed in the SDD ledger and will be carried into the Task 7 report.
