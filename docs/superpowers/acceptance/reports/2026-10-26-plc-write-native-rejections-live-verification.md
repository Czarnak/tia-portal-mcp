# plc_write native rejections live verification

Date: 2026-10-26. Verdict: **PASS for the executed installed-candidate scope on a disposable standalone project copy**, with two deviations (L2 same-folder only, L6 not reachable) and one live defect (L7) that was fixed and re-verified.
Branch `refactor/plc-write-native-rejections`, plan: [PLC write native rejections plan](../../plans/2026-10-25-plc-write-native-rejections.md).
Nothing here is plant, hardware or online-PLC evidence, and nothing was saved: the project copy was bound with `bind_project` and never saved or closed.

## Candidates and mode

| Run | Installed build | Source | Purpose |
| --- | --- | --- | --- |
| Baseline | `4.2.0-local.33.g02ffe5a` | `main` at `02ffe5a`, the branch base | Timing before the change |
| 6a | `4.2.0-local.44.g7c9cf92` | Task 5 qualified candidate | L1-L11, timing Sets A and B |
| 6c | `4.2.0-local.48.g7445c5f` | `7445c5f` (adds the rename read-back and fixtures) | L7 re-run, hash probe, L1 regression, timing Set C |

All runs: `--access-mode full` (audit `accessMode:"full"`), `contractVersion:"2.0"` confirmed on the first `plc_write` of 6a and 6c, Claude Code as the MCP client, TIA Portal V21, Portal UI language English. The project is a disposable copy, recorded as `<disposable-copy>`; the target was `PLC_1`.

Measured project size: `PLC_1` (S7-1500) holds 1 block (`Main`, OB1), 2 tag tables (`Default tag table` and one table whose name contains a slash), 2 tags and 0 user constants. The project has 4 PLC software objects and 111 top-level tree nodes (about 100 devices); the other three PLCs were counted at folder level only (1 to 11 direct children in the program-blocks folder, 1 to 6 tables), because their tag totals would flood the response budget. The measured PLC is the small one, so the fixed cost in the timing is not a large-table effect.

## Offline gates (before the live runs)

- Default suite `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`: 7715 passed, 0 failed at `7445c5f`.
- Stub build: 0 errors, 7 warnings, all present on the base `534937a` (a nullable warning introduced mid-branch was fixed in `bac9121`). The full-solution build against the installed TIA V21 assemblies also ended with 0 errors in Task 5, with the same warnings.
- Production diff `534937a..HEAD` before Task 7: 492 lines removed, 270 added (largest: worker `PlcWritePreconditions.cs` -209/+8, `PlcWriteVerifier.cs` -101/+37, `PlcWorkingState.cs` -74/+64, `PlcNameRules.cs` -34/+2). Tests: -260/+546 lines plus +576 lines in the regenerated `output-schemas.json` snapshot.
- Packaging tests were not run: no product `.csproj`, `Directory.Build.props`, `global.json` or `reference-stubs/` file changed.
- Fourteen CLI startup tests (`RemovedOptionTests`, `WorkerTimeoutStartupTests`) failed once under parallel load and passed on rerun. They have no `[Collection]` and the flakiness predates the branch.

## What the live run established

### Native rejections (L1-L11, build `g7c9cf92`)

| Case | Verdict | Result |
| --- | --- | --- |
| L1 `create_tag` existing name | PASS | Item `engineering_rejected`, `EngineeringTargetInvocationException`, project unchanged |
| L2 `create_tag_table` existing name | DEVIATION | `engineering_rejected`; same folder only, because the tool cannot create tag-table folders |
| L3 `create_block` existing block | PASS | `engineering_rejected` from `PlcBlockComposition.Import`; content hash identical before and after |
| L4 `create_block_group` existing group | PASS | `engineering_rejected`; group content unchanged |
| L5 delete the default tag table | PASS | `engineering_rejected` ("not valid because of the current status of object"); table list unchanged |
| L6 second singleton OB | DEVIATION | Blocked by `plc_ob_singleton_exists`; Portal was never reached, so the guard stays (R13) |
| L7 `update_tag` rename to a colliding name | FAIL, then fixed | Portal silently ignored the rename and the item was reported `succeeded` / `trusted`; see below |
| L8 happy path | PASS | Create, update and delete calls applied; `trusted` and `superseded` checks; info guards fired with counts |
| L9 stale `expectedContentHash` | PASS | `state_changed` at the call level before any import |
| L10 item 2 of 3 collides | PASS | Item 1 `succeeded`, item 2 `engineering_rejected`, item 3 `skipped`; `success:false`, `error:null`, MCP `isError:false`; one `partial_write_no_rollback` info guard |
| L11 `dryRun` of the L10 call | PASS | `preview`, no collision planned, no mutation |

In every `engineering_rejected` capture the `type` is `EngineeringTargetInvocationException`, `inner` is `[]` and the message is English with a `\r\n\r\n` between the "Error when calling method ..." line and the reason. Full captures, including every `engineeringError` object verbatim: [live cases](inputs/2026-10-26-plc-write-native-rejections-live-cases.md).

### L7 finding and re-verification (build `g7445c5f`)

TIA Portal V21 does not throw when `update_tag` renames to a name another tag in the table holds. It silently keeps the old name, and with the host collision guard gone and verification reduced to `trusted`, the item was reported `succeeded`. The fix (R14, commit `b32fdc1`) applies the rename first in the worker, reads `Name` back and fails the item as `postcondition_failed` before any other property changes. Re-run on 6c:

- `update_tag` of `ZZ_T6C_TagA` with `newName=ZZ_T6C_TagB` and `dataType:Real`: item `failed`, `postcondition_failed`, "Portal did not rename tag ... No other property was changed.", root `success:false`, `error:null`, phase `applied`, `isError:false`.
- `plc_read` afterwards: tag A still exists with its original type (Int, not Real); tag B unchanged.
- Positive control, rename to an unused name: applied, type preserved.

### Hash probe (build `g7445c5f`)

On a freshly created FC: the pre-write read, the verification hash of an unchanged write-back and both post-write reads were all `8603bd61...0de9`. A second `update_block_logic` that used the first verification hash as `expectedContentHash` and changed the Title was applied (not `state_changed`); its verification hash `60900034...2b7f` equals the next `plc_read` hash. So the verification `contentHash` is stable against the next read and is usable as the next `expectedContentHash`. A third chained write from `60900034...` was not run (the block was then deleted).

The hash cannot be computed from the submitted content: four identical `update_block_logic` writes of the same content in the timing runs returned four different verification hashes, because the export gains a generated `S7_BlockTitle` id and a `.s7res` part when a Title is set. The code path that computes it predates the branch (planning, verification and `plc_read` hash the same worker export), so the variability is not a regression of this change.

### L1 regression (build `g7445c5f`)

`create_tag` of an existing name: identical to 6a, `engineering_rejected`, project unchanged.

### Review-focus conditions

| Condition | Evidence |
| --- | --- |
| Siemens rejects item N of M | Live (L10), plus offline MCP tests that pin the exact captured document |
| Stale `expectedContentHash` | Live (L9) |
| `dryRun:true` | Live (L11) |
| Siemens throws during a planning read | Offline only (`WorkerExecutionTests`: a target-invocation error on a read method stays `worker_operation_failed`) |
| Portal disposed during the item | Offline only (`portal_lost` wins in `WorkerExecutionTests`) |
| Block guard in a dry run | Behavior unchanged by this plan; not re-exercised as a separate case |

## Timing

Protocol: [baseline](inputs/2026-10-25-plc-write-timing-before.md) replayed verbatim on the candidate: fixed object names, strictly sequential calls T5 then T1, T2, an untimed `plc_read`, T3, T4, untimed cleanup; one discarded warm-up, then three measured runs; medians per column. Source: audit v2 `durationMs` (root) and `items[].durationMs` (item sum); remainder is root minus item sum (planning, verification, audit, binding lease). The MCP Inspector wall-clock column was not measured (R7: the client was Claude Code). Raw tables: [Sets A and B](inputs/2026-10-26-plc-write-timing-after.md), [Set C](inputs/2026-10-26-plc-write-live-closeout.md).

Set C (build `g7445c5f`) is the primary before/after comparison (R18): it is the only run that is both protocol-complete and quiet (the no-Siemens T5 dry run stayed flat at 6.1 to 6.3 s). Set A followed the protocol on a visibly noisy machine: even T5, which has no Siemens mutation, took about twice as long. Set B was a three-run replicate without warm-up, taken after the L-case session on the same worker; it is off-protocol. Both are shown as context only.

Milliseconds, medians (root / item sum / remainder):

| Case | Call | Before | Set A (noisy) | Set B (off-protocol) | Set C (primary) |
| --- | --- | --- | --- | --- | --- |
| T1 | 1 `create_tag` | 17,826 / 5,201 / 12,673 | 21,219 / 9,877 / 11,342 | 13,212 / 6,066 / 7,312 | 11,442 / 5,178 / 6,264 |
| T2 | 10 items (table, 5 tags, 2 constants, group, block) | 137,669 / 124,185 / 13,484 | 185,284 / 173,932 / 11,132 | 128,087 / 120,818 / 7,730 | 119,732 / 112,428 / 6,437 |
| T3 | 1 `update_block_logic` | 54,933 / 37,318 / 18,356 | 73,383 / 48,464 / 22,220 | 63,814 / 43,407 / 21,166 | 55,378 / 38,335 / 17,073 |
| T4 | 1 `delete_block_group` | 18,814 / 5,325 / 13,718 | 19,336 / 8,116 / 11,220 | 13,516 / 6,197 / 8,069 | 12,017 / 5,472 / 6,545 |
| T5 | T2 with `dryRun:true` | 5,975 / 0 / 5,975 | 11,490 / 0 / 11,490 | 6,151 / 0 / 6,151 | 6,308 / 0 / 6,308 |

Set C against before:

| Case | Root | Item sum | Remainder |
| --- | --- | --- | --- |
| T1 | -36% | 0% | -51% |
| T2 | -13% | -9% | -52% |
| T3 | +1% | +3% | -7% |
| T4 | -36% | +3% | -52% |
| T5 | +6% | n/a | +6% |

Reading: the planning-plus-verification remainder roughly halves for T1, T2 and T4 (-51 to -52%), which is where the removed tag inventory and project-tree reads and the post-write re-reads were. Item sums, which are Siemens time, stay within -9 to +3%. T3, the content path, is unchanged (remainder -7%, root +1%): its export, hash and diff stay. Root time falls 36% for T1 and T4 and 13% for T2, because T2 is dominated by ten sequential Siemens mutations. T5 (planning of ten creates) is flat. Run 3 was 10 to 20% slower than runs 1 and 2 on T2, T3 and T4 in the baseline and in Set C alike; medians absorb it. No claim of speed-up beyond Set C is made.

Deviations from the protocol (Set C): the untimed T3 pre-read was done once, in the warm-up, and its hash (identical to the baseline) was reused (a stale hash would have returned `state_changed`; none did); the L7, hash-probe and L1 calls ran on the same worker immediately before, so the warm-up was not a cold start. In 6a Set A, T3 and T4 of run 1 were sent in one client batch; the host serialized them, so those figures are valid.

## Decisions taken during execution

Rulings by the controller, in order. Each was provisional against the plan's "Approved scope decisions" and "Review focus".

| Ruling | Decision | Why |
| --- | --- | --- |
| R1 | `engineering_rejected` only for mutation worker methods; reads keep `worker_operation_failed` | The plan's review focus says a planning read never produces it; the shared catch needs a mutation flag from the existing method catalog |
| R2 | Keep `dependsOn` marking, the in-call identity overlay and replans of dependent items; remove only collision and existence evaluation | Updating or deleting an object created earlier in the call, and delete-consequence guards, need it |
| R3 | `engineeringError` copied once in the shared item-failure projection, no Network, HMI or VCI guard or verification change | The plan's follow-ups section sanctions the shared seam |
| R4 | Baseline measured on `02ffe5a`, the branch base, not `7c04b44` | Like-for-like with the candidate |
| R5 | Timing and live inputs under `docs/superpowers/acceptance/reports/inputs/`; project names and paths redacted | The folder did not exist; the repository forbids project names |
| R6 | One-line conventional commits, no push during execution | User preference and the remote-write approval rule outrank the plan text |
| R7 | No Inspector wall-clock column | The client was Claude Code; audit figures are the primary metric |
| R8 | L6 tests a second OB of a singleton class | `create_block` picks the number, so a used number cannot be requested |
| R9 | `create_block` reads the project tree only for an OB | `plc_ob_singleton_exists` stays and needs the OB inventory |
| R10 | The execution record is a section appended to the plan | The plan named a record but no location |
| R11 | Task 5 also builds the worker against the real V21 assemblies | Catches stub/real drift before the live install |
| R12 | Task 6 split: 6a live run (timing first, L5 last), 6b fixtures and commit, 6c closing run | Keeps the timing replay like-for-like and avoids git races |
| R13 | `plc_ob_singleton_exists` stays | Portal's rejection of a duplicate singleton OB was not shown live; the plan retires it only on live proof |
| R14 | L7 fix: rename first, read `Name` back, fail `postcondition_failed` before other properties; no host collision guard returns | A non-throwing call is not proof; the adjacent read-back also catches other silent ignores without partial mutation |
| R15 | Investigate the `contentHash` variability; fix only if this branch caused it | It predates the branch (same export path, same hash function), so it is documented, not fixed (one fix per PR) |
| R16 | Report both timing sets honestly | Set A was noisy and Set B off-protocol |
| R17 | 6c re-runs the full timing protocol as Set C with no other session activity | More evidence beats softer wording |
| R18 | Set C is the primary comparison; A and B are context; the 6c evidence is committed as an inputs file | It is protocol-complete and quiet |

## Evidence limits

- **L2** covered same-folder duplicates only: `plc_write` cannot create tag-table folders and the project has none.
- **L6** was not reachable through `plc_write`: Portal's own response to a duplicate singleton OB is unknown, so the guard stays.
- **Unsupported-flag failure.** An `update_tag` that requests an external-access flag the tag cannot take fails mid-update, after a rename or data-type change in the same item has applied (no rollback). It was identified in review, not exercised live.
- **Create into a missing parent** group or tag-table folder now fails at mutation instead of in pre-flight; this and the `target_not_found` of a later delete of a never-placed object are offline-tested only.
- **Other write tools.** `engineering_rejected` was exercised live for `plc_write` only. The shared projection can carry it for `network_write`; `hmi_write` and `vci_write` wrap Siemens exceptions first and do not in practice. The PLC content importers (`update_block_logic`, `update_type_content`) still flatten import failures to `worker_operation_failed`; `WorkerOperationException.EngineeringError` exists but has no producer yet.
- **Bounds.** `engineeringError.message` is cut at 4,000 characters and `inner` at 8 entries, silently, so "verbatim" holds only below those bounds.
- **Hash chain.** The hash probe chained one verification hash into one next write; a longer chain was not run.
- **Language.** All messages were captured with an English Portal UI.
- The 14 CLI startup tests that fail under parallel load are a pre-existing flake, not part of this change.
- Offline gates and the live runs establish the executed scope only; they do not establish unexecuted native, plant or hardware behavior.
