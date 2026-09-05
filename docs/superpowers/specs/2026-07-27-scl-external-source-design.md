# Design: SCL external-source support (Roadmap Phase 3)

Date: 2026-07-27
Status: approved, not yet implemented
Refines `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` Phase 3. Builds directly on
`docs/superpowers/specs/2026-07-26-udt-db-external-source-design.md` (Phases 1–2), which is
implemented and merged.

## Goal

Extend the existing `format=source` read/write path from global data blocks to SCL-language
blocks, so an LLM client can read and write a function block's interface and logic as plain SCL
text instead of ~19x larger SimaticML XML. Close the SCL leg of roadmap Phase 0 with live V21
evidence.

No existing default changes. `format` omitted still means `xml` for blocks, and
`get_block_content` / `update_block_logic` must return byte-identical results to today when the
caller does not opt in. Flipping defaults is Phase 5.

## Scope

**In scope**

- `format=source` accepted for FB / FC / OB whose programming language is SCL, on both
  `get_block_content` and `update_block_logic`, alongside the `GlobalDB` support already shipped.
- A `withDependencies` option on `get_block_content` **and** `get_type_content`, defaulting off.
- Passing `GenerateOptions` explicitly on every export path — including the two existing ones that
  currently rely on an undocumented default.
- A throwaway live spike that closes Phase 0 for SCL, followed by a committed
  `<removed legacy SCL acceptance harness>` regression gate.

**Out of scope**

- STL. TIA treats STL external sources as a different file type (`.awl`), there is no STL sample in
  `priv/tia_exports/`, and no Phase 0 evidence covers it. It gets its own follow-up.
- LAD, FBD, GRAPH. Roadmap Phase 4 covers LAD via SimaticSD; GRAPH stays XML-only.
- `create_block`. See [Why `create_block` is untouched](#why-create_block-is-untouched).
- Flipping any default. Phase 5.
- Any hand-written SCL parser or generator. This design drives Siemens' pipeline; it never
  reimplements it.

## Findings that drive the design

### The samples are multi-object, and that is the central problem

Fourteen real V21 exports live in `priv/tia_exports/`. The four `.scl` files added on 2026-07-27
establish that a single `.scl` file is not one block:

| File | Bytes | Declares |
| --- | --- | --- |
| `DamperDigital.scl` | 4,965 | 1 object — `FUNCTION_BLOCK "DamperDigital"` |
| `nStageHeater.scl` | 5,216 | 1 object — FB with a nested anonymous `STRUCT` inside `VAR_INPUT` |
| `AnalogInput.scl` | 3,681 | 2 objects — `TYPE "AnalogInputSettings"` then the FB |
| `DamperAnalog.scl` | 8,395 | 4 objects — 2 `TYPE`s, `DATA_BLOCK "HMI_Settings_DB"`, then the FB |

All four are UTF-8 **with BOM** and CRLF, matching the `.udt` / `.db` encoding already handled by
`SourceTextEncoding`.

### Export can control multiplicity; import cannot

Confirmed against the installed V21 public API documentation
(`Siemens.Engineering.Step7.xml`, assembly `21.0.0.0`):

- `GenerateOptions.None` — "Generate source from block without dependent blocks"
- `GenerateOptions.WithDependencies` — "Generate source from block with dependent blocks"
- `GenerateBlockOption.None` — "Throws an exception and deletes the blocks if there is any
  generation error"; `KeepOnError` keeps them regardless.

`PlcExternalSourceSystemGroup.GenerateSource` has a two-argument overload and a three-argument
overload taking `GenerateOptions`. `PlcExternalSource.GenerateBlocksFromSource` has **no**
equivalent switch: it creates whatever the file declares. The file is in charge on the way in.

That asymmetry is why write strictness and read multiplicity have to be decided together.

### The 2-arg `GenerateSource` default is `None` — measured, not assumed

`BlockExporter.ExportSource` and `PlcTypeExporter` both call the two-argument overload and never
pass `GenerateOptions`. Siemens documents the enum but not the default.

The Task 1 spike settled it: **the default is `None`.** Against a real V21 FB with UDT- and
DB-typed dependencies, the two-argument export is byte-identical to an explicit
`GenerateOptions.None` export (1 declared object) and differs from `WithDependencies` (4 declared
objects). See the spike findings under "Live-test gates" below.

That closes the risk this section was written to flag: shipped Phase 1/2 exports have **not** been
silently emitting dependency closures, so no already-released behavior needs correcting. Phase 3
still passes the option explicitly on every export path — not to fix a defect, but so the value
stops being an undocumented default that a future Openness release could change underneath us.

### `StateMachine.scl` is not Siemens output

The 184-byte `StateMachine.scl` in `priv/tia_exports/` is a third-party tool's stub — its own
content reads `// (!) Network 1: GRAPH network parsed but rendering is deferred in v0`. It is not
evidence about `GenerateSource`. We therefore have **zero** evidence of what `GenerateSource`
produces for a graphical-language block, which is why the language gate refuses them by name rather
than attempting and reporting.

### `create_block` for SCL already works

The empty `<NetworkSource />` compile unit in `BlockSourceGenerator` is a schema-valid placeholder,
fixed in the 2026-07-25 block-write-format repair and covered by live certification evidence. It is
not broken; it simply cannot carry logic. The roadmap's Phase 3 wording ("replace the
`BlockSourceGenerator` XML-only placeholder with real source generation") describes work that is not
load-bearing for this goal.

## Architecture

### Write semantics — strict single object

`update_block_logic` with `format=source` accepts a document declaring **exactly one** object, whose
name equals the resolved target block's name. A document declaring two or more objects is rejected
in preflight, before anything touches the project.

Rationale:

- `update_block_logic` is an update, never an upsert. It already refuses to write to a block that
  does not exist. A multi-object source would create UDTs and DBs that were never addressed.
- The write safety model binds a single-use token to the exact tool, project path, requested input,
  and project state, and the preview tells the user what will change. A source that rewrites three
  objects the preview did not name breaks that promise.
- It keeps `BlockSourceWriteWarnings`' existing `generatedObjectCount != 1` rule exactly correct.
  That rule is the only cheap signal that a write landed somewhere it was not addressed, and this
  route has no automated coverage.

Multi-object atomic writes are a coherent future feature. They are not this phase.

### Read semantics — caller chooses multiplicity

`withDependencies` (optional, default `false`) on `get_block_content` and `get_type_content`:

| Value | `GenerateOptions` | Result |
| --- | --- | --- |
| `false` (default) | `None` | One object. Round-trippable: what you read is exactly what a write accepts. |
| `true` | `WithDependencies` | The object plus its dependency closure. Context only. |

A `withDependencies=true` response **must** state that it is not writable. The declared-object list
and that statement are carried as a **batch-result warning**, not injected into the document text as
an SCL comment — the document's whole value is being clean, unpolluted SCL that a client can read,
edit, and (in the default case) hand straight back.

A client that needs a block's types without the round-trip trap issues `get_block_content` and
`get_type_content` in the same read batch.

### Language gate

`format=source` is accepted for:

- `GlobalDB` — already shipped, unchanged.
- FB / FC / OB whose programming language is SCL.

Everything else is refused **by name**, mirroring `BlockExporter.RequireGlobalDb`'s existing
message style: the caller is told what they actually addressed and which format to use for it —
for example, *"'PLC_1/Blocks/Inputs_FB' is a LAD function block. format=source is not available for
LAD; use format=xml."*

The extension is always derived from the resolved object (`.scl` for an SCL block, `.db` for a
global DB, `.udt` for a type), never supplied by the caller.

### Component changes

**Siemens-free, linked into `TiaMcpServer.Tests` via `<Compile Include>` — where coverage lives:**

| Component | Change |
| --- | --- |
| `SourceDeclarationScanner` | **New.** Returns every object a source declares — kind, name, line — for `TYPE`, `DATA_BLOCK`, `FUNCTION_BLOCK`, `FUNCTION`, `ORGANIZATION_BLOCK`. Must ignore keywords appearing inside `//` and `(* *)` comments and inside string literals. Replaces the first-match regex, which cannot express "exactly one". |
| `SourceFormatEligibility` | **New.** Given a block kind and language descriptor, returns allowed / not allowed, the file extension, and the refusal message. |
| `PlcTypeSourcePreflight` | Becomes a thin caller of the scanner: require exactly one declaration, require its name to equal the target. |
| `BlockSourceWriteWarnings` | **Unchanged.** `generatedObjectCount != 1` stays correct under strict writes. |

**Siemens-touching, NOT linked into the test project:**

| Component | Change |
| --- | --- |
| `BlockExporter.ExportSource` | `RequireGlobalDb` → `SourceFormatEligibility`. Extension from the resolved block. `GenerateOptions` passed explicitly. |
| `BlockImporter.ImportSource` | Same gate. `targetName + ".db"` becomes `targetName + eligibility.Extension`. |
| `PlcTypeExporter` | Pass `GenerateOptions` explicitly; honor `withDependencies`. |
| `VerifySourcePostconditions` | Reused unchanged. |

**Host:**

| File | Change |
| --- | --- |
| `TiaMcpServer.Contracts/WorkerRequest.cs` | Add `WithDependencies`. |
| `TiaMcpServer/Batch/BatchOperationRequest.cs` | Add `WithDependencies`. |
| `TiaMcpServer/Batch/BatchOperationCatalog.cs` | Add `withDependencies` to `get_block_content` and `get_type_content` optional fields. |
| `TiaMcpServer/Batch/BatchSafetySnapshot.cs` | Preview text for a `format=source` block write says so, rather than reading identically to an XML write. |
| `TiaMcpServer/Batch/BatchWorkerInvoker.cs` | Thread `withDependencies` through. |
| `TiaMcpServer/Worker/OpennessWorkerClient.cs` | Thread `withDependencies` through. |

### Postcondition verification

`VerifySourcePostconditions` is reused as written: compile the whole PLC, then re-export and check
the document is non-empty. Compiling the whole PLC rather than the single block is *more* correct
for an FB than it was for a DB — an interface change invalidates callers and instance DBs, and the
compiler is the only thing that knows which.

### Why `create_block` is untouched

Creating a complete new SCL block already decomposes into two existing operations, and a write batch
can carry both: `create_block` produces an empty SCL block, then `update_block_logic` with
`format=source` supplies the full interface and body. Routing `create_block` through the
external-source pipeline would rewrite a path that currently works and carries live certification
evidence, for no user-visible gain — and adding a `sourceContent` field to `create_block` would
require its own preview text, preflight, postcondition verification, and live coverage, roughly
doubling the phase.

Leaving `create_block` alone also preserves the "update never creates" invariant that the strict
single-object rule depends on.

## Behavior change to already-shipped code

Making the preflight strict changes the behavior of two operations that are already merged:
`update_type_content`, and `update_block_logic` with `format=source` on a global DB. Today a source
declaring two or more objects takes the **first** declaration's name; after this change it is
rejected in preflight.

This is strictly safer and consistent with the Phase 3 write decision, and it is applied uniformly
rather than only to the new SCL path. It is recorded here because it is a change to working code,
not a new-surface-only addition.

## Live-test gates

### Task 1 — throwaway spike (closes Phase 0 for SCL)

A disposable PowerShell script against a real V21 project, exercising two block shapes drawn from
the samples: one self-contained FB (`DamperDigital`) and one with UDT + DB dependencies
(`DamperAnalog`).

That `GenerateBlocksFromSource` updates an existing FB rather than refusing it is **confirmed by the
project owner from field experience** and is not an open question; the spike exercises it
incidentally rather than gating on it.

Open questions the spike must answer:

| | Question | Consequence if the answer is unexpected |
| --- | --- | --- |
| A | Do an FB's instance DBs survive an interface-changing update, and does the PLC still compile? | May require an explicit warning, or a documented precondition. |
| B | Are block number, auto-number, header author / family / version, and know-how protection preserved? | If `GenerateSource` omits them, a round trip silently resets them — needs a documented caveat or a guard. |
| C | What does the 2-arg `GenerateSource(objects, file)` overload default to? | Settles whether shipped Phase 1/2 exports have been emitting dependencies. |
| D | Is `generated.Count == 1` for a single-object `.scl`? | Confirms `BlockSourceWriteWarnings` needs no change. |
| E | Does re-export after import produce byte-identical text? | Bounds the round-trip fidelity claim. |
| F | Does a unit-scoped SCL block resolve through the software unit's own `ExternalSourceGroup`? | Mirrors the DB case; assumed, not proven. |
| G | Exactly what does `GenerateSource` emit for an FB — `VERSION`, attribute blocks, `REGION`s, comments? | Determines whether the scanner needs to tolerate shapes not present in the samples. |

The spike is thrown away. Its findings are recorded in the implementation plan and this spec before
any production code is written.

#### Spike findings (2026-07-27)

Measured against TIA Portal V21 (Openness `21.0.0.0`), project `SimpleProject.ap21`, using
`PLC_1/Blocks/999_MISC/DamperAnalog` (SCL FB with UDT-typed and DB dependencies) and
`PLC_1/Units/Test_SU/Blocks/HartCommandsRdWrInRun` (SCL FB inside a software unit).

| | Question | Answer |
| --- | --- | --- |
| A | Instance DBs survive an interface-changing update? | **Yes.** Adding a static `VAR` member and regenerating left `DamperAnalog_DB` in place; the PLC compiled with 0 errors and 0 warnings. |
| B | Block number / auto-number / header / know-how preserved? | **Yes — all six.** `Number` (9002), `AutoNumber` (False), `HeaderAuthor`, `HeaderFamily`, `HeaderVersion` (0.1) and `IsKnowHowProtected` (False) were all unchanged across the update. |
| C | 2-arg `GenerateSource` default | **`None`.** The 2-arg export is byte-identical to explicit `GenerateOptions.None` (1 declared object) and differs from `WithDependencies` (4: `TYPE AnalogInputSettings`, `TYPE UDT_Settings`, `DATA_BLOCK HMI_Settings_DB`, `FUNCTION_BLOCK DamperAnalog`). Shipped Phase 1/2 exports have therefore never emitted dependency closures. |
| D | `generated.Count` for a single-object `.scl` | **1.** `BlockSourceWriteWarnings` needs no change. |
| E | Re-export byte-identical | **Yes, both ways.** The unmodified round trip re-exports byte-identically, and after a one-line `VAR` addition the re-export equals the submitted text exactly. |
| F | Software-unit scope resolves | **Yes.** The unit-scoped block resolved through the unit's own `ExternalSourceGroup`; `GenerateBlocksFromSource` returned 1 on both the unmodified and the edited import, the PLC compiled clean, and **zero** blocks were added to the top-level PLC root. |
| G | What an FB export emits | `VERSION : 0.1`; a block attribute line `{ S7_Optimized_Access := 'TRUE' }`; per-member inline attribute blocks `{ ExternalAccessible := 'False'; ... }`; `REGION` markers; and a nested anonymous `Settings : Struct ... END_STRUCT;` inside `VAR_IN_OUT`. No `//` or `(* *)` comments and no `TITLE =` in this sample. |

Two API facts the spike confirmed by reflection, which Tasks 7 and 8 depend on:
`GenerateSource(IEnumerable<IGenerateSource>, FileInfo, GenerateOptions)` exists, and `PlcBlock`
implements `IGenerateSource` directly — no cast or wrapper is needed. Note also that the
parameterless `GenerateBlocksFromSource()` returns `void` and so cannot report a generated count;
only the `GenerateBlockOption` overloads can.

**Two traps that produced false readings before the numbers above were trusted.** Both will bite
`<removed legacy SCL acceptance harness>` in Task 9 the same way:

- `GenerateBlocksFromSource` **destroys and recreates the block**, so any handle captured before an
  import reads `$null` for every property afterwards. Snapshotting attributes through a stale handle
  produces a convincing but entirely false "TIA reset every attribute" result for question B. Always
  re-resolve the block after an import before comparing.
- TIA **auto-renames a colliding member** to `<name>_1`. A fixed probe-member name collides with
  whatever a previous run left in the block, and the rename then shows up as a byte-difference that
  reads as a round-trip fidelity failure for question E. Use a per-run unique name, and measure the
  unmodified round trip as well as the edited one — the edited comparison is uninterpretable until
  the unmodified one is known clean.

`G` describes a single FB. It is evidence that the scanner must tolerate `VERSION`, attribute
blocks, `REGION` and nested anonymous `STRUCT`s; it is not evidence that comments never appear.

### `<removed legacy SCL acceptance harness>` — gates Phase 3 completion

Written after the spike, mirroring the structure of `<removed legacy DB acceptance harness>`. Covers, at minimum:

- Export an SCL FB with `withDependencies=false`; assert exactly one declared object.
- Export the same FB with `withDependencies=true`; assert the dependency closure is present and the
  response carries the not-writable warning.
- Edit the single-object export's body, write it back, assert the change landed and the PLC compiles.
- Submit a multi-object source; assert it is refused in preflight and the project is unchanged.
- Submit a source declaring a different block name; assert the existing refusal fires.
- Repeat the successful round trip for an SCL block inside a software unit.
- Assert no `PlcExternalSource` node survives in the project afterwards.

`<removed legacy UDT acceptance harness>` gains one case for the `get_type_content` half of `withDependencies`:
export a UDT that references another UDT, with the flag off and on, and assert the closure appears
only when it is on.

## Error handling

All refusals are `WorkerFailureCategories.ValidationError` raised before the project is touched:

- Empty or unparseable source.
- Source declaring zero recognizable objects.
- Source declaring two or more objects (strict rule).
- Source whose single declared name does not equal the resolved target.
- `format=source` addressed to a block whose language is not SCL and which is not a global DB.

Warnings, not failures:

- A surviving `PlcExternalSource` project node — existing `ExternalSourceScope` behavior.
- `generatedObjectCount != 1` — existing `BlockSourceWriteWarnings` behavior.
- A `withDependencies=true` read — the document is context only.

## Testing

**Offline unit tests** (the 80% coverage floor applies to the test project's compiled sources):

Fixtures copied into `TiaMcpServer.Tests/Fixtures/` from `priv/tia_exports/`:

| Fixture | Exercises |
| --- | --- |
| `DamperDigital.scl` | Single-object FB — the accept case. |
| `AnalogInput.scl` | Two objects — strict rejection, and the `withDependencies` read shape. |
| `DamperAnalog.scl` | Four objects across three kinds — strict rejection with a full declared list. |
| `nStageHeater.scl` | Nested anonymous `STRUCT` inside `VAR_INPUT` — the scanner must not mistake it for a declaration. |

Test subjects:

- `SourceDeclarationScanner` — counts and names across all four fixtures; keywords inside `//` and
  `(* *)` comments and inside string literals are not declarations; BOM and CRLF tolerated.
- `SourceFormatEligibility` — accept cases and every refusal message.
- `PlcTypeSourcePreflight` — exactly-one enforcement, name-mismatch rejection, and the existing
  `.udt` / `.db` / SimaticML cases still pass.
- `BatchOperationCatalog` — `withDependencies` accepted on both read operations, rejected where it
  does not belong.

**Live**: `<removed legacy SCL acceptance harness>` as above. The Siemens-touching components have no offline
coverage by construction, exactly as in Phases 1–2.

## Roadmap corrections to apply

`docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` needs two edits when this phase lands:

1. The Phase 3 row says "replace the `BlockSourceGenerator` XML-only placeholder with real source
   generation." That is descoped — see [Why `create_block` is untouched](#why-create_block-is-untouched).
   The placeholder is schema-valid and working.
2. The Phase 3 row says "SCL/STL-language blocks." STL is deferred to its own follow-up for lack of
   an `.awl` sample and any Phase 0 evidence.
3. The Phase 0 section says "Phase 0 stays open **for SCL only**." Task 1's spike closes it; the
   section should record the SCL fixture alongside the existing UDT and DB ones.

Additionally, the "Sample export analysis" table predates the 2026-07-27 samples and should gain the
multi-object observation: a `.scl` file routinely declares several objects of different kinds, and
`DamperAnalog.scl` is the in-repo example.

## Known adjacent gap

`get_block_content` with `format=source` and `withDependencies=true` returns a document that
`update_block_logic` will refuse. This is deliberate and warned about, but it is an asymmetry a
future phase may want to close — most likely by the multi-object atomic write that this phase
explicitly declines to build.
