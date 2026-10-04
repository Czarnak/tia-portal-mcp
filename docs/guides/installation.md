# Installation

How to install the TIA Portal MCP server, verify it, and register it with an MCP client.
For a condensed quick start, see the [README](../../README.md).

## Requirements

- Windows
- Siemens TIA Portal V21 installed
- TIA Portal Openness installed and enabled
- Current Windows user is a member of the `Siemens TIA Openness` user group
- Stable .NET 10 SDK (`10.0.400` or newer in the .NET 10 feature bands) for `dotnet tool install`
- .NET Framework 4.8 Runtime for the Openness worker

Source builds additionally need:

- Stable .NET SDK 10.0.400 or newer in the .NET 10 feature bands. The repo's `global.json`
  requires 10.0.400, rolls forward through stable .NET 10 feature bands, and excludes prerelease SDKs.
- .NET Framework 4.8 Developer Pack or targeting pack

By default, source builds expect Openness DLLs here:

```text
C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48
```

Local developer builds prefer real TIA Portal V21 assemblies from `TiaPortalV21Dir`. You can override that path with the `TiaPortalV21Dir` MSBuild property or environment variable. It must point to the folder containing `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll`.

The repo contains source-owned, generated compile-time reference stubs in `ref/`, declared under `reference-stubs/`, so CI can build and package the MCP server without installing TIA Portal. They preserve the V21 assembly identity using a public-only key and are never runtime substitutes. See [building from source](../development/building.md#generated-openness-references) for default verification and explicit artifact regeneration. Stub and installed-reference builds are compile evidence; live TIA behavior requires separate acceptance. Those stubs are fallback-only when a local TIA install is not found. To force stub references for CI/package builds:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
```

To force local TIA references:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

During build, the worker prints the selected reference directory:

```text
TIA Openness compile references: C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48 (UseTiaPortalReferenceStubs=false)
```

The passive Multiuser contract foundation does not enable `.als21` selection/open or local-session
save, close, discard, or commit. Internal binding integration belongs to PR 2; public local-session
operations remain deferred. Existing explicit project selection and lifecycle rules below still apply.

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
`bind_project` explicitly adopts an already-open project; `open_project` opens one, and
`create_project` creates one. Only open/create can open a project. Ordinary unbound reads do
not silently bind the session, and no request implicitly opens a project.

The worker never chooses the first running Portal or first open project. It selects an exact project
path when one was supplied, or a sole candidate when there is genuinely only one. Multiple possible
Portals/projects fail with `target_ambiguous` before Attach or mutation. A later path, PID, worker, or
project-generation mismatch fails with `binding_conflict` and invalidates the binding; call
`bind_project` with `forceRebind=true` for an already-open project, or start a new MCP session. `get_project_status(projectPath)`
is read-only and non-binding: do not use it to switch projects. Use `bind_project` for adopting or
switching to an already-open project; use `open_project` only for opening one.

Call `bind_project` without a path to adopt the sole open project or receive the candidate list;
with multiple open projects, supply an advertised absolute `.ap21` path. A different configured,
verified, or last-bound path requires `forceRebind:true`. Reattachment may show TIA's Openness
access dialog, which a human must answer. Worker ownership is not carried across a detach; after
reattachment the project is treated as UI-owned. Binding itself never opens, creates, saves or closes.

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

- no project binding is a Warning in every mode, with remediation naming `bind_project`;
- an invalid, relative, non-`.ap21`, or missing project path is a failure;
- multiple TIA Portal processes are a warning when an explicit binding is configured, because Doctor cannot verify the live match without attaching;
- multiple TIA Portal processes with no binding are a Warning in every mode; select the intended open project with `bind_project`.

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
- `--access-mode <mode>` - access mode: `read-only`, `read-write`, or `full` (default: `read-only`).
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

The server supports three access modes, enforced at discovery, host dispatch, and worker dispatch:

- **read-only** - five tools: four observation tools plus `bind_project`; no compile, edits,
  lifecycle, or PLC control.
- **read-write** (server startup default) - fifteen tools: the read-only surface plus
  `compile_check`, `preview_write_batch`, `apply_write_batch`, `network_write`, and all six lifecycle
  tools. Every actual lifecycle call requires one confirmation form.
- **full** - the same fifteen tools; lifecycle runs without server elicitation, and legacy batch
  `start_plc` / `stop_plc` is permitted through OnlineControl. Unknown operations remain denied.

**Migration:** read-write clients can save, close, and use all lifecycle tools with confirmation.
Select `--access-mode full` for lifecycle without server elicitation or PLC runtime control.
The install command still defaults
to read-only. To preview registration without changing client configuration:

```powershell
tia-mcp install codex --access-mode full --dry-run
```

`--project` configures an assertion; a read's `projectPath` never opens or switches projects in
any mode. Bind an already-open project explicitly with `bind_project` before writable operations.

Enable read-only mode:

```powershell
tia-mcp --access-mode read-only
tia-mcp --read-only
$env:TIA_MCP_ACCESS_MODE = 'read-only'
tia-mcp
```

Configuration precedence: CLI argument > environment variable > default (read-write).

The mode is resolved once at startup and cannot be changed during the process lifetime. There is no MCP tool that changes the access mode at runtime.

In read-only mode, the server exposes exactly five MCP tools:

- `bind_project` — adopt or switch to an already-open project without project mutation.
- `get_project_status` — read active project metadata without opening or switching projects.
- `browse_project_tree` — browse a canonical paged v3 snapshot using `startSelector`, `depth`, and `pageSize`; continue with `cursor`.
- `execute_read_batch` — run the four retained non-project generic reads in a batch.
- `network_read` — run dedicated network reads in a batch.

The following operations are **not available** in read-only mode:

- `compile_check` (invokes the Siemens compilation API)
- All project lifecycle operations (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`)
- All data mutations (block, PLC type, tag, tag table, user constant, and network-device operations)
- All PLC control operations (`start_plc`, `stop_plc`)

In read-only mode, the server operates only on an already-open project. It never opens, creates, saves or closes a project. Explicit `bind_project` can switch the selected session; ordinary reads cannot. A read's `projectPath` is an assertion that must match the open project. Project-tree cursors reject binding changes with `cursor_binding_mismatch`, even after switching back to the original path.

Read-only mode is a security boundary enforced at three layers:

1. Project mutation, engineering, and lifecycle tools are not registered in MCP discovery; session selection remains available.
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

### User confirmation by access mode

The removed startup switch fails with this exact migration message:

```text
--confirm-with-user was removed. Confirmation follows the access mode: read-write asks for every lifecycle call; use --access-mode full to run lifecycle tools without prompts.
```

Every actual lifecycle call in read-write requires one form elicitation `accept` with boolean
`confirm:true`, including calls with no guards or only info guards. Missing capability, decline,
cancel, timeout, or transport failure denies with `access_denied`. Full uses policy confirmation
without server elicitation. Block guards stop a call in every mode, and `dryRun:true` never elicits.
Lifecycle accepts `dryRun` with its operation inputs; the former agent confirmation array is removed.
After accepted elicitation the server resolves fresh state under the same binding lease before
dispatch. Audit v2 records confirmation by `user`, `policy`, or `none`; acknowledge guard satisfaction
is `user`, `policy`, or null. Network uses guarded writes without server elicitation: preview with
`dryRun:true`; omitted `dryRun` executes. Generic-batch token tools retain their behavior.

An elicitation client's accepted response does not prove that a person saw a dialog. Keep
destructive tools out of client auto-approve lists to require client permission prompts on every
call. See the [lifecycle reference](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md#lifecycle-operations)
for dry-run examples, all seven guards, and the staged major-release input/output migration.

The package includes the `openness-worker` folder and required non-Siemens dependencies. It intentionally excludes `Siemens.Engineering*.dll`; those are loaded from the local TIA Portal installation at runtime.
