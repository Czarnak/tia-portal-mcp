# SCL External-Source Support Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the existing `format=source` read/write path from global data blocks to SCL-language FB/FC/OB, and add a caller-selected `withDependencies` read option, so an LLM client can round-trip a function block as plain SCL instead of ~19x larger SimaticML XML.

**Architecture:** No new tools and no new pipeline. `BlockExporter.ExportSource` and `BlockImporter.ImportSource` already drive Siemens' external-source API; this plan replaces their hardcoded "global DB only" gate with a language-aware eligibility decision, replaces the first-match declared-name regex with a scanner that enforces exactly one declared object, and threads a `withDependencies` flag through to `GenerateOptions`. Every component splits into a Siemens-free half that is unit-tested and a thin `Siemens.Engineering`-calling shell covered only by a committed live-test harness.

**Tech Stack:** C# — `netstandard2.0` (Contracts), `net48` (worker, Siemens Openness V21), `net8.0` (host + xunit tests). PowerShell 7 for the live harness.

**Spec:** `docs/superpowers/specs/2026-07-27-scl-external-source-design.md`

## Global Constraints

- **Build serially.** `dotnet build TiaMcpServer.sln -m:1` — `-m:1` is required to avoid parallel worker-build conflicts.
- **Use PowerShell, not Bash, for any `dotnet` command carrying a `/p:` MSBuild flag.** Bash mangles `/p:` on this machine.
- **CI build must stay green without TIA Portal installed:** `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`.
- **Siemens DLLs are never committed** to the repo or the NuGet package.
- **Every new worker file that is free of `Siemens.Engineering` types MUST be added to `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj` as a `<Compile Include>` link.** Files that touch Siemens types must NOT be linked — the test project has no Siemens reference and will fail to compile.
- **Tool count stays at 10.** No new `[McpServerTool]` methods.
- **No default changes.** `format` omitted still means `xml` for blocks and `source` for types. `withDependencies` omitted means `false`. `get_block_content` and `update_block_logic` must return byte-identical results to today when neither field is supplied.
- **Commit format:** conventional commits — `<type>: <description>` where type is one of feat, fix, refactor, docs, test, chore, perf, ci.
- **Coverage floor:** 80% over the test project's compiled sources.
- **Branch:** `feature/scl-external-source` (already created; the spec commit is its first commit).

## Where this plan gives instructions instead of literal code, and why

Task 1 is a spike and has no production code by design — its steps are "run this, record the answer."

Tasks 7 and 8 modify `Siemens.Engineering`-calling files. Their code is complete, but two call shapes are unverified until Task 1 runs on a real installation: the three-argument `GenerateSource` overload, and whether `PlcBlock` satisfies `IGenerateSource` without a cast. Each step says so at the point of use. If a call does not compile, fix the call — do not redesign around it.

Task 9 specifies live-test cases as assertions rather than literal PowerShell. `scripts/live-test-db.ps1` is 763 lines of session setup, project lifecycle, per-case reporting, and teardown whose exact helper shape must be mirrored, not reinvented — and this plan's author did not read all 763. Inventing several hundred lines of PowerShell that looks authoritative but does not match the existing harness would be worse than telling you to read it first. The steps say what each case must assert; the harness mechanics come from the neighboring script.

## File Structure

**Created — `TiaMcpServer.OpennessWorker/Openness` (net48), Siemens-free, linked into tests:**

| File | Responsibility |
|---|---|
| `SourceDeclarationScanner.cs` | Find every object a source declares, ignoring comments and string literals. |
| `SourceFormatEligibility.cs` | Decide whether `format=source` applies to a block, and with which extension and expected declaration kind. |
| `SourceReadWarnings.cs` | Build the "this document is context only" warning for a `withDependencies` read. |

**Modified — Siemens-free, already linked into tests:**

| File | Change |
|---|---|
| `PlcTypeSourcePreflight.cs` | Delegate to the scanner; enforce exactly one declaration of the expected kind. |

**Modified — Siemens-touching, NOT linked into tests:**

| File | Change |
|---|---|
| `BlockExporter.cs` | Replace `RequireGlobalDb` with the eligibility decision; honor `withDependencies`. |
| `BlockImporter.cs` | Same gate; extension and expected kind from the decision. |
| `PlcTypeExporter.cs` | Pass `GenerateOptions` explicitly; honor `withDependencies`. |
| `PlcTypeImporter.cs` | Pass the expected declaration kind to the preflight. |
| `Program.cs` | Read `WithDependencies`; attach the read warning. |

**Modified — host:**

| File | Change |
|---|---|
| `TiaMcpServer.Contracts/WorkerRequest.cs` | Add `WithDependencies`. |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | Add `WithDependencies`. |
| `TiaMcpServer/Batch/BatchOperationCatalog.cs` | Add `withDependencies` to two read specs. |
| `TiaMcpServer/Batch/BatchWorkerInvoker.cs` | Forward `WithDependencies` on the two read arms. |
| `TiaMcpServer/Batch/BatchSafetySnapshot.cs` | Preview text distinguishes a `format=source` write. |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | Add the parameter to two methods. |

**Created — scripts and tests:**

| File | Responsibility |
|---|---|
| `scripts/live-test-scl.ps1` | Phase 3 live gate. |
| `TiaMcpServer.Tests/SourceDeclarationScannerTests.cs` | |
| `TiaMcpServer.Tests/SourceFormatEligibilityTests.cs` | |
| `TiaMcpServer.Tests/SourceReadWarningsTests.cs` | |
| `TiaMcpServer.Tests/Fixtures/DamperDigital.scl` | Real V21 export — 1 object. |
| `TiaMcpServer.Tests/Fixtures/AnalogInput.scl` | Real V21 export — 2 objects. |
| `TiaMcpServer.Tests/Fixtures/DamperAnalog.scl` | Real V21 export — 4 objects. |
| `TiaMcpServer.Tests/Fixtures/nStageHeater.scl` | Real V21 export — nested anonymous STRUCT. |

---

## Task 1: Live spike — measure the SCL round trip

Throwaway. Produces no production code. Its output is a findings block appended to the spec, which every later task depends on for its assumptions.

That `GenerateBlocksFromSource` updates an existing FB rather than refusing it is already confirmed by the project owner and is not under test here — the spike exercises it incidentally.

**Files:**
- Create (temporary, deleted in Step 6): `scripts/spike-scl.ps1`
- Modify: `docs/superpowers/specs/2026-07-27-scl-external-source-design.md`

**Interfaces:**
- Consumes: nothing.
- Produces: a "Spike findings (2026-07-27)" section in the spec, answering questions A–G. Tasks 7 and 8 read it before touching `GenerateSource`.

**Prerequisites:** TIA Portal V21 installed with Openness enabled, and a scratch project containing at least one SCL function block with a UDT-typed parameter, plus one SCL block inside a software unit. `scripts/live-test-db.ps1` is the reference for session setup, project opening, and teardown — read it before writing anything.

- [ ] **Step 1: Read the reference harness**

Read `scripts/live-test-db.ps1` end to end. It already solves: locating the Openness assemblies, attaching to or starting a `TiaPortal` instance, opening a project by path, resolving a `PlcSoftware`, and closing down without leaving a portal process alive. Reuse its structure verbatim — the spike is not the place to invent a new harness.

- [ ] **Step 2: Write the spike script**

Create `scripts/spike-scl.ps1`. It must perform, in order, printing a clearly labelled answer for each:

1. **Question C — the two-argument default.** Export one SCL FB that has a UDT-typed parameter using `GenerateSource($objects, $fileInfo)` (two arguments, no options). Print the number of `TYPE` / `DATA_BLOCK` / `FUNCTION_BLOCK` declarations in the resulting file. One declaration means the default is `None`; more means the default is `WithDependencies`.
2. **Question G — what an FB export contains.** Print the first 40 lines of that file. Record whether `VERSION`, an attribute block (`{ S7_Optimized_Access := ... }`), `REGION` markers, and `//` comments survive.
3. Repeat the export twice more with `GenerateOptions::None` and `GenerateOptions::WithDependencies` explicitly. Print the declaration count for each. Confirm they differ.
4. **Question D — generated count.** Take the `GenerateOptions::None` export, register it via `CreateFromFile`, call `GenerateBlocksFromSource($userGroup, [GenerateBlockOption]::None)`, and print `$generated.Count`.
5. **Question A — instance DBs.** Before the import, record the names of the FB's instance DBs. Edit the exported source to add one new `VAR` member, import it, then re-list the instance DBs and print whether each still exists. Compile the PLC and print the error and warning counts.
6. **Question B — attribute preservation.** Before and after that import, print the block's `Number`, `AutoNumber`, `HeaderAuthor`, `HeaderFamily`, `HeaderVersion`, and `IsKnowHowProtected`. Print a per-attribute changed/unchanged verdict.
7. **Question E — byte fidelity.** Export the block again with `GenerateOptions::None`, and print whether the bytes equal the pre-edit export with the same one-line edit applied.
8. **Question F — software unit scope.** Repeat items 4 and 5 for an SCL block inside a software unit, resolving the external source group from the unit (`$unit.ExternalSourceGroup`) rather than the PLC. Print success or the exception.
9. Delete every `PlcExternalSource` node the script created, and print whether any survived.

- [ ] **Step 3: Run the spike**

Run: `pwsh -NoProfile -File scripts/spike-scl.ps1 -ProjectPath "<path to scratch project>"`
Expected: every labelled question prints an answer. A thrown exception is itself a finding — record it rather than working around it.

- [ ] **Step 4: Record the findings in the spec**

In `docs/superpowers/specs/2026-07-27-scl-external-source-design.md`, immediately after the `### Task 1 — throwaway spike (closes Phase 0 for SCL)` heading's question table, add:

```markdown
#### Spike findings (2026-07-27)

| | Question | Answer |
| --- | --- | --- |
| A | Instance DBs survive an interface-changing update? | <answer> |
| B | Block number / auto-number / header / know-how preserved? | <answer> |
| C | 2-arg `GenerateSource` default | <answer> |
| D | `generated.Count` for a single-object `.scl` | <answer> |
| E | Re-export byte-identical | <answer> |
| F | Software-unit scope resolves | <answer> |
| G | What an FB export emits | <answer> |
```

Replace every `<answer>` with the measured result. Do not leave a placeholder — an unanswerable question is recorded as "not answerable: <reason>".

- [ ] **Step 5: Act on a surprising answer**

If **A** shows instance DBs are dropped or the PLC stops compiling, stop and report it before continuing — that is a user-facing hazard the design does not yet cover, and it needs a warning or a documented precondition added to the spec first.

If **B** shows attributes are reset, add a bullet to the spec's "Known adjacent gap" section naming the attributes affected.

If **C** shows the default is `WithDependencies`, add a bullet to the spec's "Roadmap corrections to apply" section: shipped Phase 1/2 exports have been emitting dependency closures, and Task 7 fixes it.

If **D** is not 1, stop — `BlockSourceWriteWarnings` assumes 1 and the strict-write design depends on it.

- [ ] **Step 6: Delete the spike and commit the findings**

```bash
rm scripts/spike-scl.ps1
git add docs/superpowers/specs/2026-07-27-scl-external-source-design.md
git commit -m "docs: record SCL external-source spike findings"
```

---

## Task 2: `SourceDeclarationScanner`

Finds every object a source declares. This replaces `PlcTypeSourcePreflight`'s first-match regex, which structurally cannot answer "how many objects does this declare?" — the question the strict write rule is built on.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/SourceDeclarationScanner.cs`
- Create: `TiaMcpServer.Tests/SourceDeclarationScannerTests.cs`
- Create: `TiaMcpServer.Tests/Fixtures/DamperDigital.scl`, `AnalogInput.scl`, `DamperAnalog.scl`, `nStageHeater.scl`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: `TiaMcpServer.OpennessWorker.Openness.SourceObjectKind` (enum: `Type`, `DataBlock`, `FunctionBlock`, `Function`, `OrganizationBlock`), `SourceDeclaration` (`Kind`, `Name`, `LineNumber`, `Describe()`, `static KeywordFor(SourceObjectKind)`), and `SourceDeclarationScanner` with `static IReadOnlyList<SourceDeclaration> Scan(string content)` and `static string Describe(IReadOnlyList<SourceDeclaration>)`.

- [ ] **Step 1: Copy the fixtures**

```bash
cp priv/tia_exports/DamperDigital.scl TiaMcpServer.Tests/Fixtures/DamperDigital.scl
cp priv/tia_exports/AnalogInput.scl TiaMcpServer.Tests/Fixtures/AnalogInput.scl
cp priv/tia_exports/DamperAnalog.scl TiaMcpServer.Tests/Fixtures/DamperAnalog.scl
cp priv/tia_exports/nStageHeater.scl TiaMcpServer.Tests/Fixtures/nStageHeater.scl
```

- [ ] **Step 2: Register the fixtures and the new source file in the test project**

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, next to the existing `<None Include="Fixtures\Simulation_DB.db" ... />` entry (line 121), add:

```xml
    <None Include="Fixtures\DamperDigital.scl" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Fixtures\AnalogInput.scl" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Fixtures\DamperAnalog.scl" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Fixtures\nStageHeater.scl" CopyToOutputDirectory="PreserveNewest" />
```

And next to the existing `PlcTypeSourcePreflight.cs` link (line 124), add:

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\SourceDeclarationScanner.cs"
      Link="Linked\Openness\SourceDeclarationScanner.cs" />
```

- [ ] **Step 3: Write the failing test**

Create `TiaMcpServer.Tests/SourceDeclarationScannerTests.cs`:

```csharp
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests;

public class SourceDeclarationScannerTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Single_object_scl_export_yields_one_function_block()
    {
        var declarations = SourceDeclarationScanner.Scan(Fixture("DamperDigital.scl"));

        var declaration = Assert.Single(declarations);
        Assert.Equal(SourceObjectKind.FunctionBlock, declaration.Kind);
        Assert.Equal("DamperDigital", declaration.Name);
        Assert.Equal(1, declaration.LineNumber);
    }

    [Fact]
    public void Two_object_scl_export_yields_the_type_then_the_function_block()
    {
        var declarations = SourceDeclarationScanner.Scan(Fixture("AnalogInput.scl"));

        Assert.Equal(2, declarations.Count);
        Assert.Equal(SourceObjectKind.Type, declarations[0].Kind);
        Assert.Equal("AnalogInputSettings", declarations[0].Name);
        Assert.Equal(SourceObjectKind.FunctionBlock, declarations[1].Kind);
        Assert.Equal("AnalogInput", declarations[1].Name);
    }

    [Fact]
    public void Four_object_scl_export_yields_every_object_in_file_order()
    {
        var declarations = SourceDeclarationScanner.Scan(Fixture("DamperAnalog.scl"));

        Assert.Equal(4, declarations.Count);
        Assert.Equal(
            new[] { "AnalogInputSettings", "UDT_Settings", "HMI_Settings_DB", "DamperAnalog" },
            declarations.Select(d => d.Name).ToArray());
        Assert.Equal(
            new[]
            {
                SourceObjectKind.Type,
                SourceObjectKind.Type,
                SourceObjectKind.DataBlock,
                SourceObjectKind.FunctionBlock,
            },
            declarations.Select(d => d.Kind).ToArray());
    }

    [Fact]
    public void A_nested_anonymous_struct_is_not_mistaken_for_a_declaration()
    {
        var declarations = SourceDeclarationScanner.Scan(Fixture("nStageHeater.scl"));

        var declaration = Assert.Single(declarations);
        Assert.Equal("nStageHeater", declaration.Name);
    }

    [Fact]
    public void A_member_named_Type_is_not_a_declaration()
    {
        // DamperDigital's first VAR_INPUT member is literally named "Type".
        var declarations = SourceDeclarationScanner.Scan(Fixture("DamperDigital.scl"));

        Assert.DoesNotContain(declarations, d => d.Kind == SourceObjectKind.Type);
    }

    [Fact]
    public void Real_V21_udt_export_yields_one_type()
    {
        var declarations = SourceDeclarationScanner.Scan(Fixture("AnalogInputSettings.udt"));

        var declaration = Assert.Single(declarations);
        Assert.Equal(SourceObjectKind.Type, declaration.Kind);
        Assert.Equal("AnalogInputSettings", declaration.Name);
    }

    [Fact]
    public void Real_V21_db_export_yields_one_data_block()
    {
        var declarations = SourceDeclarationScanner.Scan(Fixture("Simulation_DB.db"));

        var declaration = Assert.Single(declarations);
        Assert.Equal(SourceObjectKind.DataBlock, declaration.Kind);
        Assert.Equal("Simulation_DB", declaration.Name);
    }

    [Fact]
    public void A_declaration_inside_a_line_comment_is_ignored()
    {
        var declarations = SourceDeclarationScanner.Scan(
            "// TYPE \"Ghost\"\r\nTYPE \"Real\"\r\nEND_TYPE\r\n");

        var declaration = Assert.Single(declarations);
        Assert.Equal("Real", declaration.Name);
        Assert.Equal(2, declaration.LineNumber);
    }

    [Fact]
    public void A_declaration_inside_a_block_comment_is_ignored()
    {
        var declarations = SourceDeclarationScanner.Scan(
            "(*\r\nTYPE \"Ghost\"\r\n*)\r\nTYPE \"Real\"\r\nEND_TYPE\r\n");

        var declaration = Assert.Single(declarations);
        Assert.Equal("Real", declaration.Name);
        Assert.Equal(4, declaration.LineNumber);
    }

    [Fact]
    public void A_declaration_inside_a_string_literal_is_ignored()
    {
        var declarations = SourceDeclarationScanner.Scan(
            "FUNCTION_BLOCK \"Real\"\r\nBEGIN\r\n#msg := '\r\nTYPE \"Ghost\"\r\n';\r\nEND_FUNCTION_BLOCK\r\n");

        var declaration = Assert.Single(declarations);
        Assert.Equal("Real", declaration.Name);
    }

    [Fact]
    public void An_END_keyword_is_not_a_declaration()
    {
        var declarations = SourceDeclarationScanner.Scan(
            "FUNCTION_BLOCK \"Real\"\r\nEND_FUNCTION_BLOCK\r\n");

        Assert.Single(declarations);
    }

    [Fact]
    public void An_unquoted_name_is_accepted()
    {
        var declarations = SourceDeclarationScanner.Scan("TYPE Foo\r\nEND_TYPE\r\n");

        Assert.Equal("Foo", Assert.Single(declarations).Name);
    }

    [Fact]
    public void A_leading_byte_order_mark_does_not_hide_the_first_declaration()
    {
        var declarations = SourceDeclarationScanner.Scan("\uFEFFTYPE \"Foo\"\r\nEND_TYPE\r\n");

        Assert.Equal("Foo", Assert.Single(declarations).Name);
    }

    [Fact]
    public void Functions_and_organization_blocks_are_recognized()
    {
        var declarations = SourceDeclarationScanner.Scan(
            "FUNCTION \"Calc\" : Real\r\nEND_FUNCTION\r\n"
            + "ORGANIZATION_BLOCK \"Main\"\r\nEND_ORGANIZATION_BLOCK\r\n");

        Assert.Equal(SourceObjectKind.Function, declarations[0].Kind);
        Assert.Equal("Calc", declarations[0].Name);
        Assert.Equal(SourceObjectKind.OrganizationBlock, declarations[1].Kind);
        Assert.Equal("Main", declarations[1].Name);
    }

    [Fact]
    public void Empty_content_yields_no_declarations()
    {
        Assert.Empty(SourceDeclarationScanner.Scan(string.Empty));
    }

    [Fact]
    public void Describe_lists_every_declaration_with_keyword_name_and_line()
    {
        var text = SourceDeclarationScanner.Describe(
            SourceDeclarationScanner.Scan(Fixture("AnalogInput.scl")));

        Assert.Contains("TYPE 'AnalogInputSettings' (line 1)", text);
        Assert.Contains("FUNCTION_BLOCK 'AnalogInput' (line 21)", text);
    }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceDeclarationScannerTests"`
Expected: FAIL — compile error, `SourceDeclarationScanner` does not exist.

- [ ] **Step 5: Write minimal implementation**

Create `TiaMcpServer.OpennessWorker/Openness/SourceDeclarationScanner.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>The object kinds a Siemens external-source file can declare.</summary>
internal enum SourceObjectKind
{
    Type,
    DataBlock,
    FunctionBlock,
    Function,
    OrganizationBlock,
}

/// <summary>One object declaration found in a source file.</summary>
internal sealed class SourceDeclaration
{
    public SourceDeclaration(SourceObjectKind kind, string name, int lineNumber)
    {
        Kind = kind;
        Name = name;
        LineNumber = lineNumber;
    }

    public SourceObjectKind Kind { get; }

    public string Name { get; }

    /// <summary>1-based, so it matches what an editor shows.</summary>
    public int LineNumber { get; }

    public string Describe() => $"{KeywordFor(Kind)} '{Name}' (line {LineNumber})";

    public static string KeywordFor(SourceObjectKind kind)
    {
        switch (kind)
        {
            case SourceObjectKind.Type: return "TYPE";
            case SourceObjectKind.DataBlock: return "DATA_BLOCK";
            case SourceObjectKind.FunctionBlock: return "FUNCTION_BLOCK";
            case SourceObjectKind.Function: return "FUNCTION";
            case SourceObjectKind.OrganizationBlock: return "ORGANIZATION_BLOCK";
            default: return kind.ToString();
        }
    }
}

/// <summary>
/// Lists every object a Siemens external-source document declares.
///
/// <para>
/// A single .scl file routinely declares several objects of different kinds — the real V21 export
/// DamperAnalog.scl declares two TYPEs, a DATA_BLOCK and a FUNCTION_BLOCK — because
/// GenerateSource emits a block's dependency closure when asked to. GenerateBlocksFromSource then
/// creates all of them, with no notion of which one the caller was addressing. Counting
/// declarations is therefore a safety primitive, not a convenience: it is what lets a write refuse
/// a document that would touch objects the caller never named.
/// </para>
/// <para>
/// Comments and string literals are masked before matching, so a keyword mentioned in prose is not
/// mistaken for a declaration. Double quotes are NOT masked — in SCL they delimit identifiers, and
/// the declared name itself is usually quoted.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class SourceDeclarationScanner
{
    private const char ByteOrderMark = '\uFEFF';

    // Anchored to the start of a line: END_TYPE, END_FUNCTION_BLOCK and friends therefore cannot
    // match, and neither can a member whose name happens to be Type. FUNCTION_BLOCK precedes
    // FUNCTION in the alternation so the longer keyword wins.
    private static readonly Regex DeclarationPattern = new Regex(
        @"^[ \t]*(?<keyword>ORGANIZATION_BLOCK|FUNCTION_BLOCK|DATA_BLOCK|FUNCTION|TYPE)[ \t]+"
        + @"(?:""(?<quoted>[^""\r\n]+)""|(?<bare>[A-Za-z_][A-Za-z0-9_]*))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

    public static IReadOnlyList<SourceDeclaration> Scan(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return Array.Empty<SourceDeclaration>();
        }

        var text = content[0] == ByteOrderMark ? content.Substring(1) : content;
        var masked = MaskCommentsAndStrings(text);
        var declarations = new List<SourceDeclaration>();

        foreach (Match match in DeclarationPattern.Matches(masked))
        {
            var quoted = match.Groups["quoted"];
            var name = quoted.Success ? quoted.Value : match.Groups["bare"].Value;

            declarations.Add(new SourceDeclaration(
                KindFor(match.Groups["keyword"].Value),
                name,
                LineNumberAt(masked, match.Index)));
        }

        return declarations;
    }

    public static string Describe(IReadOnlyList<SourceDeclaration> declarations)
    {
        var parts = new List<string>(declarations.Count);
        foreach (var declaration in declarations)
        {
            parts.Add(declaration.Describe());
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Replaces comment and string-literal characters with spaces, preserving length and line
    /// breaks so match offsets and line numbers stay accurate against the original text.
    /// </summary>
    private static string MaskCommentsAndStrings(string content)
    {
        var masked = new StringBuilder(content);
        var length = content.Length;
        var i = 0;

        while (i < length)
        {
            var current = content[i];

            if (current == '/' && i + 1 < length && content[i + 1] == '/')
            {
                while (i < length && content[i] != '\n')
                {
                    Blank(masked, i);
                    i++;
                }

                continue;
            }

            if (current == '(' && i + 1 < length && content[i + 1] == '*')
            {
                Blank(masked, i);
                Blank(masked, i + 1);
                i += 2;

                while (i < length && !(content[i] == '*' && i + 1 < length && content[i + 1] == ')'))
                {
                    Blank(masked, i);
                    i++;
                }

                if (i < length)
                {
                    Blank(masked, i);
                    if (i + 1 < length)
                    {
                        Blank(masked, i + 1);
                    }

                    i += 2;
                }

                continue;
            }

            if (current == '\'')
            {
                Blank(masked, i);
                i++;

                while (i < length && content[i] != '\'')
                {
                    // SCL escapes inside a string literal start with '$'; $' is a literal quote and
                    // must not end the string.
                    if (content[i] == '$' && i + 1 < length)
                    {
                        Blank(masked, i);
                        i++;
                    }

                    Blank(masked, i);
                    i++;
                }

                if (i < length)
                {
                    Blank(masked, i);
                    i++;
                }

                continue;
            }

            i++;
        }

        return masked.ToString();
    }

    private static void Blank(StringBuilder builder, int index)
    {
        if (builder[index] != '\n' && builder[index] != '\r')
        {
            builder[index] = ' ';
        }
    }

    private static int LineNumberAt(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    private static SourceObjectKind KindFor(string keyword)
    {
        if (keyword.Equals("TYPE", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.Type;
        if (keyword.Equals("DATA_BLOCK", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.DataBlock;
        if (keyword.Equals("FUNCTION_BLOCK", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.FunctionBlock;
        if (keyword.Equals("ORGANIZATION_BLOCK", StringComparison.OrdinalIgnoreCase)) return SourceObjectKind.OrganizationBlock;
        return SourceObjectKind.Function;
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceDeclarationScannerTests"`
Expected: PASS — 16 tests passed.

If `Describe_lists_every_declaration_with_keyword_name_and_line` fails on the line number, open the fixture and read the actual line — the fixture is ground truth, not the test:

```bash
grep -n "FUNCTION_BLOCK" TiaMcpServer.Tests/Fixtures/AnalogInput.scl
```

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/SourceDeclarationScanner.cs \
        TiaMcpServer.Tests/SourceDeclarationScannerTests.cs \
        TiaMcpServer.Tests/Fixtures/ \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: add SourceDeclarationScanner for multi-object source detection"
```

---

## Task 3: `SourceFormatEligibility`

Decides whether `format=source` applies to a block, and returns the file extension and the declaration kind a write must find. Replaces `BlockExporter.RequireGlobalDb`'s hardcoded rule.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/SourceFormatEligibility.cs`
- Create: `TiaMcpServer.Tests/SourceFormatEligibilityTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: `SourceObjectKind` (Task 2), `TiaMcpServer.Contracts.SourceFormatNames`.
- Produces: `SourceFormatDecision` (`IsAllowed`, `Extension`, `ExpectedKind`, `RefusalMessage`) and `SourceFormatEligibility.Decide(string kindName, string languageName, string displayPath)`. Tasks 7 and 8 call `Decide`.

- [ ] **Step 1: Register the new file in the test project**

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, next to the `SourceDeclarationScanner.cs` link added in Task 2, add:

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\SourceFormatEligibility.cs"
      Link="Linked\Openness\SourceFormatEligibility.cs" />
```

- [ ] **Step 2: Write the failing test**

Create `TiaMcpServer.Tests/SourceFormatEligibilityTests.cs`:

```csharp
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests;

public class SourceFormatEligibilityTests
{
    [Fact]
    public void A_global_data_block_is_allowed_with_the_db_extension()
    {
        var decision = SourceFormatEligibility.Decide("GlobalDB", "DB", "PLC_1/Blocks/Settings_DB");

        Assert.True(decision.IsAllowed);
        Assert.Equal(".db", decision.Extension);
        Assert.Equal(SourceObjectKind.DataBlock, decision.ExpectedKind);
        Assert.Null(decision.RefusalMessage);
    }

    [Theory]
    [InlineData("FB", SourceObjectKind.FunctionBlock)]
    [InlineData("FC", SourceObjectKind.Function)]
    [InlineData("OB", SourceObjectKind.OrganizationBlock)]
    public void An_SCL_code_block_is_allowed_with_the_scl_extension(string kindName, SourceObjectKind expectedKind)
    {
        var decision = SourceFormatEligibility.Decide(kindName, "SCL", "PLC_1/Blocks/Thing");

        Assert.True(decision.IsAllowed);
        Assert.Equal(".scl", decision.Extension);
        Assert.Equal(expectedKind, decision.ExpectedKind);
    }

    [Fact]
    public void The_language_name_is_matched_case_insensitively()
    {
        var decision = SourceFormatEligibility.Decide("FB", "scl", "PLC_1/Blocks/Thing");

        Assert.True(decision.IsAllowed);
    }

    [Fact]
    public void A_LAD_function_block_is_refused_and_the_message_names_the_language()
    {
        var decision = SourceFormatEligibility.Decide("FB", "LAD", "PLC_1/Blocks/Inputs_FB");

        Assert.False(decision.IsAllowed);
        Assert.Equal(string.Empty, decision.Extension);
        Assert.NotNull(decision.RefusalMessage);
        Assert.Contains("PLC_1/Blocks/Inputs_FB", decision.RefusalMessage);
        Assert.Contains("LAD", decision.RefusalMessage);
        Assert.Contains("format=xml", decision.RefusalMessage);
    }

    [Fact]
    public void A_GRAPH_function_block_is_refused()
    {
        var decision = SourceFormatEligibility.Decide("FB", "GRAPH", "PLC_1/Blocks/StateMachine");

        Assert.False(decision.IsAllowed);
        Assert.Contains("GRAPH", decision.RefusalMessage);
    }

    [Fact]
    public void An_STL_function_block_is_refused_because_STL_is_out_of_scope()
    {
        var decision = SourceFormatEligibility.Decide("FC", "STL", "PLC_1/Blocks/Legacy");

        Assert.False(decision.IsAllowed);
        Assert.Contains("STL", decision.RefusalMessage);
    }

    [Fact]
    public void An_instance_data_block_is_refused_by_name()
    {
        var decision = SourceFormatEligibility.Decide("InstanceDB", "DB", "PLC_1/Blocks/Damper_DB");

        Assert.False(decision.IsAllowed);
        Assert.Contains("instance data block", decision.RefusalMessage);
    }

    [Fact]
    public void An_array_data_block_is_refused_by_name()
    {
        var decision = SourceFormatEligibility.Decide("ArrayDB", "DB", "PLC_1/Blocks/Buffer_DB");

        Assert.False(decision.IsAllowed);
        Assert.Contains("array data block", decision.RefusalMessage);
    }

    [Fact]
    public void An_unrecognized_kind_is_refused_without_throwing()
    {
        var decision = SourceFormatEligibility.Decide("SomethingElse", "Undef", "PLC_1/Blocks/Odd");

        Assert.False(decision.IsAllowed);
        Assert.Contains("SomethingElse", decision.RefusalMessage);
    }

    [Fact]
    public void The_refusal_message_states_what_source_format_is_available_for()
    {
        var decision = SourceFormatEligibility.Decide("FB", "LAD", "PLC_1/Blocks/Inputs_FB");

        Assert.Contains("global data blocks", decision.RefusalMessage);
        Assert.Contains("SCL", decision.RefusalMessage);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceFormatEligibilityTests"`
Expected: FAIL — compile error, `SourceFormatEligibility` does not exist.

- [ ] **Step 4: Write minimal implementation**

Create `TiaMcpServer.OpennessWorker/Openness/SourceFormatEligibility.cs`:

```csharp
using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>The outcome of asking whether format=source applies to one block.</summary>
internal sealed class SourceFormatDecision
{
    private SourceFormatDecision(
        bool isAllowed,
        string extension,
        SourceObjectKind expectedKind,
        string? refusalMessage)
    {
        IsAllowed = isAllowed;
        Extension = extension;
        ExpectedKind = expectedKind;
        RefusalMessage = refusalMessage;
    }

    public bool IsAllowed { get; }

    /// <summary>File extension for the temp file, including the dot. Empty when refused.</summary>
    public string Extension { get; }

    /// <summary>The declaration kind a submitted source must carry. Meaningless when refused.</summary>
    public SourceObjectKind ExpectedKind { get; }

    public string? RefusalMessage { get; }

    public static SourceFormatDecision Allow(string extension, SourceObjectKind expectedKind)
        => new SourceFormatDecision(true, extension, expectedKind, null);

    public static SourceFormatDecision Refuse(string message)
        => new SourceFormatDecision(false, string.Empty, SourceObjectKind.Type, message);
}

/// <summary>
/// Decides whether Siemens external-source text is defined for a given block, and with which
/// extension.
///
/// <para>
/// Refusals name what the caller actually addressed rather than saying "unsupported", because the
/// caller cannot see the block's language from the path they typed. Graphical languages are
/// refused rather than attempted: nothing in the sample set demonstrates a working text rendering
/// for GRAPH, and a silently degraded rendering that imports back as damaged logic is the worst
/// failure this feature could produce. STL is refused for a different reason — TIA treats STL
/// external sources as a distinct file type (.awl) with no fixture and no live evidence behind it.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it: the caller extracts the
/// block's kind and language names and passes them in as strings.
/// </para>
/// </summary>
internal static class SourceFormatEligibility
{
    public const string GlobalDbExtension = ".db";
    public const string SclExtension = ".scl";
    private const string SclLanguage = "SCL";

    public static SourceFormatDecision Decide(string kindName, string languageName, string displayPath)
    {
        if (string.Equals(kindName, "GlobalDB", StringComparison.Ordinal))
        {
            return SourceFormatDecision.Allow(GlobalDbExtension, SourceObjectKind.DataBlock);
        }

        if (string.Equals(languageName, SclLanguage, StringComparison.OrdinalIgnoreCase))
        {
            switch (kindName)
            {
                case "FB": return SourceFormatDecision.Allow(SclExtension, SourceObjectKind.FunctionBlock);
                case "FC": return SourceFormatDecision.Allow(SclExtension, SourceObjectKind.Function);
                case "OB": return SourceFormatDecision.Allow(SclExtension, SourceObjectKind.OrganizationBlock);
            }
        }

        var description = Describe(kindName, languageName);

        return SourceFormatDecision.Refuse(
            $"'{displayPath}' is {description}. format={SourceFormatNames.Source} is available for "
            + $"global data blocks and SCL-language FB/FC/OB only; use format={SourceFormatNames.Xml} "
            + $"for {description}.");
    }

    public static string Describe(string kindName, string languageName)
    {
        switch (kindName)
        {
            case "InstanceDB": return "an instance data block (InstanceDB)";
            case "ArrayDB": return "an array data block (ArrayDB)";
            case "OB": return $"a {languageName} organization block (OB)";
            case "FB": return $"a {languageName} function block (FB)";
            case "FC": return $"a {languageName} function (FC)";
            default: return $"a {kindName} block ({languageName})";
        }
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceFormatEligibilityTests"`
Expected: PASS — 12 tests passed (3 from the Theory).

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/SourceFormatEligibility.cs \
        TiaMcpServer.Tests/SourceFormatEligibilityTests.cs \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: add SourceFormatEligibility language gate for format=source"
```

---

## Task 4: Strict single-object preflight

Rewrites `PlcTypeSourcePreflight`'s source path on top of the scanner. This changes behavior for two already-shipped operations — `update_type_content` and `update_block_logic` with `format=source` — from "take the first declaration" to "reject anything but exactly one declaration of the expected kind".

Adding `FUNCTION_BLOCK` and friends to the recognized keywords opens a hole the old regex did not have: without a kind check, a source declaring `FUNCTION_BLOCK "X"` submitted to `update_type_content` for a type named `X` would pass the name comparison and generate a block. The `expectedKind` parameter closes it.

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/PlcTypeSourcePreflight.cs`
- Modify: `TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs`

**Interfaces:**
- Consumes: `SourceDeclarationScanner`, `SourceObjectKind` (Task 2).
- Produces: `PlcTypeSourcePreflight.TryReadDeclaredName(string content, string format, SourceObjectKind expectedKind, out string declaredName, out string? error)`. The old four-argument overload is gone; Tasks 7 and 8 update both call sites.

- [ ] **Step 1: Update the existing tests and add the new ones**

In `TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs`, add `using TiaMcpServer.OpennessWorker.Openness;` if absent, then add the expected kind to every existing call. The `.udt` and SimaticML cases take `SourceObjectKind.Type`; the `.db` case takes `SourceObjectKind.DataBlock`. For example, the first test becomes:

```csharp
    [Fact]
    public void Reads_the_type_name_from_a_real_V21_udt_export()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.udt"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }
```

Delete `Source_with_no_recognizable_declaration_is_rejected_with_a_useful_message` outright — its premise was that `FUNCTION_BLOCK "Nope"` is unrecognizable, which is no longer true. Replace it, and add the new strictness tests, by appending:

```csharp
    [Fact]
    public void A_function_block_submitted_to_a_type_write_is_rejected_by_kind()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "FUNCTION_BLOCK \"Nope\"\r\nEND_FUNCTION_BLOCK\r\n",
            SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("FUNCTION_BLOCK", error);
        Assert.Contains("TYPE", error);
    }

    [Fact]
    public void Source_declaring_nothing_is_rejected_and_lists_the_expected_keywords()
    {
        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            "VAR\r\n  Foo : Bool;\r\nEND_VAR\r\n",
            SourceFormatNames.Source, SourceObjectKind.Type, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("TYPE", error);
        Assert.Contains("FUNCTION_BLOCK", error);
    }

    [Fact]
    public void A_two_object_source_is_rejected_and_names_both_objects()
    {
        var content = File.ReadAllText(FixturePath("AnalogInput.scl"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.FunctionBlock, out var name, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, name);
        Assert.NotNull(error);
        Assert.Contains("2 objects", error);
        Assert.Contains("AnalogInputSettings", error);
        Assert.Contains("AnalogInput", error);
    }

    [Fact]
    public void A_four_object_source_is_rejected_and_names_every_object()
    {
        var content = File.ReadAllText(FixturePath("DamperAnalog.scl"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.FunctionBlock, out _, out var error);

        Assert.False(ok);
        Assert.Contains("4 objects", error);
        Assert.Contains("HMI_Settings_DB", error);
        Assert.Contains("UDT_Settings", error);
    }

    [Fact]
    public void A_single_object_scl_source_is_accepted_for_a_function_block_write()
    {
        var content = File.ReadAllText(FixturePath("DamperDigital.scl"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Source, SourceObjectKind.FunctionBlock, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("DamperDigital", name);
    }

    [Fact]
    public void The_xml_path_ignores_the_expected_kind()
    {
        var content = File.ReadAllText(FixturePath("AnalogInputSettings.xml"));

        var ok = PlcTypeSourcePreflight.TryReadDeclaredName(
            content, SourceFormatNames.Xml, SourceObjectKind.FunctionBlock, out var name, out var error);

        Assert.True(ok, error);
        Assert.Equal("AnalogInputSettings", name);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcTypeSourcePreflightTests"`
Expected: FAIL — compile error, `TryReadDeclaredName` takes four arguments, not five.

- [ ] **Step 3: Write minimal implementation**

In `TiaMcpServer.OpennessWorker/Openness/PlcTypeSourcePreflight.cs`, delete the `DeclarationPattern` field and the `System.Text.RegularExpressions` using, change the public signature, and replace `TryReadFromSource` entirely. The class becomes:

```csharp
using System;
using System.Linq;
using System.Xml.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Reads the object name a submitted document declares, so a write can refuse a document whose
/// name or kind does not match the object it was addressed to.
///
/// <para>
/// This is what makes update_type_content and the external-source half of update_block_logic
/// strict rather than upserts: Openness' GenerateBlocksFromSource creates whatever the source
/// declares, so without these checks a typo in the path would silently create a stray object
/// instead of failing.
/// </para>
/// <para>
/// Exactly one declaration is required. A source declaring several — which a real V21 export does
/// whenever dependencies are included — is refused, because a write's preview names one object and
/// its safety token binds to one object.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class PlcTypeSourcePreflight
{
    public static bool TryReadDeclaredName(
        string content,
        string format,
        SourceObjectKind expectedKind,
        out string declaredName,
        out string? error)
    {
        declaredName = string.Empty;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The submitted document is empty.";
            return false;
        }

        return string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
            ? TryReadFromXml(content, out declaredName, out error)
            : TryReadFromSource(content, expectedKind, out declaredName, out error);
    }

    private static bool TryReadFromSource(
        string content,
        SourceObjectKind expectedKind,
        out string declaredName,
        out string? error)
    {
        declaredName = string.Empty;

        var declarations = SourceDeclarationScanner.Scan(content);

        if (declarations.Count == 0)
        {
            error = "The submitted source declares no object. Expected a line beginning with "
                + "TYPE, DATA_BLOCK, FUNCTION_BLOCK, FUNCTION, or ORGANIZATION_BLOCK.";
            return false;
        }

        if (declarations.Count > 1)
        {
            error = $"The submitted source declares {declarations.Count} objects: "
                + SourceDeclarationScanner.Describe(declarations)
                + ". A write accepts exactly one, because its preview and safety token name exactly "
                + "one object. Submit a source declaring only the object being updated, and write "
                + "the others separately.";
            return false;
        }

        var declaration = declarations[0];

        if (declaration.Kind != expectedKind)
        {
            error = $"The submitted source declares "
                + $"{SourceDeclaration.KeywordFor(declaration.Kind)} '{declaration.Name}', but this "
                + $"write targets a {SourceDeclaration.KeywordFor(expectedKind)}. Submit a source "
                + $"declaring a {SourceDeclaration.KeywordFor(expectedKind)}, or address the object "
                + "the source actually declares.";
            return false;
        }

        declaredName = declaration.Name;
        error = null;
        return true;
    }

    private static bool TryReadFromXml(string content, out string declaredName, out string? error)
    {
        declaredName = string.Empty;

        XDocument document;
        try
        {
            document = XDocument.Parse(content);
        }
        catch (Exception ex)
        {
            error = $"The submitted document is not well-formed XML: {ex.Message}";
            return false;
        }

        var name = document
            .Descendants()
            .Where(element => element.Name.LocalName == "Name")
            .Select(element => element.Value?.Trim())
            .FirstOrDefault(value => !string.IsNullOrEmpty(value));

        if (string.IsNullOrEmpty(name))
        {
            error = "The submitted Simatic ML document has no <Name> element to identify the object.";
            return false;
        }

        declaredName = name!;
        error = null;
        return true;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PlcTypeSourcePreflightTests"`
Expected: PASS. The two production call sites in `PlcTypeImporter.cs` and `BlockImporter.cs` still pass four arguments and will not compile — that is expected and is fixed in Tasks 7 and 8. The test project does not link either file, so the filtered run passes.

- [ ] **Step 5: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/PlcTypeSourcePreflight.cs \
        TiaMcpServer.Tests/PlcTypeSourcePreflightTests.cs
git commit -m "feat: require exactly one declaration of the expected kind in source writes"
```

---

## Task 5: `SourceReadWarnings`

Builds the warning that tells a caller a `withDependencies` read is not writable. Kept separate from the exporters so it is Siemens-free and testable.

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/SourceReadWarnings.cs`
- Create: `TiaMcpServer.Tests/SourceReadWarningsTests.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: `SourceDeclarationScanner` (Task 2), `TiaMcpServer.Contracts.SourceFormatNames`.
- Produces: `SourceReadWarnings.ForExport(bool withDependencies, string format, string content)` returning `IReadOnlyList<string>`. Task 7 calls it from `Program.cs`.

- [ ] **Step 1: Register the new file in the test project**

In `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, next to the `SourceFormatEligibility.cs` link added in Task 3, add:

```xml
    <Compile Include="..\TiaMcpServer.OpennessWorker\Openness\SourceReadWarnings.cs"
      Link="Linked\Openness\SourceReadWarnings.cs" />
```

- [ ] **Step 2: Write the failing test**

Create `TiaMcpServer.Tests/SourceReadWarningsTests.cs`:

```csharp
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;

namespace TiaMcpServer.Tests;

public class SourceReadWarningsTests
{
    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void A_dependency_read_that_returned_several_objects_is_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Source, Fixture("DamperAnalog.scl"));

        var warning = Assert.Single(warnings);
        Assert.Contains("4 objects", warning);
        Assert.Contains("HMI_Settings_DB", warning);
        Assert.Contains("context only", warning);
        Assert.Contains("withDependencies", warning);
    }

    [Fact]
    public void A_dependency_read_that_returned_one_object_is_not_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Source, Fixture("DamperDigital.scl"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void A_default_read_is_never_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: false, SourceFormatNames.Source, Fixture("DamperAnalog.scl"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void An_xml_read_is_never_warned_about()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Xml, Fixture("AnalogInputSettings.xml"));

        Assert.Empty(warnings);
    }

    [Fact]
    public void Empty_content_produces_no_warning()
    {
        var warnings = SourceReadWarnings.ForExport(
            withDependencies: true, SourceFormatNames.Source, string.Empty);

        Assert.Empty(warnings);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceReadWarningsTests"`
Expected: FAIL — compile error, `SourceReadWarnings` does not exist.

- [ ] **Step 4: Write minimal implementation**

Create `TiaMcpServer.OpennessWorker/Openness/SourceReadWarnings.cs`:

```csharp
using System;
using System.Collections.Generic;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// The one thing a source read can produce that the caller cannot see from the payload alone: a
/// document that will be refused if they try to write it back.
///
/// <para>
/// withDependencies=true asks Openness for a block's dependency closure, which is genuinely useful
/// context — but a write accepts exactly one declared object, so that document is a dead end for
/// editing. Saying so in a warning is cheaper than letting the caller discover it by having a
/// write rejected, and it keeps the payload itself clean SCL rather than SCL with an injected
/// banner comment.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it.
/// </para>
/// </summary>
internal static class SourceReadWarnings
{
    public static IReadOnlyList<string> ForExport(bool withDependencies, string format, string content)
    {
        if (!withDependencies || !string.Equals(format, SourceFormatNames.Source, StringComparison.Ordinal))
        {
            return Array.Empty<string>();
        }

        var declarations = SourceDeclarationScanner.Scan(content);

        if (declarations.Count <= 1)
        {
            return Array.Empty<string>();
        }

        return new[]
        {
            $"This document was read with withDependencies=true and declares {declarations.Count} "
            + $"objects: {SourceDeclarationScanner.Describe(declarations)}. It is context only — a "
            + "write refuses any source declaring more than one object. Re-read with "
            + "withDependencies omitted to get a document you can edit and submit back."
        };
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceReadWarningsTests"`
Expected: PASS — 5 tests passed.

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/SourceReadWarnings.cs \
        TiaMcpServer.Tests/SourceReadWarningsTests.cs \
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "feat: warn that a withDependencies read is not writable"
```

---

## Task 6: Host surface — thread `withDependencies` through

Adds the field to both request DTOs, registers it on the two read specs, forwards it in `BuildRequest`, and passes it to the client. `BatchOperationCatalog` discovers wire field names by reflecting over `BatchOperationRequest`, so adding the property is enough to make `withDependencies` a recognized name — but a spec that does not list it will reject it as an unexpected field.

**Files:**
- Modify: `TiaMcpServer.Contracts/WorkerRequest.cs`
- Modify: `TiaMcpServer/Batch/BatchOperationRequest.cs:126`
- Modify: `TiaMcpServer/Batch/BatchOperationCatalog.cs:293,297`
- Modify: `TiaMcpServer/Batch/BatchWorkerInvoker.cs:111-124,150-163`
- Modify: `TiaMcpServer/Worker/OpennessWorkerClient.cs:157,189`
- Modify: `TiaMcpServer/Batch/BatchSafetySnapshot.cs:26`
- Modify: `TiaMcpServer.Tests/BatchOperationCatalogTests.cs`
- Test: `TiaMcpServer.Tests/SourceDependencyFieldTests.cs` (create)

**Interfaces:**
- Consumes: nothing.
- Produces: `BatchOperationRequest.WithDependencies` (`bool?`), `WorkerRequest.WithDependencies` (`bool?`), and the `withDependencies` optional field on `get_block_content` and `get_type_content`. Task 7 reads `WorkerRequest.WithDependencies` in the worker.

- [ ] **Step 1: Write the failing test**

Create `TiaMcpServer.Tests/SourceDependencyFieldTests.cs`:

```csharp
using TiaMcpServer.Batch;

namespace TiaMcpServer.Tests;

public class SourceDependencyFieldTests
{
    private static BatchOperationRequest BlockRead() => new()
    {
        OperationId = "r1",
        Operation = "get_block_content",
        BlockPath = "PLC_1/Blocks/DamperDigital",
        Format = "source",
    };

    private static BatchOperationRequest TypeRead() => new()
    {
        OperationId = "r2",
        Operation = "get_type_content",
        TypePath = "PLC_1/Types/AnalogInputSettings",
    };

    [Fact]
    public void get_block_content_accepts_withDependencies()
    {
        var op = BlockRead();
        op.WithDependencies = true;

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void get_type_content_accepts_withDependencies()
    {
        var op = TypeRead();
        op.WithDependencies = true;

        var result = BatchOperationCatalog.ValidateReadBatch(new[] { op });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void update_block_logic_rejects_withDependencies()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/DamperDigital",
            YamlContent = "FUNCTION_BLOCK \"DamperDigital\"\r\nEND_FUNCTION_BLOCK\r\n",
            WithDependencies = true,
        };

        var result = BatchOperationCatalog.ValidateWriteBatch(new[] { op });

        Assert.False(result.IsValid);
        Assert.Contains("withDependencies", result.Error);
    }

    [Fact]
    public void BuildRequest_forwards_withDependencies_for_get_block_content()
    {
        var op = BlockRead();
        op.WithDependencies = true;

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.True(request.WithDependencies);
    }

    [Fact]
    public void BuildRequest_forwards_withDependencies_for_get_type_content()
    {
        var op = TypeRead();
        op.WithDependencies = true;

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.True(request.WithDependencies);
    }

    [Fact]
    public void BuildRequest_leaves_withDependencies_null_when_not_supplied()
    {
        var request = BatchWorkerInvoker.BuildRequest(BlockRead());

        Assert.Null(request.WithDependencies);
    }

    [Fact]
    public void BuildRequest_never_forwards_withDependencies_on_a_write()
    {
        // The safety token binds to the single-object form of the block; a dependency-bearing
        // current-state read would bind the token to a document a write can never accept.
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/DamperDigital",
            YamlContent = "FUNCTION_BLOCK \"DamperDigital\"\r\nEND_FUNCTION_BLOCK\r\n",
        };

        var request = BatchWorkerInvoker.BuildRequest(op);

        Assert.Null(request.WithDependencies);
    }

    [Fact]
    public void A_source_format_block_write_preview_says_so()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/DamperDigital",
            YamlContent = "FUNCTION_BLOCK \"DamperDigital\"\r\nEND_FUNCTION_BLOCK\r\n",
            Format = "source",
        };

        var description = BatchSafetySnapshot.DescribeOperation(op);

        Assert.Contains("PLC_1/Blocks/DamperDigital", description);
        Assert.Contains("source format", description);
    }

    [Fact]
    public void An_xml_block_write_preview_is_unchanged()
    {
        var op = new BatchOperationRequest
        {
            OperationId = "w1",
            Operation = "update_block_logic",
            BlockPath = "PLC_1/Blocks/Main",
            YamlContent = "<Document />",
        };

        Assert.Equal("Update PLC block 'PLC_1/Blocks/Main'.", BatchSafetySnapshot.DescribeOperation(op));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceDependencyFieldTests"`
Expected: FAIL — compile error, `BatchOperationRequest` has no `WithDependencies`.

- [ ] **Step 3: Add the field to both request DTOs**

In `TiaMcpServer/Batch/BatchOperationRequest.cs`, after the `Format` property (line 126), add:

```csharp

    [Description("Include the object's dependency closure in the exported source. Optional for get_block_content and get_type_content, and only meaningful when format is source. Defaults to false, which returns exactly one object. A document read with true declares several objects and is context only: a write refuses it.")]
    public bool? WithDependencies { get; set; }
```

In `TiaMcpServer.Contracts/WorkerRequest.cs`, after the `Format` property (line 76), add:

```csharp

    /// <summary>
    /// Forwarded by: get_block_content, get_type_content. Selects GenerateOptions.WithDependencies
    /// over GenerateOptions.None on the export. Never forwarded by a write: the safety token binds
    /// to the single-object form of the object being written.
    /// </summary>
    public bool? WithDependencies { get; set; }
```

- [ ] **Step 4: Register the field on the two read specs**

In `TiaMcpServer/Batch/BatchOperationCatalog.cs`, change line 293 from:

```csharp
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format" }),
```

to:

```csharp
            new BatchOperationSpec("get_block_content", BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format", "withDependencies" }),
```

and line 297 from:

```csharp
            new BatchOperationSpec("get_type_content", BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format" }),
```

to:

```csharp
            new BatchOperationSpec("get_type_content", BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format", "withDependencies" }),
```

- [ ] **Step 5: Update the catalog's expected-spec table**

`TiaMcpServer.Tests/BatchOperationCatalogTests.cs` holds a literal table of every operation's fields (around lines 330–360). Update the two read entries to match. Find:

```csharp
            ["get_block_content"] = (BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format" }),
```

and

```csharp
            ["get_type_content"] = (BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format" }),
```

and add `"withDependencies"` to each optional-field array:

```csharp
            ["get_block_content"] = (BatchOperationCategory.Read, new[] { "blockPath" }, new[] { "format", "withDependencies" }),
```

```csharp
            ["get_type_content"] = (BatchOperationCategory.Read, new[] { "typePath" }, new[] { "format", "withDependencies" }),
```

- [ ] **Step 6: Forward the field in `BuildRequest`**

In `TiaMcpServer/Batch/BatchWorkerInvoker.cs`, in the `BuildRequest` switch, add one line to each of the two read cases. `get_block_content` becomes:

```csharp
            case "get_block_content":
                request.BlockPath = op.BlockPath;
                request.Format = NormalizeFormat(op);
                request.WithDependencies = op.WithDependencies;
                break;
```

and `get_type_content` becomes:

```csharp
            case "get_type_content":
                request.TypePath = op.TypePath;
                request.Format = NormalizeFormat(op);
                request.WithDependencies = op.WithDependencies;
                break;
```

Leave the two write cases untouched.

- [ ] **Step 7: Add the parameter to the client methods**

In `TiaMcpServer/Worker/OpennessWorkerClient.cs`, change `GetBlockContentAsync` (line 157) to:

```csharp
    public Task<WorkerCallResult> GetBlockContentAsync(
        string blockPath,
        string? projectPath,
        string? format = null,
        bool? withDependencies = null)
    {
        return SendBoundProjectRequestAsync(
            "get_block_content",
            projectPath,
            request =>
            {
                request.BlockPath = blockPath;
                request.Format = format;
                request.WithDependencies = withDependencies;
            },
            string.Empty);
    }
```

and `GetTypeContentAsync` (line 189) to:

```csharp
    public Task<WorkerCallResult> GetTypeContentAsync(
        string typePath,
        string? format,
        string? projectPath,
        bool? withDependencies = null)
    {
        return SendBoundProjectRequestAsync(
            "get_type_content",
            projectPath,
            request =>
            {
                request.TypePath = typePath;
                request.Format = format;
                request.WithDependencies = withDependencies;
            },
            string.Empty);
    }
```

Both parameters are optional, so the existing write-arm callers that read current state — which must not request dependencies — keep compiling unchanged.

- [ ] **Step 8: Pass the field on the two read invoke arms**

In `TiaMcpServer/Batch/BatchWorkerInvoker.cs`, change `InvokeGetBlockContent` and `InvokeGetTypeContent` to forward it:

```csharp
    private static Task<WorkerCallResult> InvokeGetBlockContent(OpennessWorkerClient client, BatchOperationRequest op)
        => WithValidatedFormat(
            () => BuildRequest(op),
            request => client.GetBlockContentAsync(
                request.BlockPath!, op.ProjectPath, request.Format, request.WithDependencies));

    private static Task<WorkerCallResult> InvokeGetTypeContent(OpennessWorkerClient client, BatchOperationRequest op)
        => WithValidatedFormat(
            () => BuildRequest(op),
            request => client.GetTypeContentAsync(
                request.TypePath!, request.Format, op.ProjectPath, request.WithDependencies));
```

- [ ] **Step 9: Say "source format" in the write preview**

A `format=source` write and a `format=xml` write to the same block produce the same preview text today, so the user approving the safety token cannot tell which pipeline is about to run. In `TiaMcpServer/Batch/BatchSafetySnapshot.cs`, change the `update_block_logic` arm (line 26) from:

```csharp
        "update_block_logic" => $"Update PLC block '{op.BlockPath}'.",
```

to:

```csharp
        // The format decides which pipeline runs and therefore what a failed write can leave
        // behind, so the preview names it. Omitted format keeps the original wording, because
        // callers' previews must not change when they did not opt in.
        "update_block_logic" => string.Equals(op.Format, SourceFormatNames.Source, StringComparison.OrdinalIgnoreCase)
            ? $"Update PLC block '{op.BlockPath}' from source format."
            : $"Update PLC block '{op.BlockPath}'.",
```

If `SourceFormatNames` is not already imported in this file, add `using TiaMcpServer.Contracts;` at the top.

- [ ] **Step 10: Run the tests to verify they pass**

Run: `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~SourceDependencyFieldTests|FullyQualifiedName~BatchOperationCatalogTests|FullyQualifiedName~BatchFieldForwardingTests|FullyQualifiedName~BatchSafetySnapshotTests"`
Expected: PASS. `BatchFieldForwardingTests` is the invariant guard — if it fails, a spec declares a field that `BuildRequest` does not forward, which means Step 6 was missed for one of the two operations. If an existing `BatchSafetySnapshotTests` case asserts the old `update_block_logic` wording, confirm it passes no `Format` — if it does pass `format=source`, update that assertion to the new wording.

- [ ] **Step 11: Commit**

```bash
git add TiaMcpServer.Contracts/WorkerRequest.cs \
        TiaMcpServer/Batch/BatchOperationRequest.cs \
        TiaMcpServer/Batch/BatchOperationCatalog.cs \
        TiaMcpServer/Batch/BatchWorkerInvoker.cs \
        TiaMcpServer/Batch/BatchSafetySnapshot.cs \
        TiaMcpServer/Worker/OpennessWorkerClient.cs \
        TiaMcpServer.Tests/BatchOperationCatalogTests.cs \
        TiaMcpServer.Tests/SourceDependencyFieldTests.cs
git commit -m "feat: add withDependencies to get_block_content and get_type_content"
```

---

## Task 7: Worker export paths

Widens the export gate and honors `withDependencies`. Every file here touches `Siemens.Engineering`, so none is linked into the test project and none gets an offline test — the build is the compile-time gate, `scripts/live-test-scl.ps1` (Task 9) is the behavioral one.

**Read the spike findings recorded in the spec (Task 1) before starting.** Finding C tells you whether the two-argument `GenerateSource` overload was silently including dependencies; finding G tells you what an FB export actually contains.

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs:59-67,124-193`
- Modify: `TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs:22-72`
- Modify: `TiaMcpServer.OpennessWorker/Program.cs:276-287,310-321`

**Interfaces:**
- Consumes: `SourceFormatEligibility.Decide` (Task 3), `SourceReadWarnings.ForExport` (Task 5), `WorkerRequest.WithDependencies` (Task 6).
- Produces: `BlockExporter.Export(Project, string blockPath, string format, bool withDependencies = false)`, `BlockExporter.DecideSourceFormat(PlcBlock?, BlockAddress)` returning `SourceFormatDecision`, and `PlcTypeExporter.Export(Project, string typePath, string format, bool withDependencies = false)`. Task 8 calls `DecideSourceFormat`.

- [ ] **Step 1: Replace `RequireGlobalDb` with the eligibility decision**

In `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`, delete the `RequireGlobalDb` method (lines 162–183) and the `DescribeBlockKind` method (lines 185–193) entirely, and add in their place:

```csharp
    /// <summary>
    /// Decides whether external-source text is defined for this block, throwing the refusal as a
    /// validation error if not.
    ///
    /// <para>
    /// The kind and language are reduced to plain strings here so the decision itself lives in
    /// SourceFormatEligibility, which is Siemens-free and therefore unit-tested. This method is the
    /// only part that needs a live Openness object.
    /// </para>
    /// </summary>
    internal static SourceFormatDecision DecideSourceFormat(PlcBlock? block, BlockAddress address)
    {
        if (block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block was found at '{address.ToDisplayPath()}'.");
        }

        var decision = SourceFormatEligibility.Decide(
            BlockKindName(block),
            block.ProgrammingLanguage.ToString(),
            address.ToDisplayPath());

        if (!decision.IsAllowed)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                decision.RefusalMessage!);
        }

        return decision;
    }

    private static string BlockKindName(PlcBlock block) => block switch
    {
        GlobalDB => "GlobalDB",
        InstanceDB => "InstanceDB",
        ArrayDB => "ArrayDB",
        OB => "OB",
        FB => "FB",
        FC => "FC",
        _ => block.GetType().Name
    };
```

The `GlobalDB` case must precede `InstanceDB` and `ArrayDB` only if they derive from it; if the compiler reports an unreachable pattern, reorder so the most derived type comes first.

- [ ] **Step 2: Honor `withDependencies` in `ExportSource`**

In the same file, replace `ExportSource` (lines 124–156) with:

```csharp
    /// <summary>
    /// Exports one block as Siemens external-source text, raw and unbundled: unlike the XML route,
    /// which carries an .xml plus a companion .s7dcl/.s7res pair and therefore needs
    /// BlockBundleFormat's delimiters, this is a single document with nothing to delimit.
    ///
    /// <para>
    /// GenerateOptions is always passed explicitly. The two-argument overload's default is
    /// undocumented, and the difference matters: with dependencies the file declares several
    /// objects and a write will refuse it, so the caller has to have asked for that.
    /// </para>
    /// </summary>
    private static string ExportSource(
        ResolvedBlockTarget target,
        BlockAddress address,
        bool withDependencies)
    {
        var decision = DecideSourceFormat(target.Block, address);

        string tempDir = Path.Combine(Path.GetTempPath(), "tia-mcp-block-source-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var path = Path.Combine(tempDir, target.DocumentName + decision.Extension);

            // The resolver's own group, not one re-derived from the block: a unit-scoped block must
            // be generated by the unit's external source group.
            target.ExternalSourceGroup.GenerateSource(
                new List<IGenerateSource> { target.Block! },
                new FileInfo(path),
                withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);

            if (!File.Exists(path))
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    $"TIA Portal reported no error but produced no source file for "
                    + $"'{target.DocumentName}'. Compile the block in TIA Portal and try again.");
            }

            return SourceTextEncoding.ForTransport(File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }
```

If `new List<IGenerateSource> { target.Block! }` does not compile, `PlcBlock` does not implement `IGenerateSource` directly — cast explicitly: `new List<IGenerateSource> { (IGenerateSource)target.Block! }`. Phase 0's static inspection says the interface is present, but this is one of the two call shapes the spike verifies.

- [ ] **Step 3: Add the parameter to `Export`**

In the same file, change `Export` (lines 59–67) to:

```csharp
    public static string Export(
        Project project,
        string blockPath,
        string format,
        bool withDependencies = false)
    {
        var address = BlockAddress.Parse(blockPath);
        var target = BlockTargetResolver.ResolveForExport(project, address);

        if (!string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal))
        {
            return ExportSource(target, address, withDependencies);
        }
```

and update the XML-doc comment above it so it no longer says "only available for a global data block":

```csharp
    /// <param name="format">
    /// <see cref="SourceFormatNames.Xml"/> (the block default) produces the multi-document bundle
    /// this operation has always returned, byte for byte. <see cref="SourceFormatNames.Source"/>
    /// produces Siemens external-source text and is available for a global data block or an
    /// SCL-language FB/FC/OB. The host normalizes this to exactly one of the two before it reaches
    /// the worker.
    /// </param>
    /// <param name="withDependencies">
    /// When true, the exported source carries the block's dependency closure and therefore declares
    /// several objects. Such a document is context only — a write refuses it. Ignored for
    /// <see cref="SourceFormatNames.Xml"/>.
    /// </param>
```

The default of `false` keeps `BlockImporter.VerifySourcePostconditions`' existing two-argument call site compiling and behaving identically.

- [ ] **Step 4: Do the same for the type exporter**

In `TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs`, change `Export` to take the flag and pass it down:

```csharp
    public static string Export(
        Project project,
        string typePath,
        string format,
        bool withDependencies = false)
    {
        var address = PlcTypeAddress.Parse(typePath);
        var target = PlcTypeTargetResolver.ResolveForExport(project, address);

        if (target.Type is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No PLC data type was found at '{address.ToDisplayPath()}'.");
        }

        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "tia-mcp-type-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            return string.Equals(format, SourceFormatNames.Xml, StringComparison.Ordinal)
                ? ExportXml(target, tempDirectory)
                : ExportSource(target, tempDirectory, withDependencies);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
```

and change `ExportSource`'s signature and its `GenerateSource` call:

```csharp
    private static string ExportSource(
        ResolvedTypeTarget target,
        string tempDirectory,
        bool withDependencies)
    {
        var path = Path.Combine(tempDirectory, target.DocumentName + ".udt");

        // The resolver's own group, not one re-derived from the type: a unit-scoped type must be
        // generated by the unit's external source group. GenerateOptions is passed explicitly
        // because the two-argument overload's default is undocumented, and a type that references
        // other types would otherwise silently export a multi-object document.
        target.ExternalSourceGroup.GenerateSource(
            new List<IGenerateSource> { target.Type! },
            new FileInfo(path),
            withDependencies ? GenerateOptions.WithDependencies : GenerateOptions.None);
```

Leave the rest of the method body unchanged.

- [ ] **Step 5: Read the flag and attach the warning in the worker dispatch**

In `TiaMcpServer.OpennessWorker/Program.cs`, replace `GetBlockContent` (lines 276–287) with:

```csharp
    private static WorkerResponse GetBlockContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.BlockPath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "BlockPath is required.");
        }

        var format = NormalizeBlockFormat(request.Format);
        var withDependencies = request.WithDependencies == true;

        return WithProject(request, project =>
        {
            var content = BlockExporter.Export(project, request.BlockPath!, format, withDependencies);
            return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
        });
    }
```

and replace `GetTypeContent` (lines 310–321) with:

```csharp
    private static WorkerResponse GetTypeContent(WorkerRequest request)
    {
        if (string.IsNullOrEmpty(request.TypePath))
        {
            throw new WorkerOperationException(WorkerFailureCategories.ValidationError, "TypePath is required.");
        }

        var format = NormalizeTypeFormat(request.Format);
        var withDependencies = request.WithDependencies == true;

        return WithProject(request, project =>
        {
            var content = PlcTypeExporter.Export(project, request.TypePath!, format, withDependencies);
            return RawPayload(content, SourceReadWarnings.ForExport(withDependencies, format, content));
        });
    }
```

- [ ] **Step 6: Build both configurations**

Run in PowerShell:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: build succeeded. `BlockImporter.cs` still calls the removed `RequireGlobalDb` and the four-argument `TryReadDeclaredName`, so this build **fails** until Task 8 lands. That is expected: run it anyway to confirm the only errors are those two, in `BlockImporter.cs`, and nothing else broke.

- [ ] **Step 7: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs \
        TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs \
        TiaMcpServer.OpennessWorker/Program.cs
git commit -m "feat: widen source export to SCL blocks and honor withDependencies"
```

---

## Task 8: Worker import path

Widens the write gate to match the export gate. This is the task that makes the tree compile again.

**Files:**
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs:178-245`
- Modify: `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs:49`

**Interfaces:**
- Consumes: `BlockExporter.DecideSourceFormat` (Task 7), `PlcTypeSourcePreflight.TryReadDeclaredName` five-argument form (Task 4), `SourceObjectKind` (Task 2).
- Produces: nothing new.

- [ ] **Step 1: Replace the gate, the preflight call, and the extension in `ImportSource`**

In `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs`, replace lines 185–223 — from the `// 2/3.` comment through the `ExternalSourceScope.Create` call — with:

```csharp
        // 2/3. Refuse if the block does not exist, or is not one this format is defined for. This
        // is an update, never an upsert. DecideSourceFormat throws the refusal itself and also
        // yields the file extension and the declaration kind the submitted source must carry.
        if (target.Block is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"No block exists at '{address.ToDisplayPath()}'. update_block_logic only updates a "
                + "block that is already in the project; it never creates one.");
        }

        var decision = BlockExporter.DecideSourceFormat(target.Block, address);
        var targetName = target.Block.Name;

        // 4. Refuse if the submitted document declares more than one object, an object of the wrong
        // kind, or a different name.
        if (!PlcTypeSourcePreflight.TryReadDeclaredName(
                sourceContent,
                SourceFormatNames.Source,
                decision.ExpectedKind,
                out var declaredName,
                out var preflightError))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                preflightError ?? "The submitted document declares no object name.");
        }

        if (!string.Equals(declaredName, targetName, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"The submitted document declares '{declaredName}' but '{address.ToDisplayPath()}' "
                + $"resolves to '{targetName}'. update_block_logic never renames and never creates: "
                + $"submit a document declaring '{targetName}', or address the block the document "
                + "actually declares.");
        }

        // 5. Apply the document through the Siemens external-source pipeline.
        //
        // target.ExternalSourceGroup, not one re-derived from the block: for a block inside a
        // software unit this is the unit's own group, and registering under the top-level PLC
        // instead would generate a stray block there and leave the real one untouched.
        //
        // The extension comes from the resolved block, never from the caller — .db for a global
        // data block, .scl for an SCL block.
        var scope = ExternalSourceScope.Create(
            target.ExternalSourceGroup, targetName + decision.Extension, sourceContent);
```

Leave everything from `IList<IEngineeringObject>? generated;` onward untouched.

- [ ] **Step 2: Generalize the postcondition message**

In the same file, `VerifySourcePostconditions` hardcodes "the data block update" in three messages. Replace those three occurrences of `"the data block update"` / `"after the data block update"` with wording that fits any source-format block. Change line 288 from:

```csharp
            project, address.PlcName, blockPath: null, "the data block update", warnings);
```

to:

```csharp
            project, address.PlcName, blockPath: null, "the source-format block update", warnings);
```

and the two strings in the returned evidence from:

```csharp
                    : "Re-export produced an empty document after the data block update.",
```

```csharp
                diagnosticMessage: "Re-export could not complete after the data block update: " + exception.Message,
```

to:

```csharp
                    : "Re-export produced an empty document after the source-format block update.",
```

```csharp
                diagnosticMessage: "Re-export could not complete after the source-format block update: " + exception.Message,
```

Also update the method's summary comment, which says "rewriting a global DB's declaration": change that phrase to "rewriting a block's declaration". The reasoning it gives — compile the whole PLC because the change invalidates dependents the compiler alone can enumerate — holds more strongly for an FB than for a DB, so the comment stays otherwise correct.

- [ ] **Step 3: Pass the expected kind from the type importer**

In `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs`, change line 49 from:

```csharp
        if (!PlcTypeSourcePreflight.TryReadDeclaredName(sourceContent, format, out var declaredName, out var preflightError))
```

to:

```csharp
        if (!PlcTypeSourcePreflight.TryReadDeclaredName(
                sourceContent, format, SourceObjectKind.Type, out var declaredName, out var preflightError))
```

- [ ] **Step 4: Build both configurations**

Run in PowerShell:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Expected: build succeeded, 0 errors.

Then, on a machine with TIA Portal installed:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

Expected: build succeeded, 0 errors. This is where an incorrect `GenerateSource` overload or a missing `IGenerateSource` cast surfaces.

- [ ] **Step 5: Run the full test suite**

Run: `dotnet test TiaMcpServer.Tests`
Expected: PASS, all tests. The count is higher than the previous baseline by the tests added in Tasks 2, 3, 5, and 6, minus the one deleted in Task 4.

- [ ] **Step 6: Commit**

```bash
git add TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs \
        TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs
git commit -m "feat: widen source write gate to SCL blocks with strict single-object preflight"
```

---

## Task 9: Live-test harness

The behavioral gate for everything Tasks 7 and 8 changed. Nothing in those two tasks has offline coverage by construction.

**Files:**
- Create: `scripts/live-test-scl.ps1`
- Modify: `scripts/live-test-udt.ps1`

**Interfaces:**
- Consumes: the built worker and the shipped operations.
- Produces: a pass/fail live gate. No code depends on it.

**Prerequisites:** the same scratch project Task 1 used, containing an SCL FB with a UDT-typed parameter, an SCL FB inside a software unit, a global DB, and one LAD block to prove the refusal.

- [ ] **Step 1: Read the reference harness**

Read `scripts/live-test-db.ps1` end to end. Mirror its structure: parameter block, session setup, a `Test-Case` style helper that records pass/fail per assertion, teardown that closes the portal, and a final summary line with a non-zero exit code on any failure. Do not invent a new harness shape.

- [ ] **Step 2: Write the script**

Create `scripts/live-test-scl.ps1` covering exactly these cases, each printing a labelled PASS or FAIL:

1. **Single-object read.** `get_block_content` on the SCL FB with `format=source`, `withDependencies` omitted. Assert the payload declares exactly one object, that it is a `FUNCTION_BLOCK`, and that its name matches the block.
2. **Dependency read.** Same block with `withDependencies=true`. Assert the payload declares more than one object and that the response carries a warning containing "context only".
3. **Round trip.** Take case 1's payload, append one new `Bool` member to its `VAR` section, submit it via `preview_write_batch` then `apply_write_batch` with `update_block_logic`, `format=source`. Assert the apply succeeds, then re-read and assert the new member is present.
4. **Compile clean.** After case 3, run `compile_check` on the PLC. Assert zero errors.
5. **Multi-object write refused.** Submit case 2's dependency-bearing payload as an `update_block_logic` write. Assert it fails with a validation error naming the object count, and that a re-read of the block is byte-identical to what case 3 left behind.
6. **Wrong-name write refused.** Take case 1's payload, rename the declared block in the first line only, submit it. Assert a validation error and an unchanged block.
7. **Wrong-kind write refused.** Submit a payload declaring `TYPE "<block name>"` to the FB's path. Assert a validation error naming both keywords.
8. **LAD block refused.** `get_block_content` on the LAD block with `format=source`. Assert a validation error naming LAD and suggesting `format=xml`.
9. **Global DB unchanged.** Repeat the case 1 and case 3 shapes against the global DB. Assert both still work — this is the regression guard for shipped Phase 2 behavior.
10. **Software unit.** Repeat cases 1 and 3 against the SCL FB inside the software unit.
11. **No residue.** Assert the project's external source files folder contains no node whose name includes `_tiamcp_`.

- [ ] **Step 3: Run it**

Run: `pwsh -NoProfile -File scripts/live-test-scl.ps1 -ProjectPath "<path to scratch project>"`
Expected: every case prints PASS and the script exits 0.

A failure here is a real defect in Task 7 or 8, not a harness problem — fix the production code, not the assertion.

- [ ] **Step 4: Add the type-side dependency case**

In `scripts/live-test-udt.ps1`, add one case mirroring the existing read cases: `get_type_content` on a UDT that references another UDT, first with `withDependencies` omitted and then with `withDependencies=true`. Assert the first declares exactly one `TYPE` and the second declares more than one.

- [ ] **Step 5: Run it**

Run: `pwsh -NoProfile -File scripts/live-test-udt.ps1 -ProjectPath "<path to scratch project>"`
Expected: every case prints PASS and the script exits 0.

- [ ] **Step 6: Commit**

```bash
git add scripts/live-test-scl.ps1 scripts/live-test-udt.ps1
git commit -m "test: add live SCL external-source gate and UDT dependency-read case"
```

---

## Task 10: Documentation

Applies the roadmap corrections the spec identified, and records the phase as delivered.

**Files:**
- Modify: `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: the spike findings recorded in Task 1.
- Produces: nothing code depends on.

- [ ] **Step 1: Correct the Phase 3 row**

In `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md`, replace the Phase 3 row of the phasing table:

```markdown
| 3 — SCL | Add `.scl` as a selectable/default format for SCL/STL-language blocks; replace the `BlockSourceGenerator` XML-only placeholder with real source generation. | Phase 1 (inline UDT dependencies), Phase 0 |
```

with:

```markdown
| 3 — SCL | Add `.scl` as a selectable format for SCL-language FB/FC/OB via `format=source`, with a `withDependencies` read option (delivered — see `docs/superpowers/specs/2026-07-27-scl-external-source-design.md`). STL deferred: it needs `.awl` and has no fixture. `BlockSourceGenerator` deliberately untouched — its empty `<NetworkSource />` is schema-valid and `create_block` + `update_block_logic` already compose. | Phase 1 (inline UDT dependencies), Phase 0 |
```

- [ ] **Step 2: Close Phase 0 for SCL**

In the same file, replace this sentence in the "Phase 0 — V21 API exposure confirmed" section:

```markdown
Phase 0 stays open **for SCL only**: the closing condition — a real V21 fixture proving
`GenerateSource → CreateFromFile → GenerateBlocksFromSource`, followed by compile and re-export
comparison — is now met for the UDT and DB legs; the SCL leg still needs that same fixture, for
one representative SCL block.
```

with:

```markdown
**Phase 0 is closed.** The `GenerateSource → CreateFromFile → GenerateBlocksFromSource` round trip
is proven live for UDTs (`scripts/live-test-udt.ps1`), global DBs (`scripts/live-test-db.ps1`), and
SCL-language blocks (`scripts/live-test-scl.ps1`), including a block inside a software unit in each
case. `GenerateOptions` is now passed explicitly on every export path rather than relying on the
two-argument overload's undocumented default.
```

- [ ] **Step 3: Record the multi-object finding in the sample analysis**

In the same file, in the "Sample export analysis" section's bulleted observations, add:

```markdown
- **A `.scl` file routinely declares several objects of different kinds.** The 2026-07-27 samples
  make this concrete: `DamperDigital.scl` declares one `FUNCTION_BLOCK`, `AnalogInput.scl` declares
  a `TYPE` and a `FUNCTION_BLOCK`, and `DamperAnalog.scl` declares two `TYPE`s, a `DATA_BLOCK`, and
  a `FUNCTION_BLOCK` — 8,395 bytes covering four objects. This is `GenerateOptions.WithDependencies`
  behavior, and it is why reads let the caller choose multiplicity while writes accept exactly one:
  `GenerateBlocksFromSource` creates everything the file declares and has no notion of the object
  the caller addressed.
```

- [ ] **Step 4: Document the new option in the README**

In `README.md`, find the section describing `get_block_content` / `update_block_logic` / `get_type_content` / `update_type_content` and their `format` field. Add, in the same style as the surrounding text:

```markdown
`format=source` is available for global data blocks, PLC data types, and SCL-language FB/FC/OB.
Every other block language stays on `format=xml`.

`withDependencies` (reads only, default `false`) asks TIA Portal to include the object's dependency
closure. The resulting document declares several objects and is **context only** — a write refuses
any source declaring more than one object, and the read carries a warning saying so. Omit the field
to get a document you can edit and submit back.
```

- [ ] **Step 5: Verify nothing else claims global-DB-only**

Run: `grep -rn "only available for global data blocks\|GlobalDB only" README.md docs/ TiaMcpServer/ TiaMcpServer.OpennessWorker/ TiaMcpServer.Contracts/`
Expected: no hits outside the roadmap's historical narrative. Any hit in a `[Description]` attribute or XML-doc comment is stale and must be updated — the `BatchOperationRequest.Format` description at line 125 is one such place and needs its "honor source for GlobalDB only" phrase corrected to "honor source for global data blocks and SCL-language FB/FC/OB".

- [ ] **Step 6: Run the full suite one last time**

Run: `dotnet test TiaMcpServer.Tests`
Expected: PASS, all tests. A `[Description]` change in Step 5 can break a test that asserts on the description text; if so, update the assertion to match the new wording.

- [ ] **Step 7: Commit**

```bash
git add docs/EXPORT_IMPORT_FORMAT_ROADMAP.md README.md \
        TiaMcpServer/Batch/BatchOperationRequest.cs
git commit -m "docs: record Phase 3 SCL delivery and close Phase 0"
```
