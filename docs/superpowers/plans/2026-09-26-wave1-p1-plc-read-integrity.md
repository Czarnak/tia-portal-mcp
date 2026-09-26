# Wave 1 P1 PLC Read-Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `compile_check` return bounded nested diagnostics with truthful PLC identity, and make
`read_cross_references` query documented source-object owners while distinguishing complete zero
results from incomplete or never-successful reads.

**Architecture:** Keep both existing worker methods and host envelopes. Put Siemens-specific access
in `CompileChecker` and `CrossReferenceReader`, isolate compiler-message traversal in a bounded
Siemens-free projection helper, and add explicit identity and completeness fields to the shared
contracts. Preserve selectors and compiler totals; do not reinterpret `PlcSoftwareLocator.DeviceName`.

**Tech Stack:** C#; net48 Openness worker; netstandard2.0 contracts; net10.0 xUnit tests; Siemens
TIA Portal V21 reference stubs and installed V21 references.

**Spec:** [Open Bug Parallel Pull-Request Delivery Design](../specs/2026-09-26-open-bug-parallel-pr-delivery-design.md)

**Future branch:** `fix/plc-read-integrity`, created in its own worktree from the then-current
`origin/main`; its pull request targets `main` directly.

## Global Constraints

- Do not implement this plan on `docs/open-bug-wave-planning`; first merge the docs-only PR, then
  create P1 directly from that updated `main`.
- Production allowlist:
  `TiaMcpServer.OpennessWorker/Openness/CompileChecker.cs`,
  `TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs`, create
  `TiaMcpServer.OpennessWorker/Openness/CompileReportProjection.cs`, and
  `TiaMcpServer.OpennessWorker/Openness/CrossReferenceReader.cs`.
- Contract allowlist: `TiaMcpServer.Contracts/PlcCompileInfo.cs`,
  `TiaMcpServer.Contracts/PlcCrossReferenceInfo.cs`, and
  `TiaMcpServer.Contracts/CrossReferenceReport.cs`.
- Test allowlist: create `TiaMcpServer.Tests/Block/CompileReportProjectionTests.cs`,
  `TiaMcpServer.Tests/Block/CompileCheckerTests.cs`, and
  `TiaMcpServer.Tests/Block/CrossReferenceReaderTests.cs`; modify
  `TiaMcpServer.Tests/Block/CompileCheckInfoTests.cs`,
  `TiaMcpServer.Tests/Block/CrossReferenceInfoTests.cs`,
  `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`, and
  `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`.
- Documentation allowlist: the compile and cross-reference sections of
  `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` only.
- Do not change `WorkerRequest`, worker dispatch, `OpennessWorkerClient`, FakeWorker, generic batch
  formatting/safety, `PlcSoftwareLocator`, `README.md`, or `docs/ARCHITECTURE.md`.
- L1 owns the shared worker-dispatch and IPC files in Wave 1. K1 owns operation-batch result files;
  N1 owns `NetworkDeviceCreator`. If P1 unexpectedly needs a reserved file, stop and replan.
- Run builds and tests serially. The worker-copy targets are unsafe under parallel MSBuild.
- A stub build proves only offline structure. Run a real-reference build before review, and keep
  runtime statements explicitly separated from compile/static evidence.
- Plan or PR approval does not authorize opening or compiling an exact TIA project. Obtain fresh
  authorization for the named project immediately before any live `compile_check` acceptance.

## Locked Public Semantics

- `plcName` means `PlcSoftware.Name`; additive `deviceName` means the containing hardware device.
  Input `plcName` continues accepting either identity through the existing locator.
- Compiler messages are emitted in deterministic parent-first order. Preserve Siemens' reported
  `ErrorCount`, `WarningCount`, and `State`; do not recompute them from projected rows.
- One report-wide projection budget is shared across all selected PLCs: maximum depth 16, maximum
  200 projected messages, maximum 1,024 characters for each description and path, and maximum
  32,000 aggregate description/path characters. An affected PLC gets one omission note.
- Cross-reference service owners are OB/FB/FC/DB blocks, PLC tags, PLC system constants, and PLC
  user data types, including supported nested groups and software units. Do not query
  `PlcSoftware`, tag tables, or user constants as service owners.
- A successful owner query returning no roots is a genuine zero. A selected set with no successful
  owner query fails with the existing categorized `worker_operation_failed` path.
- Some successful queries plus any skipped/failed/truncated owner or projection error returns the
  retained data with `IsComplete=false`. Incomplete `UnusedObjects` is not a complete unused audit.
- Do not add speculative cross-owner deduplication. Preserve source/reference hierarchy and record
  live overlap evidence before defining a future deduplication key.

## Review Focus

- Nested compiler leaves, their own severity and path, ordering, one omission marker, and the
  complete serialized-report bound.
- Software/device identity across PLC scope, block scope, selectors, successes, and failures.
- Cross-reference owner traversal across roots, nested groups, system groups, and software units.
- Genuine empty versus never queried, partial-coverage visibility, `maxResults`, and nested totals.
- No change to tool registration, access mode, safety tokens, audit, or generic batch envelopes.

---

### Task 1: Project bounded nested compiler diagnostics and correct PLC identity

**Files:**
- Create: `TiaMcpServer.OpennessWorker/Openness/CompileReportProjection.cs`
- Create: `TiaMcpServer.Tests/Block/CompileReportProjectionTests.cs`
- Create: `TiaMcpServer.Tests/Block/CompileCheckerTests.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/CompileChecker.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs`
- Modify: `TiaMcpServer.Contracts/PlcCompileInfo.cs`
- Modify: `TiaMcpServer.Tests/Block/CompileCheckInfoTests.cs`
- Modify: `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Add optional `DeviceName` to `PlcCompileInfo`; keep `PlcName` and existing fields compatible.
- Add a Siemens-free projection entry point equivalent to
  `Flatten<TMessage>(roots, readDescription, readPath, readSeverity, readChildren, budget)`.
  It returns projected message rows plus `WasTruncated`; `CompileChecker` remains the Siemens
  adapter and the only `PlcCompileInfo` constructor path.
- Add a `BlockTargetResolver` overload that resolves a block under an already selected
  `PlcSoftware`, avoiding a second identity lookup.

- [ ] **Step 1: Build an executable baseline around the current compiler adapter**

Source-link the current `CompileChecker` and `BlockTargetResolver` into the test project. Extend the
existing Siemens doubles with only the compiler service/result/message and block-tree behavior
needed to call `CompileChecker.Compile`. Add a characterization test showing one top-level message,
its path/severity, and Siemens-reported totals are preserved. This establishes that the harness
executes the production adapter before the new helper exists.

- [ ] **Step 2: Run the baseline GREEN**

```powershell
dotnet test .\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~CompileCheckerTests|FullyQualifiedName~CompileCheckInfoTests"
```

Expected: the current top-level behavior passes. Fix harness/setup failures before extracting code.

- [ ] **Step 3: Extract current top-level projection without changing behavior**

Create and source-link `CompileReportProjection`. Add a top-level parity test in
`CompileReportProjectionTests`, then make `CompileChecker` delegate its existing one-level mapping
through the helper. Run the Step 2 filter plus `FullyQualifiedName~CompileReportProjectionTests` and
require GREEN. This is an explicit behavior-preserving seam extraction, not the nested fix.

- [ ] **Step 4: Add and observe the nested-message behavioral RED**

Create a tree with a blank outer header, a nested undefined-tag error, and a deeper warning. Assert
parent-first output, leaf-local severity/path, and unchanged caller-supplied totals. Run:

```powershell
dotnet test .\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~CompileReportProjectionTests|FullyQualifiedName~CompileCheckerTests|FullyQualifiedName~CompileCheckInfoTests"
```

Expected RED: the helper exists and all tests compile, but nested rows are absent. A missing file,
type, or test double is a harness failure, not the accepted RED.

- [ ] **Step 5: Implement bounded recursive projection and its edge cases**

Traverse parent before children using each node's own description, reflective path, state, and
children. Sanitize reflection failures; do not return raw exception text or local paths. Apply the
locked depth, count, per-field, and aggregate-character limits through one report-wide budget.
Stop cleanly at a bound and signal `WasTruncated` without creating malformed JSON.

Cover depth 17, more than 200 wide children, overlong path/description values, escape-heavy text,
and multiple PLCs sharing one report budget. Assert one omission note for each affected PLC and a
serialized report below the standalone tool's 60,000-character item limit. Rerun the Step 4 command
to GREEN.

- [ ] **Step 6: Add an executable production-adapter identity RED**

In `CompileCheckerTests`, use software name `PLC_DP` inside device `Station_1` and invoke the real
linked `CompileChecker` for PLC success, PLC compile failure, and block scope. Assert
`plcName == "PLC_DP"`, `deviceName == "Station_1"`, selector compatibility for either input alias,
and that the block path compiles under the same selected software. Run the Step 4 command. Expected
RED: current production code emits the hardware device name as `PlcName`; projection tests remain
green.

- [ ] **Step 7: Correct identity through the tested production adapter**

Make `CompileChecker` set `PlcName = plc.Software.Name` and `DeviceName = plc.DeviceName` for PLC
success, PLC failure, qualified block, and first-PLC block paths. Select the PLC once, then resolve
its block through the new resolver overload. Keep the existing input-alias behavior.

- [ ] **Step 8: Rerun the focused compiler slice**

```powershell
dotnet test .\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers --filter "FullyQualifiedName~CompileReportProjectionTests|FullyQualifiedName~CompileCheckerTests|FullyQualifiedName~CompileCheckInfoTests"
```

Expected: nested, bounded, executable PLC/block identity, and compatibility cases pass.

- [ ] **Step 9: Review and commit the compiler half**

Confirm only Task 1 files changed, run `git diff --check`, inspect the scoped diff, then commit:

```powershell
git add TiaMcpServer.OpennessWorker/Openness/CompileChecker.cs TiaMcpServer.OpennessWorker/Openness/BlockTargetResolver.cs TiaMcpServer.OpennessWorker/Openness/CompileReportProjection.cs TiaMcpServer.Contracts/PlcCompileInfo.cs TiaMcpServer.Tests/Block/CompileReportProjectionTests.cs TiaMcpServer.Tests/Block/CompileCheckerTests.cs TiaMcpServer.Tests/Block/CompileCheckInfoTests.cs TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "fix: surface nested compile diagnostics and PLC software identity"
```

---

### Task 2: Read cross references from documented source-object owners

**Files:**
- Create: `TiaMcpServer.Tests/Block/CrossReferenceReaderTests.cs`
- Modify: `TiaMcpServer.OpennessWorker/Openness/CrossReferenceReader.cs`
- Modify: `TiaMcpServer.Contracts/PlcCrossReferenceInfo.cs`
- Modify: `TiaMcpServer.Contracts/CrossReferenceReport.cs`
- Modify: `TiaMcpServer.Tests/Block/CrossReferenceInfoTests.cs`
- Modify: `TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Add per-PLC `DeviceName`, `OwnerQueryCount`, `SuccessfulOwnerQueryCount`, and `IsComplete`.
- Add aggregate `IsComplete` to `CrossReferenceReport`.
- Preserve selector inputs, source/reference/location hierarchy, filters, `maxResults`, and totals.

- [ ] **Step 1: Extend the Siemens-free test harness**

Source-link `CrossReferenceReader` and add minimal doubles for service owners/results, nested groups,
software units, and `SystemConstants`. Keep setup narrowly scoped; do not broaden production stubs.

- [ ] **Step 2: Add owner-selection and traversal REDs**

Create one root, nested, system-group, and software-unit case covering supported blocks, a tag, a
system constant, and a UDT. Include a tag table, user constant, and `PlcSoftware` sentinel that must
not be queried. Assert selected source names and owner-query counts.

- [ ] **Step 3: Run the focused cross-reference slice and capture RED**

```powershell
dotnet test .\TiaMcpServer.Tests\TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~CrossReferenceReaderTests|FullyQualifiedName~CrossReferenceInfoTests"
```

Expected: supported-owner assertions fail because current code asks only the `PlcSoftware` object.

- [ ] **Step 4: Implement supported-owner discovery and querying**

Enumerate supported block/type/tag groups and software units, query each supported source object,
and retain existing recursive result projection. Count every attempted owner and every successful
query. Treat a successful empty service response as success. Capture bounded, sanitized per-PLC
messages and one concise stderr warning for partial coverage.

- [ ] **Step 5: Add zero, failure, and partial-coverage REDs**

Cover complete empty success; no owners/all services unavailable/all queries throwing; mixed
success and failure; projection skip; and `maxResults` truncation. Assert that no successful query
throws `worker_operation_failed`, while mixed/truncated cases retain data and report
`IsComplete=false` at PLC and report levels.

- [ ] **Step 6: Add cross-reference identity and contract tests**

With device `Station_1` and software `PLC_DP`, exercise both selector aliases and assert distinct
output identities. Verify additive JSON round trips, nested totals, complete-zero, partial values,
and conservative defaults when old JSON omits the new fields.

- [ ] **Step 7: Rerun the focused cross-reference slice**

Run the Step 3 command. Expected: owner, outcome, identity, and contract cases pass.

- [ ] **Step 8: Review and commit the cross-reference half**

Confirm only Task 2 files changed, run `git diff --check`, inspect the scoped diff, then commit:

```powershell
git add TiaMcpServer.OpennessWorker/Openness/CrossReferenceReader.cs TiaMcpServer.Contracts/PlcCrossReferenceInfo.cs TiaMcpServer.Contracts/CrossReferenceReport.cs TiaMcpServer.Tests/Block/CrossReferenceReaderTests.cs TiaMcpServer.Tests/Block/CrossReferenceInfoTests.cs TiaMcpServer.Tests/TestUtilities/TagSafetySiemensDoubles.cs TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "fix: read PLC cross references from supported source objects"
```

---

### Task 3: Document the corrected PLC read contracts and run P1 gates

**Files:**
- Modify: `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`

- [ ] **Step 1: Update maintained operation documentation**

Document nested compiler-message order and bounds, preserved Siemens totals, software/device
identity, cross-reference owner coverage, complete-zero versus partial/failure semantics, and why
incomplete `UnusedObjects` is not an authoritative unused-object audit.

- [ ] **Step 2: Run the complete offline and reference gates**

Run serially:

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
$p1PackageDir = Join-Path 'artifacts' ('p1-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $p1PackageDir | Out-Null
dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 /p:UseTiaPortalReferenceStubs=true -o $p1PackageDir
$p1Packages = @(Get-ChildItem -LiteralPath $p1PackageDir -Filter '*.nupkg')
if ($p1Packages.Count -ne 1) { throw "Expected exactly one P1 package, found $($p1Packages.Count)." }
pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath $p1Packages[0].FullName
git diff --check
```

If the installed V21 directory is unavailable, record that limitation; do not substitute the stub
build as Siemens API proof.

- [ ] **Step 3: Review and commit the maintained documentation**

Review the whole branch against the allowlist and spec, then commit:

```powershell
git add docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md
git commit -m "docs: describe PLC diagnostic and cross-reference integrity"
```

## Separately Authorized Live Acceptance

Use an explicitly identified disposable V21 project with different hardware-device and PLC-software
names and a block already known to contain a nested compiler error. With fresh authorization:

1. Capture project identity and status before/after `compile_check`; verify nested message, severity,
   path, omission behavior where applicable, and both identities. Compilation may change in-memory
   TIA state and is not a harmless read.
2. In a separately bounded read-only check, compare known block/tag/type references and an actual
   empty case with TIA's cross-reference view; exercise both PLC selector forms and inspect
   completeness fields. Do not compile, save, download, or control a PLC during this read-only step.
3. Record any cross-owner overlap before proposing deduplication. Preserve raw exact paths/selectors
   only in ignored local evidence.

No live operation is authorized by this plan.

## Pull-Request and Integration Gate

- PR title: `fix(plc): make compile and cross-reference reads truthful`.
- PR body initially uses `Refs #71`, `Refs #73`, and `Refs #82`; enumerate the exact #82.2 slice
  and evidence boundary. Change #71/#73 to `Closes` only if the separately authorized runtime gate
  for the reviewed commit completed and the required evidence was accepted before the PR merges.
- Base branch: `main`; never base P1 on another Wave 1 feature branch.
- Obtain independent review of both commits, contracts, test harness, and maintained documentation.
- Before merge, refresh from current `main` and rerun focused, full offline, and real-reference gates.
- Merge P1 independently; after merge, run the current-main integration gate before starting a
  dependent later-wave PLC repair.
