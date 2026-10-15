# `vci_read` / `vci_write` and a configurable worker timeout — design

*2026-10-15. Builds on the [VCI live spike](../acceptance/reports/2026-10-15-vci-spike.md); every Openness behavior
stated here comes from that report (cited as Q-numbers) unless marked as an evidence limit. Two deliveries, in order:
PR 1 (worker timeout) and PR 2 (VCI tools). Each gets its own implementation plan.*

## 1. Intent

Give an agent the TIA Portal Version Control Interface as plain primitives: create and delete workspaces, export
project objects into a workspace, synchronize mapped objects in either direction, delete mappings, and read
workspaces, mappings, their status and the formats an object supports. The tool provides the primitives and states
their limits. Maintaining a workspace, and anything Git does with it, belongs to the user.

**Success:** one `vci_write` call exports a whole PLC software (about 570 leaves) into a workspace laid out like the
TIA UI lays it out, with a raised worker timeout. A second call imports changed files back in one transaction that
rolls back as a whole on a thrown error. `vci_read` reports which mappings differ. A Portal crash or a worker timeout
mid-scope ends in counts plus the last attempted leaf, never in silence.

**Decided during brainstorming (2026-10-14 and 2026-10-15):**

| Decision | Reason |
| --- | --- |
| Tools are primitives; workspace upkeep and Git are the user's | Users own their repositories; the tool documents limits instead of orchestrating |
| Standalone `.ap21` only | No experience base for diagnosing local-session differences |
| Items take **uncapped scopes**; the worker expands them to leaves | Users export/sync whole devices; Openness works on leaves only (Q1) |
| Worker timeout becomes configurable, shipped first and separately | A whole-PLC scope takes 3.5–8 minutes against a fixed 5-minute timeout (Q5) |
| Results report **counts only**, plus the last attempted leaf on a crash or timeout | Which leaves differ is a read question (`vci_read`) |
| A per-leaf breadcrumb file covers the worker-timeout case | A killed worker cannot answer; the host reads the breadcrumb instead |
| Six write operations; no `connect`, workspace groups, VCI settings or library scopes | `connect` threw (Q15); groups, settings and library workspaces lack evidence (Q2, Q12); library import renames types (Q3, Q16) |
| PLC software and **Unified HMI** in scope; classic HMI rejected | Unified HMI exports only its scripts (SIMATIC SD `.yml` + `.js`); classic HMI exports nothing (maintainer) |
| New `vci_read` tool, available in every mode | `browse_project_tree` is not a batch tool, status costs ~0.5 s per mapping, and VCI's standalone-only gate does not fit tools that local sessions use. This supersedes the 2026-10-14 "reads in an existing read tool" decision |
| Optional `format` per item; fallback requested → `SimaticML` → the leaf's only format | The explicit format always wins (Q2); `SimaticML` refuses inconsistent objects cleanly and never crashed (Q10); HMI scripts support SIMATIC SD only |
| Imports run per item in one transaction; everything else runs plain | Rollback is proven (Q3/Q4); export files cannot be rolled back (Q4) |
| Export mirrors the project tree under the base directory | Matches the UI and removes the same-named-block overwrite (Q15) by construction |
| Sync touches only leaves whose source side changed; conflicts and target-side changes are skipped and counted | Nothing that changed on the other side is overwritten |
| `dryRun` is kept but cheap: it never reads status | Guards already block before mutation; status counts are `vci_read`'s job |

**Out of scope:** Git, diffs, workspace groups, `connect`, VCI settings, library scopes and library workspace
properties, classic HMI, `.als21` local sessions and server projects, importing unlinked files (Q14), MCP progress
notifications, automatic saving.

## 2. PR 1 — configurable worker timeout

- New startup option `--worker-timeout-minutes <n>` and environment variable `TIA_MCP_WORKER_TIMEOUT_MINUTES`,
  with the same precedence, parse-once-at-startup and error handling as `--max-item-chars` /
  `TIA_MCP_MAX_ITEM_CHARS`.
- **Raise-only:** 5 (today's `OpennessWorkerClient.DefaultRequestTimeout`) is the floor and default; 60 is the
  maximum. A value below 5 or above 60 is handled the way the budget options handle an out-of-range value.
- One value for every worker request, including `hello`; no per-method override. Production DI
  (`TiaMcpServer/Program.cs`) passes it into the existing `OpennessWorkerClient(requestTimeout:)` parameter.
- `doctor` reports the effective value and its source (default, option, environment).
- The client guide documents the client-side limit beside it: an MCP client applies its own per-request timeout
  (60 s in the MCP TypeScript SDK unless the client raises it; `MCP_TOOL_TIMEOUT` in Claude Code). A client that gives
  up does not stop the server; the call's audit record keeps the outcome.
- Tests: option and environment parsing (floor, maximum, precedence, rejection text), DI wiring, `doctor` output.

## 3. PR 2 — surfaces

| Layer | Addition |
| --- | --- |
| Tools | `vci_read(operations)`: read-only, every mode, `plc_read` batch model, 1–50 items, no cursor. `vci_write(operations, dryRun=false)`: read-write and full, an `IWriteDomain` on the guarded pipeline like `hmi_write`, `ConfirmsEveryCall=false`, no elicitation, audit v2. Root `contractVersion:"1.0"` on the canonical JSON contract. Counts move from 7/17/17 to **8/19/19**. |
| Strict arguments | `vci_read`: `operations`. `vci_write`: `operations`, `dryRun`. Per-operation field sets as in `PlcOperationCatalog`. |
| Gating | A new standalone-only list in `ProjectCapabilityCatalog` holding both tools and their worker methods, with explicit `vci_*` entries in `OperationFor`. Local sessions and server projects get `unsupported_capability` with no extra code. Worker reads are classified `Observe` and writes `ProjectMutation` in `OperationPolicyCatalog`. |
| Contracts | `WorkerRequest.VciQuery`, `WorkerRequest.VciWrite`, `WorkerResponse.VciWriteOutcome`; typed result DTOs that write null members and are decoded by the worker-payload reader; one CLR result type per operation. |
| Pure logic in Contracts | Status filter, format resolution, mirrored-path builder with representability check, path policy, breadcrumb matching (§7). |
| Worker | `Openness/Vci/`: scope expander, reader, writer, breadcrumb writer. Entered through `WithEngineeringRoot`, then cast to `Project`; the VCI service is `project.GetService<VersionControlInterface>()`. |
| Failures | New failure category `portal_lost` (§6). VCI-aware breadcrumb read after `worker_timeout`. |
| Stubs | `reference-stubs/Siemens.Engineering.Base/VersionControl.cs`, regenerated with `scripts/verify-reference-stubs.ps1 -Update`. The spike compiled against `Siemens.Engineering.Base.dll` alone, so no new reference is needed. |

## 4. Operations

**Scope selector.** `scope` is an ordered list of `{nodeType, name}` segments copied from `browse_project_tree`
output, starting at a Device, exactly like `read_cross_references`' target path. It may name PLC software, any
block, type, tag-table or technology-object group, a single leaf, a Unified HMI, or a script module. The worker
expands it to leaves and never passes a container to `ExportObject`, `Synchronize` or `GetSupportedFileFormats`
(Q1, Q7). A classic HMI, a library node, or a device with no supported software gives `target_kind_unsupported`;
an unresolved or ambiguous path gives `target_not_found` / `target_ambiguous`.

Every item carries `operationId` and `operation`. `workspaceName` is required everywhere except
`list_workspaces` and `supported_formats`.

### 4.1 `vci_read`

| Operation | Fields | Result |
| --- | --- | --- |
| `list_workspaces` | — | per workspace: `name`, `rootPath`, `mappingCount` |
| `list_mappings` | `workspaceName`; optional `scope`, `withStatus` (default false), `offset` (default 0), `limit` (1–200, default 100; 200 mappings stay inside the 60,000-character item budget) | `total`, and per mapping: `fileKey` (relative directory + file name), `format`, object `path` (segments, null when the object is gone), `status` (raw compare flags as Openness reports them, from `GetStatus()`; null unless `withStatus`) |
| `supported_formats` | `scope` naming one leaf | `formats` exactly as `GetSupportedFileFormats` returns them |

Paging is plain offset/limit with no snapshot; mappings that change between pages may shift. `withStatus` costs about
0.5 s per returned mapping (Q5); the tool description says so.

### 4.2 `vci_write`

| Operation | Extra fields | Behavior |
| --- | --- | --- |
| `create_workspace` | `rootPath` | Creates the root directory when missing, then `Workspaces.Create` |
| `delete_workspace` | — | Removes the workspace; every file stays (Q6) |
| `export` | `scope`; optional `relativeDirectory`, `format` | Per leaf: resolve the format (§4.3), build the mirrored path (§4.4), create its directory, `ExportObject`. Leaves already mapped in this workspace are skipped as `alreadyMapped` |
| `sync_to_workspace` | `scope` | Project → workspace for mapped leaves under the scope, filtered by status (§4.5) |
| `sync_to_project` | `scope` | Workspace → project, filtered by status (§4.5), in one transaction per item (§5) |
| `delete_mapping` | `scope` | Removes the mappings of the leaves under the scope; files stay (Q6) |

### 4.3 Format resolution

Use the requested `format` when the leaf supports it, otherwise `SimaticML`, otherwise the leaf's only supported
format (SIMATIC SD for Unified HMI scripts). Every substitution counts in `formatFallbacks`. SIMATIC SD is never a
fallback for a PLC leaf: it is used only when requested, and the tool description warns that a SIMATIC SD export
crashed the Portal on one OB (spike crash table). Global and workspace format settings are never read (Q2).

### 4.4 Mirrored layout

A leaf's file goes to `<relativeDirectory>/<device>/<software>/<category>/<groups…>/` with the leaf's name as the
file name; the format decides the extensions. PLC categories are `Program blocks`, `PLC tags`, `PLC data types`
and `Technology objects`, as the UI writes them; a Unified HMI writes `<hmi>/Scripts/`. The exact HMI level (device
or software name) is confirmed during live acceptance. Two PLCs with same-named blocks therefore never share a file key.

### 4.5 Status filter

Status always comes from `GetStatus()`, never the `Status` property, which lags after a sync (Q8). Per direction:

| Compare flags | `sync_to_workspace` | `sync_to_project` |
| --- | --- | --- |
| `Equal` | skipped `equal` | skipped `equal` |
| project changed only | synced | skipped `targetChanged` |
| workspace file changed or missing only | skipped `targetChanged` | synced (changed) / failed (missing) |
| both changed | skipped `conflict` | skipped `conflict` |

Running `Synchronize` on `Equal` items throws (Q5), so filtering happens before any call.

### 4.6 Item result

Counts only: `leaves`, `succeeded`, `failed`, `skipped {equal, alreadyMapped, conflict, targetChanged}`,
`formatFallbacks`, `lastAttempted` (null unless the item ended in `portal_lost` or a timeout: leaf path, operation,
format), `projectModified`. Fixed notes: "files remain on disk" for both deletes; "imported, not validated; run
`compile_check`" for `sync_to_project` (Q3); "call `save_project`; unsaved workspaces and mappings are lost on close"
whenever `projectModified` (Q9).

## 5. Validation, paths, guards, `dryRun`

**Validation** (canonical root error before binding, no worker call): 1–50 items, unique `operationId`s, strict
fields per operation, non-empty `scope` segments, `offset`/`limit` bounds, and the path policy.

**Path policy**, ported from the old `feature/workspace-operations` branch (`VciMutationPathPolicy`) into Contracts:

- `rootPath`: absolute local-drive path; no UNC, device prefix or ADS; not a drive root; not inside the project
  folder (`ArchiveDirectoryGuard.IsWithinProjectFolder`); no reparse-point ancestor; its parent must exist. An
  existing directory, including a Git repository root, is accepted.
- `relativeDirectory`: relative; no `..`, no ADS; stays inside the root after `GetFullPath`; no reparse point in
  its components. The tool creates the directories and passes the `\`-rooted form Openness requires (Q6).
- Existing private copies (`ArchiveDirectoryGuard`, `LifecyclePayloadContract.IsInside`, the block import stager)
  switch to the shared policy only where the swap is mechanical; otherwise they stay as they are.

**Guards** (planned per item over all of its leaves):

| Guard | Severity | Condition |
| --- | --- | --- |
| `vci_file_key_collision` | block | An export target file already exists on disk, or another mapping in the workspace uses the key (Q15, Q6) |
| `vci_name_not_representable` | block | A mirrored segment is a reserved or invalid file name or ends in a dot or space, or the full path exceeds 259 characters; names the first offending leaves |
| `vci_workspace_exists` | block | `create_workspace` with a duplicate name, or a root another workspace of the project already uses |
| `vci_state_unverifiable` | block | The scope or the workspace mappings cannot be read during planning |
| `vci_files_remain` | info | `delete_workspace`, `delete_mapping` |
| `vci_import_unvalidated` | info | `sync_to_project` |

**`dryRun`** returns `preview` with exactly what planning computes: scope expansion, leaf counts, format fallbacks
and guard outcomes. Planning never reads status, in either mode; sync status is read once, during mutation. The tool
description points to `vci_read list_mappings withStatus` for status counts.

## 6. Execution and failures

**Per item**, the worker resolves and expands the scope, then processes leaves in order. Before each leaf it
overwrites the breadcrumb (§6.3).

- `export`, `sync_to_workspace`, `delete_mapping`, `delete_workspace`, `create_workspace` run plain. A leaf refused
  by Openness (for example "object(s) is not consistent", Q10) counts as failed and the item continues.
- `sync_to_project`: the status filter runs **first, outside the transaction**; then `ExclusiveAccess` and one
  `Transaction` cover every candidate leaf's `Synchronize`. Any thrown leaf error makes `CanCommit` false (Q4): the
  transaction is disposed without commit, the project reverts (Q3), and all candidate leaves count as failed.
  Otherwise it commits. The TIA UI is locked for the length of the import; the tool description says so. An imported
  object is recreated (Q3); later steps re-resolve it through the mapping instead of reusing an old reference.
- **Across items** the pipeline's stop-at-first-failure applies: an item with `failed > 0`, or that ends in
  `portal_lost`, is not Succeeded, and the items after it are skipped. No rollback across items.

### 6.1 Portal crash

A `NonRecoverableException` stops the item and the call at once. The worker survives (it is a separate process
from the Portal) and returns the item with category `portal_lost`, the counts so far and `lastAttempted`. The host
invalidates the binding immediately rather than at the next call. Guidance: restart TIA Portal and reopen the
project; exported files remain; unsaved workspaces, mappings and project edits are lost; do not retry the same leaf
unattended. The real exception is only in the Portal's crash report (spike, "Portal crashes"); the documentation
says where to find it.

### 6.2 Worker timeout

The existing transport kills the worker and returns `worker_timeout` with an unknown dispatch state and an
invalidated binding. For a `vci_*` method the host then reads the breadcrumb and, when it matches the request,
adds `lastAttempted` to the error and the audit. Guidance: the leaf may still be completing inside TIA Portal;
inspect with `vci_read` before retrying.

### 6.3 Breadcrumb

One small JSON file under `%LOCALAPPDATA%\TiaMcpServer`, overwritten by the worker before each leaf with the worker
request id, `operationId`, operation, leaf path and format, and removed when the request completes. The host uses it
only when the request id matches the timed-out request; a stale or foreign file is ignored and removed. A failure to
write the breadcrumb never fails the operation.

### 6.4 Verification and audit

Verification checks existence only: exported leaves have mappings; deleted mappings and workspaces are gone; a
created workspace exists. It does not re-read status, which would double the cost; the verification block says sync
results are not status-verified and points to `vci_read`. Each call writes one audit v2 record with per-item counts
and `lastAttempted`.

## 7. Testing

The decision logic is pure functions in Contracts, linked into `TiaMcpServer.Tests`; the worker only makes Openness
calls, like the PLC and HMI writers.

| Area | Coverage |
| --- | --- |
| Pure logic | Status filter (every row of §4.5, both directions), format resolution, mirrored-path builder and representability, path policy (the old branch's suite minus the two dropped rules), breadcrumb matching |
| Catalogs | Strict fields per operation, item limits, unique ids, `offset`/`limit` bounds |
| Guards and `dryRun` | Each guard; `dryRun` yields `preview` and the worker plan never reads status |
| Contracts | Typed decode per operation, `protocol_error` without echo; `ConditionalMemberRegisterTests`; `WorkerPayloadNullPolicyRegisterTests` |
| Registers | Success and rejection probes for both tools in `ToolOutputContractConformanceTests`; 8/19/19 in `WriteToolMcpAnnotationProtocolTests`, `ReadOnlyModeTests`, `AccessModeDiscoveryTests`, `McpToolSchemaTests`; local session gets `unsupported_capability` in `LocalSessionContextContractTests` |
| Failures (FakeWorker) | `portal_lost` invalidates the binding immediately; timeout with a matching breadcrumb yields `lastAttempted`; a stale or foreign breadcrumb is ignored |

## 8. Live acceptance

On a disposable standalone copy; the report names no project, device or file.

1. Every operation on a mixed LAD/SCL/DB group: fallback counts and the mirrored layout on disk.
2. Unified HMI script module: export and `sync_to_project` (no spike evidence exists).
3. A conflict and a target-side change, both skipped and counted.
4. A collision with leftover files blocks; save, reopen, mappings persist.
5. Whole-PLC export (about 570 leaves) with the default 5-minute timeout: a real timeout and breadcrumb. Then with a
   raised timeout, timed.
6. `portal_lost` through the known crash repro (SIMATIC SD on the unexportable OB), only with the maintainer's
   explicit go-ahead at that moment.
7. Read-only mode exposes `vci_read` and hides `vci_write`.
8. Transaction rollback on a thrown leaf error, if a reliable throwing case exists; malformed content does not throw
   (Q3), so the absence of one is recorded as an evidence limit.

## 9. Documentation (last task, after live acceptance)

New `docs/SupportedOperations/VCI_OPERATIONS_SUMMARY.md` with every limit in this spec, listed in `docs/README.md`;
`README.md` tool list and counts (absolute links); `AGENTS.md` counts and a VCI paragraph; `docs/ARCHITECTURE.md`;
`PROJECT_OPERATIONS_SUMMARY.md` (the "not provided" line); `IMPROVEMENT_LOG.md` follow-ups: library scopes,
`connect`, workspace groups, VCI settings, MCP progress notifications, local sessions. PR 1 documents its own option
and the client-side timeout in the client guide.

## 10. Evidence limits carried into the design

- Unified HMI behavior (formats, layout level, import) is unproven until live acceptance step 2.
- `sync_to_project` on a conflict was never run (spike); conflicts are skipped, so the design never runs it either.
- Whether `ExclusiveAccess` alone speeds up exports was not measured; exports run plain.
- `Unknown` status was never produced (no `InitializeStatus` in V21); it is passed through raw and treated like a
  conflict by the filter.
- No F-CPU run, no STL or GRAPH blocks; their behavior follows the generic leaf rules.
