# PR 5 Tag Operation Safety Scopes Live Acceptance

Acceptance date: 2026-09-05 UTC

Result: **PASS** for the exact offline build, host binary, TIA Portal session, disposable project
copy, fixtures, guarded calls, cleanup, and saved-baseline checks recorded here.

## Environment

- TIA Portal version: the live status payload reported `Totally Integrated Automation Portal V21`
  and `STEP 7 Professional V21`. Separately, project history recorded that the copy was last saved
  by V21 Update 2 Hotfix 1; that history entry is not treated as the runtime-version probe.
- Harness script path:
  [`scripts/live-test-tag-operation-safety-scopes.ps1`](../../../../scripts/live-test-tag-operation-safety-scopes.ps1),
  run with PowerShell 7.6.5.
- Repository revision: `2d6d071` on `feat/tag-operation-safety-scopes`.
- Host DLL: `TiaMcpServer/bin/Debug/net8.0/TiaMcpServer.dll`, SHA-256
  `3EA42029CE1797085851DAC73CA72B2126DD778CE2EF5E2C92BB277B65B88028`.
- Disposable project copy:
  `C:\Users\LCZ\AppData\Local\Temp\TIA_PR5_20260905T222714Z\TIA_PR5_20260905T222714Z.ap21`.
- Source project:
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`.
- PLC identity: `PLC_LAD`.
- Target table / sibling table: `Default tag table` at `/` / `Inputs` at `/`.
- Target tag / collision tag / user constant: `AlwaysTRUE` (`Bool`, baseline `%M1.2`) /
  `PR5_CollisionTag_20260905` with collision address `%M10.0` / `heaterStages` (`USInt`,
  baseline `3`, live applied value `4`). The sibling constant was `PR5_Sibling_USInt`
  (`USInt`, baseline `5`, drift value `6`); unused creation fixtures were
  `PR5_NewConstant_20260905` and `PR5_NewTable_20260905`.
- Portal identity: PID `54096` throughout. Each harness process had its own worker session:
  `c07c38b6390948eebe7a3e2d62cb5aa5` for `PreviewOnly`,
  `dfeabc17b9f948c4bf8c0c05cf8c46ba` for `DriftAndRestore`, and
  `8aebe1d8ea394c1fb52eadbf3c70704a` for `ApplyAndRestore`, each at generation `2`
  and bound to the exact disposable path. Final source verification used direct worker
  `72043e5a1a2f448b9148a4ae3c1d1b8f`, generation `8`, PID `54096`.

## Calls Performed

- Preview-only checks: `PreviewOnly` ran all eight operation-specific previews twice and ran one
  ordered duplicate-selector preview. The eight operations were `create_tag_table`,
  `delete_tag_table`, `create_tag`, `update_tag`, `delete_tag`, `create_user_constant`,
  `update_user_constant`, and `delete_user_constant`. The project stayed open and unmodified.
- Drift-and-restore checks: `DriftAndRestore` previewed the same-object target, name collision,
  address collision, and unrelated-sibling target before guarded mutations. It applied only to the
  authorized disposable copy, retained scenario mutations until cleanup, and closed the copy with
  `saveBeforeClose=false`; the mode name does not imply an inverse-write restore.
- Apply-and-restore checks: `ApplyAndRestore` used the unchanged issued token for one authorized
  `update_user_constant`, observed `heaterStages=4`, and closed the copy with
  `saveBeforeClose=false`.
- Post-run registered calls: after a read-only binding reconciliation, a fresh
  `open_project(forceRebind=true)` preview/apply reopened the disposable copy. Registered
  `get_project_status` and `list_tag_tables` reads proved the saved baseline. A fresh
  `close_project(saveBeforeClose=false)` preview/apply closed the copy, then a fresh
  `open_project(forceRebind=true)` preview/apply reopened the source project and a final status
  read verified it clean.

The successful redacted artifact directories are:

| Mode | Artifact directory | Files |
| --- | --- | ---: |
| `PreviewOnly` | `tia-tag-safety-20260905T223842Z-9a69e824bbef4ab18198439a9b609914` | 82 |
| `DriftAndRestore` | `tia-tag-safety-20260905T224041Z-70aa5cc842d2437aa6282642d9231a2d` | 178 |
| `ApplyAndRestore` | `tia-tag-safety-20260905T224355Z-eb51691b7ae1434b9ad363a06aa6123c` | 98 |

All three successful runs used the artifact root
`.superpowers/sdd/2026-09-01-pr5-tag-operation-safety-scopes/live-artifacts`.

## Evidence

- Same-object drift result: **PASS**. The target constant changed after token issuance; the stale
  token was rejected with `state_changed`, and rejection did not change the target state.
- Relevant collision result: **PASS**. Creating `PR5_CollisionTag_20260905` at `%M10.0` invalidated
  both the stale name-collision token and the stale logical-address-collision token. Both applies
  were rejected with `state_changed`. Together with same-object drift, the run contained exactly
  three `state_changed` responses and no other failure category.
- Unrelated sibling tolerance result: **PASS**. Changing `PR5_Sibling_USInt` from `5` to `6` did
  not change the target snapshot hash; the original token remained valid and created the requested
  target-table constant.
- Successful apply result: **PASS**. `ApplyAndRestore` applied one unchanged issued token and a
  registered read observed `heaterStages` as `USInt` value `4` before discard.
- Restore or discard result: **PASS**. Both mutation modes closed the exact disposable copy through
  guarded `close_project(saveBeforeClose=false)` preview/apply. The later saved-copy read proved
  `heaterStages=3`, `PR5_Sibling_USInt=5`, and `AlwaysTRUE=Bool %M1.2`; the collision tag and
  `%M10.0` address, new constant, and new table were absent. The copy was unmodified and was closed
  again without saving. The exact source project was then reopened, verified `isModified=false`,
  and left open.
- Artifact hygiene result: **PASS**. Recursive parsing found 78 safety-token fields, all 78 stored
  as `[REDACTED]`; no raw token, unexpected file, unexpected nested directory, or JSON parse failure
  was found. The expected first-connect status advisory in each successful harness named PID
  `54096` and the exact disposable path; no batch operation returned warnings.
- Offline and build evidence: focused PR5 tests passed 83/83; current-state FakeWorker tests passed
  24/24; live-harness contracts passed 7/7; the PowerShell parser reported 0 errors; the stub build
  and real TIA V21 build both completed with 0 warnings and 0 errors; the full offline suite passed
  2691/2691. `git diff --check` passed.

### Failed and reconciled attempts

Before the harness runs, the first direct status call emitted its expected first-connect advisory.
The controller accepted only that advisory because its PID `54096` and exact disposable path
matched the complete verified status identity; it was not treated as permission to ignore any
operation warning.

The first sandboxed `PreviewOnly` launch could not see the desktop TIA instance and stopped at
`get_project_status` with `worker_operation_failed`: `No running TIA Portal V21 instance found`.
Its artifact directory,
`tia-tag-safety-20260905T223749Z-98107d2a263040e9967a1913e37653d8`, records
`mutationAttempted=false`, no token, no warning, and successful host/transient cleanup. The exact
read-only command was rerun through the approved desktop-access path. This was a pre-mutation
sandbox visibility failure, not an uncertain write.

After `ApplyAndRestore` closed the copy from a separate harness process, the first direct
`open_project` apply was rejected with `binding_conflict`: its preview expected direct-worker
generation `4` and the disposable path, while apply observed generation `5` and no open project.
The controller ruled this a deterministic pre-mutation rejection. A required read-only status call
then invalidated the stale binding and returned the same deterministic `binding_conflict`; it did
not mutate TIA. One authorized fresh preview/apply after reconciliation succeeded, producing
direct-worker generation `6` on the disposable copy. Neither binding conflict was an uncertain
write, and neither was automatically retried before reconciliation.

Two earlier lifecycle preview tokens were issued but never retained or applied after the live
controller initially expected a `success` field that lifecycle previews do not contain. They were
not written to this report or the artifacts.

## Verification Boundary

- Proven: for repository revision `2d6d071`, the host SHA-256 and TIA V21/PID/project identities
  above, all eight tag/table/user-constant operations produced stable exact-selector previews;
  same-object, tag-name, and logical-address collision drift invalidated stale tokens;
  unrelated sibling drift did not invalidate the scoped target; one unchanged-token apply
  succeeded; mutation-mode cleanup used confirmed no-save discard; the saved disposable baseline
  was intact; and the source project was restored open and unmodified. Offline tests separately
  prove typed snapshot shapes, exact worker dispatch, guarded `SafetyRead` identity policy,
  cross-kind tag/constant/block name-collision candidates, phase-local deduplication with ordered
  expansion, fresh apply reads, and deterministic delete-table export behavior.
- Not proven: this is not plant or production acceptance and does not qualify physical PLC
  behavior. Multilingual per-tag comment binding remains deferred. Public `list_tag_tables`
  completeness and best-effort behavior remain unchanged. Broader snapshot narrowing beyond the
  eight PR5 operations remains deferred. Software Unit namespace-aware collision handling remains
  deferred; unit-local block names were not treated as unqualified CPU-global collisions. PLC
  `start_plc` and `stop_plc` were neither changed nor live-qualified. No claim is made about those
  deferred areas from static, offline, FakeWorker, or this bounded live evidence.

The implementation plan for this evidence is
[`2026-09-01-pr5-tag-operation-safety-scopes.md`](../../plans/2026-09-01-pr5-tag-operation-safety-scopes.md).
