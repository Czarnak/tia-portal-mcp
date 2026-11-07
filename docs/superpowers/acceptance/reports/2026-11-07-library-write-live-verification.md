# library_write — live verification, 2026-11-07

**Verdict:** scoped PASS. All 12 operations, the read-write lifecycle prompt (including decline), full mode, the
four guards that the fixture could trigger, the native refusals the plan names (M1, M5) and the local-session
capability split behaved as specified, on one standalone project, one user global library and one local session.
Rename postconditions, close-after-save failures, unverifiable state, contents truncation, non-block PLC kinds,
nested PLC folders, non-user global libraries and Portal loss were not exercised and remain offline-only.

## Provenance and scope

The installed global tool `tiamcpserver 4.3.0-local.64.ge1a6daa8` was used, installed from candidate `e1a6daa8`
(branch `feat/library-write`) with `scripts/install-local-tool.ps1`, built against the installed V21 reference
assemblies. Claude Code was the stdio MCP client; it launched the host with `--access-mode read-write` for every
case except FM, which used `--access-mode full`. Default value budgets applied. TIA Portal V21 executable build
`2100.0.121.1`, English UI.

| Artifact | SHA-256 |
| --- | --- |
| Host `TiaMcpServer.dll` | `FCAFCDE59DF1F1E4997F1F8ECC0DF3B06B3D57117DADFFABDB56EA02B08949BC` |
| Worker `TiaMcpServer.OpennessWorker.exe` | `C46204D40D462A2DB46B1F8A560BB405ACE37EF141D2412E18020CF22143C7C6` |

Before the run, the candidate passed the offline gate: stub build and real-V21 build succeeded,
`Category!=Packaging` 8,121 passed / 0 failed, `Category=Packaging` 39 passed / 0 failed.

Fixtures (identities and paths are not published here; the raw MCP traces are not retained):
- **P1**: one disposable standalone project with one S7-1500 PLC, open in Portal A.
- **L1**: one disposable user global library, closed at the start.
- **S1**: one disposable local session, opened by the maintainer in a second Portal B.
- Scratch directories outside both fixtures for archive, retrieve and create output.

Test objects were named `LW_*`. Every case ran through `library_write`; `plc_write` created and deleted the
scratch FC, and `library_read`, `plc_read`, `browse_project_tree` and the audit file were used for read-back.

## Verdicts

| ID | Observed result | Verdict |
| --- | --- | --- |
| B1 | Unbound `library_write` (dry run) → whole-call `binding_conflict`, phase `error`. | PASS |
| B2 | `bind_project` on P1 succeeded; later calls used Portal A. | PASS |
| O1 | `open_global` L1 `readWrite`: the dry run previewed `alreadyOpen:false` with no prompt and audit `none`; the actual call prompted once, opened L1 and audited `user/confirmed`. | PASS |
| C1 | One content-only call with 5 items: `create_folder` in L1 (master copies and types) and in the project library, and `create_master_copy` from the scratch FC into L1 and into the project library. All succeeded with no prompt and audit `none/not_requested`. The L1 item named the source `lw_fc`; the PLC lookup matched case-insensitively and the master copy took the block's native name `LW_FC`. | PASS |
| M1 | `create_master_copy` into a folder that already held `LW_FC` → `library_master_copy_exists` block guard, phase `blocked`, nothing created. | PASS |
| M1c | Review Focus: block renamed to `lw_fc`; its `create_master_copy` next to the existing `LW_FC` master copy → the same block guard in preview. | PASS |
| CP | `copy_master_copy` global → project folder and project → global folder: both succeeded with `actualName:"LW_FC"`. | PASS |
| M5 | `instantiate_master_copy` into PLC_1's root block group while block `LW_FC` existed → `engineering_rejected`, `engineeringError.type:"EngineeringTargetInvocationException"`, Portal message "Copying from the library failed." | PASS |
| I1 | After `plc_write` deleted the block, `instantiate_master_copy` (`plcKind:"blockGroup"`, root) succeeded; `plc_read` showed `FUNCTION "LW_FC"` in PLC_1. The same call's `delete_master_copy` on L1 succeeded. | PASS |
| F5 | `delete_folder` preview on a non-empty L1 folder listed its master copy in `folderContents` (`contentsTruncated:false`) with the `library_deletes_folder_contents` info guard. The actual call deleted that folder, an empty L1 types folder and a non-empty project-library folder, with no prompt. | PASS |
| G9 | Review Focus mixed call: `create_folder` plus `close_global discard` on L1 prompted once, audited `user/confirmed`, raised the `library_discards_changes` info guard and returned `saved:false`; the folder item was `superseded` by the close. After re-opening L1 the folder was absent. | PASS |
| DE | Review Focus decline: `save_global` with the prompt declined → `access_denied`, phase `blocked`, no worker mutation, audit `user/declined`. | PASS |
| G13 | One call with `save_global` (`saved:true`), `archive_global` with name `LW_Archive` and no extension, and `open_global` on L1's already-open path. The archive path was `…\LW_Archive.zal21` and the directory held exactly that one file. The re-open reported `alreadyOpen:true` (P9; Portal accepted the same-mode open). One prompt for the call. | PASS |
| G6 | `retrieve_global` of that archive (`readOnly`) and `create_global` in one call: both reported the actual library path Portal chose, `<directory>\<name>\<name>.al21`, not the requested directory. | PASS |
| SEL | Review Focus: with L1 and its retrieved copy both open under one name, a name-only selector → `target_ambiguous`; the path selector resolved the copy. | PASS |
| G8 | Review Focus: `close_global save` on the clean read-only retrieved copy → success, `saved:false` (no `Save()` attempted). The new library's clean `close_global save` also returned `saved:false`. | PASS |
| CS | `create_folder` plus `close_global save` on modified L1 → `saved:true`. In Portal B, L1 showed that folder, so the save persisted. | PASS |
| LS | Bound to S1 in Portal B (`containerKind:"localSession"`; `library_project_write` advertised as standalone-only). A global `open_global` worked. A call mixing a global `create_folder` and a project-library `create_folder` → whole-call `unsupported_capability` naming `library_project_create_folder`, with the global folder not created. A global-only `delete_folder` then succeeded. | PASS |
| FM | Full mode, re-bound to S1: `close_global save` on modified L1 ran without a prompt, `saved:true`, audit `policy/not_requested`. | PASS |

Audit totals for `library_write` in read-write mode: 8 applied calls `user/confirmed` (exactly the 8 lifecycle
calls), 1 blocked `user/declined`, and 7 applied content-only calls, 4 previews, 1 guard block and 3 refusals, all
`none/not_requested`. Every call wrote exactly one audit record.

## Fixture state after the run

P1 holds no `LW_*` objects; its scratch block and master copies were deleted, and the project was left unsaved with
no net change. L1 was saved with no net content change and closed. The scratch archive, retrieved copy and new
library remain in the scratch directories outside the repository.

## Limits

Not exercised live, so only the offline tests stand behind them:
- A native master-copy rename (`postcondition_failed` with `actualName`); Portal never renamed here.
- `Close` throwing after a save (`saved:true` outcome on failure).
- `library_state_unverifiable`, and a preview over 200 entries (`contentsTruncated`).
- `plcType`/`tagTable` sources, `typeGroup`/`tagTableGroup` destinations, and nested `plcFolderPath`.
- A types folder whose types have dependents (F6), and `save`/`close`/`archive` on a non-user global library (P5).
- An `open_global` mode change on an already-open library (G4).
- Portal loss during a library write.
- The prompt count rests on the audit (one confirmation record per call) and on what the maintainer observed in the client.
