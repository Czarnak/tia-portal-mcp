# Design: UDT and DB external-source support (Roadmap Phases 1–2)

Date: 2026-07-26
Status: approved, not yet implemented
Supersedes nothing. Refines `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` Phases 1 and 2, and corrects
two factual claims in it (see [Roadmap corrections](#roadmap-corrections)).

## Goal

Add PLC data type (UDT) read/write support to this MCP — which has none today — using Siemens'
native external-source pipeline, then extend the same pipeline to global data blocks as an opt-in
format. Both phases keep SimaticML reachable as an explicit choice and change no existing default.

## Scope

**In scope**

- Phase 1: `get_type_content` and `update_type_content` batch operations for `PlcType` objects.
- Phase 2: a `format` field on the existing `get_block_content` / `update_block_logic` operations,
  honored for `GlobalDB` only.
- A committed live-test harness per phase, run against real TIA Portal V21.

**Out of scope**

- `create_type`, `delete_type`, `create_type_group`, `delete_type_group`.
- SCL (roadmap Phase 3) and LAD/SimaticSD (roadmap Phase 4).
- `format` reaching FB/FC/OB blocks.
- Flipping any default format (roadmap Phase 5 owns that decision exclusively).
- Fixing the `browse_project_tree` → Types dead-end described in
  [Known adjacent gap](#known-adjacent-gap). Tracked separately.

## Findings that drive the design

### Test wiring dictates file granularity

`TiaMcpServer.Tests` links worker source files individually via `<Compile Include>`, and only links
files free of `Siemens.Engineering` types (`BlockImportRouting`, `BlockSourceGenerator`,
`BlockWritePreflight`, `BlockXmlSanitizer`, `BlockImportBundleParser`, …). Siemens-touching files —
`BlockExporter`, `BlockTargetResolver`, `BlockImporter` — are deliberately not linked and carry
**zero unit coverage**.

Consequence: every new component splits into a Siemens-free half that is unit-tested and a thin
Siemens-calling shell that is not. No logic that could live on the free side may live in the shell.
The legacy live acceptance procedure is the only coverage the shells will ever have, which is why it gates the phase.

| | Siemens-free — linked into tests | Siemens-touching — legacy live acceptance procedure only |
|---|---|---|
| Phase 1 | `PlcTypeAddress`, `SourceFormatNames`, `PlcTypeSourcePreflight`, `SourceTextEncoding` | `PlcTypeTargetResolver`, `PlcTypeExporter`, `PlcTypeImporter`, `ExternalSourceScope` |
| Phase 2 | `DbSourceOffsetColumn`, catalog and request changes | `BlockExporter` source branch, `BlockImporter` source route |

### `ExternalSources.CreateFromFile` mutates the project tree

It creates a visible `PlcExternalSource` node under the PLC's "External source files" folder. Any
import through this pipeline must delete that node afterwards, and the postcondition verifier must
assert none remains. This is the single largest correctness hazard in Phase 1.

### Encoding is a first-class concern

Siemens writes these files as UTF-8 **with BOM** and CRLF line endings. Export strips the BOM
(it is noise inside a JSON string payload) and preserves CRLF; import re-emits UTF-8 with BOM and
CRLF.

### Non-optimized DBs carry a byte-offset column

Confirmed by the project owner: both optimized and non-optimized DBs export a
`BEGIN` / `END_DATA_BLOCK` section, but a non-optimized DB additionally emits a per-variable byte
offset column. A client that adds, removes, or reorders a member therefore leaves every subsequent
offset stale. This is Phase 2's equivalent of Phase 1's user-group hazard and is resolved by live
test L2.4 below.

## Architecture

### Phase 1 — UDT

**`TiaMcpServer.Contracts` (netstandard2.0)**

| File | Responsibility |
|---|---|
| `PlcTypeAddress.cs` | Parses `TypeName`, `PLC/TypeName`, `PLC/Types/…/TypeName`, `PLC/Units/<unit>/Types/…/TypeName`. Mirrors `BlockAddress`'s shape (`PlcName`, `UnitName`, `FolderPath`, `TypeName`, `IsDeterministic`) and accepts exactly the paths `ProjectTreeWalker` prints. |
| `SourceFormatNames.cs` | Allowed values `source` and `xml`. `TryNormalize` follows `CrossReferenceFilterNames` so an invalid format is rejected **before the session binds**. It exposes no single default — the default is per-operation, chosen by the caller of `TryNormalize` (see below). |
| `WorkerRequest.cs` | Adds `TypePath`, `SourceContent`, `Format`, each documenting which operations forward it, per the file's existing convention. |

`format` values are object-kind-agnostic on purpose: `source` means "Siemens' external-source text
for whatever this object is" — `.udt` for a `PlcType`, `.db` for a `GlobalDB`, and `.scl` later in
roadmap Phase 3. The extension is derived from the resolved object, never from the caller.

**The two phases default `format` differently, deliberately.** The type operations default to
`source`, because they are net-new surface with no existing callers and no behavior to preserve.
The block operations default to `xml`, because they have production callers whose payloads must not
change mid-roadmap. This is not an inconsistency to be reconciled later: roadmap Phase 5 is the
single place block defaults ever flip, and it flips them for every block language at once.

**`TiaMcpServer.OpennessWorker` (net48)**

| File | Responsibility |
|---|---|
| `PlcTypeTargetResolver.cs` | Resolves a `PlcTypeAddress` to `(PlcTypeGroup group, PlcType? type, string documentName)`. Deterministic path walk over `plcSoftware.TypeGroup` and `unit.TypeGroup`, plus the same fuzzy-match-with-ambiguity-error fallback `BlockTargetResolver` uses. |
| `PlcTypeExporter.cs` | `source`: `ExternalSourceGroup.GenerateSource(new[]{ type }, tempFile)`. `xml`: `type.Export(...)` then `BlockXmlSanitizer.RemoveDocumentInfo`. Returns **raw text with no bundle envelope** — a single document has nothing to delimit. |
| `ExternalSourceScope.cs` | `IDisposable` owning the temp file and the `PlcExternalSource` node, deleting the project node in `finally`. Phase 2 reuses this unchanged. |
| `PlcTypeImporter.cs` | `source`: scope → `CreateFromFile` → `GenerateBlocksFromSource` against the resolved type group. `xml`: `group.Types.Import(..., ImportOptions.Override)`. |
| `PlcTypeSourcePreflight.cs` | Extracts the declared object name from `TYPE "Name"` (source) or the XML `<Name>` element. Siemens-free, unit-tested. |
| `SourceTextEncoding.cs` | Strips the UTF-8 BOM on export and re-emits BOM + CRLF on import. Siemens-free, unit-tested. Shared with Phase 2. |
| `PlcTypePostconditionVerifier.cs` | Re-exports and compiles after the write, reusing `BlockPostconditionEvidence`. |
| `Program.cs` | Two new dispatch cases: `get_type_content`, `update_type_content`. |

**`TiaMcpServer` host (net8.0)**

- `BatchOperationRequest`: `TypePath`, `SourceContent`, `Format` with `[Description]` attributes.
- `BatchOperationCatalog.BuildSpecs`:
  - `get_type_content` — Read, required `["typePath"]`, optional `["format"]`
  - `update_type_content` — Write, required `["typePath", "sourceContent"]`, optional `["format"]`
- `BatchSafetySnapshot.DescribeOperation`: `update_type_content` → `"Update PLC data type 'X'."`
- `BatchWorkerInvoker`: maps both operations; the per-item current-state reading for
  `update_type_content` is the type's current exported source in the requested format.
- `OpennessWorkerClient`: one method per operation.
- `BatchPayloadBudget`: `get_type_content` registered as a read whose payload is budgeted.

No new `[McpServerTool]`. The server's tool count stays at 10.

### Write semantics — strict, not upsert

`GenerateBlocksFromSource` natively creates a type it does not recognize. `update_type_content`
refuses to rely on that. Its preflight requires **both**:

1. A type already exists at `typePath`.
2. The declared name inside `sourceContent` equals the target type's name.

Either failure is an error naming both values, and the project is left unchanged. A typo in
`typePath` can therefore never silently create a stray type.

### Safety model

`update_type_content` is a data write and goes through `preview_write_batch` / `apply_write_batch`
like every other data write. Because the token's current-state reading is the type's exported
source, editing that type inside TIA Portal between preview and apply invalidates the token.

Dependent blocks are reported by the postcondition verifier's compile step rather than by
pre-counting cross-references at preview time: compiling reports what actually broke, and costs
nothing extra beyond the verification already performed for block writes.

### Phase 2 — DB

`format` becomes an optional field on `get_block_content` and `update_block_logic`. Default remains
`xml`, so every existing caller sees byte-identical behavior. `source` is honored for `GlobalDB`
only; requesting it for an instance DB, array DB, or any FB/FC/OB returns an explicit error naming
the unsupported combination.

`BlockExporter` and `BlockImporter` gain a source branch that reuses `ExternalSourceScope` and
`PlcTypeSourcePreflight` unchanged — the preflight parses `DATA_BLOCK "Name"` exactly as it parses
`TYPE "Name"`.

**Byte-offset column handling.** Default behavior: export passes the generated `.db` through
verbatim, offsets included, and import passes the caller's text through verbatim. A new Siemens-free
`DbSourceOffsetColumn.cs` detects whether the submitted source carries an offset column and, if it
does, records a warning that offsets are only valid for the member layout they were generated from.
The design assigned the final decision on this default to historical live case L2.4; its
contingency is defined with the case.

## Historical live gates

At the time of this design, both procedures were committed PowerShell scripts that piped
newline-delimited JSON directly into the built `TiaMcpServer.OpennessWorker.exe` against an open
project. They required no plugin install and were rerunnable, but they were later removed during
repository cleanup. Their recorded results remain historical evidence; they do not provide a
current regression gate and did not exercise the MCP layer above the worker.

### `<removed legacy UDT acceptance harness>` — gates the start of Phase 2

| ID | Check | Why it matters |
|---|---|---|
| L1.1 | Export a type at the **Types root** and one in a **nested type folder** | `GenerateBlocksFromSource` overloads target `PlcTypeUserGroup`; the root `PlcTypeGroup` may not be accepted. Most likely point of total failure. |
| L1.2 | Unchanged round-trip re-exports byte-identically | Proves the pipeline is lossless before any edit is trusted. |
| L1.3 | Mutate a member's initial value, import, re-export, confirm the change | Proves writes actually apply. |
| L1.4 | Assert no residual `PlcExternalSource` node | `ExternalSourceScope` cleanup correctness. Second most likely point of total failure. |
| L1.5 | Name mismatch and nonexistent-type calls both rejected, project unchanged | Proves strict preflight. |
| L1.6 | `format: "xml"` round-trip | Proves the fallback the roadmap requires stays reachable. |
| L1.7 | Compile clean, original restored | Leaves the test project as found. |

L1.1 and L1.4 are blocking: if either fails, Phase 2 does not start and the approach is
reconsidered.

### `<removed legacy DB acceptance harness>` — gates Phase 2 completion

Mirrors L1.1–L1.7 for a `GlobalDB`, plus:

| ID | Check | Contingency if it fails |
|---|---|---|
| L2.1 | `format: "xml"` remains the default — an unmodified `get_block_content` call returns exactly what it returns today | Blocking. A regression here breaks existing callers. |
| L2.2 | Optimized DB round-trips losslessly | Blocking for Phase 2. |
| L2.3 | Non-optimized DB round-trips losslessly with its offset column intact | Blocking for non-optimized support; optimized-only ships if it fails, with the limitation documented. |
| L2.4 | Submit a non-optimized DB source with a member **added** and therefore stale offsets | If TIA rejects it, the preflight is upgraded from a warning to a hard error that tells the caller to remove the offset column. If TIA accepts it and recomputes, the preflight strips the offset column on import and the warning is dropped. Either outcome is implementable; the test decides which. |
| L2.5 | Instance DB, array DB, and an FB with `format: "source"` are each rejected with a clear error | Proves scoping. |

## Error handling

| Condition | Behavior |
|---|---|
| Unrecognized `format` | Rejected by `SourceFormatNames.TryNormalize` before the session binds, listing valid values. |
| Type or block not found | Error naming the path and any near matches, following `BlockTargetResolver`'s existing ambiguity message. |
| Declared name ≠ target name | Error quoting both names. Project unchanged. |
| Target does not exist on update | Error stating the type must be created in TIA Portal first. Project unchanged. |
| `GenerateBlocksFromSource` reports errors | Surfaced verbatim; the external source node is still deleted, and the postcondition verifier reports the project's resulting state rather than claiming success. |
| `format: "source"` on an unsupported object kind | Error naming the object kind and the formats valid for it. |
| Residual `PlcExternalSource` after a write | Reported as a postcondition failure, not swallowed. |

## Testing

**Unit (xunit, `TiaMcpServer.Tests`)** — `PlcTypeAddress` parsing across all four path shapes plus
malformed input; `SourceFormatNames` normalization and rejection; `PlcTypeSourcePreflight` name
extraction from `.udt`, `.db`, and XML including malformed sources; `SourceTextEncoding` BOM and
CRLF handling; `DbSourceOffsetColumn` detection; catalog validation for both new operations
including required/optional/inapplicable-field cases; `BatchSafetySnapshot.DescribeOperation`
coverage for `update_type_content`.

Fixtures come from `priv/tia_exports/` — `AnalogInputSettings.udt`, `AnalogInputSettings.xml`,
`Simulation_DB.db`, `InputValues_DB.xml`. These are real V21 exports, so the parsers are tested
against genuine Siemens output rather than hand-written approximations. Repo policy is that Siemens
DLLs are never committed; these are text exports and carry no such restriction, but they are copied
into `TiaMcpServer.Tests/Fixtures/` rather than referenced from `priv/`.

**Integration** — `TiaMcpServer.FakeWorker` scripts scripted responses for both new operations so
the host-side batch path is covered end to end without TIA Portal.

**Historical live evidence** — the two removed procedures above were the only runtime coverage for
`PlcTypeTargetResolver`, `PlcTypeExporter`, `PlcTypeImporter`, and `ExternalSourceScope` at the time
of this design. They are not a current regression gate.

The repo's 80% coverage threshold is measured over compiled test-project sources; the Siemens-
touching shells are not part of that compilation and neither raise nor lower the number.

## Roadmap corrections

Both should be applied to `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` when this work lands.

1. **`.udt` and `.s7dcl` are different formats from different pipelines**, not "informal/decoded
   names for the same declaration syntax." Comparing `AnalogInputSettings.udt` against
   `AnalogInputSettings.s7dcl` in `priv/tia_exports/`: the `.udt` opens `TYPE "AnalogInputSettings"`
   with a `VERSION` line and keeps comments inline as `//`; the `.s7dcl` opens a bare `TYPE` with
   the name on the STRUCT line, encodes attributes as `{ S7_MLC := "MLC_aC" }`, and externalizes
   every comment to a companion `.s7res`. Similar byte counts, unrelated syntaxes. `.udt` is the
   better client format: one file, comments in place, no ID indirection.

2. **Phase 2's dependency on Phase 1 is `ExternalSourceScope`, not "shared struct-parsing
   groundwork."** Roadmap Phase 0 established that Openness exposes a native external-source
   pipeline, so no struct parsing happens in either phase. What Phase 2 genuinely reuses is the
   temp-file and `PlcExternalSource` lifecycle helper, plus the declared-name preflight.

## Known adjacent gap

`ProjectTreeWalker` emits `PLC_1/Types/<name>` nodes from `browse_project_tree`, but
`BlockAddress.Parse` accepts only `Blocks` and `Units` segments and throws on any Types path. The
tree therefore advertises objects no operation can read. Phase 1 does not close this, because the
chosen design gives types their own `typePath` field rather than teaching `get_block_content` to
accept them. Closing it would mean adding a path sniff to `get_block_content` that returns an error
naming `get_type_content`. Tracked as a standalone item, not part of either phase.
