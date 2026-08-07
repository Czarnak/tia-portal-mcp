# Building from source

How to build the solution, run the test suite with coverage, and run the server locally
from a source checkout. See [CONTRIBUTING](../../CONTRIBUTING.md) for the contribution
workflow itself.

## Build From Source

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1
```

The `-m:1` option serializes solution builds. The MCP host project also builds and copies the net48 Openness worker, so serialized builds avoid duplicate parallel worker builds during local development.

The source build creates the .NET 8 host and copies the .NET Framework worker into:

```text
TiaMcpServer\bin\Debug\net8.0\openness-worker
```

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
'{ "method": "read_cross_references", "projectPath": null, "crossReferenceFilter": "ObjectsWithReferences" }' | .\TiaMcpServer.OpennessWorker\bin\Debug\net48\TiaMcpServer.OpennessWorker.exe
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
