# Phase 5 Plan 3 Final Review Fix Round 2 Report

Branch: `codex/phase5-03-plc-block-write-repairs`

## RED

Initial command:

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockImportBundleParserTests|FullyQualifiedName~BlockSourceValidatorTests" --verbosity minimal
```

The sandboxed restore failed before test execution with `NU1301` because NuGet TLS
authentication could not reach `https://api.nuget.org/v3/index.json`. An approved
`dotnet restore TiaMcpServer.sln` outside the sandbox then completed successfully.

Behavioral RED command:

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockImportBundleParserTests|FullyQualifiedName~BlockSourceValidatorTests" --verbosity minimal
```

Result: exit 1; 12 failed, 29 passed, 41 total. The malformed delimiter cases,
all tested DOS device names, and `DB/UNKNOWN` were accepted instead of throwing
`WorkerOperationException(validation_error)`.

Preflight RED command:

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockWritePreflightTests" --verbosity minimal
```

Result: exit 1 with four `CS0103` errors because the pure
`BlockWritePreflight` contract did not exist.

## Production Changes

- `BlockImportBundleParser.cs` rejects malformed `--- FILE:` candidate lines
  before single-document fallback and rejects reserved DOS device basenames,
  case-insensitively, with or without extensions.
- `BlockWritePreflight.cs` adds immutable update/create preflight descriptors.
  Invalid block paths are converted to `validation_error`; update paths are
  parsed before bundle parsing and coordinator staging.
- `BlockImporter.cs` completes pure path and bundle preflight before entering
  `BlockImportCoordinator.Execute` and reuses the parsed address for Siemens
  target resolution.
- `BlockMutationService.cs` completes normalized type/language preflight before
  PLC or group resolution.
- `BlockSourceValidator.cs` permits the DB/GlobalDB default `LAD` sentinel only
  and rejects unsupported database languages as `validation_error`.
- The pure preflight helper is linked into the net8 test project.

Changed implementation/test files:

- `TiaMcpServer.OpennessWorker/Openness/BlockImportBundleParser.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockMutationService.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockSourceValidator.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockWritePreflight.cs`
- `TiaMcpServer.Tests/BlockImportBundleParserTests.cs`
- `TiaMcpServer.Tests/BlockSourceValidatorTests.cs`
- `TiaMcpServer.Tests/BlockWritePreflightTests.cs`
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

## GREEN

Focused command:

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockImportBundleParserTests|FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockImportStagerTests|FullyQualifiedName~BlockSourceGeneratorTests|FullyQualifiedName~BlockSourceValidatorTests|FullyQualifiedName~BlockWritePreflightTests|FullyQualifiedName~BlockMutationPostconditionTests" --verbosity minimal
```

Result: exit 0; 67 passed, 0 failed, 0 skipped.

Serialized stub build:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true --no-restore
```

Result: exit 0; 0 warnings and 0 errors.

Full suite:

```powershell
dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
```

Result: exit 0; 582 passed, 0 failed, 0 skipped.

Coverage verification:

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --no-build --collect:"XPlat Code Coverage" --results-directory build\coverage-final --verbosity minimal
```

Result: exit 0; 582 passed, with 87.32% overall line coverage and 86.92%
overall branch coverage in the generated Cobertura report.

## Finding 4 Decision

Exact submitted XML versus re-exported XML comparison was not implemented.

Plan 3 requires one compile and a re-export whose primary document exists and
is non-empty (`docs/superpowers/plans/2026-07-23-phase5-03-plc-block-write-repairs.md`,
lines 252-260); its exit gate repeats only compile plus non-empty re-export
(line 452). AC-026 requires a byte-identical submitted bundle to remain
semantically unchanged while compile or re-export verification succeeds, and
AC-027 assigns proof that a specific edit is reflected to live certification
(`docs/superpowers/acceptance/2026-07-23-phase5-reliability-lifecycle-integrity.md`,
lines 38-39).

A generic exact XML comparison would be brittle because Siemens can normalize
exported XML independently of semantic block content. It could reject both an
unchanged AC-026 round trip and a valid AC-027 edit. The worker also has no
edit-specific semantic predicate with which to identify the intended change.
Keep the Plan 3 non-empty re-export postcondition and prove the selected edit
through AC-027 live evidence. Re-review should reject exact XML equality unless
a normalization-aware semantic comparison contract and live fixtures are added
in a separately scoped plan.

## Commit

Commit SHA: `SELF` (this report is committed atomically with the fix; the exact
SHA is returned in the final handoff).
