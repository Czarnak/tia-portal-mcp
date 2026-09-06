# Acceptance Test Report — PR 6 Project-Tree Safety Scopes

Acceptance date: 2026-09-06 UTC

Result: **PASS** for the exact branch revision, already-open TIA Portal V21 project, PLC-global
owner fixture, Software Unit owner fixture, guarded public MCP calls, byte-equivalent restoration,
and post-restoration compile checks recorded here.

## Environment

- Branch: `feat/project-tree-safety-scopes`
- Commit: `cc63a93`
- Startup host command:
  `dotnet run --project TiaMcpServer -- --project C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`
- Disposable project:
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`
- TIA Portal version: `V21`, observed through public `get_project_status`.
- `get_project_status` success before preview: `true` for both owner scopes in Preview and Apply
  modes; the separate Inventory status gate also succeeded.
- Payload `isOpen` before preview: `true`.
- Payload `path` before preview:
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`.
- Envelope `sessionIdentity.projectPath` before preview:
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`.
- Repeated `get_project_status` success before final compile: `true` before all six
  post-restoration compile checks in each owner scope.
- Repeated payload `isOpen` before final compile: `true`.
- Repeated payload `path` before final compile:
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`.
- Repeated envelope `sessionIdentity.projectPath` before final compile:
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`.

The PLC-global fixture was `PLC_LAD/Blocks/PR6_Fixture`, with occupied FC
`PLC_LAD/Blocks/PR6_Fixture/PR6_Occupied`. The Software Unit fixture was
`PLC_LAD/Units/Test_SU/Blocks/PR6_UnitFixture`, with occupied FC
`PLC_LAD/Units/Test_SU/Blocks/PR6_UnitFixture/PR6_UnitOccupied`.

## Restoration

The harness compared each restored deterministic export with its pre-Apply baseline before it
called `compile_check`.

### Global PLC Owner

- Pre-Apply deterministic export SHA-256:
  `8786E433E6DCCC1685A8D7C753FA3833FD45CA897170D3EF559910CD86594D6F`
- Restored deterministic export SHA-256:
  `8786E433E6DCCC1685A8D7C753FA3833FD45CA897170D3EF559910CD86594D6F`
- Byte comparison result: **PASS**. All six scenario-level hash pairs matched; all 26
  byte-equivalence proof files, containing 52 record comparisons, reported equality.

### Software Unit Owner

- Pre-Apply deterministic export SHA-256:
  `7456B0EB289E88791290FC901AE6D88B72743943C20567BDFDFA1689761EF3F4`
- Restored deterministic export SHA-256:
  `7456B0EB289E88791290FC901AE6D88B72743943C20567BDFDFA1689761EF3F4`
- Byte comparison result: **PASS**. All six scenario-level hash pairs matched; all 26
  byte-equivalence proof files, containing 52 record comparisons, reported equality.

## Evidence

### Global PLC Owner

1. Occupied-target content drift changed only the occupied FC export and invalidated the original
   `create_block` token with `state_changed` before mutation.
2. Descendant block-content drift preserved subtree membership but invalidated the original
   `delete_block_group` token with `state_changed` before mutation.
3. Same-parent requested-name occupancy invalidated `create_block_group` with `state_changed`, and
   adding a relevant descendant group separately invalidated `delete_block_group` with
   `state_changed`. Each rejected apply left the post-drift content unchanged.
4. Unrelated sibling-tree drift left the target current-state hash unchanged, and the original
   token was accepted.
5. The authorized three-operation target-mutation sequence and every separately previewed
   restoration sequence succeeded through `apply_write_batch`.
6. The six pre-Apply and restored deterministic export SHA-256 pairs matched. All 52 record-level
   comparisons were byte-equivalent before any final compile was attempted.
7. All 84 redacted binding evidence artifacts recorded successful public status, payload
   `isOpen:true`, and payload/envelope paths equal to the exact startup project.
8. Six final `compile_check` results reported `Success`, 0 errors, and 0 warnings after restoration.

The successful Apply run recorded 233 redacted requests and 233 redacted responses, returned
`success:true` with `restorationProven:true`, and produced no failure artifact.

### Software Unit Owner

1. Occupied-target content drift changed only the unit-owned occupied FC export and invalidated
   the original `create_block` token with `state_changed` before mutation.
2. Descendant block-content drift preserved the unit-owned subtree membership but invalidated the
   original `delete_block_group` token with `state_changed` before mutation.
3. Same-parent requested-name occupancy invalidated `create_block_group` with `state_changed`, and
   adding a relevant unit-owned descendant group separately invalidated `delete_block_group` with
   `state_changed`. Each rejected apply left the post-drift content unchanged.
4. Unrelated sibling-tree drift left the unit-owned target current-state hash unchanged, and the
   original token was accepted.
5. The authorized three-operation target-mutation sequence and every separately previewed
   restoration sequence succeeded through `apply_write_batch`.
6. The six pre-Apply and restored deterministic export SHA-256 pairs matched. All 52 record-level
   comparisons were byte-equivalent before any final compile was attempted.
7. All 84 redacted binding evidence artifacts recorded successful public status, payload
   `isOpen:true`, and payload/envelope paths equal to the exact startup project.
8. Six final `compile_check` results reported `Success`, 0 errors, and 0 warnings after restoration.

The successful Apply run recorded 233 redacted requests and 233 redacted responses, returned
`success:true` with `restorationProven:true`, and produced no failure artifact. The fixture-prep
evidence separately recorded the exact Software Unit owner paths, a successful compile, and an
authoritative FC re-export before the unit-scoped acceptance run.

## Evidence provenance

Repository-auditable evidence consists of the tracked contracts, worker/host implementation,
FakeWorker and unit tests, static harness-contract tests, the guarded harness itself, and this
report. The successful live observations above come from redacted, git-ignored local artifacts;
they are not committed repository evidence and are therefore identified separately:

| Mode / scope | Redacted local artifact directory |
| --- | --- |
| Inventory | `tia-project-tree-safety-20260906T161225Z-85a84aee31b044b7a1f1c4d3939eb0d7` |
| Global PLC Owner — Preview | `tia-project-tree-safety-20260906T162115Z-4745566dddb84b37b4de34f76f1632e4` |
| Global PLC Owner — Apply | `tia-project-tree-safety-20260906T162148Z-3aa768d7f81840a98a027649ab44d3d0` |
| Software Unit Owner — fixture readiness | `tia-project-tree-fixture-prep-20260906T163053Z-a557e6dd85054aecaa9f5e2436841a50` |
| Software Unit Owner — Preview | `tia-project-tree-safety-20260906T163134Z-f40f31721993435282ca2df8f015f527` |
| Software Unit Owner — Apply | `tia-project-tree-safety-20260906T163204Z-ff00aee6b80a48cb9c13048331add2ad` |

The inventory and both Preview runs completed with `success:true` and `mutationStarted:false`.
The successful live claims are limited to this project, these fixtures, this branch revision, and
the public calls recorded above. No claim is made that TIA saved or persisted the restored state,
and this is not plant or physical-hardware acceptance. No safety token or raw project content is
included in this report.

## Deferred Items

- Broader snapshot narrowing: unchanged and out of scope.
- `start_plc` / `stop_plc`: unchanged and out of scope.

The implementation plan for this evidence is
[`2026-09-01-pr6-project-tree-safety-scopes.md`](../../plans/2026-09-01-pr6-project-tree-safety-scopes.md).
