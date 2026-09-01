# PR 1 Explicit MCP Tool Annotations — Live TIA Portal V21 Acceptance

## Evidence status

Live acceptance completed by this separately authorized, non-mutating run. This evidence proves only the exact live TiaMcpServer host, attached TIA Portal session, and disposable project path recorded below. It does not replace offline, stub, or FakeWorker evidence, and those evidence classes do not replace this live run.

- Harness exit code: `0`
- Host modes: current-branch `--read-only` and `--read-write` sessions

## Tested environment

- TIA Portal product version: 2100.0.121.1
- Project copy path: `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`
- Harness report path: `C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp\docs\superpowers\acceptance\reports\2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`
- Read-only server: TiaMcpServer 1.0.0.0
- Read-write server: TiaMcpServer 1.0.0.0
- Attached Portal PID: 3152 in both sanitized status summaries

## Read-only MCP surface

Expected and observed exactly 4 tools:

```json
[
  "browse_project_tree",
  "execute_read_batch",
  "get_project_status",
  "network_read"
]
```

Benign call: `tools/call` for `get_project_status` with the project copy path. Result summary:

```json
{
  "success": true,
  "isOpen": true,
  "path": "C:\\Users\\LCZ\\Desktop\\RnD\\plc-prompt-injections\\SimpleProject\\SimpleProject.ap21",
  "sessionIdentity": {
    "projectPath": "C:\\Users\\LCZ\\Desktop\\RnD\\plc-prompt-injections\\SimpleProject\\SimpleProject.ap21",
    "portalProcessId": 3152
  }
}
```

## Read-write MCP surface

Expected and observed exactly 14 tools:

```json
[
  "apply_write_batch",
  "archive_project",
  "browse_project_tree",
  "close_project",
  "compile_check",
  "create_project",
  "execute_read_batch",
  "get_project_status",
  "network_read",
  "network_write",
  "open_project",
  "preview_write_batch",
  "save_project",
  "save_project_as"
]
```

Emitted annotations for the approved write-tool matrix:

```json
[
  {
    "name": "preview_write_batch",
    "readOnlyHint": true,
    "destructiveHint": false,
    "openWorldHint": false
  },
  {
    "name": "apply_write_batch",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  },
  {
    "name": "open_project",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  },
  {
    "name": "create_project",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  },
  {
    "name": "save_project",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  },
  {
    "name": "save_project_as",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  },
  {
    "name": "archive_project",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  },
  {
    "name": "close_project",
    "readOnlyHint": false,
    "destructiveHint": true,
    "openWorldHint": false
  }
]
```

Benign call: `tools/call` for `get_project_status` with the project copy path. Result summary:

```json
{
  "success": true,
  "isOpen": true,
  "path": "C:\\Users\\LCZ\\Desktop\\RnD\\plc-prompt-injections\\SimpleProject\\SimpleProject.ap21",
  "sessionIdentity": {
    "projectPath": "C:\\Users\\LCZ\\Desktop\\RnD\\plc-prompt-injections\\SimpleProject\\SimpleProject.ap21",
    "portalProcessId": 3152
  }
}
```

## Non-mutation and evidence boundary

The harness sent only `initialize`, `notifications/initialized`, `tools/list`, and one `get_project_status` `tools/call` per access mode. It did not call any lifecycle, preview, apply, compilation, network-write, or PLC-control operation; no project mutation was performed. PLC `start_plc` and `stop_plc` remain deferred.

A post-run read-only status check reported `isModified:false` for the project copy.

The explicit MCP annotations are client-facing, untrusted metadata. Server-enforced access policy, preview/token/apply validation, binding checks, and auditing remain the write-safety authority.
