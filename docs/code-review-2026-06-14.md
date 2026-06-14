# Code Review — tia-portal-mcp

**Date:** 2026-06-14
**Reviewer:** Claude (Opus 4.8)
**Scope:** Full codebase (~8k LOC, 4 projects). Read-only review; no behavior changes implied by this document.

## Architecture summary

`TiaMcpServer` (net8.0 MCP host) spawns `TiaMcpServer.OpennessWorker` (net48 subprocess) once per
request, exchanging a single line of JSON (`WorkerRequest` / `WorkerResponse`). `TiaMcpServer.Contracts`
(netstandard2.0) holds the shared DTOs. The process split exists because Siemens TIA Openness V21 is
net48-only. The design is sound; the findings below are mostly **duplication** plus two real **bugs**.

Severity legend: 🔴 correctness · 🟠 duplication/simplification · 🟡 minor/lower-confidence.

---

## 🔴 Bugs / correctness

### 1. `UpdateTag` applies partial mutations, then throws

**File:** `TiaMcpServer.OpennessWorker/Openness/TagMutationService.cs:65-128`

The `isSafety` guard is the *last* statement (lines 122-125), executed only after `newName`,
`dataType`, `logicalAddress`, and all three external-access flags have already been written to the tag.
So `update_tag(name="A", newName="B", isSafety=true)` **renames the tag to B and then returns an
error** claiming the operation failed. The caller is told nothing changed, but the rename persists on
the next project save — a data-integrity / misleading-result bug.

Related: the `update_tag` tool advertises "…or safety flag" and exposes an `isSafety` parameter
(`TiaMcpServer/Tools/TagOperationsTools.cs:87,100`), but the operation can **never** succeed when it is
supplied — it always throws.

**Fix:** move the `isSafety` rejection to the top of the method, alongside the other validation, so it
fails before any mutation. (Longer term: either implement the flag or drop the parameter and the doc claim.)

### 2. Dead `AssemblyResolve` handler in the MCP host

**File:** `TiaMcpServer/Program.cs:12-42`

The net8.0 host has **no reference to `Siemens.Engineering`** (its csproj references only
`ModelContextProtocol`, `Microsoft.Extensions.Hosting`, and `Contracts`; all Openness work happens in
the subprocess). The `static Program()` constructor and `CurrentDomain_AssemblyResolve` therefore never
fire. The handler is also incorrect: it uses a different registry key
(`_InstalledSoftware\TIAP\21.0`) and path (`PublicAPI\V21`, no `net48`) than the worker's real resolver,
with a comment referencing a nonexistent ".NET 8 DLL".

**Fix:** delete the static constructor and the handler.

### 3. Project re-opened on every call even when already open *(lower confidence — needs SDK verification)*

**File:** `TiaMcpServer.OpennessWorker/Openness/TiaPortalSession.cs:49-64` and the worker handlers.

`Connect()` already sets `Project = _tiaPortal.Projects.FirstOrDefault()`; each handler that receives a
`projectPath` then calls `OpenProject` → `Projects.Open(new FileInfo(path))` unconditionally. With a
fresh worker process per request, every path-bearing call re-opens the project. In Openness, `Open` on
an already-open project commonly throws, and opening is slow. **Deferred** — requires verifying actual
V21 `Projects.Open` behavior against a running TIA Portal before changing.

---

## 🟠 Duplication / simplification

### 4. Five byte-identical copies of `FindPlcSoftwareInDeviceItems`

The recursive PLC-software finder is copy-pasted across:

- `Openness/PlcSoftwareLocator.cs:32`
- `Openness/CompileChecker.cs:136`
- `Openness/BlockTargetResolver.cs:95`
- `Openness/ProjectTreeWalker.cs:50`
- `Openness/CrossReferenceReader.cs:184`

The device-loop wrapper (`FindPlcSoftware` / `FindAllPlcSoftware`) is duplicated 4×, and the
`DiscoveredPlcSoftware` holder appears in both `CompileChecker` and `CrossReferenceReader`.

**Fix:** consolidate into `PlcSoftwareLocator` — a `FindAll(project, plcName)` yielding
`(deviceName, software)` plus the existing first-match `Find`. Every caller collapses to one line.

### 5. ~12 copies of the same five-`catch` block in the worker

**File:** `TiaMcpServer.OpennessWorker/Program.cs`

Every handler ends with the identical
`EngineeringException / NonRecoverableException / InvalidOperationException / IOException` wall
(~19 lines × ~12 ≈ 230 duplicated lines). The session boilerplate
(`new TiaPortalSession()` → `EnsureConnected()` → optional `OpenProject` → `Project is null` check)
repeats in ~10 handlers.

**Fix:** extract `Execute(Func<WorkerResponse>)` for the catch wall and
`WithProject(request, Func<Project, WorkerResponse>)` for the setup. The pattern already exists partially
(`TagMutation`, `ProjectLifecycle`); generalize it.

### 6. ~10 redundant methods in `OpennessWorkerClient`

**File:** `TiaMcpServer/Worker/OpennessWorkerClient.cs:25-322`

`BrowseProjectTreeAsync`, `ReadHardwareConfigAsync`, `SearchEquipmentCatalogAsync`,
`AddNetworkDeviceAsync`, `ConfigureNetworkDeviceAsync`, `ReadCrossReferencesAsync`,
`GetBlockContentAsync`, `UpdateBlockLogicAsync`, `ListTagTablesAsync`, `CompileCheckAsync` each
re-implement the exact `TryResolve → SendAsync → success ? payload : error` flow that the existing
`SendBoundProjectRequestAsync` helper (line 700) already provides.

**Fix:** route all of them through `SendBoundProjectRequestAsync` with the correct empty payload
(`"[]"` / `"{}"` / `""`).

### 7. Three near-identical reflection helpers

`ReadProperty` / `ReadEnumerableProperty` / `Enumerate` (guarded reflection over Openness objects) are
duplicated in `HardwareConfigReader.cs:314`, `NetworkDeviceConfigurator.cs:297`, and
`EquipmentCatalogSearcher.cs:223`.

**Fix:** extract a shared `OpennessReflection` utility.

---

## 🟡 Minor / lower confidence *(documented, not scheduled)*

- **String-prefix error signalling** — `OpennessWorkerClient` detects failure via
  `result.StartsWith("Error:")` (`SaveProjectAsAsync:639`, `CloseProjectAsync:691`). Fragile.
  `CloseProjectAsync`'s `Clear(projectPath) is false → Clear(null)` fallback is effectively unreachable.
- **`SendAsync` orphans tasks on timeout** (`OpennessWorkerClient.cs:807-816`) — `responseLineTask` /
  `stderrTask` are not awaited after a kill; risk of unobserved task exceptions. Low severity.
- **`TryResolve` mutates binding state** (`ProjectSessionBinding.cs:28-32`) — binds as a side effect of
  a "resolve"; intentional but the name hides the write.
- **`AllowTiaConfirmations` ignored** on `AddNetworkDevice` / `ConfigureNetworkDevice` (worker
  hardcodes `true`); harmless but inconsistent.
- **Catch ordering** — `NonRecoverableException` after `EngineeringException` is valid only while they
  remain siblings in the SDK hierarchy.

---

## What's solid

Per-item `try/catch` around SDK enumeration (one bad block doesn't sink a whole read), consistent
confirm-gating on writes, a well-structured and unit-tested `BlockAddress.Parse`, honest
`// UNVERIFIED SDK CALL` markers, and the correct net48/net8 process isolation.

---

## Resolution (applied 2026-06-14)

High-confidence items **#1, #2, #4, #5, #6, #7** were applied. **#3** and all 🟡 items were deferred.
Result: net **−841 lines** (11 files), build clean (**0 warnings**, down from 6), **97/97 tests pass**.

| # | Change |
|---|--------|
| 1 | `TagMutationService.UpdateTag` now rejects `isSafety` before any mutation. |
| 2 | Deleted the dead `AssemblyResolve` handler + static ctor from `TiaMcpServer/Program.cs` (also removed the 5 CA1416 warnings). |
| 4 | All five PLC-finder copies + duplicate `DiscoveredPlcSoftware` collapsed into `PlcSoftwareLocator` (`Find` / `FindAll` / `FindInDevice`). |
| 5 | Worker `Program.cs`: extracted `Execute` (the catch wall), `WithSession`, `WithProject`, `Success<T>` / `RawPayload`; every handler routed through them (537→~lean). |
| 6 | `OpennessWorkerClient`: ~10 inline methods collapsed onto `SendBoundProjectRequestAsync`. |
| 7 | Shared `OpennessReflection` extracted for `HardwareConfigReader` + `NetworkDeviceConfigurator`. **`EquipmentCatalogSearcher` left as-is** — its broader per-member exception handling over unverified catalog APIs is intentional and would regress to coarser recovery if narrowed. |

### Two intentional micro-behavior notes (both behavior-equivalent for the real caller)

- **#6 `ReadCrossReferencesAsync`**: the filter is now validated *before* `TryResolve`, so an invalid filter no longer binds the session. Previously it bound first, then rejected.
- **#5 `add_network_device` / `configure_network_device`**: the worker now derives the confirmation-allow flag from `request.AllowTiaConfirmations` (via `WithSession`) instead of a hardcoded `true`. The client always sends `true` for these, so behavior is unchanged — and this incidentally resolves the 🟡 "AllowTiaConfirmations ignored" inconsistency.

Verification was compile + the existing 97-test suite (which links `OpennessWorkerClient` and the tool files). The worker's net48 paths are exercised only at runtime against TIA Portal; the refactor is behavior-preserving by construction. No commit was made.

---

## Follow-up suggestions (new work, not regressions from this pass)

### A. Clearer up-front diagnostic when the worker can't locate TIA Openness

**Files:** `TiaMcpServer.OpennessWorker/Openness/AssemblyResolver.cs`,
`TiaMcpServer/Worker/OpennessWorkerClient.cs`

The net8 host shells out to the bundled **net48** worker, which resolves `Siemens.Engineering.*` from the
user's local TIA Portal V21 install (env var `TiaPortalV21Dir` → registry → standard path). This is the
correct design and is unaffected by deleting the host's dead `AssemblyResolve` handler (#2). However, the
two runtime prerequisites — **.NET Framework 4.8** (to run the worker) and **TIA Portal V21 installed** —
are only discovered implicitly:

- If Openness assemblies are missing, `AssemblyResolver` throws `FileNotFoundException` *lazily*, on first
  touch of a Siemens type. The host then reports it generically as
  *"TIA Openness worker exited without a response. {stderr}"* (`OpennessWorkerClient.SendAsync`), burying the
  actionable detail.
- If the .NET Framework 4.8 runtime itself is absent, the worker `.exe` fails to start and the user gets an
  opaque process-start error.

**Suggested enhancement:** make the failure explicit and actionable, e.g.:

- Add a `validate_environment` worker method (or a startup self-check) that calls
  `GetOpennessInstallPath()` and returns a structured, friendly message ("TIA Portal V21 Openness not found;
  install TIA Portal V21 or set `TiaPortalV21Dir` to the folder containing `Siemens.Engineering.*.dll`. Checked: …")
  instead of a raw exception buried in stderr.
- In `OpennessWorkerClient`, when the worker exits without a response, detect the
  `FileNotFoundException` / missing-Openness signature in stderr and surface the resolver's "Checked locations"
  hint directly to the MCP caller.
- Optionally, detect a missing .NET Framework 4.8 runtime before/while spawning the worker and return a
  one-line install pointer rather than a generic process-start failure.

This is purely additive (better operator experience on first run); it does not change any existing
success path. Estimated scope: small, isolated to the resolver + worker-client error mapping.
