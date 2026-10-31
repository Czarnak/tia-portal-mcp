# B-16/Q3 worker Portal-loss scoped live verification

Date: 2026-10-20 (live runs 2026-10-19 and 2026-10-20). Verdict: **accepted by the maintainer on 2026-10-20 as finished within the documented scope, with untested cases retained**.
Native write interruption, dedicated native inventory-fault injection and the other limits below remain offline-only evidence.

## Behavior under test

`NonRecoverableException` maps to `portal_lost` across worker domains. An escaping `EngineeringException` maps to `portal_lost` only when the session observed unexpected disposal of the current Portal attachment during that execution. The host then invalidates the verified binding and cursors without replay. Current behavior is summarized in `AGENTS.md` (Portal loss).

## Candidates and outcomes

| Candidate | Scope | Outcome |
| --- | --- | --- |
| `08f4d4a` (initial) | Network and PLC interruptions on a disposable standalone fixture | Failed live gate. Interrupted PLC and Network reads, including a manual Portal close, returned `worker_operation_failed` with a disposal warning while the worker survived; a cached tree continuation was still served before a later native read invalidated binding; a force-stop attempt missed the read window; direct `NonRecoverableException` to `portal_lost` was not observed. This exposed the disposal-aware mapping gap fixed by the follow-up. |
| `e804e6f`, installed `4.1.1-local.14.ge804e6f` | Network, PLC, VCI and local-session read interruptions | Passed (see below); HMI failed. |
| `2fb5e9a`, installed `4.1.1-local.19.g2fb5e9a` | Standalone HMI read interruption and recovery | Passed. |

## `e804e6f` Network interruption

The maintainer reopened disposable standalone Fixture A and closed Portal through the UI during one large read. One `list_network_objects` operation requested all six kinds with `pageSize:1`; the scan ran 12.931 seconds. The item failed with `portal_lost`, a disposed native enumerator diagnostic and the attached-Portal disposal warning, and the worker survived. The first TIA call afterwards rejected the previously healthy tree cursor with `cursor_binding_mismatch`, and the next call rejected the hardware cursor. Status then refused with `binding_conflict` carrying the Portal-loss reason. Host and worker PIDs and creation times survived and the AP21 SHA-256 matched baseline.

## `e804e6f` remaining read cases

Four actual read interruptions were performed with exact PID, creation time, executable and window-title checks before each stop (a first PLC stop was refused by an incorrect UTC comparison and terminated nothing).

| Case | Result |
| --- | --- |
| PLC interruption | Passed: `portal_lost` with disposal warning; the first following call rejected the saved tree cursor with `cursor_binding_mismatch`. |
| VCI interruption | Passed: a pending one-row `list_mappings(withStatus:true)` over a 676-mapping workspace returned `portal_lost`; the first following call rejected the hardware cursor. |
| Local-session content interruption | Passed: a 50-item PLC-read batch completed four items, returned one `portal_lost`, then 45 `binding_conflict` refusals; the next call rejected the tree cursor. |
| Standalone and local-session recovery | Passed: reopen, explicit bind, healthy reads; old cursors stayed invalid. The local session reported `isModified:true` immediately on first open; nothing was saved or checked in. |
| HMI interruption | **Failed:** `worker_operation_failed` with a disposed-device-item diagnostic; the first HMI continuation returned `binding_conflict` rather than immediate cursor rejection. |
| HMI after Portal recovery | **Failed twice** on fresh listings with a disposed `Siemens.Engineering.Project`; an explicit worker restart restored the 97-tag listing (workaround, not a fix). |

Two HMI defects followed: strict discovery in `HmiSoftwareLocator.WalkItems` wrapped the original engineering exception in `worker_operation_failed`, and `HmiSoftwareLocator.FindUnique` compared a possibly disposed cached project with `object.Equals`. Offline fixes were commits `f916a91` (preserve the original exception) and `0925f67` (evict on a different managed root handle without native equality), qualified by 7,086/7,086 passing tests plus serial stub and installed-V21-reference builds. A separate local-session hardware pagination limitation (6,559-character cursor over the fixed 4,096-character limit) was observed and is not part of this scope.

## `2fb5e9a` HMI read interruption

One guarded force-stop targeted only the observed Portal process (executable, creation time and exact window title checked) while a fresh `list_tags(limit:2000)` was in flight. No worker or host was stopped; no write, save or compile was requested.

| Check | Observed result |
| --- | --- |
| Healthy baseline | Verified binding, unmodified project, 97 Unified tags; HMI and tree continuations (1,045 nodes) succeeded. |
| Interrupted HMI read | `portal_lost` with a disposed `DeviceItemComposition`/remoting diagnostic and the Portal-disposed warning; one failed item, no omission. |
| First call after loss | The saved HMI continuation immediately returned `cursor_binding_mismatch`; the saved tree cursor did too. Subsequent inventory showed the binding invalidated. |
| Recovery | Portal restarted, the same AP21 reopened and explicitly bound; the project was unmodified. |
| Fresh HMI access | Two fresh `list_tags(limit:1)` reads each reported 97 tags; a recovered continuation succeeded at offset 1; the old pre-loss cursor stayed rejected. |
| Continuity | Host and worker PIDs and creation times matched before and after; no worker-restart workaround. |
| Restoration | Final status open and unmodified; AP21 SHA-256 matched before, after interruption and after recovery. |

The recovery batch mixed two successful fresh operations with one intentional rejection of the old cursor, so its root `success:false` is expected.

## Acceptance and evidence limits

On 2026-10-20 the maintainer accepted B-16/Q3 live testing as finished within this scope: the candidate-specific Network, PLC, VCI and local-session read checks on `e804e6f`, the final HMI loss, cursor and recovery retest on `2fb5e9a`, restoration evidence and the offline qualification of both HMI fixes. The following remain untested or unproved and do not block it:

- Native write interruption and partial or post-mutation outcomes (offline coverage only).
- Dedicated native inventory-fault injection and every HMI failure location or interruption timing.
- The exact native CLR exception type and throwing line, and native IPC dispatch or replay counts (no CLR trace or request log was captured).
- Complete project-directory preservation (file hashes cover the named fixture files only).
- Cross-candidate breadth: earlier domain observations are tied to their candidates and fixtures and were not all rerun on `2fb5e9a`.

Raw paths, cursors, process identities, hashes and canonical responses are retained only in ignored local evidence under `TestResults/portal-loss/`. This record was promoted from the B-16/Q3 implementation plan before that plan was compacted.
