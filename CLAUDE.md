# Project overview

MCP server for Siemens TIA Portal V21. Exposes 14 tools in read-write mode and four read-only tools in read-only mode. Windows-only, requires TIA Portal V21 with Openness enabled.

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

- **Generic batch data writes**: call `preview_write_batch` (returns `safetyToken`), then `apply_write_batch` with `confirm=true` + the unchanged operation list and token
- **Network writes**: call `network_write` with `confirm=false` and no token to preview, then call the same tool with `confirm=true`, the unchanged ordered operation list, and the returned token
- **Project lifecycle writes** (`open_project`, `create_project`, etc.): self-previewing — call the tool without `safetyToken` to get a preview + token, then call again with `confirm=true` + the token
- Safety tokens are single-use, expire in 10 minutes, bound to exact tool name + project path + requested input + current project state
- Reordering, changing input, or project state changes invalidate the token
- Successful writes append audit JSONL under `%LOCALAPPDATA%\TiaMcpServer\audit`

## Structured JSON contract rules (Network Phase 2 and beyond)

`network_read`/`network_write` are the first tools on the opt-in canonical JSON contract
(`TiaMcpServer/Json/CanonicalJson.cs`, `TiaMcpServer/Tools/StructuredToolResult.cs`,
`TiaMcpServer/OperationBatches/StructuredOperationBatch*.cs`,
`TiaMcpServer/Safety/CanonicalWriteSafety.cs`). These rules are durable for any future tool that
migrates onto it — not just Network:

- **Reuse the shared gate.** A new structured tool builds on `StructuredToolResult` /
  `StructuredOperationBatch` / `CanonicalWriteSafety`; do not hand-roll a parallel canonical-JSON
  or safety-token mechanism for a new domain.
- **Text and structured documents are the same document.** A migrated tool's `content` text block
  and its `structuredContent` come from exactly one `CanonicalJson.Serialize` call. They must
  never be built from two independent renderings that could drift apart.
- **Worker success payloads are typed.** A migrated tool declares exactly one CLR result type per
  operation (see `TiaMcpServer/Network/NetworkPayloadContract.cs` for the pattern) and rejects a
  payload that does not decode as that type — category `protocol_error` — rather than forwarding
  worker-shaped data under a schema that does not describe it. The rejected payload is never
  echoed back.
- **No nested JSON strings.** A migrated tool's operation results are real JSON objects/arrays
  under the response document, never an escaped JSON string a caller has to parse a second time.
  This is exactly the Phase 1 defect Phase 2 removed for Network.

See `docs/ARCHITECTURE.md` §7a for the full seam description and the exact host-to-worker
selector boundary, and `docs/SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md` for the concrete
Network contract these rules describe in the abstract.

A separately authorized live-TIA acceptance harness for the Network contract lives at
`scripts/live-test-network-phase2.ps1` (PowerShell 7, `Read`/`Preview`/`Apply` modes). It is never
run by an ordinary test or CI job — see `TiaMcpServer.Tests/NetworkLiveHarnessContractTests.cs`.

## Key conventions

- **`global.json`** pins .NET SDK 8.0.400 with `rollForward: latestMajor` — use `dotnet` commands, not raw `dotnet8`
- **Tests link host source files** via `<Compile Include>` — when editing files in `TiaMcpServer/Worker/`, `TiaMcpServer/Batch/`, `TiaMcpServer/Network/`, `TiaMcpServer/OperationBatches/`, `TiaMcpServer/Safety/`, `TiaMcpServer/Tools/`, `TiaMcpServer/Diagnostics/`, or `TiaMcpServer/Cli/`, the test project picks up changes automatically
- **Worker methods** are dispatched by `method` string in `WorkerRequest` — add new operations in `TiaMcpServer.OpennessWorker/Program.cs` switch expression, then register them in their owning domain catalog and invoker. A worker method is not automatically a generic batch operation; network operations use their own request, catalog, and invoker.
- **Contract types** live in `TiaMcpServer.Contracts` (netstandard2.0) so both host and worker can share them — no Siemens dependencies here
- Siemens DLLs are **never committed** to the repo or the NuGet package
