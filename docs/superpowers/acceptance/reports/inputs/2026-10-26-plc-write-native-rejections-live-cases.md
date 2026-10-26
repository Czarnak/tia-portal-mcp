# plc_write native rejections: live cases L1-L11 (2026-10-26)

Build `4.2.0-local.44.g7c9cf92`, `--access-mode full`, plc_write `contractVersion:"2.0"`, PLC `PLC_1` of `<disposable-copy>` (bound, never saved). L cases use `ZZ_T6_` objects. Session order followed R12: timing first (`timing-after.md`), then L cases, L5 last.

Portal UI language: English. Every captured Siemens message is English, with a `\r\n\r\n` between the "Error when calling method ..." line and the reason; `inner` is `[]` in every capture; `type` is `EngineeringTargetInvocationException` in every `engineering_rejected`.

Setup call (`s1-01..05`): table `ZZ_T6_Tbl` with tags `ZZ_T6_TagA`, `ZZ_T6_TagB`; group `PLC_1/Blocks/ZZ_T6_Grp` with FC `ZZ_T6_FC1` (xml hash `xml:sha256:f5872c46092f95f70929aa690515a06c44f0926bc8678db8a5ff2a9a90d0bc6e`). All applied, `trusted`.

| Case | Verdict | Category | engineeringError.type | Source capture |
| --- | --- | --- | --- | --- |
| L1 | PASS | engineering_rejected | EngineeringTargetInvocationException | L01.json |
| L2 | DEVIATION (same folder only) | engineering_rejected | EngineeringTargetInvocationException | L02.json |
| L3 | PASS | engineering_rejected | EngineeringTargetInvocationException | L03.json |
| L4 | PASS | engineering_rejected | EngineeringTargetInvocationException | L04.json |
| L5 | PASS | engineering_rejected | EngineeringTargetInvocationException | L05.json |
| L6 | DEVIATION (Portal never reached; guard retained) | guard_blocked (root) | none | L06.json, L06-attempts.json |
| L7 | FAIL | none, item `succeeded` | none | L07.json, L07-retry-and-control.json |
| L8 | PASS | none | none | L08a.json, L08b.json |
| L9 | PASS | state_changed (root) | none | L09.json |
| L10 | PASS | engineering_rejected (item 2) | EngineeringTargetInvocationException | L10.json |
| L11 | PASS | none | none | L11.json |

## L1 create_tag with an existing name

- Call: `create_tag` `PLC_1` / `ZZ_T6_Tbl` / `ZZ_T6_TagA` Bool (exists).
- Expected: item `engineering_rejected`, project unchanged.
- Observed: `phase:"applied"`, `success:false`, `error:null`; item `failed`, category `engineering_rejected`, type `EngineeringTargetInvocationException`, message "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'. An attempt was made to create an object that already exists." Verification `operations:[]`, `success:true`. Audit item 7.5 s, root 16.9 s.
- Before/after: `plc_read list_tag_tables` before (A and B, Bool, same attributes) and after (L1-L4 and the first L7 call done; A and B unchanged, A listed once).
- PASS: matches the expectation.

## L2 create_tag_table with an existing name

- Call: `create_tag_table` `PLC_1` / `ZZ_T6_Tbl` (exists at the root).
- Expected: `engineering_rejected`; the brief says the existing table is in another folder.
- Observed: `engineering_rejected`, type `EngineeringTargetInvocationException`, message "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagTableComposition'. An attempt was made to create an object that already exists." Root 30.3 s.
- Before/after: table list before and after identical (one `ZZ_T6_Tbl`, same tags); `post-h` full listing later also showed exactly one.
- DEVIATION: the tool cannot create a tag-table folder and the project has none (all tables at `/`), so the collision is within the same folder, not "another folder". The cross-folder variant was not reachable; the same-folder collision behaves as expected.

## L3 create_block where the block exists

- Call: `create_block` `PLC_1/Blocks/ZZ_T6_Grp/ZZ_T6_FC1` FC LAD (exists).
- Expected: `engineering_rejected`, content hash equal before and after.
- Observed: `engineering_rejected`, type `EngineeringTargetInvocationException`, message "Error when calling method 'Import' of type 'Siemens.Engineering.SW.Blocks.PlcBlockComposition'. Cannot import object with Simatic ML ID '0' at line number 4 at line position 4.  The composition already contains an object with the same values for the identifier(s) 'Name, Namespace'." (create_block is implemented as an XML import, so the message mentions an import, a line and a position, not "create"). Root 43.1 s (item 16.1 s).
- Before/after: `get_block_content` hash before `xml:sha256:f5872c46092f95f70929aa690515a06c44f0926bc8678db8a5ff2a9a90d0bc6e`, after the same value.
- PASS.

## L4 create_block_group with an existing name

- Call: `create_block_group` `PLC_1/Blocks/ZZ_T6_Grp` (exists, holds `ZZ_T6_FC1`).
- Observed: `engineering_rejected`, type `EngineeringTargetInvocationException`, message "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Blocks.PlcBlockUserGroupComposition'. An attempt was made to create an object that already exists." Root 24.7 s.
- Before/after: the group still holds `ZZ_T6_FC1` with the same hash (read after L4); the final delete of the group (`z-01`) listed exactly `ZZ_T6_FC1`, `ZZ_T6_OB_A`, `ZZ_T6_OB_B`, so no duplicate group or lost content.
- PASS.

## L5 delete the default tag table (last case of the session)

- Call: `delete_tag_table` `PLC_1` / `Default tag table`.
- Observed: `engineering_rejected`, type `EngineeringTargetInvocationException`, message "Error when calling method 'Delete' of type 'Siemens.Engineering.SW.Tags.PlcTagTable'. The operation is not valid because of the current status of object 'Siemens.Engineering.SW.Tags.PlcTagTable'." The effect shows `removes` all zero (it was empty).
- Before/after: `list_tag_tables` for `PLC_1` before and after identical (`Default tag table`, `TagWith/Slash` with tags `assd`, `d`).
- PASS. The message does not say "default"; a caller sees only "not valid because of the current status".

## L6 create_block OB with a used number (R8: singleton OB)

- `create_block` picks the number itself, so a used number cannot be requested. Used event class `TimeErrorInterrupt` (always OB 80).
- Setup: first OB created (`ZZ_T6_OB_A`, number 80), applied.
- Call: second `create_block` OB `ZZ_T6_OB_B` `TimeErrorInterrupt`.
- Observed: `phase:"blocked"`, root `error.category:"guard_blocked"`, block guard `plc_ob_singleton_exists` ("OB number 80 of singleton event class 'TimeErrorInterrupt' is already held by 'PLC_1/Blocks/ZZ_T6_Grp/ZZ_T6_OB_A'; only one OB of that class can exist."). No mutation, no Portal call.
- Attempts to reach Portal with a duplicate singleton (see `L06-attempts.json`):
  1. Two `DiagnosticErrorInterrupt` OBs in one call (dry run): the guard also tracks earlier items of the same call (block guard on item 2).
  2. `update_block_logic` on a `ProgramCycle` OB (`ZZ_T6_OB_B`, number 123), content declaring Number 80 and SecondaryType `TimeErrorInterrupt`: failed with `validation_error` "Block import did not complete.", `importStage:"not_started"`, `targetMutationCommitted:false`, target hash unchanged. Rejected before any import call; no `engineeringError`.
  3. No other tool argument selects a number or class for an existing OB.
- Conclusion: no way found to reach Portal with a duplicate singleton through this tool, so Portal's own response is unknown. This run gives no basis to retire `plc_ob_singleton_exists`; keep it. A decision needs a probe outside plc_write (for example a direct Openness or TIA Scripting import of a second OB 80), or a decision to accept the guard as redundant-but-harmless.
- DEVIATION from the brief (cannot request a used number; guard retained as the brief says to do if Portal accepts, but here Portal was never asked).

## L7 update_tag renaming to a colliding name

- Call: `update_tag` `ZZ_T6_Tbl` / `ZZ_T6_TagB` with `newName:"ZZ_T6_TagA"` (exists).
- Expected: `engineering_rejected`, original tag unchanged.
- Observed: item `succeeded`, `success:true`, `phase:"applied"`, verification `check:"trusted"` ("the Openness call completed without rejection; nothing was read back"). No exception from Portal.
- Before/after: `plc_read` after shows `ZZ_T6_TagA` and `ZZ_T6_TagB` unchanged, so the rename did not happen. Retried once (`l7-02`): identical result. Positive control (`l7-ctl`, rename `TagB` to unused `ZZ_T6_TagC`): applied and `plc_read` shows `ZZ_T6_TagC`, so `update_tag` rename works and only the colliding rename is silently ignored by Portal.
- FAIL: the tool reports a successful update that did not happen. With the pre-check gone and verification reduced to `trusted`, nothing catches it. Suggest verifying `update_tag` renames by read-back (name equals `newName`), or keeping a collision check for `update_tag` renames.

## L8 happy path

- Call a (`L08a.json`): `create_tag_table` `ZZ_T6_H_Tbl`, `create_tag` `ZZ_T6_H_Tag`, `create_block_group` `ZZ_T6_H_Grp`, `create_block` `ZZ_T6_H_FC1`, `update_tag` rename to `ZZ_T6_H_TagR` plus `externalWritable:false`. Result `applied`, `success:true`, 5 succeeded; verification: `trusted` for table, group, block, update; `superseded` for the create_tag (later update). `plc_read` confirmed the table holds `ZZ_T6_H_TagR` with `externalWritable:false`.
- Call b (`L08b.json`): `delete_tag`, `delete_tag_table`, `delete_block`, `delete_block_group`. Result `applied`, `success:true`, 4 succeeded; `superseded`/`trusted` checks; one info guard `plc_deletes_block` on the block delete.
- PASS with a note: the table and group delete guards (`plc_deletes_table_contents`, `plc_deletes_group_contents`) did not fire in b because the table and group were already empty when deleted (tag and block removed first). They fired with counts in the cleanup call (`z-01`: group removes 3 blocks, `z-02`: table removes 2 tags) and in the timing T4 and cleanup calls.

## L9 update_block_logic with a stale hash

- Call: `update_block_logic` `ZZ_T6_FC1`, current content, `expectedContentHash` of a different block's earlier state (`xml:sha256:8ab52978...`).
- Observed: `phase:"error"`, root `error.category:"state_changed"` ("The content changed since it was read: the current hash no longer matches expectedContentHash. Re-read and retry."), `batch:null`, item recorded `skipped`, no import. MCP result carried `isError`.
- PASS. No post-read was taken for L9 (the brief requires them for L1-L7 only); the block was still present in the later group delete.

## L10 multi-item call, item 2 collides

- Call: `create_tag` `ZZ_T6_L10Tag1` (new), `create_tag` `ZZ_T6_TagA` (exists), `create_tag` `ZZ_T6_L10Tag3` (new).
- Observed: `phase:"applied"`, `success:false`, `error:null`, normal (non-error) MCP result. Item 1 `succeeded`; item 2 `failed` `engineering_rejected` / `EngineeringTargetInvocationException` ("... An attempt was made to create an object that already exists."); item 3 `skipped` with `skipReason:"earlierOperationFailed"`. Info guard `partial_write_no_rollback` on item 2 and a matching warning. Verification lists only item 1 (`trusted`).
- `plc_read` afterwards showed `ZZ_T6_L10Tag1` present and `ZZ_T6_L10Tag3` absent; `ZZ_T6_L10Tag1` was then deleted (`c10-01`).
- PASS.

## L11 dryRun of L10's call

- Call: identical operations, `dryRun:true`, run after `ZZ_T6_L10Tag1` was deleted so the state equals L10's starting state.
- Observed: `phase:"preview"`, `success:true`, three effects, `guards:[]`, `batch:null`, no `engineering_rejected`; the colliding `ZZ_T6_TagA` is planned like the others (collisions are not planned).
- Project unchanged: `plc_read` (`post-h`) shows neither `ZZ_T6_L10Tag1` nor `ZZ_T6_L10Tag3`.
- PASS.

## Cleanup

All `ZZ_T6_*` objects deleted (`z-01` group, `z-02` table); final `list_tag_tables` shows the project's original two tables with unchanged contents. No project tool other than `bind_project` was called; nothing was saved.

## Captured documents

One trimmed `plc_write` response per call. Kept: phase, success, root error, batch counts, each item's status, skip reason, failure (every `engineeringError` object verbatim) and result (project path dropped), guards, and verification. Dropped: effects (except the L11 preview's target list), warnings that repeat a guard message, omission members that were null. Project identity is `<disposable-copy>` throughout.

### L1 create_tag with an existing name

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 0
    },
    "operations": [
      {
        "operationId": "l1-01",
        "operation": "create_tag",
        "status": "failed",
        "failure": {
          "category": "engineering_rejected",
          "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
          "engineeringError": {
            "inner": [],
            "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
            "type": "EngineeringTargetInvocationException"
          }
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": []
  }
}
```
### L2 create_tag_table with an existing name

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 0
    },
    "operations": [
      {
        "operationId": "l2-01",
        "operation": "create_tag_table",
        "status": "failed",
        "failure": {
          "category": "engineering_rejected",
          "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagTableComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
          "engineeringError": {
            "inner": [],
            "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagTableComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
            "type": "EngineeringTargetInvocationException"
          }
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": []
  }
}
```
### L3 create_block where the block exists

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 0
    },
    "operations": [
      {
        "operationId": "l3-01",
        "operation": "create_block",
        "status": "failed",
        "failure": {
          "category": "engineering_rejected",
          "message": "TIA Portal rejected the operation: Error when calling method 'Import' of type 'Siemens.Engineering.SW.Blocks.PlcBlockComposition'.\r\n\r\nCannot import object with Simatic ML ID '0' at line number 4 at line position 4.  The composition already contains an object with the same values for the identifier(s) 'Name, Namespace'.",
          "engineeringError": {
            "inner": [],
            "message": "Error when calling method 'Import' of type 'Siemens.Engineering.SW.Blocks.PlcBlockComposition'.\r\n\r\nCannot import object with Simatic ML ID '0' at line number 4 at line position 4.  The composition already contains an object with the same values for the identifier(s) 'Name, Namespace'.",
            "type": "EngineeringTargetInvocationException"
          }
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": []
  }
}
```
### L4 create_block_group with an existing name

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 0
    },
    "operations": [
      {
        "operationId": "l4-01",
        "operation": "create_block_group",
        "status": "failed",
        "failure": {
          "category": "engineering_rejected",
          "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Blocks.PlcBlockUserGroupComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
          "engineeringError": {
            "inner": [],
            "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Blocks.PlcBlockUserGroupComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
            "type": "EngineeringTargetInvocationException"
          }
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": []
  }
}
```
### L5 delete the default tag table

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 0
    },
    "operations": [
      {
        "operationId": "l5-01",
        "operation": "delete_tag_table",
        "status": "failed",
        "failure": {
          "category": "engineering_rejected",
          "message": "TIA Portal rejected the operation: Error when calling method 'Delete' of type 'Siemens.Engineering.SW.Tags.PlcTagTable'.\r\n\r\nThe operation is not valid because of the current status of object 'Siemens.Engineering.SW.Tags.PlcTagTable'.",
          "engineeringError": {
            "inner": [],
            "message": "Error when calling method 'Delete' of type 'Siemens.Engineering.SW.Tags.PlcTagTable'.\r\n\r\nThe operation is not valid because of the current status of object 'Siemens.Engineering.SW.Tags.PlcTagTable'.",
            "type": "EngineeringTargetInvocationException"
          }
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": []
  }
}
```
### L6 second singleton OB

```json
{
  "phase": "blocked",
  "success": false,
  "error": {
    "category": "guard_blocked",
    "message": "The write was stopped by block guard(s): plc_ob_singleton_exists."
  },
  "batch": null,
  "guards": [
    {
      "id": "plc_ob_singleton_exists",
      "severity": "block",
      "operationId": "l6-01",
      "message": "Operation 'l6-01': OB number 80 of singleton event class 'TimeErrorInterrupt' is already held by 'PLC_1/Blocks/ZZ_T6_Grp/ZZ_T6_OB_A'; only one OB of that class can exist."
    }
  ],
  "verification": null
}
```
### L7 update_tag renaming to a colliding name

```json
{
  "phase": "applied",
  "success": true,
  "error": null,
  "batch": {
    "counts": {
      "failed": 0,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 1
    },
    "operations": [
      {
        "operationId": "l7-01",
        "operation": "update_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "update_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_Tbl",
          "tagName": "ZZ_T6_TagB",
          "userConstantName": null
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "l7-01",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'l7-01': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
### L8a happy path, creates and update

```json
{
  "phase": "applied",
  "success": true,
  "error": null,
  "batch": {
    "counts": {
      "failed": 0,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 5
    },
    "operations": [
      {
        "operationId": "h-01",
        "operation": "create_tag_table",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "create_tag_table",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_H_Tbl",
          "tagName": null,
          "userConstantName": null
        }
      },
      {
        "operationId": "h-02",
        "operation": "create_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "create_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_H_Tbl",
          "tagName": "ZZ_T6_H_Tag",
          "userConstantName": null
        }
      },
      {
        "operationId": "h-03",
        "operation": "create_block_group",
        "status": "succeeded",
        "result": {
          "blockPath": "PLC_1/Blocks/ZZ_T6_H_Grp",
          "blockType": null,
          "language": null,
          "number": null,
          "obEventClass": null,
          "operation": "create_block_group",
          "plcName": "PLC_1",
          "success": true
        }
      },
      {
        "operationId": "h-04",
        "operation": "create_block",
        "status": "succeeded",
        "result": {
          "blockPath": "PLC_1/Blocks/ZZ_T6_H_Grp/ZZ_T6_H_FC1",
          "blockType": "FC",
          "language": "LAD",
          "number": 2,
          "obEventClass": null,
          "operation": "create_block",
          "plcName": "PLC_1",
          "success": true
        }
      },
      {
        "operationId": "h-05",
        "operation": "update_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "update_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_H_Tbl",
          "tagName": "ZZ_T6_H_TagR",
          "userConstantName": null
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "h-01",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-01': the Openness call completed without rejection; nothing was read back."
      },
      {
        "operationId": "h-02",
        "check": "superseded",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-02': superseded by later operation 'h-05' on the same object; the final state is verified there."
      },
      {
        "operationId": "h-03",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-03': the Openness call completed without rejection; nothing was read back."
      },
      {
        "operationId": "h-04",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-04': the Openness call completed without rejection; nothing was read back."
      },
      {
        "operationId": "h-05",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-05': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
### L8b happy path, deletes

```json
{
  "phase": "applied",
  "success": true,
  "error": null,
  "batch": {
    "counts": {
      "failed": 0,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 4
    },
    "operations": [
      {
        "operationId": "h-06",
        "operation": "delete_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "delete_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_H_Tbl",
          "tagName": "ZZ_T6_H_TagR",
          "userConstantName": null
        }
      },
      {
        "operationId": "h-07",
        "operation": "delete_tag_table",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "delete_tag_table",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_H_Tbl",
          "tagName": null,
          "userConstantName": null
        }
      },
      {
        "operationId": "h-08",
        "operation": "delete_block",
        "status": "succeeded",
        "result": {
          "blockPath": "PLC_1/Blocks/ZZ_T6_H_Grp/ZZ_T6_H_FC1",
          "blockType": null,
          "language": null,
          "number": null,
          "obEventClass": null,
          "operation": "delete_block",
          "plcName": "PLC_1",
          "success": true
        }
      },
      {
        "operationId": "h-09",
        "operation": "delete_block_group",
        "status": "succeeded",
        "result": {
          "blockPath": "PLC_1/Blocks/ZZ_T6_H_Grp",
          "blockType": null,
          "language": null,
          "number": null,
          "obEventClass": null,
          "operation": "delete_block_group",
          "plcName": "PLC_1",
          "success": true
        }
      }
    ]
  },
  "guards": [
    {
      "id": "plc_deletes_block",
      "severity": "info",
      "operationId": "h-08",
      "message": "Operation 'h-08': Deleting block 'PLC_1/Blocks/ZZ_T6_H_Grp/ZZ_T6_H_FC1' (FC, LAD) removes it and its content."
    }
  ],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "h-06",
        "check": "superseded",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-06': superseded by later operation 'h-07' on the same object; the final state is verified there."
      },
      {
        "operationId": "h-07",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-07': the Openness call completed without rejection; nothing was read back."
      },
      {
        "operationId": "h-08",
        "check": "superseded",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-08': superseded by later operation 'h-09' on the same object; the final state is verified there."
      },
      {
        "operationId": "h-09",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'h-09': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
### L9 update_block_logic with a stale hash

```json
{
  "phase": "error",
  "success": false,
  "error": {
    "category": "state_changed",
    "message": "Operation 'l9-01': The content changed since it was read: the current hash no longer matches expectedContentHash. Re-read and retry."
  },
  "batch": null,
  "guards": [],
  "verification": null
}
```
### L10 multi-item call, item 2 collides

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 1,
      "succeeded": 1
    },
    "operations": [
      {
        "operationId": "l10-01",
        "operation": "create_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "create_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_Tbl",
          "tagName": "ZZ_T6_L10Tag1",
          "userConstantName": null
        }
      },
      {
        "operationId": "l10-02",
        "operation": "create_tag",
        "status": "failed",
        "failure": {
          "category": "engineering_rejected",
          "message": "TIA Portal rejected the operation: Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
          "engineeringError": {
            "inner": [],
            "message": "Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists.",
            "type": "EngineeringTargetInvocationException"
          }
        }
      },
      {
        "operationId": "l10-03",
        "operation": "create_tag",
        "status": "skipped",
        "skipReason": "earlierOperationFailed"
      }
    ]
  },
  "guards": [
    {
      "id": "partial_write_no_rollback",
      "severity": "info",
      "operationId": "l10-02",
      "message": "This call stopped at this operation. This operation and any earlier operation in the same call may already have changed TIA state; no rollback was attempted. Re-read the affected objects before retrying."
    }
  ],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "l10-01",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'l10-01': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
### L11 dryRun of L10

```json
{
  "phase": "preview",
  "success": true,
  "error": null,
  "batch": null,
  "guards": [],
  "effects": [
    {
      "operationId": "l10-01",
      "operation": "create_tag",
      "name": "ZZ_T6_L10Tag1"
    },
    {
      "operationId": "l10-02",
      "operation": "create_tag",
      "name": "ZZ_T6_TagA"
    },
    {
      "operationId": "l10-03",
      "operation": "create_tag",
      "name": "ZZ_T6_L10Tag3"
    }
  ],
  "verification": null
}
```
### L6 attempt 1

Setup: first TimeErrorInterrupt OB created (number 80) - succeeded

```json
{
  "phase": "applied",
  "success": true,
  "error": null,
  "batch": {
    "counts": {
      "failed": 0,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 1
    },
    "operations": [
      {
        "operationId": "l6-s1",
        "operation": "create_block",
        "status": "succeeded",
        "result": {
          "blockPath": "PLC_1/Blocks/ZZ_T6_Grp/ZZ_T6_OB_A",
          "blockType": "OB",
          "language": "LAD",
          "number": 80,
          "obEventClass": "TimeErrorInterrupt",
          "operation": "create_block",
          "plcName": "PLC_1",
          "success": true
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "l6-s1",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'l6-s1': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
### L6 attempt 2

Route 2: dryRun of two DiagnosticErrorInterrupt OBs in ONE call - host guard also tracks earlier items of the same call (preview phase, block guard on item 2)

```json
{
  "phase": "preview",
  "success": true,
  "error": null,
  "batch": null,
  "guards": [
    {
      "id": "plc_ob_singleton_exists",
      "severity": "block",
      "operationId": "l6-d2",
      "message": "Operation 'l6-d2': OB number 82 of singleton event class 'DiagnosticErrorInterrupt' is already held by 'PLC_1/Blocks/ZZ_T6_Grp/ZZ_T6_OB_C'; only one OB of that class can exist."
    }
  ],
  "effects": [
    {
      "operationId": "l6-d1",
      "operation": "create_block",
      "name": "ZZ_T6_OB_C"
    },
    {
      "operationId": "l6-d2",
      "operation": "create_block",
      "name": "ZZ_T6_OB_D"
    }
  ],
  "verification": null
}
```
### L6 attempt 3

Route 3: update_block_logic on a ProgramCycle OB (ZZ_T6_OB_B, number 123) whose content declares Number 80 and SecondaryType TimeErrorInterrupt, to push a duplicate singleton OB at Portal through the import path. Rejected before import (importStage not_started, targetMutationCommitted false); target hash unchanged

```json
{
  "phase": "applied",
  "success": false,
  "error": null,
  "batch": {
    "counts": {
      "failed": 1,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 0
    },
    "operations": [
      {
        "operationId": "l6-x1",
        "operation": "update_block_logic",
        "status": "failed",
        "failure": {
          "category": "validation_error",
          "message": "Block import did not complete.",
          "blockImportOutcome": {
            "importStage": "not_started",
            "targetMutationCommitted": false,
            "compileStage": "not_started"
          }
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "l6-x1",
        "check": "contentHash",
        "success": true,
        "contentHash": "xml:sha256:1db3dc0a869a28c0f36eb723c07bfc2ae24200dd65f742e276721ce09859a8d5",
        "message": "Operation 'l6-x1' failed with validation_error; the current content hash is reported."
      }
    ]
  }
}
```
### L7 retry

L7 retry (identical payload, operationId l7-02): same silent success; plc_read afterwards still showed ZZ_T6_TagA and ZZ_T6_TagB unchanged

```json
{
  "phase": "applied",
  "success": true,
  "error": null,
  "batch": {
    "counts": {
      "failed": 0,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 1
    },
    "operations": [
      {
        "operationId": "l7-02",
        "operation": "update_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "update_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_Tbl",
          "tagName": "ZZ_T6_TagB",
          "userConstantName": null
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "l7-02",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'l7-02': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
### L7 positive control

L7 positive control: rename ZZ_T6_TagB to a non-colliding name ZZ_T6_TagC; plc_read afterwards shows ZZ_T6_TagA and ZZ_T6_TagC, so update_tag rename works and the colliding rename was silently ignored by Portal

```json
{
  "phase": "applied",
  "success": true,
  "error": null,
  "batch": {
    "counts": {
      "failed": 0,
      "omitted": 0,
      "skipped": 0,
      "succeeded": 1
    },
    "operations": [
      {
        "operationId": "l7-ctl",
        "operation": "update_tag",
        "status": "succeeded",
        "result": {
          "folderPath": "/",
          "operation": "update_tag",
          "plcName": "PLC_1",
          "success": true,
          "tableName": "ZZ_T6_Tbl",
          "tagName": "ZZ_T6_TagC",
          "userConstantName": null
        }
      }
    ]
  },
  "guards": [],
  "verification": {
    "success": true,
    "operations": [
      {
        "operationId": "l7-ctl",
        "check": "trusted",
        "success": true,
        "contentHash": null,
        "message": "Operation 'l7-ctl': the Openness call completed without rejection; nothing was read back."
      }
    ]
  }
}
```
