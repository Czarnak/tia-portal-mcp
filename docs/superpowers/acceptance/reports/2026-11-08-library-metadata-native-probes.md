# Library metadata native probes - 2026-11-08

**Verdict:** A1-A4 have been observed across two disposable headless Portal sessions. Metadata reads,
explicit author creation and multiline author round trips work on this installation. **A 10,000-character
author crashes Portal**, contradicting the proposed creation-author validation in design section 7.3.
The owner subsequently approved an admission cap of 255 UTF-16 code units after reporting
a 255-character TIA UI limit; the Task 1 decision gate is resolved (see followup below). This is native
planning evidence and an owner-approved admission decision, not MCP feature acceptance.

## Provenance and scope

- Siemens TIA Portal V21 Update 3; diagnostic product build `2100.0.121.1`.
- Direct net48/x64 Openness console; no host/worker validation, binding, elicitation or product changes.
  [Probe source and instructions](inputs/2026-11-08-library-metadata-native-probe/README.md).
- Each run started its own `TiaPortal(WithoutUserInterface)` and created user libraries in a new temporary
  directory. The probe did not attach to or use the already-open disposable project.
- Run 1: 18:23:08-18:23:45 UTC (19:23 local), source `2d2f7125`, exit 1, 157 capture records.
  Executable SHA-256: `0983583af8123f4b6d042a9a8e526ccfada9c3835906de8dfd7ca26cb9be70fb`.
- Run 2: 18:27:40-18:28:04 UTC (19:27 local), source `0757db4c`, exit 0, 162 capture records,
  final `failures: 0`. Its only native-case change excluded the known fatal author; A1-A3 repeated and
  the multiline case completed. Executable SHA-256:
  `ed98d834464259e6aafbf6cc9681449c9fa37d643b4747e969f298fc3539204f`.
- Times bound the stdout captures, not individually timestamped calls. Both builds had zero warnings and
  errors; the offline work-path/JSON self-test passed after the exclusion.
- Raw direct-call captures: [run 1](inputs/2026-11-08-library-metadata-native-probes-run1.jsonl.gz),
  [run 2](inputs/2026-11-08-library-metadata-native-probes-run2.jsonl.gz). They include the fatal stdout
  record that is outside run 1's `results.jsonl`. Work/profile paths and the current user's exact name
  were redacted; author inputs, returned authors, comparisons, arrays and exceptions were preserved.
- [Derived crash summary](inputs/2026-11-08-library-metadata-native-crash-summary.json) records the native
  diagnostic bundle hash and selected fields; it is not a raw Openness trace. The full diagnostic bundle
  remains in the local Siemens Diagnostics directory and is not committed.

## A1-A4 results

| Case | Native result | Consequence for PR A |
| --- | --- | --- |
| A1 | `IEngineeringComposition.Create(typeof(UserGlobalLibrary), {Name, TargetDirectory, Author})`, without `Comment`, returned `UserGlobalLibrary`. `Metadata probe author` read back exactly (21 characters), in both runs. | Use `typeof(UserGlobalLibrary)`. The `GlobalLibrary` fallback was unnecessary and was not tested; do not add a production fallback/retry. |
| A2 | Typed `Create<UserGlobalLibrary>(directory, name)` returned an author of `""` in both runs. | Preserve the existing omitted-author path. Describe this observed default as empty, not the Windows user; do not promise the same default on every installation. |
| A3 | All 20 property/collection reads succeeded on the fresh user library, its saved/read-only reopen, and installed system library `Buttons-and-Switches`, in both runs. No unavailable member or native null was observed. | The proposed metadata shape works on these samples. Keep null/diagnostic handling for other libraries and failures; these runs do not exercise it. |
| A4, long | Creating with 10,000 `A` characters threw `Siemens.Engineering.NonRecoverableException` in run 1. The Portal diagnostic at 18:23:40 UTC records `Reason=Openness`, inner `Siemens.Automation.ProjectManager.CallingContractViolationException`, `Author is too long`, and `ProjectCreator.ValidateParameters` in the stack. No `A4_long` directory existed in the work directory afterwards. | **Original contradiction:** this input is below default `MaxItemChars` but causes fatal loss, not ordinary native rejection. No safe maximum is established by this probe. Run 2 excluded the fatal input; the separately authorized attached repeat is recorded below. |
| A4, multiline | Run 2 created a library with `First line\nSecond line\r\nThird line`; returned length 34 and ordinal equality true. | Preserve accepted text exactly. This sample does not justify rejecting or normalizing line breaks. |

## A3 metadata detail

Both runs produced the same collection counts and language settings:

| Value | Fresh user library | Read-only user library | System library |
| --- | --- | --- | --- |
| `IsReadOnly` | false | true | true |
| `IsWriteProtected` | false | false | false |
| Author | empty string | empty string | `Siemens AG` |
| Version / family / copyright | empty strings | empty strings | empty strings |
| Comment items | one: `en-US`, empty text | same | same |
| Supported languages | 108 | 108 | 108 |
| Active / editing / reference | `en-US` | `en-US` | `en-US` |
| Used products | one: Portal V21 | same | Portal V21 and WinCC Basic V21 |
| History entries | 1 | 1 | 15 |

Dates returned ISO-formattable UTC values; size returned integer KB. Save/reopen changed the observed user
library size, so size is an observation, not an invariant. Empty strings and empty comment items must remain
empty strings/items in the metadata result. A system library being read-only does not imply its
`IsWriteProtected` property is true.

## Owner resolution and attached-repeat followup (2026-11-09)

The owner reports testing the TIA UI directly: a creation author of at most 255 characters is accepted;
longer input shows an error. The owner approved host fail-fast validation and explicitly said no
additional cap probe is needed. The agreed creation-author admission rule is nonblank and at most 255
UTF-16 code units (`string.Length`), with exact text preservation and the effective item budget as an
additional constraint. The owner's latest instruction also caps release authors now with the same rule:
a reusable length helper in `TiaMcpServer.Contracts/Shared` is to serve `LibraryWriteMethods` validation
for `create_global` and `release_type_version`, on host before worker dispatch and worker before native
calls, with a `validation_error` naming author, maximum and actual length without echoing text. Release
text/blank handling otherwise stays unchanged. This is owner evidence/approval,
not a Siemens-documented or independently probed native maximum or Unicode-counting rule.

The owner also authorized and observed one repeat attached to the already-open Portal on 2026-11-09.
Retained [native events](inputs/2026-11-09-library-metadata-attached-author-10000.jsonl.gz) record one
direct creation call at 00:29:41.9517024 UTC with 10,000 ASCII `A` characters and no `Comment`, followed
by `NonRecoverableException` at 00:29:48.6690040 UTC. An external monitor held original process handles;
its [derived process-exit summary](inputs/2026-11-09-library-metadata-attached-author-10000-process-exits.json)
records the original Portal PID 42364 exiting -1 at 00:29:48.5902130 UTC and child PID 32520 exiting -1
at 00:29:51.7538365 UTC, both before helper detach began at 00:29:58.6851946 UTC. The helper returned 2;
the retained local result states neither helper nor monitor killed these processes. This establishes
termination in that attached run, not its internal mechanism or the original headless run's process
state. It does not qualify the owner's UI boundary or Unicode counting. The monitor is process evidence,
not a native-call trace; the summary is derived from 176 local monitor records and the native events.

The local result reports no remaining test-library directory and no project save, restart, reopen or
retry. Native events redact private project/profile/work paths; the process summary includes source
hashes and no private paths. Existing probe sources still exclude the fatal case. No further live test
was performed for this decision correction; do not repeat the fatal input or search towards another crash.

## Remaining limits

No PR B transaction, comment setter or language setter was tested. No library with unavailable properties,
null language settings, oversized comments, more than 100 products or more than 50 history entries was
available in these samples; those contract paths need offline tests. Read-write/full MCP behavior,
metadata transport, payload budgets and post-create partial outcomes remain implementation/acceptance work.
No product implementation, push or deployment occurred. The shared design spec is retained for PR B.
