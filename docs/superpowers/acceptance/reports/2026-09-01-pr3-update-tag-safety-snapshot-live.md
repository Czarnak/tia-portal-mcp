# Acceptance Test Report - PR 3 update_tag safety snapshot (live TIA Portal V21)

**Date:** 2026-09-05
**Runtime:** Real TIA Portal V21
**Harness:** `scripts/live-test-update-tag-safety.ps1`
**Boundary:** Disposable project copy; the mandatory live gate is exact-target read plus one authorized flag-only drift and stale-token rejection.

**Status:** PASS. PR 3's mandatory live acceptance is complete. The optional unavailable-flag
probe was not run because no distinct second target was established to expose a truly unavailable
external flag.

**Live-tested branch revision:** `845824b0903743220c82084610891b4ab4f4dceb`.
**Production safety implementation revision:** `3c7e8b29ca0b923877cf310cfd6d3a4e1fc5a0e1`
(`fix: validate update tag safety snapshot`). The live harness repairs after that production
revision were separately TDD-tested and scoped re-reviewed. The live runtime was TIA Portal V21
with portal PID `44176`.

**Offline-tested evidence:** Fresh final evidence at `845824b` passed the serial reference-stub
build with 0 warnings/0 errors and the full suite with 2,622/2,622 tests and 0 skipped. This
supports the required offline/registered rows below; it does not replace the live acceptance.

## Mandatory Live Target

- Project copy path (authorized test project): `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`
- Requested `plcName` input: `PLC_LAD`
- Resolved PLC name from `read_update_tag_safety_snapshot`: `PLC_LAD`
- Root folder: `/`
- Tag table name: `Inputs`
- Tag name: `DI_Reserve_1_7`
- Data type: `Bool`
- Logical address: `%I1.7`
- Drift flag proved: `ExternalVisible`, observed as `True`

## Mandatory Calls Performed

- `get_project_status` identity establishment for the worker-level preflight - PASS; direct worker hello/status established a complete worker-stamped session identity, with portal PID `44176`
- `read_update_tag_safety_snapshot` preflight on the drift target - PASS; strict snapshot resolved `PLC_LAD` / `/` / `Inputs` / `DI_Reserve_1_7`, and observed `ExternalVisible = True`
- `list_tag_tables` comparison read - PASS; registered `execute_read_batch/list_tag_tables` returned one successful operation whose row matched `Bool` / `%I1.7`
- `preview_write_batch` - PASS; `PreviewDrift` observed `ExternalVisible = True` and issued a safety token; no mutation was made and the token was discarded
- one authorized intermediate flag-only drift write on the disposable copy - PASS; `ApplyDrift -AllowApply` changed only `ExternalVisible` from `True` to `False`
- stale-token `apply_write_batch` - PASS; applying the original stale token failed with `failureCategory = state_changed`
- restoration or discard step - PASS; the harness `finally` reconciliation restored `ExternalVisible = True`; no save, close, or discard was performed

## Mandatory Live Results

| Criterion | Command | Observed |
|---|---|---|
| Identity-bound internal safety read succeeds with the worker-stamped identity | `$projectPath = 'C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21'; pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath $projectPath -PlcName 'PLC_LAD' -TableName 'Inputs' -TagName 'DI_Reserve_1_7' -DriftFlagName ExternalVisible -Mode Read` | PASS - direct worker hello/status established the complete worker-stamped session identity; the strict safety snapshot resolved the exact target and observed `ExternalVisible = True` |
| Exact-target snapshot resolves PLC identity and the chosen drift flag is readable | `$projectPath = 'C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21'; pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath $projectPath -PlcName 'PLC_LAD' -TableName 'Inputs' -TagName 'DI_Reserve_1_7' -DriftFlagName ExternalVisible -Mode Read` | PASS - resolved PLC `PLC_LAD`, root `/`, table `Inputs`, tag `DI_Reserve_1_7`; `ExternalVisible = True` |
| Flag-only drift causes stale-token `state_changed` before mutation | `$projectPath = 'C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21'; pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath $projectPath -PlcName 'PLC_LAD' -TableName 'Inputs' -TagName 'DI_Reserve_1_7' -DriftFlagName ExternalVisible -Mode ApplyDrift -AllowApply` | PASS - the authorized intermediate update changed only `ExternalVisible` `True` -> `False`; the original stale token was rejected with `failureCategory = state_changed`; reconciliation restored `True` |
| Public `list_tag_tables` semantics remain unchanged | `$projectPath = 'C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21'; pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath $projectPath -PlcName 'PLC_LAD' -TableName 'Inputs' -TagName 'DI_Reserve_1_7' -DriftFlagName ExternalVisible -Mode Read` | PASS - registered `execute_read_batch/list_tag_tables` returned one successful operation matching `Bool` / `%I1.7`; an independent final `Read` again observed `ExternalVisible = True` |

The mandatory non-mutating preview command passed without mutation:

```powershell
$projectPath = 'C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21'
pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath $projectPath -PlcName 'PLC_LAD' -TableName 'Inputs' -TagName 'DI_Reserve_1_7' -DriftFlagName ExternalVisible -Mode PreviewDrift
```

`PreviewDrift` observed `ExternalVisible = True` and issued a safety token. No mutation was made
by this mode, and the token was discarded.

## Required Offline and Registered Evidence

| Criterion | Evidence source | Observed |
|---|---|---|
| Bound client sends a complete expected identity; worker rejects missing and mismatched identity as `binding_conflict` | `TagUpdateSafetySnapshotWorkerClientTests` plus `TagUpdateSafetySnapshotIdentityEnforcementTests` | PASS - revision `845824b0903743220c82084610891b4ab4f4dceb`; fresh final full offline suite (2,622/2,622, 0 skipped) |
| Requested unavailable flag fails before token issuance | `TagUpdateCurrentStateFakeWorkerTests` plus contract/worker tests | PASS - revision `845824b0903743220c82084610891b4ab4f4dceb`; fresh final full offline suite (2,622/2,622, 0 skipped) |
| Strict snapshot payload rejects `{}`, malformed/unsupported shapes, every omitted member, and wrong member types before broad fallback or token issuance | `TagUpdateCurrentStateFakeWorkerTests` | PASS - revision `845824b0903743220c82084610891b4ab4f4dceb`; fresh final full offline suite (2,622/2,622, 0 skipped) |
| Harness accepts a legal tool result with omitted `isError`, preserves explicit application-error handling, and defines the optional-probe guard before its entrypoint call | `TagUpdateSafetyLiveHarnessContractTests` | PASS - revision `845824b0903743220c82084610891b4ab4f4dceb`; focused harness suite 13/13 and fresh final full offline suite (2,622/2,622, 0 skipped) |

## Optional Live Unavailable Probe

| Criterion | Command | Observed |
|---|---|---|
| Second target exposes an unavailable flag and preview rejects it before token issuance | Not run; no distinct second target was established to expose a truly unavailable external flag. | NOT RUN |

If the authorized disposable target does not expose an unavailable flag for the chosen second target,
record `NOT RUN` here and keep the acceptance gate anchored to the mandatory live rows plus the
required offline/registered evidence.

## Execution notes

- The reproducible commands above use the harness default, which starts the local host DLL with the
  exact `--project $ProjectPath` binding. The previously recorded native `pwsh -File` form with an
  array-valued `-HostArguments` override was removed because native parameter binding consumed its
  embedded `--project` as another harness argument. Earlier abbreviated unbound commands are not
  the accepted evidence.
- Earlier attempts all stopped before mutation: empty-array binder; missing worker protocol marker;
  Openness permission timeout; optional JSON-RPC error access; and wrong public payload wrapper. The
  corresponding harness repairs were `c07a624` (empty worker arguments), `594e304` (per-request
  worker protocol marker), `d000fb5` (optional JSON-RPC `error`), and `845824b` (direct public
  tag-table array). Each was TDD-tested and scoped re-reviewed. The successful mandatory read
  preceded any preview/write; only the final authorized `ApplyDrift -AllowApply` mutated TIA.
- The exact flag value was restored in memory to `ExternalVisible = True`, but direct final
  `get_project_status` observed the unchanged project path with `isModified = true`. No save, close,
  or discard was performed; TIA retains an unsaved modification marker from the transient
  change/restore round trip.
