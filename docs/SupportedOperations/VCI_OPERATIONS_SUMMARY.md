# Version Control Interface (VCI) operations

`vci_read` is available in every access mode; `vci_write` is available in read-write and full. Both require a verified, already-open **standalone** project. Local sessions and server projects return `unsupported_capability`. These tools manage TIA Portal V21 VCI workspaces and mappings; they do not run Git, connect an unlinked file, or save the project automatically. Current discovery counts are **8/19/19** (read-only/read-write/full). The installed connector was not changed by the [scoped candidate acceptance](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md).

Both tools return one canonical JSON document as matching text and `structuredContent`, with `contractVersion:"1.0"`, typed results, explicit nulls and no nested JSON strings. Calls contain **1–50** operations with unique, nonblank `operationId` values of at most **256 characters** and one project path. Unknown operations, missing/extra operation fields, invalid bounds, and malformed scopes fail strict validation. Effective response budgets default to **60,000 characters per value** and **180,000 per document**, and can be raised at startup through the [response size options](../guides/mcp-client-configuration.md#response-size-limits). Whole values may be omitted with narrowing guidance; inspect `omission` and item status before treating a response as complete.

## Reads

Each read item has `operationId` and `operation`; an optional `projectPath` asserts the bound project. Read items run independently, so one failed item does not stop the others.

| Operation | Allowed fields beyond the common fields | Result |
| --- | --- | --- |
| `list_workspaces` | None | `workspaces[]` with `name`, nullable `rootPath`, and `mappingCount` |
| `list_mappings` | Required `workspaceName`; optional `scope`, `withStatus` (default `false`), `offset` (default `0`), `limit` (default `100`, range **1–200**) | `total`, `offset`, `limit`, and `mappings[]`: relative `fileKey` (`directory`, `fileName` stem), `format`, nullable selector `path`, nullable raw `status` (`compareState`, `details`) |
| `supported_formats` | Required `scope` naming exactly one leaf; optional `workspaceName` | `formats[]` exactly as the selected workspace's `GetSupportedFileFormats` returns them |

When `supported_formats` omits `workspaceName`, it uses the project's first workspace; if none exists, it returns `target_not_found`. `list_mappings` uses plain offset paging, **without a snapshot**. Mappings added or removed between pages can shift rows. It retains software-unit and system-block mappings, even though those scopes cannot be written; a deleted or library object has `path:null`. An ambiguous or unreadable owner is never guessed into a selector. A scope filter includes only mappings with a resolved matching path.

`withStatus:true` calls native `GetStatus()` for returned mappings; it does not read the potentially stale `Status` property. The observed cost is about **0.5 seconds per mapping** and varies with TIA and project state. Omit it for inventory, then use a narrow status read when deciding whether to synchronize. Planning and `dryRun` do not read status.

## Scopes, files and formats

`scope`, when supplied, is a nonempty Device-rooted sequence of `{nodeType,name}` segments. PLC paths use `Device → PlcSoftware` followed by `BlockFolder` (and optional nested folders) to an `OB`, `FB`, `FC`, `GlobalDB`, `InstanceDB`, `ArrayDB` or `Block`; `TagTableFolder → TagTable`; `TypeFolder → Type`; or `TechnologyObjectFolder → TechnologyObject`. Unified HMI scopes use **Device → HmiSoftware → ScriptModule**; obtain HMI identity from `hmi_read list_hmi_devices` or a resolved mapping path. `browse_project_tree` does not emit HMI or technology-object nodes. A container scope expands to supported **leaves**; native export, format and sync calls never receive a container. A directly selected `SoftwareUnit` or `SystemBlockFolder` returns `target_kind_unsupported`; expansion skips these branches. Classic HMI, library and devices without supported software are outside this VCI scope. Missing or ambiguous selectors return `target_not_found` or `target_ambiguous`.

Export mirrors each leaf under `<relativeDirectory>/<owner>/<category>/<user groups...>/<leaf>`, matching the folders a TIA Portal UI export creates. `<owner>` follows the UI project tree: a hardware station is hidden, so a PLC uses its software name and a Unified panel its device name; a PC station (device type `System:Device.PC` or an `OrderNumber:…//Device` IPC) uses `<device>/<software>` for both PLC and HMI. A station name therefore never blocks a hardware-station export. The root folder segment is replaced by `Program blocks`, `PLC tags`, `PLC data types`, `Technology objects`, or Unified HMI `Scripts`. The native format chooses the extension. Mappings use relative, case-insensitive directory plus file-stem keys, even though native `MappedObject.DirectoryPath` is absolute. Renaming a TIA object retains its file key; deleting a mapping or workspace leaves files on disk. An unlinked file placed in a workspace is not imported.

The requested `format`, if supported by that leaf, wins (case-insensitive match, native spelling retained). Otherwise the tool uses `SimaticML` if supported, then the sole supported format; each substitution from `format ?? "SimaticML"` increments `formatFallbacks`. A PLC leaf never falls back to `SimaticSD`, though an explicit supported request may use it. A Unified script can use its sole `SimaticSD` format. The tool does not read global or workspace export settings. **`SimaticSD` export of one SCL OB crashed the Portal in the [live spike](../superpowers/acceptance/reports/2026-10-15-vci-spike.md#portal-crashes-cause-and-evidence); do not run that known case unattended.** Other formats and objects are not thereby certified safe.

`rootPath` for a new workspace must be an absolute local-drive path with an existing parent. Drive roots, the user-profile root, a path inside the bound project folder, UNC/device paths, alternate data streams, traversal and reparse-point ancestors are rejected. An existing directory, including a Git repository root, is allowed. `relativeDirectory` must stay within the workspace root and cannot use absolute paths, `..`, ADS or reparse points. Mirrored segments cannot contain invalid/reserved Windows names or end in a dot/space. The full represented path, with extension allowance, is limited to **259 characters**. The project-folder rule fails validation at plan time; other path checks are also repeated near mutation.

## Writes and safeguards

Every write item has `operationId`, `operation`, `workspaceName`, and optional `projectPath`. `vci_write(operations, dryRun=false)` executes when `dryRun` is omitted or false; `dryRun:true` returns `preview` with planned leaf counts, effects and guards, without mutation or status reads. It sends **no server elicitation** in either writable mode. Client permission and request timeout settings still apply.

Binding-gate failures preserve their original category, including `worker_timeout`, `worker_crashed`,
`portal_lost` and `protocol_error`, in the top-level error and audit. A gate denial stops before planning
or mutation; a missing, mismatched or changed binding is `binding_conflict`.

| Operation | Additional fields | Effect |
| --- | --- | --- |
| `create_workspace` | Required `rootPath` | Create a missing root directory, then a project VCI workspace |
| `delete_workspace` | None | Delete the workspace; files remain |
| `export` | Required `scope`; optional `relativeDirectory`, `format` | Export supported leaves; already mapped leaves skip as `alreadyMapped` |
| `sync_to_workspace` | Required `scope` | Synchronize eligible mapped leaves from project to workspace |
| `sync_to_project` | Required `scope` | Synchronize eligible mapped leaves from workspace to project |
| `delete_mapping` | Required `scope` | Delete mappings for selected leaves; files remain |

The blocking guards are `vci_file_key_collision` (another mapping owns the relative key or a target `<stem>.*` file already exists), `vci_name_not_representable`, `vci_workspace_exists` (duplicate workspace name or root), and `vci_state_unverifiable`. Their evidence lists at most the first **10** offending leaves. `vci_files_remain` and `vci_import_unvalidated` are informational. Export checks mapping keys and existing files again around directory creation and immediately before the native call; it never intentionally overwrites a target. Those explicit scans grow with **leaves × mappings**. A bounded three-leaf live export passed, but no scan reduction, whole-project completion or general speedup was established. Native `Find` complexity is unknown; [original bulk timeout evidence](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md) remains relevant.

Items execute in caller order. An item naming the same workspace (case-insensitive) depends on the nearest earlier such item and is planned again after it; `create_workspace` then `export` can therefore share one call. A leaf refusal increments `failed` and processing continues within the item, but the batch **stops after that failed item**; later items are skipped. Earlier completed items are never rolled back or replayed. Results give `leaves`, `succeeded`, `failed`, `skipped {equal, alreadyMapped, conflict, targetChanged}`, `formatFallbacks`, nullable `lastAttempted`, `projectModified` and notes. `lastAttempted` is failure context for Portal loss or a matched worker timeout/crash, not proof that the native action finished.

The sync decision uses a fresh `GetStatus()` result, not `Status`:

| Native compare | To workspace | To project |
| --- | --- | --- |
| `Equal` | Skip `equal` | Skip `equal` |
| `Unequal` / project changed only | Synchronize | Skip `targetChanged` |
| `Unequal` / workspace file changed only | Skip `targetChanged` | Synchronize |
| `WorkspaceFileMissing` | Skip `targetChanged` | Count failed |
| Both changed, `Unknown`, or unrecognized flags | Skip `conflict` | Skip `conflict` |

`sync_to_workspace` re-resolves the original selected leaf, verifies mapping ownership, and reads plain status immediately before each native `Synchronize` call. `sync_to_project` first filters by status **outside** ExclusiveAccess; it then uses one ExclusiveAccess and transaction for candidate leaves. A thrown leaf prevents commit and counts the candidates as failed; the specified prefilter window remains, so concurrent edits can still race between status and import. Import can recreate the engineering object; the worker re-resolves mapping and selector ownership before native use. These sequential checks are not atomic against external remapping or changes between leaves. A successful import is **not validated**: run `compile_check` on the affected PLC. The TIA UI is locked during the import transaction. An all-`Equal` import opens no transaction.

Workspaces and mappings persist only after an explicit `save_project`; `projectModified:true` carries that reminder. `sync_to_workspace` can change exported files without marking the project modified. Verification checks that exported mappings or created workspaces exist and deleted mappings/workspaces are absent; it does **not** re-read sync status. The last item for a workspace is checked; earlier same-workspace items may be marked `superseded`. A sync verification reports `notStatusVerified`; use `vci_read list_mappings` with `withStatus:true` for fresh state. Every entered write, including preview or refusal, has one audit-v2 record with delivered document/hash and item counts. VCI confirmation is `none` in read-write and policy-derived in full; there is no VCI prompt.

## Portal loss, timeout and retry

VCI read and planning now use the worker's shared fatal-exception mapping. The
VCI writer retains its own `portal_lost` mapping so partial counts, `lastAttempted`
and persistence notes survive. This B-16/Q3 consolidation has offline coverage
and maintainer-accepted scoped live read testing; native write interruptions
remain untested. Candidate and fixture limits are retained in the
[implementation record](../superpowers/plans/2026-10-19-worker-portal-loss.md).

A native `NonRecoverableException` is reported as `portal_lost` and invalidates the binding immediately. It is a generic client exception: the real cause may be in the Portal report at `<ProgramData>\Siemens\Automation\Portal V21\Diagnostics\<build>\<id>\<id>.dr`, a ZIP containing `ErrorReport.xml` (`Reason="Openness"` in the observed crash). A worker timeout or crash also invalidates binding. Before each attempted leaf the worker writes a breadcrumb under `%LOCALAPPDATA%\TiaMcpServer`; on timeout/crash the host uses it only if its request ID matches, and may report `lastAttempted` in the error and audit. A missing breadcrumb does not prove that no leaf ran.

After loss, inspect TIA Portal and files, restart if needed, and prove a **fresh zero-owner attachment** before explicitly opening the exact destination with `forceRebind:true` when required. Use `bind_project` for an already-open exact project; reads and writes never bind, open or replay implicitly. Unknown ownership, another project owner or identity drift must deny recovery. Unsaved workspaces, mappings and project edits may be lost while exported files remain. Inspect exact selectors and status before any retry: the timed-out leaf may still be completing inside Portal. The worker timeout defaults to **5 minutes** and accepts whole minutes **5–60** at startup; it does not extend an MCP client's own request timeout. Configure both for long operations.

The [live report](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md#completed-follow-up-checks--2026-10-16) records affected repaired-candidate checks and restoration. It preserves the original frozen candidate's failed verdict and partial bulk timeouts. A controlled, graceful restart recheck did **not** repeat or fix the known native exporter crash. Unknown status, thrown-leaf rollback, current-candidate HMI import/runtime behavior, local sessions, STL/GRAPH and concurrent live races remain unqualified. The successful PLC compile in that report applies only to its temporary import case.
