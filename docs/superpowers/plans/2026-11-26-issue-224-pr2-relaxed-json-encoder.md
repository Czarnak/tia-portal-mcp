# Issue #224 PR 2: relaxed JSON encoder implementation plan

**Goal:** Reduce escaping in canonical tool-result text while preserving decoded JSON values, response contracts, audit integrity and existing budget enforcement.

**Design:** [Approved issue #224 design](../specs/2026-11-26-issue-224-large-block-writes-design.md), section 5, PR 2. The specified order is PR 1 → PR 3 → PR 2. Local `main` at `b3dbe11b` contains PR #239 (`83842c3e`, PR 1) and PR #240 (`b3dbe11b`, PR 3). PR 2 is the remaining specified PR; working copies remain deferred.

**Architecture:** Configure the existing canonical serializer and its final `Utf8JsonWriter` with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`. Keep the single serialization used by `StructuredToolResult`, and let existing budget measurement and delivered-document hashing consume the resulting canonical text.

**Session boundary:** This session prepares this plan only. Do not implement the tasks, run live TIA actions, create commits or perform remote writes. The unchecked steps and implementation commands below are for a later implementation session. Planning checks are recorded separately at the end.

**Implementation order:** Stage Task 1's regression tests and Task 2's new quote/CJK admission cases against the unchanged encoder first. Observe their intended failures, then apply Task 1's two encoder assignments and finish Task 2's rejection-fixture and audit updates. Tasks 3–4 follow those passing regressions.

## Constraints and decisions

- Host/tests remain net10.0, Contracts netstandard2.0 and worker net48. No dependency, project-file, worker-method or Openness changes are required.
- Change both `JsonWriterOptions.Encoder` in `CanonicalJson.Serialize` and `JsonSerializerOptions.Encoder` in `CreateCanonicalWrite`. Changing the serializer alone does not change the final writer's output.
- Keep property naming, recursive ordinal property ordering, array ordering, explicit nulls, compact output, strict readers and typed worker-payload validation. Add no new public fields, schema, operation or contract version. PLC writes remain `2.1` after PR 3; other versions retain their current values.
- Representative HTML-sensitive characters (`<`, `>`, `&`, `'`, `+`) and ordinary BMP text such as `ä`, `Ł` and `漢` become literal. A quotation mark becomes `\"` rather than `\u0022`. Backslashes and control characters remain JSON-escaped. Cover non-BMP characters and U+2028/U+2029 as retained escaping on the target runtime.
- Do not promise that every BMP character becomes literal: .NET has a global block list, including some spaces and private-use characters. Use the built-in encoder without a custom allow list or replacement pass. See Microsoft's [encoder API](https://learn.microsoft.com/en-us/dotnet/api/system.text.encodings.web.javascriptencoder.unsaferelaxedjsonescaping?view=net-10.0) and [block-list guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/character-encoding#block-lists).
- `content` text and `structuredContent` still originate from one canonical serialization. SDK/client serialization of the structured object or the outer JSON-RPC envelope is a separate boundary and is outside this change.
- Budgets continue to count canonical string characters using effective `MaxItemChars` and `MaxDocumentChars`. Default limits remain 60,000/180,000; startup overrides and fixed domain limits retain their current rules. Some content and operation identities will now fit where they were formerly omitted or refused.
- `responseHash` remains SHA-256 over UTF-8 bytes of exact delivered `responseText`. Its value changes when canonical spelling changes, even when JSON values are equal. Preserve old audit records. PLC export `contentHash` still hashes exported content, not canonical response text.
- Leave `WorkerJson` and IPC encoding unchanged. The shared `CanonicalJson` helper also serves host normalization, measurements and audit fields; accept consistent spelling changes there rather than adding a second delivery serializer.
- Document that relaxed JSON output must be handled as JSON/decoded data. It no longer provides HTML escaping as defense in depth; do not introduce HTML mitigation or transport redesign in this PR.
- Keep the improvement log's escaping-inflation follow-up open until measurement in the owner's actual client is complete. Automated serializer tests or Inspector alone cannot establish model-facing token savings.

## Evidence from the merged code

| Owning file | Current behavior and planned use |
| --- | --- |
| `TiaMcpServer/Json/CanonicalJson.cs` | `Serialize` constructs the final writer near line 66; `WriteCanonical` re-emits names/values near line 138; `CreateCanonicalWrite` near line 273 lacks an encoder. Both output configurations need the same built-in encoder. |
| `TiaMcpServer/Tools/StructuredToolResult.cs` | `Create` serializes once, and `CreateCanonical` parses/clones that text. Keep this path intact. |
| `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs` | Both whole-response `Measure` and per-result `ResultChars` use `CanonicalJson.Serialize(...).Length`. No new sizing algorithm is needed. |
| `TiaMcpServer/Safety/Pipeline/WriteRun.cs` | `BuildAuditRecord` stores the delivered text and hashes it through `ContentHashRules.Sha256Hex`. No hash algorithm or audit version change is needed. |
| `TiaMcpServer.Tests/Tools/StandaloneStatusContractTests.cs` | `ValueBudget_AccountsForEscapedStrings` uses 11,000 quotes to trigger omission; that payload becomes small enough to fit. Preserve rejection coverage with still-escaped characters. |
| `TiaMcpServer.Tests/Network/NetworkGuardedWriteBudgetTests.cs` | Two rejection fixtures depend on 50 maximum-length CJK IDs expanding sixfold. These IDs now fit; preserve the rejection cases and add admission coverage for this newly feasible input. |
| `TiaMcpServer.Tests/Hmi/HmiGuardedWriteMcpTests.cs` | The dependency test normalizes `\\u0026` in raw JSON before asserting `&amp;`. Its `AssertAudit` helper reserializes a parsed document. Use decoded text assertions and original delivered text for new audit evidence. |

All paths below are repository-relative. Extend existing test files; this plan requires no `.csproj` changes.

## Task 1: Change the canonical encoder with focused regression tests

**Files**

- Modify `TiaMcpServer/Json/CanonicalJson.cs` for the import and two encoder assignments.
- Modify `TiaMcpServer.Tests/Json/CanonicalJsonTests.cs` for canonical spelling and round-trip regressions.
- Modify `TiaMcpServer.Tests/Tools/StructuredToolResultTests.cs` for the shared text/structured document.

**Interfaces:** Keep `CanonicalJson.Serialize`, `CanonicalJson.ToElement`, `StructuredToolResult.Create` and `CreateCanonical` signatures and their callers unchanged.

- [x] Add `Serialize_UsesRelaxedEscapingWithoutChangingValues`, exercising both a CLR object and an already-parsed `JsonElement` whose input uses Unicode escapes. Include nested values, an array and a special-character property name, with `<Network>`, `<>&'+`, a quote, a backslash and `äŁ漢`. Assert exact literal output for the relaxed characters, `\"` for a quote, deterministic ordinal property order and equality of decoded names/values. The current implementation must fail the literal spelling assertions; a pre-escaped element must be re-emitted through the final writer rather than copied raw.
- [x] Add `Serialize_PreservesRequiredEscapingAndRoundTripsValues`, covering U+0001, CR/LF, tab, backslash, U+2028, U+2029 and U+1F600. Assert valid JSON, expected retained escape forms and identical decoded values. Use C# Unicode escapes/`char.ConvertFromUtf32` in fixture construction where helpful. Do not assert a comprehensive .NET block-list inventory.
- [x] Add `Create_RelaxedTextAndStructuredContentUseOneDocument` in `StructuredToolResultTests`, using `Create` with a payload containing XML attributes, quotes and BMP text. Read its original `TextContentBlock.Text`; assert relaxed spelling, unchanged parsed content, and equality with the detached `StructuredContent` element's `GetRawText()` before any SDK reserialization.
- [x] Run the focused command below. Observe the relaxed-spelling cases fail before production changes; existing ordering, explicit-null, parsing and repeated-stability tests must keep their meaning.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~CanonicalJsonTests|FullyQualifiedName~StructuredToolResultTests"
```

- [x] Before changing production code, add Task 2's positive quote-budget and maximum-CJK-ID admission cases and run Task 2's focused command against the unchanged encoder. Require those new cases to fail for omission/admission respectively, while the existing rejection fixtures still pass. Then add `using System.Text.Encodings.Web;` and set the following property in both the existing final writer's `JsonWriterOptions` initializer and the `CreateCanonicalWrite` serializer initializer, preserving every other option:

```csharp
Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
```

- [x] Rerun the same command and require all selected tests to pass. Inspect the production diff: the import and two assignments should be sufficient; do not change the strict reader configurations or `WriteCanonical` traversal.

**Acceptance:** Both CLR and `JsonElement` inputs use relaxed final output; JSON values and canonical ordering survive; required escaping and the one-document result seam remain covered.

## Task 2: Preserve budget rejection coverage and prove delivered audit bytes

**Files**

- Modify `TiaMcpServer.Tests/Tools/StandaloneStatusContractTests.cs` for status value-budget fixtures.
- Modify `TiaMcpServer.Tests/Network/NetworkGuardedWriteBudgetTests.cs` for identity-budget fixtures.
- Modify `TiaMcpServer.Tests/Hmi/HmiGuardedWriteMcpTests.cs` for public MCP text and exact audit checks.

**Interfaces:** Use existing status projection, `NetworkWriteDomain.Validate`, `NetworkWritePayloadBudget.MeasureProtectedCore`, `McpProtocolTestHarness`, `CanonicalToolDocument`, and temporary audit helpers. Keep all production budget and audit APIs unchanged.

- [x] At Task 1's baseline checkpoint, add a status case using the original 11,000-quote Author: require the full value to be returned without omission and round-trip exactly. It must fail against baseline omission behavior and pass with the encoder change. Stage the positive maximum-CJK-ID case below at the same checkpoint.
- [x] After the encoder change, update `ValueBudget_AccountsForEscapedStrings` to use `new string('\u0001', 11000)`. Keep its existing `status == "omitted"` assertion and add assertions for omission metadata and the canonical delivered-size bound. This fixture still expands beyond 60,000 characters under either encoder.
- [x] In `AdmittedEscapedOperationIds_InfeasibleProtectedEnvelopeIsRejectedBeforePlanning` and `InfeasibleIdentities_RejectBeforeBindingOrWorker_WithOneAudit`, replace each 255-character CJK prefix with 255 U+0001 characters and append a unique one-character suffix. Preserve 50 distinct IDs of exactly 256 characters and their existing validation, no-binding/no-worker and one-audit assertions. Retain the protected-core size assertions; do not relax their thresholds to make the tests pass.
- [x] Extend `FeasibleFiftyIdentities_AreAdmitted` with a distinct third case: 255 copies of U+4E00 plus the existing unique CJK suffix. Assert 50 distinct 256-character IDs, successful domain validation and protected-core size within the effective document budget. Preserve existing ASCII and short-Unicode cases. This admission assertion must fail against the default-encoder baseline.
- [x] Update `PreviewThenApplyDependenciesNormalizeTextsWithoutElicitation`: retain both raw `CallToolResult` instances before calling `CanonicalToolDocument.Read`. Assert the normalized alarm text as a decoded string rather than using `GetRawText().Replace("\\u0026", "&")`. Keep the existing `A & B` → `&amp;` normalization coverage, and include ordinary BMP text in that fixture so Unicode delivery is exercised too.
- [x] For both preview and apply, take the exact original `TextContentBlock.Text`, assert the expected literal relaxed markup/BMP text, and compare it byte-for-byte as a string to the corresponding audit line's decoded `responseText`. Compute `"sha256:" + SHA256(UTF8(text))` independently and compare with `responseHash`. Keep text-versus-structured semantic equality and existing access-mode, dependency and zero-elicitation assertions. A canonical reserialization of the parsed root is not a substitute for capturing original delivered text.
- [x] Run the focused command below. Before changing the rejection fixtures, confirm failures are explained by reduced escaping; after the changes, require all selected tests to pass. Existing HMI maximum-ID and library metadata omission fixtures already use U+0001 and require no mechanical change.

```powershell
dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~CanonicalJsonTests|FullyQualifiedName~StructuredToolResultTests|FullyQualifiedName~StandaloneStatusContractTests|FullyQualifiedName~NetworkGuardedWriteBudgetTests|FullyQualifiedName~HmiGuardedWriteMcpTests|FullyQualifiedName~LibraryWriteBudgetTests"
```

**Acceptance:** Newly feasible quote/CJK payloads are admitted, still-escaped oversized payloads retain denial/omission coverage, and public MCP results prove exact delivered audit text/hash for preview and apply. No budget setting, hash algorithm, contract version or write policy changes.

## Task 3: Document the compatibility effect and measure an actual client

**Files**

- Modify `docs/ARCHITECTURE.md`, section 7a, to describe the canonical encoder and its boundaries.
- Modify `CHANGELOG.md`, Unreleased, to state reduced escaping and delivered-document hash changes.
- Modify `docs/IMPROVEMENT_LOG.md`, the escaping-inflation bullet under large PLC block follow-ups, according to measured evidence.
- If actual-client acceptance is performed, create `docs/superpowers/acceptance/reports/2026-11-26-issue-224-pr2-relaxed-json-client-verification.md` using the actual verification date in the filename, and link it from `docs/superpowers/README.md`. Create no report claiming checks that were not performed.

**Interfaces:** Existing canonical response contract, default/effective budgets, exact exported `contentHash`, and UTF-8 delivered-document `responseHash`. No new user-facing API or CLI flag.

- [x] Explain the writer and serializer policy, reduced XML/SCL escaping, required escaping that remains, unchanged JSON values/versions, character-based budget effects and changed audit document hashes. State that the JSON representation is not suitable for direct embedding in HTML or a script element. Link the official Microsoft guidance from the architecture explanation.
- [x] Describe `structuredContent` and outer transport reserialization separately: this change controls canonical text, not every byte the SDK/client emits. Avoid a blanket claim that all non-ASCII characters are literal or that model tokens/latency decrease by a fixed amount.
- [x] Initially update the improvement-log bullet to say host escaping is implemented and actual-client measurement is pending. Do not close the broader large-block follow-ups section or fold unrelated doctor/paging work into this PR. Move only the escaping follow-up to completed once the following actual-client evidence exists.
- [ ] Identify baseline and candidate builds by commit and, for any uncommitted candidate, exact diff/build provenance. Record .NET runtime, MCP SDK version, client/version, access mode, arguments and budget settings. Use read-only access for both. Follow [local MCP testing](../../development/local-mcp-testing.md) for launch, explicit bind and reads.
- [ ] In the owner's actual target client, bind each build to the same already-open disposable project. Read the same existing block using `plc_read` / `get_block_content` in XML and, for an eligible block, source format. Use identical selectors and successful non-omitted responses, with XML attributes, source quotes/operators and representative BMP text. Do not open, close, save, compile or modify the project for this measurement.
- [ ] Capture each original text block after exactly one JSON-RPC decode and the structured object separately. Confirm parsed text equals `structuredContent`, exported content and `contentHash` match across builds, and format/omission settings are equivalent. The changed representation must not change the exported PLC text.
- [ ] For each original text block, report character count, UTF-8 byte count and absolute/percentage reduction. Inspect the client's actual model-facing representation separately: record whether it exposes text, reserializes the structured object or exposes both. Report model-facing counts only when observable. If token counts are available, record the client usage source or identified tokenizer; otherwise explicitly leave token cost and model latency unmeasured.
- [ ] Keep raw MCP receipts, client logs and timing output private/outside retained repository evidence. Retain only a concise dated summary of cases, provenance, counts, limits and observed client behavior. Do not use the lifecycle schedule in `scripts/timing/measure_write_timing.py`: it defaults to full mode and closes/reopens the project. Its saved JSON is reserialized, so capture-file size is not raw wire size.
- [x] If actual-client access is unavailable in the implementation session, finish the code, tests and documentation with this acceptance step explicitly pending and the follow-up still open. An Inspector/fake-worker check can prove transport semantics but cannot replace the required client measurement or justify closing the item.

**Acceptance:** Architecture/changelog describe the real compatibility boundary. Completion status of the escaping follow-up matches actual-client evidence; unobserved token or timing gains are not claimed.

## Task 4: Final verification and review

**Files:** Review all files changed by Tasks 1–3; add no implementation scope in this task.

- [x] Run the focused regression command in Task 2, then the full non-packaging suite and serial stub build, sequentially:

```powershell
dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30
git diff --check
```

- [x] Packaging tests are unnecessary for the planned files. If implementation changes a product `.csproj`, `Directory.Build.props`, `global.json`, packaging verification scripts or reference stubs, first justify that scope change and run `dotnet test TiaMcpServer.Tests --filter "Category=Packaging"` as required by `AGENTS.md`.
- [x] Investigate full-suite failures for any other fixture that assumes six-character escaping. Preserve each test's intended admission/omission guarantee with deterministic data; do not replace expected results indiscriminately or alter production limits. Add a newly admitted case when a changed fixture would otherwise lose coverage of the behavior change.
- [x] Obtain independent review of the two encoder assignments, special-character property names/values, single serialization seam, budget tests, exact delivered audit hash, and the actual-client evidence/remaining limitation.
- [x] Recheck the final diff against this plan. Require no changes to `WorkerJson`, Openness import/export, access modes, public contract versions, hash algorithms, budget values, schemas or dependencies. Report tests/build/docs results and whether actual-client acceptance remains pending. A stub build does not establish live-client behavior.
- [ ] Before a later PR merge, follow the repository's transient-document lifecycle: preserve durable behavior in architecture/changelog, retain only performed acceptance evidence, and remove this new plan and its temporary index link in the final cleanup step. Do not remove the pre-existing design/PR 1/PR 3 files as part of this planning session.

## Coverage map

| Approved PR 2 requirement | Planned evidence |
| --- | --- |
| Final writer and serializer use the relaxed encoder | Task 1: two assignments; CLR and pre-escaped `JsonElement` spelling tests |
| Literal punctuation/BMP text, retained JSON escaping | Task 1: exact output and round-trip cases, including property names |
| Text and structured result share one document | Task 1 result test; Task 2 registered MCP checks |
| Same values, changed audit hashes, unchanged versions | Task 2 exact delivered audit check; Task 3 architecture/changelog |
| Smaller canonical budget measurements, unchanged limits | Task 2 quote/CJK positive cases and retained control-character rejection cases |
| HMI escaping-dependent assertion corrected | Task 2 decoded alarm text and original delivered result |
| Worker IPC stays outside scope | Constraints and Task 4 diff review |
| Actual-client measurement before closing follow-up | Task 3 explicit measurement and pending-acceptance rule |

## Planning-session validation

The approved design and current owning source were inspected. The initial workspace was clean on local `main`; its PR 1 and PR 3 merge commits were verified. Independent serialization/test and client-measurement investigations informed the tasks, followed by independent plan review with no blocking findings. Review corrections make the baseline test order explicit and distinguish existing status assertions from assertions to add.

All referenced existing code/doc/script paths and local plan links were checked; the only absent literal file path is the explicitly planned future acceptance report. `python -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30` passed with **0 errors and 4 transient-file warnings**. `git diff --check` and an explicit whitespace check of the new untracked plan passed. The plan and its four-line directory-index entry are the only changes; `HEAD` remains `b3dbe11b`.

No implementation tests, product build, actual-client measurement or live TIA action has been run for this plan. No commit has been created.

## Implementation-session validation

Implementation is on `fix/issue-224-relaxed-json-encoder`. The plan and its directory-index entry
were committed first as `588f7534`; implementation is in `3c6a6331`, with additional regression
fixture corrections in `b3e442d3`.

The unchanged-encoder baseline produced the four intended failures (canonical spelling,
single-document result spelling, quote-heavy status admission and maximum-length CJK ID
admission), with 119 other selected tests passing. The expanded focused suite now passes all
179 tests. The full non-packaging suite passes all 8,967 tests (0 skipped, 0 failed).
The serial stub build passes with 0 errors and 9 warnings. Documentation validation passes
with 0 errors and 4 transient-file warnings; `git diff --check` passes.

Full-suite investigation also corrected stale canonical quote fixtures and escaping-dependent
hardware-cursor/PLC protected-budget fixtures. Still-escaped rejection cases retain their limits;
complementary cases cover newly admitted CJK paths and literal punctuation/CJK identities. The
shared result helper accepts SDK reserialization of structured data while retaining canonical
text/value equality; the pre-SDK result test still requires exact original text/raw equality.
Independent review of the implementation and subsequent fixture corrections found no blockers.

Actual-client measurement remains pending. Available Inspector/fake-worker evidence does not
establish the owner's model-facing representation or baseline/candidate provenance. No live TIA
action or actual-client acceptance report was produced; the escaping follow-up remains open.
The transient plan/index cleanup remains a step for the later PR merge.
