# Library metadata PR B live verification - 2026-11-09

**Verdict:** the required built-MCP metadata cases passed on the authorized disposable V21 setup.
The optional admitted oversized-comment native case was unavailable under the effective 60,000-character
item budget; the known 100,000-character case was not submitted. There were no material contradictions
with the approved type-based refusal rules. This report adds live evidence to the existing offline
qualification; it does not claim other Portal versions, concurrent editing or hardware acceptance.

## Candidate and target provenance

- Source: `feat/library-metadata-pr-b`, `8bb495f10eb2c79a4399f8798ce576db9edcc4b4`, clean at start.
  No production code was changed during acceptance.
- Installed package: `4.3.0-local.151.g8bb495f1`. Installed host, net48 worker and contracts hashes match
  the existing Release build byte-for-byte. The worker informational version names the same candidate.
  The Debug worker is a different build and was not substituted for the installed candidate.
- Host SHA-256: `e16ff30a73a1397298842eedc6f6cc6b38a3adf5142007a85fe5070d412390f9`.
  Worker SHA-256: `343007aa4c9e314f9defbf7f0079a9e6ae61a3e9bab1b8950052c16e7822609f`.
  All three hashes and summarized private-evidence provenance are in the [provenance index](inputs/2026-11-09-library-metadata-pr-b-provenance.json).
- The installed full-mode MCP tool verified the exact authorized disposable project in Portal PID
  `33000`, with `isModified:false`. The other Portal, PID `14140`, was not attached or used.
  Full-mode worker PID `17168` resolved to the installed package; worker PID `41020` was also observed
  at that package path. The separate read-write bridge host was PID `32960` and exited zero.
  Native calls remained inside the product's net48 worker; no Openness code ran in the host process.
- Initial inventory had no open user libraries. The supplied existing user library was left unopened;
  all mutations used a fresh library under its authorized disposable directory. Before/after existing
  file SHA-256 was `7cc28b30d1f1a82d842e73d70252ee6f94a4f0787e49c851aa0d573068443684`.
  Exact personal paths and fixture names are retained privately and replaced by stable placeholders
  in the committed evidence.

## Acceptance matrix

Receipt labels below identify the verified MCP records retained in ignored local scratch.
MCP-level traces are not retained in the repository, as required by `AGENTS.md`.
An expected rejection is a passed acceptance case. A failed item is not a successful mutation.

| Case | Evidence and observed result | Verdict |
| --- | --- | --- |
| Full create then edit | `full-create-then-edit`: `create_global` then `set_comments` succeeded in one call, `effects:[]`, both trusted. | Passed |
| One comment and dirty library | The new `en-US` comment became `Preserved unrelated seed`; subsequent inspect reported `isModified:true`. | Passed |
| Two comments | `two-comments`: two existing cultures were accepted together, with exact before/after values. | Passed |
| Clear and unchanged repeat | `mixed-folder-metadata`: cleared `af-ZA` to `""`, then repeated the clear; inspect confirmed empty text and preserved unrelated `en-US`. | Passed |
| Inactive comment | `inactive-comment`: supported inactive `af-ZA` failed `target_not_found`; state inspect showed it remained inactive. | Passed |
| Unsupported/neutral cultures | `unsupported-language`, `neutral-language`, `unsupported-comment`, `neutral-comment`: `aa-DJ` and `en` failed `target_not_found`, without native exception evidence. Reads before subsequent use confirmed state. | Passed |
| Preview each operation | `preview-comments`, `preview-languages`: successful preview effects; following inspect retained the original text and only `en-US` active. | Passed |
| Activation then comment preview | `preview-activation-comment`: the comment effect had `createdByItem:1`, `before:null`; no mutation. | Passed |
| Activation then comment apply | `apply-activation-comment`: content-only unplanned call activated `af-ZA` and set its comment; both items completed. | Passed |
| Exact text | The applied comment's observed `metadataChange.after` equalled `"  LF\nCRLF\r\nZażółć 日本語 <&>  "` ordinally, including both newline forms and edge spaces. This is the worker's in-transaction end-state evidence; the sample was later explicitly cleared. | Passed |
| Explicit and automatic activation | `auto-and-explicit-languages`: activated `de-DE`, selected it for editing, and automatically activated reference `pl-PL`. | Passed |
| Automatic editing/reference separately | `automatic-editing-reference`: editing `ja-JP` and reference `fr-FR` each activated its inactive language. Public inspect preserved the unrelated nonempty comments. | Passed |
| Already active/unchanged language repeat | `repeat-languages`: succeeded with `activatedLanguages:[]` and equal before/after editing/reference values. | Passed |
| Unrelated nonempty comment preservation | The `en-US` seed survived activation, other-culture writes/clear, language changes, save/reopen and read-only failures. Later language edits also preserved the nonempty `de-DE` comment. | Passed |
| Mixed folder and metadata verification | `mixed-folder-metadata`: the unrelated master-copy folder stayed trusted; the repeated metadata pair became superseded/trusted. No master copy or PLC/type mutation was needed. | Passed |
| Save, close, reopen persistence | `save-close-reopen`, `persistence-after-reopen`: public read matched saved comments, active languages and editing/reference; the reopened library was clean. | Passed |
| Discard and supersession | `discard-supersession`, `readonly-baseline`: the preceding comment was superseded by discard-close; reopened `en-US` retained the saved seed instead of the discarded edit. | Passed |
| Read-only comments | `readonly-comment`: failed `worker_operation_failed`, attached `engineeringError.type:EngineeringNotSupportedException`; attached `metadataChange` retained before-text with `after:null`. | Passed |
| Read-only language activation | `readonly-language`: failed `engineering_rejected`, attached `engineeringError.type:EngineeringTargetInvocationException` and typed `metadataChange`. | Passed |
| Read-only failed-state inspection | `readonly-after-comment`, `readonly-after-language`: complete metadata equalled the read-only baseline and `isModified:false` after each attempted write. No automatic retry was used. | Passed |
| Read-write content confirmation | `rw/003-content-only`: successful metadata call, exactly zero elicitation requests in its wire interval. | Passed |
| Read-write lifecycle confirmation | `rw/004-single-confirmation`: `save_global` + comments + languages succeeded after exactly one `elicitation/create`; client returned `accept` with boolean `confirm:true`. Matching audit recorded user confirmation. | Passed |
| Read-write staging restriction | `rw/005-staging-restriction`: create then metadata failed planning with root `target_not_found`, `batch:null`, no elicitation; the proposed library directory never existed. | Passed |
| Oversized native refusal through MCP | Known 100,000-character input exceeds the effective default item budget of 60,000. No higher-budget MCP session or substitute size was used. The isolated native refusal remains Task 0 evidence, not a passed MCP case. | Unavailable, optional |
| Final restoration | `cleanup-close`, `final-status`, `final-inventory`: fresh library discarded/closed, project remained open and clean, no libraries remained open. Fresh fixture directory removed after exact path validation; existing library hash unchanged. | Passed |

## Failure interpretation

The read-only results match [Task 0's native observations](2026-11-09-library-metadata-pr-b-native-probes.md):
comments produce `EngineeringNotSupportedException`, while activation produces
`EngineeringTargetInvocationException`. The shared classifier correctly maps these to
`worker_operation_failed` and `engineering_rejected`, respectively. Both are attempted item failures
with root `error:null`, `success:false`, MCP `isError:false`, and attached typed failure evidence.
Unsupported/inactive/neutral targets instead fail exact native lookup with `target_not_found` and
have no `engineeringError`. These categories do not imply a successful mutation. No `portal_lost`
or unexpected product failure occurred. Native Task 0 was reused rather than rerun.

## Evidence verification and cleanup

- Private evidence: 43 MCP receipts, 28 audit records matched by original canonical response hash,
  and two bidirectional read-write wire records, totalling 73 JSONL records, retained only in ignored
  local scratch. This permanent report retains summarized acceptance facts rather than MCP traces.
- 99 offline assertions passed: text/structured equality for every receipt, exact Unicode/newlines,
  expected categories and typed outcomes, metadata preservation, persistence/discard, supersession,
  exact per-request elicitation counts, matching audits, installed/Release binary identity and cleanup.
  The reused bridge's native-free self-check passed. Its lifecycle answers are automated protocol
  evidence; they do not prove a human saw a prompt.
- The private archive hashes are recorded in the provenance index; the archive itself is not committed.
  Decompression/parsing and sanitization were checked before it was moved to ignored scratch.
  Original audit `responseHash` identifies the private canonical bytes and differs from hashes of
  reserialized sanitized text. Original per-receipt hashes also remain in private scratch.
- Private raw receipts, wire files, audit details, process provenance, the sanitized archive and the
  offline assertion script remain in ignored local scratch. A Windows-default text-decoding issue in the offline packager was fixed to
  explicit UTF-8 before verification; it did not affect the MCP request or native text round trip.
- The fresh library was closed with discard, its directory was deleted, and the staging test created
  no directory. The original user library was neither opened nor changed. The existing project
  remained bound, open and unmodified. No project content, PLC, device or other Portal was mutated.

No new length boundary search, author probe, broad build/test rerun, hardware operation, push or PR
was performed by this acceptance task. The optional oversized MCP native case remains unavailable;
native fault locations and concurrent editing beyond this matrix remain untested.
