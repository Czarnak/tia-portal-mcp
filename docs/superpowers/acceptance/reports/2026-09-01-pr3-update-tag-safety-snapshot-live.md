# Acceptance Test Report - PR 3 update_tag safety snapshot (live TIA Portal V21)

**Date:** 2026-09-01
**Runtime:** Real TIA Portal V21
**Harness:** `scripts/live-test-update-tag-safety.ps1`
**Boundary:** Disposable project copy; the mandatory live gate is exact-target read plus one authorized flag-only drift and stale-token rejection.

**Status:** PENDING / NOT RUN. This report prepares the required live evidence, but no live
TIA Portal V21 harness mode has been authorized or run. PR 3 remains incomplete until the
mandatory live acceptance is separately authorized and observed.

## Mandatory Live Target

- Project copy path: PENDING
- Requested `plcName` input: PENDING
- Resolved PLC name from `read_update_tag_safety_snapshot`: PENDING
- Tag table name: PENDING
- Tag name: PENDING
- Drift flag proved: PENDING

## Mandatory Calls Performed

- `get_project_status` identity establishment for the worker-level preflight - NOT RUN
- `read_update_tag_safety_snapshot` preflight on the drift target - NOT RUN
- `list_tag_tables` comparison read - NOT RUN
- `preview_write_batch` - NOT RUN
- one authorized intermediate flag-only drift write on the disposable copy - NOT RUN
- stale-token `apply_write_batch` - NOT RUN
- restoration or discard step - NOT RUN

## Mandatory Live Results

| Criterion | Command | Observed |
|---|---|---|
| Identity-bound internal safety read succeeds with the worker-stamped identity | `pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode Read` | NOT RUN |
| Exact-target snapshot resolves PLC identity and the chosen drift flag is readable | `pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode Read` | NOT RUN |
| Flag-only drift causes stale-token `state_changed` before mutation | `pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode ApplyDrift -AllowApply` | NOT RUN |
| Public `list_tag_tables` semantics remain unchanged | `pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode Read` | NOT RUN |

The mandatory non-mutating preview command is also prepared, but has not been run:

```powershell
pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -DriftFlagName ExternalVisible -Mode PreviewDrift
```

## Required Offline and Registered Evidence

| Criterion | Evidence source | Observed |
|---|---|---|
| Bound client sends a complete expected identity; worker rejects missing and mismatched identity as `binding_conflict` | `TagUpdateSafetySnapshotWorkerClientTests` plus `TagUpdateSafetySnapshotIdentityEnforcementTests` | PASS - fresh 2026-09-02 full offline suite (2,590/2,590) |
| Requested unavailable flag fails before token issuance | `TagUpdateCurrentStateFakeWorkerTests` plus contract/worker tests | PASS - fresh 2026-09-02 full offline suite (2,590/2,590) |

## Optional Live Unavailable Probe

| Criterion | Command | Observed |
|---|---|---|
| Second target exposes an unavailable flag and preview rejects it before token issuance | `pwsh -File scripts/live-test-update-tag-safety.ps1 -ProjectPath 'C:\Disposable\ProjectCopy.ap21' -PlcName 'PLC_1' -TableName 'Default tag table' -TagName 'MotorReady' -ProbeTableName 'Legacy table' -ProbeTagName 'LegacyTag' -ProbeFlagName ExternalWritable -Mode ProbeUnavailable` | NOT RUN |

If the authorized disposable target does not expose an unavailable flag for the chosen second target,
record `NOT RUN` here and keep the acceptance gate anchored to the mandatory live rows plus the
required offline/registered evidence.
