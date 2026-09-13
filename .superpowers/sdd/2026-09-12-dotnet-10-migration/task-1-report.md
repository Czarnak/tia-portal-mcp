# Task 1 Report: Pin the .NET 10 toolchain contract in tests

## Changes

- Added `Toolchain_UsesServicedDotNet10WithoutCrossMajorRollForward` to `CiWorkflowTests`.
- Added the required `System.Text.Json` import.
- Added `ToolAndStandalonePublish_KeepDistinctDeploymentModels` to `ReleaseWorkflowTests`.
- No production, SDK, workflow, or public MCP behavior files were changed.

## Test command and output summary

Command:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter "FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~ReleaseWorkflowTests" --configuration Release
```

The sandboxed attempt could not restore packages because NuGet TLS authentication was unavailable. The same command was then run with host network access and completed the build and test execution. Build emitted six pre-existing xUnit2031 warnings. Test summary:

```text
Failed: 1, Passed: 16, Skipped: 0, Total: 17
```

## RED evidence and why expected

The new test failed at the first deliberate contract assertion:

```text
Expected: "10.0.400"
Actual:   "8.0.400"
```

This is the expected RED state because Task 2 has not yet migrated `global.json` or the workflow SDK lines. The release-boundary assertions remained green; the overall 16 passing tests include the existing diagnostics tests and the new standalone/package deployment-model test.

## Files changed

- `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`
- `TiaMcpServer.Tests/Diagnostics/ReleaseWorkflowTests.cs`
- `.superpowers/sdd/2026-09-12-dotnet-10-migration/task-1-report.md`

## Self-review

- Assertions match the task brief verbatim, including exact SDK version, roll-forward policy, prerelease flag, workflow SDK checks, and publish boundary flags.
- Changes are limited to the two requested test files plus this report.
- No live TIA harness or test was invoked.
- No GitHub, Dependabot, production, or toolchain state was mutated.

## Concerns

- The focused test command intentionally fails until the subsequent migration task updates the toolchain and workflows.
- The initial sandboxed test invocation was restore-blocked by NuGet TLS credentials; the escalated retry provided the recorded RED evidence.
