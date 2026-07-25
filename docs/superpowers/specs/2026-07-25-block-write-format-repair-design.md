# Block write path — format repair design

**Date:** 2026-07-25
**Branch:** `phase5` @ `e65dc64`
**Input:** `priv/MCP_TOOL_TEST_REPORT_2026-07-25.md` (round 2 live test)
**Fixtures:** `priv/tia_exports/` (gitignored) — `Inputs_FB.xml` (FB/LAD, 21 compile units),
`nStageHeater.xml` (FB/SCL), `InputValues_DB.xml` (GlobalDB): raw Simatic ML exports from the
live V21 project. Plus `TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.bundle.txt`
(committed): literal `get_block_content` output for `MCP_Test_CPU/Blocks/Main`, captured
2026-07-25 via `execute_read_batch`.
**Status:** proposal — no implementation. Import format: **Option A approved**.

---

## Summary

Round 2 reported three bugs. Source investigation confirms all three and finds that
Finding #3 (`update_block_logic`) has **two independent root causes, not one**. The fix
direction the report suggested (insert a newline in `BlockExporter`) is necessary but
**not sufficient** — the update path would still fail afterwards, because it feeds a
Simatic ML `.xml` document to the SIMATIC SD documents importer.

Three further defects surfaced during the trace that the live test could not observe.

| ID | Defect | Report finding | Confirmed by |
|---|---|---|---|
| D1 | Bundle delimiter contract broken between producer and consumer | #3 | source |
| D2 | Simatic ML `.xml` passed to the SD-documents importer as its base name | #3 (undiagnosed) | source + Openness API contract |
| D3 | Post-write verification re-exports under the same wrong name | — | source |
| D4 | SCL compile unit is schema-invalid, and the validator *mandates* the invalid shape | #1 | source + Siemens error |
| D5 | GlobalDB XML omits `<ProgrammingLanguage>`; validator rejects the only sensible value | #2 | source + Siemens error |
| D6 | Export failure placeholder becomes a legal import document name | — | source |

---

## Root causes

### D1 — the bundle delimiter contract is violated by its own producer

`BlockExporter.Export` (`TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs:63-64, 80-81`)
appends each `--- FILE: {name} ---\n` marker directly onto the end of the previous
document's content. The `.xml` section's content comes from `StripNonDeterministic`
(`BlockExporter.cs:99-114`), which returns `XDocument.ToString()` — never terminated by a
newline. So the emitted stream is:

```
--- FILE: Main.xml ---\n<Document>…</Document>--- FILE: Main.s7dcl ---\n…
                                             ^ no preceding newline
```

`BlockImportBundleParser.DocumentDelimiter` (`BlockImportBundleParser.cs:11-13`) is
`^--- FILE: (?<name>.+) ---(?:\r?\n|$)` under `RegexOptions.Multiline`. In .NET, `^` in
multiline mode matches only at string start or immediately after `\n`. The second marker
therefore does not match.

`ValidateDelimiterLines` (`BlockImportBundleParser.cs:82-92`) was meant to catch malformed
delimiters, but its candidate regex `^--- FILE:` carries the **same anchor**, so the
unmatched marker is invisible to the validator too. No error is raised.

**Effect:** only `documents[0]` is ever recognised. Its content silently absorbs the
literal marker text and every subsequent document's body. Documents 2..n are never staged.

**Confirmed empirically.** `TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.bundle.txt`
is the literal output of `get_block_content("MCP_Test_CPU/Blocks/Main")` — 3346 bytes, two
documents. The boundary reads:

```
"\n</Document>--- FILE: Main.s7dcl ---\n{"
                ^ character before the marker is '>', not '\n'
```

Replaying `BlockImportBundleParser.DocumentDelimiter` against those exact bytes yields
**1 document (`Main.xml`) out of the 2 present**. The fixture also shows the bundle uses
**mixed line endings** — bare `LF` on the two `--- FILE:` markers, `CRLF` throughout both
document bodies (93 CRLF, exactly 2 lone LF). Any `Compose`/`Parse` round-trip test must
cover that mix; normalising everything to `\n` would not reproduce the real input.

### D2 — two incompatible Openness exchange formats are mixed, then handed to the wrong importer

`BlockExporter.Export` produces a bundle from **two different Openness services**:

- `Block.Export(FileInfo, ExportOptions)` → **Simatic ML** `<name>.xml`
  (imported with `PlcBlockComposition.Import(FileInfo, ImportOptions)`)
- `Block.ExportAsDocuments(DirectoryInfo, name)` → **SIMATIC SD** `<name>.s7dcl` (+ `.s7res`)
  (imported with `PlcBlockComposition.ImportFromDocuments(DirectoryInfo, name, ImportDocumentOptions)`)

The Openness contract for the documents pair is symmetric on an **extension-less base name**:

```csharp
blocks[0].ExportAsDocuments(new DirectoryInfo(dir), "LAD_Block");
blockGroup.Blocks.ImportFromDocuments(new DirectoryInfo(dir), "LAD_Block", ImportDocumentOptions.Override);
```

`BlockImporter.Import` (`BlockImporter.cs:17, 44-47`) instead passes
`bundle.PrimaryDocumentName`, which is `documents[0].LogicalName`
(`BlockImportBundleParser.cs:74`) — i.e. `"<Block>.xml"`, with an extension, and pointing
at a document that is not part of the SD document set at all. The SD importer cannot
resolve a document set under that name, which is exactly the observed
`The file 'MCP_Test_FB.xml' does not exist.`

**This is why fixing D1 alone will not fix `update_block_logic`.** With D1 fixed, the
parser would correctly yield two documents — and the importer would still be asked to
import a Simatic ML file through the SD documents API under an `.xml` base name.

**D2 was already diagnosed and already fixed once — then silently regressed.** Commit
`c53e6f4` states it outright:

> BlockImporter: route single Simatic ML XML to `Import(FileInfo, ImportOptions.Override)`
> instead of `ImportFromDocuments`. The latter expects a .s7dcl documents package; passing a
> bare .xml produced a **misleading "file does not exist" error even when the OS file
> existed**.

That is the round-2 symptom, verbatim, identified by a previous developer on this same V21
install. The routing survived `55c502d` and `1163fe4`, then **`dddf9d2` ("fix: verify block
update postconditions") removed it** — a commit whose stated scope was postcondition
verification, not import routing. The removal was collateral damage in a refactor. The three
later hardening commits (`0ceeccc`, `4f16d54`, `ab12c78`) were then layered on top of a path
that `dddf9d2` had already broken.

Independent corroboration of the base-name semantics also sits in `1163fe4`: *"write temp file
with .s7dcl extension so ImportFromDocuments finds the file by **fileNameWithoutExtension**
lookup"* — a prior direct observation of exactly the lookup rule diagnosed here.

**Consequence for Fix 2: Option A is not new work.** It is restoring `c53e6f4`'s content-type
routing (`IsXmlContent` → `Blocks.Import(FileInfo, …)`) on top of today's preflight, staging
and postcondition machinery. That sharply lowers the risk of the approved option, and it
raises a requirement: the regression test must pin the *routing decision itself*, since the
last time it was correct, a refactor removed it without any test objecting.

The tool schema already declares the intended surface as SD
(`BatchOperationRequest.cs:32`: *"SIMATIC SD document content for the block"*), while
`get_block_content` puts the Simatic ML `.xml` **first**, making it the primary. Producer
intent and consumer intent diverged.

### D3 — post-write verification inherits the same wrong name semantics

`BlockExporter.VerifyPrimaryDocument` (`BlockExporter.cs:21-31`) re-exports with
`ExportAsDocuments(directory, documentName)` and then asserts
`File.Exists(Path.Combine(directory, documentName))` where `documentName` is the declared
primary — `"<Block>.xml"`. `ExportAsDocuments` treats that as a base name and writes
`<Block>.xml.s7dcl`, so the existence check can never pass.

Note also that `resolvedTargetDocumentName` (the *correct* base name, from
`ResolvedBlockTarget.DocumentName`) is null-checked at
`BlockExporterVerification.cs:22-25` and then **never used**. Commit `71b6687`
("honor declared primary document during verification") substituted the declared
extension-bearing name for the resolved base name — this is a regression, and it explains
why round 1's error text (`The file 'Main' does not exist.`) differs from round 2's.

### D4 — SCL compile unit is schema-invalid, and the validator requires the invalid shape

`BlockSourceGenerator.GenerateCompileUnit` (`BlockSourceGenerator.cs:146-159`) emits:

```xml
<StructuredText xmlns="…/StructuredText/v3">// Generated SCL source</StructuredText>
```

The v3 schema forbids text nodes there; permitted children are
`Access, Token, Parameter, Text, Comment, LineComment, Blank, NewLine`. That is precisely
the Siemens error in Finding #1.

The two halves are locked together: `BlockSourceValidator.HasSclSourceBody`
(`BlockSourceValidator.cs:61-72`) passes only when `StructuredText.Value` is non-whitespace
— i.e. it **requires** the raw text node that the schema rejects. Meanwhile the STL path
(`BlockSourceGenerator.cs:167`) emits an *empty, self-closing* `<StructuredText/>`. Generator
and validator must be changed together; changing either alone re-breaks the other.

### D5 — GlobalDB creation is unreachable by construction

Two independent gates, in sequence:

1. `BlockWritePreflight.PrepareCreate` (`BlockWritePreflight.cs:58`) defaults a missing
   language to `"LAD"` **regardless of block type**. `ValidateTypeLanguage` then accepts it
   (`BlockSourceValidator.cs:40-47` — `LAD` is the only value permitted for `GLOBALDB`).
   Generation proceeds to `GenerateGlobalDbXml` (`BlockSourceGenerator.cs:109-132`), whose
   `AttributeList` contains **no `<ProgrammingLanguage>` element at all**. Siemens `Import`
   rejects it: *"The argument 'ProgrammingLanguage' is missing."*
2. Supplying `language: "DB"` never reaches generation — `ValidateTypeLanguage` throws
   *"Block type 'GLOBALDB' does not support language 'DB'."* first.

So the only value that passes local validation (`LAD`) fails in Siemens, and the only value
that would satisfy Siemens (`DB`) fails locally. No input succeeds. The public tool
documentation ("language: FB/FC/OB only") describes the intended contract correctly; the
implementation leaks an internal placeholder into it.

### D6 — export failure placeholder becomes a legal import document name (latent)

When the Simatic ML export fails, `BlockExporter.cs:68` emits
`--- FILE: <Block>.xml (unavailable) ---`. `ValidateDocumentName`
(`BlockImportBundleParser.cs:94-109`) accepts that string — no invalid filename chars — so
a later `update_block_logic` would stage a file literally named `Block.xml (unavailable)`
containing an HTML comment. An export-side error marker must never be able to round-trip
into a write.

---

## Ground truth from the live V21 exports

The fixtures invalidate three assumptions in the original draft of this document. Every
statement below is read directly from `priv/tia_exports/`.

**G1 — the StructuredText namespace in V21 is `v4`, not `v3`.**
`nStageHeater.xml` uses
`http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4`.
`BlockSourceGenerator.cs:150,167` emits `…/StructuredText/v3`. LAD networks use
`…/NetworkSource/FlgNet/v5`; interfaces use `…/SW/Interface/v5`. The v3 namespace quoted in
the Siemens error from Finding #1 is *our own* — the generator told Siemens which schema to
validate against, and named an outdated one.

**G2 — an empty compile-unit body is legal.**
`Inputs_FB.xml` contains **five** self-closing `<NetworkSource />` elements among its 21
compile units. So a compile unit with no body content round-trips through TIA today. This
makes the empty-body hypothesis in Fix 4 substantially stronger and removes the need to
hand-author any token sequence.

**G3 — text inside StructuredText is carried by `<Text UId="…">` elements, never as a raw
text node.** The real SCL body is a flat token stream:
`<Token Text="REGION" UId="21" /> <Blank UId="22" /> <Text UId="23">Initialisation</Text>
<NewLine UId="24" />…` — 265 `Token`, 263 `Blank`, 135 `Access`, 101 `NewLine`, 13 `Text`.
Every element carries a `UId`. This confirms D4 exactly: the generator's raw text node is
the one shape the schema does not permit.

**G4 — Simatic ML `ID` attributes are hexadecimal and monotonically increasing in document
order.** `Inputs_FB.xml` compile-unit IDs run `3, 8, D, 12, 17, 1C, 21, …` (step 5, hex).
The generator emits IDs in document order `1, 3, 4, 5, 2` — non-monotonic. Siemens' Finding
#1 error names the object it could not create as *"'SW.Blocks.CompileUnit' object with
Simatic ML ID '3'"*. The proven cause of that failure is the raw text node (G3), but ID
allocation is a credible second-order defect and must be corrected in the same pass rather
than left to be discovered later.

**G5 — a CompileUnit's `ObjectList` is optional.** The SCL compile unit in `nStageHeater.xml`
has `AttributeList` only; the LAD compile units have both. The generator always emits an
`ObjectList` with two `MultilingualText` children.

**G6 — GlobalDB ground truth (`InputValues_DB.xml`), against `GenerateGlobalDbXml`:**

| Element | Real export | Generator (`BlockSourceGenerator.cs:109-132`) |
|---|---|---|
| `ProgrammingLanguage` | `DB` | **absent** — this is the D5 failure |
| memory layout | `<MemoryLayout>Optimized</MemoryLayout>` | `<Optimized>true</Optimized>` — **wrong element name** |
| `AutoNumber` | `false` | `true` |
| `DBAccessibleFromOPCUA` / `…Webserver` | present (`false`) | absent |
| `MemoryReserve` | `100` | absent |
| `Number` | `101` | absent |
| `HeaderAuthor/Family/Name/Version` | **absent** | all four emitted |

The `<Optimized>` guess flagged in the first draft is confirmed wrong. Since FB/FC creation
in LAD works today while emitting `Header*` and omitting `MemoryLayout`/`Number`, those
fields are evidently optional on import — but the fixture now supplies the canonical set, so
the templates should stop guessing.

**G7 — canonical OB template, from the captured bundle.** The real, freshly-created `Main` OB
declares `Interface, MemoryLayout, Name, Namespace, Number, ProgrammingLanguage,
SecondaryType, SetENOAutomatically` — **no `AutoNumber`, no `Header*`**, where
`GenerateObXml` (`BlockSourceGenerator.cs:81-107`) emits `AutoNumber` plus all four `Header*`.
Its IDs run `1…9` strictly monotonic in document order: `MultilingualText` Comment `1`/`2`,
then `CompileUnit` `3` carrying `4`-`7`, then `MultilingualText` Title **`8`**/`9`. The
generator emits Title as ID `2`, i.e. a lower ID after a higher one — the exact G4 violation,
now with a correct reference to copy. Its compile unit is `<NetworkSource />`, empty and
self-closing, on a block TIA itself had just created — G2 reconfirmed on new-block output.

**G8 — `StripNonDeterministic` is a whole-document reserialization on what Option A makes a
write path.** This is the highest-risk unknown remaining in Fix 2 and deserves its own
treatment; see "G8 in detail" below.

**Prompt-injection scan.** All three raw exports scanned for injected directives. The only
keyword hit was the literal Simatic ML element `<Instruction>` in `nStageHeater.xml`, which
is markup for a called instruction. All `<Text>` values are ordinary region names
(`Initialisation`, `Usage`, `HeatersLogic`, `HysteresisCalculation`, `AutoMode`, `ManualMode`,
`Output`, `WorkCounting`). Nothing in these files was treated as instruction.

---

## G8 in detail

### What the function does, and why it exists

```csharp
private static string StripNonDeterministic(string xml)   // BlockExporter.cs:99-114
{
    try
    {
        var doc = XDocument.Parse(xml);
        doc.Root?.Elements().Where(e => e.Name.LocalName == "DocumentInfo").Remove();
        return doc.ToString();
    }
    catch { return xml; }
}
```

Its documented purpose (`BlockExporter.cs:93-98`) is narrow: `<DocumentInfo>` carries a
`<Created>` timestamp that changes on every export, which would make `get_block_content`
non-idempotent and invalidate the write-safety state hash on every preview/apply.

That purpose is real. Confirmed in `WriteSafetyService.cs:52` —
`var currentStateHash = HashText(currentState)` — where `currentState` is the worker's read
result verbatim. So the exported bytes *are* the hash input, and a moving timestamp really
would break every token.

### The problem: a broad transform serving a narrow goal

`XDocument.Parse(...).ToString()` does far more than remove one element. Three effects, all
certain from the .NET API contract, independent of anything TIA does:

1. **The XML declaration is dropped.** `XDocument.ToString()` is `ToString(SaveOptions.None)`,
   which never writes `<?xml … ?>`. All three raw exports in `priv/tia_exports/` carry
   `<?xml version="1.0" encoding="utf-8"?>`; the captured bundle does not.
2. **The document is re-indented.** `SaveOptions.None` means `Indent = true`. Whitespace
   between elements is regenerated rather than preserved. (Mixed-content elements such as
   `<Text UId="23">Initialisation</Text>` are left alone by the indenting writer, so the SCL
   token stream survives — but the formatting of everything else does not.)
3. **All of Siemens' serialization choices are replaced** by `XmlWriter`'s: attribute quoting,
   self-closing forms, namespace declaration placement, entity forms.

Today that is harmless, because the output is only ever *read*. **Option A promotes this
function to a write-path transform** — the stripped text becomes the document handed to
`PlcBlockComposition.Import`. A transformation written for display is now shaping what gets
written into the PLC project.

### Why this is a live risk and not a theoretical one

The only `Blocks.Import` call in this codebase with evidence of success is `create_block`,
and `BlockSourceGenerator` emits `<?xml version="1.0" encoding="utf-8"?>` on **every**
template (`BlockSourceGenerator.cs:31, 57, 84, 111`). So every import known to work here
supplies a prolog. Option A would supply XML without one. We would be moving from a form
proven to work to a form never tested.

The BOM question resolves itself: `BlockImportStager.cs:39` writes with `Encoding.UTF8`,
whose .NET instance emits a BOM, matching the raw exports. `BlockMutationService.cs:235`
does the same on the create path that works. Nothing to fix there.

So the delta between "known-working import" and "Option A import" is exactly:
**missing prolog + re-indentation + `DocumentInfo` removed.**

### ANSWERED: `ExportOptions.None` **does** emit `<DocumentInfo>`

Settled from the commit that introduced the function, `c53e6f4`:

> Strip `<DocumentInfo>` (which carries a `<Created>` timestamp) so get_block_content output
> is deterministic across calls — **previously the timestamp made the write-safety state hash
> non-deterministic, causing every preview→apply sequence to fail with "current state no
> longer matches"**.

That is a *reproduced runtime failure* on this exact V21 install, not a precaution. Every
preview→apply failed, which can only happen if `Block.Export(…, ExportOptions.None)` emitted a
changing `<Created>` timestamp. `StripNonDeterministic` is load-bearing.

**Option 1 (delete the function) is dead.** Take **option 2** — surgical textual removal of the
`<DocumentInfo>…</DocumentInfo>` span — which keeps deterministic reads while leaving the
prolog, indentation and every other byte untouched. Option 3 remains the better long-term
shape if this area is revisited.

**Corollary — fixture provenance.** The three raw exports in `priv/tia_exports/` contain no
`<DocumentInfo>`, so they were **not** produced by Openness `Export()`; they came from the TIA
Portal UI or an equivalent path. They remain valid references for *block structure* — G1, G3,
G4, G6 and G7 are all claims about the `SW.Blocks.*` subtree and stand unaffected — but they
are **not** authoritative for document-level framing (prolog, `DocumentInfo`, BOM). Note also
that a UI export may include default-valued attributes that `ExportOptions.None` omits, so
"the fixture contains X" does not prove `Export(None)` emits X. This does not weaken the
template work: for *import* it is harmless to emit more than the minimum, and the "absent from
the fixture" findings (no `Header*` on OB/GlobalDB) get *stronger* under that reading, not
weaker.

### Options

| # | Approach | Payload fidelity | Reads byte-idempotent | Verdict |
|---|---|---|---|---|
| 1 | Delete the function | exact | no | **RULED OUT** — `DocumentInfo` is emitted |
| 2 | Surgical textual removal of the `<DocumentInfo>…</DocumentInfo>` span; touch nothing else | near-exact | yes | **RECOMMENDED** |
| 3 | Normalize at hash time instead: payload stays byte-exact, `WriteSafetyService` strips volatile fields before hashing | exact | no | Cleaner long-term shape |
| 4 | Keep the round-trip, re-emit `XDeclaration` with `SaveOptions.DisableFormatting` | good | yes | Still a full reserialization |

**Decision: option 2.** Smallest behavioural delta, preserves both required properties, and
removes the prolog/indentation collateral entirely. Option 3 is the better long-term shape and
is worth doing if this area is touched again: determinism is a *hashing* requirement, and
satisfying it by rewriting the payload is what put a display transform on a write path in the
first place.

### Correction to an earlier framing

I previously said re-emitting the prolog "invalidates every outstanding safety token" and
implied that was a significant cost. Tokens expire after 10 minutes
(`CLAUDE.md`, `WriteSafetyService`), so the real cost is a ≤10-minute window at deploy time
during which in-flight preview→apply pairs must be re-previewed. That is a deployment note,
not a design constraint — it should not weigh against any of the four options above.

---

## Why three "fix" commits missed this

`BlockImportBundleParserTests.cs:186` asserts multi-document parsing with a hand-written
literal:

```csharp
const string content = "--- FILE: Main.xml ---\n<Main />\n--- FILE: Types.xml ---\n<Types />";
//                                                      ^ the newline the exporter never emits
```

The test encodes the format the parser *wants*. The exporter encodes the format it
*happens to write*. Both were tested in isolation against different assumptions of the same
contract, and **nothing tested them against each other**. `ab12c78`, `0ceeccc` and `4f16d54`
added preflight validation and postcondition verification *around* this seam without ever
crossing it — the failure occurs during parse/stage, before any content those checks would
inspect exists.

That is the systemic lesson, and it drives the test strategy below.

---

## Proposed solution

### Fix 1 — make the bundle format one owned, round-trip-tested contract (addresses D1, D6)

Extract the format into a single type that owns **both** directions, e.g.
`BlockBundleFormat.Compose(IReadOnlyList<(string Name, string Content)>)` and
`BlockBundleFormat.Parse(string fallbackName, string raw)`, in one file. `BlockExporter`
must build its output through `Compose` — it may not concatenate markers itself.

`Compose` guarantees the invariant the parser assumes: every marker after the first is
preceded by exactly one `\n` (normalise by ensuring each document's content ends with a
newline before appending the next marker).

**Keep the parser's `^` anchor.** The report offered relaxing the regex as option (b); that
should be rejected. A block's SCL or comment text can legitimately contain
`--- FILE: … ---` mid-line, and an unanchored matcher would silently split a document on
user content. Anchoring keeps that case an error rather than a corruption. Fixing the
producer is the correct side.

`Compose` must additionally reject (or escape) any document whose content contains a line
that would parse as a delimiter — otherwise the round trip is ambiguous by construction.

Also reject the `(unavailable)` placeholder as a document name on the import side, and make
a failed Simatic ML export a hard error on any path whose output can feed a write (D6).

### Fix 2 — pick one import format and use it end to end (addresses D2, D3)

This is the decision that actually unblocks `update_block_logic`. Three options:

**Option A — Simatic ML round trip (APPROVED — this is the implementation path).**
`update_block_logic` selects the `.xml` document from the bundle, writes it to a temp file,
and calls `group.Blocks.Import(new FileInfo(path), ImportOptions.Override)` — the exact call
`create_block` already uses successfully today. Verification re-exports via
`Block.Export(…, ExportOptions.None)` and compares.

- *Pro:* uses a code path proven to work in this codebase; eliminates the SD base-name
  problem entirely; `get_block_content`'s output stays unchanged, so the write-safety state
  hash and the existing caller contract are preserved.
- *Con:* the `.s7dcl` half of the bundle becomes read-only context. An agent that edits the
  `.s7dcl` and submits it would have that edit silently ignored — **unacceptable as-is**.
  Mitigation: the preflight already fetches current block content to build the safety token
  (`BatchWorkerInvoker.cs:16`), so compare the submitted non-authoritative documents against
  the current export and **reject** the write if they differ, with a message naming the
  authoritative document. Update the tool description to state which document is editable.
- *Con:* `Block.Export()` requires a consistent block, so a freshly created or inconsistent
  block cannot be updated this way (the exporter already treats the XML section as
  best-effort for this reason). This must become an explicit, actionable error rather than a
  degraded bundle.

**Option B — SIMATIC SD round trip.**
Drop the `.xml` from the bundle, make `PrimaryDocumentName` the extension-less block base
name, stage `<Block>.s7dcl` (+ `.s7res`), and call
`ImportFromDocuments(dir, "<Block>", ImportDocumentOptions.Override)`.

- *Pro:* matches the declared schema wording; a genuinely human-editable surface; works on
  blocks `Export()` refuses.
- *Con:* SD document coverage varies by block type and language; the `.xml` was presumably
  added because SD alone was insufficient for some blocks. Changing `get_block_content`'s
  output also invalidates every previously issued safety token and changes the read contract.

**Option C — keep both, tagged by role.**
Mark each document in the bundle with its role (ML primary / SD set) and dispatch to the
matching importer. Most flexible; most machinery; defers the decision rather than making it.

**Option A is approved.** The non-authoritative-document guard is a hard requirement of that
option, not a follow-up: without it, an edit to the `.s7dcl` half is silently discarded.

Under Option A, Fix 3 below still applies to `ExportAsDocuments` usage that remains.

### Fix 3 — correct the verification name semantics (addresses D3)

`ExportAsDocuments` must be called with the **resolved base name**
(`ResolvedBlockTarget.DocumentName`), and success must be asserted from
`DocumentExportResult.ExportedDocuments` (non-empty, non-zero-length) rather than from
`File.Exists` on a hand-built path. Reverting the substitution introduced by `71b6687` is
part of this; the now-unused `resolvedTargetDocumentName` parameter is the visible symptom.

### Fix 4 — SCL compile unit (addresses D4, informed by G1/G2/G3/G4/G5)

The fixtures make this concrete. The generated SCL compile unit becomes:

```xml
<SW.Blocks.CompileUnit ID="3" CompositionName="CompileUnits">
  <AttributeList>
    <NetworkSource />
    <ProgrammingLanguage>SCL</ProgrammingLanguage>
  </AttributeList>
</SW.Blocks.CompileUnit>
```

Justification, point by point:

- **Empty `<NetworkSource />`** rather than an empty or token-populated `<StructuredText>`.
  G2 proves this exact shape round-trips through TIA — it appears five times in the real LAD
  export. It also sidesteps G1 entirely: with no `StructuredText` element there is no
  namespace to get wrong. This is the smallest change with direct fixture evidence behind it.
- **No `ObjectList`** on the compile unit (G5) — the real SCL compile unit omits it.
- If Siemens nonetheless rejects an empty body *specifically for SCL*, the fallback is a
  minimal token stream taken from `nStageHeater.xml` — `<StructuredText xmlns="…/v4">`
  with a `Token`/`Blank`/`Text`/`NewLine` sequence, **v4** (G1), every element carrying a
  `UId` (G3). Do not hand-author this from the error message's element list; copy the shape
  from the fixture.
- **Correct the ID allocation** (G4) so IDs increase monotonically in document order across
  the whole document, and emit them as hex. Today's FB template emits `1, 3, 4, 5, 2`. This
  is the object Siemens names in the Finding #1 error and must not be left unaddressed.
- **`HasSclSourceBody` must be inverted.** As written (`BlockSourceValidator.cs:61-72`) it
  requires a non-whitespace text node — the exact shape G3 shows is illegal — so it would
  reject the corrected template. Replace it with the structural assertion actually needed: a
  `CompileUnit` exists whose `AttributeList` declares `ProgrammingLanguage` matching the
  block's, and whose `NetworkSource` is either empty or contains exactly one
  `StructuredText`. Add a positive assertion that no `StructuredText` element contains a
  non-whitespace **text node**, which is the defect class D4 belongs to.
- **Update the STL path too** (`BlockSourceGenerator.cs:167`): it emits the same stale v3
  namespace. Under this fix it becomes an empty `<NetworkSource />` as well.

### Fix 5 — GlobalDB (addresses D5, informed by G6)

- Add `<ProgrammingLanguage>DB</ProgrammingLanguage>` — confirmed by `InputValues_DB.xml`.
- **Replace `<Optimized>true</Optimized>` with `<MemoryLayout>Optimized</MemoryLayout>`**
  (`BlockSourceGenerator.cs:124`). The real export has no `Optimized` element; the first
  draft flagged this as a guess and G6 confirms the guess was wrong.
- Drop the four `Header*` elements from the GlobalDB template — the real export omits them.
- Make the default in `PrepareCreate` type-aware: `GLOBALDB` → `"DB"`, otherwise `"LAD"`.
- Make `ValidateTypeLanguage` accept absent/`"DB"` for `GLOBALDB` and reject `LAD`/`FBD`/etc.
  — the inverse of today's rule.
- Leave `Number`, `MemoryReserve`, `DBAccessibleFromOPCUA` and `DBAccessibleFromWebserver`
  out of the create template (TIA assigns them); the fixture documents their canonical form
  should they later be needed.
- The public parameter documentation ("FB/FC/OB only") stays correct once the internal
  placeholder is removed.

---

## Test strategy

All of the following run in `TiaMcpServer.Tests` with no Siemens dependency (the test project
links worker source via `<Compile Include>`).

1. **Round-trip property test — the one that would have caught D1.**
   `Parse(Compose(docs)) == docs` for 1, 2 and 3 documents, with content that does and does
   not end in a newline, and with both LF and CRLF. This must fail against today's exporter.
2. **Golden bundle fixture — captured and committed.**
   `TiaMcpServer.Tests/Fixtures/get_block_content.ob-lad.bundle.txt`. Assert it parses into
   **2** documents named `Main.xml` and `Main.s7dcl`. Against today's parser this yields 1 and
   fails — it is a genuine RED test, verified. Load it as bytes, not through any newline-
   normalising helper, or the defect disappears from the fixture.
3. **Negative delimiter tests.** A marker not at line start must throw
   `ValidationError`, not be silently absorbed. A document body containing a delimiter-shaped
   line must be rejected by `Compose`.
4. **Generator golden fixtures.** FC/FB/OB × LAD/FBD/STL/SCL, plus GlobalDB, each compared
   against a real TIA export rather than against hand-written expectations. Assert
   specifically: no raw text node inside any `StructuredText` (D4/G3); `MemoryLayout`, not
   `Optimized` (G6); `ProgrammingLanguage` present for every block type including GlobalDB
   (D5/G6); namespace v4 wherever `StructuredText` is emitted (G1); IDs monotonic in document
   order (G4). `priv/tia_exports/` supplies the reference for FB/LAD, FB/SCL and GlobalDB;
   FC, OB, FBD and STL references are still missing.
5. **Verification name test.** Assert `ExportAsDocuments` is invoked with the resolved base
   name and that success is derived from `ExportedDocuments` (fake the delegate — the seam
   already exists at `BlockExporterVerification.cs:12`).
6. **Live E2E checklist (manual, requires TIA Portal).** Round-3 matrix: `get_block_content`
   → unmodified `update_block_logic` for a LAD/OB, an SCL/FB and a DB; `create_block` for
   SCL and GlobalDB; `compile_check` clean after each.

---

## Sequencing

| Step | Scope | Blocks |
|---|---|---|
| 1 | Fix 1 + test 1/3 (pure, no Siemens) | — |
| 2 | Fix 2 (Option A) + Fix 3 + test 2/5 | step 1 |
| 3 | Fix 5 (GlobalDB) + test 4 | independent of 1-2 |
| 4 | Fix 4 (SCL + STL namespace + ID allocation) + test 4 | independent of 1-2 |
| 5 | Live E2E (test 6) | steps 1-4 |

Steps 3 and 4 are independent of 1-2 and of each other, and both are now unblocked — the
fixtures supplied everything they were waiting on.

---

## Open questions

1. ~~**Format decision (Fix 2).**~~ **Resolved — Option A approved.**
2. ~~**Bundle fixture missing.**~~ **Resolved — captured, committed, and confirmed to
   reproduce D1.**
3. ~~**G8 — does `ExportOptions.None` emit `DocumentInfo`?**~~ **Resolved — yes.** Option 2
   (surgical removal) chosen; the prolog question is moot once the document is no longer
   reserialized. See "G8 in detail".
4. **Fixture gaps for test 4.** No reference export yet for FC, OB, FBD or STL. The
   corresponding golden tests will be written against the shapes we can justify and marked as
   provisional until a reference lands.
5. **`Number` / `MemoryReserve` on created blocks.** The create templates omit them and let
   TIA assign. Confirm that is the desired behaviour rather than exposing them as tool
   parameters.
6. **Out of scope, flagged from the report.** No `delete_network_device` operation exists, so
   `MCP_Test_Device` and `MCP_Test_CPU` residue in the test project can only be removed via
   the TIA UI. Separate decision on whether to add that operation.
