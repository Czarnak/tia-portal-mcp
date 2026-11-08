# Global-library metadata (`library_read` / `library_write`) — design

Refs #218. Two PRs: **PR A** reads metadata and sets the author at creation; **PR B** edits comments and
languages. Neither closes #218.

## 1. Intent

Agents can see who made a global library, what it is for and which languages it uses, and can keep its
comment and language settings current, without opening the TIA UI.

The feature comes from the contributor's updated #218 bundle (cumulative patch on `v4.2.0`). We rebuild it on
main's library design and credit the contributor; the bundle is a reference, not a source to copy. Its
native findings were checked against the V21 API documentation and reflection; the parts we depend on are
listed in §5 and are re-proved by our own probes before code is written.

## 2. Goals, non-goals, anti-goals

**Goals**

- PR A: `inspect_library` on a global library returns metadata when asked (`includeMetadata: true`).
- PR A: `create_global` accepts an optional `author` and always returns the new library's metadata.
- PR B: `set_comments` sets or clears the library comment for named languages.
- PR B: `set_languages` activates languages and sets the editing and reference languages.

**Non-goals (this design)**

- Removing or deactivating a language. Portal supports it (`LanguageAssociation.Remove`); it needs its own
  rules for what happens to that language's comment. Follow-up.
- Project-library metadata. A project library has none of its own; the owning project's metadata is not
  substituted.
- Setting name, author after creation, version, family or copyright. V21 has no setter for any of them
  (reflection: get-only; Siemens "Accessing global libraries": access Read).
- A collision check on comment edits (`expectedTextHash`). Owner decision: last writer wins. It can be
  added later as an optional field without breaking callers.
- Unbound (Portal-only) global operations, project-library writes on local sessions. Unchanged.

**Anti-goals**

- No implicit save, close, reopen or retry. Metadata edits leave the library modified; the caller saves with
  `save_global` or `close_global`.
- No planning pass on apply (ADR-0017), no fingerprint, no server-side read-only guard (IMPROVEMENT_LOG
  precedent: the old bundle's read-only guards and verify step were not ported). Portal is the refusal
  authority.
- No second verification read, after the call or after a failure. The end-state check runs inside the
  transaction (§8.4); every failure before commit is rolled back by the transaction, so the state that
  remains is the before-state and needs no re-read.
- No message-text matching. Failure categories follow exception types (AGENTS.md "Failure categories").

## 3. Constraints

- Every library call needs a verified binding; global libraries are resolved in the bound Portal
  (ADR-0015). Only libraries already open in that Portal can be targeted.
- `library_write` keeps 1–50 items in caller order, stop at first failure, no batch rollback
  (`Safety/Pipeline`). Read-write prompts only for global-library lifecycle (ADR-0016).
- Worker payloads use explicit nulls; members that are omitted when null are declared
  `JsonIgnore(WhenWritingNull)` and registered in `ConditionalMemberRegisterTests` (AGENTS.md).
- Budgets read `StructuredOperationBatchPayloadBudget.MaxItemChars` / `MaxDocumentChars`, never a literal.
- Siemens property reads use `OpennessExceptions.IsRecoverablePropertyFailure`; anything else (a disposed
  object, a lost Portal) fails the whole call so `portal_lost` is classified and the binding invalidated.
- Reference stubs get the new members from V21 reflection, not from the bundle. Touching `reference-stubs/`
  requires the Packaging tests.

## 4. Decisions taken (owner, 2026-11-08)

| # | Decision |
| --- | --- |
| D1 | Rebuild on main with credit; the bundle is reference only. |
| D2 | Failures are thrown with their real category; one end-state check inside the transaction is the only postcondition (`postcondition_failed`, with the observed state attached); no read-only guard; no extra verify step. |
| D3 | Metadata is a flag on `inspect_library` (`includeMetadata`), global scope only. All write fields are flat item members. `create_global` returns metadata. |
| D4 | Metadata edits get their own verification key (library + `metadata`). |
| D5 | Size limits follow the configured budgets; previews and results show only the languages the item names. |
| D6 | Last writer wins for comment edits. |
| D7 | PR A = read + author; PR B = comment and language edits, after native-rejection probes. No prompt for metadata edits. |

## 5. Native facts relied on

Verified from V21 reflection and Siemens documentation during the bundle review; each is re-proved by a probe
(§6) before the matching PR is built.

| Fact | Source | Probe |
| --- | --- | --- |
| `GlobalLibrary` exposes get-only `Author`, `Version`, `Family`, `Copyright`, `CreationTime`, `LastModified`, `LastModifiedBy`, `Size` (KB), `Comment` (`MultilingualText`), `LanguageSettings`, `UsedProducts`, `HistoryEntries`. | reflection; "Accessing global libraries" | A3 |
| `GlobalLibraryComposition` implements `IEngineeringComposition.Create(Type, parameters)`; creation parameters `Author` (mandatory on this path), `Comment` (optional), `Name`, `TargetDirectory`; returns a `UserGlobalLibrary`. Which `Type` to pass is undocumented. | "Creating global libraries" | A1 |
| `Comment.Items.Find(Language)` returns an item; `MultilingualTextItem.Text` is settable. | reflection; "Accessing and enumerating multilingual texts" | B1 |
| `LanguageSettings.Languages.Find(CultureInfo)`, `ActiveLanguages.Add(Language)`, settable `EditingLanguage` / `ReferenceLanguage`. | reflection; "Accessing global library languages" | B4, B5 |
| Siemens: setting a new supported language through Openness adds it to the active languages. | same page | B5 |
| Siemens: language changes do not change languages of released type versions in the library. | same page | — (documented) |
| `GlobalLibrary` is `ITransactionSupport`; `ExclusiveAccess.Transaction(library, text)` with `CommitOnDispose`. Opening/closing a global library is not allowed inside a transaction. | reflection; "Transaction handling" | B7 |

Unknown and therefore probed: whether comment items exist only for active languages; whether activation
creates an empty item; whether clearing removes the item or leaves `""`; the exception type when a
read-only library is edited; native length limits for author and comment text; whether
`EditingLanguage`/`ReferenceLanguage` can read as null.

## 6. Native probes

Each PR starts with a probe console (same pattern as the PR 3 probe in its evidence folder), run against a
disposable library in a disposable Portal session. Raw Openness call/response files that support a
documented behaviour are kept under `docs/superpowers/acceptance/reports/inputs/`.

**PR A (Task 0)**

- **A1** `Create(typeof(UserGlobalLibrary), {Name, TargetDirectory, Author})`, no `Comment`: succeeds,
  returns a `UserGlobalLibrary`, `Author` reads back. If `UserGlobalLibrary` is refused, retry with
  `typeof(GlobalLibrary)` and record which one works.
- **A2** Typed `Create<UserGlobalLibrary>(dir, name)`: record the author Portal sets, so docs can say what
  omission means.
- **A3** Read every metadata member on a fresh library, a library opened read-only and a system library if
  one is installed; record nulls, history length, used products.
- **A4** Author of 10,000 characters and an author with line breaks: native accept or reject (type).

**PR B (Task 0)**

- **B1** Inside `ExclusiveAccess` + `Transaction(library)`: set one active culture's comment, read it back
  before commit (the end-state check depends on reads inside an uncommitted library transaction seeing the
  new value), commit; `IsModified` becomes true.
- **B2** `Comment.Items.Find` for a supported but inactive language: null or an item; if an item, whether
  writing it succeeds.
- **B3** Set `""`: item kept with empty text, or removed.
- **B4** Activate a language: is a comment item created, and with what text.
- **B5** Set `EditingLanguage` and `ReferenceLanguage` to an inactive language: Portal activates it, or
  throws (type).
- **B6** Edit a library opened read-only: where it throws (transaction creation or setter) and the exception
  type.
- **B7** Throw after setters, before commit: comments and languages are back to the before state. Record
  `CanCommit` before and after the setters.
- **B8** Comment text of 100,000 characters: native accept or reject.
- **B9** `Languages.Find` for an unsupported culture and for a neutral culture (`en`): null or throw.
- **B10** Text round trip: a comment with LF and CRLF line breaks, leading and trailing spaces, `<`, `&`
  and non-Latin characters reads back exactly (ordinal), inside the transaction and after commit.

A probe result that contradicts this design stops the PR and comes back to the owner.

### 6.1 PR A findings and owner gate (2026-11-08)

[A1-A4 report](../acceptance/reports/2026-11-08-library-metadata-native-probes.md): explicit-author
creation succeeded with `typeof(UserGlobalLibrary)`; the typed omitted-author path returned an empty
author on this installation. All metadata reads succeeded on fresh, read-only and one installed system
library. The LF/CRLF author round-tripped exactly.

**Contradiction:** a 10,000-character author caused `NonRecoverableException` and a Portal crash report
with `CallingContractViolationException` / `Author is too long`. Section 7.3's `MaxItemChars`-only rule is
therefore insufficient; the safe native maximum is not established. Do not treat this as a recoverable
native refusal, repeat the fatal input or infer a safe cap from the successful short samples.

**Owner ruling:** keep author creation in PR A, but block implementation until a safe author-length rule
is verified and agreed. The [PR A implementation plan](../plans/2026-11-08-library-metadata-pr-a.md) begins
with this gate. Section 7.3 remains pending that explicit correction; no replacement limit is approved.

**Retention:** retain this complete shared spec and the PR A plan through PR A; there is no documentation
pruning at the end because PR B needs this spec. PR B's design and probes remain unimplemented.

## 7. PR A — read metadata, author at creation

### 7.1 `inspect_library` with `includeMetadata`

- New optional item member `includeMetadata` (boolean). Valid only for `inspect_library`; any other
  operation with it is a `validation_error` (same closed-member rule as the other members).
- `includeMetadata: true` with `scope: "project"` is a `validation_error`: "Metadata is available for
  global libraries only."
- `false` or omitted: today's row, unchanged. `inspect_library` stays unpaged; the flag is not part of any
  cursor.
- The worker method stays `library_global_inspect_library`; `LibraryQueryInfo` gains the flag. No new
  capability or policy entry.

### 7.2 Metadata object

`LibraryEntryInfo` gains a conditional `metadata` member, present only on a global `inspect_library` row
when the flag is true (registered in `ConditionalMemberRegisterTests`). The same type is used by
`create_global`'s result.

```text
metadata: {
  author, version, family, copyright, lastModifiedBy : string | null
  creationTime, lastModified                         : ISO 8601 string | null
  sizeKilobytes                                      : integer | null
  comments          : [{ culture, text }] | null      sorted by culture, ordinal
  supportedLanguages: [culture] | null                sorted
  activeLanguages   : [culture] | null                sorted
  editingLanguage, referenceLanguage : culture | null
  usedProducts      : [{ name, version }] | null      first 100, native order
  history           : [{ dateTime, text }] | null     first 50, native order
  complete          : boolean
  diagnostics       : [string]
}
```

- The library name stays in `identity`; read-only, write-protected and modified stay on the row.
- A member that cannot be read is null with a `diagnostics` entry (`"author unavailable: <ExceptionType>"`),
  using `IsRecoverablePropertyFailure`; a null `EditingLanguage` is null without a failure.
- `complete` is false when a member is unavailable or `usedProducts`/`history` was cut; the cut adds a
  diagnostic naming the cap. Comments and languages are never cut: if the row then exceeds the item budget,
  the existing budget omission applies to the whole value and the caller can raise `--max-item-chars`.
- Comment text is returned whole (no per-text cap). `comments` lists every native comment item, including
  items with empty text, so a caller sees which languages have an item.
- Culture values everywhere (read, results, previews, outcomes) are `Language.Culture.Name` as Portal
  reports them.

### 7.3 `create_global` with `author`

- New allowed member `author` on `create_global` (the existing `author` field, today release-only, gains a
  second use; its description names both). Non-blank; at most `MaxItemChars` characters. Other limits are
  Portal's (probe A4).
- Omitted: today's typed `Create<UserGlobalLibrary>(dir, name)`, unchanged.
- Present: `IEngineeringComposition.Create(<type from A1>, {Name, TargetDirectory, Author})`. No `Comment`
  parameter (Portal would write it in an undocumented culture). The returned object is taken with an `as`
  cast; if it is not a global library, the item fails `postcondition_failed` with a message that the library
  may exist on disk and must be inspected.
- Read-write confirmation text adds the author when present:
  `create_global: directory '…', name '…', author '…'.`
- Result: `LibraryWriteResultInfo` gains conditional `metadata` (§7.2 type), filled for every
  `create_global`, with or without author. It is the state right after creation; a later item in the same
  call can change it (§8.7).
- A member that cannot be read (`IsRecoverablePropertyFailure`) is null with a diagnostic and
  `complete: false`; the create still succeeds.
- Any other failure while reading metadata after the library exists is caught like the existing
  `close_global`-after-save path: classified by type (`WorkerExecution.Classify`), `engineeringError`
  attached so a Portal disposal still becomes `portal_lost`, and `libraryWriteOutcome.library` (new
  conditional identity member) names the library that now exists, with a message saying so. Retrying the
  same create would collide.
- Dry run: the `create_global` plan stays empty (today's behaviour); the author is only echoed in the
  confirmation text.

## 8. PR B — comment and language edits

### 8.1 Operations and members

| Operation | Members (besides `operationId`, `operation`) |
| --- | --- |
| `set_comments` | `scope: "global"`, `libraryPath` or `libraryName`, `comments: [{culture, text}]` |
| `set_languages` | `scope: "global"`, `libraryPath` or `libraryName`, at least one of `activateLanguages: [culture]`, `editingLanguage`, `referenceLanguage` |

Request rules (host and worker `Validate`):

- `scope` must be `global` for both; project scope is a `validation_error`.
- Culture syntax: a name `CultureInfo.GetCultureInfo` resolves (the rule HMI writes use, moved to one
  shared helper). Each culture is canonicalized to `GetCultureInfo(name).Name` before any uniqueness check
  or comparison. Whether the library supports it is Portal's answer (§8.3).
- `comments`: non-empty; each entry has a culture and a non-null `text`; canonical cultures unique. `""`
  clears that language's comment. Languages not named stay unchanged.
- The sum of all comment text lengths in one item is at most `MaxItemChars`.
- `activateLanguages`: if present, non-empty, canonical cultures unique. An already active language is
  skipped (no `Add` call), so repeating an activation changes nothing. Editing and reference language are
  set only when they differ from the current value.
- New operations join a new `MetadataOperations` list. Like lifecycle operations they have no project
  variant: the method arrays must exclude them from `library_project_*`. Worker methods:
  `library_global_set_comments`, `library_global_set_languages` (ProjectMutation, capability
  `library_write`, so allowed on standalone projects and local sessions, `notYetDelivered` on server
  projects, as today).
- No prompt in read-write (`ConfirmsCall` unchanged: lifecycle only). No save.

### 8.2 Apply

One worker call per item, inside the mutation method. Apply is unplanned (ADR-0017): each item resolves
against the state left by earlier items, so `set_languages` activating de-DE followed by `set_comments`
de-DE works in one call.

1. Resolve the library (`LibraryWriteTargets.Library`).
2. `portal.ExclusiveAccess(...)`, then `access.Transaction(library, ...)`. Everything below runs inside
   both, so no other client can change the library between the before-read and the check.
3. Resolve every culture to a native object (`Languages.Find`, then `Comment.Items.Find` for
   `set_comments`). All lookups finish before the first setter.
4. Read the before-state of the parts §8.4 compares.
5. Run the setters: comment texts; or `ActiveLanguages.Add` for each language not yet active, then editing,
   then reference language.
6. Read the end-state and compare (§8.4). Mismatch: throw `postcondition_failed`.
7. If `CanCommit` is false: throw `worker_operation_failed` ("Portal did not allow the metadata change to
   commit; nothing was changed.").
8. `CommitOnDispose`, then dispose.

A throw in steps 3–7 disposes the transaction without commit, so Portal undoes every setter (Siemens
"Transaction handling"; probe B7).

### 8.3 Failures

| Situation | Category |
| --- | --- |
| Library not open in the bound Portal / ambiguous | `target_not_found` / `target_ambiguous` (existing) |
| `Languages.Find` returns null (unsupported or neutral culture) | `target_not_found`: "Language '<c>' is not supported by library '<name>'." |
| `Comment.Items.Find` returns null (language not active, per B2) | `target_not_found`: "Library '<name>' has no comment for '<c>'; activate the language with set_languages first." |
| Native rejection (`EngineeringTargetInvocationException`), e.g. read-only library | `engineering_rejected` with `engineeringError` (existing classification) |
| Other Siemens `EngineeringException` | `worker_operation_failed` with `engineeringError` |
| Portal lost / disposed | `portal_lost`; binding invalidated (takes precedence over every row) |
| End-state mismatch | `postcondition_failed`; message names each mismatched culture or setting |
| `CanCommit` false | `worker_operation_failed` (§8.2 step 7) |
| Exception during commit/dispose after `CommitOnDispose` | classified by type; message: "the change may or may not have been committed; inspect with includeMetadata before retrying" |

Outcome (`libraryWriteOutcome.metadataChange`, §8.5) for failures raised inside the transaction (steps
3–7): `before` holds the before-state of the named parts when step 4 completed, and `after` holds what the
in-transaction read observed when step 6 ran, or null for parts never reached. The message states that the
transaction was rolled back, so the library still has the `before` state. Failures thrown inside the
transaction are wrapped with the outcome and classified by type, as in `LibraryTypeWriter.WithOutcome`.
Siemens exceptions keep `engineeringError`, so the existing Portal-disposal re-check still runs. A commit or
dispose failure carries no outcome, because the state is unknown. No failure path reads the library again
after the transaction.

### 8.4 End-state check

Text compares ordinal and exact (probe B10; a probe showing normalization stops PR B). Cultures compare by
canonical `Culture.Name`.

`set_comments` passes when:

- every named culture reads back its requested text (for `""`: empty text or no item, per B3);
- every other comment that existed before is unchanged;
- active, editing and reference languages are unchanged.

`set_languages` passes when:

- every requested activation is active, and editing/reference equal the request when given;
- an editing or reference language that was not requested equals its before-value;
- every language active before is still active;
- the only newly active languages are the requested activations plus the requested editing/reference
  language (Siemens documents that setting an inactive one activates it; probe B5);
- every comment that existed before is unchanged; new empty comment items for newly active languages are
  allowed (B4).

Neither check compares timestamps, size or history (Portal changes them). Name, author, version, family and
copyright have no setter, so they are not compared.

### 8.5 Results, previews, outcomes

`LibraryMetadataChangeInfo` — shows only what the item names:

```text
metadataChange: {
  comments          : [{ culture, before, after }] | null   set_comments
  activatedLanguages: [culture] | null                       set_languages; languages that became active
  editingLanguage   : { before, after } | null               when requested or changed
  referenceLanguage : { before, after } | null               when requested or changed
}
```

- `before: null` means the language had no comment item. A clear always shows `after: ""`, whether Portal
  keeps an empty item or removes it (B3). `after: null` appears only in a failure outcome, for a part the
  in-transaction read never reached.
- Apply result: `LibraryWriteResultInfo.metadataChange` with the observed values, plus `library`.
- Failure outcome: `LibraryWriteOutcomeInfo.metadataChange` (§8.3) and, for the `create_global` path,
  `LibraryWriteOutcomeInfo.library` (§7.3). Both are validated by
  `LibraryWritePayloadContract.ValidateEnvelopeOutcome` like the other outcome members; malformed evidence is
  `protocol_error`.
- New members are conditional (`WhenWritingNull`) on `LibraryWritePlanInfo`, `LibraryWriteResultInfo`,
  `LibraryWriteOutcomeInfo` and `LibraryWriteEffect`, each registered with a "when present" description.
  Their lists get the same null-element checks as the existing identity and type members, on the success
  path and the outcome path.
- Budget: the request cap (§8.1) bounds the new text, but `before` is existing text of any length. If an
  item's `metadataChange` exceeds the item budget, the existing budget code omits that value. The item stays
  applied and trusted, root delivery `success` becomes false, and the caller reads the state with
  `includeMetadata`. This is today's behaviour for every oversized value, not a new rule.

### 8.6 Planned calls

Dry runs and read-write calls that prompt (they contain a lifecycle item) plan every item before any runs
(ADR-0016/0017):

- The plan (`library_global_plan`) resolves the library and cultures without setters. It returns
  `metadataChange` on the plan and effect, with `before` from the current state and `after` set to the
  requested values. For `set_languages`, `activatedLanguages` lists the requested activations and the
  requested editing and reference languages that are not active now.
- A culture whose comment item is missing (`set_comments`) is reported as a missing target, not a failure.
  A `set_languages` earlier in the call on the same library produces it if the culture is among its
  activations or its editing/reference language. That producer is filled in as `createdByItem`, and the
  item's `before` is null. Without a producer the item fails `target_not_found`, as it would on apply.
- An unsupported culture (`Languages.Find` null) cannot be produced and fails in the plan.
- A library created, opened or retrieved by a lifecycle item in a planned call must be edited in a later
  call (main's existing rule). So in read-write, `create_global` followed by `set_comments` on the new
  library needs two calls. Full mode does not plan, so it runs them in one call.
- Categories in a plan can differ from apply only where a native call throws: plan methods are Observe, so
  a Siemens exception there is `worker_operation_failed`, never `engineering_rejected`.

### 8.7 Verification

`LibraryVerificationPolicy` keys `set_comments` and `set_languages` as library root + `metadata`, both as
the item's own key and as the key it offers to earlier items, whether the item succeeded or failed. This check
runs before the "no effect" and "no target" branches. Results:

- a later metadata edit on the same library supersedes an earlier one;
- master-copy, folder and type items are not superseded by a metadata edit;
- lifecycle items (whole-library key) still supersede metadata edits, e.g. `close_global` with discard;
- a metadata edit does not supersede an earlier `create_global` (whole-library key is not under
  root + `metadata`). The `metadata` that `create_global` returned is the state at creation; documented,
  not re-verified. This combination is possible only in unplanned (full-mode) calls (§8.6).

## 9. Concurrency

Last writer wins. Between an agent's read and its `set_comments`, another session or a person in the TIA UI
can change the same comment; the write replaces it without warning. The documentation says so, and a dry run
shows the current `before` text. Edits of languages are idempotent apart from the editing and reference
language, which are plain settings.

## 10. Documentation (last task of each PR, after live acceptance)

- `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md`: `includeMetadata`, the metadata object, the
  `create_global` author and result; PR B: the two operations, end-state check, failures, dirty-library
  note, last-writer-wins, the released-versions caveat, no language removal.
- Tool descriptions of `library_read` and `library_write`; `README.md` and `AGENTS.md` operation counts and
  library bullets (`library_write` goes from 20 to 22 operations in PR B).
- `docs/ARCHITECTURE.md` where library writes are described.
- `docs/IMPROVEMENT_LOG.md`: remove "library metadata writes" from "Still deferred after PR 3"; add
  follow-ups: language removal, optional `expectedTextHash`, project-library metadata (if ever wanted).
- PR B adds ADR-0019: metadata edits run in one native transaction with an end-state check before commit,
  last writer wins, no server-side read-only guard.
- Live report under `docs/superpowers/acceptance/reports/`; kept raw Openness files under its `inputs/`.

## 11. Notes for the builder

Touch points on main (verified 2026-11-08):

- Read: `LibraryReadItem` (new member + description), `LibraryReadCatalog.ToQuery`/`Check`,
  `LibraryReadMethods.Validate` (`inspect_library` branch and every other branch must reject the flag),
  `LibraryQueryInfo`, `LibraryReadModels.LibraryEntryInfo`, worker `LibraryReader` (`inspect_library` path),
  FakeWorker `LibraryReadScenario`.
- Write: `LibraryWriteItem`, `LibraryWriteRequestInfo`, `LibraryWriteCatalog.ToWorkerRequest` (member
  copy) and `ValidateWrite` (where the `MaxItemChars` check lives), `LibraryWriteMethods` (new list, method
  arrays, `Validate` member tuple and allowed strings), `LibraryWriteDomain.DescribeForConfirmation`,
  `LibraryWriter.Write` dispatch, worker `LibraryWritePlanReader` (missing comment items through its
  `Missing()` path), host `LibraryWritePlanner` (copy and validate `metadataChange` into the effect;
  `Produces` for `set_languages`), `LibraryWriteDomain.Project` (effect from result),
  `LibraryWritePayloadContract` (`Project` and `ValidateEnvelopeOutcome` null-element checks),
  `LibraryVerificationPolicy`, FakeWorker `LibraryWriteScenario`.
- Snapshots and wiring: `TiaMcpServer.Tests/Tools/Snapshots/output-schemas.json` pins library row and
  effect members and must be regenerated; `TiaMcpServer.Tests.csproj` links worker library files one by
  one, so the new worker files need `Compile Include` entries; `ConditionalMemberRegisterTests` gets every
  new conditional member. Add a `LibraryWriteBudgetTests` case for an oversized `metadataChange`.
- New worker files: a metadata reader (shared by read and `create_global`) and a metadata writer. No
  existing helper wraps `ExclusiveAccess` + `Transaction`; follow the inline pattern in
  `LibraryTypeWriter.CreateType`, with the library (not the project) as the persistence.
- Stubs: `GlobalLibrary` metadata members, `GlobalLibraryComposition : IEngineeringComposition`,
  `LanguageComposition.Find`, `LanguageAssociation.Add/Contains/Find`, settable `EditingLanguage` /
  `ReferenceLanguage`, `MultilingualTextItemComposition.Find`. Regenerate `ref/` with
  `scripts/verify-reference-stubs.ps1 -Update`, then verify, then the Packaging tests.
- Test doubles live in `TestSupport/LibrarySiemensDoubles.cs`, `HmiUnifiedSiemensDoubles.cs`
  (`Language`, `LanguageSettings`, `MultilingualText`) and `VciSiemensDoubles.cs` (`ExclusiveAccess`,
  `Transaction`).
- Pure logic (end-state comparison, change building, request validation) lives in Siemens-free code with
  unit tests; the worker only gathers native state and runs setters.
- Tests that pin method arrays: `LibraryReadCatalogTests`, `LibraryWriteMethodsTests`,
  `LibraryWorkerDispatchTests`, `LibraryWriteCatalogRegistrationTests`.
- Credit: final empty commit `chore: credit library contribution` with the contributor's `Co-authored-by`,
  as in PR #220 and #225.

## 12. Live acceptance

On a disposable global library in a disposable Portal session, through MCP:

**PR A**: `inspect_library` without and with the flag (global), the flag on project scope
(`validation_error`), the flag on another operation (`validation_error`); `create_global` without author and
with author, both returning metadata; a library opened read-only and a system library if available
(null members with diagnostics, not a failure).

**PR B**: set one and two comments, clear one, an inactive culture (`target_not_found`), an unsupported
culture (`target_not_found`); activate a language and set editing/reference; set editing to an inactive
language (B5 behaviour); a library opened read-only (`engineering_rejected` and the attached state); dry run
of each; one dry run with `set_languages` activating a language and `set_comments` for it (`createdByItem`),
and the same pair applied in one call; a multi-line comment read back exactly; one call mixing `create_master_copy` and `set_comments` on the same library (master copy stays
`trusted`); `close_global` with `discard` after an edit (edits gone, metadata item `superseded`);
`save_global` after an edit, then reopen and read back.
