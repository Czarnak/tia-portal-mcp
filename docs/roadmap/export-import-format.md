# Export/Import Format Roadmap (2026-07-26)

Forward-looking note, not an implemented plan. Captures the intended direction for the file
formats used by block export/import so the rationale isn't lost between now and whenever this is
scheduled.

## Current state

Block export (`get_block_content`) and import (`update_block_logic`) are the only import/export
operations this MCP exposes (see `BlockExporter.cs`, `BlockImporter.cs`,
`BlockImportRouting.cs`). Today every export always tries **SimaticML** (`.xml`, via
`Block.Export`/`Group.Blocks.Import`) first and treats it as the authoritative document; the
**SimaticSD** document set (`.s7dcl`/`.s7res`, via `ExportAsDocuments`/`ImportFromDocuments`) is
bundled alongside as read-mostly context, and only takes over as the applied format when a bundle
has no `.xml` document at all (`BlockImportRouting.SelectRoute`).

## Problem

SimaticML XML is verbose: full interface schema, namespaces, and boilerplate wrapper elements
repeated per block, on every read and every write round-trip. For an LLM-driven client this is
token-heavy relative to the actual logic content, and it dominates the payload size of
`get_block_content`/`update_block_logic` for blocks that don't need the full schema to round-trip
correctly.

## Goal

Keep SimaticML **available** as an explicit opt-in fallback everywhere. But stop making it the
*default* everywhere. Default to a lighter, block-type-appropriate plain-text format where one
exists and round-trips losslessly:

- **SimaticSD** (`.s7dcl` for interface + logic, `.s7res` for multilingual text resources) as the
  default for blocks/situations that don't strictly need the XML interface schema — **including
  LAD**, once verified (see Phase 4 below). FBD is presumed similar to LAD but untested; GRAPH is
  excluded (see Sample export analysis).
- **`.scl`** for SCL-language blocks (FB/FC/OB with `language=SCL`).
- **`.db`** for data blocks.
- **`.udt`** for PLC data types (user-defined types).

Graphical languages are the one case where SimaticML may stay mandatory rather than just a
fallback, and even there it's language-specific: LAD has real in-sample evidence of a working text
rendering, so whether it can stay mandatory-XML or move to SimaticSD-default depends on whether
that text form round-trips on *import*, not on whether one exists. GRAPH has no such evidence and
should be treated as XML-only until proven otherwise, independent of what Phase 4 finds for LAD.

## Sample export analysis (`priv/tia_exports/`, 2026-07-26)

Fourteen real exports from TIA Portal V21 are collected for comparison, including matched
SimaticML/SimaticSD pairs for the same block/type so format overhead can be measured directly
rather than estimated:

| Object | SimaticML (`.xml`) | SimaticSD | Ratio |
|---|---|---|---|
| `Inputs_FB` (**LAD**) | 96,665 B | `.s7dcl` 9,926 B + `.s7res` 885 B = 10,811 B | ~8.9x |
| `nStageHeater` (SCL, XML) vs `AnalogInput` (SCL, comparable complexity, native source) | 68,965 B | `.scl` 3,681 B | ~19x (different blocks, same order of magnitude both ways) |
| `InputValues_DB` (GlobalDB) | 2,969 B | `.s7dcl` 1,312 B | ~2.3x |
| `AnalogInputSettings` (UDT) | 2,440 B | `.s7dcl` 509 B + `.s7res` 74 B = 583 B | ~4.2x |
| `StateMachine` (**GRAPH**) | 40,672 B | no working decode — see below | n/a |

Observations:

- **`.s7dcl` and `.s7res` are two different kinds of content, not "a format and its friend."**
  `Inputs_FB.s7dcl` contains the full interface *and* the logic body, as an actual textual
  network DSL: `NETWORK ... RUNG wire#powerrail Contact("DI_EmergencyStop") ... END_RUNG
  END_NETWORK`. `Inputs_FB.s7res` is a completely separate thing — a flat list of multilingual
  text resources (`MultiLingualTexts: - id: MLC_35C \n en-US: Emergency stop`), i.e. the comment/
  title strings referenced by ID from the `.s7dcl` file. Any SimaticSD-based reader needs both
  files to render fully commented logic, but `.s7dcl` alone already carries the structural truth.
- **This is real, working evidence that LAD round-trips through text, not a hypothesis.**
  `Inputs_FB.s7dcl`'s `RUNG`/`Contact`/`END_RUNG` syntax is a genuine textual rendering of ladder
  logic, at roughly 1/9th the byte cost of the XML. This is materially stronger evidence than what
  was available before this sample: the open question for the LAD/FBD phase is no longer "does a
  text form exist" but "can it be imported back, and does it survive `Openness Import` /
  `ImportFromDocuments` unmodified."
- **GRAPH is the harder case, and there is direct negative evidence for it in-sample.**
  `StateMachine.xml` is a GRAPH-language FB (S7-GRAPH sequential function chart). Its `.scl`
  companion in this sample set is not a working decode — it's a stub whose own content says so:
  `// (!) Network 1: GRAPH network parsed but rendering is deferred in v0` / `// (empty network)`.
  GRAPH sequences are a distinctly different structure from LAD rungs or FBD wire diagrams, and
  nothing in this sample demonstrates a working text rendering for it. Treat GRAPH as out of scope
  for the LAD/FBD phase until separately investigated — bundling it in would understate the risk.
- **UDT and DB samples confirm the same shape found earlier, now with a second, independent data
  point.** `AnalogInputSettings.xml`/`.s7dcl`/`.s7res` line up with the previously-added
  `AnalogInputSettings.udt` almost byte-for-byte (509 B `.s7dcl` vs. 483 B `.udt`). `.udt` and
  `.s7dcl` are different formats produced by different Openness pipelines, not two names for one
  syntax. The `.udt` (external source, `GenerateSource`) opens `TYPE "AnalogInputSettings"` with a
  `VERSION` line and keeps comments inline as `//`. The `.s7dcl` (`ExportAsDocuments`) opens a bare
  `TYPE` with the name on the STRUCT line, encodes attributes as `{ S7_MLC := "MLC_aC" }`, and
  externalizes every comment to a companion `.s7res`. Their byte counts are similar; their syntaxes
  are unrelated. `.udt` is the better client format: one file, comments in place, no ID indirection.
  `InputValues_DB.xml`/`.s7dcl` reconfirm the DB ratio from the first sample round.
- **`.scl` is not a bare logic file — it's the Siemens "external source" bundle.**
  `AnalogInput.scl` contains a full `TYPE "AnalogInputSettings" ... END_TYPE` declaration
  *followed by* the `FUNCTION_BLOCK "AnalogInput" ... END_FUNCTION_BLOCK` body, because the FB
  consumes that UDT as a parameter type. This is almost certainly Siemens' own "generate/import
  external source" feature (a distinct Openness surface from `Block.Export`/`Import`), not
  something hand-rolled — worth confirming before designing a parser, since round-tripping may be
  a matter of driving that existing API rather than writing one. This also means **UDT dependency
  resolution isn't optional for SCL** — any SCL import path will see inline type declarations and
  must handle them.
- **`.db` is plain text, but disguised as binary.** `Simulation_DB.db` is UTF-8 **with a BOM** and
  CRLF line endings; the Read tooling used for this analysis initially misidentified it as a
  binary file purely because of the BOM. Any importer/exporter for this format has to treat BOM +
  CRLF handling as a first-class concern, not an afterthought.
- **A `.scl` file routinely declares several objects of different kinds.** The 2026-07-27 samples
  make this concrete: `DamperDigital.scl` declares one `FUNCTION_BLOCK`, `AnalogInput.scl` declares
  a `TYPE` and a `FUNCTION_BLOCK`, and `DamperAnalog.scl` declares two `TYPE`s, a `DATA_BLOCK`, and
  a `FUNCTION_BLOCK` — 8,395 bytes covering four objects. This is `GenerateOptions.WithDependencies`
  behavior, and it is why reads let the caller choose multiplicity while writes accept exactly one:
  `GenerateBlocksFromSource` creates everything the file declares and has no notion of the object
  the caller addressed.

## Known constraints (found while reading the current implementation)

- **LAD has a working plain-text rendering; GRAPH demonstrably does not (yet).** A SimaticSD
  document set is already exported for graphical blocks today via `ExportAsDocuments`, but only
  ever consumed as read-only companion context in this codebase. The sample analysis above shows
  `.s7dcl` genuinely renders LAD rungs as text (`NETWORK`/`RUNG`/`Contact(...)`) — the remaining
  unknown for LAD is only whether `Group.Blocks.ImportFromDocuments` can write that text back, not
  whether a text form exists. GRAPH is a different story: the one GRAPH sample in this set has no
  working text decode at all. Until import is verified, SimaticML stays mandatory (not just
  default) for writes to graphical blocks; Phase 4 below exists to resolve this for LAD
  specifically. FBD is untested either way and should be assumed to behave like LAD until checked.
- **UDT tooling now exists.** `get_type_content`/`update_type_content` are batch catalog operations
  that read and write PLC data types. A `.udt` format is no longer meaningless for lack of a
  UDT-level tool — that prerequisite is done.
- **`BlockSourceGenerator` only emits XML today**, including an intentionally empty
  `<NetworkSource />` for SCL/STL compile units (a schema-valid placeholder, not real SCL text —
  see the block-write-format-repair note in `docs/IMPROVEMENT_LOG.md`). A `.scl`-default path
  needs real SCL text generation/parsing, not this placeholder.
- **`BlockImportRouting` already supports a no-XML bundle** (routes to `ImportFromDocuments`), but
  that path is currently only exercised as a fallback, not verified as the primary, default-driving
  route for every block type it would need to cover.

## Suggested phasing (Phase 0 partially closed; Phase 1 delivered; Phase 2 round trip delivered; 3-5 not yet scheduled)

Priority order is **UDT → DB → SCL**, not the historical order these were investigated in. Reasons
for that order, derived from the analysis above:

- **UDT first**: no UDT tooling existed in this MCP when this ordering was chosen, so it was pure
  net-new surface with no legacy behavior to preserve — now delivered via `get_type_content`/
  `update_type_content`. It's also a dependency for the other two — SCL blocks embed UDT
  declarations inline (confirmed above), and DB structs commonly reference UDTs as member types —
  so getting UDT parsing/generation right first de-risks both of the others. It's also the
  structurally simplest format (a bare struct declaration, no runtime values, no executable body).
- **DB second**: structurally the closest thing to a UDT plus one addition (initial values in a
  `BEGIN`/`END_DATA_BLOCK` block). `GlobalDB` creation already exists in this codebase
  (`BlockSourceGenerator`'s `GLOBALDB`/`DB` case), so this extends an existing path rather than
  building a new one. With the native external-source pipeline confirmed in Phase 0, neither phase
  parses a struct. What Phase 2 reuses from Phase 1 is the temp-file and `PlcExternalSource`
  lifecycle helper plus the declared-name preflight. The BOM/CRLF handling found in the sample
  needs solving here, but is self-contained.
- **SCL third**: the highest-risk and highest-payoff format among the three text-native ones. It's
  the only one with an executable statement body that must round-trip losslessly (not just a
  declaration), it directly replaces the known placeholder in `BlockSourceGenerator` (the empty
  `<NetworkSource />` workaround, see Known constraints above), and it depends on Phase 1's UDT
  handling for any block with a non-trivial interface.
- **LAD via SimaticSD, fourth, after SCL**: deliberately placed after the three text-native
  formats, not alongside them. UDT/DB/SCL are format changes to data that was already
  fully textual; LAD is different in kind — it asks whether a *graphical* language can be
  round-tripped through text at all. The sample analysis found real evidence the text rendering
  exists and is far smaller than XML (~8.9x for the one sample measured), which is why this phase
  is worth doing — but unlike Phases 1-3, the risk here is on the *write* side (does
  `ImportFromDocuments` actually apply a parsed `.s7dcl` network back to a LAD block, faithfully
  and without corruption), which can only be established by testing against real TIA Portal, not
  by reading more samples. That verification step is exactly why it comes after the lower-risk,
  already-proven-viable formats rather than before or alongside them. GRAPH is explicitly excluded
  from this phase — treat it as a separate, later investigation with its own phase if pursued.

| Phase | Scope | Depends on |
|---|---|---|
| 0 — Spike | Confirm whether these sample files were produced by Siemens' native "generate/import external source" Openness API rather than `Block.Export`/`Import`. If so, prefer driving that API over hand-rolling a `.scl`/`.db`/`.udt` parser — changes the shape of every phase below. | — |
| 1 — UDT | Add read/export/import support for PLC data types (delivered — `get_type_content`/`update_type_content`) with `.udt` as its format. | Phase 0 |
| 2 — DB | Add `.db` as a selectable/default format for data blocks, alongside the existing SimaticML path (round trip delivered and proven live, see Phase 0 below; the selectable-format work itself is Phase 5). | Phase 1 (`ExternalSourceScope` and the declared-name preflight), Phase 0 |
| 3 — SCL | Add `.scl` as a selectable format for SCL-language FB/FC/OB via `format=source`, with a `withDependencies` read option (delivered — see `docs/superpowers/specs/2026-07-27-scl-external-source-design.md`). STL deferred: it needs `.awl` and has no fixture. `BlockSourceGenerator` deliberately untouched — its empty `<NetworkSource />` is schema-valid and `create_block` + `update_block_logic` already compose. | Phase 1 (inline UDT dependencies), Phase 0 |
| 4 — LAD (SimaticSD) | Verify whether `ImportFromDocuments` can apply a real network-level change to a LAD block from an edited `.s7dcl`/`.s7res` pair; if confirmed, add SimaticSD as a selectable/default read (and, if verified, write) format for LAD. FBD only if it's confirmed to behave the same way; GRAPH explicitly out of scope. | Phase 0; independent of Phases 1-3 otherwise |
| 5 — Rollout | Add the explicit format selector on `get_block_content`/`update_block_logic`, flip defaults per block language/type now that 1-4 exist, measure real token savings, keep `xml` permanently available everywhere as an explicit override. GRAPH (and FBD/LAD if Phase 4 finds import doesn't work) never leave `xml`. | Phases 1-4 |

### Phase 0 — V21 API exposure confirmed (2026-07-26)

Static inspection of the installed V21 public API (`Siemens.Engineering.Step7.dll`,
assembly version `21.0.0.0`) confirms that Openness has a native external-source pipeline;
it is not necessary to assume or design a handwritten `.scl`/`.db`/`.udt` parser first.

- `PlcType` (UDT), `DataBlock`, and `PlcBlock` all implement
  `Siemens.Engineering.SW.ExternalSources.IGenerateSource`.
- `PlcSoftware.ExternalSourceGroup.GenerateSource(IEnumerable<IGenerateSource>, FileInfo[, GenerateOptions])`
  writes a source file for those objects.
- `ExternalSources.CreateFromFile(name, path)` creates a `PlcExternalSource`, whose
  `GenerateBlocksFromSource` overloads generate into either a `PlcBlockUserGroup` or a
  `PlcTypeUserGroup`.
- The structured fallback remains available: `PlcBlock`/`PlcType` expose `Export` and
  `ExportAsDocuments`; their respective compositions expose `Import` and
  `ImportFromDocuments`.

This proves the relevant public API surface exists for UDTs, DBs, and SCL-language blocks.
It does **not** prove that the existing samples were produced by this pipeline, nor certify
that any exact `.udt`, `.db`, or `.scl` file round-trips on a target CPU. The API accepts a
generic `FileInfo`/path rather than a type-specific extension selector.

**Phase 0 progress as of 2026-07-26 (see `docs/superpowers/plans/2026-07-26-udt-db-external-source.md`).**
The `GenerateSource → CreateFromFile → GenerateBlocksFromSource` round trip was proven live for
UDTs and global DBs by the recorded Phase 0 acceptance runs. The DB coverage includes a global DB
inside a software unit, resolved through the owning unit's `ExternalSourceGroup` rather than the
top-level one. The SCL leg was unproven at this point; see below for its closure.

A non-optimized global DB's external-source export is **identical in shape** to an optimized one.
There is no `Offset` column. The only difference is `S7_Optimized_Access := 'FALSE'` in the
header. Optimized and non-optimized global DBs therefore take **one identical code path**; no
offset parsing, no stale-offset detection, and no optimized/non-optimized branching is needed.
Both were covered by the recorded DB acceptance run.

**Phase 0 is closed.** Recorded acceptance runs prove the
`GenerateSource → CreateFromFile → GenerateBlocksFromSource` round trip for UDTs, global DBs, and
SCL-language blocks, including a block inside a software unit in each case. `GenerateOptions` is
now passed explicitly on every export path rather than relying on the two-argument overload's
undocumented default.

## Non-goals

- Removing SimaticML support entirely — it's required for graphical languages and stays available
  everywhere as an explicit choice.
- Changing the bundle envelope (`BlockBundleFormat`'s `--- FILE: name ---` delimiter scheme) —
  this roadmap is about which *documents* go in the bundle, not the bundle format itself.
