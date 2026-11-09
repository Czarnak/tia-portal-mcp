# TIA Portal Library Operations

`library_read` is the read-only library tool. It is registered in every access mode (counts 9/21/21), never opens, retrieves, saves, closes or changes a library, and runs up to 50 items per call on the structured JSON contract (`contractVersion:"1.0"`). Items run independently, so a failing item does not stop later ones. Every call needs a verified project binding, including a batch made only of global reads: an unbound call fails whole with `binding_conflict`, so call `bind_project` first. Global reads use the bound Portal.

This is the read half of issue #218. Library writes are the separate `library_write` tool, described in [Writes](#writes-library_write).

The scoped [live verification report](../superpowers/acceptance/reports/2026-11-06-library-read-live-verification.md) covers one bound standalone project and one opened user global library. This page states what that report observed and marks everything else as offline-only or unverified.

## Operations

| Operation | Scope | Selectors | Paged |
| --- | --- | --- | --- |
| `list_global_libraries` | none (the bound Portal) | none | yes |
| `inspect_library` | `project` or `global` | library | no |
| `list_children` | `project` or `global` | library, `folderKind` (`types` or `masterCopies`), optional `folderPath` | yes |
| `list_versions` | `project` or `global` | library, `typeName` or `typeGuid`, optional `folderPath` | yes |
| `list_dependencies` | `project` or `global` | library, `typeName` or `typeGuid`, optional `versionNumber` or `versionGuid` (default version when omitted), optional `direction` (`dependencies` default, or `dependents`), optional `folderPath` | yes |

The items are flat objects: `operationId`, `operation`, and the optional `scope`, `libraryPath`, `libraryName`, `folderKind`, `folderPath`, `typeName`, `typeGuid`, `versionNumber`, `versionGuid`, `direction`, `offset`, `limit`, `cursor`, `includeMetadata`. An unknown member or a field that does not belong to the operation is a `validation_error` that rejects the whole call.

- **Project scope** reads the bound project's own library. It takes no `libraryPath` or `libraryName`; the identity comes from the binding.
- **Global scope** needs a non-blank `libraryPath` (absolute drive or UNC path with at least server and share) or `libraryName`. When both are given, both must match. The name is compared exactly (ordinal); the path is normalized with `Path.GetFullPath` and compared case-insensitively. A relative or malformed path is a `validation_error`.
- **`folderPath`** is a list of exact folder names (at most 32 non-blank segments), never a slash-delimited string. For `list_children`, omit it for the root. For type selectors, omit it to search the whole type tree, supply `[]` to select only root types, or a path to narrow to that folder.
- **Type selectors** take exactly one of `typeName` (ordinal exact name) or `typeGuid`. Multiple matches fail `target_ambiguous` with candidate paths and GUIDs. Version operations take no `folderKind`.
- **Version selectors** take at most one of `versionNumber` or `versionGuid`; `list_dependencies` uses the default version when neither is given. `versionNumber` has three numeric parts, each at most 2147483647, and is not `0.0.0`. GUIDs must be non-empty.

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

## Global-library metadata

`inspect_library` accepts `includeMetadata:true` only with `scope:"global"`. It reads getters on the
already-open library and adds `metadata` to the row. Omitted or false skips those getters and omits
the member. Project scope accepts omitted/false; true is `validation_error`. The flag on any other
operation is `validation_error`, including false. It adds no operation or cursor field.

The same object appears on every successful `create_global` result, with or without an author:

| Metadata members | Values |
| --- | --- |
| `author`, `version`, `family`, `copyright`, `lastModifiedBy` | string or null |
| `creationTime`, `lastModified` | ISO 8601 string or null |
| `sizeKilobytes` | integer or null |
| `comments` | array of `{culture,text}` or null, sorted by culture (ordinal), including empty texts |
| `supportedLanguages`, `activeLanguages` | sorted culture arrays or null |
| `editingLanguage`, `referenceLanguage` | culture or null, as reported by Portal |
| `usedProducts` | `{name,version}` array or null, first 100 in native order |
| `history` | `{dateTime,text}` array or null, first 50 in native order |
| `complete`, `diagnostics` | boolean and string array |

Nested string members can be null. A recoverable property failure returns null for that member with
`"<member> unavailable: <ExceptionType>"` in `diagnostics` and `complete:false`; it does not fail the
read or creation. A native null, including an unset editing language, does not itself add a failure.
Cutting products/history adds a cap diagnostic and makes `complete:false`. Comments, author text and
language arrays are returned whole. If the containing value exceeds the effective item/document
budget, it is omitted whole; retry the read alone or raise startup `--max-item-chars` and
`--max-document-chars`. Unpaged inspection has no smaller page size to request.

The [PR A live report](../superpowers/acceptance/reports/2026-11-09-library-metadata-pr-a-live-verification.md)
qualifies global metadata, author admission and the read-only user-library case through the built MCP.
System-library metadata was not exercised in this acceptance run; earlier direct native probes are
separate evidence. Comment and language editing remain deferred to PR B.

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

- unbound call `binding_conflict`; 9 tools in read-only and 20 in read-write (before `library_write` existed);
- `list_global_libraries` over all available libraries, with `libraryType`, `isOpen` and the not-open diagnostic, and paging that matched the full listing;
- global and project `inspect_library`, `list_children` (folders before entries), `list_versions` and `list_dependencies` in both directions;
- selector rules, the unopened-library `target_not_found`, and cursor paging with the `offset`, tamper and selector-change failures;
- no library or project file changed during the final candidate's run.

An earlier run observed a one-time rewrite of library storage files inside an opened library's folder that the report cannot attribute (the working explanation is a Portal-side compaction or save); the final candidate's run changed nothing.

Not verified: local sessions (global reads succeeding and project-library `unsupported_capability` are offline only); `cursor_binding_mismatch` after a rebind; a library closed, a library changed, or the Portal lost between pages; duplicate global library names (`target_ambiguous`). Master-copy folders were observed empty only.

## Writes (`library_write`)

`library_write(operations, dryRun=false)` provides 20 operations in read-write and full (tool counts 9/21/21). Write contract `2.0`; `library_read` stays `1.0`. It takes 1-50 items with unique `operationId`s (at most 256 characters), runs them in caller order, stops at the first failure and never rolls back the batch or replays. Every call needs a verified binding, global-only calls included. An unknown or inapplicable member rejects the whole call with canonical `validation_error` and no audit record. Each entered call writes one audit v2 record.

### Operations and selectors

In this table, **library** means `libraryPath` or `libraryName` for global scope; project scope takes neither. **Type** means exactly one of `typeName` or `typeGuid`, with optional `folderPath` as described above. **Version** means at most one of `versionNumber`, `versionGuid`, or `inWork:true`; omission selects the default except where stated.

| Operation | Required members | Optional members | Native route |
| --- | --- | --- | --- |
| `create_global` | `directory`, `name` | `author` | `GlobalLibraries.Create` |
| `open_global` | `filePath` (`.al21`), `openMode` (`readOnly`/`readWrite`) | none | `GlobalLibraries.Open` |
| `retrieve_global` | `filePath` (`.zal21`), `directory`, `openMode` | none | `GlobalLibraries.Retrieve` |
| `save_global` | `scope:"global"`, library | none | `Save` |
| `close_global` | `scope:"global"`, library, `closeAction` (`save`/`discard`) | none | `Save` when modified, then `Close` |
| `archive_global` | `scope:"global"`, library, `directory`, `name` | none | `Archive` (`Compressed`) |
| `create_folder`, `delete_folder` | `scope`, `folderKind` (`types`/`masterCopies`), `name` | library, `folderPath` | `Folders.Create`, `Delete` |
| `create_master_copy` | `scope`, `plcName`, `plcKind` (`block`/`plcType`/`tagTable`), `plcObjectName` | library, `folderPath`, `plcFolderPath` | `MasterCopies.Create` |
| `copy_master_copy` | `scope`, `sourceScope`, `name` | both libraries and both folder paths | `MasterCopies.CreateFrom` |
| `instantiate_master_copy` | `scope`, `name`, `plcName`, `plcKind` (`blockGroup`/`typeGroup`/`tagTableGroup`) | library, `folderPath`, `plcFolderPath` | PLC container `CreateFrom(mc, ThrowIfExists)` |
| `delete_master_copy` | `scope`, `name` | library, `folderPath` | `Delete` |
| `create_type` | `plcName`, `plcKind` (`block`/`plcType`), `plcObjectName` | `plcFolderPath`, project-library `folderPath` | Convert a plain PLC object with `Types.CreateFromDocuments` in one project transaction |
| `create_type_version` | `plcName`, `plcKind` (`block`/`plcType`), `plcObjectName`, `content`, `expectedContentHash` | `plcFolderPath`, `replaceInWork` (default false) | Linked project type's `Versions.CreateFromDocuments` |
| `release_type_version` | `scope:"project"`, type | `versionNumber`, `author`, `comment` | Release the in-work version without automatically creating or releasing dependencies |
| `set_default_type_version` | `scope`, library, type, exact `versionNumber` or `versionGuid` | none | `SetAsDefault` |
| `synchronize_type` | `sourceScope`, `scope`, source type | source and target libraries | `UpdateLibrary(DoNotDelete, CancelIfStructureConflicts, SetOnlyHigherUpdatedVersionAsDefault)` |
| `instantiate_type_version` | `sourceScope`, source type, `plcName` | source library, version, `plcFolderPath` | PLC blocks/types `CreateFrom(version, ThrowIfPathsConflict)` |
| `delete_type` | `scope`, library, type | none | `LibraryType.Delete` |
| `delete_type_version` | `scope`, library, type | version | Released version `Delete`; in-work version `Discard` |

`sourceScope`, `sourceLibraryPath`, `sourceLibraryName` and `sourceFolderPath` select the source of `copy_master_copy`. Type synchronization and instantiation use `sourceScope` and source library selectors, with `folderPath` selecting the source type. PLC members are prefixed: `plcName`, `plcKind`, segmented `plcFolderPath`, `plcObjectName`. An empty `plcFolderPath` is the PLC root; PLC groups match case-insensitively and library folders ordinally. `directory` and `filePath` must be absolute. Supported type kinds are FB, FC and PLC UDT; other kinds fail `target_kind_unsupported`.

`set_default_type_version` requires the exact released version to make default; omission and `inWork` are invalid. `release_type_version` always selects the in-work version and uses Portal's prefilled number when `versionNumber` is omitted. Numbers have three numeric parts, each at most 2147483647, and cannot be `0.0.0`; this is the `System.Version` representation limit, not a verified safe Portal release range.

### Creation author and metadata

`create_global` accepts an optional nonblank `author`, at most **255 UTF-16 code units**
(`string.Length`, so a non-BMP character counts as two). Text is preserved exactly, including leading
and trailing spaces and LF/CRLF; it is never trimmed, normalized or truncated. `release_type_version`
uses the same length cap while retaining its existing blank/omitted-author handling. Host validation
rejects overlength before dispatch and worker validation repeats it before native calls. The
`validation_error` names `author`, maximum 255 and actual length without echoing the text. Raising
response budgets does not relax this cap; observed author values returned by reads are not capped.

Omitting the author keeps Portal's typed creation overload. The live fixture on V21 Update 3 returned
an empty author for this path; this is an installation-specific observation, not a username default
guarantee. Supplying the author uses the native engineering-composition creation parameters. There
is no later author setter or implicit comment/language change. In read-write the confirmation text
includes the supplied author; previews do not create a library or return metadata.

Creation returns the metadata observed immediately after creation, before any later item can change
the library. A nonrecoverable metadata-read failure after creation carries the captured identity in
`libraryWriteOutcome.library` when available: the library may already exist on disk. Inspect it with
`library_read` and inspect the destination before retrying; an omitted response value also does not
mean creation failed. Shared `trusted` verification is not an extra native read-back or permission
to replay. Native failure/partial-outcome and large-value omission paths remain covered offline.

### Apply, preview and confirmation

The tool plans only for `dryRun:true` or a read-write call containing global-library lifecycle. An ordinary content apply and every full-mode apply resolve each item when it runs and return `effects:[]`; the item `result` records the outcome. A later item can use a type, folder, master copy or library created earlier in an unplanned call. Planned calls show supported earlier-created targets with `createdByItem`; a library created, opened or retrieved by a lifecycle item must be used in a later prompting call.

In read-write, a call containing any of the six global-library lifecycle operations asks once through form elicitation. Decline, cancel, timeout or transport failure is `access_denied` before mutation. Content-only calls, including all eight type operations, never prompt; full never prompts (audit `policy`). A preview never mutates or prompts. There are no acknowledge guards, tokens or public confirmation arguments.

A planned block guard stops the whole call before mutation. The same checks run beside each native mutation; in an unplanned apply, a refusal fails that item after earlier items have run. This changes existing master-copy behavior: a case-insensitive name collision is now an item `guard_blocked` failure, and a missing target is an item `target_not_found`, with earlier successes retained. `library_deletes_folder_contents` is preview/planned-call information. `library_discards_changes` remains on every planned `closeAction:"discard"` call.

### Capabilities and checks

Capability is decided per item. Any project-library selector on either side, and every `create_master_copy`, `instantiate_master_copy`, `create_type`, `create_type_version`, `release_type_version` and `instantiate_type_version`, uses `library_project_write` (`standaloneOnly`). Other items use `library_write` (`projectContent`, allowed on bound local sessions, `notYetDelivered` on server projects). One unsupported item refuses the whole call before worker activity. Global-library lifecycle is Portal-scope and is not subject to the project lifecycle's standalone-only rule.

| Tool-owned check | Failure |
| --- | --- |
| Master-copy create/copy name already exists in the destination folder (OrdinalIgnoreCase) | `guard_blocked`, `library_master_copy_exists` |
| `create_type` name already exists anywhere in the project library, including another folder | `guard_blocked`, `library_type_name_exists` |
| `create_type` source is already a type instance, even if its name differs from the type | `guard_blocked`, `library_source_is_type_instance` |
| Explicit or prefilled release number is not higher than every released version | `guard_blocked`, `library_version_not_higher` |
| Required check evidence or fresh PLC export cannot be read | `guard_blocked`, `library_state_unverifiable` |
| `create_type_version` fresh export differs from `expectedContentHash` | `state_changed` |
| Converted instance does not link to the created type | `postcondition_failed`, conversion transaction rolls back |

`library_discards_changes` and `library_deletes_folder_contents` are info guards; folder previews list contents recursively, capped at 200 with `contentsTruncated`. Checks do not pre-empt native refusal of existing in-work versions, read-only state, name validity, dependencies, placement collisions or apply-time deletes. Delete instance/master-copy scans run only in previews/planned calls. A precondition refusal without an outcome is treated as not mutated; inspect outcome-bearing or native failures before retrying.

### Type workflow and results

`create_type` exports a plain PLC object to operation-owned storage, then uses `ExclusiveAccess` and one project transaction to delete it, create the project-library type and verify its linked replacement before committing. A failed conversion rolls back that transaction; there is no batch rollback. `create_type_version` resolves the type through the selected PLC instance, compares a fresh export with its required `xml:sha256` hash, stages SimaticML XML and calls `Versions.CreateFromDocuments` without a test-environment argument. `replaceInWork:true` permits replacement; without it, Portal decides whether an existing in-work version prevents creation. The result reports `replacedInWork` and instance paths read back from the created version. Live checks observed `replaceInWork:true` create a version when none was in work (`replacedInWork:false`) and replace an existing in-work version (`true`), with compiled content read-back. With instances in more than one PLC, Portal chooses the test instance independently of the requested PLC. The live case supplied the second PLC instance's content/hash but the result identified the first PLC as test instance; released content appeared there and remained absent on the second PLC. Inspect `instancePaths` and read back the reported instance instead of assuming the requested one was changed.

Changing a type: `plc_read` the instance (after `compile_check` if needed), `create_type_version` with the edited content, then `release_type_version`. `plc_write` is not a supported type-instance editing route: writes to a test instance can block behind a modal Portal dialog, and writes to a released instance can silently unlink it from its type. A compile-first workflow is recommended after conversion, instantiation, version creation or release: run `compile_check`, then `plc_read`. Portal can require compilation for inconsistent instances, as earlier native probes observed, but current live cases exported both an uncompiled ordinary instance and an uncompiled FB with changed interface content successfully. A hash from before conversion or content change still fails `state_changed`; uncompiled status alone does not imply an unreadable instance. Release never creates or releases dependencies automatically; make and release required dependency types explicitly. The live sequence released `0.0.1`, created content version `0.0.2`, then successfully released its own prefilled number explicitly; the default moved from `0.0.1` to `0.0.2`.

Releasing a version makes it the type's default version (Portal does this). The tool reports the default before and after and never restores the previous default. To keep it, add `set_default_type_version` to the same call. Synchronize and instantiate include dependencies. Instantiating onto an existing instance of the same type can succeed through Portal's reuse/update behavior. A colliding plain object can cause a native suffixed name (observed `_1`) instead of refusal. Its `ThrowIfPathsConflict` mode does not make every existing object a collision refusal; inspect returned `instancePaths` to locate the actual instance.

Synchronizing a type moves the target library's default only when the source default has a higher number than the target default. The comparison is with the current target default, not its highest owned release: a target default of `0.0.4` stayed unchanged with source default `0.0.2`; after explicitly selecting target default `0.0.1`, the same sync moved it to `0.0.2` even though the target still held `0.0.4`.
Portal refuses to delete a type or version while linked PLC instances exist. A global type delete and a nondefault global-version delete were also refused when that version was held only by a global master copy. An unused nondefault project version can be deleted; deleting a global type's only version deletes the type (`typeDeleted:true`), and an unused global type can be deleted directly.

Delete previews report PLC paths in `blockingInstances` and linked master-copy names in `masterCopiesContainingInstances`. Global-library type/version previews have no project PLC instance scan (`blockingInstances:[]`); their master-copy associations still identify a global master copy that blocks native deletion. The corrected live candidate returned that evidence for both type and version previews, and an unused global only-version preview was clear before successful deletion. Preview reads are sequential and non-atomic; apply still lets Portal decide the delete.

Discarding an in-work version after a previous release keeps its test instance, relinks it to the last released version and restores that released content (compiled content read-back verified this). Discarding a never-released in-work version deletes its test instance; a later compile/read cannot target it. Do not assume every discard preserves a PLC object.

Type results include type/version GUIDs, native version number/state, instance paths, default changes and delete/replacement facts where applicable. The shared verification entries are `trusted` or `superseded`, derived from resolved identities in success results or failure outcomes; this is distinct from the native evidence collected by a type writer. An attempted failure without an outcome conservatively supersedes earlier items on the selected library, except for a precondition refusal. Inspect `library_read`/`plc_read` before retrying.

### Other behavior and limits

- `close_global` requires `closeAction`; `save` calls `Save()` only when modified, so a clean read-only library closes with `saved:false`. A `Close` failure after a save retains `libraryWriteOutcome.saved:true`.
- `save_global`, `close_global` and `archive_global` require a user global library (`target_kind_unsupported` otherwise).
- `archive_global` appends `.zal21` when missing (case-insensitively) and reports `archivePath`.
- A created/copied master copy renamed by Portal fails `postcondition_failed` with `actualName`; master-copy placement uses `ThrowIfExists`. Creating a global master copy of a linked PLC instance can import that instance's type version into the global library even when the global type already has other versions/defaults.
- Name-only global selectors over duplicate open names fail `target_ambiguous`; use a path. `open_global` always calls `Open`; `alreadyOpen` is plan-time information when planned. Create/open/retrieve report Portal's returned `Path.FullName`.
- Library writes do not drop library-read snapshots. No `UpdateProject`, automatic dependency release, type-instance editing through `plc_write`, batch rollback, or default restoration is provided.
- Native mutation `EngineeringTargetInvocationException` is `engineering_rejected` with `engineeringError`; `portal_lost` takes precedence and invalidates binding. Typed failure outcomes are validated before use; malformed evidence is `protocol_error`.

The [first write live report](../superpowers/acceptance/reports/2026-11-07-library-write-live-verification.md) covers the original 12 operations, lifecycle accept/decline, full mode and local-session capability split. Its stated limits still apply unless superseded by new evidence. See [ADR-0017](../adr/0017-library-writes-apply-without-planning.md) for conditional planning, conversion and default decisions.

The [type live report](../superpowers/acceptance/reports/2026-11-08-library-types-live-verification.md) records FB/FC/UDT conversion and release, content-version creation/replacement with compiled read-back, stale-hash and tool-owned checks, name ambiguity with folder/GUID disambiguation, earlier-item application before a later refusal, default changes and synchronization, native instance placement/reuse/rename, global master-copy deletion blockers and both discard outcomes. Native refusals include self-synchronization, an in-work version as default or instance source, restricted release content and protected/noncompilable conversion sources. The corrected global type/version delete previews also reported master-copy blockers without an unverifiable-state guard. The uncompiled-export `library_state_unverifiable` path remains covered offline and was not reproduced by the current fixture; successful pre-compile exports instead rejected stale hashes with `state_changed`. These cases do not establish hardware/runtime behavior, Portal-loss recovery, large inventories or storage restoration.

## Known limits

- Global reads need a verified binding. Reading global libraries with no project open is a follow-up.

See [IMPROVEMENT_LOG](../IMPROVEMENT_LOG.md) for open follow-ups and ADR-0015 ([Portal-scope reads under the bound-project gate](../adr/0015-portal-scope-reads-under-binding-gate.md)) for the binding decision.
