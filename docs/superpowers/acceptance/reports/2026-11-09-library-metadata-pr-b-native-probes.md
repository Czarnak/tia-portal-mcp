# Library metadata PR B native probes - 2026-11-09

**Verdict:** B1-B10 were observed in two separate disposable headless Portal sessions. Both probe
processes exited 0 with `failures:0`. One design acceptance expectation needs correction before PR B
implementation: a read-only comment setter throws `EngineeringNotSupportedException`, so the existing
type-based classifier implies `worker_operation_failed`, not the `engineering_rejected` expected by
the shared design's section 12. Read-only language activation does throw
`EngineeringTargetInvocationException`. No change to failure classification has been approved or made.

B8's 100,000-character comment was refused recoverably, with unchanged metadata. No fatal Portal loss
was observed. The remaining observations support the proposed transaction and exact-text rules on
these fixtures. This is direct native planning evidence, not MCP feature acceptance.

## Provenance and scope

- Installed Siemens V21 `Siemens.Engineering.Base.dll` file/product version `2100.0.121.1`.
- [Standalone net48/x64 probe source and commands](inputs/2026-11-09-library-metadata-pr-b-native-probe/README.md),
  source commit `76cee08d35f74b6773acced7d503184d9c3d0c2c`, unchanged between runs.
- Executable SHA-256: `44eca5536bec3e2c61cadcbe2c90d04164dbc741f142809605957a22887597ad`.
  Fresh installed-API build: zero warnings/errors; native-free self-test passed.
- Normal run: 08:53:19-08:54:22 UTC (09:53-09:54 Europe/Warsaw), 395 records, exit 0.
  B8-only run: 08:55:08-08:55:24 UTC (09:55 Europe/Warsaw), 31 records, exit 0.
  Times bound the probe process invocations, not individual native calls. Both stderr captures were empty.
- Each invocation constructed a new `TiaPortal(WithoutUserInterface)` and created fresh library fixtures
  under a new work directory. The normal process completed before the separate B8 process began.
  Neither invocation attached to the existing Portal or opened the supplied disposable project/library.
  B6 reopened only its own fresh fixture read-only. Probe-owned libraries were closed and Portal disposal
  returned normally. Temporary fixture files remain locally for inspection.
- No MCP host/worker run, project binding, product implementation, save/reopen persistence test,
  deployment, push or merge was performed. Branch renamed to `feat/library-metadata-pr-b`.

## B1-B10 results

| Case | Native observation | Design consequence |
| --- | --- | --- |
| B1 | `en-US` comment reads back exactly inside the uncommitted transaction and after disposal. `IsModified` changes false to true. | Supports the in-transaction end-state check and explicit-save model. |
| B2 | Supported inactive `af-ZA`: `Comment.Items.Find` returns null. No setter was attempted. | Supports the proposed missing-comment `target_not_found` path; activate first. |
| B3 | Setting empty text keeps the `en-US` item with `Text == ""`, inside and after the transaction. | Empty text is a valid clear result. |
| B4 | Activating `af-ZA` creates an empty comment item, visible before commit and afterwards. The existing `en-US` comment/settings remain unchanged. | Supports allowing new empty comment items for activated languages. |
| B5 | Editing and reference tested independently on fresh fixtures. Each setter activates `af-ZA`, creates its empty comment item, changes the requested setting and preserves the other setting and `en-US`. | Supports documented automatic activation and the end-state rules. |
| B6, comments | Read-only transaction creation succeeds; `Text.set` throws `Siemens.Engineering.EngineeringNotSupportedException`: the property is read-only. Full metadata matches baseline after disposal; `IsModified` stays false. | **Contradicts section 12's blanket `engineering_rejected` expectation.** Existing classification implies `worker_operation_failed` with `engineeringError`. |
| B6, languages | Read-only transaction creation succeeds; `ActiveLanguages.Add` throws `Siemens.Engineering.EngineeringTargetInvocationException`: cannot modify read-only languages. Metadata matches baseline; `IsModified` stays false. | Existing classification implies `engineering_rejected` with `engineeringError`, as expected. |
| B7 | After a comment setter, activation and editing/reference setters, a forced managed exception before commit restores the complete metadata snapshot. `CanCommit` is true before and after setters; `IsModified` changes false to true to false. | Supports rollback of comments and languages on this failure path. |
| B8 | In its own session, setting 100,000 ASCII `C` characters throws `EngineeringTargetInvocationException` at `Text.set`: the message reports a system limit of 32767. Full metadata remains equal to baseline and `IsModified` false; cleanup completes normally. | Recoverable native refusal is an allowed probe result. Do not infer an independently tested 32767/32768 boundary or add a new cap from this single observation. |
| B9 | `Languages.Find` returns null for unsupported specific culture `aa-DJ` and neutral `en`. | Supports the proposed unsupported-language `target_not_found` paths. |
| B10 | The 45-code-unit sample containing LF, CRLF, leading/trailing spaces, `<`, `&`, Polish and Japanese text round-trips ordinally inside the transaction and after commit. | No normalization was observed. |

The public error categories above are inferences from the native exception types and the existing
`WorkerExecution.Classify` implementation. They were not returned by an MCP call in these runs.
The section 8.3 type-based failure table already agrees with the observations; section 12's read-only
acceptance wording is the conflicting expectation. Owner resolution is still needed before PR B
implementation; this report does not introduce a special-case classifier or read-only guard.

## Retained evidence

- [Normal native trace](inputs/2026-11-09-library-metadata-pr-b-native-probes.jsonl.gz): 395 JSONL records.
  The single work-root prefix in B6's `Path.get` result was replaced with `<work>`; no other bytes changed.
  Original JSONL SHA-256: `e2a04936505b472921e38f327ebe60881e406c3a7cee60c657cc12af068be7d6`.
  Retained uncompressed SHA-256: `cbed92523c1c69a6d88d0fff3155ee58e9cdec01161a0151649a6266039cdd7f`.
- [B8 native trace](inputs/2026-11-09-library-metadata-pr-b-native-probes-b8.jsonl.gz): 31 JSONL records,
  byte-exact before compression; no redaction required. JSONL SHA-256:
  `80564f52a29cdbc63adc2a463cdecfbbdf39bcd31e73f73fce580b82434ea553`.
- Each original `results.jsonl` matched its process stdout byte-for-byte. Retained traces were decompressed,
  parsed and compared with the original records, allowing only that one path replacement. Comment inputs,
  exception types/messages, nulls, collection contents and comparisons are preserved.

## Remaining limits

These fresh fixtures mostly had empty unrelated comments; preservation of unrelated nonempty comments
is weakly covered. This forced managed rollback does not qualify every native or disposal failure path.
No comment-length boundary, system/corporate-library mutation, released-type-language behavior,
save/reopen persistence, concurrent edit or bound-project/local-session MCP behavior was tested.
No PR B product behavior is implemented or accepted by this report.
