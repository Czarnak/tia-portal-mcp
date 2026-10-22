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

Without explicit access arguments the server starts in `read-write` with nineteen tools: edits,
compilation, and all six lifecycle tools. Each actual lifecycle call asks once through form
elicitation `accept` with boolean `confirm:true`, even with no guards or only info guards. A client
without that capability receives `access_denied`; decline, cancel, timeout, or transport failure
also denies. Set `"args": ["--access-mode", "full"]` for the same nineteen tools with lifecycle
policy confirmation and no server elicitation; full adds no tools or operations.
`tia-mcp install` defaults separately to read-only with eight tools, including `bind_project`.
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
unknown root keys: every argument-shape error returns the tool's canonical document (`validation_error`, `isError:true`) with no audit record.
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
Content reads (`browse_project_tree`, `plc_read`, `network_read`, `read_cross_references`,
`hmi_read`), `plc_write`, `network_write`, `hmi_write`, `compile_check` and `save_project` work on a bound
local session. Local `save_project` saves the session only and never checks in to the Project
Server; create, save-as, archive and close remain `unsupported_capability`. After an uncertain
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

## Response size limits

Structured tool results are budgeted at **60,000 characters per value** (one `plc_read` block,
one `network_read` result, one write effect) and **180,000 characters per complete response**.
A value over its budget is omitted whole with `reason`, `limitChars`, `originalChars` and retry
guidance; it is never cut. Sizes are measured on the escaped JSON the client receives, so an SCL
or XML document counts somewhat larger than its file.

A block or type export over the value budget therefore comes back without `content` or
`contentHash`, and `update_block_logic`/`update_type_content` cannot be used on it. Raise both
budgets at server startup:

| Setting | Command line | Environment variable | Default |
| --- | --- | --- | --- |
| Value budget | `--max-item-chars <n>` | `TIA_MCP_MAX_ITEM_CHARS` | 60,000 |
| Response budget | `--max-document-chars <n>` | `TIA_MCP_MAX_DOCUMENT_CHARS` | 180,000 |

```json
{
  "mcpServers": {
    "tia-portal": {
      "command": "tia-mcp",
      "args": ["--access-mode", "read-write", "--max-item-chars", "100000", "--max-document-chars", "250000"]
    }
  }
}
```

The same values can be set under the client's `env` block instead; a command-line value wins
over its environment variable. Rules:

- Limits can only be raised: each value must lie between its default and 1,000,000. Both
  `--name n` and `--name=n` forms are accepted, each at most once.
- Set the value budget at or above the omission's `originalChars`.
- Keep the response budget above the value budget with headroom for the response envelope
  (operation IDs, counts, warnings). With equal limits, a value just under the limit can still be
  omitted as `responseExceededDocumentCharLimit`. Read one large block per `plc_read` call.
- An invalid value stops the server at startup with an error; the effective budgets are logged
  to standard error as `TIA MCP response budget: ...`.
- `tia-mcp install` and `tia-mcp doctor` do not write or report these settings; edit the client
  configuration by hand.

Raising the server budgets does not raise limits the MCP client enforces on its own, such as a
maximum tool-result size, and a content write still sends the whole document as one tool
argument. Fixed limits are unaffected: project-tree pages, `network_read` hardware pages and the
`compile_check` report stay at 60,000 characters.

## Worker timeout

The host waits up to five minutes for each worker request by default. Raise this at server
startup for operations that need more time:

| Setting | Command line | Environment variable | Default |
| --- | --- | --- | --- |
| Worker timeout (minutes) | `--worker-timeout-minutes <n>` | `TIA_MCP_WORKER_TIMEOUT_MINUTES` | 5 |

Use whole minutes from **5 to 60 inclusive**; the setting is raise-only. Both
`--worker-timeout-minutes 15` and `--worker-timeout-minutes=15` are accepted. The value is
parsed once at startup: command line wins over environment, then the default applies.
Invalid values reject startup with an error instead of silently falling back.

One value applies to every worker request, including the startup `hello`. Startup stderr
reports `TIA MCP worker timeout: <n> minutes`; `doctor` reports
`Worker timeout: <n> minutes (<source>)`, where the source is `default`, `option`, or
`environment`. Raising the value also delays detection of a worker hung during `hello` or
startup. There is no per-method override or progress-notification feature.

The server setting cannot override the MCP client's own limits. The
[MCP TypeScript SDK](https://ts.sdk.modelcontextprotocol.io/v2/api/index/@modelcontextprotocol/client/)
defaults to a 60-second request timeout unless the caller raises it; this is not a universal
MCP-client default. Claude Code exposes
[`MCP_TOOL_TIMEOUT`](https://code.claude.com/docs/en/env-vars) in milliseconds. Give the client
headroom above the server timeout and check any separate transport or idle limits.

A call already dispatched to the worker can keep running after the client abandons it.
The lifecycle/write audit retains its outcome; inspect the audit and current project state
before retrying a mutation.
