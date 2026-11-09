# Library metadata PR A — built-MCP acceptance (2026-11-09)

Scoped PASS for global metadata reads and creation-author admission through the freshly built MCP.
The owner-selected existing **user** library was read-only throughout. System-library metadata was
not tested: available system infos were closed, `open_global` accepts `.al21`, and the owner's latest
instruction selected the user library instead of a UI system-library opening. This report does not
substitute the earlier [direct native probes](2026-11-08-library-metadata-native-probes.md) for MCP coverage.

## Candidate and environment

- Candidate checkout: `feat/library-metadata`, `3a9f437c03905bd2f9015b09c46fc84c52c25711`.
  Executable behavior last changed at `d01a829b`; Task 6 changes documentation and its retention checker.
- Real V21 Update 3 assemblies, `UseTiaPortalReferenceStubs=false`. Serial build, no restore,
  `/p:UseSharedCompilation=false`, explicit installed V21 `net48` references and process-local TEMP/TMP.
  Build succeeded with nine existing warnings and zero errors. No installed configuration or package changed.
- Host SHA-256: `aa0f1d959a8e56183df1d2a4bb8d3df2e92ec85b73c3cb7cd65d562ca3ad5a48`.
  Worker SHA-256: `b1739099e3ea867b5a986b21f610522b95dd3eae2a75a16fba3852d40059e60e`.
  Contracts SHA-256: `f37c60eb39674347e9659c3198b6b7f60aabdfa150b7a972587ef74efbf3bd00`.
  Copied worker and host/worker contracts were hash-matched. The controller independently rechecked all five files.
- Same already-open standalone disposable project and Portal PID 22536, exact path asserted by
  `bind_project`; project initially clean. No project open, close, restart, save or content mutation.
  Baseline: 20 available global infos, zero open handles. Effective response budgets remained defaults.
- JSON-RPC MCP stdio, protocol `2025-06-18`, form elicitation advertised. The existing queued
  acceptance bridge launched the matched host; no direct native helper or installed connector performed mutations.

The first sandboxed process could not enumerate the desktop Portal. Unsandboxed execution resolved
that environment limitation. The first attached read-only preflight completed, then an owner interruption
terminated its harness. Reconciliation found no queued mutation, no pending call and no surviving
candidate host/worker; the same Portal survived. A new candidate session re-bound and rechecked the
clean project/baseline before fixture creation. No call was replayed after an unknown mutation.

## Live matrix

Receipt paths below are case IDs in the retained sanitized MCP archive. `rw-resume` is the final
read-write run; `ro-live` and `full-live` are separate correctly configured candidate hosts.

| Case IDs | Observed result |
| --- | --- |
| `rw-live/001-unbound`, `ro-live/002-unbound`, `full-live/002-unbound` | Unbound reads and full-mode write refused `binding_conflict`; no implicit adoption/open |
| `rw-resume/001-bind`, `002-status`, `003-baseline` | Exact authorized project/PID bound; clean state and zero open-library baseline |
| `rw-resume/020-inspect-flags` | Global omitted/false omitted `metadata`; true returned complete metadata; project false succeeded without metadata |
| `rw-resume/004-project-true`, `005-other-flag` | Project true and flag on `list_global_libraries` (even false) rejected whole with `validation_error` |
| `rw-resume/019-create-omitted` | Create succeeded; result contained complete metadata with `author:""` on this installation |
| `rw-resume/021-create-author` | `Task6 explicit author` returned exactly in creation metadata |
| `rw-resume/022-create-multiline` | `First line\nSecond line\r\nThird line` (34 UTF-16 units) returned exactly, preserving LF and CRLF |
| `rw-resume/023-create-boundary255` | 255 ASCII `A` characters accepted and returned exactly |
| `rw-resume/026-post-create-read` | Separate public inspections returned the exact explicit, multiline and 255-unit authors |
| `rw-resume/014-create-length-256`, `015-create-length-10000` | Host `validation_error` naming author, maximum 255 and actual length; no author echoed and no elicitation |
| `rw-resume/008-release-length-256`, `009-release-length-10000` | Same rejection before type lookup/mutation; placeholder type required no native release |
| `rw-resume/016-create-blank` | Whitespace-only supplied creation author rejected `validation_error` |
| `rw-resume/017-dryrun`, `018-decline` | Preview had no batch, metadata or prompt and created no directory; one declined elicitation blocked actual create with `access_denied`, no directory |
| `rw-resume/024-open-selected-readonly`, `025-inspect-selected` | Owner-selected existing library opened `readOnly`; inspection reported `libraryType:"user"`, `isReadOnly:true`, complete metadata and no diagnostics |
| `ro-live/004-inspect`, `005-write-denied` | Bound read-only mode returned identical selected-library metadata; `library_write` was absent and invocation returned JSON-RPC `-32602` unknown tool |
| `ro-live/001-tools`, `rw-resume/027-tools`, `full-live/001-tools` | Tool counts 9/21/21; operations and contract versions unchanged (read 1.0, write 2.0) |
| `full-live/005-create-full`, `006-inspect-full`, `007-close-full` | Own authored library created/read/closed successfully, `effects:[]`, zero elicitation, audit confirmation `policy` |
| `rw-resume/028-restoration-close`, `029-restoration-status`, `030-restoration-inventory` | Five remaining owned handles closed/discarded; same exact project status, still clean, zero open global handles |

The initial `rw-resume/006`, `007`, `010`–`013` creation requests mistakenly supplied an inapplicable
`scope`; the host safely rejected them before mutation. Corrected distinct requests above exercised
the intended cases. These client-shape mistakes are retained and are not product defects.

The known fatal 10,000-character input was exercised **only through host rejection**, never sent
directly to Siemens. The shared admission path was also checked offline. ASCII boundary acceptance
does not establish native Unicode boundary safety, and the rejected release cases do not qualify a
native release mutation. Accepted text preservation and UTF-16 counting have separate offline tests.

## Confirmation, partial outcomes and budgets

Each actual read-write lifecycle call requested exactly one form elicitation; the five-item restoration
batch also requested only one. The supplied author appeared exactly in each creation message, including
line endings. The harness returned `accept` with `confirm:true` for authorized fixture calls and
`decline` for the refusal case. Audit records matched exact original delivered response hashes:
accepted read-write calls record `user/confirmed`, decline `user/declined`, preview/admission rejection
`none/not_requested`, full applies `policy/not_requested`.

These are automated client acknowledgments. They prove the server's MCP elicitation contract, not
that a human read each lifecycle prompt. The native Openness access prompt is a separate attachment
step, not lifecycle confirmation. There were no server elicitations in read-only or full mode.

No recoverable getter failure, collection cap, oversized live value, unexpected post-create getter
failure or Portal-loss write interruption was induced natively. Fifteen focused existing offline
tests passed in this task: recoverable null/diagnostic metadata, escaping security/disposal/fatal
exceptions, captured post-create library identity and failure classification, and whole-value omission
preserving execution/verification with raised-limit recovery. These are doubles/budget tests, not native
acceptance. PR A's earlier full offline evidence remains 8,618 non-Packaging plus 39 Packaging tests;
no full suite was repeated for this documentation change.

## Restoration and evidence

Five own uniquely named created libraries remain on disk under ignored repository-local acceptance
scratch as evidence. All five handles were closed with discard, as was the selected existing read-only
user-library handle. No baseline handle was closed. Final global inventory had zero open handles,
matching baseline; the complete project status value equaled its preflight value
and `isModified:false`. Preview, declined, invalid-author, unbound and read-only-denied create paths
were absent. Candidate clients then shut down without closing the project or Portal.

The selected existing `.al21` SHA-256 before and after was
`7cc28b30d1f1a82d842e73d70252ee6f94a4f0787e49c851aa0d573068443684`.
This verifies the selected library file, not a recursive hash of every companion file. Its contents
were never edited or saved by the candidate.

- [Sanitized MCP receipts and matching audit records](inputs/2026-11-09-library-metadata-pr-a-mcp.jsonl.gz):
  50 exact-case receipts plus 22 selected audit records, including safe rejected preflights.
- [Provenance and hash index](inputs/2026-11-09-library-metadata-pr-a-provenance.json): binary hashes,
  original private receipt hashes, sanitized archive hashes and redaction rules. Audit `responseHash`
  identifies the **original** document, not sanitized response text; sanitized byte hashes are separate.
- Exact unsanitized requests, responses, wire streams, audits, fixture files and receipt-only assertions
  remain in ignored local acceptance scratch. Public copies redact project/library identity, profile/repo
  paths and current username (including observed existing-library author), preserving supplied author tests.

No deployment, remote publication, push, merge, project lifecycle change or system-library opening
occurred. Comment/language editing remains PR B. The #218 contribution follows the existing
`chore: credit library contribution` empty-commit convention with the previously recorded doerk77 trailer.
