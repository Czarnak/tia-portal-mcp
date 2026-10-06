# plc_write (PR B) validation — 2026-10-07

Result: **PASS with one live-found defect fixed and recorded follow-ups.** Every step of the spec §6.2 step 6 matrix behaved as specified in read-write, and one call was repeated in full. Live testing exposed a duplicated guard on dependent items in the shared write pipeline. It was fixed (`9886a23`), re-tested offline, reinstalled and confirmed live.

Frozen candidate: `9886a23` (branch `refactor/plc-write`), installed locally as `tia-mcp` `3.0.1-local.339.g9886a23`. The package was built against the installed V21 assemblies and contains no Siemens DLLs. The read-write matrix ran on the earlier install from `0e5358d`. The only change between `0e5358d` and `9886a23` is the pipeline fix, which was re-verified live on the frozen install.

## Offline qualification

- **At `6815685`, before the final-review fixes:**
  - The real-V21 build had 0 errors and 6 warnings.
  - The stub build had 0 errors and 5 warnings.
  - 5,084 tests passed.
  - Line coverage was 93.33%.
  - The reference stubs were current.
- **At `9886a23`, the frozen candidate:**
  - The stub build had 0 errors and 4 warnings. The 4 warnings are the existing `BlockMutationService` CS8602 warnings, also present on `main`.
  - 5,089 tests passed, run serially.
  - Scoped line coverage was 93.42%, above the 80% threshold.
  - `verify-reference-stubs.ps1` reported both artifacts current.
  - The local install built against the real assemblies.
- **Reviews:**
  - Each task was reviewed for spec compliance and quality.
  - An independent whole-branch review found 1 Important and 6 Minor findings. The Important finding, plus M1, M3 and M6, was fixed in `6815685..0e5358d`, and the re-review addressed all of them.
  - M2, M4 and M5 went to the follow-ups below.

## Authorization and target

The maintainer authorized writes against a disposable copy of a test project, `SimpleProject_copy.ap21`, which was already open in TIA Portal V21.

- Binding used `bind_project` with the exact open path, giving a verified binding to one Portal process. Nothing was opened, saved, compiled on purpose, or closed.
- The project has three PLCs: `PLC_LAD`, `PLC_FBD` and `PLC_DP`. `PLC_LAD` has several block folders, a software unit (`Test_SU`) and four tag tables.
- The live tool list had 15 tools, including `plc_write` and `plc_read`, and no batch tools. That matches the 6 read-only / 15 read-write / 15 full tool sets.

## Results — read-write (`--access-mode read-write`)

| Check | Observed result |
| --- | --- |
| `create_tag_table` and `create_tag` in that table, one call, `dryRun:true` | `preview`, with the tag's effect carrying `dependsOn:"tbl"` and placement `/MCP_Live`. Nothing was written. |
| The same call, executed | `applied`. Verification: the table `exists`, and the tag `values` read back as `dataType=Bool; logicalAddress=%M50.0`. |
| `create_tag` named `FirstScan`, which exists in the default table | `blocked`, `plc_name_collision`, naming the colliding tag and table. Nothing ran. |
| `create_tag` on the already-used `%M50.0` | `plc_address_overlap` at info severity plus a warning, by design. In the same call, the collision above blocked everything. |
| `create_block` over the existing `PLC_LAD/Main` | `blocked`, `plc_block_exists`. |
| `create_block` with `PLC/Group/Block`, which leaves out `Blocks/` | Rejected with `validation_error` naming the accepted path forms. |
| `create_block_group` plus FC (SCL) and global DB inside it, one call | `applied`, both blocks `dependsOn` the group, and all three verified `exists`. |
| `update_block_logic` without `expectedContentHash` | Rejected with `validation_error`, "missing required field(s): expectedContentHash". |
| `update_block_logic`, `dryRun:true`, valid hash | `preview` with a bounded `contentDiff`: excerpts, changed-line counts, and sha256 of both sides. |
| Edit made in the TIA Portal UI after `plc_read`, then `update_block_logic` with the old hash | Rejected with `state_changed`. A re-read showed the UI edit intact and none of the requested content. |
| Re-read, then update with the fresh hash | `applied`. Import and compile succeeded, and the `contentHash` verified. |
| `delete_tag_table`, `dryRun:true`, then executed | `removes` reported `tagCount:1, userConstantCount:0`, with info guard `plc_deletes_table_contents`. Applied, then verified `absent`. |
| `delete_tag_table` on a default tag table, `dryRun:true` | `plc_default_tag_table` at block severity. |
| `delete_block_group`, `dryRun:true`, then executed | `removes.blocks` listed both blocks, with info guard `plc_deletes_group_contents`. Applied, then verified `absent`. |
| Prompts | None on any call. |
| Audit | 16 `plc_write` calls produced 16 audit v2 records, phases in the same order: 6 `preview`, 5 `applied`, 2 `blocked`, 3 `error`. Every record has `accessMode:"read-write"` and `confirmation {by:"none", outcome:"not_requested"}`. |

## Results — full (`--access-mode full`)

| Check | Observed result |
| --- | --- |
| `create_tag_table`, `create_tag`, `delete_tag_table` on the same table, one call | `applied` with no prompt. Verification marks the create steps `superseded` by the delete, and the delete verifies `absent`. |
| Audit | One record per call, with `accessMode:"full"` and `confirmation {by:"policy", outcome:"not_requested"}`. |
| **Defect found** | `plc_deletes_table_contents` appeared **twice** in `guards[]` and in the audit record for the dependent item. |
| After the fix, on the reinstalled `9886a23` | The same call shape reports the guard once, in both the response and the audit record. |

### Defect: repeated guard on a re-planned dependent item

`WriteRun` adds every guard from the planning pass. Before a dependent item mutates, the pipeline re-plans it and re-evaluates that item's guards, and it appended them again. PLC is the first domain that plans a dependent item's effect up front, so its guards fire in both passes. The code dates from the pipeline's introduction (`028108b`) and is on `main`.

The fix, `9886a23`, skips a late guard identical (same id, operation ID and message) to one already reported. A genuinely new late consequence is still reported and still decides the item. A new test, `DependentItemGuard_FiredAtPlanAndReplan_IsReportedOnce`, failed before the fix and passes after it.

## Deviations and follow-ups

- **False name collision with software-unit blocks (M2, confirmed live).** `create_tag` named `SU_DB` is blocked as a collision with `PLC_LAD/Units/Test_SU/Blocks/SU_DB`. The maintainer confirmed this is a false collision: the unit's DB is not published and cannot be referenced outside the unit. It fails closed. The follow-up is to exclude unpublished software-unit blocks from the CPU namespace.
- **Result naming differs from the effect (cosmetic).** The `create_block` result echoes `blockType:"GLOBALDB"`, and the `create_block_group` result gives `PLC_LAD/MCP_LiveGroup`. The effect uses `GlobalDB` and `PLC_LAD/Blocks/MCP_LiveGroup`.
- **`removes.blocks` order differs between dry run and apply (cosmetic).** The order follows Openness enumeration, which changed after the FC was re-imported.
- **Compile messages are redacted.** `update_block_logic` compile messages show the worker's existing fixed text, "Compiler diagnostic omitted.". This is not a regression from this branch.
- **Not exercised live:** `isSafety` (M4), `compile_check` on grouped PLCs (M5), and user-constant operations.
