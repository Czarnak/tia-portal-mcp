# Protected FC export supplement — 2026-10-31

This closes the know-how-specific evidence gap in the [Stage 0 record](../../2026-10-31-vci-write-stage0.md). The maintainer supplied a saved protected SCL FC. Its native rejection explicitly confirms know-how protection; a Safety-language inference is unnecessary.

The exact same one-leaf SimaticML request ran on baseline `8713a476` and diagnostic `7275a3fe`. Both failed with one failed leaf, zero successes, no fallback/skips, `projectModified:false`, an empty mapping view and no exported file. Four empty export directories were created and removed during restoration. The one pre-existing workspace metadata file stayed unchanged.

The diagnostic note contains `Siemens.Engineering.EngineeringTargetInvocationException` from `Workspace.ExportObject`: **“The block is know-how protected. Export is not possible.”** Its outer message is 232 characters before anonymization, with no inner chain. A separate 66-character aggregate `WorkerOperationException` note is not another leaf failure. The canonical response still uses the baseline `worker_operation_failed` contract; the refactor's typed error contract is pending.

Independent review checked 26 original canonical responses and eight original write/audit pairs. After both restores, the project was open/unmodified, the supplied FC109/SCL was present, the base retained 216 mappings and all 217 base files matched the original fixture by relative name, size and SHA-256. The temporary root was removed. Both clients stopped and the temporary diagnostic tool was uninstalled.

| File | Purpose |
| --- | --- |
| `evidence.jsonl.gz` | 79 anonymized artifacts plus verification header; original canonical/audit correlation checked before anonymization |
| `summary.json` | Focused B2/P5 summary; unrelated matrix cases and timing slots correctly remain pending in this focused dataset |
| `verification.json` | Native message/type, paired-request equality, fixture revision and state/file preservation checks |
| `case-inputs.json` | Anonymized export and restoration macros |
| `checksums.json` | SHA-256/lengths of the other files in this folder; text inherits LF and gzip binary attributes from the parent |

Original response hashes describe private unredacted bytes. Full project-status metadata/history and PLC source exports are excluded, and actor names are anonymized. The parent evidence archive is unchanged; this supplement adds one probe variant and one paired baseline case.

The maintainer added this FC after the original timing capture. These probes retained the timed selectors/scopes and base mappings; no timing samples were repeated. Record the saved fixture addition in the later candidate comparison. This result qualifies this protected FC/SimaticML refusal, not every protection mechanism or export format.
