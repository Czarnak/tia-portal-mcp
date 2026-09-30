# JSON Contract Phase 2 — Live Verification

Date: 2026-10-01, Europe/Warsaw. Result: **PASS for the observed cases; the complete live producer gate remains open.**

## Candidate and Authorization

The maintainer installed the current branch build and authorized live verification through
`tia-mcp` against the already-open project. This report calls that exact project **Fixture A**;
its private name and path are retained only in ignored local evidence.

- Branch: `phase2-json-contract`.
- Frozen candidate: `b8e5508246c5348de775e6583ddbf46c09ba0a5c`.
- Installed package and assembly product version: `3.0.1-local.94.gb8e5508`.
- Live calls: installed MCP tools backed by TIA Portal V21 and its net48 Openness worker.
- Captured window: 2026-09-30 21:58:39 through 22:02:15 UTC, spanning midnight in Europe/Warsaw.

The running worker's installation path identified the candidate package. Installed file hashes
were read without rebuilding or replacing the package:

| Installed file | SHA-256 |
| --- | --- |
| `TiaMcpServer.dll` | `105612F4CE3B236390A43B9F2FDAF294EACDF33DE762AE923AC847371F678401` |
| `TiaMcpServer.Contracts.dll` | `FF837C3C080620E3D0C5D2CD2A37A6FE20E3168E7553DC028F4D6B1DAA2F83A7` |
| `openness-worker/TiaMcpServer.OpennessWorker.exe` | `0BCCF0A566E3E1335CDBC3F8D107786573336095E33642B67B46AB1F32FA3140` |

## Observed Results

All calls were serial. Positive and ambiguous-block checks repeated Fixture A's exact path.

| MCP call | Observed response |
| --- | --- |
| `get_project_status(projectPath=Fixture A)`, before and after compilation | `success: true`, `error: null`, `result.status: succeeded`, `isError: false`; typed status and metadata for the same open project |
| `compile_check(projectPath=Fixture A)` | Three PLC reports: `PLC_LAD`, `PLC_FBD`, and `PLC_DP`; `overallState: Success`, zero errors and warnings, `success: true`, `isError: false` |
| `compile_check(projectPath=Fixture A, plcName=PLC_LAD)` | One PLC report, `scope: plc`, explicit `blockPath: null`, zero errors and warnings |
| `compile_check(projectPath=Fixture A, plcName=PLC_LAD, blockPath=Main)` | Ambiguous block lookup: `success: false`, `error: null`, `result.status: failed`, typed `worker_operation_failed`, `value: null`, `isError: false` |
| `compile_check(projectPath=Fixture A, plcName=PLC_LAD, blockPath=PLC_LAD/Blocks/Main)` | Qualified block scope succeeds with its path retained and zero errors and warnings |
| `get_project_status(projectPath=<NUL>)` | Pre-operation `binding_conflict`: `success: false`, top-level `error`, explicit `result: null`, `isError: true` |
| `compile_check(projectPath=<NUL>)` | The same pre-operation binding rejection shape |
| `browse_project_tree(projectPath=Fixture A, depth=6, pageSize=100)` | Successful discovery used to select the PLC-global `Main` block; 100 of 132 snapshot nodes returned |

The live compiler reported that blocks were up-to-date. These responses establish successful
compiler invocation and report delivery, not a forced rebuild of changed source.

Before the final reconnection, compilation requests were rejected with `binding_conflict`
because the server session lacked a verified write binding. Status reads correctly did not
adopt an unbound session. Following the maintainer's reconnection and explicit status read,
compilation ran against Fixture A. No rejected request was an unknown mutation outcome.

## Contract Checks and Evidence

The nine captured calls include eight standalone responses and the supporting tree read.
**87 assertions passed**: one text block, semantic equality with `structuredContent`, recursive
ordinal property ordering, compact JSON, and the document limit; standalone version/envelope
members and rejection/outcome shapes; compiler report/message members, count totals, and
compilation success classification. Nullable outcome members and PLC-scope `blockPath` were
present explicitly. Reports and metadata were JSON values rather than nested JSON strings.

The first local evidence check incorrectly expected rejection to omit `result`; it was corrected
to the documented `result: null` contract. This required no production change.

Raw tool arguments, responses, UTC timestamps, assertions, and installed hashes are preserved
in ignored `priv/json-contract-phase2/live-2026-10-01/tool-evidence.json`. Earlier offline build,
coverage, and independent review evidence remains in the
[offline validation report](2026-09-30-json-contract-phase2-offline-validation.md).

## Project State and Remaining Acceptance

The initial status reported `isModified: false`; the final status reported `isModified: true`
for the same still-open project after the compilation checks. No save, close, project switch,
source edit, apply-write operation, or PLC runtime command was issued during this verification.

The [implementation plan's](../../plans/2026-09-30-json-contract-phase2-standalone-tools.md)
complete live producer gate remains unchecked. The clean fixture did not exercise compiler
diagnostics with errors, warning-only or unknown compiler states, or response omission. Keeping
the authorized project open did not exercise no-project status. The read-write connection did
not expose the six full-mode lifecycle tools, so their live wire compatibility was not tested.
The ambiguous-block outcome establishes an attempted worker failure, not a compiler-error report.

This report does not establish persistence, download, PLC execution, or plant acceptance.
No code fix was needed, and this documentation follow-up does not change the tested executable tree.
