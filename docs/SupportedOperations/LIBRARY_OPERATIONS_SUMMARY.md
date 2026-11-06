# TIA Portal Library Operations

`library_read` is the read-only library tool. It is registered in every access mode (counts 9/20/20), never opens, retrieves, saves, closes or changes a library, and runs up to 50 items per call on the structured JSON contract (`contractVersion:"1.0"`). Items run independently, so a failing item does not stop later ones. Every call needs a verified project binding, including a batch made only of global reads: an unbound call fails whole with `binding_conflict`, so call `bind_project` first. Global reads use the bound Portal.

This is the read half of issue #218. Library writes are not part of this surface.

The scoped [live verification report](../superpowers/acceptance/reports/2026-11-06-library-read-live-verification.md) covers one bound standalone project and one opened user global library. This page states what that report observed and marks everything else as offline-only or unverified.

## Operations

| Operation | Scope | Selectors | Paged |
| --- | --- | --- | --- |
| `list_global_libraries` | none (the bound Portal) | none | yes |
| `inspect_library` | `project` or `global` | library | no |
| `list_children` | `project` or `global` | library, `folderKind` (`types` or `masterCopies`), optional `folderPath` | yes |
| `list_versions` | `project` or `global` | library, `typeGuid`, optional `folderPath` | yes |
| `list_dependencies` | `project` or `global` | library, `typeGuid`, `versionGuid`, optional `direction` (`dependencies` default, or `dependents`), optional `folderPath` | yes |

The items are flat objects: `operationId`, `operation`, and the optional `scope`, `libraryPath`, `libraryName`, `folderKind`, `folderPath`, `typeGuid`, `versionGuid`, `direction`, `offset`, `limit`, `cursor`. An unknown member or a field that does not belong to the operation is a `validation_error` that rejects the whole call.

- **Project scope** reads the bound project's own library. It takes no `libraryPath` or `libraryName`; the identity comes from the binding.
- **Global scope** needs a non-blank `libraryPath` (absolute drive or UNC path with at least server and share) or `libraryName`. When both are given, both must match. The name is compared exactly (ordinal); the path is normalized with `Path.GetFullPath` and compared case-insensitively. A relative or malformed path is a `validation_error`.
- **`folderPath`** is a list of exact folder names (at most 32 non-blank segments), never a slash-delimited string. Omit it for the root of the selected folder.
- **Version operations** take no `folderKind`. The type lookup is non-recursive within the selected type folder.
- `typeGuid` and `versionGuid` must be non-empty GUIDs.

### Global libraries

`list_global_libraries` enumerates `TiaPortal.GlobalLibraries.GetGlobalLibraryInfos()`, so it lists every global library available in the bound Portal (system, corporate and user), not only opened ones. Each row carries `libraryType` (`system`, `corporate` or `user`; null with a row diagnostic when the native value is unknown or no single info matches) and `isOpen`. Both are explicit null on project-library rows and on every non-library row.

Only an opened library can be read further. For an unopened row, `isReadOnly` comes from the info, while `isModified`, `isWriteProtected` and `nativeType` are null, and the row carries a diagnostic that the library is not open.

The other four operations with `scope:"global"` find only libraries open in the bound Portal. A selector that matches only an unopened library fails `target_not_found`, with a message that the library is available but not open (open it in TIA Portal first; nothing was opened or changed). A selector matching nothing fails `target_not_found` ("found none"). Several matches would fail `target_ambiguous` (offline only; see the limits below). A global `inspect_library` row carries `isOpen:true` and `libraryType` from the matching info.

The server never opens a library for you. A global library that is closed in TIA Portal cannot be read until a person opens it there.

## Result shape

Every operation returns one page, `{items, total, offset, nextOffset, nextCursor, complete, diagnostics}`. `inspect_library` returns a one-row page. Each row is `{identity, namespace, versionNumber, state, isDefault, isReadOnly, isWriteProtected, isModified, libraryType, isOpen, nativeType, diagnostics}`.

- `identity` is `{scope, libraryPath, libraryName, folderKind, folderPath[], kind, name, typeGuid, versionGuid}`. `kind` is `library`, `folder`, `type`, `version` or `masterCopy`. Folder paths are segments.
- Every flag (`isDefault`, `isReadOnly`, `isWriteProtected`, `isModified`, `isOpen`) is nullable. A flag that cannot be read is null with a diagnostic, never `false`. A flag that does not apply to a row kind (for example `isDefault` on a folder) is null without a diagnostic.
- For a project library, `isModified` is the bound project's own modified flag (the row diagnostic says so), and read-only and write-protection state are null with a diagnostic. `nativeType` is `ProjectLibrary`.
- Row order: `list_global_libraries` by name then path; `list_children` folders before entries, each by name (ordinal); `list_versions` by version number; `list_dependencies` by owner path, then type name, then version.
- Errors: `validation_error`, `binding_conflict`, `target_not_found`, `target_ambiguous`, `unsupported_capability`, `invalid_cursor`, `cursor_filter_mismatch`, `cursor_binding_mismatch`, `snapshot_unavailable`, `cursor_snapshot_mismatch`, `protocol_error`, `worker_operation_failed`, `portal_lost`.
- A value over the value budget (60,000 characters by default) is omitted whole with guidance to lower `limit` or re-run the `operationId` alone; the budgets can be raised at startup with `--max-item-chars`/`--max-document-chars`.

## Cursor paging

The four paged operations take `offset` (default 0) and `limit` (1 to 200, default 100). Whenever more rows exist, the page carries `nextCursor`; pass it back as `cursor`, instead of `offset`, with the same operation and selectors to continue the same scan. `limit` may differ between pages (offline only).

- **Frozen order, live values.** The first page of a paged scan enumerates every row in the order above and freezes that ordered set in a worker snapshot. A continuation pages the frozen set and reads each row's property values live. A row whose object no longer exists fails only that row with a diagnostic (offline only). Rows added after the first page are not included; a call without a cursor sees them (offline only).
- **Lifetime.** The worker keeps at most 4 library snapshots, least recently used evicted first, each dropped after 10 idle minutes. Library snapshots use their own store, separate from HMI's, so neither evicts the other. A snapshot is not dropped when the last page is read: a page-1 cursor still worked after the last page (live observation).
- **`isOpen` and open flags** are re-evaluated against the current composition on every page (offline only).
- **Failures.** `cursor` together with `offset`, or on `inspect_library`, is a `validation_error` for the whole call. Checks before any worker call:

  | Condition | Category |
  | --- | --- |
  | blank, malformed or tampered cursor, or (offline only) one issued by an earlier server process | `invalid_cursor` |
  | different operation or any selector changed (the hash excludes `offset`, `limit` and `cursor`) | `cursor_filter_mismatch` |
  | project binding changed since the cursor was issued (offline only) | `cursor_binding_mismatch` |

  In the worker, an expired or evicted snapshot is `snapshot_unavailable` and a snapshot from another project or operation is `cursor_snapshot_mismatch` (both offline only). Restart without a cursor in each case. A continuation that restates the default `direction` explicitly is accepted.

## Containers and capability labels

| Reads | Standalone project | Local session | Server project |
| --- | --- | --- | --- |
| global (`list_global_libraries`, `scope:"global"`) | yes | offline only | `unsupported_capability` (capability label `notYetDelivered`; offline only) |
| project (`scope:"project"`) | yes | `unsupported_capability` (offline only) | `unsupported_capability` (capability label `standaloneOnly`; offline only) |

Project-library reads on a local session are refused with `unsupported_capability` because `library_project_read` is a standalone-only capability until a separate probe covers local sessions; the same applies to server projects.

Capability labels in the `bind_project` context are not one-to-one with the tool name:

- `library_read` is reported as `projectContent` (and `notYetDelivered` on server projects), although it reads Portal-scope libraries. It gates the global reads.
- `library_project_read` appears in the context capabilities as `standaloneOnly`. It is not a tool name; it gates the project-library reads, like `vci_read`/`vci_write`.

`ProjectCapabilityCatalog.OperationFor` maps the worker methods (`library_list_global_libraries`, `library_global_*` and `library_project_*`) to those two capabilities.

## Failure behavior

- A library closed in the UI between pages fails the item `worker_operation_failed` and keeps the binding. It is `portal_lost` only when the Portal attachment itself was disposed during the execution (offline only).
- The worker raises `binding_conflict` only for identity or Portal-attachment loss, because the host invalidates a verified binding on any worker-reported `binding_conflict`.
- Item member names are matched case-insensitively by the SDK deserializer; unknown names and miscased root keys are rejected. This is repo-wide, not specific to this tool.

## Live-verified scope and limits

Verified (one bound standalone project, one opened read-only user global library; see the [report](../superpowers/acceptance/reports/2026-11-06-library-read-live-verification.md)):

- unbound call `binding_conflict`; 9 tools in read-only and 20 in read-write;
- `list_global_libraries` over all available libraries, with `libraryType`, `isOpen` and the not-open diagnostic, and paging that matched the full listing;
- global and project `inspect_library`, `list_children` (folders before entries), `list_versions` and `list_dependencies` in both directions;
- selector rules, the unopened-library `target_not_found`, and cursor paging with the `offset`, tamper and selector-change failures;
- no library or project file changed during the final candidate's run.

An earlier run observed a one-time rewrite of library storage files inside an opened library's folder that the report cannot attribute (the working explanation is a Portal-side compaction or save); the final candidate's run changed nothing.

Not verified: local sessions (global reads succeeding and project-library `unsupported_capability` are offline only); `cursor_binding_mismatch` after a rebind; a library closed, a library changed, or the Portal lost between pages; duplicate global library names (`target_ambiguous`). Master-copy folders were observed empty only.

## Known limits

- Global reads need a verified binding. Reading global libraries with no project open is a follow-up.
- No write operation exists. `library_write` is a separate, later surface.

See [IMPROVEMENT_LOG](../IMPROVEMENT_LOG.md) for open follow-ups and ADR-0015 ([Portal-scope reads under the bound-project gate](../adr/0015-portal-scope-reads-under-binding-gate.md)) for the binding decision.
