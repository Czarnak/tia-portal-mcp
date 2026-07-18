## Build and test

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1        # CRITICAL: -m:1 required — parallel builds race on worker copy targets
dotnet test TiaMcpServer.sln               # xUnit; no TIA Portal needed for unit tests
```

The host csproj has MSBuild targets that build the net48 worker and copy it into `TiaMcpServer/bin/<Config>/net8.0/openness-worker/`. Parallel solution builds cause duplicate copy conflicts — always use `-m:1`.

## Project structure

```
TiaMcpServer/                  # .NET 8 MCP stdio host (entrypoint, packs as `tia-mcp` global tool)
TiaMcpServer.Contracts/        # netstandard2.0 shared contracts (worker IPC schema)
TiaMcpServer.OpennessWorker/   # .NET Framework 4.8 worker — loads Siemens.Engineering.*, talks to TIA Portal
TiaMcpServer.Tests/            # xUnit tests — uses linked <Compile Include> to test host internals
TiaMcpServer.FakeWorker/       # Fake worker for integration-testing IPC (NOT for diagnostic fakes)
ref/                           # Compile-time Siemens stubs for CI/package builds without TIA installed
```

**Two-process architecture**: The .NET 8 host cannot load Siemens Openness DLLs (they require .NET Framework remoting). It spawns a persistent net48 worker process and communicates via newline-delimited JSON over stdin/stdout. The worker auto-restarts after crash or timeout.

## Build reference selection

The worker csproj auto-detects whether to use real TIA assemblies or compile-time stubs:

- If `TiaPortalV21Dir` points to a folder containing `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll` → uses real references
- Otherwise → falls back to stubs in `ref/` (for CI and machines without TIA)

Override explicitly:
```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true    # force stubs
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=false   # force real TIA refs
```

`Directory.Build.props` sets the default `TiaPortalV21Dir` to `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`. Override via MSBuild property or environment variable.

## Testing

- **Framework**: xUnit with `[Fact]` / `[Theory]` / `[InlineData]`
- **No mocking libraries** — hand-written fakes implementing service interfaces
- **Linked compilation**: `TiaMcpServer.Tests.csproj` compiles host source via `<Compile Include>` with `<Link>` paths (e.g. `Diagnostics\*.cs`, `Worker\OpennessWorkerClient.cs`) to test internal classes without making them public
- **Each test class is self-contained** — no shared fixtures or base classes
- **Test file naming**: `{ClassUnderTest}Tests.cs` in namespace `TiaMcpServer.Tests`
- **Diagnostic fakes**: 6 service interfaces need fakes: `IApplicationInfoService`, `IEnvironmentVariableService`, `IRegistryService`, `IFileSystemService`, `IWindowsIdentityService`, `IProcessEnumerationService`
- **FakeWorker is NOT for diagnostics** — it's for integration-testing the worker IPC transport

### Gotchas

- `Microsoft.Win32.Registry` NuGet package — use version `5.0.0` (8.0.0 does not exist on nuget.org)
- `DiagnosticCheckResult` record: `Evidence` is an init-only property, not a constructor param — use object initializer `{ Evidence = dict }`, not named argument
- `DoctorJsonRenderer.cs` uses `System.Text.Encoding` but may be missing `using System.Text;` in linked-compilation context
- Using `params DiagnosticCheckResult[]` in test helpers can cause CS8752 under linked compilation — use non-params arrays with `new[] { ... }`

## CI/CD

Single workflow: `.github/workflows/publish.yml`

- Triggers on `v*` tags or manual `workflow_dispatch`
- Runs on `windows-latest`, builds with stubs (no TIA Portal in CI)
- Packs NuGet package + standalone win-x64 binary
- Publishes to NuGet via OIDC and creates GitHub Release with both artifacts
- Verifies package contents: must include `openness-worker/TiaMcpServer.OpennessWorker.exe`, must NOT include `Siemens.Engineering*.dll`

## Runtime requirements

- Windows only
- Siemens TIA Portal V21 with Openness enabled
- Current user must be in `Siemens TIA Openness` Windows user group
- .NET SDK 8.0.4xx+ for builds (`global.json` pins 8.0.400 with `rollForward: latestMajor`)
- .NET Framework 4.8 Runtime for the worker process

## graphify

This project has a graphify knowledge graph at `graphify-out/`.

- Before answering architecture or codebase questions, read `graphify-out/GRAPH_REPORT.md` for god nodes and community structure
- If `graphify-out/wiki/index.md` exists, navigate it instead of reading raw files
- For cross-module "how does X relate to Y" questions, prefer `graphify query`, `graphify path`, or `graphify explain` over grep
- After modifying code files in this session, run `graphify update .` to keep the graph current (AST-only, no API cost)
