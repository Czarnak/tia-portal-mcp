# Wave 1 N1 Network-Device Creation Names Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Correct `add_network_device` so distinct station and device-item names reach Siemens
`CreateWithItem` in the documented order without narrowing the operation to PLC-only hardware.

**Architecture:** Keep the host request, worker dispatch, result DTO, and canonical network-write
contract unchanged. Fix the single worker adapter call, prove the call order with a source-contract
RED, retain executable distinct-name request forwarding coverage, and verify real created identities
only in a separately authorized V21 acceptance run.

**Tech Stack:** C#; net48 Openness worker; net10.0 host/tests; xUnit; Siemens V21 reference stubs;
FakeWorker network-write integration.

**Spec:** [Open Bug Parallel Pull-Request Delivery Design](../specs/2026-09-26-open-bug-parallel-pr-delivery-design.md)

**Future branch:** `fix/network-device-creation-names`, created in its own worktree from the
then-current `origin/main`; its pull request targets `main` directly.

## Global Constraints

- Do not implement this plan on `docs/open-bug-wave-planning`; first merge the docs-only PR, then
  create the N1 branch directly from that updated `main`.
- Production allowlist: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs` only.
- Test allowlist: create
  `TiaMcpServer.Tests/Network/NetworkDeviceCreatorWorkerContractTests.cs` and modify
  `TiaMcpServer.Tests/Network/NetworkFieldForwardingTests.cs` only.
- Do not change `AddDeviceResultInfo`, worker/host dispatch, `WorkerRequest`, `OpennessWorkerClient`,
  FakeWorker, generic safety/batch code, or public input/output schemas.
- Reserve `WorkerRequest.cs`, worker `Program.cs`, `OpennessWorkerClient.cs`,
  `TiaMcpServer.FakeWorker/Program.cs`, and `TiaMcpServer.Tests.csproj` for other Wave 1 owners.
- `add_network_device` remains valid for non-PLC hardware. Do not require `PlcSoftware` and do not
  treat `rootItemName` as the requested CPU item name.
- Run all builds/tests serially because the solution's worker-copy targets conflict under parallel
  MSBuild.
- Plan or PR approval does not authorize a live TIA mutation. Obtain fresh authorization for one
  exact disposable `.ap21` immediately before the live gate.

## Review Focus

- Distinct station and item names must not be reversed at the Siemens call boundary.
- Omitted `deviceItemName` must continue to fall back to `deviceName` before worker invocation.
- Equal names must remain supported without concealing the distinct-name regression test.
- Non-PLC devices and devices whose first root item is a rack must retain current result semantics.
- A lost response after Apply must trigger inspection, never an automatic retry.

---

### Task 1: Correct and prove the Siemens `CreateWithItem` argument order

**Files:**
- Create: `TiaMcpServer.Tests/Network/NetworkDeviceCreatorWorkerContractTests.cs`
- Modify: `TiaMcpServer.Tests/Network/NetworkFieldForwardingTests.cs:96`
- Modify: `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs:9-50`

**Interfaces:**
- Consumes: existing
  `NetworkDeviceCreator.Create(Project project, string typeIdentifier, string deviceName, string deviceItemName)`.
- Produces: exactly one Siemens call
  `project.Devices.CreateWithItem(typeIdentifier, deviceItemName, deviceName)`.
- Preserves: `AddDeviceResultInfo.DeviceName`, `RootItemName`, `TypeIdentifier`, warnings, optional
  item-name fallback, and existing exception behavior.

- [ ] **Step 1: Add the distinct-name host/FakeWorker characterization**

Extend `NetworkFieldForwardingTests` with
`AddNetworkDevice_DistinctDeviceAndItemNames_AreForwardedUnchanged`. Use
`deviceName = "MCP_Station"` and `deviceItemName = "MCP_PLC"`; assert the captured worker request
contains both exact values. This test may already pass and must not be recorded as the behavioral
RED.

- [ ] **Step 2: Add the worker call-order RED**

Create `NetworkDeviceCreatorWorkerContractTests` with
`Create_UsesTypeItemNameThenDeviceNameForCreateWithItem`. Read the worker source using the
repository's existing source-contract-test path helper and use whitespace-tolerant matching to
assert exactly one invocation equivalent to:

```csharp
project.Devices.CreateWithItem(typeIdentifier, deviceItemName, deviceName)
```

Also assert that the reversed three-argument invocation is absent.

- [ ] **Step 3: Run the focused test and observe the intended RED**

Run:

```powershell
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -m:1 /p:UseTiaPortalReferenceStubs=true --filter "FullyQualifiedName~NetworkDeviceCreatorWorkerContractTests|FullyQualifiedName~NetworkFieldForwardingTests.AddNetworkDevice"
```

Expected: the forwarding characterization passes; the source-contract assertion fails because the
current worker passes `deviceName` before `deviceItemName`. A compile failure or forwarding failure
is the wrong RED and must be corrected before production code changes.

- [ ] **Step 4: Apply the one-line worker fix**

In `NetworkDeviceCreator.Create`, change only the `CreateWithItem` call to pass
`typeIdentifier, deviceItemName, deviceName`. Do not add a PLC lookup, result-field rewrite, or new
postcondition.

- [ ] **Step 5: Rerun the focused slice**

Run the command from Step 3. Expected: both focused test classes pass.

- [ ] **Step 6: Run N1's offline gate**

Run serially:

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests -m:1 /p:UseTiaPortalReferenceStubs=true
$n1PackageDir = Join-Path 'artifacts' ('n1-package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $n1PackageDir | Out-Null
dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release -m:1 /p:UseTiaPortalReferenceStubs=true -o $n1PackageDir
$n1Packages = @(Get-ChildItem -LiteralPath $n1PackageDir -Filter '*.nupkg')
if ($n1Packages.Count -ne 1) { throw "Expected exactly one N1 package, found $($n1Packages.Count)." }
pwsh -NoProfile -File scripts/verify-doctor-package.ps1 -PackagePath $n1Packages[0].FullName
git diff --check
```

If installed V21 references are available, also run:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
```

The real-reference build proves compile compatibility only, not Siemens runtime behavior.

- [ ] **Step 7: Review the exact allowlist and commit**

Confirm that only the three N1 files changed, then commit:

```powershell
git add TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs TiaMcpServer.Tests/Network/NetworkDeviceCreatorWorkerContractTests.cs TiaMcpServer.Tests/Network/NetworkFieldForwardingTests.cs
git commit -m "fix(network): preserve station and item names on device creation"
```

## Separately Authorized Live Acceptance

Do not enter this section when N1 alone is ready. The orchestrator first requires L1, K1, P1, and
N1 to be implementation-complete, clean, independently reviewed, and green on their offline gates.
It then freezes all four heads and verifies a combined-wave candidate from fresh `main`. Run live
acceptance only from that exact candidate; any lane change closes the gate and invalidates the
candidate and affected evidence.

On one exact disposable empty V21 project, first prove the requested names do not exist. Preview and
apply one unchanged `add_network_device` operation with `deviceName = "MCP_Station"` and
`deviceItemName = "MCP_PLC"`. Independently read the project tree and hardware configuration and
verify:

- `Device.Name == "MCP_Station"`;
- the CPU device item name is `"MCP_PLC"`; and
- `PlcSoftware.Name == "MCP_PLC"`.

Do not use `AddDeviceResultInfo.RootItemName` as proof of the CPU item name; a legitimate result can
be a rack such as `Rack_0`. Capture the response, audit, project status, and authorized discard or
restoration route. Inspect an unknown outcome before any retry.

## Pull-Request and Integration Gate

- PR title: `fix(network): preserve station and item names on device creation`.
- PR body initially uses `Refs #76` and states the offline/static versus live evidence boundary
  explicitly. Change it to `Closes #76` only if the separately authorized three-identity runtime
  gate for the reviewed commit completed and its evidence was accepted before the PR merges.
- Base branch: `main`; never base N1 on another Wave 1 feature branch.
- Before the wave barrier, refresh against current `main`, rerun the focused and full offline gates,
  and obtain independent whole-branch review. Mark N1 `offline-ready`; do not start live acceptance
  or merge while another Wave 1 lane remains unready.
- After merge, rerun the current-main integration gate before using `add_network_device` to build
  N2/N3 live fixtures.
