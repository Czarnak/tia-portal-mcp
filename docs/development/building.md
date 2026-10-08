# Building from source

How to build the solution, run the test suite with coverage, and run the server locally
from a source checkout. See [CONTRIBUTING](../../CONTRIBUTING.md) for the contribution
workflow itself.

## Build From Source

Use a stable .NET 10 SDK. The repository requires SDK `10.0.400` or newer within the .NET 10
feature bands; `global.json` uses `latestFeature` roll-forward and excludes prerelease SDKs.

```powershell
dotnet restore TiaMcpServer.slnx
dotnet build TiaMcpServer.slnx -m:1
```

The `-m:1` option serializes solution builds. The MCP host project also builds and copies the net48 Openness worker, so serialized builds avoid duplicate parallel worker builds during local development.

The source build creates the .NET 10 host and copies the .NET Framework worker into:

```text
TiaMcpServer\bin\Debug\net10.0\openness-worker
```

Custom .NET integrations that compile directly against the C# MCP SDK must review the
ModelContextProtocol 2.2 migration notes and retest protocol negotiation, tool schemas, and
structured results. Normal `tia-mcp` users do not install that NuGet package: the framework-dependent
global tool uses the supported .NET 10 runtime, while the separate self-contained `win-x64` archive
includes the host runtime.

### Generated Openness references

`reference-stubs/Siemens.Engineering.Base/`, `reference-stubs/Siemens.Engineering.Step7/` and `reference-stubs/Siemens.Engineering.WinCCUnified/`
contain the minimal C# declarations used by the current worker and the locked compile-only
`reference-stubs/TiaMcpServer.OpennessReferenceProbe/`. The dedicated solution is
`reference-stubs/TiaMcpServer.ReferenceStubs.sln`. The old tracked references already contained
Multiuser types, but were opaque and much broader than necessary. The reviewed source now
owns the generated `ref/Siemens.Engineering.Base.dll`, `ref/Siemens.Engineering.Step7.dll` and `ref/Siemens.Engineering.WinCCUnified.dll` (the WinCC Unified stub backs `hmi_read`; all three share one source hash).

Restore both solutions and run the default read-only verifier:

```powershell
dotnet restore TiaMcpServer.slnx -m:1
dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln -m:1
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
```

The verifier builds in a temporary directory, compiles the probe, and checks both generated and
tracked artifact identities and canonical source-hash metadata. It does not replace tracked
artifacts by default. After an intentional source change, explicitly regenerate, inspect the
source and binary diff, then verify again without `-Update`:

```powershell
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore -Update
pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
```

`-Update` replaces only the three known reference DLLs after validation. Both retain their Siemens
assembly simple names, version `21.0.0.0`, and public-key token `29bfe5fdf4ba5d3b` through
`reference-stubs/Siemens.Engineering.PublicKey.snk`, a public-only key used for delay signing.
No private Siemens key or proprietary implementation is present; these compile aids must never
be loaded at runtime.

The canonical SHA256 digest includes ordered repository-relative paths, lengths, and raw bytes
of the selected C# and project inputs, both relevant build-property files, and the public key.
`.gitattributes` pins those exact text inputs to CRLF; preserve that scoped policy when editing
or regenerating. `StubSourceHash` records source provenance. Whole-PE SHA256 hashes are useful
diagnostics, but compiler patch versions can change PE bytes without changing the canonical
source hash; do not require cross-compiler binary byte equality as a provenance check.

Force stub mode for an offline Release build:

```powershell
dotnet build TiaMcpServer.slnx -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=true
```

For signature compatibility, build both the solution and locked probe against installed V21:

```powershell
dotnet build TiaMcpServer.slnx -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 --no-restore -c Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

The worker and probe log the selected reference directory and mode. Stub success proves the
reviewed declarations compile; installed-reference success proves signature compatibility.
Neither executes TIA, Project Server, or PLC operations or proves live behavior. Operation-specific
live acceptance is a separate gate for later runtime work.

Generated stubs are excluded from runtime output. All `Siemens.Engineering*.dll` files, including
installed real references, are excluded from the NuGet package and staged worker payload.
The net48 worker still loads real assemblies from the local TIA installation at runtime.

The Multiuser foundation contracts are passive, Siemens-free DTOs with explicit JSON nulls;
capability descriptions do not grant authorization, and `sessionBind` differs from `sessionOpen`.
They add no MCP tool, dispatch operation, `.als21` lifecycle support, or binding change. PR 2
owns internal context integration and must be planned from merged `main` after PR 1 evidence
and review are accepted. Public selection/open and later save, close, discard, and commit remain
deferred. Existing `bind_project`, lifecycle confirmation, access modes, and audit v2 remain
as documented in the [installation guide](../guides/installation.md).

### Startup subprocess tests

`RemovedOptionTests` launches the host with `dotnet run --no-build --no-restore` in the
test assembly's configuration. Build the solution from the current checkout before running
these tests, including filtered runs. Building only `TiaMcpServer.Tests` links host source
into the test assembly but does not build the host executable; an isolated test run can
otherwise launch a stale binary or fail because it is missing.

For an offline Debug run with Siemens reference stubs:

```powershell
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~RemovedOptionTests
```

For Release tests, pass `--configuration Release` to both commands. Use the same
solution-build prerequisite before the full suite and coverage runs below.

### Coverage

CI collects coverage, then enforces an 80% scoped line-coverage threshold locally (before the Codecov upload, which stays reporting-only). Run the same scoped collection and threshold check locally:

```powershell
$results = Join-Path 'TestResults' ('local-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory $results
$report = Get-ChildItem -LiteralPath $results -Recurse -Filter coverage.cobertura.xml | Select-Object -First 1
./scripts/verify-coverage-threshold.ps1 -CoveragePath $report.FullName -MinimumLineRate 0.80
```

`coverage.runsettings` scopes the Cobertura report to `TiaMcpServer` and `TiaMcpServer.Contracts`; test assemblies, `TiaMcpServer.FakeWorker`, and `TiaMcpServer.OpennessWorker` are excluded. `verify-coverage-threshold.ps1` exits non-zero below the threshold.

## Run Locally

Start TIA Portal V21 first and open a project, then run:

```powershell
dotnet run --project TiaMcpServer
```

The server uses MCP over stdio, so it is normally launched by an MCP client rather than used interactively in a terminal.

You can test the Openness worker directly for internal diagnostics (the worker protocol is not the public MCP API):

```powershell
'{ "method": "browse_project_tree", "projectPath": null }' | .\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe
```

Use the dedicated `network_read` and `network_write` MCP tools for hardware discovery,
catalog lookup, device creation, and device configuration.

Expected successful response shape:

```json
{"success":true,"payload":"[...]"}
```

Expected error response shape:

```json
{"success":false,"error":"No running TIA Portal V21 instance found. Please start TIA Portal before using the MCP server."}
```
