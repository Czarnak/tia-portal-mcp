# Phase 5 Plan 3 Task 3 Report

## What I implemented

- Added immutable postcondition evidence, verifier, and import result helpers.
- Added a pure import coordinator that parses before staging, stages and verifies every declared document, imports once, verifies once, and cleans staging in `finally`.
- Replaced the legacy importer content-mode paths with coordinator-backed `ImportFromDocuments` handling. The declared bundle primary is used for post-import verification.
- Added postcondition verification: compile once, re-export the target documents, require the primary document to exist and be non-empty, and report `postcondition_failed` with an uncertain-state warning when either predicate fails.
- Propagated non-fatal staging and verification cleanup warnings through `BlockImportResult` to the worker response.
- Added a FakeWorker postcondition-failure scenario and host integration assertion proving the first response is surfaced without retry.

## TDD Evidence

### RED

Command:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~BlockPostconditionVerifierTests|FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests" -m:1
```

The initial sandbox run could not restore NuGet packages because of `NU1301` TLS/authentication failures. The elevated rerun restored packages and then failed as intended with `CS2001` for the four missing production source files:

- `BlockPostconditionEvidence.cs`
- `BlockPostconditionVerifier.cs`
- `BlockImportResult.cs`
- `BlockImportCoordinator.cs`

This was the expected RED state: the linked Task 3 tests could not compile until the new production helpers existed.

### GREEN

Command:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockImport|FullyQualifiedName~BlockPostcondition|FullyQualifiedName~OpennessWorkerClientIntegrationTests" -m:1
```

Result: `85` passed, `0` failed, `0` skipped.

## Tests Run

```powershell
dotnet build TiaMcpServer.sln --no-restore -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: succeeded with `0` warnings and `0` errors.

```powershell
dotnet test TiaMcpServer.sln --no-build -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `545` passed, `0` failed, `0` skipped.

## Files Changed

- `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionEvidence.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionVerifier.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImportResult.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- `TiaMcpServer.FakeWorker/Program.cs`
- `TiaMcpServer.Tests/BlockPostconditionVerifierTests.cs`
- `TiaMcpServer.Tests/BlockImportCoordinatorTests.cs`
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- `TiaMcpServer.Tests/OpennessWorkerClientIntegrationTests.cs`
- `.superpowers/sdd/task-3-report.md`

## Important Review Fix: Declared Primary Re-export Verification

### Fix Summary

- Routed `BlockExporter.VerifyPrimaryDocument` through a testable re-export verification selector.
- The selector receives both `ResolvedBlockTarget.DocumentName` and the declared primary name, but invokes the non-empty-document predicate only for the declared primary name.
- Added focused regression coverage with distinct names: `ResolvedTarget.xml` and `DeclaredPrimary.xml`.

### TDD RED/GREEN Evidence

#### RED

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockPostconditionVerifierTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: failed as expected with `CS0117` because `BlockPostconditionVerifier.VerifyReExportedPrimaryDocument` did not yet exist.

#### GREEN

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockPostconditionVerifierTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `6` passed, `0` failed, `0` skipped.

### Covering Tests

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockPostconditionVerifierTests|FullyQualifiedName~BlockImportCoordinatorTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `12` passed, `0` failed, `0` skipped.

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: succeeded with `0` warnings and `0` errors after an elevated retry for sandbox `NU1301` NuGet TLS/authentication failure.

```powershell
dotnet test TiaMcpServer.sln --no-build -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `549` passed, `0` failed, `0` skipped.

### Files Changed

- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionVerifier.cs`
- `TiaMcpServer.Tests/BlockPostconditionVerifierTests.cs`
- `.superpowers/sdd/task-3-report.md`

## Final Review Fixes

### Fix Summary

- Added an internal, dependency-free `BlockExporter` verification seam. The Siemens-facing `VerifyPrimaryDocument` now delegates its re-export and verification-temp cleanup into that seam.
- Added direct `BlockExporter` regression coverage that asserts the export delegate receives the declared primary document name when it differs from the resolved target document name.
- Added verification-temp cleanup-failure coverage for both re-export outcomes. The warning is capped at 512 characters and does not change the postcondition result.

### TDD RED/GREEN Evidence

#### RED

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockExporterVerificationTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: failed as expected with `CS2001` because the linked `BlockExporterVerification` production seam did not yet exist.

#### GREEN

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockExporterVerificationTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: passed, 3/3 tests.

### Covering Tests

```powershell
dotnet test TiaMcpServer.Tests\TiaMcpServer.Tests.csproj --no-build --filter "FullyQualifiedName~BlockExporterVerificationTests|FullyQualifiedName~BlockPostconditionVerifierTests|FullyQualifiedName~BlockImportCoordinatorTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: passed, 15/15 tests.

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: initial sandboxed restore was blocked by NuGet TLS/authentication `NU1301`; the required rerun with external package access succeeded with 0 warnings and 0 errors.

```powershell
dotnet test TiaMcpServer.sln --no-build -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: passed, 552/552 tests.

### Files Changed

- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockExporterVerification.cs`
- `TiaMcpServer.Tests/BlockExporterVerificationTests.cs`
- `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- `.superpowers/sdd/task-3-report.md`

## Self-review Findings

- Fixed before final verification: multi-document verification must use the first declared document, not the single-document fallback name.
- Fixed before final verification: construct the immutable success result only after `finally`, so a staging cleanup warning remains visible.
- `git diff --check` passed. The staged allowlist excludes generated `graphify-out` files.

## Issues or Concerns

- Verification used compile-time Siemens stubs. A live TIA Portal execution remains necessary to validate the Siemens API behavior against a real project.
- Cleanup failure warnings are covered by the coordinator implementation, but deterministic filesystem cleanup-failure injection is not included in the pure test suite.

## Review Fix Follow-up

### Fix Summary

- Routed verification document export through `BlockPostconditionVerifier.ReExportPrimaryDocument`, so `BlockExporter.VerifyPrimaryDocument` passes the declared first document name to `ExportAsDocuments` and checks that same name after re-export.
- Added an injectable staging cleanup operation to `BlockImportCoordinator.Execute` for deterministic cleanup-failure coverage. Cleanup warnings remain capped at 512 characters and do not alter a successful result or mask an import failure.

### TDD Evidence

#### RED

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockPostconditionVerifierTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: failed as expected with `CS0117` because `BlockPostconditionVerifier.ReExportPrimaryDocument` did not exist, and `CS1739` because `BlockImportCoordinator.Execute` had no `cleanupDirectory` seam.

#### GREEN

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockImportCoordinatorTests|FullyQualifiedName~BlockPostconditionVerifierTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `11` passed, `0` failed, `0` skipped.

### Covering Tests

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~BlockImport|FullyQualifiedName~BlockPostcondition|FullyQualifiedName~OpennessWorkerClientIntegrationTests" -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `88` passed, `0` failed, `0` skipped.

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: succeeded with `0` warnings and `0` errors.

```powershell
dotnet test TiaMcpServer.sln --no-build -m:1 /p:UseTiaPortalReferenceStubs=true
```

Result: `548` passed, `0` failed, `0` skipped.

### Files Changed

- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImportCoordinator.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockPostconditionVerifier.cs`
- `TiaMcpServer.Tests/BlockImportCoordinatorTests.cs`
- `TiaMcpServer.Tests/BlockPostconditionVerifierTests.cs`
- `.superpowers/sdd/task-3-report.md`
