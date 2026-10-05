# TIA Portal PLC Operations

## Supported read operations

| Entry point | Operation | Inputs | Behavior |
|---|---|---|---|
| `browse_project_tree` | `browse_project_tree` | Optional `projectPath`, `depth`, `startSelector`, `pageSize`; continuation `cursor` | Locates PLC software, blocks, groups, types, and other project objects; cursors reject binding changes. |
| `plc_read` | `get_block_content` | Required `blockPath`; optional `format`, `withDependencies` | Reads an existing block as XML/SimaticML (default) or an eligible external source. See [IMPORT_EXPORT_OPTIONS_SUMMARY.md](IMPORT_EXPORT_OPTIONS_SUMMARY.md). |
| `plc_read` | `get_type_content` | Required `typePath`; optional `format`, `withDependencies` | Reads an existing PLC type as `.udt` source (default) or XML/SimaticML. |
| `plc_read` | `list_tag_tables` | Optional `plcName`, `folderPath`, `tableName` | Lists PLC tag tables and the exposed tag and constant information. |
| `read_cross_references` | `read_cross_references` | Required `target`; optional `filter`, `maxResults`, `projectPath` | Reads the cross-references of one project-tree target. Filters are `AllObjects`, `ObjectsWithReferences` (default), `ObjectsWithoutReferences`, and `UnusedObjects`. |
| `compile_check` | `compile_check` | Optional `projectPath`, `plcName`, `blockPath` | Compiles the PLC or selected block scope and returns compiler messages. |

`plc_read` and `read_cross_references` are registered in every access mode. `execute_read_batch` was
retired; use these two tools instead. Tree browsing and compilation are standalone tools. `compile_check` is available in read-write
and full modes and does not use a safety token. In-project PLC edits also work in both writable
modes. Legacy batch `start_plc` and `stop_plc` require full; a read-write batch containing either
is rejected before binding, snapshots, token handling, or worker activity.

Use `bind_project` in any mode to adopt or switch to an already-open project; a different binding
requires `forceRebind:true`. Ordinary PLC reads never bind, switch or open. Only `open_project` and
`create_project` open projects. Lifecycle runs in read-write with one confirmation form per actual
call, or in full without server elicitation; block guards stop the call in every mode. The complete
tool counts are 6/16/16. Read-only never opens, creates, saves or closes a project.

## PLC reads (`plc_read`)

`plc_read(operations)` takes at most 50 items. Each has a unique, non-blank `operationId` of at most
256 characters, an `operation`, an optional `projectPath`, and that operation's parameters. Items run
in caller order and independently: a failed item does not stop the others. Unknown fields are
rejected. The response is one structured document (`contractVersion` `1.0`, root `warnings` array,
explicit nulls), delivered identically as text and `structuredContent`. `isError` is true only for a
rejection before anything ran.

- `get_block_content` and `get_type_content` return `{ format, content, contentHash, warnings }`.
  `format` is `xml` or `source`; blocks default to `xml` and types to `source`. `withDependencies`
  adds the dependency closure to a `source` export; that document is context only and cannot be
  written back.
- `contentHash` is `xml:sha256:<hex>` or `source:sha256:<hex>` over the exact served text. It is an
  explicit `null` when `withDependencies` is true.
- `list_tag_tables` returns `{ isComplete, plcs[] }`, each PLC naming its software and device and
  carrying its `tables[]`. An omitted `plcName` reads every PLC, including PLCs in device groups.
  Tags carry `externalAccessible`, `externalVisible` and `externalWritable` (`null` when unreadable);
  user constants carry a readability marker; anything that could not be read sets `isComplete:false`
  with a message (the first 50 messages are kept, then one summary entry). `tableName` keeps one table
  and `folderPath` the tables directly in one folder (written as the inventory emits it, `/` or
  `/Line/Cell`); both are case-insensitive and combine with `plcName`. A narrowing that matches no
  table fails the item with `target_not_found`, unless part of the tree was unreadable.
- A `projectPath` that differs from the bound project fails only that item with `binding_conflict`.
  Reads never bind, switch or open a project.

Budget: a value over 60,000 characters or a document over 180,000 characters is never cut. The
affected item `result` is omitted whole with an omission record and narrowing guidance, so it has no
hash. A single block or type export over 60,000 characters therefore cannot be read through
`plc_read`, and cannot be updated through the batch write path, which needs the hash. One PLC of
roughly 400 tags already exceeds the value: narrow `list_tag_tables` with `plcName`, `folderPath` or
`tableName`.

## Compiler diagnostics (`compile_check`)

Compilation can change TIA Portal's in-memory project state. A returned report is not evidence of
saving, downloading, or PLC/plant acceptance.

Each PLC result uses `plcName` for the actual PLC software name and `deviceName` for the containing
hardware device. The input `plcName` selector continues accepting either name. The same identities
are returned for PLC and selected-block compilation, including expected compiler failures.

Nested compiler messages are flattened in deterministic parent-before-child order. Each row uses
that message's own description, path, and severity. Siemens-reported `state`, `errorCount`, and
`warningCount` are preserved whenever a compiler result is available; they are not recomputed from
the displayed rows and remain available on handled root-diagnostic collection access failures.

One projection budget is shared across all selected PLCs:

- Maximum message depth: 16, counting roots as depth 1.
- Maximum projected message count: 200.
- Maximum length of each description and path: 1,024 UTF-16 code units.
- Maximum combined description/path text: 32,000 UTF-16 code units.

The serialized compile report is additionally kept below 60,000 characters, including JSON
escaping. Trailing message rows may be removed to meet that bound without changing reported totals
or identities. Each affected PLC receives one sanitized omission note in `diagnosticNotes` when
diagnostics are shortened, omitted, or recoverably unreadable. For nested diagnostic access,
recoverable failures are expected exact `InvalidOperationException` access failures, not derived
exceptions. Infrastructure or interruption failures—including session loss, I/O, cancellation,
format errors, derived invalid-operation exceptions, and other unexpected faults—fail the operation
rather than return typed partial success. If metadata alone exceeds the response limit, the
operation fails with bounded guidance: compilation may have run, so inspect the current in-memory
project state before deciding whether to retry.

When an expected compiler invocation failure leaves no compiler result, the typed PLC result keeps
both identities, reports `state: "Error"`, and includes a sanitized unavailable-details note.
Its zero counts are fallback values, not evidence of an error-free compilation.

## Cross-reference coverage (`read_cross_references`)

`read_cross_references(target, filter?, maxResults?, projectPath?)` reads one target per call.
`target` is `{ path: [{ nodeType, name }, ...], member?: { kind, name } }`: copy the `path` segments
from `browse_project_tree` output (matching is case-insensitive as in the tree; siblings that differ
only in case are `target_ambiguous`). `member` addresses leaves the tree does not model, under a
`TagTable` path, with `kind` one of `Tag`, `SystemConstant` or `UserConstant` (exact case). `filter`
and `maxResults` (1 or greater) are validated before any worker call; `projectPath` is a target
constraint only.

- A path ending at a leaf (an OB, FB, FC, GlobalDB, InstanceDB or ArrayDB block, a system block, or a
  PLC type) or any `member` is one query on that object.
- A path ending at a container (`Device`, `PlcSoftware`, `SoftwareUnit`, a block, system-block,
  tag-table or type folder, or `TagTable`) sweeps every owner beneath it. A container with no owners
  is an empty, complete success; if owners exist and none of their queries succeeds, the call fails
  with `worker_operation_failed`.
- Zero matches is `target_not_found`; a leaf without the cross-reference service is
  `target_kind_unsupported`, which is distinct from "no references".

The result is one typed document: the target echo, `filter`, completeness fields (`isComplete`,
`ownerQueryCount`, `successfulOwnerQueryCount`), totals and the recursive
`sources[] -> references[] -> locations[]` hierarchy. `referencedAs` is `{ name, typeName }` of the
referenced object, and `access` and `referenceType` are V21 member names. Overlapping results from
different owners are not deduplicated. A complete successful sweep can have zero sources.

Unavailable owner services, traversal skips and a `maxResults` cut retain the available data and set
`isComplete:false` with messages. An incomplete `UnusedObjects` result is not an authoritative
unused-object audit and must not be used as proof that objects can safely be deleted.

Budget: sources are dropped whole from the tail until the report value fits 60,000 characters and
the document 180,000. The report then carries `isComplete:false`, `omittedSourceCount` and narrowing
guidance (`filter`, `maxResults`, a narrower `path`, or a `member`). When not even one whole source
fits, the first source is kept and trimmed inside (child sources, then references, then locations),
with one message counting what was omitted; the omitted tail cannot be paged.

### Unverified owner kinds

Only kinds whose owners were verified live (2026-10-05, V21) ship as members or endpoints. Not
shipped, and unverified for this tool: technology objects and WinCC Unified `HmiTag` (the
cross-reference service works, but the project tree has no node type for them), `DeviceItem`,
`Subnet` and `Node` (no service observed), technology-object instance DBs and WinCC Classic HMI tags
(not present in the spike project). `Device`, `PlcSoftware`, `SoftwareUnit` and `TagTable` are not
owners themselves; they only enumerate the owners beneath them. See Appendix B of the
[design spec](../superpowers/specs/2026-10-05-plc-read-write-and-cross-references-design.md).

## Supported write operations

The operations below run through `preview_write_batch` and `apply_write_batch`.

| Operation | Required inputs | Optional inputs |
|---|---|---|
| `update_block_logic` | `blockPath`, `yamlContent` | `format` (`xml` by default; `source` for global DBs) |
| `create_block` | `blockPath`, `blockType` | `language`, `obEventClass` |
| `delete_block` | `blockPath` | — |
| `create_block_group` / `delete_block_group` | `blockPath` | — |
| `update_type_content` | `typePath`, `sourceContent` | `format` (`source` by default) |
| `create_tag_table` / `delete_tag_table` | `tableName` | `plcName`, `folderPath` |
| `create_tag` | `tableName`, `name`, `dataType` | `plcName`, `folderPath`, `logicalAddress` |
| `update_tag` | `tableName`, `name` | `plcName`, `folderPath`, `newName`, `dataType`, `logicalAddress`, `externalAccessible`, `externalVisible`, `externalWritable`, `isSafety` |
| `delete_tag` | `tableName`, `name` | `plcName`, `folderPath` |
| `create_user_constant` | `tableName`, `name`, `dataType`, `value` | `plcName`, `folderPath` |
| `update_user_constant` | `tableName`, `name` | `plcName`, `folderPath`, `dataType`, `value` |
| `delete_user_constant` | `tableName`, `name` | `plcName`, `folderPath` |
| `start_plc` / `stop_plc` | — | `plcName` |

`create_block` accepts `FB`, `FC`, `OB`, or `GlobalDB`. Applicable block types support `LAD`, `FBD`, `STL`, `SCL`, and `GRAPH`. OB creation also accepts event classes such as `ProgramCycle`, `Startup`, `TimeDelay`, `CyclicInterrupt`, `HardwareInterrupt`, `Diagnostic`, and `TimeOfDay`.

## Update and write behavior

- Block updates validate the supplied document, import it into an existing block, compile the affected PLC scope, and verify the resulting block state through a postcondition/re-export check.
- Type updates require an existing addressed type and a declaration whose name matches that target.
- Block/type replacement previews can include bounded structured evidence that is current-versus-requested only, not predicted post-write state.
- Import/update operations modify existing objects only. They do not create, rename, delete, or upsert the addressed object.
- Deletes and PLC start/stop operations use the same confirmed write flow as other data writes.
- A write batch is applied in order and stops on the first failure. Completed mutations are not rolled back.
- Tag-related writes bind exact targets plus scoped name/address collision probes, replacing
  the full table-list safety payload. Table deletion binds the exact table's normalized Simatic ML
  export and digest; tag deletion binds exact tag state; constant deletion binds exact constant
  state. Create operations bind the parent/table identity and relevant collisions. Updates also
  bind the exact object's current state. See the [eight selector shapes](../ARCHITECTURE.md#8-write-safety)
  for the complete contract. Identical selectors share reads only within one phase and expand
  back into the original operation order; apply reads fresh state with no cross-phase cache.
- Tag and user-constant create/update name probes include case-insensitive matches against PLC
  tags, user constants, and blocks in the selected PLC's unqualified CPU namespace. Matching blocks
  include nested user and system-block groups; each probe retains its symbol kind and exact path.
  Logical-address probes remain tag-only. Table creation retains the exact destination folder but
  probes matching table names across the PLC's entire tag-table hierarchy, including sibling and
  nested folders. Unrelated names remain outside the snapshot; incomplete traversal fails closed.
  These scopes follow Siemens V21's [tag-name rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/rules-for-plc-tags/valid-names-of-plc-tags),
  [constant-name rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/declaring-global-constants/rules-for-global-user-constants),
  and [table-creation rules](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/creating-and-managing-plc-tag-tables/creating-plc-tag-tables).
  Software Unit namespace-aware block collision coverage remains a design/live qualification
  follow-up; these operations do not treat unit-local names as unqualified CPU-global names.
- If an `update_tag` requests `externalAccessible`, `externalVisible`, or
  `externalWritable` and that selected flag cannot be read for the exact tag, preview fails before
  token issuance. This safety condition does not change the public `list_tag_tables` contract:
  that read remains best-effort and may retain its existing skipped-read behavior.
- Structural `create_block`, `create_block_group`, and `delete_block_group` writes bind typed,
  operation-specific project-tree snapshots rather than a broad project-tree browse. The owner is
  either the PLC-global block root or the exact Software Unit block root. `create_block` binds the
  exact parent, ancestor chain, requested-name occupancy, and authoritative XML for an occupied
  block; `create_block_group` binds block and group occupancy for the requested name; and
  `delete_block_group` binds parent membership plus the complete content-bearing descendant tree,
  including authoritative XML for contained blocks. Malformed or conflicting typed payloads fail
  closed as `protocol_error` without echoing the rejected payload.
- Identical structural selectors share one read only within the current preview or apply phase and
  still expand into the original operation order. Apply performs a fresh read under the pinned
  binding lease. The internal worker methods are guarded `SafetyRead` operations and require the
  exact expected worker/Portal/project session identity.

## Tag safety acceptance boundary

PR 5 has completed offline/FakeWorker, static harness-contract, and guarded live TIA Portal V21
evidence. The
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
records the exact host, PID, disposable copy, fixtures, artifacts, and saved-baseline/source cleanup.
All eight operation previews and the ordered duplicate-selector check passed; same-object and
name/address collision drift returned `state_changed`; unrelated sibling drift preserved the
original target token; and one authorized unchanged-token apply succeeded. Public previews still
expose hashes and ordered targets rather than internal typed snapshot contents or worker read
counts, so those internal claims remain offline/FakeWorker evidence rather than live observations.

The harness requires PowerShell 7.2 or later, the built net8 host, an already-open exact
disposable project copy, and explicit PLC/table/tag/user-constant fixture names and values.
`PreviewOnly` is the non-mutating default. `DriftAndRestore` covers same-object drift, relevant
name/address collision drift, and unrelated sibling tolerance. `ApplyAndRestore` performs one
authorized feature apply. Both mutation modes require `-AllowMutation`, `-ConfirmDisposableCopy`, an
`-AuthorizedProjectPath` equal to `-ProjectPath`, and `-CleanupStrategy Discard`, plus a pre-saved
unmodified copy. The implemented cleanup is guarded `close_project` with `saveBeforeClose=false`;
the mode names do not imply an implemented inverse-restore strategy. No project files are deleted
and no save is issued. Scenario mutations remain until that final discard; no inverse fixture
writes restore constants or delete collision tags between checks. Acceptance must verify the
on-disk copy remains clean. A failed or
unconfirmed discard fails the run and requires manual no-save cleanup of the isolated copy.
Each run retains redacted MCP and failure/cleanup JSON in a dedicated artifact directory. The
completed report confirms that both mutation modes performed guarded no-save discard, the saved
copy returned to its exact baseline, and the original source was left open and unmodified. The
initial sandbox visibility failure and two deterministic lifecycle binding conflicts occurred
before mutation and were not uncertain writes. Ordinary tests only inspect the script as text.

Explicitly deferred: multilingual per-tag comment binding; public `list_tag_tables` completeness
changes; broader snapshot narrowing; Software Unit namespace-aware collisions; and PLC `start_plc`
and `stop_plc` safety work. Existing PLC start/stop operations are not changed or qualified by PR 5.

## Project-tree safety acceptance boundary

PR 6 completed offline/FakeWorker and static harness-contract coverage plus guarded live TIA
Portal V21 acceptance against the exact startup-bound project. The
[live acceptance report](../superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md)
records separate PLC-global and Software Unit owner runs.

For both owner scopes, occupied-block content drift invalidated `create_block`, descendant-block
content drift invalidated `delete_block_group`, and same-parent requested-name occupancy
invalidated `create_block_group`; relevant descendant membership separately invalidated group
deletion. All stale-token rejections returned `state_changed` before target mutation. Unrelated
sibling-tree drift left the target state hash unchanged and preserved the original token. The
authorized three-operation apply and restoration sequences succeeded through the public guarded
flow. Six restoration hash pairs per owner matched, all 52 record comparisons per owner were
byte-equivalent before compile, and six final compile checks per owner reported `Success` with 0
errors and 0 warnings.

The live report records only successful public project-binding fields: payload `isOpen`, payload
`path`, and envelope `sessionIdentity.projectPath`. Its redacted live artifacts are local and
git-ignored, while the contracts, implementation, offline tests, static harness tests, harness,
and report are repository-auditable. The acceptance does not establish save or persistence
behavior and is not plant or physical-hardware acceptance.

Explicitly deferred:

- Broader snapshot narrowing: unchanged and out of scope.
- `start_plc` / `stop_plc`: unchanged and out of scope.

## Current limits

The current surface does not provide:

- Generic online/offline status or connection configuration.
- Compare-to-online, program upload/download, or `UpdateProgram` workflows.
- Software Unit lifecycle or administration operations; block operations within existing Software Units are supported. Safety units, safety administration, safety signatures, and safety validation are not provided.
- PLC alarms, alarm classes, alarm text lists, ProDiag supervision, or supervision import/export.
- Technology objects, motion-control objects, watch tables, or force tables.
- OPC UA server configuration, communication groups, access control, or role mapping.
- System blocks, know-how protection workflows, webserver pages, block fingerprints, or loadable files.
