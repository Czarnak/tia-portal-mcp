# PR5 local-session content reads — live verification

**Passed for every enabled read on 2026-10-09.** Content reads ran against one cold-adopted local
session through the installed public tools, and the same reads passed a standalone regression in a
second TIA Portal instance. Local compile and PLC writes stayed refused before dispatch. Exclusive
and offline sessions were not exercised.

## Scope

PR5 delivers content reads on `localSession` containers: `browse_project_tree`, `plc_read`
(`get_block_content`, `get_type_content`, `list_tag_tables`), `network_read`
(`read_hardware_config`, `list_network_objects`, `inspect_network_object`),
`read_cross_references` and `hmi_read`. Content writes, `compile_check`, `save_project` and
server-project contexts remain undelivered. The change also fixes `hmi_read` on `main`: the PR4
capability gate did not map `hmi_*` worker methods, so the worker refused them for every container.

## Candidate and provenance

| Evidence | Recorded value |
| --- | --- |
| Tested implementation | `feat/multiuser-pr5-reads`, `909c8f8` (clean at install) |
| Version | `3.0.1-local.465.g909c8f8`, installed with `scripts/install-local-tool.ps1` |
| Package | `TiaMcpServer.3.0.1-local.465.g909c8f8.nupkg`, 2,960,685 bytes |
| Package SHA256 | `5589882F7151A9CCC9B4096CF571AC1A931EF2BB0502B0E7CB1023C3CB90D162` |
| Installed host DLL SHA256 | `E07ED0A3575623364A07D250FF78D1454218AE854D1086CE416858995CBF8656` |
| Installed worker EXE SHA256 | `718C8D98292EBB732035BA1C1B2B3392BAD0D8F38689C90396B1F89E55A66EF2` |
| Access mode | `full` (global tool launch arguments) |
| Offline suite | 5,986 of 5,987 passed. `DoctorPackageVerificationScriptTests.WorkerBuild_OverwritesNewerValueTupleBeforePackaging` failed once under full-suite load, then passed alone and in the earlier full run |
| Builds | Worker against installed V21 assemblies; full solution with reference stubs |
| Documentation commit | Tool-description and docs changes rebuilt with stubs; 5,987 of 5,987 tests passed |

Coverage was not measured for this candidate. The loaded host module hash was not inspected
directly; the installed files above match the package that the install script verified.

## Local session

Fixture: `C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_LS_1\LS\SimpleProject.amc21`,
already open in a UI Portal, PID 48288. `bind_project` cold-adopted it: `containerKind:"localSession"`,
`sessionMode:"unknown"`, `openedByWorker:false`, `sessionContainerPath:null`, connection
observation `unknown`. The capability list advertised the reads above as `projectContent` and
writes, compile and save as `notYetDelivered`.

| Call | Observation |
| --- | --- |
| `browse_project_tree` depth 4 | 63 nodes: 5 devices, PLCs `PLC_LAD`/`PLC_FBD`/`PLC_DP`, software unit `Test_SU`, tag tables, types |
| `plc_read list_tag_tables` | All three PLCs with tags and user constants |
| `plc_read get_block_content` | `PLC_LAD/Blocks/Main` (xml) and `PLC_LAD/Simulation_DB` (source) returned content and hashes. `PLC_LAD/Main` failed `target_ambiguous` because `Main` also exists in `Test_SU`, which is the expected selector behavior |
| `plc_read get_type_content` | `PLC_LAD/AnalogInputSettings` returned |
| `network_read read_hardware_config` | 5 devices, 3 subnets; attribute-unsupported node messages only |
| `network_read list_network_objects` | 15 subnet, node and IO-system objects |
| `network_read inspect_network_object` | Node `E1` (address 192.168.12.1, mask, device name) and subnet `B9DF-1` |
| `read_cross_references` | Tag `DI_EmergencyStop` read by `Inputs_FB` network 2; an FB target returned zero sources |
| `hmi_read` | `list_hmi_devices` empty (fixture has no HMI); `list_project_languages` en-US |
| `compile_check` | `unsupported_capability`, as designed |
| `plc_write` `dryRun:true` | `unsupported_capability`, phase `error`, as designed |
| `get_project_status` afterwards | `isModified:false`; the reads left the session unmodified |

## Standalone regression

`bind_project` with `forceRebind:true` switched to
`C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\3185_Copy\3185_Copy.ap21`, PID 36284.

| Call | Observation |
| --- | --- |
| `hmi_read list_hmi_devices` | Five Unified HMIs and one Classic HMI; languages en-US and pl-PL |
| `hmi_read` on `HMI_RT_1` | Grouped screens, 72 tags, 78 alarms and `HMI_Connection_1` returned |
| `hmi_read list_screens` on Classic `HMI_RT_6` | `target_kind_unsupported`, as designed |
| `browse_project_tree` depth 2 | 57 nodes with a continuation cursor |
| `plc_read list_tag_tables` | `TH-300A1` default table returned |
| `network_read` | Subnets and IO systems listed; `read_hardware_config` returned 55 connected PN/IE_1 nodes with complete connection evidence, exercising the widened `ProjectBase` owner walk |

The `main` refusal of `hmi_read` was not reproduced live, because that would require reinstalling
`main`. It follows from the code: standalone binding creates an `ActiveProjectContext`, and the
`main` worker passed the raw `hmi_*` method name to `ProjectCapabilityCatalog.Supports`, which
returned false. A catalog unit test now pins the mapping.

## Not exercised

- Exclusive sessions and sessions opened offline.
- Unified HMI content inside a local session; the local fixture contains no HMI.
- Other-user markings, freshness and outdated-object state. Reads do not observe these.
