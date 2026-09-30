# TIA Portal Project and Portal Operations

## Read operations

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `get_project_status` | `get_project_status` | Optional `projectPath`; reads status and metadata. Read-only asserts the open project; writable modes can establish the initial exact-path session. Reads never switch an attached project. |
| `browse_project_tree` | `browse_project_tree` | v3: optional `projectPath`, typed `startSelector`, `depth`, and `pageSize`; use `cursor` to continue an immutable snapshot. |
| `compile_check` | `compile_check` | Optional `projectPath`, `plcName`, and `blockPath`; compiles the selected scope and returns compiler messages. Available in read-write and full modes. |

### `browse_project_tree` v3

`browse_project_tree` returns one canonical JSON document identically in the MCP `content` text block and `structuredContent`. The response is a versioned envelope, and `result.nodes` is a flat parent-first sequence. Grouped and ungrouped devices have the same `Device` shape. PLC system-block groups use `nodeType: "SystemBlockFolder"`; contained blocks keep their functional block type and carry `details.IsSystemBlock: "true"`. That marker records TIA hierarchy membership only and is not an author, vendor, library, or provenance claim.

Every PLC block node may include these optional, default-on string fields in its `details` object; populated values are emitted without an opt-in request.

| Block detail | Meaning |
| --- | --- |
| `HeaderAuthor` | Non-blank `PlcBlock.HeaderAuthor` exactly as reported by TIA Portal. |
| `HeaderVersion` | Non-blank `PlcBlock.HeaderVersion.ToString()` value. The value is included when the typed Openness property is non-blank, regardless of whether the TIA UI appears blank or the exported SimaticML contains a `<HeaderVersion>` element. |
| `HeaderFamily` | Non-blank `PlcBlock.HeaderFamily` exactly as reported by TIA Portal. |
| `HeaderName` | Non-blank `PlcBlock.HeaderName`; separate from the node's engineering-object `name`. |

Blank header values are omitted rather than emitted as `null` or empty strings. This is based on the value returned by the typed Openness property: only `null`, empty, or whitespace values are omitted. In the bounded V21 acceptance-project observation, an omitted SimaticML version was exposed by the typed property as `0.1`, while clearing another version produced/exposed `0.0.0.0`. Those concrete values are observations from that project, not universal Siemens guarantees. Therefore, `details.HeaderVersion` means that Openness reported a typed value; it does not prove that the version was explicitly populated in the TIA UI or in XML. Consumers that require XML element-presence semantics should inspect an export instead. Header metadata is descriptive project data, not verified vendor provenance. `HeaderName` never changes selector identity, and `IsSystemBlock` continues to describe hierarchy membership only. The node's engineering-object `name` and `HeaderName` are independent; neither value is a fallback for the other.

The initial-request fields are:

| Field | Behavior |
| --- | --- |
| `projectPath` | Optional project assertion. In read-only mode it must identify the project already open in TIA Portal; it never opens or switches a project. |
| `startSelector` | Optional root-to-target array of `{ nodeType, name }` segments. `nodeType` is case-sensitive and drawn from the closed v3 vocabulary; `name` is an exact ordinal-ignore-case match among direct children. Zero or multiple matches fail closed. |
| `depth` | Optional maximum returned depth from the selected root, inclusive; minimum `1`. |
| `pageSize` | Optional requested node count from `1` through `200`; default `100`. Canonical size projection may return fewer complete nodes. |
| `cursor` | Opaque process-local continuation. A cursor-only call is the normal continuation request. |

#### v2-to-v3 request migration

The v2 request was:

```json
{ "projectPath": null, "depth": 2, "startPath": "PLC_1" }
```

The v3 request is:

```json
{
  "projectPath": null,
  "depth": 2,
  "startSelector": [
    { "nodeType": "Device", "name": "PLC_1" }
  ]
}
```

Use `v3.0.0` or newer. The old bare nested array is not retained, and the legacy `startPath` and `deviceName` project-tree inputs are removed instead of accepted as aliases.

#### Complete success envelope

```json
{
  "contractVersion": "3.0",
  "status": "succeeded",
  "result": {
    "snapshot": {
      "snapshotId": "0123456789abcdef0123456789abcdef",
      "createdAt": "2026-09-06T12:00:00+00:00",
      "idleExpiresAt": "2026-09-06T12:10:00+00:00",
      "totalNodes": 2
    },
    "query": {
      "projectPath": "C:\\Projects\\Plant.ap21",
      "startSelector": [
        { "nodeType": "Device", "name": "PLC_1" }
      ],
      "depth": 2
    },
    "pagination": {
      "offset": 0,
      "requestedPageSize": 100,
      "returnedCount": 2,
      "nextCursor": null
    },
    "nodes": [
      {
        "nodeId": "n0",
        "parentNodeId": null,
        "sequence": 0,
        "name": "PLC_1",
        "nodeType": "Device",
        "details": {}
      },
      {
        "nodeId": "n1",
        "parentNodeId": "n0",
        "sequence": 1,
        "name": "PLC_1",
        "nodeType": "PlcSoftware",
        "details": {}
      }
    ]
  },
  "failure": null,
  "warnings": []
}
```

When `pagination.nextCursor` is non-null, continue with a cursor-only request and repeat until it becomes null:

```json
{ "cursor": "<pagination.nextCursor>" }
```

Each initial request makes one tree-observation worker call; configured read-write startup may first call `get_project_status` to verify the project binding. The net48 worker applies the residual subtree selector and depth filter after walking the selected device. Each continuation authenticates the cursor and serves the same immutable point-in-time snapshot without another worker call. The snapshot does not incorporate later TIA edits. Cursors are process-local: host restart, expiry, or bounded-cache eviction returns `snapshot_unavailable`. Start a new cursor-free browse after that outcome. Supplying query fields with a cursor is allowed only when they match the captured query; a mismatch returns `cursor_filter_mismatch`.

Nodes are parent-first, so a caller can reconstruct a selector without legacy path text: index nodes by `nodeId`, start at the desired node, follow `parentNodeId` to null, reverse the chain, and project every node to `{ "nodeType": node.nodeType, "name": node.name }`. That returned chain begins at the selected snapshot root, which may be below `Device`. To build the complete selector, prepend `result.query.startSelector` with its final segment removed, then append the reconstructed returned chain. When the initial `startSelector` was omitted, the prefix is empty. This avoids duplicating the selected root while preserving Device ancestry for snapshots rooted at `PlcSoftware`, `BlockFolder`, or deeper. Submit the combined array as `startSelector`. Do not persist `nodeId` across snapshots; it is snapshot-local.

Every snapshot is limited to 4,000,000 canonical characters. The process retains at most four snapshots and 16,000,000 aggregate snapshot characters with a ten-minute sliding idle lifetime. Every page is limited to 60,000 canonical characters and contains complete nodes only. `snapshot_too_large`, `result_item_too_large`, and `result_metadata_too_large` fail explicitly; v3 never appends a truncation marker. The legacy `details.Path` member is removed.

All project-tree failures retain the same envelope with `status: "failed"`, `result: null`, a `{ category, message }` failure, and separate warnings. Input and selection failures are `validation_error`, `invalid_selector`, `target_not_found`, and `target_ambiguous`. Structurally invalid selector members use `invalid_selector`; malformed or empty cursors use `invalid_cursor`. Cursor and snapshot failures are `invalid_cursor`, `cursor_filter_mismatch`, `cursor_out_of_range`, and `snapshot_unavailable`. Size failures are `snapshot_too_large`, `result_item_too_large`, and `result_metadata_too_large`. Oversized diagnostics produce a bounded `result_metadata_too_large` envelope without echoing the diagnostics. Binding/worker/protocol failures may be `binding_conflict`, `worker_operation_failed`, `worker_timeout`, `worker_crashed`, `access_denied`, or `protocol_error`.

Per-item Openness degradation is retained in the envelope `warnings` array rather than changing a failure into success.

`compile_check` is a standalone engineering operation. It is not marked read-only, does not use a safety token, and is exposed in read-write and full modes.

### Standalone status and compilation contract

`get_project_status` and `compile_check` declare concrete MCP output schemas and return the same canonical JSON document in `content` text and `structuredContent`. Both use contract version `1.0` and the envelope `tool`, `contractVersion`, `success`, `error`, `warnings`, and `result`. Inputs and access modes are unchanged. This output change breaks legacy clients: replace parsing the JSON string in `payload` with reading the object at `result.value`.

`result` contains four explicit members: `status` (`succeeded`, `failed`, or `omitted`), `value`, `failure`, and `omission`. The value is the complete `ProjectStatusInfo` (including `metadata`) or `CompileCheckReport`. Optional/unavailable data is represented by explicit nulls, including within nested metadata. A no-project read succeeds with `result.value.isOpen:false` and does not bind an unbound host session.

Rejection before the requested operation sets `success:false`, a top-level `{category,message}` `error`, `result:null`, and MCP `isError:true`. An attempted operation that fails sets `success:false`, `error:null`, and MCP `isError:false`, with failure evidence in `result.failure` or compiler diagnostics in `result.value`. Host identity validation can fail after compilation; that remains an attempted failure and the session binding is invalidated. A timeout/crash leaves the outcome unknown; inspect current project state before deciding whether to retry.

For compilation, `success` means the selected compilation passed, including warning-only reports. Errors, unavailable compiler results, and unknown/incomplete states do not pass. Compiler errors retain the report, for example:

```json
{
  "contractVersion": "1.0",
  "error": null,
  "result": {
    "failure": null,
    "omission": null,
    "status": "failed",
    "value": {
      "blockPath": "PLC_1/Blocks/Main",
      "overallState": "Error",
      "plcs": [{
        "deviceName": null,
        "diagnosticNotes": [],
        "errorCount": 1,
        "messages": [{"description": "Invalid expression", "path": "Main", "severity": "Error"}],
        "plcName": "PLC_1",
        "state": "Error",
        "warningCount": 0
      }],
      "scope": "block",
      "totalErrorCount": 1,
      "totalWarningCount": 0
    }
  },
  "success": false,
  "tool": "compile_check",
  "warnings": []
}
```

The complete canonical value has a 60,000-character limit; the final document, including escaped strings, warnings and omission metadata, has a 180,000-character limit. Oversized values are replaced whole: `status:omitted`, `value:null`, `success:false`, `error:null`, and `isError:false`. `omission` reports `reason` (`resultExceededItemCharLimit` or `responseExceededDocumentCharLimit`), `limitChars`, `originalChars`, `retryTool`, and `guidance`. Compile retries can narrow `plcName` or `blockPath`; status has no metadata selector, so its guidance directs users to reduce metadata or inspect it in TIA Portal. Whole warning entries may be removed to fit the document, with a bounded count notice. Failure prose is shortened only as a last resort and marked explicitly.

Strict decoding precedes budgeting: malformed or inconsistent worker success payloads become a fixed-message `protocol_error` outcome and are never echoed. Producer diagnostic limits are separate: a compiler report's `diagnosticNotes` can disclose omitted/shortened messages, and counts need not equal the number of delivered messages. A passing compilation does not prove diagnostics were exhaustive, a project was saved, or PLC code was downloaded.

### `get_project_status` metadata surface

When a project is open, `get_project_status` reports the status fields plus a nested `metadata`
object carrying the extended read-only project metadata:

| Field | Description |
| --- | --- |
| `copyright` | Project copyright text, verbatim from Openness. |
| `family` | Project family, verbatim from Openness. |
| `comment` | Multilingual project comment: `translations` list of `{ culture, text }` in the order Openness reports them, preserving every translation. `culture` is the language culture name (for example `en-US`). |
| `languageSettings` | `languages` and `activeLanguages` as culture-name lists; `editingLanguage` and `referenceLanguage` culture names (null when unset). |
| `historyEntries` | Text and date-time of each history entry, in Openness order, verbatim and not deduplicated. Capped at `200` entries (oldest first); when Openness reports more, `historyTruncated` is `true`. When history could not be read, both `historyEntries` and `historyTruncated` are explicit `null` — `historyTruncated` is `false` only when history was read completely. |
| `usedProducts` | `{ name, version }` for every product Openness records, no inference and no deduplication. |
| `compilationSettings` | V21 block-compilation toggles read through `PlcSimulationSettingsProvider` and `VirtualPlcSettingsProvider`: `isSimulationDuringBlockCompilationEnabled` and `isVirtualPlcDuringBlockCompilationEnabled`. A value is explicit `null` when its provider or value is unavailable, reported as a response warning — never a fabricated `false`. |

All metadata is readable in all three access modes; metadata inspection never closes, switches, saves, or
confirms anything. Unavailable sections degrade to a warning and `null` output rather than a
fabricated default; unrelated errors still fail the call normally.

The `get_project_status` response uses the whole-value omission budget described above.
Lifecycle post-write verification (after
`open_project`, `create_project`, `save_project`, `save_project_as`, and `archive_project`) reads
the plain project status only — it never enumerates history or the extended metadata surface.

## Lifecycle operations

All six lifecycle tools require `full`; read-write permits edits and compilation but leaves
persistence to the human. Its initial `--project` or unattached read `projectPath` can establish
the session. Once attached, a read cannot switch or close the project. Read-only paths are
assertions against the project already open and never open one.

| Tool | Behavior | Main inputs |
|---|---|---|
| `open_project` | Opens a project and binds the session to it. | Absolute `.ap21` `projectPath`; optional `forceRebind`. |
| `create_project` | Creates a project and binds the session to it. | Absolute `projectDirectory`, `projectName`; optional `author`, `comment`. |
| `save_project` | Saves the active project. | Optional `projectPath`. |
| `save_project_as` | Saves a copy and rebinds the session to the copy. | `targetDirectory`, `targetName`; optional source `projectPath`; `rebind` must remain `true`. |
| `archive_project` | Archives a project. | `archiveDirectory`, `archiveName`; optional `mode`, `saveBeforeArchive`, `projectPath`. |
| `close_project` | Closes the project and clears the session binding. | Optional `projectPath`, `saveBeforeClose`. |

Supported archive modes are `None`, `DiscardRestorableData`, `Compressed`, and `DiscardRestorableDataAndCompressed`. Lifecycle tools are single-tool operations and cannot be included in a batch.

### What a lifecycle preview shows

The preview names the effective project in `target.projectPath` and in its summary for `save_project`, `save_project_as`, `archive_project`, and `close_project`. When `projectPath` is omitted, that displayed path comes from the verified session binding; omission is still preserved as `null` in the token's requested-input hash. Passing an explicit path keeps that original input in the hash. The preview and confirmed apply reconstruct the same target, so changing the input or the binding requires a fresh preview.

For a force-rebind `open_project` preview, `target` distinguishes `sourceProjectPath` from `destinationProjectPath`; the summary names both projects and says whether the source will close or remain open, based on the exact rebind-state probe. A worker-owned source with unsaved changes that would close is refused at preview with `validation_error` and no token. A UI-owned source remains open; a same-path open is idempotent, and an unbound session has no source project to close. These are previewed consequences, not proof that a later apply has occurred.

Archive and close previews show `saveBeforeArchive` and `saveBeforeClose`, including when the caller chooses not to save first. An archive directory must already exist and must not be the project's own folder or a subdirectory; both conditions are checked before a token is issued, and existence is checked again on apply before worker mutation. The own-folder diagnostic takes precedence when both restrictions apply.

### MCP client hints

`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, and
`close_project` are advertised with conservative mutating MCP hints: `readOnlyHint: false`,
`destructiveHint: true`, and `openWorldHint: false`. These are client-facing metadata only; they
do not bypass the safety model. The first lifecycle call still returns a preview and single-use
safety token. It applies only on a later call with the unchanged input, `confirm=true`, and that
token.

## Safety and session binding

- A lifecycle call without a safety token returns a preview and a single-use token. Applying the change requires the same tool input, `confirm=true`, and that token.
- Tokens expire after ten minutes and bind the exact tool input, project path, and current project state.
- `save_project_as` requires rebinding because Siemens `SaveAs` switches the active project to the copy.
- Archive output is rejected when the archive directory is inside the project folder.
- After a timeout or worker crash, inspect the current project state before deciding whether another call is safe; the server does not automatically retry lifecycle writes.
- `get_project_status(projectPath)` is non-binding and never switches the active project. Use `open_project` for an intentional project switch.

## Current limits

The current project surface does not provide:

- `OpenWithUpgrade`, `Retrieve`, `RetrieveWithUpgrade`, or project deletion.
- Project language settings and multilingual text import/export (reading is exposed through `get_project_status` metadata; editing is not).
- Full project attribute editing (copyright, family, comment, and language settings are read-only today).
- UMAC delegates, authentication events, or explicit primary/secondary `ProjectOpenMode` selection.
- Portal settings, diagnostics settings, or search-index administration.
- VCI workspace, version-control, compare, synchronize, or mapped-object operations.
- Multiuser server projects and local sessions; see [MULTIUSER_OPERATIONS_SUMMARY.md](MULTIUSER_OPERATIONS_SUMMARY.md).
