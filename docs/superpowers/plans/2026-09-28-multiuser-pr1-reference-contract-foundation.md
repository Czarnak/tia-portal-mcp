# Multiuser PR 1 Reference and Contract Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the opaque tracked V21 reference-assembly baseline with reproducible, reviewable,
minimal compile stubs and add the Siemens-free Multiuser identity, capability, and connection
contracts needed by later pull requests, without exposing or enabling a public operation.

**Architecture:** Keep Siemens API references confined to the net48 worker and a compile-only probe.
Generate deterministic `Siemens.Engineering.Base.dll` and `Siemens.Engineering.Step7.dll` reference
artifacts from repository-owned C# sources, preserve the V21 assembly identities needed for runtime
binding, and fail CI when the tracked artifacts drift from those sources. Add passive netstandard2.0
contracts and closed string vocabularies, but do not attach them to the live worker binding until PR 2.

**Tech Stack:** C#; .NET SDK 10.0.400; net48 reference-stub and probe projects; netstandard2.0
contracts; System.Text.Json; xUnit; PowerShell 7; MSBuild; GitHub Actions.

**Spec:** [Multiuser Engineering design](../specs/2026-09-28-multiuser-engineering-design.md)

## Global Constraints

- This plan covers PR 1 only. Do not start `ActiveProjectContext`, `.als21` opening, inventory,
  lifecycle, lock, marking, save, discard, commit, or any other later-PR implementation.
- Add no MCP tool, worker method, operation-catalog entry, advertised capability, or public schema.
- Do not change preview/apply safety, safety tokens, canonicalization, audit, leases, or confirmation.
- Keep `TiaMcpServer` and `TiaMcpServer.Contracts` free of Siemens assembly references. Only the
  net48 worker and the compile-only probe may reference `Siemens.Engineering.*`.
- Do not extend `WorkerSessionIdentity`, `ProjectSessionBinding`, or binding comparison semantics in
  this PR. PR 2 owns that integration after the passive contracts are reviewed.
- The generated stubs are compile evidence only. They must never be copied into packages, loaded at
  runtime, or presented as live TIA evidence.
- Preserve assembly simple names, version `21.0.0.0`, and public-key token
  `29bfe5fdf4ba5d3b`. The checked-in key material must contain only the public key; no Siemens
  private key, implementation binary, or other proprietary payload may enter the repository.
- Stub source may declare only the surface needed by the current worker or the PR 1 probe. Do not
  reproduce the full V21 API. A later operation extends the stub surface in its designated PR and
  receives that PR's real-reference and live verification.
- Serialize builds with `-m:1`. Disable xUnit collection parallelism for the full suite because the
  repository's worker-build fixtures share outputs.
- Use exact, validated paths in the PowerShell verifier. Its default verification mode is read-only;
  artifact replacement requires the explicit `-Update` switch and may write only the two known
  files under `ref/`.
- Do not commit, push, open a PR, or run a live TIA operation unless separately authorized for that
  action. This plan does not itself authorize implementation.

## Revalidated Baseline

- `ref/Siemens.Engineering.Base.dll` and `ref/Siemens.Engineering.Step7.dll` are strong-named V21
  reference assemblies, not a repository-built minimal stub set.
- The Base reference contains 1,828 type definitions and already exposes `ProjectBase`,
  `MultiuserProject`, `LocalSession`, `LocalSessionComposition`, `LocalSessionInfo`, `ProjectServer`,
  and `ProjectServerComposition`.
- The current worker binary references 157 Siemens types but no Multiuser type because no Multiuser
  production path exists yet.
- The source definition for the tracked reference assemblies is absent. PR 1 therefore replaces an
  opaque broad baseline; it does not claim that the V21 type family was missing.
- `TiaMcpServer.OpennessWorker.csproj` currently selects `ref/` when
  `UseTiaPortalReferenceStubs=true`, selects `TiaPortalV21Dir` otherwise, and marks both Siemens
  references `Private=False`.
- CI restores once, builds Release with `-m:1` and stubs, runs coverage with
  `xUnit.ParallelizeTestCollections=false`, and enforces 80% line coverage.

## Locked PR 1 Contract

Add the following passive contract types under `TiaMcpServer.Contracts`. They are not populated or
wired into any response in PR 1.

| Type | Members or exact values |
| --- | --- |
| `ProjectContainerKinds` | `standaloneProject`, `localSession`, `serverProject`; ordered `All` |
| `MultiuserSessionModes` | `multiuser`, `exclusive`, `unknown`, `notApplicable`; ordered `All` |
| `ProjectCapabilityApplicabilities` | `projectContent`, `standaloneOnly`, `localSessionConditional`, `serverProjectOnly`, `notYetDelivered`, `unsupported`; ordered `All` |
| `ProjectCapabilityInfo` | `Operation`, `Applicability` |
| `ProjectServerGroupIdentity` | `IsRoot`, nullable `Name`; root is `{ isRoot: true, name: null }`, not an invented string sentinel |
| `MultiuserRemoteIdentity` | `ServerAlias`, nullable `Host`, `Port`, `Protocol`, `Group`, `ServerProjectName`, `LocalSessionId`, and `LocalSessionPath` |
| `ProjectServerConnectionStates` | `connected`, `unavailable`, `unknown`; ordered `All` |
| `ProjectServerConnectionObservationSources` | `sessionOpen`, `explicitRead`, `operationPreflight`, `postFailure`; ordered `All` |
| `ProjectServerConnectionObservation` | `State`, `ObservedAt`, `ObservationSource`, nullable `PreviousState`, and Boolean `Transition` |

Use mutable sealed POCOs to match the existing worker-contract style. Use `DateTimeOffset` for
`ObservedAt`, `int?` for the V21 `LocalSessionInfo.SessionId`, and `int?` for the server port. New
payload contracts remain unmarked, so `WorkerJson.SerializePayload` writes every member including
null. Later services validate combinations; PR 1 does not add a parallel validation framework.

## Locked Compile Probe

The net48 probe is compile-only and never executes Siemens code. It must statically prove both the
generated stubs and installed V21 references support this foundation:

- `Project` and `MultiuserProject` are assignable to `ProjectBase`;
- `TiaPortal.LocalSessions` and `TiaPortal.ProjectServers` exist;
- `LocalSessionComposition.Open(FileInfo)` and `OpenServerProject(FileInfo)` return `LocalSession`;
- `LocalSession.Project` returns `MultiuserProject`;
- `LocalSession.MarkingService`, `Save()`, `Close()`, `CloseAndCommit(string)`, and
  `IsUptoDate()` exist;
- `LocalSessionInfo.ProjectFileInfo` exists and `SessionId` is `int`; and
- `ProjectServer` and `ProjectServerComposition` are available for later inventory work.

Do not probe later mutation signatures such as connection creation, adding a project, session
creation/deletion, markings, locks, or commit preconditions. Those belong to the PR that adds the
operation and its mandatory live verification.

## Review Focus

1. **Assembly identity:** An unsigned or differently versioned stub can compile but leave the
   worker referencing an identity that will not bind to installed Siemens V21 assemblies.
2. **Surface fidelity:** The minimal stubs must compile the current worker and locked probe without
   inventing extra API or copying the full reference surface.
3. **Deterministic drift gate:** CI must rebuild the sources, recompute their canonical source hash,
   and prove each tracked artifact embeds that hash. Do not require whole-file byte equality across
   compiler patch versions.
4. **Contract truthfulness:** Closed values, null semantics, `LocalSessionInfo.SessionId` type, and
   connection uncertainty must match the approved design and worker JSON policy.
5. **Scope containment:** PR 1 must not change tool discovery, session binding, safety, worker
   dispatch, runtime behavior, or claimed live support.
6. **Packaging:** Neither generated stubs nor installed Siemens assemblies may enter NuGet or the
   packaged worker directory.

---

## Execution Preflight

- [ ] Confirm `git status --short --branch` names the intended PR 1 branch and is clean. Preserve
  unrelated work; stop if the implementation allowlist overlaps user changes.
- [ ] Run `dotnet restore TiaMcpServer.sln` and the current serialized stub build before editing.
- [ ] Confirm the installed real-reference directory contains both V21 assemblies. If it does not,
  the branch may be explored but cannot be called PR-ready.

## Task 1: Lock the V21 Multiuser Foundation with a Compile-Only Probe

**Files:**

- Create: `reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj`
- Create: `reference-stubs/TiaMcpServer.OpennessReferenceProbe/MultiuserReferenceSurface.cs`
- Create: `TiaMcpServer.Tests/Diagnostics/OpennessReferenceProbeContractTests.cs`

- [ ] **Step 1: Write the failing source-contract test.**

  Add `OpennessReferenceProbeContractTests` with:

  - `ProbeIsCompileOnlyAndReferencesExactlyBaseAndStep7` — require `OutputType=Library`, `net48`,
    both `Private=False` references, and no package or product-project reference;
  - `ProbeLocksOnlyTheFoundationMultiuserSurface` — require the types and members listed in
    **Locked Compile Probe**, and reject calls to `Create`, `Delete`, `AddProjectToServer`,
    `CreateLocalSession`, marking mutation, or lock mutation APIs.

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~OpennessReferenceProbeContractTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: FAIL because the probe project and source do not exist.

- [ ] **Step 3: Add the smallest compile-only probe.**

  Mirror the worker's `UseTiaPortalReferenceStubs` and `TiaPortalV21Dir` selection in the probe
  project. Use typed assignments and delegates in `MultiuserReferenceSurface.cs`; do not call them
  and do not add an entry point. Keep the file free of host or contract dependencies.

- [ ] **Step 4: Run the source-contract test and stub-reference compile.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~OpennessReferenceProbeContractTests" -- xUnit.ParallelizeTestCollections=false
  dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS. This confirms the current tracked references expose the locked foundation; it
  does not prove they are reproducible or that any runtime operation works.

- [ ] **Step 5: Compile the same probe against installed V21 references.**

  ```powershell
  dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  ```

  Expected: PASS. If the installed V21 path is absent or a signature differs, stop; do not weaken
  the probe or substitute the tracked reference assembly as real-reference evidence.

- [ ] **Step 6: Commit the probe after reviewing its scope.**

  ```powershell
  git add -- reference-stubs/TiaMcpServer.OpennessReferenceProbe TiaMcpServer.Tests/Diagnostics/OpennessReferenceProbeContractTests.cs
  git diff --cached --check
  git commit -m "test: lock multiuser reference foundation"
  ```

## Task 2: Author the Minimal Source-Owned Reference Surface

**Files:**

- Create: `reference-stubs/Siemens.Engineering.PublicKey.snk`
- Create: `reference-stubs/Directory.Build.props`
- Create: `reference-stubs/Siemens.Engineering.Base/Siemens.Engineering.Base.csproj`
- Create: `reference-stubs/Siemens.Engineering.Base/AssemblyInfo.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/Core.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/Compiler.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/CrossReference.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/Hardware.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/HardwareConnections.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/Online.cs`
- Create: `reference-stubs/Siemens.Engineering.Base/Multiuser.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/Siemens.Engineering.Step7.csproj`
- Create: `reference-stubs/Siemens.Engineering.Step7/AssemblyInfo.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/Software.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/Blocks.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/ExternalSources.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/Tags.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/Types.cs`
- Create: `reference-stubs/Siemens.Engineering.Step7/Units.cs`
- Create: `reference-stubs/TiaMcpServer.ReferenceStubs.sln`
- Create: `reference-stubs/README.md`
- Create: `TiaMcpServer.Tests/Diagnostics/ReferenceStubSourceContractTests.cs`

- [ ] **Step 1: Write failing source and identity contract tests.**

  Add tests that require:

  - the two source projects, the public-only key, the probe, and their membership in the dedicated
    reference-stub solution;
  - `AssemblyName` values `Siemens.Engineering.Base` and `Siemens.Engineering.Step7`;
  - `AssemblyVersion` and `FileVersion` `21.0.0.0`;
  - deterministic Release builds, path mapping, no PDB in the tracked artifact, and delayed signing
    with the checked-in public-only key;
  - a repository-origin metadata marker on generated artifacts;
  - Base source ownership of core, hardware, and Multiuser namespaces and Step7 source ownership of
    software namespaces; and
  - no reference from either stub project to product projects or installed Siemens files.

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ReferenceStubSourceContractTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: FAIL because the source-owned stub tree is absent.

- [ ] **Step 3: Add deterministic source projects and the public-only signing key.**

  Target `net48`, enable nullable and latest language version, set deterministic and path-map
  properties, disable debug artifacts for Release, produce reference assemblies, and delay-sign
  with only the public key corresponding to token `29bfe5fdf4ba5d3b`. Add
  `AssemblyMetadata("StubOrigin", "tia-portal-mcp")` and
  `AssemblyMetadata("StubSourceHash", "$(StubSourceHash)")`; direct developer builds may use the
  explicit sentinel `unverified`, while the verifier always supplies the canonical hash. Document
  that the key establishes assembly identity only and cannot sign Siemens implementation code.

- [ ] **Step 4: Implement only the current worker's compiler-required surface.**

  Start from the namespaces and types referenced by the current worker, grouped in the files above.
  Use empty interfaces, enums with only referenced values, abstract or sealed placeholder classes,
  and members whose bodies throw `NotSupportedException`. Do not copy private/internal factory
  facades or unreferenced V21 members. Build against the generated reference output after each
  compiler-error batch and add only the missing signature.

  The generated types must preserve the inheritance and interface relationships on which current
  worker source relies, including `Project : ProjectBase`, compiler services, engineering objects,
  hardware compositions, Step7 software/group compositions, and the exact enum member names used
  in switches.

- [ ] **Step 5: Add only the locked Multiuser foundation.**

  In `Multiuser.cs`, add the exact types and members from **Locked Compile Probe**. Do not add later
  operation methods merely because the opaque baseline exposes them. `LocalSessionInfo.SessionId`
  is `int`; `MultiuserProject` derives from `ProjectBase`.

- [ ] **Step 6: Build the source stubs and compile current worker plus probe against them.**

  ```powershell
  dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln
  dotnet build reference-stubs/TiaMcpServer.ReferenceStubs.sln -m:1 --no-restore --configuration Release
  $stubOutput = Join-Path (Resolve-Path 'reference-stubs').Path 'artifacts\Release'
  New-Item -ItemType Directory -Force -Path $stubOutput | Out-Null
  Copy-Item -LiteralPath 'reference-stubs/Siemens.Engineering.Base/obj/Release/net48/ref/Siemens.Engineering.Base.dll' -Destination $stubOutput
  Copy-Item -LiteralPath 'reference-stubs/Siemens.Engineering.Step7/obj/Release/net48/ref/Siemens.Engineering.Step7.dll' -Destination $stubOutput
  dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true /p:TiaOpennessReferenceDir="$stubOutput"
  dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true /p:TiaOpennessReferenceDir="$stubOutput"
  ```

  Expected: all builds PASS. The generated Base surface should be dramatically smaller than the
  1,828-type opaque baseline. Record the generated public type counts in the PR evidence.

- [ ] **Step 7: Run the source-contract tests and commit.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ReferenceStubSourceContractTests|FullyQualifiedName~OpennessReferenceProbeContractTests" -- xUnit.ParallelizeTestCollections=false
  git add -- reference-stubs TiaMcpServer.Tests/Diagnostics/ReferenceStubSourceContractTests.cs
  git diff --cached --check
  git commit -m "build: add source-owned openness references"
  ```

## Task 3: Generate, Verify, and Replace the Tracked Reference Artifacts

**Files:**

- Create: `scripts/verify-reference-stubs.ps1`
- Create: `TiaMcpServer.Tests/Diagnostics/ReferenceStubVerificationScriptTests.cs`
- Create: `TiaMcpServer.Tests/Diagnostics/ReferenceStubArtifactTests.cs`
- Modify: `ref/Siemens.Engineering.Base.dll`
- Modify: `ref/Siemens.Engineering.Step7.dll`

- [ ] **Step 1: Write failing verifier behavior tests.**

  Follow `CoverageThresholdScriptTests` and `DoctorPackageVerificationScriptTests`. Exercise the
  verifier only in temporary directories and require:

  - matching exact artifacts pass;
  - a stale or mismatched embedded source hash fails with the exact filename and does not update
    either tracked file;
  - a missing or extra artifact fails closed;
  - `-Update` replaces only the two exact target filenames; and
  - a supplied directory outside the resolved temporary or repository target boundary is rejected.

  Add artifact metadata tests using `System.Reflection.Metadata`/`PEReader`; do not load reference
  assemblies for execution. Assert simple names, version, public-key token, repository-origin
  marker, public Multiuser foundation types, and the required inheritance/member names.

- [ ] **Step 2: Run the focused tests and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ReferenceStubVerificationScriptTests|FullyQualifiedName~ReferenceStubArtifactTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: FAIL because the verifier is absent and the tracked binaries lack the repository-origin
  marker.

- [ ] **Step 3: Implement the exact-target verifier.**

  The default path must calculate one canonical SHA-256 over the ordered relative path and bytes of
  both stub projects, their C# sources, shared build properties, and the public-only key. It then
  restores/builds the source stub solution serially into an isolated temporary output while passing
  that value as `StubSourceHash`, stages the two generated references in one exact temporary
  directory, and compiles the locked probe against that directory. It returns nonzero unless both
  generated and tracked artifacts embed the current hash and the required identity/origin metadata.
  Whole-file hashes are diagnostic only because compiler patch versions may produce different but
  equivalent PE bytes. Support:

  - `-Configuration Release`;
  - `-NoRestore` for CI after the dedicated reference-stub solution restore;
  - `-GeneratedReferenceDirectory` for behavior tests only; and
  - explicit `-Update` to copy the two generated files into `ref/`.

  Resolve every path before comparing or copying. Use `-LiteralPath`. Validate any recursive temp
  cleanup remains under the exact temp directory created by the script. Never enumerate one shell
  and delete through another.

- [ ] **Step 4: Generate and replace the tracked artifacts once.**

  ```powershell
  pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -Update
  pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release
  ```

  Expected: the update reports exactly two replacements and the canonical source hash; the second
  command reports both artifacts current. `git status --short` shows only those two binary
  replacements plus the new verifier/test files from this task.

- [ ] **Step 5: Rebuild the worker and probe against the tracked generated artifacts.**

  ```powershell
  dotnet build TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
  dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 --configuration Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS. A compiler error is a missing minimal signature; add only that signature to the
  source and regenerate. Do not restore an opaque V21 type just to make compilation easy.

- [ ] **Step 6: Run focused tests and package leakage regression.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~ReferenceStubVerificationScriptTests|FullyQualifiedName~ReferenceStubArtifactTests|FullyQualifiedName~DoctorPackageVerificationScriptTests|FullyQualifiedName~OpennessWorkerCheckTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: PASS, including the existing `Siemens.Engineering*` package exclusion checks.

- [ ] **Step 7: Commit the generated artifacts and verifier together.**

  ```powershell
  git add -- scripts/verify-reference-stubs.ps1 ref/Siemens.Engineering.Base.dll ref/Siemens.Engineering.Step7.dll TiaMcpServer.Tests/Diagnostics/ReferenceStubVerificationScriptTests.cs TiaMcpServer.Tests/Diagnostics/ReferenceStubArtifactTests.cs
  git diff --cached --check
  git commit -m "build: generate openness reference stubs"
  ```

## Task 4: Make Reference Drift a CI Failure

**Files:**

- Modify: `.github/workflows/ci.yml`
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`

- [ ] **Step 1: Add a failing workflow-order test.**

  Add `ReferenceStubVerificationRunsAfterRestoreAndBeforeBuild`. Require a restore of
  `reference-stubs/TiaMcpServer.ReferenceStubs.sln`, then one invocation of
  `scripts/verify-reference-stubs.ps1 -NoRestore`, and assert both occur after the main restore and
  before the Release solution build. Retain the existing serialized build and coverage assertions.

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CiWorkflowTests.ReferenceStubVerificationRunsAfterRestoreAndBeforeBuild" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: FAIL because CI does not invoke the verifier.

- [ ] **Step 3: Add the CI verification step.**

  After the main restore and before build, add these steps:

  ```powershell
  dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln
  pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
  ```

  Do not pass `-Update` in CI. Drift must fail and show the exact stale artifact.

- [ ] **Step 4: Run workflow and verifier tests.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~ReferenceStubVerificationScriptTests" -- xUnit.ParallelizeTestCollections=false
  dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln
  pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
  ```

  Expected: PASS.

- [ ] **Step 5: Commit the CI gate.**

  ```powershell
  git add -- .github/workflows/ci.yml TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs
  git diff --cached --check
  git commit -m "ci: verify openness reference drift"
  ```

## Task 5: Add the Siemens-Free Multiuser Foundation Contracts

**Files:**

- Create: `TiaMcpServer.Contracts/ProjectContainerKinds.cs`
- Create: `TiaMcpServer.Contracts/MultiuserSessionModes.cs`
- Create: `TiaMcpServer.Contracts/ProjectCapabilityApplicabilities.cs`
- Create: `TiaMcpServer.Contracts/ProjectCapabilityInfo.cs`
- Create: `TiaMcpServer.Contracts/ProjectServerGroupIdentity.cs`
- Create: `TiaMcpServer.Contracts/MultiuserRemoteIdentity.cs`
- Create: `TiaMcpServer.Contracts/ProjectServerConnectionStates.cs`
- Create: `TiaMcpServer.Contracts/ProjectServerConnectionObservationSources.cs`
- Create: `TiaMcpServer.Contracts/ProjectServerConnectionObservation.cs`
- Create: `TiaMcpServer.Tests/Multiuser/MultiuserFoundationContractTests.cs`

- [ ] **Step 1: Write exact vocabulary and serialization tests.**

  Add tests that:

  - assert every ordered `All` collection exactly matches **Locked PR 1 Contract**, without case
    aliases or duplicates;
  - assert `WorkerJson.SerializePayload` uses camelCase and explicit nulls for remote identity,
    group identity, and connection observation;
  - assert a root group serializes as `isRoot=true,name=null` and does not invent a root name;
  - assert `LocalSessionId` and `Port` serialize as JSON numbers when present;
  - assert an initial unknown observation has `previousState=null` and `transition=false`; and
  - assert the contracts assembly has no reference whose name starts with `Siemens.Engineering`.

- [ ] **Step 2: Run the focused test and observe RED.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~MultiuserFoundationContractTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: build/test FAIL because the contract types do not exist.

- [ ] **Step 3: Implement the exact passive contracts.**

  Use sealed POCOs and static string-vocabulary classes. Add XML comments that capabilities are
  descriptive, not authorization, and that `unknown` must never be treated as healthy. Do not add
  `[LegacyNullOmission]`, validators, worker dispatch, or host integration.

- [ ] **Step 4: Run contract and null-policy regressions.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-restore --filter "FullyQualifiedName~MultiuserFoundationContractTests|FullyQualifiedName~WorkerJsonTests|FullyQualifiedName~WorkerPayloadNullPolicyRegisterTests" -- xUnit.ParallelizeTestCollections=false
  ```

  Expected: PASS. The legacy-null register remains unchanged because every new contract writes
  nulls explicitly.

- [ ] **Step 5: Confirm no premature binding or public-surface integration.**

  ```powershell
  rg -n "ProjectContainerKinds|MultiuserSessionModes|MultiuserRemoteIdentity|ProjectServerConnectionObservation" TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker
  ```

  Expected: no production matches. Contract and test matches are expected outside those paths.

- [ ] **Step 6: Commit the contract foundation.**

  ```powershell
  git add -- TiaMcpServer.Contracts/ProjectContainerKinds.cs TiaMcpServer.Contracts/MultiuserSessionModes.cs TiaMcpServer.Contracts/ProjectCapabilityApplicabilities.cs TiaMcpServer.Contracts/ProjectCapabilityInfo.cs TiaMcpServer.Contracts/ProjectServerGroupIdentity.cs TiaMcpServer.Contracts/MultiuserRemoteIdentity.cs TiaMcpServer.Contracts/ProjectServerConnectionStates.cs TiaMcpServer.Contracts/ProjectServerConnectionObservationSources.cs TiaMcpServer.Contracts/ProjectServerConnectionObservation.cs TiaMcpServer.Tests/Multiuser/MultiuserFoundationContractTests.cs
  git diff --cached --check
  git commit -m "feat(contracts): add multiuser foundation types"
  ```

## Task 6: Document the Build Boundary and Run the PR 1 Gates

**Files:**

- Modify: `CONTRIBUTING.md`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/development/building.md`
- Modify: `docs/guides/installation.md`
- Modify: `docs/IMPROVEMENT_LOG.md`

- [ ] **Step 1: Update maintained documentation.**

  Document:

  - source location, normal verification command, and explicit artifact-update command;
  - compile-only status and the difference between stub, real-reference, and live TIA evidence;
  - preserved strong-name identity and public-only key;
  - runtime/package exclusion of all `Siemens.Engineering*` binaries;
  - the passive contract foundation and explicit deferral of binding integration to PR 2; and
  - the corrected baseline that the former tracked references already contained Multiuser types but
    were opaque and much broader than necessary.

  Keep user procedure in the building/installation guides. Add only a concise compile-time boundary
  paragraph to `ARCHITECTURE.md`. Record the completed foundation and remaining PR 2 dependency in
  `IMPROVEMENT_LOG.md`.

- [ ] **Step 2: Restore and verify generated-reference drift.**

  ```powershell
  dotnet restore TiaMcpServer.sln
  dotnet restore reference-stubs/TiaMcpServer.ReferenceStubs.sln
  pwsh -NoProfile -File scripts/verify-reference-stubs.ps1 -Configuration Release -NoRestore
  ```

  Expected: PASS with no artifact drift.

- [ ] **Step 3: Run the authoritative stub build.**

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=true
  ```

  Expected: PASS and log `UseTiaPortalReferenceStubs=true` for the worker and probe.

- [ ] **Step 4: Run focused tests, then the full serialized suite and coverage gate.**

  ```powershell
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~OpennessReferenceProbeContractTests|FullyQualifiedName~ReferenceStubSourceContractTests|FullyQualifiedName~ReferenceStubVerificationScriptTests|FullyQualifiedName~ReferenceStubArtifactTests|FullyQualifiedName~MultiuserFoundationContractTests|FullyQualifiedName~WorkerJsonTests|FullyQualifiedName~WorkerPayloadNullPolicyRegisterTests|FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~DoctorPackageVerificationScriptTests|FullyQualifiedName~OpennessWorkerCheckTests" -- xUnit.ParallelizeTestCollections=false
  dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build --configuration Release --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults -- xUnit.ParallelizeTestCollections=false
  $coverageReports = @(Get-ChildItem -Path TestResults -Recurse -Filter coverage.cobertura.xml)
  if ($coverageReports.Count -ne 1) { throw "Expected exactly one coverage report; found $($coverageReports.Count)." }
  pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1 -CoveragePath $coverageReports[0].FullName -MinimumLineRate 0.80
  ```

  Expected: all tests PASS and line coverage is at least 80%. Do not re-enable collection
  parallelism to work around timeouts.

- [ ] **Step 5: Run the real-reference gates.**

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  dotnet build reference-stubs/TiaMcpServer.OpennessReferenceProbe/TiaMcpServer.OpennessReferenceProbe.csproj -m:1 --no-restore --configuration Release /p:UseTiaPortalReferenceStubs=false /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
  ```

  Expected: PASS against installed V21. This is signature compatibility only, not a live TIA test.
  If unavailable or failing, PR 1 is not merge-ready.

- [ ] **Step 6: Pack and prove Siemens binaries remain excluded.**

  ```powershell
  dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 --no-restore /p:UseTiaPortalReferenceStubs=true -o artifacts/multiuser-pr1
  $packages = @(Get-ChildItem -LiteralPath 'artifacts/multiuser-pr1' -Filter '*.nupkg')
  if ($packages.Count -ne 1) { throw "Expected exactly one package; found $($packages.Count)." }
  pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath $packages[0].FullName
  ```

  Expected: PASS; no `Siemens.Engineering*.dll` exists in the package or staged worker payload.

- [ ] **Step 7: Review scope and documentation integrity.**

  ```powershell
  git diff --check
  git status --short
  git diff --name-only
  rg -n "McpServerToolType|WorkerRequest|case \"|multiuser_read|multiuser_write|open_project.*als21" TiaMcpServer TiaMcpServer.OpennessWorker TiaMcpServer.FakeWorker
  ```

  Expected: only the PR 1 allowlist is changed, and the final search shows no newly registered tool,
  dispatch case, `.als21` lifecycle path, or mutation. Inspect the complete binary and text diff;
  confirm the generated artifacts are reproducible from the reviewed source.

- [ ] **Step 8: Commit documentation after all gates pass.**

  ```powershell
  git add -- CONTRIBUTING.md docs/ARCHITECTURE.md docs/development/building.md docs/guides/installation.md docs/IMPROVEMENT_LOG.md
  git diff --cached --check
  git commit -m "docs: explain openness reference generation"
  ```

- [ ] **Step 9: Seek independent review before proposing the PR.**

  Review against the six **Review Focus** risks. The PR description must state:

  - no public operation or runtime Multiuser behavior was added;
  - stub, contract, full-suite, coverage, package, and real-reference evidence;
  - no live TIA acceptance was required because PR 1 adds no operation;
  - stubs remain non-runtime compile aids; and
  - PR 2 must be planned from merged `main` only after this evidence and review are accepted.

## Stop Conditions

- Stop if the public-only key cannot reproduce token `29bfe5fdf4ba5d3b`; do not ship unsigned or
  differently identified references.
- Stop if a clean build cannot embed and reproduce the same canonical source hash across directories;
  fix input ordering/path handling rather than weakening the source-to-artifact check.
- Stop if the current worker or locked probe requires an uncertain signature; verify it against the
  installed V21 reference and Siemens documentation before adding it.
- Stop if any generated or installed Siemens assembly appears in package output.
- Stop if contract work requires changing session binding or public results; move that work to PR 2.
- Stop if real-reference compilation is unavailable. Stub success is not a substitute.
- Do not open TIA Portal or a Project Server for PR 1 verification. If any runtime behavior is added
  despite this boundary, the plan is invalid and must be revised before implementation continues.

## Completion Evidence

PR 1 is ready for review only when the branch contains the commit sequence above (or equivalently
small, reviewable commits), all Task 6 gates pass, the generated artifacts are clean on a second
verification run, the full diff contains no operation-bearing code, and an independent reviewer has
checked assembly identity, source minimality, deterministic generation, contract JSON, scope, and
package exclusion.
