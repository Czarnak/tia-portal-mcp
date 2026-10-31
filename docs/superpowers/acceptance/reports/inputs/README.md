Only verified raw Openness call/response evidence is kept here; everything else is at tag `docs-archive-2026-10-30`.

| File or directory | Report it backs | Raw Openness (how verified) | Notes |
| --- | --- | --- | --- |
| `2026-10-29-hmi-write-native-probes.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Decompressed and read individually: case records carry native `EngineeringTargetInvocationException` text from direct Openness calls with probe stack frames and no MCP envelope. | Complete native trace. Contains a Windows user-profile path; accepted as-is, not redacted. |
| `2026-10-29-hmi-ui-P10.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Decompressed and read individually: startup, leave-open-mode, one case with `nativeMutationReturned`, `appliedStages` and before/after snapshots; no MCP envelope. | Leave-open UI-compile probe trace. |
| `2026-10-29-hmi-ui-P12-analog.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10 (one case record, native before/after snapshots). | Case-only analog-alarm rename. |
| `2026-10-29-hmi-ui-P12-discrete.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10. | Case-only discrete-alarm rename. |
| `2026-10-29-hmi-ui-P12-table.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10. | Case-only table rename. |
| `2026-10-29-hmi-ui-P13.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10. | Delete tag, then create its case variant. |
| `2026-10-29-hmi-ui-P19.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10. | Create with a missing alarm class. |
| `2026-10-29-hmi-ui-P20-missing-member.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10. | Create with a missing struct member. |
| `2026-10-29-hmi-ui-P20-missing-tag.jsonl.gz` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | Yes. Structure-checked against P10. | Create with a missing trigger tag. |
| `2026-10-29-hmi-native-probe/` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | No (source). Maps the trace stage labels to native calls (for example `hmi.TagTables.Create`). | C# probe source and README. Edited after the run only to remove the project path; see the SHA-256 note below. |
| `2026-10-29-hmi-ui-compile-probe/` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | No (source). Produced the `ui-P*` traces; diffed against the native probe, native calls unchanged. | Leave-open probe source and README. Same post-run path edit. |
| `2026-10-29-hmi-write-native-summary.json` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | No (derived). Subset of the case records in the native trace. | Readable index of the raw trace; the trace is authoritative. |
| `2026-10-29-hmi-ui-P19-compile.png` | [2026-10-29-hmi-write-checkpoint-probes.md](../2026-10-29-hmi-write-checkpoint-probes.md) | No (UI proof). TIA Portal UI output, not an Openness call. | Maintainer-supplied screenshot. |
| `2026-10-26-plc-write-collision-namespace-probes.md` | [2026-10-26-plc-write-native-rejections-live-verification.md](../2026-10-26-plc-write-native-rejections-live-verification.md) | No. Every case is a `plc_write` call; outcomes are the tool's `engineering_rejected` / `engineeringError` fields relaying the native message, plus `plc_read` afterwards. | Only record of four collision directions cited as scoped. |
| `2026-10-29-network-write-native-rejections-live-rerun.md` | [2026-10-29-network-write-native-rejections-live-verification.md](../2026-10-29-network-write-native-rejections-live-verification.md) | No. Canonical `network_write` MCP responses with audit-v2 hashes and a restoration comparison. | Deep-linked from `NETWORK_OPERATIONS_SUMMARY.md`. |

Caveats:

- The archived probe source was edited after the run only to remove the project path, so the recorded executable SHA-256 does not match the archived source.
- The `ui-P*` case records say `uiCompile: 'pending maintainer return'`. The maintainer's UI outcomes exist only in the checkpoint report text and the P19 PNG.
- The Windows profile path in `2026-10-29-hmi-write-native-probes.jsonl.gz` is accepted as-is (no redaction).
- Files here are byte-exact: `.gitattributes` marks the directory `-text` so line endings are never normalized.
