# Phase 5 Plan 1: CI and Quality Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every solution build deterministic and enforce at least 80% scoped line coverage before later Phase 5 behavior changes land.

**Architecture:** Keep GitHub Actions as the CI orchestrator. Pin solution builds to `-m:1`, collect Cobertura from the test project with a repository-owned runsettings file, and enforce the threshold locally with a strict PowerShell parser before Codecov upload.

**Tech Stack:** GitHub Actions, PowerShell 7, .NET SDK, xUnit, Coverlet XPlat collector, Cobertura XML.

## Global Constraints

- AC scope: AC-001, AC-002, AC-003.
- `publish.yml` currently has no solution-build step. Do not add or modify one merely to satisfy AC-001; instead, assert that every solution-build command present in any workflow uses `-m:1`.
- Codecov remains reporting-only. The repository script is the authoritative pass/fail gate.
- Coverage includes `TiaMcpServer` and `TiaMcpServer.Contracts`; excludes tests, FakeWorker, generated code, and the net48 Openness worker.
- Do not lower `0.80`, broaden exclusions, or mark the job non-blocking to make CI green.
- At each task end, run the focused test, serialized stub build, and full test suite before commit.

---

## Task 1: Pin every solution build to one MSBuild node

**Files:**

- Create: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`
- Modify: `.github/workflows/ci.yml`
- Inspect only: `.github/workflows/publish.yml`

**Acceptance:** AC-001.

- [ ] **Step 1: Add the failing workflow contract test.**

  Create `CiWorkflowTests` with a repository-root helper matching the existing `ReleaseWorkflowTests` convention. Enumerate both `*.yml` and `*.yaml`, extract complete `run:` command blocks including indented/folded continuation lines, and follow repository-local `.ps1`, `.cmd`, or `.bat` scripts invoked by those blocks. Then assert every discovered solution build is serialized:

  ```csharp
  [Fact]
  public void EverySolutionBuild_IsSerialized()
  {
      var solutionBuildCommands = EnumerateWorkflowFiles()
          .SelectMany(ReadRunCommandBlocks)
          .SelectMany(ExpandRepositoryBuildScripts)
          .Where(command => command.Contains("dotnet build", StringComparison.OrdinalIgnoreCase))
          .Where(command => command.Contains("TiaMcpServer.sln", StringComparison.OrdinalIgnoreCase))
          .ToArray();

      Assert.NotEmpty(solutionBuildCommands);
      Assert.All(solutionBuildCommands, command => Assert.Contains("-m:1", command, StringComparison.Ordinal));
  }
  ```

  `ExpandRepositoryBuildScripts` returns the original command plus the contents of each referenced repository-local build script. It rejects paths that resolve outside the repository. This prevents `.yaml`, multiline, and script-indirection blind spots.

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~CiWorkflowTests.EverySolutionBuild_IsSerialized
  ```

  Expected failure: the current CI `dotnet build TiaMcpServer.sln` command does not contain `-m:1`.

- [ ] **Step 3: Serialize the CI build.**

  In `.github/workflows/ci.yml`, keep restore separate and make the solution build explicit:

  ```yaml
  - name: Restore
    run: dotnet restore TiaMcpServer.sln

  - name: Build
    run: dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
  ```

  Leave `publish.yml` unchanged unless a solution-build command is actually introduced there later.

- [ ] **Step 4: Rerun the focused test and observe GREEN.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~CiWorkflowTests.EverySolutionBuild_IsSerialized
  ```

- [ ] **Step 5: Run task verification.**

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  ```

- [ ] **Step 6: Review and commit.**

  Review `git diff -- .github/workflows/ci.yml TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`, confirm no workflow secrets or permissions changed, then commit:

  ```powershell
  git add .github/workflows/ci.yml TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs
  git commit -m "ci: serialize solution builds"
  ```

---

## Task 2: Define the approved coverage scope

**Files:**

- Create: `TiaMcpServer.Tests/coverage.runsettings`
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`

**Acceptance:** AC-002.

- [ ] **Step 1: Add a failing runsettings contract test.**

  Add `CoverageRunsettings_UsesApprovedScope` to `CiWorkflowTests`. Parse the XML with `XDocument` and assert all of the following exact values:

  ```csharp
  Assert.Equal("cobertura", Value("Format"));
  Assert.Equal("[TiaMcpServer]*,[TiaMcpServer.Contracts]*", Value("Include"));
  Assert.Equal("[TiaMcpServer.Tests]*,[TiaMcpServer.FakeWorker]*,[TiaMcpServer.OpennessWorker]*", Value("Exclude"));
  Assert.Contains("GeneratedCodeAttribute", Value("ExcludeByAttribute"), StringComparison.Ordinal);
  Assert.Contains("CompilerGeneratedAttribute", Value("ExcludeByAttribute"), StringComparison.Ordinal);
  Assert.Contains("**/*.g.cs", Value("ExcludeByFile"), StringComparison.Ordinal);
  Assert.Contains("**/*.Designer.cs", Value("ExcludeByFile"), StringComparison.Ordinal);
  ```

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~CiWorkflowTests.CoverageRunsettings_UsesApprovedScope
  ```

  Expected failure: `TiaMcpServer.Tests/coverage.runsettings` does not exist.

- [ ] **Step 3: Add the runsettings file.**

  Use this configuration:

  ```xml
  <?xml version="1.0" encoding="utf-8"?>
  <RunSettings>
    <DataCollectionRunSettings>
      <DataCollectors>
        <DataCollector friendlyName="XPlat Code Coverage">
          <Configuration>
            <Format>cobertura</Format>
            <Include>[TiaMcpServer]*,[TiaMcpServer.Contracts]*</Include>
            <Exclude>[TiaMcpServer.Tests]*,[TiaMcpServer.FakeWorker]*,[TiaMcpServer.OpennessWorker]*</Exclude>
            <ExcludeByAttribute>GeneratedCodeAttribute,CompilerGeneratedAttribute</ExcludeByAttribute>
            <ExcludeByFile>**/*.g.cs,**/*.Designer.cs,**/obj/**</ExcludeByFile>
          </Configuration>
        </DataCollector>
      </DataCollectors>
    </DataCollectionRunSettings>
  </RunSettings>
  ```

- [ ] **Step 4: Rerun the contract test and collect a real report.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~CiWorkflowTests.CoverageRunsettings_UsesApprovedScope
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults
  ```

  Confirm exactly one new `coverage.cobertura.xml` exists below `TestResults` and its `<packages>` include only the approved production assemblies.

- [ ] **Step 5: Run task verification and commit.**

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git add TiaMcpServer.Tests/coverage.runsettings TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs
  git commit -m "test: define scoped coverage collection"
  ```

---

## Task 3: Enforce the inclusive 80% line-rate gate

**Files:**

- Create: `scripts/verify-coverage-threshold.ps1`
- Create: `TiaMcpServer.Tests/Diagnostics/CoverageThresholdScriptTests.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`
- Modify: `.github/workflows/ci.yml`

**Acceptance:** AC-003, and final wiring for AC-002.

- [ ] **Step 1: Add failing script behavior tests.**

  `CoverageThresholdScriptTests` must create temporary Cobertura XML files and invoke `pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1`. Add:

  ```csharp
  [Theory]
  [InlineData("0.79", 1)]
  [InlineData("0.80", 0)]
  [InlineData("0.81", 0)]
  public void LineRate_UsesInclusiveMinimum(string lineRate, int expectedExitCode)

  [Fact]
  public void MissingFile_Fails()

  [Fact]
  public void MissingLineRate_Fails()

  [Fact]
  public void MalformedXml_Fails()
  ```

  Use `<coverage line-rate="{lineRate}" />` for the valid fixtures. Assert non-zero for every invalid-input case and assert stdout/stderr does not echo file contents.

- [ ] **Step 2: Run the script tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~CoverageThresholdScriptTests
  ```

  Expected failure: the script does not exist.

- [ ] **Step 3: Implement the strict threshold script.**

  The script interface is fixed:

  ```powershell
  ./scripts/verify-coverage-threshold.ps1 -CoveragePath <coverage.cobertura.xml> -MinimumLineRate 0.80
  ```

  Implement mandatory `string $CoveragePath` and `double $MinimumLineRate` parameters, `Set-StrictMode -Version Latest`, `$ErrorActionPreference = 'Stop'`, `Resolve-Path -LiteralPath`, XML parsing, invariant-culture numeric parsing, and these outcomes:

  - missing/unreadable/malformed XML: throw and exit non-zero;
  - missing or non-numeric root `line-rate`: throw and exit non-zero;
  - actual `< minimum`: write one concise error and exit `1`;
  - actual `>= minimum`: write one concise status line and exit `0`.

- [ ] **Step 4: Rerun script tests and observe GREEN.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --filter FullyQualifiedName~CoverageThresholdScriptTests
  ```

- [ ] **Step 5: Add failing workflow-wiring assertions.**

  Add `CiCoverage_CollectsThenEnforcesBeforeUpload` to `CiWorkflowTests`. Assert the CI text contains, in this order:

  1. `--settings TiaMcpServer.Tests/coverage.runsettings`
  2. `verify-coverage-threshold.ps1`
  3. `-MinimumLineRate 0.80`
  4. `codecov/codecov-action`

  Run it and observe RED before editing the workflow.

- [ ] **Step 6: Wire collection and enforcement into CI.**

  Add these steps after the serialized Release build and before Codecov:

  ```yaml
  - name: Run scoped coverage
    run: dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults

  - name: Enforce coverage threshold
    shell: pwsh
    run: |
      $reports = @(Get-ChildItem -Path TestResults -Recurse -Filter coverage.cobertura.xml)
      if ($reports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($reports.Count)." }
      ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
  ```

  Configure the existing Codecov step to upload that same report and keep it after the threshold step.

- [ ] **Step 7: Run the real local gate.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults
  $reports = @(Get-ChildItem -Path TestResults -Recurse -Filter coverage.cobertura.xml)
  if ($reports.Count -ne 1) { throw "Expected exactly one Cobertura report; found $($reports.Count)." }
  ./scripts/verify-coverage-threshold.ps1 -CoveragePath $reports[0].FullName -MinimumLineRate 0.80
  ```

  If the gate fails below 0.80, add behavior-focused tests for uncovered approved-scope production code. Do not change the threshold or exclusions.

- [ ] **Step 8: Run full verification, review, and commit.**

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  dotnet test TiaMcpServer.sln --no-restore --no-build --verbosity minimal
  git diff --check
  git add .github/workflows/ci.yml scripts/verify-coverage-threshold.ps1 TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs TiaMcpServer.Tests/Diagnostics/CoverageThresholdScriptTests.cs
  git commit -m "ci: enforce scoped coverage threshold"
  ```

## Plan 1 Exit Gate

- [ ] AC-001, AC-002, and AC-003 each have a named automated test.
- [ ] The real scoped report passes at `line-rate >= 0.80`.
- [ ] Codecov runs only after the local threshold succeeds.
- [ ] Full serial build and test suite pass.
- [ ] Worktree is clean before starting Plan 2.
