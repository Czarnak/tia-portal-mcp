# Installation

How to install the TIA Portal MCP server, verify it, and register it with an MCP client.
For a condensed quick start, see the [README](../../README.md).

## Requirements

- Windows
- Siemens TIA Portal V21 installed
- TIA Portal Openness installed and enabled
- Current Windows user is a member of the `Siemens TIA Openness` user group
- .NET SDK 8.0 or newer for `dotnet tool install`
- .NET Framework 4.8 Runtime for the Openness worker

Source builds additionally need:

- .NET SDK 8.0.4xx or newer 8.0 feature band. The repo includes `global.json` to prefer .NET SDK 8 for builds.
- .NET Framework 4.8 Developer Pack or targeting pack

By default, source builds expect Openness DLLs here:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Local developer builds prefer real TIA Portal V21 assemblies from `TiaPortalV21Dir`. You can override that path with the `TiaPortalV21Dir` MSBuild property or environment variable. It must point to the folder containing `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll`.

The repo also contains compile-time reference stubs in `ref/` so CI can build and package the MCP server without installing TIA Portal. Those stubs are fallback-only when a local TIA install is not found. To force stub references for CI/package builds:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

To force local TIA references:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

During build, the worker prints the selected reference directory:

```text
TIA Openness compile references: C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48 (UseTiaPortalReferenceStubs=false)
```

## Install

```powershell
dotnet tool install -g TiaMcpServer
```

Run the installed server:

```powershell
tia-mcp
```

To bind an MCP server process to a specific project, pass `--project` or set `TIA_MCP_PROJECT_PATH`:

```powershell
tia-mcp --project C:\Projects\Line.ap21
$env:TIA_MCP_PROJECT_PATH = 'C:\Projects\Line.ap21'
tia-mcp
```

`--project` starts as a configured-but-unverified assertion. Before any guarded write preview, the
host performs a read-only status check and accepts the binding only when the worker reports a complete,
matching identity: worker process, TIA Portal PID, project generation, and canonical project path.
`open_project` and `create_project` can establish that identity explicitly. Ordinary unbound reads do
not silently bind the session.

The worker never chooses the first running Portal or first open project. It selects an exact project
path when one was supplied, or a sole candidate when there is genuinely only one. Multiple possible
Portals/projects fail with `target_ambiguous` before Attach or mutation. A later path, PID, worker, or
project-generation mismatch fails with `binding_conflict` and invalidates the binding; call
`open_project` with `forceRebind=true`, or start a new MCP session. `get_project_status(projectPath)`
is read-only and non-binding: do not use it to switch projects. Use `open_project` for deliberate
session switching.

### Version flag

Run `tia-mcp --version` (or `tia-mcp -v`) to print the host version and exit without starting the MCP server.

### Doctor command

Run `tia-mcp doctor` to validate the runtime environment before using the MCP server. It checks the operating system, .NET runtimes, TIA Portal installation, Openness assemblies, user group membership, worker executable, host/worker version compatibility, running TIA Portal processes, and project-binding configuration.

Doctor is non-invasive: it does not start the MCP host, attach to TIA Portal, open a project, or inspect project content. For an explicit binding it verifies that the value is an absolute path to an existing `.ap21` file. Process detection uses the Windows process list, so with multiple TIA Portal processes Doctor cannot prove which process has that file open; it reports that uncertainty instead of passing silently.

```powershell
tia-mcp doctor
tia-mcp doctor --json
tia-mcp doctor --verbose
tia-mcp doctor --project C:\Projects\Line.ap21
```

Options:

- `--json` - emit a single JSON document to stdout.
- `--verbose` - include diagnostic evidence for each check.
- `--project` - validate the exact absolute path of an existing `.ap21` project without opening or attaching to TIA Portal.

Project-selection diagnostics are access-mode aware:

- no project binding is a warning in read-only mode and a failure in read-write mode;
- an invalid, relative, non-`.ap21`, or missing project path is a failure;
- multiple TIA Portal processes are a warning when an explicit binding is configured, because Doctor cannot verify the live match without attaching;
- multiple TIA Portal processes with no binding are a warning in read-only mode and a failure in read-write mode.

Even an existing local project path remains a Doctor warning because Doctor deliberately does not
Attach and cannot prove which Portal has it open. Before using project tools, open the exact project
in the intended TIA Portal process. Runtime identity checks remain the authority for accepting or
rejecting a request.

Exit codes: `0` (no blocking failures), `1` (one or more checks failed), `2` (invalid arguments).

### Register with an MCP client

The `tia-mcp install` command registers the TIA Portal MCP server with a supported MCP client by invoking the client's native CLI. It does not edit configuration files directly.

Supported clients: Claude Code, Codex, OpenCode, MiMoCode.

```powershell
tia-mcp install claude-code
tia-mcp install codex
tia-mcp install opencode
tia-mcp install mimocode
```

Aliases: `claude` (Claude Code), `mimo` (MiMoCode).

Options:

- `--name <name>` - server registration name (default: `tia-portal`).
- `--access-mode <mode>` - access mode: `read-only` or `read-write` (default: `read-only`).
- `--tia-project <path>` - bind to a specific TIA Portal project.
- `--server-path <path>` - explicit path to the `tia-mcp` executable.
- `--dry-run` - print the install command without executing.
- `--json` - emit JSON output (not supported with MiMoCode).

Examples:

```powershell
# Register with Codex using read-write mode
tia-mcp install codex --access-mode read-write

# Register with Claude Code bound to a specific project
tia-mcp install claude-code --tia-project C:\Projects\Line.ap21

# Preview the install command without running it
tia-mcp install codex --dry-run

# JSON output for automation
tia-mcp install codex --json
```

MiMoCode uses interactive mode and will prompt for values. The `--json` flag is not supported with MiMoCode.

Exit codes: `0` (success), `1` (general failure), `2` (invalid arguments), `3` (unsupported client), `4` (client not found), `5` (tia-mcp executable not found), `6` (native command failed), `7` (verification failed), `8` (unsupported option combination).

### Access modes

The server supports two access modes that control which operations are available:

- **read-write** (default) - full tool surface with the existing preview-and-apply write safety model.
- **read-only** - only observation operations are available. Write tools are not advertised to MCP clients, and prohibited operations are rejected at both the host and worker levels.

Enable read-only mode:

```powershell
tia-mcp --access-mode read-only
tia-mcp --read-only
$env:TIA_MCP_ACCESS_MODE = 'read-only'
tia-mcp
```

Configuration precedence: CLI argument > environment variable > default (read-write).

The mode is resolved once at startup and cannot be changed during the process lifetime. There is no MCP tool that changes the access mode at runtime.

In read-only mode, the server exposes exactly four MCP tools:

- `get_project_status` — read active project metadata without opening or switching projects.
- `browse_project_tree` — browse a bounded project subtree with optional `depth` and `startPath`.
- `execute_read_batch` — run the four retained non-project generic reads in a batch.
- `network_read` — run the two dedicated network reads in a batch.

The following operations are **not available** in read-only mode:

- `compile_check` (invokes the Siemens compilation API)
- All project lifecycle operations (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`)
- All data mutations (block, PLC type, tag, tag table, user constant, and network-device operations)
- All PLC control operations (`start_plc`, `stop_plc`)

In read-only mode, the server operates only on the project already open in TIA Portal. It never opens, creates, switches, or closes a project. A supplied `projectPath` is used only as an assertion that must match the currently open project.

Read-only mode is a security boundary enforced at three layers:

1. Write tools are not registered in the MCP tool discovery response.
2. The host-side `OperationAccessPolicy` rejects prohibited operations before the worker process is started.
3. The worker-side `WorkerOperationAuthorization` independently rejects prohibited operations even if a raw worker request bypasses the host.

MCP client configuration example:

```json
{
  "mcpServers": {
    "tia-portal-read-only": {
      "command": "tia-mcp",
      "args": ["--access-mode", "read-only"]
    }
  }
}
```

The `tia-mcp doctor` command reports the active access mode.

The package includes the `openness-worker` folder and required non-Siemens dependencies. It intentionally excludes `Siemens.Engineering*.dll`; those are loaded from the local TIA Portal installation at runtime.
