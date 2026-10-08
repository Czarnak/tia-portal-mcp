# MCP client configuration

Reference for configuring the server in an MCP client, including how block paths are
addressed. For first-time installation, see [Installation](installation.md).

## Block Paths

Prefer block paths returned in `browse_project_tree` node `Details.Path` values. Supported block path forms are:

```text
BlockName
PLC_1/BlockName
PLC_1/Blocks/Folder/SubFolder/BlockName
PLC_1/Units/UnitName/Blocks/Folder/SubFolder/BlockName
```

Legacy `BlockName` and `PLC_1/BlockName` paths are accepted only when the block name is unambiguous. If more than one block has the same name, use the deterministic `Path` returned by `browse_project_tree`.

## MCP Client Configuration

Configure your MCP client to launch the tool command:

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp"
    }
  }
}
```

Without explicit access arguments the server starts in `read-write` with sixteen tools: edits,
compilation, and all six lifecycle tools. Each actual lifecycle call asks once through form
elicitation `accept` with boolean `confirm:true`, even with no guards or only info guards. A client
without that capability receives `access_denied`; decline, cancel, timeout, or transport failure
also denies. Set `"args": ["--access-mode", "full"]` for the same sixteen tools with lifecycle
policy confirmation and no server elicitation; full adds no tools or operations.
`tia-mcp install` defaults separately to read-only with seven tools, including `bind_project`.
Block guards stop the call in every mode. See [Installation](installation.md#access-modes).

Remove the old startup confirmation switch; it is rejected with:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Lifecycle inputs use `dryRun`; remove former agent confirmation arrays and old public `confirm`
and the former safety-token argument. Read the structured `result` and `verification` outcomes and audit v2
confirmation by `user`, `policy`, or `none`. A dry run never elicits, although a client
may still show its destructive-tool permission prompt. No tool uses safety tokens; replace
`preview_write_batch`/`apply_write_batch` callers with `plc_write`, passing `content` plus
`expectedContentHash` (from `plc_read`) instead of `yamlContent`/`sourceContent`.
`plc_write` and Network take `operations` and optional boolean `dryRun=false`; **omitting dryRun executes**.
Set `dryRun:true` for every preview caller. Remove `confirm`, the former safety-token argument, `acknowledge` and
unknown root keys: SDK/wrapper rejection before entry is a normal MCP error with no write audit.
Entered validation/binding/guard denials use a canonical root error and one audit v2 record.
No `plc_write` or Network server elicitation occurs in either writable mode, including deletes.
Read `batch`, `effects`, `verification` and explicit-null root `omission`; delivery failure can
coexist with successful execution summaries. Inspect original exact selectors before retry. Keep destructive tools out of client auto-approve lists if you require a
permission prompt on every call; the server cannot establish whether a human saw an accepted
elicitation dialog. The [lifecycle reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
documents requests, guards, typed failures, and recovery.

Use default/null `action` or `action:"bind"` on `bind_project` to adopt or switch to an already-open project in any mode; switching to a
different configured or previously bound path requires `forceRebind:true`. Explicit
`action:"list_portals"` only discovers; Project Server inspection actions reuse a persistent
attachment and cannot switch it or adopt a project. See [Multiuser inventory](../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md)
for exact selectors, PID continuity, current-user session scope and recorded acceptance limits.
For local sessions, use the exact observed `.amc21` engineering path for bind/status and
`--project`/`TIA_MCP_PROJECT_PATH` already-open assertions, with an exact binding PID where
needed. `.als21` and inventory directories are not adoption selectors. Writable `open_project`
accepts an independently known exact existing `.als21` file; set `dryRun:true` to preview.
Cold adoption reports null opener provenance; successful open records it only after verification.
Local content/compile/save/terminal calls remain `unsupported_capability`. After an uncertain
opener inspect AMC status and Portal inventory; never automatically replay. Offline read-write
opening required operator-dismissed Siemens dialogs in the scoped
[PR4 run](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md).
Omit the path with the default binding action to select
the sole open project or list candidates. Reattachment may require a human response to TIA's
Openness access dialog. Worker ownership is lost on detach. Only `open_project` and `create_project`
open projects; ordinary reads never open, bind or switch. Read-only never opens, creates, saves or
closes. A project-tree cursor is invalid after a binding change; restart browsing without it.

For local development without installing the tool, point the client at `dotnet`:

```json
{
  "mcpServers": {
    "tia-portal-dev": {
      "command": "dotnet",
      "args": ["run", "--project", "{REPO PATH}\\TiaMcpServer"]
    }
  }
}
```

With an explicit project binding:

```json
{
  "mcpServers": {
    "tia-portal-dev": {
      "command": "dotnet",
      "args": ["run", "--project", "{REPO PATH}\\TiaMcpServer", "--", "--project", "C:\\Projects\\Sandbox\\Line.ap21"]
    }
  }
}
```
