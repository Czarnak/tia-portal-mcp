# TIA Portal PLC Import and Export

## Operation surface

The MCP provides a focused PLC round-trip interface through five operations (reads through `plc_read`, writes through `plc_write` contract `2.1`):

| Operation | Direction | Default format | Purpose |
|---|---|---|---|
| `get_block_content` | Read | `xml` | Reads one existing PLC block. |
| `update_block_logic` | Write | `xml` | Updates one existing PLC block from supplied document content. |
| `patch_block_logic` | Write | `xml` | Resolves unique text replacements against a fresh block export, then uses the existing block update path. |
| `get_type_content` | Read | `source` | Reads one existing PLC data type. |
| `update_type_content` | Write | `source` | Updates one existing PLC data type from supplied document content. |

The `format` field accepts `xml` and `source`, case-insensitively. Block and type reads use `plc_read`; writes use `plc_write`, passing the document as `content` or block patch replacements as `edits`, together with `expectedContentHash`, the `contentHash` from a `plc_read` of the same object in the same format. Content changed since that read fails with `state_changed`. A document over the value budget (60,000 characters by default; raise it with `--max-item-chars`, see [response size limits](../guides/mcp-client-configuration.md#response-size-limits)) is omitted from the read, has no hash, and cannot be written, including by a patch.

## Supported formats

### PLC blocks

- `format="xml"` is the default. Reads return a controlled bundle containing sanitized SimaticML and SIMATIC SD `.s7dcl`/`.s7res` documents.
- `format="source"` returns Siemens external-source text for global DBs (`.db`) and for SCL-language FB, FC and OB blocks (`.scl`).
- Instance DBs, array DBs, and FB/FC/OB blocks written in LAD, FBD, GRAPH or STL use the XML route; they are not eligible for the external-source route and are refused with a message naming the addressed block.

### PLC data types

- `format="source"` is the default and returns `.udt` text.
- `format="xml"` returns SimaticML.

## Update contract

Updates are strict updates to an existing object:

- The target path must resolve to an existing block or data type.
- The declaration in the submitted content must match the addressed object name.
- Import does not create, rename, delete, or upsert objects.
- XML block imports use `ImportOptions.Override`.
- External-source generation uses `GenerateBlockOption.None`.
- Root groups, user groups, and software-unit-owned external-source groups are resolved according to the addressed path.

For `patch_block_logic`, the host checks the hash of the whole fresh served export before matching
edits. XML anchors search only its authoritative `.xml` part and resolve to XML-only content;
companions and separator bytes remain part of the hash precondition, never patch targets. Source
anchors search the whole source. Each nonblank anchor must occur once, counting overlapping
occurrences, and edits use disjoint original spans without cascading. CRLF and LF are equivalent
for matching; unmatched characters and lone CR are preserved, and replacement line endings follow the
matched span or original starting line. Empty replacements are allowed. Introduced bundle
delimiter lines are refused. See [patching block logic](PLC_OPERATIONS_SUMMARY.md#patching-block-logic)
for exact request fields, newline fallback, delimiter rules and the preview/apply workflow. Type
updates remain full-document writes.

For block updates, the worker attempts validation, target import or generation, compile observation,
and an independent fresh postcondition read in order, then reports which stages were established. A
proven refusal before target invocation leaves import, compile, and final read `not_started`. If
target invocation starts but its return cannot be established, commitment is `unknown`; compile and
any unestablished final read are `unavailable`. On the normal-return path, the worker observes
compilation and checks the postcondition by freshly resolving and re-exporting the block.
In a validated call with at least two source-format block updates or patches, only the whole-PLC
compile is deferred: source generation, temporary-source cleanup and the fresh final read still
run per import. A completed deferred import reports `compileStage:"deferred"`, `compileReport:null`
and `compileDetailsOmitted:false`; a refused or unknown import retains its existing stages.
Other calls keep per-operation compilation, including external-source global DB updates, because
changing a DB declaration can affect dependent blocks.

Import success is independent of that subsequent compilation: a completed successful import with a
successful final read remains a succeeded item when compilation reports errors or is unavailable.
Its `result.importOutcome.compileStage` and `compileReport` retain the separate compile result, and
later batch items continue. Successful import does not mean the PLC compiles. Actual import or final
verification failures still fail the item; observed Portal loss remains `portal_lost`. Compiler
descriptions in the import outcome remain sanitized; use `compile_check` for compiler messages.
For deferred imports, the root `compilationMode` is `"shared-per-plc"` and `sharedCompilations`
contains the informational compile results after the mutation loop, before content verification.
Per-operation calls use `"per-operation"` and `sharedCompilations:null`; shared previews and
pre-apply rejections also have null shared results. The selector, early-stop and cancellation rules
are described in [shared source compilation](PLC_OPERATIONS_SUMMARY.md#shared-source-compilation).

### Block-update outcome evidence

`plc_write` reports the non-atomic result of each `update_block_logic` or `patch_block_logic` item as an import outcome
object. A succeeded item's `result` is `{ format, importOutcome }`; a failed content import carries
the same object as `failure.blockImportOutcome`. `blockImportOutcome` is a conditional member on
every structured tool's failure schema and is omitted from unrelated items. The host requires
worker capability `typed-block-import-outcome-v1` before it sends engineering requests, so an older
worker is rejected during the capability handshake.

The object uses these fields and closed values:

| Field | Values and meaning |
|---|---|
| `importStage` | `not_started`, `completed`, or `unknown`. |
| `importResultState` | `success`, `non_success`, or `unavailable`. A normally returned `Blocks.Import` or `GenerateBlocksFromSource` call is `success`; a normally returned SIMATIC SD result maps to `success` or `non_success`; a call that does not return is `unavailable`. |
| `targetMutationCommitted` | Nullable Boolean. `false` proves refusal before the target import/generation call; `true` means that call returned normally; `null` means commitment is unknown after invocation started or transport became ambiguous. Presence evidence alone never determines this field. |
| `compileStage` | `not_started`, `deferred`, `succeeded`, `failed`, or `unavailable`. `deferred` appears only for a completed source import selected for shared compilation and supplies no per-item compile evidence. Warning-only reports are `succeeded`. For per-operation observations, `unavailable` is based on structured target/session/invocation evidence and takes precedence over `failed`, which takes precedence over `succeeded`. |
| `compileReport` | A nullable bounded copy of the whole aggregate compile report. It is null for deferred compilation, when compilation did not start, and may be null when no report could be established; a partial report is retained when available. Full aggregate error/warning totals are preserved even when detail is omitted. |
| `compileDetailsOmitted` | `true` when any PLC, message, note, or string detail was omitted or shortened by the bounded projection. |
| `finalReadStage` | `not_started`, `succeeded`, or `unavailable`. |
| `targetPresent` | Nullable Boolean. It is `true` or `false` when the independent fresh resolver establishes presence or absence, and null only when resolution itself is unavailable or unreliable. A later export/read failure can leave `finalReadStage=unavailable` while preserving `targetPresent=true`. Presence does not imply import commitment. |
| `contentRelation` | Always `unknown` in this contract. The final export is not claimed to equal either the requested content or the prior content. |
| `temporarySourceState` | `not_applicable`, `not_created`, `removed`, `residue_possible`, or `unknown`. XML uses `not_applicable`; source updates distinguish refusal before `CreateFromFile`, confirmed removal, possible residue, and ambiguous creation/cleanup. No temporary node name or path is exposed. |

The import fields form a closed matrix: `not_started` requires
`targetMutationCommitted=false` and `importResultState=unavailable`; `unknown` requires a null
commitment and `unavailable`; `completed` requires `true` and either `success` or `non_success`.
A normally returned target call is therefore conservative evidence that the addressed in-memory
target may have changed. It does **not** mean the project was saved, downloaded, persisted outside
the open engineering session, plant-accepted, or made byte-equivalent to the request.

Compile evidence aggregates the existing selected scope: the exact block target when `blockPath`
is supplied, the named PLC when only `plcName` is supplied, or all discovered PLCs in deterministic
order otherwise. The outcome retains at most 8 PLC entries, 20 message rows, 8 diagnostic notes,
256 decoded characters per string, and 1,024 decoded diagnostic characters across message text,
paths, and notes. The complete serialized `blockImportOutcome` is limited to 12,000 UTF-16
characters, including JSON escape expansion and metadata.

The sequential call remains non-atomic. Processing stops at the first failed item and later items
are marked `skipped`; the server does not retry, roll back, compensate, save, or download.
Unsafe exception-derived message text is deliberately replaced by bounded summaries. The pinned
project binding, access policy, guards, and audit of the guarded pipeline apply unchanged.

### Preview evidence

Each `update_block_logic`, `patch_block_logic` and `update_type_content` item's `plc_write` effect carries a structured
`contentDiff` object, in previews and actual calls alike. It compares the current exact-format text
read at planning with the submitted `content` or resolved patch candidate; it does not predict Siemens post-write state and is
evidence only, not part of the hash check.

- Each eligible operation has at most 40 excerpt lines and 8,192 excerpt characters per side.
- When a changed span exceeds 40 lines, each excerpt contains the first 20 plus last 20 lines.
- Each displayed line is limited to 512 characters.
- The complete call is limited to 320 excerpt lines and 32,768 excerpt characters in request
  order across initial planning, dependent replanning and read-back verification. Spent allowance
  is not refunded when evidence is replaced or omitted. After that excerpt budget is exhausted,
  every eligible entry still retains its raw hashes, counts, and equality flags.
- Raw SHA-256, raw character count, and raw line count use the original text. For line-window
  comparison only, line-ending normalization maps CRLF and CR to LF; it performs no other
  normalization. The response also reports `rawTextEqual`, `normalizedLinesEqual`, and
  `lineEndingOnly`; a line-ending-only difference has unequal raw text but equal normalized lines.
- An XML write whose `content` is a single Simatic ML document (no `--- FILE:` separator, or a
  bundle holding only the `.xml`) is compared with the `.xml` document of the current bundle, so
  `contentDiff.current` (`sha256`, character and line counts) then describes that `.xml` part, not
  the whole served bundle, and a one-document bundle's separator line is left out of the requested
  side. `expectedContentHash` still covers the whole bundle. Any other content, including text
  that does not parse as a bundle, is compared whole.
- XML patches always resolve to the `.xml` document alone and use that same XML-only diff path,
  while the expected hash still protects the whole served bundle. Source patches compare the
  whole original source with the resolved source. The audit's conditional
  `items[].resolvedOperation.content` retains the full input selected for an attempted patch;
  the public diff remains bounded evidence, and neither establishes import success.
- Every other operation has `contentDiff: null`.

### Submitted versus observed content

The existing nonempty verification re-export can supply optional `verification.operations[].readBack`
for block updates, resolved patches and type updates. It compares the submitted execution content
with the observed export after any shared compilation. A failed attempt can still expose observed
content without changing its failure. Successful superseded items, previews, non-content items,
skipped or denied items, and unavailable or empty exports omit this member. This observation adds
no worker call and never changes import outcome, verification success or the reusable full-export
`actual`/`contentHash`.

`comparisonScope` is `xmlDocument` when a raw XML or single-XML-document submission selects XML
on both sides, excluding observed companions and bundle separators. Source, multiple-document
submissions and selection fallbacks use `wholeText`. `rawTextEqual` is exact selected-text equality;
`normalizedLinesEqual` converts CRLF and lone CR to LF only. `lineEndingOnly` means raw unequal
but newline-normalized equal. No semantic or XML normalization is performed. `submitted` and
`observed` carry diagnostic side hashes, counts and bounded excerpts; their `sha256` values are
not concurrency hashes. Changed-span line counts include unchanged lines between differences.

The preview generation bounds above also cover read-back. Generation exhaustion retains summaries
and records excerpt omission counts without failing execution or verification; an exhausted budget
can accompany equal text with zero omissions. Outer encoded response budgeting removes retained
read-back excerpts, then whole comparisons, before existing verification checks and hashes.
Excerpt removal records `readBack.omission`; whole-comparison removal records
`verification.omission`. Delivery omissions can make root `success:false` while item statuses and
verification success remain true. See [execution, verification and audit](PLC_OPERATIONS_SUMMARY.md#execution-verification-and-audit)
for fields, omission order and hash-preserving inspection guidance. Differences are observational
and may include compilation effects; they do not establish import success or the cause of a change.

## Format matrix

| Capability | Status |
|---|---|
| PLC block SimaticML/XML read and update | Supported |
| PLC block SIMATIC SD `.s7dcl`/`.s7res` bundle handling | Partial: supported as controlled bundle context |
| PLC block external-source `.db` exchange | Partial: global DBs |
| PLC block external-source `.scl` exchange | Partial: SCL-language FB/FC/OB |
| LAD/FBD/GRAPH/STL FB/FC/OB external-source exchange, instance DBs and array DBs | Not supported; use the XML route |
| PLC data type SimaticML/XML exchange | Supported |
| PLC data type `.udt` exchange | Supported |
| Existing-object update | Supported |
| Unique text patches of existing block XML or eligible external source | Supported; full-export hash required; types and companions excluded |
| Create, rename, delete, or upsert through import | Not supported |
| Root, user-group, and software-unit target routing | Supported |
| Caller-selected Openness `ExportOptions` | Partial: XML export uses `None` |
| Caller-selected Openness `ImportOptions` | Partial: imports use `Override` |
| `SWImportOptions` for structural changes or missing references | Not supported |
| Know-how-protected block exchange | Partial: generic export may expose only the public interface or fail; protected import is unavailable |
| Dedicated failsafe block exchange | Not supported; compatible consistent F-blocks may use the generic XML route |
| System-block exchange | Not supported |
| Tag tables, tags, and constants through import/export | Not supported; use PLC tag operations |
| Technology objects, watch tables, and force tables | Not supported |
| PLC alarm, ProDiag, hardware CAx, project-text, and graphics exchange | Not supported |
| HMI import/export | Not supported; see [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md) |
| Generic arbitrary-object XML import/export | Not supported |

## Relationship to the Openness API

TIA Portal Openness supports additional PLC, HMI, project, hardware, alarm, and text exchange APIs. This MCP surface intentionally exposes only the PLC block and PLC data-type routes described above. It does not provide a generic proxy for arbitrary `Import` and `Export` calls.
