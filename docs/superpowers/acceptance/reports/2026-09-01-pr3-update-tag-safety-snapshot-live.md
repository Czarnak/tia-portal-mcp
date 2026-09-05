# Acceptance Test Report - PR 3 update_tag safety snapshot (live TIA Portal V21)

**Date:** 2026-09-05
**Runtime:** Real TIA Portal V21
**Harness:** `scripts/live-test-update-tag-safety.ps1`
**Boundary:** Disposable project copy; the mandatory live gate is exact-target read plus one authorized flag-only drift and stale-token rejection.

**Status:** PASS. PR 3's mandatory live acceptance is complete. The optional unavailable-flag
probe was not run because no distinct second target was established to expose a truly unavailable
external flag.

**Source and live-tested revision:** `952f66f3627486e9d5ef0cb2d48bfcbd93a71c2e`.
**Production safety implementation revision:** `3c7e8b29ca0b923877cf310cfd6d3a4e1fc5a0e1`
(`fix: validate update tag safety snapshot`). The live harness repairs after that production
revision, including `5510e40`, `0feadf3`, and `952f66f`, were separately TDD-tested and scoped
re-reviewed. The live runtime was TIA Portal V21 with portal PID `44176`.

**Offline-tested evidence:** Fresh final evidence at `952f66f` passed the serial reference-stub
build with 0 warnings/0 errors, the focused `TagUpdateSafetyLiveHarnessContractTests` suite with
18/18 tests, and the complete suite with 2,632/2,632 tests and 0 skipped. The complete passing run
used:

```powershell
dotnet test TiaMcpServer.Tests --no-build --no-restore --nologo --verbosity minimal -- xunit.parallelizeTestCollections=false xunit.maxParallelThreads=1
```

This supports the required offline/registered rows below; it does not replace the live
acceptance. Before that sequential pass, two default-parallel attempts exposed unrelated existing
timing failures outside PR 3: the first had a network smoke-test timeout plus fake-worker restart
timing failures, and the second had only the network smoke-test timeout. The exact failing groups
then passed in isolation (2/2 and 4/4).

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
- `preview_write_batch` - PASS; exact default-bound `PreviewDrift` accepted the registered token-only preview shape, observed `ExternalVisible = True`, and issued a safety token; no mutation was made and the token was discarded
- one authorized intermediate flag-only drift write on the disposable copy - PASS; `ApplyDrift -AllowApply` changed only `ExternalVisible` from `True` to `False`
- stale-token `apply_write_batch` - PASS; applying the original stale token failed with `failureCategory = state_changed`
- restoration step - PASS; the harness `finally` reconciliation restored `ExternalVisible = True`; no save, close, or discard was performed
- separate final `Read` - PASS; strict snapshot and public-row assertions passed with `ExternalVisible = True`
- direct final `get_project_status` - PASS; `SimpleProject.ap21` remained open with `isModified = true`; no save, close, or discard was performed

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

The exact default-bound `PreviewDrift` accepted the registered preview's token-only result shape,
observed `ExternalVisible = True`, and issued a safety token. No mutation was made by this mode,
and the token was discarded.

## Required Offline and Registered Evidence

| Criterion | Evidence source | Observed |
|---|---|---|
| Bound client sends a complete expected identity; worker rejects missing and mismatched identity as `binding_conflict` | `TagUpdateSafetySnapshotWorkerClientTests` plus `TagUpdateSafetySnapshotIdentityEnforcementTests` | PASS - revision `952f66f`; fresh final complete suite (2,632/2,632, 0 skipped) |
| Requested unavailable flag fails before token issuance | `TagUpdateCurrentStateFakeWorkerTests` plus contract/worker tests | PASS - revision `952f66f`; registered matrix coverage includes `0feadf3`; fresh final complete suite (2,632/2,632, 0 skipped) |
| Strict snapshot payload rejects `{}`, malformed/unsupported shapes, every omitted member, and wrong member types before broad fallback or token issuance | `TagUpdateCurrentStateFakeWorkerTests` | PASS - revision `952f66f`; fresh final complete suite (2,632/2,632, 0 skipped) |
| Harness accepts legal registered token-only preview results, preserves explicit application-error handling, rejects invalid shapes without leaking tokens, and defines the optional-probe guard before its entrypoint call | `TagUpdateSafetyLiveHarnessContractTests` | PASS - revision `952f66f`; focused harness suite 18/18 and fresh final complete suite (2,632/2,632, 0 skipped) |

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
  worker protocol marker), `d000fb5` (optional JSON-RPC `error`), and the direct public tag-table
  array repair. Later reviewed commits were `5510e40` (exact default project binding and hardened
  validation handling), `0feadf3` (registered update-tag safety matrix coverage), and `952f66f`
  (registered token-only preview acceptance with fail-closed invalid-shape handling). The successful
  mandatory read preceded any preview/write; only the authorized `ApplyDrift -AllowApply` mutated
  TIA.
- `ApplyDrift -AllowApply` changed only `ExternalVisible` from `True` to `False`; the original token
  was rejected with `failureCategory = state_changed`, and `finally` restored the flag to `True`.
  A separate final `Read` then passed the strict snapshot and public-row assertions with
  `ExternalVisible = True`.
- Direct final `get_project_status` showed `SimpleProject.ap21` still open with `isModified = true`.
  No save, close, or discard was performed; TIA retains an unsaved modification marker from the
  transient change/restore round trip.
