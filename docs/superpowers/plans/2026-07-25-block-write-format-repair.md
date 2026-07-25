# Block Write Format Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `update_block_logic` work for round-trip edits, and make `create_block` work for SCL and GlobalDB.

**Architecture:** The block bundle format gets one owned producer/consumer pair enforced by a round-trip test. `update_block_logic` stops feeding Simatic ML XML to the SIMATIC SD documents importer and routes it to `Blocks.Import(FileInfo, …)` instead — restoring routing that existed at `c53e6f4` and was removed as collateral damage by `dddf9d2`. Every decision that a refactor could silently delete is extracted into a Siemens-free, directly-tested function.

**Tech Stack:** C# / .NET — host `net8.0`, worker `net48`, contracts `netstandard2.0`. xunit. Siemens TIA Openness V21.

**Spec:** `docs/superpowers/specs/2026-07-25-block-write-format-repair-design.md`

## Global Constraints

- Build with `dotnet build TiaMcpServer.sln -m:1` — `-m:1` is required; parallel builds conflict over the worker copy step.
- CI/stub build: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`.
- Test with `dotnet test TiaMcpServer.Tests`.
- `global.json` pins SDK 8.0.400 with `rollForward: latestMajor`. Use `dotnet`, never `dotnet8`.
- **Worker source files are linked into the test project one-by-one in `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` (lines 49-65). There is no glob.** Every new file under `TiaMcpServer.OpennessWorker/Openness/` that needs tests MUST be added there explicitly or it will not compile into the test assembly.
- **A file that has `using Siemens.Engineering…` cannot be linked into the test project** (net8.0 cannot load the net48 Siemens assemblies). `BlockExporter.cs`, `BlockImporter.cs` and `BlockMutationService.cs` are therefore untestable. All new logic goes in Siemens-free files. This is the existing pattern — `BlockExporterVerification.cs` is the Siemens-free half of `BlockExporter`'s partial class and is linked at line 59.
- Immutability: return new objects, never mutate inputs.
- Files: 200-400 lines typical, 800 max. Functions under 50 lines.
- Conventional commits: `<type>: <description>`.
- Never commit Siemens DLLs.

## Verified facts this plan depends on

Do not re-derive these; they were established against the live V21 project and the git history.

- `ImportFromDocuments(dir, name, opts)` takes an **extension-less base name**, symmetric with `ExportAsDocuments`. Corroborated by commit `1163fe4`: *"ImportFromDocuments finds the file by fileNameWithoutExtension lookup"*.
- `Block.Export(…, ExportOptions.None)` **does** emit `<DocumentInfo>` with a volatile `<Created>` timestamp (commit `c53e6f4` documents the resulting preview→apply failures). Stripping it is load-bearing.
- An empty self-closing `<NetworkSource />` is valid in a compile unit — five occur in the real `Inputs_FB.xml` export.
- Real V21 GlobalDB uses `<MemoryLayout>Optimized</MemoryLayout>` and `<ProgrammingLanguage>DB</ProgrammingLanguage>`. There is no `<Optimized>` element.
- Simatic ML `ID` attributes are hex and monotonic in real exports — **but `create_block` for LAD works today while emitting `1, 3, 2`, so ID ordering is not a blocker.** Task 6 normalizes it as hygiene, not as the fix.

---

## File Structure

**New files (all Siemens-free, all must be added to `TiaMcpServer.Tests.csproj`):**

| File | Responsibility |
|---|---|
| `TiaMcpServer.OpennessWorker/Openness/BlockBundleFormat.cs` | Owns the `--- FILE:` delimiter. `Compose` + the shared delimiter regexes the parser consumes. |
| `TiaMcpServer.OpennessWorker/Openness/BlockImportRouting.cs` | Decides Simatic ML vs SIMATIC SD, picks the authoritative document, guards non-authoritative edits. |
| `TiaMcpServer.OpennessWorker/Openness/BlockXmlSanitizer.cs` | Surgical `<DocumentInfo>` removal that preserves every other byte. |

**Modified files:**

| File | Change |
|---|---|
| `TiaMcpServer.OpennessWorker/Openness/BlockImportBundleParser.cs` | Consume `BlockBundleFormat`'s regexes instead of private copies. |
| `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs` | Build output via `Compose`; sanitize via `BlockXmlSanitizer`; drop the `(unavailable)` pseudo-document; pass the resolved base name to verification. |
| `TiaMcpServer.OpennessWorker/Openness/BlockExporterVerification.cs` | Use `resolvedTargetDocumentName` for the re-export (it is currently validated then ignored). |
| `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs` | Route by `BlockImportRouting`; Simatic ML goes to `Blocks.Import(FileInfo, …)`. |
| `TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs` | GlobalDB and SCL/STL templates. |
| `TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs` | Invert the SCL body rule; fix the GlobalDB language rule. |
| `TiaMcpServer.OpennessWorker/Openness/BlockWritePreflight.cs` | Type-aware language default. |
| `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` | Link the three new files. |

**Test fixture already in place:** `TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.bundle.txt` — real `get_block_content` output captured 2026-07-25. It is a **parser input fixture**, not an exporter golden: it pins "this historical output must parse into 2 documents" and stays valid after the exporter changes.

## Task dependency order

**Execute Task 4 before Task 1.** Task 1 rewrites `BlockExporter.Export` to call
`BlockXmlSanitizer.RemoveDocumentInfo`, which Task 4 creates. The tasks are numbered by
subject area, not by execution order.

```
Task 4 (XML sanitizer) → Task 1 (bundle format) → Task 2 (import routing) → Task 3 (verification names)
Task 5 (GlobalDB)      ── fully independent, no shared files with 1-4
Task 6 (SCL/STL)       ── fully independent, no shared files with 1-4
Task 7 (live E2E)      ── requires all of the above
```

Tasks 5 and 6 touch only `BlockSourceGenerator.cs`, `BlockSourceValidator.cs` and
`BlockWritePreflight.cs`; tasks 1-4 touch none of those three. The two groups can be worked in
parallel without conflict. Within task 5+6, both edit the same two files — do them in sequence.

---

### Task 1: Bundle format — one owned contract

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockBundleFormat.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockImportBundleParser.cs:11-16`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs:52-84`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj:66`
- Test: `TiaMcpServer.Tests/BlockBundleFormatTests.cs`

**Interfaces:**
- Consumes: `BlockImportDocument(string logicalName, string safeFileName, string content)`, `BlockImportBundleParser.Parse(string documentName, string rawContent)`, `WorkerOperationException`, `WorkerFailureCategories.ValidationError`.
- Produces: `BlockBundleFormat.Compose(IReadOnlyList<BlockImportDocument>) → string`; `BlockBundleFormat.DocumentDelimiter` and `BlockBundleFormat.DocumentDelimiterCandidate` (`Regex`, used by the parser); `BlockBundleFormat.ContainsDelimiterLine(string) → bool`.

**Why the round-trip property is stated the way it is:** `Compose` guarantees every marker after the first is preceded by exactly one `\n`, which means it may append a newline to a document that lacked one. So `Parse(Compose(x)) == x` is false by design. The property that actually matters — and that this task tests — is that composing is **stable under a parse round-trip**: `Compose(Parse(Compose(x))) == Compose(x)`. That is exactly the guarantee the write path needs, since it re-reads what the read path wrote.

- [ ] **Step 1: Write the failing tests**

Create `TiaMcpServer.Tests/BlockBundleFormatTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockBundleFormatTests
{
    private static BlockImportDocument Doc(string name, string content)
        => new BlockImportDocument(name, name, content);

    [Fact]
    public void Compose_inserts_a_newline_before_every_marker_after_the_first()
    {
        var composed = BlockBundleFormat.Compose(new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />"),      // no trailing newline
            Doc("Main.s7dcl", "BLOCK\r\n"),
        });

        Assert.Contains("<Document />\n--- FILE: Main.s7dcl ---\n", composed);
    }

    [Fact]
    public void Parse_recovers_every_document_from_Compose_output()
    {
        var documents = new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />"),
            Doc("Main.s7dcl", "BLOCK\r\n"),
            Doc("Main.s7res", "res"),
        };

        var parsed = BlockImportBundleParser.Parse("Main.xml", BlockBundleFormat.Compose(documents));

        Assert.Equal(3, parsed.Documents.Count);
        Assert.Equal(
            new[] { "Main.xml", "Main.s7dcl", "Main.s7res" },
            parsed.Documents.Select(d => d.LogicalName).ToArray());
    }

    [Fact]
    public void Compose_is_stable_under_a_parse_round_trip()
    {
        var documents = new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />"),
            Doc("Main.s7dcl", "BLOCK\r\nEND_BLOCK\r\n"),
        };

        var once = BlockBundleFormat.Compose(documents);
        var twice = BlockBundleFormat.Compose(BlockImportBundleParser.Parse("Main.xml", once).Documents);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void Compose_rejects_content_that_would_parse_as_a_delimiter()
    {
        var documents = new List<BlockImportDocument>
        {
            Doc("Main.xml", "<Document />\n--- FILE: Injected.xml ---\nevil"),
        };

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockBundleFormat.Compose(documents));
        Assert.Contains("delimiter", exception.Message);
    }

    [Fact]
    public void Real_captured_get_block_content_output_parses_into_both_documents()
    {
        var raw = File.ReadAllText(
            Path.Combine("Fixtures", "get_block_content.ob-lad.bundle.txt"));

        var parsed = BlockImportBundleParser.Parse("Main.xml", raw);

        Assert.Equal(2, parsed.Documents.Count);
        Assert.Equal("Main.xml", parsed.Documents[0].LogicalName);
        Assert.Equal("Main.s7dcl", parsed.Documents[1].LogicalName);
        Assert.DoesNotContain("--- FILE:", parsed.Documents[0].Content);
    }
}
```

- [ ] **Step 2: Make the fixture available to the test binary**

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, add inside the second `<ItemGroup>` (the one ending at line 66):

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\BlockBundleFormat.cs" Link="Linked\Openness\BlockBundleFormat.cs" />
    <None Include="Fixtures\get_block_content.ob-lad.bundle.txt" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter BlockBundleFormatTests`
Expected: FAIL — `BlockBundleFormat` does not exist (compile error `CS0103`).

- [ ] **Step 4: Create `BlockBundleFormat.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Sole owner of the multi-document block bundle format returned by get_block_content and
/// consumed by update_block_logic. Both directions live here so the producer cannot drift
/// from the consumer — which is exactly the defect this type was introduced to end.
/// </summary>
internal static class BlockBundleFormat
{
    internal static readonly Regex DocumentDelimiter = new Regex(
        @"^--- FILE: (?<name>.+) ---(?:\r?\n|$)",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static readonly Regex DocumentDelimiterCandidate = new Regex(
        @"^--- FILE:",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    public static bool ContainsDelimiterLine(string content)
    {
        return content != null && DocumentDelimiterCandidate.IsMatch(content);
    }

    /// <summary>
    /// Renders documents into the bundle format. Guarantees the invariant the parser relies
    /// on: every marker after the first is preceded by exactly one newline. Content that does
    /// not already end in a newline gets one appended, so Compose is stable under a parse
    /// round trip rather than byte-identical to its input.
    /// </summary>
    public static string Compose(IReadOnlyList<BlockImportDocument> documents)
    {
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        if (documents.Count == 0)
        {
            throw ValidationFailure("A block bundle must contain at least one document.");
        }

        var builder = new StringBuilder();

        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];

            if (ContainsDelimiterLine(document.Content))
            {
                throw ValidationFailure(
                    "Block bundle content must not contain a line that parses as a document delimiter.");
            }

            builder.Append("--- FILE: ").Append(document.LogicalName).Append(" ---\n");
            builder.Append(document.Content);

            var isLast = index == documents.Count - 1;
            if (!isLast && !EndsWithNewline(document.Content))
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    private static bool EndsWithNewline(string content)
    {
        return content.Length > 0 && content[content.Length - 1] == '\n';
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
```

- [ ] **Step 5: Point the parser at the shared regexes**

In `BlockImportBundleParser.cs`, delete the two private regex fields at lines 11-16 and replace every use:

```csharp
    // DELETE the private DocumentDelimiter and DocumentDelimiterCandidate fields.
    // Then replace the three usages:
    //   line 31:  var delimiters = BlockBundleFormat.DocumentDelimiter.Matches(rawContent);
    //   line 84:  foreach (Match candidate in BlockBundleFormat.DocumentDelimiterCandidate.Matches(rawContent))
    //   line 86:  var delimiter = BlockBundleFormat.DocumentDelimiter.Match(rawContent, candidate.Index);
```

Leave `ReservedDeviceName` where it is — it belongs to name validation, not the delimiter contract.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter BlockBundleFormatTests`
Expected: PASS, 5 tests.

- [ ] **Step 7: Rebuild the exporter output through `Compose`**

In `BlockExporter.cs`, replace the body of `Export` (lines 52-84) with:

```csharp
            var documents = new List<BlockImportDocument>();

            // Simatic ML XML (FlgNet) — the authoritative document for update_block_logic.
            // Export() requires a consistent block. When it fails we emit no XML document at
            // all rather than a placeholder: a placeholder would round-trip back into
            // update_block_logic as a real document name and be staged to disk.
            try
            {
                string xmlPath = Path.Combine(tempDir, target.DocumentName + ".xml");
                target.Block!.Export(new FileInfo(xmlPath), ExportOptions.None);
                var xmlName = target.DocumentName + ".xml";
                documents.Add(new BlockImportDocument(
                    xmlName,
                    xmlName,
                    BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(xmlPath))));
            }
            catch (Exception)
            {
                // Intentionally no document. The write path reports this as an actionable
                // error when it finds no Simatic ML document in the bundle.
            }

            // s7dcl documents package (human-readable rung text) — read-only context.
            DocumentExportResult result = target.Block!.ExportAsDocuments(
                new DirectoryInfo(tempDir), target.DocumentName);

            if (result.State != DocumentResultState.Success)
                throw new InvalidOperationException($"Export failed with state: {result.State}");

            foreach (FileInfo file in result.ExportedDocuments)
            {
                documents.Add(new BlockImportDocument(
                    file.Name, file.Name, File.ReadAllText(file.FullName)));
            }

            return BlockBundleFormat.Compose(documents);
```

Add `using System.Collections.Generic;` to the top of the file.

**This step requires Task 4 to be complete** — it calls `BlockXmlSanitizer.RemoveDocumentInfo`. Task 4 also deletes the `StripNonDeterministic` method this replaces; if Task 4 has already run, that method is gone and nothing further is needed here.

- [ ] **Step 8: Build and run the whole suite**

Run: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`
Expected: Build succeeded.
Run: `dotnet test TiaMcpServer.Tests`
Expected: all tests pass. `BlockImportBundleParserTests` should still pass unchanged — it tests hand-written inputs the parser must still accept.

- [ ] **Step 9: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockBundleFormat.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockImportBundleParser.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs \
        TiaMcpServer.Tests/BlockBundleFormatTests.cs \
        TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.bundle.txt \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "fix: give the block bundle format one owned producer and consumer"
```

---

### Task 2: Route Simatic ML XML to the Simatic ML importer

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockImportRouting.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs:37-53`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs:37`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Test: `TiaMcpServer.Tests/BlockImportRoutingTests.cs`

**Interfaces:**
- Consumes: `ParsedBlockImportBundle`, `BlockImportDocument`, `WorkerOperationException`.
- Produces:
  - `enum BlockImportRoute { SimaticMl, SimaticSd }`
  - `BlockImportRouting.SelectRoute(ParsedBlockImportBundle) → BlockImportRoute`
  - `BlockImportRouting.SelectAuthoritativeDocument(ParsedBlockImportBundle) → BlockImportDocument`
  - `BlockImportRouting.SimaticSdBaseName(ParsedBlockImportBundle) → string`
  - `BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(ParsedBlockImportBundle submitted, ParsedBlockImportBundle current, string authoritativeName) → void`

**Why this is a separate testable type:** the identical routing existed at `c53e6f4` inside `BlockImporter` and `dddf9d2` deleted it during an unrelated refactor with no test objecting. Putting the decision in a Siemens-free function with its own tests is the anti-regression measure.

- [ ] **Step 1: Write the failing tests**

Create `TiaMcpServer.Tests/BlockImportRoutingTests.cs`:

```csharp
using System.Collections.Generic;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockImportRoutingTests
{
    private static ParsedBlockImportBundle Bundle(params (string Name, string Content)[] documents)
    {
        var list = new List<BlockImportDocument>();
        foreach (var (name, content) in documents)
        {
            list.Add(new BlockImportDocument(name, name, content));
        }

        return new ParsedBlockImportBundle(list[0].LogicalName, list);
    }

    [Fact]
    public void A_bundle_containing_simatic_ml_xml_routes_to_the_simatic_ml_importer()
    {
        var bundle = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "BLOCK\r\n"));

        Assert.Equal(BlockImportRoute.SimaticMl, BlockImportRouting.SelectRoute(bundle));
        Assert.Equal("Main.xml", BlockImportRouting.SelectAuthoritativeDocument(bundle).LogicalName);
    }

    [Fact]
    public void A_bundle_without_xml_routes_to_the_documents_importer()
    {
        var bundle = Bundle(("Main.s7dcl", "BLOCK\r\n"));

        Assert.Equal(BlockImportRoute.SimaticSd, BlockImportRouting.SelectRoute(bundle));
    }

    [Fact]
    public void The_documents_route_uses_an_extension_less_base_name()
    {
        var bundle = Bundle(("Main.s7dcl", "BLOCK\r\n"), ("Main.s7res", "res"));

        Assert.Equal("Main", BlockImportRouting.SimaticSdBaseName(bundle));
    }

    [Fact]
    public void Editing_a_non_authoritative_document_is_rejected()
    {
        var submitted = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "EDITED\r\n"));
        var current = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "BLOCK\r\n"));

        var exception = Assert.Throws<WorkerOperationException>(() =>
            BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(submitted, current, "Main.xml"));

        Assert.Contains("Main.s7dcl", exception.Message);
        Assert.Contains("Main.xml", exception.Message);
    }

    [Fact]
    public void Editing_the_authoritative_document_is_allowed()
    {
        var submitted = Bundle(("Main.xml", "<Document>edited</Document>"), ("Main.s7dcl", "BLOCK\r\n"));
        var current = Bundle(("Main.xml", "<Document />"), ("Main.s7dcl", "BLOCK\r\n"));

        BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(submitted, current, "Main.xml");
    }
}
```

- [ ] **Step 2: Link the new file and run the tests to verify they fail**

Add to `TiaMcpServer.Tests.csproj`:

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\BlockImportRouting.cs" Link="Linked\Openness\BlockImportRouting.cs" />
```

Run: `dotnet test TiaMcpServer.Tests --filter BlockImportRoutingTests`
Expected: FAIL — `BlockImportRouting` does not exist.

- [ ] **Step 3: Create `BlockImportRouting.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal enum BlockImportRoute
{
    SimaticMl,
    SimaticSd,
}

/// <summary>
/// Chooses which Openness importer a parsed bundle belongs to.
///
/// This decision is deliberately isolated and directly tested. It previously lived inline in
/// BlockImporter (commit c53e6f4) and was removed by an unrelated refactor (dddf9d2) without
/// any test failing, which left update_block_logic broken for months.
/// </summary>
internal static class BlockImportRouting
{
    public static BlockImportRoute SelectRoute(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));
        return FindSimaticMlDocument(bundle) is null
            ? BlockImportRoute.SimaticSd
            : BlockImportRoute.SimaticMl;
    }

    public static BlockImportDocument SelectAuthoritativeDocument(ParsedBlockImportBundle bundle)
    {
        return FindSimaticMlDocument(bundle)
            ?? throw ValidationFailure(
                "This bundle contains no Simatic ML (.xml) document. The block could not be "
                + "exported as Simatic ML, which usually means it is inconsistent — compile it "
                + "in TIA Portal and read it again before writing.");
    }

    public static string SimaticSdBaseName(ParsedBlockImportBundle bundle)
    {
        if (bundle is null) throw new ArgumentNullException(nameof(bundle));

        // ImportFromDocuments resolves the document set by file name WITHOUT extension.
        return Path.GetFileNameWithoutExtension(bundle.PrimaryDocumentName);
    }

    /// <summary>
    /// Only the authoritative document is applied. If the caller edited any other document we
    /// must refuse rather than silently discard their edit.
    /// </summary>
    public static void EnsureOnlyAuthoritativeDocumentChanged(
        ParsedBlockImportBundle submitted,
        ParsedBlockImportBundle current,
        string authoritativeName)
    {
        if (submitted is null) throw new ArgumentNullException(nameof(submitted));
        if (current is null) throw new ArgumentNullException(nameof(current));

        var currentByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in current.Documents)
        {
            currentByName[document.LogicalName] = document.Content;
        }

        foreach (var document in submitted.Documents)
        {
            if (string.Equals(document.LogicalName, authoritativeName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!currentByName.TryGetValue(document.LogicalName, out var currentContent)
                || !string.Equals(currentContent, document.Content, StringComparison.Ordinal))
            {
                throw ValidationFailure(
                    $"'{document.LogicalName}' was modified, but only '{authoritativeName}' is "
                    + "applied by update_block_logic. Re-read the block, edit "
                    + $"'{authoritativeName}', and submit again.");
            }
        }
    }

    private static BlockImportDocument? FindSimaticMlDocument(ParsedBlockImportBundle bundle)
    {
        foreach (var document in bundle.Documents)
        {
            if (document.LogicalName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                return document;
            }
        }

        return null;
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter BlockImportRoutingTests`
Expected: PASS, 5 tests.

- [ ] **Step 5: Wire the routing into `BlockImporter`**

`BlockImportCoordinator.Execute` currently hands the importer a directory plus the primary document name. Change its `importDocuments` delegate to also receive the parsed bundle. In `BlockImportCoordinator.cs`, change the parameter type at line 13 and the call at line 37:

```csharp
        Action<DirectoryInfo, ParsedBlockImportBundle> importDocuments,
```
```csharp
            importDocuments(new DirectoryInfo(stagingPath), bundle);
```

Then replace `BlockImporter.ImportDocuments` (lines 37-53) with:

```csharp
    private static void ImportDocuments(
        Project project,
        BlockAddress address,
        string blockPath,
        DirectoryInfo directory,
        ParsedBlockImportBundle bundle)
    {
        var target = BlockTargetResolver.ResolveForImport(project, address);

        if (BlockImportRouting.SelectRoute(bundle) == BlockImportRoute.SimaticMl)
        {
            var authoritative = BlockImportRouting.SelectAuthoritativeDocument(bundle);

            if (bundle.Documents.Count > 1)
            {
                var current = BlockImportBundleParser.Parse(
                    authoritative.LogicalName,
                    BlockExporter.Export(project, blockPath));
                BlockImportRouting.EnsureOnlyAuthoritativeDocumentChanged(
                    bundle, current, authoritative.LogicalName);
            }

            // A single Simatic ML XML document must go through Import(FileInfo, ImportOptions).
            // ImportFromDocuments is only for SIMATIC SD packages keyed by an extension-less
            // base name; passing it a bare .xml produces a misleading "file does not exist".
            var xmlPath = Path.Combine(directory.FullName, authoritative.SafeFileName);
            target.Group.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
            return;
        }

        var result = target.Group.Blocks.ImportFromDocuments(
            directory,
            BlockImportRouting.SimaticSdBaseName(bundle),
            ImportDocumentOptions.Override);

        if (result.State != DocumentResultState.Success)
        {
            throw new InvalidOperationException("Import failed with state: " + result.State);
        }
    }
```

Update the call site in `BlockImporter.Import` (lines 26-30):

```csharp
            (directory, bundle) => ImportDocuments(
                project,
                preflight.Address,
                blockPath,
                directory,
                bundle),
```

- [ ] **Step 6: Build and run the whole suite**

Run: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`
Expected: Build succeeded.
Run: `dotnet test TiaMcpServer.Tests`
Expected: all pass. `BlockImportCoordinatorTests` will need its lambda signature updated to `(directory, bundle) => …`; update the test, not the production signature.

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockImportRouting.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs \
        TiaMcpServer.Tests/BlockImportRoutingTests.cs \
        TiaMcpServer.Tests/BlockImportCoordinatorTests.cs \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "fix: route Simatic ML block documents to the Simatic ML importer"
```

---

### Task 3: Re-export verification must use the resolved base name

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporterVerification.cs:36`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs:21-31`
- Test: `TiaMcpServer.Tests/BlockExporterVerificationTests.cs`

**Interfaces:**
- Consumes: `BlockExporter.VerifyPrimaryDocument(string resolvedTargetDocumentName, string primaryDocumentName, Func<DirectoryInfo, string, bool> exportDocuments, Action<string>? cleanupDirectory = null)`.
- Produces: no new API. Behaviour change only — the delegate now receives `resolvedTargetDocumentName`.

**Context:** `resolvedTargetDocumentName` is validated at lines 22-25 then never used; commit `71b6687` substituted the declared name (`Main.xml`) for the resolved base name (`Main`). `ExportAsDocuments` treats its argument as a base name, so `File.Exists(dir/"Main.xml")` can never pass.

- [ ] **Step 1: Write the failing test**

Append to `TiaMcpServer.Tests/BlockExporterVerificationTests.cs`:

```csharp
    [Fact]
    public void Re_export_uses_the_resolved_base_name_not_the_declared_document_name()
    {
        string? observedName = null;

        var evidence = BlockExporter.VerifyPrimaryDocument(
            resolvedTargetDocumentName: "Main",
            primaryDocumentName: "Main.xml",
            exportDocuments: (directory, name) =>
            {
                observedName = name;
                return true;
            },
            cleanupDirectory: _ => { });

        Assert.Equal("Main", observedName);
        Assert.True(evidence.ReExportSucceeded);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter Re_export_uses_the_resolved_base_name`
Expected: FAIL — `Assert.Equal() Failure: Expected: "Main", Actual: "Main.xml"`.

- [ ] **Step 3: Use the resolved name**

In `BlockExporterVerification.cs` line 36, change:

```csharp
            reExportSucceeded = exportDocuments(new DirectoryInfo(verificationDirectory), resolvedTargetDocumentName);
```

- [ ] **Step 4: Assert on the export result rather than a guessed path**

In `BlockExporter.cs`, replace the lambda at lines 24-31:

```csharp
                (directory, documentName) =>
                {
                    var result = target.Block!.ExportAsDocuments(directory, documentName);
                    if (result.State != DocumentResultState.Success)
                    {
                        return false;
                    }

                    foreach (FileInfo exported in result.ExportedDocuments)
                    {
                        if (exported.Exists && exported.Length > 0)
                        {
                            return true;
                        }
                    }

                    return false;
                });
```

- [ ] **Step 5: Run tests and commit**

Run: `dotnet test TiaMcpServer.Tests`
Expected: all pass.

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockExporterVerification.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs \
        TiaMcpServer.Tests/BlockExporterVerificationTests.cs
git commit -m "fix: verify block re-export using the resolved document base name"
```

---

### Task 4: Surgical `<DocumentInfo>` removal

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/BlockXmlSanitizer.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs` (remove `StripNonDeterministic`)
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Test: `TiaMcpServer.Tests/BlockXmlSanitizerTests.cs`

**Interfaces:**
- Produces: `BlockXmlSanitizer.RemoveDocumentInfo(string xml) → string`.

**Context:** `StripNonDeterministic` used `XDocument.Parse(...).ToString()`, which removes `<DocumentInfo>` but also drops the `<?xml … ?>` prolog and re-indents the whole document. Under Task 2 that text becomes the input to `Blocks.Import`, so it must stay byte-faithful. `<DocumentInfo>` genuinely must go — commit `c53e6f4` records the preview→apply failures its `<Created>` timestamp caused.

- [ ] **Step 1: Write the failing tests**

Create `TiaMcpServer.Tests/BlockXmlSanitizerTests.cs`:

```csharp
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockXmlSanitizerTests
{
    private const string WithDocumentInfo =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
        "<Document>\r\n" +
        "  <Engineering version=\"V21\" />\r\n" +
        "  <DocumentInfo>\r\n" +
        "    <Created>2026-07-25T10:00:00.1234567Z</Created>\r\n" +
        "  </DocumentInfo>\r\n" +
        "  <SW.Blocks.OB ID=\"0\" />\r\n" +
        "</Document>";

    [Fact]
    public void DocumentInfo_is_removed()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.DoesNotContain("DocumentInfo", result);
        Assert.DoesNotContain("Created", result);
    }

    [Fact]
    public void The_xml_declaration_survives()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", result);
    }

    [Fact]
    public void Every_other_byte_survives_including_indentation_and_line_endings()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.Equal(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<Document>\r\n" +
            "  <Engineering version=\"V21\" />\r\n" +
            "  <SW.Blocks.OB ID=\"0\" />\r\n" +
            "</Document>",
            result);
    }

    [Fact]
    public void A_self_closing_DocumentInfo_is_removed()
    {
        var result = BlockXmlSanitizer.RemoveDocumentInfo(
            "<Document>\r\n  <DocumentInfo />\r\n  <SW.Blocks.OB ID=\"0\" />\r\n</Document>");

        Assert.Equal(
            "<Document>\r\n  <SW.Blocks.OB ID=\"0\" />\r\n</Document>",
            result);
    }

    [Fact]
    public void Xml_without_DocumentInfo_is_returned_unchanged()
    {
        const string xml = "<Document>\r\n  <SW.Blocks.OB ID=\"0\" />\r\n</Document>";

        Assert.Equal(xml, BlockXmlSanitizer.RemoveDocumentInfo(xml));
    }

    [Fact]
    public void Removal_is_idempotent()
    {
        var once = BlockXmlSanitizer.RemoveDocumentInfo(WithDocumentInfo);

        Assert.Equal(once, BlockXmlSanitizer.RemoveDocumentInfo(once));
    }
}
```

- [ ] **Step 2: Link the new file and run the tests to verify they fail**

Add to `TiaMcpServer.Tests.csproj`:

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\BlockXmlSanitizer.cs" Link="Linked\Openness\BlockXmlSanitizer.cs" />
```

Run: `dotnet test TiaMcpServer.Tests --filter BlockXmlSanitizerTests`
Expected: FAIL — `BlockXmlSanitizer` does not exist.

- [ ] **Step 3: Create `BlockXmlSanitizer.cs`**

```csharp
using System.Text.RegularExpressions;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Removes non-deterministic content from exported Simatic ML so get_block_content is stable
/// across calls. The DocumentInfo element carries a Created timestamp that changes on every
/// export; leaving it in makes the write-safety state hash non-deterministic and every
/// preview -> apply pair fails with "current state no longer matches" (see commit c53e6f4).
///
/// The removal is textual on purpose. An XDocument round trip would also drop the XML
/// declaration and re-indent the document, and this text is handed to Blocks.Import by
/// update_block_logic — it must stay byte-faithful everywhere except the removed element.
/// </summary>
internal static class BlockXmlSanitizer
{
    private static readonly Regex DocumentInfoElement = new Regex(
        @"[ \t]*<DocumentInfo(?:\s[^>]*)?(?:/>|>.*?</DocumentInfo>)\r?\n?",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public static string RemoveDocumentInfo(string xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return xml;
        }

        return DocumentInfoElement.Replace(xml, string.Empty);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter BlockXmlSanitizerTests`
Expected: PASS, 6 tests.

- [ ] **Step 5: Remove `StripNonDeterministic`**

In `BlockExporter.cs`, delete the `StripNonDeterministic` method (lines 93-114) and its XML doc comment, and remove `using System.Xml.Linq;`.

Its only caller is line 64, `combined.Append(StripNonDeterministic(File.ReadAllText(xmlPath)));`. Change that call to:

```csharp
                combined.Append(BlockXmlSanitizer.RemoveDocumentInfo(File.ReadAllText(xmlPath)));
```

Task 1 later replaces this whole block with the `Compose`-based version, which calls `BlockXmlSanitizer.RemoveDocumentInfo` directly. Making the swap here keeps the tree compiling between the two tasks.

- [ ] **Step 6: Build, test, commit**

Run: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`
Run: `dotnet test TiaMcpServer.Tests`
Expected: all pass.

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockXmlSanitizer.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs \
        TiaMcpServer.Tests/BlockXmlSanitizerTests.cs \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "fix: strip DocumentInfo without reserializing exported block XML"
```

---

### Task 5: GlobalDB creation

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs:109-132`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs:40-47`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockWritePreflight.cs:58`
- Test: `TiaMcpServer.Tests/BlockSourceGeneratorTests.cs`, `TiaMcpServer.Tests/BlockWritePreflightTests.cs`

**Interfaces:**
- Consumes: `BlockSourceGenerator.Generate(string blockName, string blockType, string language, string? obEventClass)`, `BlockWritePreflight.PrepareCreate(string blockPath, string blockType, string? language)`.
- Produces: no signature changes. `PrepareCreate` now defaults `GLOBALDB` to language `"DB"` instead of `"LAD"`.

**Context:** today no input works. Omitting `language` defaults to `"LAD"`, passes validation, and then Siemens rejects the generated XML with *"The argument 'ProgrammingLanguage' is missing."* because `GenerateGlobalDbXml` emits no such element. Passing `language: "DB"` is rejected by `ValidateTypeLanguage` before generation. Ground truth from `priv/tia_exports/InputValues_DB.xml`: `<ProgrammingLanguage>DB</ProgrammingLanguage>` and `<MemoryLayout>Optimized</MemoryLayout>` (there is no `<Optimized>` element), and no `Header*` elements.

- [ ] **Step 1: Write the failing tests**

Append to `TiaMcpServer.Tests/BlockSourceGeneratorTests.cs`:

```csharp
    [Fact]
    public void GlobalDb_source_declares_the_DB_programming_language()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null);

        Assert.Contains("<ProgrammingLanguage>DB</ProgrammingLanguage>", xml);
    }

    [Fact]
    public void GlobalDb_source_uses_MemoryLayout_not_an_Optimized_element()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null);

        Assert.Contains("<MemoryLayout>Optimized</MemoryLayout>", xml);
        Assert.DoesNotContain("<Optimized>", xml);
    }

    [Fact]
    public void GlobalDb_source_omits_header_attributes()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null);

        Assert.DoesNotContain("HeaderAuthor", xml);
        Assert.DoesNotContain("HeaderVersion", xml);
    }
```

Append to `TiaMcpServer.Tests/BlockWritePreflightTests.cs`:

```csharp
    [Fact]
    public void GlobalDb_defaults_to_the_DB_language_when_none_is_supplied()
    {
        var preflight = BlockWritePreflight.PrepareCreate("PLC_1/Blocks/MyDb", "GlobalDB", language: null);

        Assert.Equal("GLOBALDB", preflight.BlockType);
        Assert.Equal("DB", preflight.Language);
    }

    [Fact]
    public void GlobalDb_accepts_an_explicit_DB_language()
    {
        var preflight = BlockWritePreflight.PrepareCreate("PLC_1/Blocks/MyDb", "GlobalDB", language: "DB");

        Assert.Equal("DB", preflight.Language);
    }

    [Fact]
    public void GlobalDb_rejects_a_ladder_language()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockWritePreflight.PrepareCreate("PLC_1/Blocks/MyDb", "GlobalDB", language: "LAD"));

        Assert.Contains("GLOBALDB", exception.Message);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "GlobalDb"`
Expected: FAIL — the generator tests fail on missing `ProgrammingLanguage` / present `<Optimized>`; the preflight tests fail because `"DB"` is rejected and the default is `"LAD"`.

- [ ] **Step 3: Fix the GlobalDB template**

In `BlockSourceGenerator.cs`, replace `GenerateGlobalDbXml` (lines 109-132):

```csharp
    private static string GenerateGlobalDbXml(string blockName)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{EngineeringVersion}"" />
  <SW.Blocks.GlobalDB ID=""0"">
    <AttributeList>
      <AutoNumber>true</AutoNumber>
      <Interface><Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5""><Section Name=""Static"" /></Sections></Interface>
      <MemoryLayout>Optimized</MemoryLayout>
      <Name>{blockName}</Name>
      <Namespace></Namespace>
      <ProgrammingLanguage>DB</ProgrammingLanguage>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.GlobalDB>
</Document>";
    }
```

- [ ] **Step 4: Fix the language rule**

In `BlockSourceValidator.cs`, replace the `GLOBALDB` branch of `ValidateTypeLanguage` (lines 40-47):

```csharp
        if (blockType is "GLOBALDB" or "DB")
        {
            if (language != "DB")
            {
                throw ValidationFailure(
                    $"Block type '{blockType}' uses language 'DB'; '{language}' is not supported. "
                    + "Omit the language parameter for data blocks.");
            }

            return;
        }
```

- [ ] **Step 5: Make the default type-aware**

In `BlockWritePreflight.cs`, replace line 58:

```csharp
        var normalizedLanguage = (language ?? DefaultLanguageFor(normalizedType)).ToUpperInvariant();
```

and add the helper to the same class:

```csharp
    private static string DefaultLanguageFor(string normalizedBlockType)
    {
        return normalizedBlockType is "GLOBALDB" or "DB" ? "DB" : "LAD";
    }
```

Note `normalizedType` is assigned on line 57, before this line — no reordering needed.

- [ ] **Step 6: Run tests**

Run: `dotnet test TiaMcpServer.Tests`
Expected: all pass. If an existing test asserted `GLOBALDB` + `LAD` was valid, it encoded the bug — update it to the new rule.

- [ ] **Step 7: Update the tool documentation**

In `TiaMcpServer/Batch/BatchOperationRequest.cs`, the `Language` description currently reads *"Programming language for create_block (FB/FC/OB only). Valid values: LAD, FBD, STL, SCL, GRAPH. Defaults to LAD."* Replace with:

```csharp
    [Description("Programming language for create_block (FB/FC/OB only). Valid values: LAD, FBD, STL, SCL, GRAPH. Defaults to LAD. Omit for blockType=GlobalDB, which always uses DB.")]
```

- [ ] **Step 8: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockWritePreflight.cs \
        TiaMcpServer/Batch/BatchOperationRequest.cs \
        TiaMcpServer.Tests/BlockSourceGeneratorTests.cs \
        TiaMcpServer.Tests/BlockWritePreflightTests.cs
git commit -m "fix: make GlobalDB creation reachable"
```

---

### Task 6: SCL and STL compile units

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs:134-176` and `:48, :74, :102`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs:32-35, 61-72`
- Test: `TiaMcpServer.Tests/BlockSourceGeneratorTests.cs`, `TiaMcpServer.Tests/BlockSourceValidatorTests.cs`

**Interfaces:**
- Consumes: `BlockSourceGenerator.Generate(...)`, `BlockSourceValidator.Validate(string blockType, string language, string xml)`.
- Produces: no signature changes. `BlockSourceValidator.HasSclSourceBody` is replaced by `HasCompileUnitFor(XDocument, string language)` (private) and a new private `ContainsRawStructuredTextNode(XDocument)`.

**Context:** the generator puts a raw text node inside `<StructuredText>`, which the schema forbids — permitted children are `Access, Token, Parameter, Text, Comment, LineComment, Blank, NewLine`. `HasSclSourceBody` *requires* that raw text, so generator and validator must change together. The fix uses an empty `<NetworkSource />`, proven valid by five occurrences in the real `Inputs_FB.xml` export, which also sidesteps the stale `StructuredText/v3` namespace (V21 uses `v4`). ID normalization is included as hygiene — `create_block` for LAD already works while emitting `1, 3, 2`, so ID ordering is not the blocker.

**This task's central hypothesis — that Siemens accepts an SCL compile unit with an empty `NetworkSource` — can only be confirmed by Task 7 against live TIA Portal.** If Task 7 rejects it, the fallback is a minimal token stream copied from `priv/tia_exports/nStageHeater.xml` using the `v4` namespace, with a `UId` on every element.

- [ ] **Step 1: Write the failing tests**

Append to `TiaMcpServer.Tests/BlockSourceGeneratorTests.cs`:

```csharp
    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    [InlineData("OB")]
    public void Scl_source_contains_a_compile_unit_with_an_empty_network_source(string blockType)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", blockType, "SCL", obEventClass: null);

        Assert.Contains("<SW.Blocks.CompileUnit", xml);
        Assert.Contains("<NetworkSource />", xml);
        Assert.Contains("<ProgrammingLanguage>SCL</ProgrammingLanguage>", xml);
    }

    [Theory]
    [InlineData("SCL")]
    [InlineData("STL")]
    public void Generated_sources_never_emit_a_StructuredText_element(string language)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", language, obEventClass: null);

        Assert.DoesNotContain("StructuredText", xml);
        Assert.DoesNotContain("StructuredText/v3", xml);
    }

    [Fact]
    public void Object_ids_increase_monotonically_in_document_order()
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", "SCL", obEventClass: null);

        var ids = System.Text.RegularExpressions.Regex.Matches(xml, "ID=\"(\\d+)\"");
        var previous = -1;
        foreach (System.Text.RegularExpressions.Match match in ids)
        {
            var current = int.Parse(match.Groups[1].Value);
            Assert.True(current > previous, $"ID {current} follows {previous} in document order.");
            previous = current;
        }
    }
```

Append to `TiaMcpServer.Tests/BlockSourceValidatorTests.cs`:

```csharp
    [Fact]
    public void Validation_accepts_an_scl_compile_unit_with_an_empty_network_source()
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", "SCL", obEventClass: null);

        BlockSourceValidator.Validate("FB", "SCL", xml);
    }

    [Fact]
    public void Validation_rejects_a_raw_text_node_inside_StructuredText()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <SW.Blocks.FB ID=""0"">
    <ObjectList>
      <SW.Blocks.CompileUnit ID=""1"">
        <AttributeList>
          <NetworkSource>
            <StructuredText xmlns=""http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4"">// raw</StructuredText>
          </NetworkSource>
          <ProgrammingLanguage>SCL</ProgrammingLanguage>
        </AttributeList>
      </SW.Blocks.CompileUnit>
    </ObjectList>
  </SW.Blocks.FB>
</Document>";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.Validate("FB", "SCL", xml));

        Assert.Contains("StructuredText", exception.Message);
    }

    [Fact]
    public void Validation_rejects_scl_source_with_no_compile_unit()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <SW.Blocks.FB ID=""0""><ObjectList /></SW.Blocks.FB>
</Document>";

        Assert.Throws<WorkerOperationException>(() => BlockSourceValidator.Validate("FB", "SCL", xml));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test TiaMcpServer.Tests --filter "Scl_source OR StructuredText OR monotonically OR compile_unit"`
Expected: FAIL — the generator still emits `StructuredText` with raw text, and the validator still demands it.

- [ ] **Step 3: Replace the compile-unit generator**

In `BlockSourceGenerator.cs`, replace both `GenerateCompileUnit` and `GenerateEmptyStlCompileUnit` (lines 134-176) with a single method:

```csharp
    /// <summary>
    /// Emits a compile unit for languages that require one. The NetworkSource is empty and
    /// self-closing: real V21 exports contain that exact shape (five occur in a single LAD FB
    /// export), and it avoids hand-authoring a StructuredText token stream, which is what made
    /// the previous SCL template schema-invalid.
    /// </summary>
    private static string GenerateCompileUnit(string language, int compileUnitId)
    {
        if (language is not ("SCL" or "STL"))
        {
            return string.Empty;
        }

        return $@"
      <SW.Blocks.CompileUnit ID=""{compileUnitId}"" CompositionName=""CompileUnits"">
        <AttributeList>
          <NetworkSource />
          <ProgrammingLanguage>{language}</ProgrammingLanguage>
        </AttributeList>
      </SW.Blocks.CompileUnit>";
    }
```

- [ ] **Step 4: Renumber IDs so they increase in document order**

In each of `GenerateFbXml`, `GenerateFcXml` and `GenerateObXml`, replace the `<ObjectList>` block so Comment is `1`, the compile unit is `2`, and Title is `3`. For `GenerateFbXml` (line 47-50):

```csharp
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />{GenerateCompileUnit(language, compileUnitId: 2)}
      <MultilingualText ID=""3"" CompositionName=""Title"" />
    </ObjectList>
```

Apply the identical change at `GenerateFcXml` (lines 73-76) and `GenerateObXml` (lines 101-104). The `includeStl` argument disappears — `GenerateCompileUnit` now decides from the language alone, so FC and OB gain STL compile units too, which they previously lacked.

- [ ] **Step 5: Invert the validator rule**

In `BlockSourceValidator.cs`, replace lines 32-35:

```csharp
        if (ContainsRawStructuredTextNode(document))
        {
            throw ValidationFailure(
                "Generated source places raw text inside a StructuredText element, which the "
                + "Simatic ML schema forbids.");
        }

        if (language is "SCL" or "STL" && !HasCompileUnitFor(document, language))
        {
            throw ValidationFailure(
                $"Generated {blockType} {language} source must contain a compile unit.");
        }
```

and replace `HasSclSourceBody` (lines 61-72) with:

```csharp
    private static bool HasCompileUnitFor(XDocument document, string language)
    {
        return document.Descendants("SW.Blocks.CompileUnit")
            .Select(compileUnit => compileUnit.Element("AttributeList"))
            .Where(attributeList => attributeList is not null)
            .Any(attributeList => string.Equals(
                attributeList!.Element("ProgrammingLanguage")?.Value,
                language,
                StringComparison.Ordinal));
    }

    private static bool ContainsRawStructuredTextNode(XDocument document)
    {
        return document.Descendants()
            .Where(element => element.Name.LocalName == "StructuredText")
            .Any(element => element.Nodes().OfType<XText>().Any(
                text => !string.IsNullOrWhiteSpace(text.Value)));
    }
```

Add `using System.Xml.Linq;` if not already present (it is, at line 3) and confirm `System` and `System.Linq` are imported (lines 1-2).

- [ ] **Step 6: Run tests**

Run: `dotnet test TiaMcpServer.Tests`
Expected: all pass. `BlockSourceGeneratorTests` may contain an existing assertion that SCL output contains `// Generated SCL source` — that assertion encoded the bug; delete it.

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockSourceGenerator.cs \
        TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs \
        TiaMcpServer.Tests/BlockSourceGeneratorTests.cs \
        TiaMcpServer.Tests/BlockSourceValidatorTests.cs
git commit -m "fix: emit schema-valid SCL and STL compile units"
```

---

### Task 7: Live end-to-end verification

**Files:**
- Modify: `README.md` "Known Issues" section (lines ~448-455)
- Create: `priv/MCP_TOOL_TEST_REPORT_ROUND3.md`

**Interfaces:** none — this task runs the MCP tools against live TIA Portal and records results.

**Requires:** TIA Portal V21 running with `SimpleProject.ap21` open. This task cannot be completed by CI or by an agent without a live Openness connection.

- [ ] **Step 1: Build and install the worker**

Run: `dotnet build TiaMcpServer.sln -m:1`
Expected: Build succeeded, worker copied to `openness-worker/`.

- [ ] **Step 2: Verify the SCL hypothesis first**

It gates whether Task 6 stands. Via `preview_write_batch` then `apply_write_batch`:

```
create_block(blockPath: "PLC_1/Blocks/MCP_R3_SCL", blockType: "FC", language: "SCL")
```

Expected: success. If it fails with a schema or compile-unit error, **stop and revisit Task 6** using the fallback described there (a minimal `v4` token stream from `nStageHeater.xml`).

- [ ] **Step 3: Verify GlobalDB**

```
create_block(blockPath: "PLC_1/Blocks/MCP_R3_DB", blockType: "GlobalDB")
```

Expected: success with no `language` supplied. Then confirm that supplying `language: "LAD"` is rejected with the new message.

- [ ] **Step 4: Verify the round trip that started all of this**

```
get_block_content(blockPath: "PLC_1/Blocks/MCP_R3_SCL")
update_block_logic(blockPath: "PLC_1/Blocks/MCP_R3_SCL", yamlContent: <that exact output, unmodified>)
```

Expected: success. This is the case that failed in both prior test rounds.

- [ ] **Step 5: Verify the non-authoritative guard**

Repeat step 4 but edit one character inside the `.s7dcl` document.
Expected: rejected with a message naming `.s7dcl` and pointing at the `.xml` document. It must NOT silently succeed.

- [ ] **Step 6: Verify a real edit applies**

Repeat step 4 but change the block's title text inside the `.xml` document.
Expected: success; a follow-up `get_block_content` shows the new title; `compile_check` reports 0 errors.

- [ ] **Step 7: Clean up and record**

Delete `MCP_R3_SCL` and `MCP_R3_DB` via `delete_block`. Run `compile_check` and confirm 0 errors / 0 warnings. Write `priv/MCP_TOOL_TEST_REPORT_ROUND3.md` in the same shape as the round-2 report, and remove the now-fixed entries from the README "Known Issues" section.

- [ ] **Step 8: Re-capture the bundle fixture**

The exporter output changed in Tasks 1 and 4 (the XML declaration now survives). Capture a fresh `get_block_content` for `MCP_Test_CPU/Blocks/Main` and save it as `TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.v2.bundle.txt`, then add a test asserting it parses into 2 documents. **Keep the original fixture and its test** — it pins that historical output still parses, which is the D1 regression guard.

- [ ] **Step 9: Commit**

```bash
git add README.md priv/MCP_TOOL_TEST_REPORT_ROUND3.md \
        TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.v2.bundle.txt \
        TiaMcpServer.Tests/BlockBundleFormatTests.cs
git commit -m "test: verify block write repairs against live TIA Portal V21"
```

---

## Deferred / out of scope

- **Option 3 for `StripNonDeterministic`** (normalize at hash time, keep the payload byte-exact) is the cleaner long-term shape but is not needed once Task 4 lands. Revisit if this area is touched again.
- **`delete_network_device`** does not exist, so `MCP_Test_Device` and `MCP_Test_CPU` can only be removed from the test project via the TIA UI. Separate decision.
- **Golden fixtures for FC, OB, FBD, GRAPH** — no reference exports captured yet. Task 6's tests assert structure rather than byte equality for those.
- **The `(unavailable)` export path** now yields a bundle with no `.xml` document instead of a placeholder document. The read still succeeds and the write fails with an actionable message, but the read gives no warning explaining the missing document. Plumbing warnings out of `BlockExporter.Export` is deferred.
