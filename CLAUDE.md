# Project overview

MCP server for Siemens TIA Portal V21. Exposes 10 tools (batch reads/writes, project lifecycle, diagnostics) to MCP clients. Windows-only, requires TIA Portal V21 with Openness enabled.

## Two-process architecture (critical to understand)

The host (`TiaMcpServer`, net8.0) and the worker (`TiaMcpServer.OpennessWorker`, net48) are separate processes. Siemens Openness DLLs use .NET Framework remoting and **cannot run in a .NET 8 process** — this is why the split exists.

- Host communicates with worker via newline-delimited JSON over stdin/stdout
- The host builds the worker and copies it to `openness-worker/` subdirectory automatically
- The worker restarts automatically after crash or timeout
- `ref/` contains compile-time Siemens stubs so CI can build without TIA Portal installed

**Do not try to run Openness code directly from the host process** — always go through the worker.

## Solution structure

| Project | TFM | Role |
| --------- | ----- | ------ |
| `TiaMcpServer` | net8.0 | MCP stdio server, tool registration, batch engine, safety tokens, CLI (doctor) |
| `TiaMcpServer.Contracts` | netstandard2.0 | Shared DTOs (`WorkerRequest`, `WorkerResponse`, all info/result types) |
| `TiaMcpServer.OpennessWorker` | net48 | Worker that loads `Siemens.Engineering.*`, handles all TIA Portal operations |
| `TiaMcpServer.Tests` | net8.0 | xunit tests; links host source files directly via `<Compile Include>` (not a project reference) |
| `TiaMcpServer.FakeWorker` | net8.0 | Scripted worker stand-in for IPC integration tests |

## Build and test

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1          # -m:1 serializes builds — required to avoid parallel worker build conflicts
dotnet test TiaMcpServer.Tests
```

CI/stub build (no TIA Portal needed):

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Local dev (uses real TIA assemblies):

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

## Write safety model

Every write goes through preview-then-apply. This is non-negotiable.

- **Batch data writes**: call `preview_write_batch` (returns `safetyToken`), then `apply_write_batch` with `confirm=true` + the token
- **Type writes** (`update_type_content`): batch data writes like any other, but strict — the type must already exist and the declared name in `sourceContent` must match the target. Openness would otherwise create a new type from an unrecognized name.
- **Project lifecycle writes** (`open_project`, `create_project`, etc.): self-previewing — call the tool without `safetyToken` to get a preview + token, then call again with `confirm=true` + the token
- Safety tokens are single-use, expire in 10 minutes, bound to exact tool name + project path + requested input + current project state
- Reordering, changing input, or project state changes invalidate the token
- Successful writes append audit JSONL under `%LOCALAPPDATA%\TiaMcpServer\audit`

## Key conventions

- **`global.json`** pins .NET SDK 8.0.400 with `rollForward: latestMajor` — use `dotnet` commands, not raw `dotnet8`
- **Tests link host source files** via `<Compile Include>` — when editing files in `TiaMcpServer/Worker/`, `TiaMcpServer/Batch/`, `TiaMcpServer/Safety/`, `TiaMcpServer/Tools/`, `TiaMcpServer/Diagnostics/`, or `TiaMcpServer/Cli/`, the test project picks up changes automatically
- **Worker methods** are dispatched by `method` string in `WorkerRequest` — add new operations in `TiaMcpServer.OpennessWorker/Program.cs` switch expression and register them in the batch catalog
- **Contract types** live in `TiaMcpServer.Contracts` (netstandard2.0) so both host and worker can share them — no Siemens dependencies here
- Siemens DLLs are **never committed** to the repo or the NuGet package
