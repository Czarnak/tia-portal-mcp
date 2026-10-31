# Native path-length supplement — 2026-10-31

The maintainer requested native testing before ruling on the numeric length check. The original [Stage 0 probe](../../2026-10-31-vci-write-stage0.md) had retained that check and could not reach the native boundary.

The new diagnostic build changes only the numeric refusal in `VciMirroredPath.FirstProblem` and the existing exception-note instrumentation. Filename/segment, containment, collision, format and workspace name/root checks remain enabled. Baseline source is `8713a476`; the diagnostic commit is `b792c2e827a7f51f1a4311fb48cbf390f3d0d8b5`, version `4.2.0-stage0.pathprobe.gb792c2e8`.

One known valid unmapped LAD FC was exported as SimaticML into a fresh workspace for each case. Paths contain ASCII characters only. Each baseline/probe request pair is identical; each case restores the project and removes its own files before the next attempt. Parent directories remain 237–245 characters, separating this experiment from longer directory-creation paths. The real control output calibrated the stem and `.xml` lengths before the remaining cases were queued.

Lengths below describe original private Windows paths before anonymization; the published aliases are shorter.

| Stem without extension | Complete `.xml` path | Parent directory | Baseline | Length-only diagnostic |
| ---: | ---: | ---: | --- | --- |
| 251 | 255 | 237 | Exported; Equal | Exported; Equal |
| 252 | 256 | 238 | Guard blocked | Exported; Equal |
| 253 | 257 | 239 | Guard blocked | Exported; Equal |
| 254 | 258 | 240 | Guard blocked | Exported; Equal |
| 255 | 259 | 241 | Guard blocked | Exported; Equal |
| 256 | 260 | 242 | Guard blocked | Native ETI; no file/mapping |
| 259 | 263 | 245 | Guard blocked | Native ETI; no file/mapping |

All five diagnostic successes produced exactly the requested untruncated filename/path, one XML file and one `Equal` mapping to the selected FC. The current length guard therefore blocks four valid outputs in this fixture.

Both diagnostic failures raised `Siemens.Engineering.EngineeringTargetInvocationException` from `Workspace.ExportObject`. The 96-character outer message is generic and has no inner chain; it does not state a path-length reason. Each also has a separate 66-character aggregate `WorkerOperationException` note. No ordinary I/O exception was surfaced. The requests differed in controlled path lengths, but these results do not identify the native internal limit: with fixed `.xml`, stem `<256` and complete path `<260` yield the same observations.

The writer creates directories before `ExportObject`; successful and natively refused exports each created six directories. Failed exports left no file or mapping. Baseline guard refusals created neither export directories nor files. These effects were recorded before cleanup.

The maintainer's final ruling retains the numeric length diagnostic and corrects its conservative calculation using this SimaticML evidence. Complete `.xml` paths through 259 characters succeeded; the native 260/263 refusals did not explain their cause. Other export formats, longer parent directories and lengths outside this grid remain unqualified. The maintainer separately accepted removing export collision prevention and leaf filename-rule duplication, while retaining containment and directory-name boundary protection before managed filesystem operations. Those changes are recorded in the [Stage 0 decisions](../../2026-10-31-vci-write-stage0.md#maintainer-decisions-and-completion-boundary); they were not implemented or exercised by this length-only diagnostic.

Independent review verified 124 original canonical responses, 56 original audited writes, seven exact request pairs and 14 clean restorations. Both sessions retained the saved Protected FC109/SCL, base216 mappings and all 217 base files/directories unchanged. Both clients stopped; the diagnostic installation and branch were removed after preserving its source in a verified private bundle and this published patch. Production source is unchanged. No timing samples were measured.

| File | Purpose |
| --- | --- |
| `evidence.jsonl.gz` | 448 anonymized artifacts plus verification header; original canonical/audit/hash/time correlation checked first |
| `summary.json` | Focused B3/P6 summary; unrelated cases/timing slots correctly remain pending |
| `length-ledger.json` | Original numeric lengths and aliased paths; Windows `ntpath` checks and actual output calibration agree |
| `case-inputs.json` | Exact paired control/matrix command shapes with private values anonymized |
| `build-provenance.json`, `probe.patch` | Exact temporary source, tests and payload hashes; includes both host and worker Contracts copies |
| `verification.json` | Derived path, mapping, exception and restoration checks |
| `checksums.json` | SHA-256/lengths of the other files in this subfolder |

The focused VCI suite passed 540 tests with one intentional old-length-assertion failure; the full non-packaging suite passed 7,643 with that same failure and no unexpected failures/errors/timeouts/aborts. The exact assertion passed before patching. Host/worker payloads and both packaged/installed Contracts copies matched the fresh build; packages contain no Siemens DLLs. Packaging-category tests were not required because no product project files changed.

Original response hashes describe private unredacted bytes, not anonymized bytes. Full project-status metadata/history and PLC source exports are excluded; lifecycle actor names are anonymized. Text inherits LF and the archive remains binary. The original timing/main archive and protected-FC archive are unchanged.

[Siemens V21 documentation](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-version-control-interface/exporting-individual-object-to-workspace) documents an ETI for excessive `RootPath + RelativeWorkspaceDirectoryPath + ObjectName` length. The original tool instead budgeted 259 characters including an eight-character `.hmi.yml` allowance; this experiment records observed `.xml` behavior without assuming the two length definitions are identical.
