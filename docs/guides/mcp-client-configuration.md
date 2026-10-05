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
policy confirmation and no server elicitation, plus OnlineControl (PLC run/stop).
`tia-mcp install` defaults separately to read-only with six tools, including `bind_project`.
Block guards stop the call in every mode. See [Installation](installation.md#access-modes).

Remove the old startup confirmation switch; it is rejected with:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Lifecycle inputs use `dryRun`; remove former agent confirmation arrays and old public `confirm`
and `safetyToken` arguments. Read the structured `result` and `verification` outcomes and audit v2
confirmation by `user`, `policy`, or `none`. A dry run never elicits, although a client
may still show its destructive-tool permission prompt. Only generic batch writes retain tokens.
Network takes `operations` and optional boolean `dryRun=false`; **omitting dryRun executes**.
Set `dryRun:true` for every preview caller. Remove `confirm`, `safetyToken`, `acknowledge` and
unknown root keys: SDK/wrapper rejection before entry is a normal MCP error with no write audit.
Entered validation/binding/guard denials use a canonical root error and one audit v2 record.
No Network server elicitation occurs in either writable mode, including connected deletion.
Read `batch`, `effects`, `verification` and explicit-null root `omission`; delivery failure can
coexist with successful execution summaries. Inspect original exact selectors before retry. Keep destructive tools out of client auto-approve lists if you require a
permission prompt on every call; the server cannot establish whether a human saw an accepted
elicitation dialog. The [lifecycle reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
documents requests, guards, typed failures, and recovery.

Use `bind_project` to adopt or switch to an already-open project in any mode; switching to a
different configured or previously bound path requires `forceRebind:true`. Omit the path to select
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
