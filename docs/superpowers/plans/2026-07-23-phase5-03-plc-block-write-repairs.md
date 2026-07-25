# Phase 5 Plan 3: PLC Block-Write Repairs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `update_block_logic` stage safe deterministic document bundles and verify the imported block, while making SCL `create_block` generate a non-empty compile unit and succeed only after compile verification.

**Architecture:** Isolate pure parsing, path validation, staging, and source-generation logic into small immutable worker helpers linked into the net8 test project. Keep Siemens API calls in the net48 worker. Parse and validate everything before file or project mutation, import once, then compile and re-export/resolve before returning success.

**Tech Stack:** C# worker helpers, Siemens Openness block document import/export/compile APIs, xUnit linked compilation, FakeWorker, live TIA Portal V21 acceptance.

## Global Constraints

- Primary AC scope: AC-023–AC-031; shared AC-032, AC-042, AC-043.
- Keep existing MCP and worker entrypoints: `Program.UpdateBlockLogic(WorkerRequest)` and `BlockMutationService.CreateBlock(...)`.
- Parse the full bundle into immutable descriptors before creating a staging directory or invoking Siemens.
- Reject missing, empty, duplicate, rooted, traversal, separator-containing, invalid-character, and root-escaping document names as `validation_error`.
- Preserve document order deterministically and pass the first declared document as the primary `ImportFromDocuments` document.
- Stage only declared documents. Verify every expected staged file exists under the canonical staging root before import.
- Always clean temporary staging in `finally`. Cleanup failure is a capped warning and must not turn a verified success into failure or a failed import into success.
- Import/creation success is provisional until postconditions pass. Compile or re-export/resolve failure is `postcondition_failed` with an uncertain-state warning.
- Do not automatically retry any Siemens write.
- Keep worker-only Siemens logic excluded from the coverage denominator, but cover every pure helper through linked compilation.

---

## Task 1: Parse and validate immutable document bundles

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/BlockImportDocument.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/ParsedBlockImportBundle.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockImportBundleParser.cs`
- Reuse/link: `TiaMcpServer.OpennessWorker/WorkerOperationException.cs`
- Create: `TiaMcpServer.Tests/BlockImportBundleParserTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Acceptance:** AC-023, AC-024, AC-042.

- [ ] **Step 1: Add linked-compilation entries and RED parser tests.**

  Link the three pure worker files plus `TiaMcpServer.OpennessWorker/WorkerOperationException.cs` into the test project under `Linked/Openness`. Add tests for:

  ```csharp
  [Fact] public void Parse_RejectsMissingDocumentName()
  [Fact] public void Parse_RejectsDuplicateDocumentNamesCaseInsensitively()
  [Theory]
  [InlineData("../Main.xml")]
  [InlineData("..\\Main.xml")]
  [InlineData("C:\\temp\\Main.xml")]
  [InlineData("/tmp/Main.xml")]
  [InlineData("folder/Main.xml")]
  [InlineData("folder\\Main.xml")]
  public void Parse_RejectsUnsafeDocumentName(string name)
  [Fact] public void Parse_SingleXml_ProducesOnePrimaryDocument()
  [Fact] public void Parse_MultiDocumentBundle_PreservesDeclarationOrder()
  ```

  Use the current delimiter exactly: `--- FILE: <name> ---`. A non-bundle XML input uses the request document name as its sole document.

- [ ] **Step 2: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~BlockImportBundleParserTests
  ```

- [ ] **Step 3: Implement immutable models.**

  Use get-only properties and copy collections at construction:

  ```csharp
  internal sealed class BlockImportDocument
  {
      public BlockImportDocument(string logicalName, string safeFileName, string content);

      public string LogicalName { get; }
      public string SafeFileName { get; }
      public string Content { get; }
  }

  internal sealed class ParsedBlockImportBundle
  {
      public ParsedBlockImportBundle(
          string primaryDocumentName,
          IReadOnlyList<BlockImportDocument> documents);

      public string PrimaryDocumentName { get; }
      public IReadOnlyList<BlockImportDocument> Documents { get; }
  }
  ```

  Do not expose mutable `List<T>` instances.

- [ ] **Step 4: Implement parser validation before returning descriptors.**

  `BlockImportBundleParser.Parse(string documentName, string rawContent)` must:

  1. reject null/whitespace `documentName` or `rawContent`;
  2. split multi-document input only on complete delimiter lines;
  3. require non-empty content for every declared document;
  4. require `Path.GetFileName(name) == name`, `!Path.IsPathRooted(name)`, no `.`/`..` segments, no directory separators, and no invalid file-name characters;
  5. reject duplicate safe names using `StringComparer.OrdinalIgnoreCase`;
  6. preserve declaration order in the returned read-only collection;
  7. choose the first declared document as `PrimaryDocumentName`.

  Throw `WorkerOperationException(WorkerFailureCategories.ValidationError, ...)` with a field-safe message. Never rely on catch-all `ArgumentException` classification; the explicit exception proves caller validation failed before Siemens invocation.

- [ ] **Step 5: Rerun tests, build, and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~BlockImportBundleParserTests
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer.OpennessWorker/Openness/BlockImportDocument.cs TiaMcpServer.OpennessWorker/Openness/ParsedBlockImportBundle.cs TiaMcpServer.OpennessWorker/Openness/BlockImportBundleParser.cs TiaMcpServer.Tests/BlockImportBundleParserTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "fix: validate block document bundles"
  ```

---

## Task 2: Stage exactly the validated documents

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/BlockImportStager.cs`
- Create: `TiaMcpServer.Tests/BlockImportStagerTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Acceptance:** Pure staging portion of AC-025; precondition for AC-026–AC-029.

- [ ] **Step 1: Add RED stager tests.**

  Link `BlockImportStager.cs` into the test project. Add:

  ```csharp
  [Fact] public void Stage_WritesEveryDocumentExactlyOnceInOrder()
  [Fact] public void Stage_ReturnsCanonicalPathsUnderRoot()
  [Fact] public void Stage_RejectsAPathThatEscapesCanonicalRoot()
  [Fact] public void Stage_DoesNotCreateUndeclaredFiles()
  ```

  Use a test-owned temporary directory and delete it in `finally`.

- [ ] **Step 2: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~BlockImportStagerTests
  ```

- [ ] **Step 3: Implement deterministic staging.**

  Add:

  ```csharp
  internal static class BlockImportStager
  {
      public static IReadOnlyList<string> StageDocuments(
          string stagingRoot,
          ParsedBlockImportBundle bundle);
  }
  ```

  Canonicalize `stagingRoot` once. For each descriptor in order, combine and canonicalize the file path, require it to remain directly below the root, write UTF-8 content once, and assert `File.Exists`. Return a read-only ordered list. Reject pre-existing destination files to prevent accidental overwrite in a reused directory.

- [ ] **Step 4: Rerun tests, build, and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockImportBundleParserTests|FullyQualifiedName~BlockImportStagerTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer.OpennessWorker/Openness/BlockImportStager.cs TiaMcpServer.Tests/BlockImportStagerTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "fix: stage exact block import documents"
  ```

---

## Task 3: Verify block-update postconditions before success

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionEvidence.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionVerifier.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockImportResult.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs` only to reuse a non-mutating export helper if needed
- Modify: `TiaMcpServer.OpennessWorker/Openness/CompileChecker.cs` only to expose existing compile evidence if needed
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/BlockPostconditionVerifierTests.cs`
- Create: `TiaMcpServer.Tests/BlockImportCoordinatorTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`

**Acceptance:** AC-029, AC-032, AC-043; implementation support for live AC-026–AC-028.

- [ ] **Step 1: Add RED pure decision tests.**

  Link the evidence/verifier/coordinator files and the already-created `TiaMcpServer.OpennessWorker/WorkerOperationException.cs` into the test project. Test:

  ```csharp
  [Fact] public void Verify_AcceptsSuccessfulCompileAndNonEmptyReExport()
  [Fact] public void Verify_RejectsCompileFailureAsPostconditionFailed()
  [Fact] public void Verify_RejectsMissingReExportAsPostconditionFailed()
  [Fact] public void Verify_FailureCarriesUncertainStateWarning()
  [Fact] public void Execute_InvokesImportOnceAfterEveryStagedFileExists()
  [Fact] public void Execute_InvalidBundle_DoesNotInvokeImport()
  [Fact] public void Execute_PostconditionFailure_DoesNotRetryImport()
  [Fact] public void Execute_CleansStagingAfterSuccessAndFailure()
  ```

  `BlockPostconditionEvidence` is immutable and contains `CompileSucceeded`, `ReExportSucceeded`, and a sanitized diagnostic message. The verifier throws `WorkerOperationException(postcondition_failed, ...)` on either failed predicate.

- [ ] **Step 2: Add RED worker-response integration coverage.**

  Give FakeWorker a block-update postcondition-failure scenario. Assert the host receives `postcondition_failed`, success is false, warning says project state may have changed, and no automatic retry occurs.

- [ ] **Step 3: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockPostconditionVerifierTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  ```

- [ ] **Step 4: Implement the testable import coordinator.**

  Add this pure orchestration seam:

  ```csharp
  internal static class BlockImportCoordinator
  {
      public static BlockImportResult Execute(
          string documentName,
          string rawContent,
          Action<DirectoryInfo, string> importDocuments,
          Func<BlockPostconditionEvidence> verifyPostcondition);
  }
  ```

  It parses before creating a temp directory, stages all documents, asserts count/order/existence, calls `importDocuments` exactly once with the staging directory and primary name, calls `verifyPostcondition` exactly once, and cleans staging in `finally`. No failure path replays either delegate.

- [ ] **Step 5: Replace blind worker import with the coordinator and gather real postconditions.**

  Change `BlockImporter` to the Siemens adapter:

  ```csharp
  public static BlockImportResult Import(
      Project project,
      string blockPath,
      string yamlContent)
  ```

  Remove `Contains("--- FILE:")` mode detection and the unvalidated `WriteContentToTempDir` path. For a single XML document, derive the fallback safe name as `Path.GetFileName(blockPath) + ".xml"`; bundle delimiters remain authoritative for multi-document names. Pass one delegate that calls Siemens `ImportFromDocuments` with the exact directory/primary name and one delegate that gathers this evidence after import:

  After `ImportFromDocuments` returns:

  1. resolve the target block through the existing `BlockTargetResolver`/export path;
  2. compile that target through `CompileChecker.Compile` exactly once;
  3. fail evidence if compile reports errors or cannot complete;
  4. re-export the target as documents through `BlockExporter` into a new verification temp directory;
  5. require the re-exported primary document to exist and be non-empty;
  6. pass immutable evidence to `BlockPostconditionVerifier`;
  7. only then return success.

  Never re-import or replay on verification failure.

- [ ] **Step 6: Return warnings separately.**

  Use this result:

  ```csharp
  internal sealed class BlockImportResult
  {
      public BlockImportResult(string payload, IReadOnlyList<string> warnings);
      public string Payload { get; }
      public IReadOnlyList<string> Warnings { get; }
  }
  ```

  Do not return from inside the import `try`. Capture the payload or primary exception, run cleanup in `finally`, then construct the immutable result or rethrow an enriched categorized exception after cleanup completes. Update `Program.UpdateBlockLogic` to copy payload/warnings to `WorkerResponse`. If staging or verification cleanup fails, add a capped cleanup warning. If import/postcondition failed, preserve that primary failure; cleanup never masks it.

- [ ] **Step 7: Run focused and full verification, then commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockImport|FullyQualifiedName~BlockPostcondition|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker/Program.cs TiaMcpServer.Tests
  git commit -m "fix: verify block update postconditions"
  ```

---

## Task 4: Generate non-empty SCL compile units for supported block types

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs`
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs`
- Create: `TiaMcpServer.Tests/BlockSourceGeneratorTests.cs`
- Create: `TiaMcpServer.Tests/BlockSourceValidatorTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Acceptance:** AC-030; implementation support for live AC-031 and boundary AC-042.

- [ ] **Step 1: Add RED source-generation tests.**

  Link the two pure helper files into the test project. Add:

  ```csharp
  [Theory]
  [InlineData("FB", "SCL")]
  [InlineData("FC", "SCL")]
  [InlineData("OB", "SCL")]
  public void Generate_SclBlock_HasNonEmptyCompileUnit(string blockType, string language)

  [Theory]
  [InlineData("FB", "UNKNOWN")]
  [InlineData("DB", "SCL")]
  public void Generate_RejectsUnsupportedTypeLanguagePair(string blockType, string language)
  ```

  Parse generated XML with `XDocument`. Require at least one `SW.Blocks.CompileUnit`, a non-empty network/source descendant, the requested block name/type, and well-formed XML. The current FC/OB SCL cases must fail before implementation.

- [ ] **Step 2: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockSourceGeneratorTests|FullyQualifiedName~BlockSourceValidatorTests"
  ```

- [ ] **Step 3: Extract one source generator.**

  Add:

  ```csharp
  internal static class BlockSourceGenerator
  {
      public static string Generate(
          string blockName,
          string blockType,
          string language,
          string? obEventClass);
  }
  ```

  Move the existing FB/FC/OB XML generation into this class. Reuse the existing FB structured-text compile-unit shape for SCL and apply the same non-empty compile-unit construction to supported FC and OB SCL generation. Do not change STL behavior in Phase 5 and do not duplicate three independent raw compile-unit templates.

- [ ] **Step 4: Validate generated source before import.**

  Add:

  ```csharp
  internal static class BlockSourceValidator
  {
      public static void Validate(
          string blockType,
          string language,
          string xml);
  }
  ```

  It parses XML, checks type/language compatibility, and requires a non-empty compile unit for SCL. Invalid caller input throws `WorkerOperationException(validation_error, ...)` explicitly. `BlockMutationService.CreateBlock` calls `Generate`, then `Validate`, before writing the temp XML or invoking Siemens. Existing STL behavior remains untouched.

- [ ] **Step 5: Rerun tests, build, and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockSourceGeneratorTests|FullyQualifiedName~BlockSourceValidatorTests"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs TiaMcpServer.Tests/BlockSourceGeneratorTests.cs TiaMcpServer.Tests/BlockSourceValidatorTests.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
  git commit -m "fix: generate compilable SCL block sources"
  ```

---

## Task 5: Verify created blocks before reporting success

**Files:**

- Create: `TiaMcpServer.OpennessWorker/Openness/BlockCreationCoordinator.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs`
- Reuse: `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionVerifier.cs`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs`
- Modify: `TiaMcpServer.FakeWorker/Program.cs`
- Create: `TiaMcpServer.Tests/BlockMutationPostconditionTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Modify: `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`

**Acceptance:** AC-030, AC-031 support, AC-032, AC-043.

- [ ] **Step 1: Add RED coordinator and postcondition tests.**

  Link a not-yet-created `BlockCreationCoordinator.cs` into the test project and add tests proving the import delegate and verification delegate are each called once, compile/resolve failure prevents a returned success, and no failure retries the import. Add FakeWorker integration proving a forced postcondition failure is categorized, warned, and never retried.

  The coordinator seam is:

  ```csharp
  internal static class BlockCreationCoordinator
  {
      public static TResult Execute<TResult>(
          Func<TResult> importBlock,
          Func<BlockPostconditionEvidence> verifyPostcondition);
  }
  ```

- [ ] **Step 2: Run focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockMutationPostconditionTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests"
  ```

- [ ] **Step 3: Implement the coordinator and verify resolve/compile after one import.**

  Keep the public signature:

  ```csharp
  public static BlockMutationResultInfo CreateBlock(
      Project project,
      string blockPath,
      string blockType,
      string? language,
      string? obEventClass)
  ```

  Route the existing import through `BlockCreationCoordinator.Execute`. The import delegate calls `group.Blocks.Import(...)` exactly once. Its verification delegate then:

  1. resolve the created block at the exact requested path;
  2. fail `postcondition_failed` if it cannot be resolved;
  3. compile it once with `CompileChecker`;
  4. fail `postcondition_failed` with uncertain-state warning on compile errors;
  5. return `BlockMutationResultInfo` success only after both checks.

- [ ] **Step 4: Run the complete Plan 3 automated gate.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockImport|FullyQualifiedName~BlockPostcondition|FullyQualifiedName~BlockSource|FullyQualifiedName~BlockMutation"
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  ```

- [ ] **Step 5: Review and commit.**

  Review path canonicalization, XML parsing, exception text, temp cleanup, exact Siemens invocation counts, and no-retry behavior. Then commit:

  ```powershell
  git add TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker TiaMcpServer.Tests
  git commit -m "fix: verify PLC block write outcomes"
  ```

## Plan 3 Exit Gate

- [ ] All unsafe/missing/duplicate document cases fail before staging/import.
- [ ] Staging is deterministic, canonical, exact, and cleaned in `finally`.
- [ ] Update success requires compile plus non-empty re-export.
- [ ] FB, FC, and OB SCL generation contains a non-empty compile unit where supported.
- [ ] Create success requires resolve plus compile.
- [ ] Postcondition failures are failures with uncertain-state warnings and one write attempt.
- [ ] Full serial build and tests pass before live certification.
