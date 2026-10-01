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

Without explicit access arguments the server starts in `read-write`: edits and compilation,
with eight tools. Set `"args": ["--access-mode", "full"]` on an installed `tia-mcp` launch for
the fourteen-tool surface, including save/close and other lifecycle operations and PLC runtime
control. Existing read-write clients needing those operations must migrate to full.
`tia-mcp install` has a separate read-only default (four tools).

User-confirmation configuration defaults to on. Add `"--confirm-with-user=false"` to server
arguments to disable it, or `"--confirm-with-user"` to explicitly enable it. Lifecycle tools apply
this setting when acknowledge guards fire: default-on requires client form elicitation `accept`
with boolean `confirm:true`, ignoring agent acknowledgements. A client that lacks elicitation is
refused with `access_denied` for those calls, with no fallback to agent input. The off path requires
exactly the fired acknowledge IDs in `acknowledge`. No guard can bypass full-mode access or a hard
block. See [Installation](installation.md#access-modes) for the complete modes.

Lifecycle inputs use `dryRun` and `acknowledge`; remove old `confirm` and `safetyToken` arguments and
read the structured `result` and `verification` outcomes. A dry run never elicits, although a client
may still show its destructive-tool permission prompt. Token workflows remain active for Network
and generic batch writes. Keep destructive tools out of client auto-approve lists if you require a
permission prompt on every call; the server cannot establish whether a human saw an accepted
elicitation dialog. The [lifecycle reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
documents requests, guards, typed failures, and recovery.

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
