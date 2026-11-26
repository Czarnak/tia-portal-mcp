# Issue #224 PR 1: write-path trims Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Share one Siemens-free bundle parser between host and worker, drop the second worker export of an XML write, and make the host diff of an XML-only submission compare like with like.

**Architecture:** The bundle text format moves into `TiaMcpServer.Contracts/Block` and throws its own `BlockBundleFormatException`. The worker keeps its two entry types (`BlockBundleFormat`, `BlockImportBundleParser`) as thin adapters that convert that exception into `WorkerOperationException(validation_error, same message)`, so no worker call site and no failure category changes. The host uses the shared parser to pick the diff sides.

**Tech Stack:** C# (netstandard2.0 Contracts, net48 worker, net10.0 host/tests), xunit.

**Spec:** `docs/superpowers/specs/2026-11-26-issue-224-large-block-writes-design.md`, section 5, PR 1.

## Global Constraints

- No public contract change: `plc_read` stays `1.0`, `plc_write` stays `2.0`; no new response member.
- Every bundle refusal keeps category `validation_error` and its exact current message.
- The hash check is unchanged: it is over the whole served bundle.
- Contracts stays Siemens-free (netstandard2.0, no worker types).
- Build/test: `export PATH=$HOME/.dotnet:$PATH`, then `dotnet test TiaMcpServer.Tests --filter "<filter>" /p:UseTiaPortalReferenceStubs=true -m:1`. Full run filter: `Category!=Packaging`. A product `.csproj` changes, so also run `Category=Packaging` before the final commit.
- Commit messages: short one-liners, with the session's attribution trailer.
- Linux container caveat: 1,768 tests fail here before any change (integration tests look for `TiaMcpServer.FakeWorker.exe`; a few parser name tests depend on Windows `Path.GetInvalidFileNameChars`). The baseline list is in the session scratchpad (`baseline-failed.txt`). "Green" here means: no failure outside that list, and every new test passes unless it needs the fake worker. Tests that need the fake worker (`PlcWritePlannerTests`) are verified by Windows CI (`ci.yml`, `windows-latest`).

## Review Focus

1. The issue's real failure: an XML-only submission with LF line endings. Expected: the diff is against the `.xml` part and reports `lineEndingOnly: true`, not 1,789 changed lines against the bundle. (Task 3, `XmlOnlyLfSubmissionIsLineEndingOnly`.)
2. A submission or current export that does not parse as a bundle (bad delimiter, empty, companion-only export with no `.xml`). Expected: the planner never throws; it falls back to today's whole-text diff and the worker refuses malformed content later. (Task 3, `UnparsableSidesFallBackToWholeText`, `CompanionOnlyExportFallsBackToWholeText`.)
3. A full bundle submission whose companion was edited. Expected: whole-bundle diff as today, so the companion edit stays visible. (Task 3, `FullBundleSubmissionKeepsWholeBundleDiff`.)
4. `.XML` in upper case as the document name. Expected: recognized as the SimaticML document, as `BlockImportRouting` does today (OrdinalIgnoreCase). (Task 1, `FindSimaticMlDocumentIgnoresCase`; Task 3 reuses it.)
5. A moved-parser refusal reaching the import coordinator. Expected: `validation_error`, not `worker_operation_failed`. (Task 1, `CoordinatorKeepsValidationErrorForMalformedBundle`.)

---

### Task 1: Move the bundle format into Contracts

**Files:**
- Create: `TiaMcpServer.Contracts/Block/BlockBundleText.cs` (format owner: delimiter regexes, `Compose`, `Parse`, `ContainsDelimiterLine`, `FindSimaticMlDocument`)
- Create: `TiaMcpServer.Contracts/Block/BlockBundleFormatException.cs`
- Move: `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportDocument.cs` → `TiaMcpServer.Contracts/Block/BlockImportDocument.cs` (namespace `TiaMcpServer.Contracts.Block`, `public sealed`)
- Move: `TiaMcpServer.OpennessWorker/Openness/Block/ParsedBlockImportBundle.cs` → `TiaMcpServer.Contracts/Block/ParsedBlockImportBundle.cs` (same)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Block/BlockBundleFormat.cs` (adapter only)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportBundleParser.cs` (adapter only)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportRouting.cs` (`FindSimaticMlDocument` delegates to Contracts)
- Modify: worker files that use the moved types, adding `using TiaMcpServer.Contracts.Block;` where the compiler asks
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (drop the links to the two moved files; adapters stay linked)
- Test: `TiaMcpServer.Tests/Block/BlockBundleTextTests.cs` (new), `TiaMcpServer.Tests/Block/BlockImportCoordinatorTests.cs`

**Interfaces:**
- Produces (namespace `TiaMcpServer.Contracts.Block`):
  - `public sealed class BlockBundleFormatException : Exception` — `(string message)`; always means `validation_error`.
  - `public static class BlockBundleText`:
    - `string Compose(IReadOnlyList<BlockImportDocument> documents)`
    - `ParsedBlockImportBundle Parse(string documentName, string rawContent)`
    - `bool ContainsDelimiterLine(string content)`
    - `BlockImportDocument? FindSimaticMlDocument(ParsedBlockImportBundle bundle)` — first document whose `LogicalName` ends with `.xml`, OrdinalIgnoreCase.
  - `BlockImportDocument`, `ParsedBlockImportBundle`: unchanged members, now public in Contracts.
- Worker adapters keep their current signatures: `BlockBundleFormat.Compose/ContainsDelimiterLine`, `BlockImportBundleParser.Parse`. Each catches `BlockBundleFormatException` and throws `new WorkerOperationException(WorkerFailureCategories.ValidationError, ex.Message)`.

- [ ] **Step 1: Write the failing tests** in `BlockBundleTextTests.cs`:
  - `ParseThrowsBundleFormatExceptionWithCurrentMessage`: `BlockBundleText.Parse("a.xml", "--- FILE: a.xml ---\n")` throws `BlockBundleFormatException` with message `"Every declared block import document must have content."`
  - `ComposeRoundTripsThroughParse`: compose `[a.xml:"<x/>\r\n", a.s7dcl:"d"]`, parse it back, documents equal by name and content.
  - `FindSimaticMlDocumentIgnoresCase`: bundle with `B.XML` and `B.s7dcl` returns the `B.XML` document; bundle with only `B.s7dcl` returns null.
  - In `BlockImportCoordinatorTests.cs`, `CoordinatorKeepsValidationErrorForMalformedBundle`: `BlockImportCoordinator.Execute` with content `"--- FILE: bad\n<x/>"` fails with `FailureCategory == validation_error`.
- [ ] **Step 2: Run them, expect compile failure** (`BlockBundleText` not defined). Filter: `FullyQualifiedName~BlockBundleTextTests|FullyQualifiedName~BlockImportCoordinatorTests`.
- [ ] **Step 3: Implement.** Move the regexes, `Compose`, `Parse`, name validation and `ContainsDelimiterLine` bodies verbatim into `BlockBundleText`, replacing `ValidationFailure(...)` with `new BlockBundleFormatException(...)`. Reduce the worker types to adapters. Make `BlockImportRouting.FindSimaticMlDocument` call `BlockBundleText.FindSimaticMlDocument`. Move the two data classes. Move the "sole owner of the bundle format" doc comment to `BlockBundleText`, and point the adapters' comments to it.
- [ ] **Step 4: Run the filter, expect PASS;** then run the existing `BlockBundleFormatTests`, `BlockImportBundleParserTests`, `BlockImportRoutingTests`, `BlockImportStagerTests`, `BlockExporterSafetyTests`, `LibraryTypeWriterTests` unchanged. Expect PASS (they pin the adapters' categories and messages).
- [ ] **Step 5: Commit** `refactor: move block bundle format to contracts`.

### Task 2: Reuse the precondition export for the companion check

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/Block/BlockImporter.cs` (`Import`, `XmlImportPreflight`, `ImportDocuments`)
- Modify: `TiaMcpServer.OpennessWorker/Openness/Block/BlockImportRouting.cs`
- Test: `TiaMcpServer.Tests/Block/BlockImportRoutingTests.cs`

**Interfaces:**
- Consumes: `BlockImportBundleParser.Parse` (Task 1 adapter).
- Produces: `BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(ParsedBlockImportBundle submitted, string currentExport, string authoritativeName)`, an overload that parses `currentExport` with `authoritativeName` as the fallback name, then applies the existing check.

- [ ] **Step 1: Write the failing tests** in `BlockImportRoutingTests.cs`:
  - `CompanionCheckFromExportTextAcceptsUnchangedCompanions`: submitted bundle `[B.xml:"new", B.s7dcl:"d"]`, export text `Compose([B.xml:"old", B.s7dcl:"d"])` → no exception.
  - `CompanionCheckFromExportTextRefusesEditedCompanion`: same with export `B.s7dcl:"other"` → `WorkerOperationException`, `validation_error`, message containing `"'B.s7dcl' was modified"`.
- [ ] **Step 2: Run them, expect compile failure** (overload missing). Filter: `FullyQualifiedName~BlockImportRoutingTests`.
- [ ] **Step 3: Implement the overload; wire `BlockImporter`.** The preflight lambda keeps the export string it passes to `RequireContentHash` and stores it on `XmlImportPreflight` as `CurrentExport`. `ImportDocuments` takes `string currentExport` and calls the new overload instead of `BlockExporter.Export(...)`. The call stays inside the coordinator's import callback, so `BlockImportOutcome` keeps its shape. Siemens types keep `BlockImporter` out of unit tests; the wiring is checked in review and by the owner's live run.
- [ ] **Step 4: Run the filter, expect PASS.**
- [ ] **Step 5: Commit** `perf: reuse precondition export for companion check`.

### Task 3: Host diff for an XML-only submission

**Files:**
- Create: `TiaMcpServer/Plc/PlcContentDiffSides.cs`
- Modify: `TiaMcpServer/Plc/PlcWritePlanner.cs:128` (`ResolveContentAsync`)
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (link the new file as `Host\Plc\PlcContentDiffSides.cs`)
- Modify: `docs/SupportedOperations/` PLC reference, the `contentDiff` description
- Test: `TiaMcpServer.Tests/Plc/PlcContentDiffSidesTests.cs` (new), `TiaMcpServer.Tests/Plc/PlcWritePlannerTests.cs`

**Interfaces:**
- Consumes: `BlockBundleText.Parse`, `BlockBundleText.FindSimaticMlDocument`, `BlockBundleText.ContainsDelimiterLine` (Task 1).
- Produces: `internal static (string Current, string Requested) PlcContentDiffSides.Select(string format, string currentExport, string submitted)`.

Rule (`format == "xml"` only; otherwise return both texts unchanged):
1. Parse `currentExport` (fallback name `current.xml`) and take its SimaticML document; if parsing throws `BlockBundleFormatException` or there is none, return both texts unchanged.
2. If `submitted` contains no delimiter line, return `(currentXml.Content, submitted)`.
3. Else parse `submitted` (fallback name `submitted.xml`); if it holds exactly one document and that document is SimaticML, return `(currentXml.Content, thatDocument.Content)`.
4. Otherwise (parse failure, several documents, a non-XML single document), return both texts unchanged.

- [ ] **Step 1: Write the failing tests** in `PlcContentDiffSidesTests.cs`, using a current export `Compose([B.xml: X, B.s7dcl: D])` where `X` is three CRLF lines:
  - `RawXmlSubmissionDiffsAgainstXmlPart`: submitted = `X` with line 2 changed → `Current == X`, `Requested == submitted`.
  - `OneDocumentBundleDiffsWithoutSeparator`: submitted = `"--- FILE: B.xml ---\n" + X'` → `Requested == X'`.
  - `XmlOnlyLfSubmissionIsLineEndingOnly`: submitted = `X` with CRLF replaced by LF; `PlcContentDiff.Build(sides.Current, sides.Requested)` has `LineEndingOnly == true`.
  - `FullBundleSubmissionKeepsWholeBundleDiff`: submitted = `Compose([B.xml: X, B.s7dcl: "edited"])` → both sides are the full texts.
  - `UnparsableSidesFallBackToWholeText`: submitted `"--- FILE: bad\n<x/>"` → both full texts; current export `"--- FILE: bad\n"` with raw submitted → both full texts; no exception.
  - `CompanionOnlyExportFallsBackToWholeText`: current `Compose([B.s7dcl: D])`, submitted raw `X` → both full texts.
  - `SourceFormatIsUntouched`: `format == "source"` → both full texts.
  - In `PlcWritePlannerTests.cs`, `XmlOnlySubmissionPlansOneLineDiff`: fake export returns the bundle; an `update_block_logic` with raw `X'` (one line changed) and the bundle's hash plans an effect whose `ContentDiff` has `CurrentChangedLineCount == 1` and `RequestedChangedLineCount == 1`.
- [ ] **Step 2: Run them, expect compile failure.** Filter: `FullyQualifiedName~PlcContentDiffSidesTests|FullyQualifiedName~PlcWritePlannerTests`.
- [ ] **Step 3: Implement `Select`;** in `ResolveContentAsync`, after the hash check passes, build the diff from `PlcContentDiffSides.Select(format, export.Payload, item.Content!)`. The hash check keeps using `export.Payload`.
- [ ] **Step 4: Run the filter, expect PASS.**
- [ ] **Step 5: Document.** In the PLC reference's `contentDiff` description, add: for an XML write whose content is a single SimaticML document, `contentDiff.current` (`sha256`, characters, lines) describes the `.xml` part of the served bundle, not the whole bundle; `expectedContentHash` still covers the whole bundle. Run `python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`; expect `0 error(s)`.
- [ ] **Step 6: Full verification.** Run filter `Category!=Packaging` with a trx logger and compare its failed set with `baseline-failed.txt`: no new failures. Run `Category=Packaging` and report its result. Push and confirm Windows CI is green, including `XmlOnlySubmissionPlansOneLineDiff`.
- [ ] **Step 7: Commit** `feat: diff xml-only submissions against the xml part`.
