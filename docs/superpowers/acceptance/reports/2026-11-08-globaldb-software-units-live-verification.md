# GlobalDB creation in software units — issue #221

2026-11-08. Scoped PASS for the unchanged failure reproduction, native XML comparison,
and the fixed worktree worker on one disposable V21 S7-1500 project.

## Target and provenance

- Existing open disposable project: `C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\MixOfEverything\MixOfEverything.ap21`.
- Portal PID `43340`; Openness assembly version `21.0.0.0`.
- Device `S7-1500/ET200MP station_1`, software `PLC_1`, CPU 1516-3 PN/DP,
  native `TypeIdentifier=OrderNumber:6ES7 516-3AP03-0AB0/V4.1`.
- Existing empty software unit `Unit_1`. Only uniquely named issue-221 probe blocks
  and folders were created; no PLC hardware, online operation or other project was changed.
- Baseline source: `5af42c140269b74fd7cd3c69598e2b2a810bc67c`.
  The unchanged generator working-tree file had SHA-256
  `28842C68F7EE58856E4C3A5DA18657931952D085B89A94859E07B4C0C8077FE9`.
- Fixed worker: built in the isolated GlobalDB worktree against installed V21
  assemblies; executable SHA-256
  `D1D385E66F293C3030D175A73D19B48E32A3B7D9F569990DB2D61BE7272FA762`.
  Its functional change was committed as `9d30103b`. It was launched directly;
  no installed server or global tool was replaced.

## Unchanged production reproduction

The installed `plc_write` call used `create_block`, `blockType:GlobalDB` and
`PLC_1/Units/Unit_1/Blocks/Issue221BaselineDb`.
It failed `engineering_rejected` with native `EngineeringTargetInvocationException`:
`PlcBlockComposition.Import` → `Create` → missing XML attribute `ReadOnly` for
`MemoryLayout`. A later `plc_read` found no such block.

The failed import made the project dirty despite leaving no target DB. Therefore
absence of the target did not establish an unchanged project. The project was not saved.

## Native comparison before implementing

The native probe extracted the exact GlobalDB template from the unchanged generator.
Each case changed only its name and the MemoryLayout form, imported with
`PlcBlockComposition.Import(FileInfo, ImportOptions.None)`, committed successful
imports, resolved the returned block, compiled it and exported it with
`ExportOptions.WithDefaults`. Imports were individually transactional under
exclusive access; compilation and export occurred outside transactions.

| Destination | Plain MemoryLayout=Optimized | MemoryLayout omitted | MemoryLayout ReadOnly=true |
| --- | --- | --- | --- |
| PLC root | Imports, compiles | Imports, compiles | Imports, compiles |
| Unit_1 root | Exact missing-ReadOnly rejection | Imports, compiles | Imports, compiles |
| Unit_1/Issue221Probe | Exact missing-ReadOnly rejection | Imports, compiles | Imports, compiles |

All seven successful imports compiled `Success`, zero errors and zero warnings.
Both failed imports left no block. Every PLC-root export contained
`<MemoryLayout>Optimized</MemoryLayout>`, including the omitted-input case.
Unit exports omitted MemoryLayout under WithDefaults.

Both candidate remedies worked. Omitting the element was chosen because the same
minimal XML works in all three scopes and needs no new owner-context parameter.
Only GlobalDB generation changed; the OB MemoryLayout assignment is unchanged.

The [V21 software-unit manual](https://docs.tia.siemens.cloud/r/en-us/v21/using-software-units-s7-1500/creating-a-plc-program-in-software-units-s7-1500)
requires optimized access in units. The
[V21 import reference](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/importing-block)
says read-only/informative attributes are ignored during import.
The [export reference](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/export/import/importing/exporting-data-of-a-plc-device/blocks/exporting-blocks)
allows non-unit XML to be imported under a unit with default Access. No Access
attribute or other unit-specific field was added.

## Fixed-worker qualification

The built net48 worker ran with `--access-mode read-write`.
The harness sent `hello` with `protocolVersion:project-tree-v3`, selected the exact
PID/project through `select_portal_project`, and supplied the returned complete
`expectedSessionIdentity` on every later request.

All three `create_block`, `get_block_content` and `compile_check` requests succeeded:

| Path | Independent native MemoryLayout | Independent native block compile |
| --- | --- | --- |
| PLC_1/Blocks/Issue221FixedRoot | Optimized | Success; 0 errors, 0 warnings |
| PLC_1/Units/Unit_1/Blocks/Issue221FixedUnit | Optimized | Success; 0 errors, 0 warnings |
| PLC_1/Units/Unit_1/Blocks/Issue221FixedGroup/Issue221FixedNested | Optimized | Success; 0 errors, 0 warnings |

After worker exit, a separate native attachment resolved each block, read
`GetAttribute("MemoryLayout")` and called its `ICompilable.Compile()`.
These checks establish the actual optimized layout even where the export omitted it.

One earlier worker attachment timed out at the Openness security dialog before
any mutation request. A subsequent run created the root DB successfully but stopped
because the harness incorrectly parsed raw block-content text as JSON; the probe
reader was corrected, native state was inspected and those objects were removed
before the complete run. Neither event was treated as a product failure or silently replayed.

## Offline verification and cleanup

- RED: both GLOBALDB/DB generator regression cases failed on the existing MemoryLayout element.
- GREEN: 149 focused generator, validator, precondition and source-contract tests passed.
- Installed-assembly serial solution build: 0 errors, 8 existing warnings.
- Full non-packaging regression: 8,397 passed, 0 failed, 0 skipped; 5m39s.
- Independent source and documentation/evidence reviews found no actionable issues.

All seven native-comparison blocks and their temporary nested folder were deleted
under exclusive access and a transaction. After fixed-worker checks, the root DB,
unit DB and created nested folder were also deleted in a transaction; direct native
lookups confirmed all three probe names absent. The baseline failed target was absent.
A final PLC_1 aggregate `compile_check` returned Success, 0 errors and 0 warnings.

The project remains open, dirty and unsaved. No deployment, global installation,
download, save, close, push or merge was performed.

## Evidence and limits

Byte-preserved native traces, exact input XML and probe sources are in
[inputs/2026-11-08-globaldb-software-units](inputs/2026-11-08-globaldb-software-units/README.md).
The retained readback trace is direct Openness evidence; worker NDJSON and MCP
traces remain transient and are not retained as raw native evidence.

This qualifies one CPU1516-3 PN/DP V4.1 project on the installed V21 environment.
Other CPU families, other Portal versions, local sessions, nonempty DB interfaces,
name collisions and unrelated write operations were not exercised live.
Portal determines the destination layout; this report does not establish
universally optimized defaults on other controller families.
