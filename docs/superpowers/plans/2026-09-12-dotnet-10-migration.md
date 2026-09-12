# .NET 10 Migration and Dependency Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate the modern host, tests, and FakeWorker to .NET 10, adopt the reviewed dependency upgrades, and preserve the net48 worker, public MCP contract, and release package boundaries.

**Architecture:** Retarget only the modern process side of the existing two-process architecture; retain `netstandard2.0` contracts and the `net48` Openness worker. Land SDK, TFM, dependency, package-layout, test, and documentation changes in one migration PR with bisectable commits, using the existing FakeWorker/protocol harness and exact package verifier as the compatibility gates.

**Tech Stack:** C#; .NET 10 host/tests/FakeWorker; .NET Standard 2.0 contracts; .NET Framework 4.8 Openness worker; ModelContextProtocol 2.2.0; System.Text.Json 10.0.12; Microsoft.Extensions 10.0.12; xUnit; PowerShell 7; GitHub Actions; NuGet global-tool and self-contained `win-x64` packaging.

**Spec:** [`docs/superpowers/specs/2026-09-12-dotnet-10-migration-design.md`](../specs/2026-09-12-dotnet-10-migration-design.md)

## Global Constraints

- Change `TiaMcpServer`, `TiaMcpServer.Tests`, and `TiaMcpServer.FakeWorker` from `net8.0` to `net10.0`.
- Keep `TiaMcpServer.Contracts` on `netstandard2.0` and `TiaMcpServer.OpennessWorker` on `net48`.
- Keep Siemens assemblies out of the host, repository, NuGet package, and self-contained release.
- Preserve all public MCP tool names, counts, input schemas, output schemas, annotations, access-mode availability, and write-safety behavior.
- Use SDK minimum `10.0.400`, `rollForward: latestFeature`, and `allowPrerelease: false`; CI and release install `10.0.x`.
- Use `ModelContextProtocol` `2.2.0`, `System.Text.Json` `10.0.12`, `Microsoft.SourceLink.GitHub` `10.0.401`, and direct `Microsoft.Extensions.*` references `10.0.12`.
- Recheck stable patch releases before implementation; a newer patch in the same major line is allowed only if all gates are rerun and the plan's recorded versions are updated.
- Keep the NuGet tool framework-dependent at `tools/net10.0/any/`; keep the standalone release explicit `win-x64`, self-contained, single-file, and `PackAsTool=false`.
- Treat `System.IO.Pipelines.dll` as an exact required net48 worker companion after the System.Text.Json upgrade; do not weaken the worker-file allowlist.
- Keep solution builds serial with `-m:1`, use Siemens reference stubs for the mandatory offline Release gate, and retain the 80% line-coverage threshold.
- Stop before live TIA Portal. A live V21 read-only acceptance requires separate authorization after all offline gates pass.
- Do not merge, close, comment on, or otherwise mutate Dependabot PRs without separate explicit approval.

---

### Task 1: Pin the .NET 10 toolchain contract in tests

**Files:**
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/ReleaseWorkflowTests.cs`

**Interfaces:**
- Consumes: the repository-root lookup and workflow command readers already in `CiWorkflowTests` and `ReleaseWorkflowTests`.
- Produces: failing contract tests for the exact SDK selection, CI/publish SDK line, NuGet tool layout, and standalone publish boundary used by Tasks 2 and 5.

- [ ] **Step 1: Add a RED test for `global.json` and workflow SDK selection**

Add a test that parses `global.json` and reads both workflow files:

```csharp
[Fact]
public void Toolchain_UsesServicedDotNet10WithoutCrossMajorRollForward()
{
    var repositoryRoot = GetRepositoryRoot();
    using var globalJson = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(repositoryRoot, "global.json")));
    var sdk = globalJson.RootElement.GetProperty("sdk");

    Assert.Equal("10.0.400", sdk.GetProperty("version").GetString());
    Assert.Equal("latestFeature", sdk.GetProperty("rollForward").GetString());
    Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

    foreach (var workflow in new[] { "ci.yml", "publish.yml" })
    {
        var text = File.ReadAllText(Path.Combine(
            repositoryRoot, ".github", "workflows", workflow));
        Assert.Contains("10.0.x", text, StringComparison.Ordinal);
        Assert.DoesNotContain("8.0.x", text, StringComparison.Ordinal);
    }
}
```

Add this exact import to `CiWorkflowTests.cs`:

```csharp
using System.Text.Json;
```

- [ ] **Step 2: Add RED release-boundary assertions**

Extend `ReleaseWorkflowTests` so the pack step stays framework-dependent and the standalone step
stays explicit:

```csharp
[Fact]
public void ToolAndStandalonePublish_KeepDistinctDeploymentModels()
{
    var packStep = ReadWorkflowStep("Build and Pack", "Verify tool package contents");
    Assert.DoesNotContain("-r win-x64", packStep, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("--self-contained true", packStep, StringComparison.OrdinalIgnoreCase);

    var publishStep = ReadWorkflowStep("Publish Standalone Binary", "Zip Standalone Binary");
    Assert.Contains("-r win-x64", publishStep, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("--self-contained true", publishStep, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("/p:PackAsTool=false", publishStep, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 3: Run the focused tests and record RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj `
  --filter "FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~ReleaseWorkflowTests" `
  --configuration Release
```

Expected: the new toolchain test fails because `global.json` and both workflows still name .NET 8;
the existing release-boundary assertions remain green.

- [ ] **Step 4: Commit the migration contract tests**

```powershell
git add TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs `
        TiaMcpServer.Tests/Diagnostics/ReleaseWorkflowTests.cs
git commit -m "test: pin dotnet 10 migration boundaries"
```

### Task 2: Retarget the modern projects, SDK, CI, and path-sensitive harnesses

**Files:**
- Modify: `global.json`
- Modify: `TiaMcpServer/TiaMcpServer.csproj`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Modify: `TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj`
- Modify: `.github/workflows/ci.yml`
- Modify: `.github/workflows/publish.yml`
- Modify: `TiaMcpServer.Tests/TestSupport/FakeWorkerLocator.cs`
- Modify: `TiaMcpServer.Tests/Batch/BatchFieldForwardingTests.cs`
- Modify: `scripts/live-test-project-tree-v3.ps1`

**Interfaces:**
- Consumes: Task 1's exact toolchain contract; existing serial solution build and FakeWorker executable discovery.
- Produces: a `net10.0` modern build while retaining the unchanged `netstandard2.0`/`net48` IPC boundary.

- [ ] **Step 1: Update the SDK selection**

Replace `global.json` with:

```json
{
  "sdk": {
    "version": "10.0.400",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

- [ ] **Step 2: Retarget only the modern projects**

Change the three project properties:

```xml
<TargetFramework>net10.0</TargetFramework>
```

Do not change either of these:

```xml
<!-- TiaMcpServer.Contracts -->
<TargetFramework>netstandard2.0</TargetFramework>

<!-- TiaMcpServer.OpennessWorker -->
<TargetFramework>net48</TargetFramework>
```

- [ ] **Step 3: Update CI and release SDK setup**

In both workflow files, change the setup display text and input from `8.0`/`8.0.x` to
`10.0`/`10.0.x`. Preserve `actions/setup-dotnet@v6`, the serial `-m:1` stub build, coverage gate,
pack command, OIDC publishing, and explicit self-contained publish flags.

- [ ] **Step 4: Update executable lookup paths**

Change only the modern output segments from `net8.0` to `net10.0` in:

```csharp
Path.Combine(
    directory.FullName,
    "TiaMcpServer.FakeWorker", "bin", configuration, "net10.0",
    "TiaMcpServer.FakeWorker.exe")
```

Update the default live-harness path to
`TiaMcpServer\bin\Release\net10.0\TiaMcpServer.exe`. Do not run the live harness.

- [ ] **Step 5: Restore with the selected SDK**

Run:

```powershell
dotnet --version
dotnet restore TiaMcpServer.sln
```

Expected: `dotnet --version` resolves to stable `10.0.4xx`; restore succeeds for `net10.0`,
`netstandard2.0`, and `net48`.

- [ ] **Step 6: Build the cross-framework solution**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release `
  /p:UseTiaPortalReferenceStubs=true
```

Expected: all five projects build; the host output contains the `net48` worker payload.

- [ ] **Step 7: Rerun the Task 1 tests and FakeWorker path coverage**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~ReleaseWorkflowTests|FullyQualifiedName~BatchFieldForwardingTests"
```

Expected: PASS.

- [ ] **Step 8: Commit the modern TFM cutover**

```powershell
git add global.json .github/workflows/ci.yml .github/workflows/publish.yml `
        TiaMcpServer/TiaMcpServer.csproj `
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj `
        TiaMcpServer.FakeWorker/TiaMcpServer.FakeWorker.csproj `
        TiaMcpServer.Tests/TestSupport/FakeWorkerLocator.cs `
        TiaMcpServer.Tests/Batch/BatchFieldForwardingTests.cs `
        scripts/live-test-project-tree-v3.ps1
git commit -m "build: retarget host tooling to dotnet 10"
```

### Task 3: Upgrade System.Text.Json and make the new worker asset explicit

**Files:**
- Modify: `TiaMcpServer.Contracts/TiaMcpServer.Contracts.csproj`
- Modify: `TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj`
- Modify: `TiaMcpServer/Diagnostics/Checks/OpennessWorkerCheck.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/OpennessWorkerCheckTests.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/DoctorPackageVerificationScriptTests.cs`
- Modify: `scripts/verify-doctor-package.ps1`

**Interfaces:**
- Consumes: the unchanged `netstandard2.0` contracts and `net48` worker target; the exact worker companion-file and NuGet allowlists.
- Produces: System.Text.Json 10.0.12 on both sides of the legacy boundary, with `System.IO.Pipelines.dll` copied, diagnosed, packed, and verified exactly once.

- [ ] **Step 1: Convert the pipeline assembly test to a RED required-file test**

Replace `WorkerPayloadWithUnexpectedPipelineAssembly_FailsPackageVerification` with:

```csharp
[Fact]
public void WorkerPayloadWithoutPipelines_FailsPackageVerification()
{
    var workerOutput = FindBuiltWorkerOutput();
    var packagePath = CreatePackage(
        workerOutput,
        excludedFile: "System.IO.Pipelines.dll");

    try
    {
        var result = RunVerifier(packagePath);
        var output = result.StandardOutput + Environment.NewLine + result.StandardError;

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("System.IO.Pipelines.dll", output, StringComparison.Ordinal);
    }
    finally
    {
        File.Delete(packagePath);
    }
}
```

Also change the test package entry prefix from `tools/net8.0/any/openness-worker/` to
`tools/net10.0/any/openness-worker/`.

- [ ] **Step 2: Add `System.IO.Pipelines.dll` to both companion-file contracts**

Insert the file in the same deterministic position in
`OpennessWorkerCheck.RequiredCompanionFiles` and the test's `RequiredCompanionFiles` array:

```csharp
"System.Numerics.Vectors.dll",
"System.IO.Pipelines.dll",
"System.Runtime.CompilerServices.Unsafe.dll",
```

- [ ] **Step 3: Update the strict package verifier**

Change:

```powershell
$canonicalPrefix = 'tools/net10.0/any/openness-worker/'
```

Add `'System.IO.Pipelines.dll'` to `$requiredFiles`. Preserve exact duplicate, missing-file,
unexpected-file, Siemens-DLL, and runtimeconfig rejection.

- [ ] **Step 4: Run the focused tests and record RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~OpennessWorkerCheckTests|FullyQualifiedName~DoctorPackageVerificationScriptTests"
```

Expected: RED because the current System.Text.Json 8 worker build does not contain
`System.IO.Pipelines.dll`.

- [ ] **Step 5: Upgrade System.Text.Json on both shared boundaries**

Use exactly:

```xml
<PackageReference Include="System.Text.Json" Version="10.0.12" />
```

in the contracts and worker projects. Do not add a host-level direct reference; the `net10.0`
shared framework and MCP dependency graph own the host copy.

- [ ] **Step 6: Clean, restore, and rebuild before judging the worker payload**

Run:

```powershell
dotnet clean TiaMcpServer.sln -m:1 --configuration Release
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release `
  /p:UseTiaPortalReferenceStubs=true
```

Expected: the fresh `TiaMcpServer.OpennessWorker\bin\Release\net48` output contains exactly one
`System.IO.Pipelines.dll` alongside the other required runtime assets.

- [ ] **Step 7: Run worker, JSON, cursor, and package-verifier regressions**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~OpennessWorkerCheckTests|FullyQualifiedName~DoctorPackageVerificationScriptTests|FullyQualifiedName~CanonicalJson|FullyQualifiedName~PayloadContract|FullyQualifiedName~CursorCodec"
```

Expected: PASS with no property-name conflict or worker payload drift.

- [ ] **Step 8: Commit the serializer and worker-runtime graph together**

```powershell
git add TiaMcpServer.Contracts/TiaMcpServer.Contracts.csproj `
        TiaMcpServer.OpennessWorker/TiaMcpServer.OpennessWorker.csproj `
        TiaMcpServer/Diagnostics/Checks/OpennessWorkerCheck.cs `
        TiaMcpServer.Tests/Diagnostics/OpennessWorkerCheckTests.cs `
        TiaMcpServer.Tests/Diagnostics/DoctorPackageVerificationScriptTests.cs `
        scripts/verify-doctor-package.ps1
git commit -m "build: upgrade worker json runtime to version 10"
```

### Task 4: Upgrade ModelContextProtocol with protocol-level compatibility gates

**Files:**
- Modify: `TiaMcpServer/TiaMcpServer.csproj`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`
- Modify if compilation requires it: `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`
- Modify only if a failing contract test proves it necessary: MCP-registered tool classes under `TiaMcpServer/Batch/`, `TiaMcpServer/Network/`, and `TiaMcpServer/Tools/`
- Test: `TiaMcpServer.Tests/Tools/WriteToolMcpAnnotationProtocolTests.cs`
- Test: `TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs`
- Test: `TiaMcpServer.Tests/Network/NetworkStructuredProtocolTests.cs`

**Interfaces:**
- Consumes: the existing stdio/stream protocol harness and the exact public tool/schema tests.
- Produces: ModelContextProtocol 2.2.0 with unchanged repository tool behavior and explicit evidence that v2's required `inputSchema` and structured-result rules are satisfied.

- [ ] **Step 1: Capture the v1.2 protocol baseline**

Before changing the package, run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~WriteToolMcpAnnotationProtocolTests|FullyQualifiedName~McpToolSchemaTests|FullyQualifiedName~NetworkStructuredProtocolTests"
```

Expected: PASS. Save the test count in the implementation notes.

- [ ] **Step 2: Strengthen the existing `tools/list` assertion if needed**

Ensure the production-surface protocol test checks every advertised tool has an object input
schema. Add this assertion inside its existing tool loop if no equivalent assertion exists:

```csharp
Assert.All(
    tools,
    tool => Assert.Equal(
        System.Text.Json.JsonValueKind.Object,
        tool.ProtocolTool.InputSchema.ValueKind));
```

This assertion must pass before the package upgrade; it is a preservation sentinel, not a reason
to change production schemas.

- [ ] **Step 3: Upgrade both direct MCP references**

Use:

```xml
<PackageReference Include="ModelContextProtocol" Version="2.2.0" />
```

in the host and test projects.

- [ ] **Step 4: Restore and compile before adapting APIs**

Run:

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release `
  /p:UseTiaPortalReferenceStubs=true
```

Expected: either a clean build or precise compile errors limited to MCP SDK construction or
protocol model APIs. Do not make speculative HTTP, OAuth, Tasks, Roots, Sampling, or Logging
migrations.

- [ ] **Step 5: Make the minimum compile adaptation**

If v2 changed construction signatures, update only the in-process harness and production host
registration needed to preserve these established calls:

```csharp
registerTools(collection.AddMcpServer());
var server = McpServer.Create(...);
var client = await McpClient.CreateAsync(...);
```

Keep `WithTools<T>()`, `WithStdioServerTransport()`, the access-mode registration split, and all
`[McpServerTool]` names/annotations unchanged unless the compiler or a focused protocol test proves
a v2 equivalent is required.

- [ ] **Step 6: Rebuild and run the protocol compatibility suite**

Run the Task 4 Step 1 command again.

Expected: PASS; read-only/read-write tool counts, exact names, annotations, input/output schemas,
and canonical structured responses match the pre-upgrade contract.

- [ ] **Step 7: Commit the MCP major upgrade as one review unit**

```powershell
git add TiaMcpServer/TiaMcpServer.csproj `
        TiaMcpServer.Tests/TiaMcpServer.Tests.csproj `
        TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs `
        TiaMcpServer.Tests/Tools/WriteToolMcpAnnotationProtocolTests.cs `
        TiaMcpServer.Tests/Tools/McpToolSchemaTests.cs `
        TiaMcpServer.Tests/Network/NetworkStructuredProtocolTests.cs
git commit -m "build: upgrade model context protocol sdk to 2.2"
```

Stage only files actually changed; do not touch production tool classes if the package compiles
and protocol tests already pass.

### Task 5: Align Microsoft.Extensions and SourceLink with .NET 10

**Files:**
- Modify: `TiaMcpServer/TiaMcpServer.csproj`
- Modify: `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`

**Interfaces:**
- Consumes: the .NET 10 TFM and MCP 2.2 dependency graph from Tasks 2 and 4.
- Produces: one direct Microsoft.Extensions 10.0.12 line and SourceLink 10.0.401, without unrelated package churn.

- [ ] **Step 1: Align the host packages**

Use:

```xml
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
<PackageReference Include="Microsoft.SourceLink.GitHub" Version="10.0.401" PrivateAssets="All" />
```

Keep `Microsoft.Win32.Registry` at `5.0.0`.

- [ ] **Step 2: Align the direct test dependencies**

Use:

```xml
<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.12" />
<PackageReference Include="Microsoft.Extensions.Logging" Version="10.0.12" />
<PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.12" />
```

Keep `Microsoft.NET.Test.Sdk`, xUnit, runner, coverlet, and Registry unchanged.

- [ ] **Step 3: Restore and inspect the resolved graph**

Run:

```powershell
dotnet restore TiaMcpServer.sln
dotnet list TiaMcpServer.sln package --include-transitive
```

Expected: all direct `Microsoft.Extensions.*` references resolve to `10.0.12`; SourceLink resolves
to `10.0.401`; there are no package-downgrade warnings or direct `8.x` framework-library
references.

- [ ] **Step 4: Run host startup and diagnostics tests**

Run:

```powershell
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release `
  /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~Doctor|FullyQualifiedName~ApplicationInfoService|FullyQualifiedName~ReadOnlyModeTests|FullyQualifiedName~McpToolSchemaTests"
```

Expected: PASS.

- [ ] **Step 5: Commit the framework-library alignment**

```powershell
git add TiaMcpServer/TiaMcpServer.csproj TiaMcpServer.Tests/TiaMcpServer.Tests.csproj
git commit -m "build: align framework packages with dotnet 10"
```

### Task 6: Update package layout tests and produce clean package artifacts

**Files:**
- Modify: `TiaMcpServer.Tests/Diagnostics/DoctorPackageVerificationScriptTests.cs`
- Modify if an assertion is absent: `TiaMcpServer.Tests/Diagnostics/ReleaseWorkflowTests.cs`
- Modify only if a RED package test requires it: `TiaMcpServer/TiaMcpServer.csproj`
- Verify: `scripts/verify-doctor-package.ps1`

**Interfaces:**
- Consumes: the `net10.0` host, updated worker asset allowlist, and .NET 10 tool-pack behavior.
- Produces: a framework-dependent `tools/net10.0/any/` NuGet tool and a distinct self-contained `win-x64` release with reproducible worker contents.

- [ ] **Step 1: Add a package-layout RED test before changing MSBuild behavior**

Extend the package verifier tests to reject worker entries under an old or RID-specific prefix.
The fixture must create one package with:

```text
tools/net8.0/any/openness-worker/TiaMcpServer.OpennessWorker.exe
```

and one with:

```text
tools/net10.0/win-x64/openness-worker/TiaMcpServer.OpennessWorker.exe
```

Both must fail with a message naming the canonical `tools/net10.0/any/openness-worker/` prefix.

- [ ] **Step 2: Run the focused package tests and record RED/GREEN accurately**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~DoctorPackageVerificationScriptTests|FullyQualifiedName~ReleaseWorkflowTests"
```

Expected: new malformed-layout fixtures fail verification; the built-worker fixture passes.

- [ ] **Step 3: Pack from a clean Release output**

Run:

```powershell
dotnet clean TiaMcpServer.sln -m:1 --configuration Release
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release `
  /p:UseTiaPortalReferenceStubs=true
dotnet pack TiaMcpServer/TiaMcpServer.csproj --configuration Release --no-restore `
  --output artifacts/dotnet10 `
  /p:Version=3.0.0 /p:PackageVersion=3.0.0 /p:InformationalVersion=3.0.0 `
  /p:IncludeSourceRevisionInInformationalVersion=false
```

Expected: exactly one `.nupkg` under `artifacts/dotnet10`.

- [ ] **Step 4: Run the strict package verifier**

```powershell
$package = Get-ChildItem artifacts/dotnet10 -Filter *.nupkg -File
if ($package.Count -ne 1) { throw "Expected one package; found $($package.Count)." }
./scripts/verify-doctor-package.ps1 -PackagePath $package[0].FullName
```

Expected: PASS; canonical tool assets are under `tools/net10.0/any/`, the worker subtree contains
every required companion including `System.IO.Pipelines.dll`, no worker runtimeconfig is present,
and no Siemens DLL is packed.

- [ ] **Step 5: Publish and inspect the self-contained release**

Run:

```powershell
dotnet publish TiaMcpServer/TiaMcpServer.csproj --configuration Release `
  --runtime win-x64 --self-contained true `
  /p:PublishSingleFile=true /p:PackAsTool=false `
  /p:Version=3.0.0 /p:PackageVersion=3.0.0 /p:InformationalVersion=3.0.0 `
  /p:IncludeSourceRevisionInInformationalVersion=false `
  --output artifacts/dotnet10/win-x64
```

Expected: the root contains the self-contained host executable; `openness-worker/` contains the
complete net48 payload including `System.IO.Pipelines.dll`; no Siemens DLL is present anywhere.

- [ ] **Step 6: Apply MSBuild compatibility properties only on demonstrated failure**

If Step 4 shows a RID-specific NuGet tool layout because of .NET 10 SDK behavior, add:

```xml
<CreateRidSpecificToolPackages>false</CreateRidSpecificToolPackages>
<UseAppHost>false</UseAppHost>
```

to the tool project, then repeat Steps 3-5. Do not add these properties preemptively when the
existing project—with no `RuntimeIdentifiers`—already produces the canonical layout.

- [ ] **Step 7: Commit only proven package-boundary changes**

```powershell
git add TiaMcpServer.Tests/Diagnostics/DoctorPackageVerificationScriptTests.cs `
        TiaMcpServer.Tests/Diagnostics/ReleaseWorkflowTests.cs `
        TiaMcpServer/TiaMcpServer.csproj
git commit -m "test: pin dotnet 10 package layouts"
```

Stage only files actually changed. Do not commit `artifacts/`.

### Task 7: Update user, maintainer, and custom-integration documentation

**Files:**
- Modify: `README.md`
- Modify: `AGENTS.md`
- Modify: `.github/dependabot.yml`
- Modify: `docs/ARCHITECTURE.md`
- Modify: `docs/development/building.md`
- Modify: `docs/development/local-mcp-testing.md`
- Modify: `docs/guides/troubleshooting.md`
- Modify: `docs/SupportedOperations/README.md`
- Modify: `TiaMcpServer/Worker/TiaPortalInstallationLocator.cs`
- Modify: `TiaMcpServer.Contracts/IsExternalInit.cs`
- Modify: `TiaMcpServer.Contracts/ProjectOpenPolicy.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/Fakes.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/DotNetRuntimeCheckTests.cs`
- Modify: `TiaMcpServer.Tests/Diagnostics/DoctorTextRendererTests.cs`
- Modify after verification: `docs/IMPROVEMENT_LOG.md`

**Interfaces:**
- Consumes: the verified framework/package behavior from Tasks 2-6.
- Produces: audience-correct .NET 10 requirements and build paths without rewriting historical evidence or telling normal users to migrate internal SDK code.

- [ ] **Step 1: Update current architecture and build references**

Replace current-behavior references to `.NET 8`/`net8.0` with `.NET 10`/`net10.0` in the listed
landing, architecture, development, supported-operations, source-comment, and agent-guidance
files. Preserve every statement explaining why Siemens Openness remains in the `net48` worker.

Do not mechanically rewrite dated historical claims in `docs/superpowers/` or completed entries in
`docs/IMPROVEMENT_LOG.md`.

- [ ] **Step 2: Update runtime troubleshooting and test fixtures**

Change the SDK guidance to a stable .NET 10 SDK (`10.0.4xx` or the repository's current minimum)
and modern output paths to `net10.0`. Update diagnostic fixture strings from `.NET 8.0.5` to a
representative `.NET 10.0.x` value while preserving tests that merely echo the runtime description.

- [ ] **Step 3: Keep normal-user and custom-integration guidance separate**

Add concise guidance equivalent to:

```markdown
The framework-dependent `tia-mcp` global tool requires a supported .NET 10 runtime. The
self-contained `win-x64` archive includes the host runtime. Neither installation method requires
users to install the ModelContextProtocol NuGet package.

Custom .NET integrations that compile directly against the C# MCP SDK must review the
ModelContextProtocol 2.2 migration notes and retest protocol negotiation, tool schemas, and
structured results.
```

Keep the existing `browse_project_tree` v3 migration guidance separate.

- [ ] **Step 4: Update Dependabot's deliberate major-version boundary**

Keep the `Microsoft.Extensions.*` semver-major ignore, but change its comment from the
`net8.0`/SDK 8 alignment to the `net10.0`/SDK 10 alignment. This prevents an eventual .NET 11
major bump from bypassing an explicit platform decision.

- [ ] **Step 5: Record the completed migration only after all gates pass**

Add a dated completed entry to `docs/IMPROVEMENT_LOG.md` that names the modern `net10.0` side, the
unchanged `netstandard2.0`/`net48` boundary, the exact dependency versions, the package layouts,
and the offline evidence. Do not claim live TIA evidence unless a separate authorized run occurred.

- [ ] **Step 6: Check current-document references**

Run:

```powershell
rg -n -i "net8\.0|\.NET 8|8\.0\.400|8\.0\.x" `
  README.md AGENTS.md .github docs/development docs/guides `
  docs/ARCHITECTURE.md docs/SupportedOperations `
  TiaMcpServer TiaMcpServer.Contracts TiaMcpServer.Tests scripts
```

Expected: no current-behavior .NET 8 references. Any retained match is explicitly historical and
reviewed one by one.

- [ ] **Step 7: Run documentation-coupled tests**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --filter "FullyQualifiedName~CiWorkflowTests|FullyQualifiedName~DotNetRuntimeCheckTests|FullyQualifiedName~DoctorTextRendererTests"
```

Expected: PASS.

- [ ] **Step 8: Commit the migration documentation**

```powershell
git add README.md AGENTS.md .github/dependabot.yml docs/ARCHITECTURE.md `
        docs/development/building.md docs/development/local-mcp-testing.md `
        docs/guides/troubleshooting.md docs/SupportedOperations/README.md `
        docs/IMPROVEMENT_LOG.md TiaMcpServer/Worker/TiaPortalInstallationLocator.cs `
        TiaMcpServer.Contracts/IsExternalInit.cs TiaMcpServer.Contracts/ProjectOpenPolicy.cs `
        TiaMcpServer.Tests/Diagnostics/Fakes.cs `
        TiaMcpServer.Tests/Diagnostics/DotNetRuntimeCheckTests.cs `
        TiaMcpServer.Tests/Diagnostics/DoctorTextRendererTests.cs
git commit -m "docs: describe the dotnet 10 runtime boundary"
```

### Task 8: Run the mandatory offline release gate and prepare review

**Files:**
- Verify: all files changed in Tasks 1-7
- Do not modify: live TIA Portal projects or GitHub PR state

**Interfaces:**
- Consumes: the complete migration branch.
- Produces: one evidence bundle suitable for review and a separately gated live-acceptance option.

- [ ] **Step 1: Confirm exact direct dependency versions**

```powershell
dotnet list TiaMcpServer.sln package
```

Expected direct versions:

```text
ModelContextProtocol                           2.2.0
System.Text.Json                              10.0.12
Microsoft.SourceLink.GitHub                   10.0.401
Microsoft.Extensions.Hosting                 10.0.12
Microsoft.Extensions.DependencyInjection     10.0.12
Microsoft.Extensions.Logging                 10.0.12
Microsoft.Extensions.Logging.Abstractions    10.0.12
```

- [ ] **Step 2: Run the serial Release stub build**

```powershell
dotnet clean TiaMcpServer.sln -m:1 --configuration Release
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 --no-restore --configuration Release `
  /p:UseTiaPortalReferenceStubs=true
```

Expected: exit 0. Record warning and error counts; investigate every new warning.

- [ ] **Step 3: Run the full Release suite with coverage**

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --no-build `
  --configuration Release `
  --collect:"XPlat Code Coverage" `
  --settings TiaMcpServer.Tests/coverage.runsettings `
  --results-directory TestResults

$reports = @(Get-ChildItem TestResults -Recurse -Filter coverage.cobertura.xml)
if ($reports.Count -ne 1) { throw "Expected one Cobertura report; found $($reports.Count)." }
./scripts/verify-coverage-threshold.ps1 `
  -CoveragePath $reports[0].FullName `
  -MinimumLineRate 0.80
```

Expected: all tests pass and line coverage is at least 80%.

- [ ] **Step 4: Repeat package and publish verification**

Repeat Task 6 Steps 3-5 from the clean output created in Step 2. Record the NuGet tool's canonical
TFM/RID layout, worker file list, standalone host layout, and absence of Siemens DLLs.

- [ ] **Step 5: Review scope and whitespace**

```powershell
git diff --check
git status --short
git diff --stat
git diff -- global.json .github '*.csproj' scripts README.md AGENTS.md docs
```

Expected: no whitespace errors, no generated artifacts, no Siemens assemblies, no unrelated user
changes, and only the planned migration surface.

- [ ] **Step 6: Obtain an independent code review**

Ask the reviewer to focus on:

```text
- accidental movement of Siemens Openness into the net10.0 host
- public MCP schema, annotation, tool-count, or access-mode drift under SDK 2.2
- missing or extra net48 worker runtime assets, especially System.IO.Pipelines.dll
- RID-specific NuGet tool packaging under the .NET 10 SDK
- confusion between normal-user runtime requirements and custom SDK migration
```

Resolve findings with focused RED/GREEN evidence and rerun every affected gate.

- [ ] **Step 7: Stop before live TIA and remote PR housekeeping**

Report the offline result and request separate authorization for either action:

1. a read-only packaged-host acceptance against TIA Portal V21; or
2. closing/commenting on superseded Dependabot PRs after the migration lands.

Do not perform either action as part of this plan's offline implementation phase.

- [ ] **Step 8: Commit any review-only corrections after rerunning affected gates**

Use a focused conventional commit message that describes the correction. Do not squash or merge
without the user's explicit instruction.
