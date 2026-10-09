# PR6 local-session writes, compile and save — live verification

**Passed for every newly enabled write, compile and save path on 2026-10-09.** Each path ran
through the installed public tools against an already-open Multiuser local session, an
already-open Exclusive local session, or both, with a standalone regression in a third TIA Portal
instance. `close_project` stayed refused on a local session. Read-write confirmation was not
exercised live because the tool ran in `full` mode.

## Scope

PR6 delivers on `localSession` containers: `plc_write` (all 14 operations), `network_write`
(all 5 operations), `compile_check`, and `save_project` through `LocalSession.Save()`. A local save
never updates from or checks in to the Project Server. `create_project`, `save_project_as`,
`archive_project`, `close_project`, the internal qualification probes and server-project
contexts remain undelivered.

## Candidate and provenance

| Evidence | Recorded value |
| --- | --- |
| Tested implementation | `feat/multiuser-pr6-writes`, `52e2c49` (clean at install) |
| Version | `3.0.1-local.468.g52e2c49`, installed with `scripts/install-local-tool.ps1` |
| Package | `TiaMcpServer.3.0.1-local.468.g52e2c49.nupkg`, 2,960,897 bytes |
| Package SHA256 | `905BA2284D6F9D991B43F4E570E1711D769844AF65413E70A826425732619901` |
| Installed host DLL SHA256 | `A967D8A614BE84E457004F3C5FF88053F7B7145EB731A7CD7133C926FB7598D4` |
| Installed worker EXE SHA256 | `C980905A5D1A35E2CFCB661B331C408474195BB5C2AB8FAFBF5F1C8955AF056F` |
| Access mode | `full` (global tool launch arguments) |
| Offline suite | 6,002 of 6,002 passed (reference-stub build) |
| Builds | Full solution with reference stubs and against installed V21 assemblies, zero errors |

Coverage was not measured. The loaded host module hash was not inspected directly.

## Fixtures

| Container | Path | PID |
| --- | --- | --- |
| Multiuser local session | `C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_LS_1\LS\SimpleProject.amc21` | 48288 |
| Exclusive local session | `C:\Users\LCZ\Documents\Automation\Sessions\SimpleProject_ES1\SV\SimpleProject.amc21` | 49188 |
| Standalone regression | `C:\Users\LCZ\Desktop\RnD\TIA-Portal\DisposableProjects\3185_Copy\3185_Copy.ap21` | 36284 |

Both sessions were cold-adopted with `bind_project`: `containerKind:"localSession"`,
`sessionMode:"unknown"`, `openedByWorker:false`, connection observation `unknown`. The capability
list advertised every PR6 operation as `projectContent` and the four standalone lifecycle tools as
`standaloneOnly`. Both started with `isModified:false`. The Project Server connection state was
not observed; whether either session was online is unknown.

## Results

| Path | Multiuser | Exclusive | Evidence |
| --- | --- | --- | --- |
| `plc_write` dry run (5 creates) | Pass | — | `phase:preview`, no guards, no mutation |
| `create_block_group`, `create_block` (FC/SCL), `create_tag_table`, `create_tag`, `create_user_constant` | Pass | Pass | `applied`; verification `exists`/values for every item |
| `update_block_logic`, XML | Pass | — | Comment-only change; import and block compile succeeded; post hash `xml:sha256:95716b4a…` |
| `update_block_logic`, source | See below | Pass | One-line comment edit; post hash `source:sha256:59007a0b…` |
| `update_type_content`, source | — | Pass | `UDT_Settings` default `T#5m`→`T#6m`; read-back shows `T#6M` |
| `update_tag` (rename + address), `update_user_constant` | Pass | Pass | Verified values `%M100.1`, `7` |
| `delete_tag`, `delete_user_constant`, `delete_tag_table`, `delete_block`, `delete_block_group` | Pass | Pass (table/group) | Verified `absent`; info guards fired as designed |
| `compile_check` (PLC scope) | Pass | Pass | `Success`, 0 errors, 0 warnings |
| `create_subnet` | Pass (Ethernet) | Pass (Profibus) | Identity, name, type and device count verified |
| `update_subnet` | Pass (name) | Pass (name + `highestAddress` 126→31) | Verified |
| `delete_subnet` | Pass | Pass | `subnetAbsent`, nodes preserved, device count unchanged |
| `configure_network_device` | — | Pass | X2 address `.20`→`.21`→`.20`; final check `192.168.13.20` |
| `add_network_device` | — | Pass | `PR6_Added_Station` (`6ES7 516-3AP03-0AB0/V4.1`) verified |
| `save_project` dry run | Pass | — | `preview`, source context `localSession`, `isModified:true`, no guards |
| `save_project` | Pass | Pass | Result and verification `isModified:false`, same owner context, binding stayed verified |
| `close_project` dry run | — | Refused | `unsupported_capability` before dispatch |

### Source import observation

On the Multiuser session, an SCL source update that added a `VAR_TEMP` section and two statements
failed with `worker_operation_failed` "Block source generation did not complete." The content
hash was unchanged and nothing was committed. The **identical** request failed identically on the
standalone project, and a one-line comment edit then succeeded on standalone and on the Exclusive
session. The failure is therefore Siemens rejecting that source text, not a PR6 regression. The
error carries no compiler or generation diagnostics; that is a pre-existing usability gap.

### Standalone regression

On `3185_Copy.ap21` the five creates, the source update (comment edit), two deletes and
`save_project` passed; `lastModified` advanced and the project ended `isModified:false`.

## Not exercised

- Read-write confirmation for local save (requires `--access-mode read-write`); the
  "save local session … does not check in" wording is covered only by the FakeWorker protocol test.
- Worker-opened (`.als21` opened via `open_project`) sessions; only cold-adopted sessions ran.
- Explicitly offline sessions, and markings/locks or concurrent edits by another user (PR8).
- XML import and `update_type_content` on the Multiuser session; `configure_network_device` and
  `add_network_device` on the Multiuser session.
- Delete of an added device: no tool exists, so `PR6_Added_Station` remains in the saved Exclusive
  session.

## Fixture end state

- Multiuser: all fixtures deleted after the save; those deletions are **unsaved**.
- Exclusive: PLC and subnet fixtures deleted, `UDT_Settings` changed to `T#6M`,
  `PR6_Added_Station` added; saved.
- Standalone: fixtures deleted; saved.
