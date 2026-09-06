# Improvement Log — tia-portal-mcp

Engineering log for this project, started 2026-07-15. **Open follow-ups come first; completed work
is grouped at the end.** Entries are kept verbatim as a record of what was found and when.

Originally consolidated from three parallel reviews (C# correctness, silent-failure hunt, AI-agent usability audit)
plus manual verification of the highest-stakes claims. Test suite at authoring time: 146/146 green
(pure-logic tests only; no integration coverage of the worker or IPC layer). As of 2026-07-20 the
suite is 341/341 green and does cover the worker/IPC layer via the fake-worker harness.

## Overall verdict

Solid foundation: the batch-tool consolidation (45 → 16 tools), typed batch item schema with
per-property descriptions, the preview→token→apply safety model, and the audit trail are all
well-designed. The three biggest problems, in order of impact:

1. **Process-per-request worker transport** — every tool call spawns a fresh net48 worker,
   re-attaches to TIA Portal, and never closes projects it opens. This is the root cause of
   latency, token-expiry brittleness, and leaked project handles.
2. **Error signals get lost on the way to the agent** — worker stderr is discarded on success,
   ~20 catch blocks degrade results silently, two write operations report success after failing,
   and the whole host layer re-derives failure from an `"Error:"` string prefix.
3. **Small-model traps** — silently-ignored misspelled optional params, unbounded response
   payloads (up to 50 concatenated in one batch), token rejections without recovery hints,
   and schema descriptions that contradict the code.

---

## Phase 0 — Quick wins (small-model usability; ~1 day total, all low-risk)

| # | Change | Where | Why |
|---|--------|-------|-----|
| 0.1 | Unknown-operation error lists all valid names for the batch category | `Batch/BatchOperationCatalog.cs:144` | Most common dead-end for weak models; `CrossReferenceFilterNames.cs:43` already sets the precedent — DONE 2026-07-15 |
| 0.2 | Aggregate ALL batch validation errors instead of failing on the first | `BatchOperationCatalog.cs:77-112` | One round-trip fixes N mistakes instead of N round-trips — DONE 2026-07-15 |
| 0.3 | Append recovery instruction ("tokens are single-use, expire after 10 min; call preview_* again") to all 7 token-rejection texts; state the 10-min TTL in preview tool descriptions + README | `Safety/WriteSafetyService.cs:87-124`, `Batch/BatchTools.cs`, `Tools/ProjectLifecycleTools.cs`, README | Agent can self-recover without inference — DONE 2026-07-15 |
| 0.4 | Fix schema-facing description drift: `newName` falsely claims user-constant rename (`BatchOperationRequest.cs:47`, `BatchWorkerInvoker.cs:46` never forwards it — implement or remove the claim); `blockPath` omits `compile_check` scoping; `filter` lists 2 of 4 values and doesn't name its operation; `plcName` doesn't say which ops honor it | `Batch/BatchOperationRequest.cs` | The flat DTO makes descriptions the load-bearing contract — DONE 2026-07-15 |
| 0.5 | README drift: add `get_project_status` to batch read list (line 18); document `forceRebind` escape hatch; unify the two "already bound" error texts (`ProjectSessionBinding.cs:42` should mention forceRebind like `OpennessWorkerClient.cs:580` does) | README, Contracts | Three sources of truth currently disagree — DONE 2026-07-15 |
| 0.6 | Log audit-write failures to stderr/ILogger instead of bare `catch { }` | `Safety/WriteSafetyService.cs:164` | Silent loss of audit trail on a PLC-mutating tool — DONE 2026-07-15 |

## Phase 1 — Error propagation correctness (~2-4 days)

| # | Change | Where | Why |
|---|--------|-------|-----|
| 1.1 | Replace the `"Error:"` string-prefix convention with a structured result record (`Success`, `Payload`, `Error`) threaded through client → batch → safety → tools | `OpennessWorkerClient.cs:552`, `BatchExecutionEngine.cs:10`, `WriteSafetyTooling.cs:30,58,83`, `ProjectLifecycleTools.cs` (5 sites), `BatchTools.cs:139` | Any payload legitimately starting with "Error:" is misclassified; false-success bugs become structurally impossible — DONE 2026-07-16 |
| 1.2 | **False-success writes**: `NetworkDeviceCreator.cs:23-31` must fail the response when `CreateWithItem` throws (today: buried warning + `Success=true`, and batch stop-on-failure doesn't stop). `NetworkDeviceConfigurator.cs:71-74` must fail when ALL requested settings were skipped | worker Openness/ | CRITICAL: agent believes a device exists that doesn't; audit log records success — DONE 2026-07-15 |
| 1.3 | Surface worker stderr: attach non-empty stderr as `warnings` on `WorkerResponse` and/or log via host ILogger; route per-item "Skipping X" degradation messages into `messages` arrays on the payload DTOs (pattern already used correctly by `CrossReferenceReader` and `CompileChecker`) | `OpennessWorkerClient.cs:633-656`, ~20 catch sites in worker readers | `browse_project_tree`/`read_hardware_config` can silently return partial trees today — DONE 2026-07-16 |
| 1.4 | `HardwareConfigReader` fallback defaults (`0`/`""`/`null`) indistinguishable from real values → nullable + `messages` marker | `HardwareConfigReader.cs:260-297` | Agent can't tell "read failed" from "actual value" — DONE 2026-07-16 |
| 1.5 | Add `Win32Exception` to the client catch filters with an actionable message (missing .NET FX 4.8 / corrupt openness-worker folder); include exception type name in the worker's catch-all error; wrap `SaveProjectAsAsync` like its siblings | `OpennessWorkerClient.cs:392,431,556,630`, worker `Program.cs:86-90` | The one prerequisite failure that surfaces as a raw protocol error today — DONE 2026-07-16 |
| 1.6 | Reject unknown JSON properties on batch items (`JsonUnmappedMemberHandling.Disallow` or equivalent SDK option) | host serializer config | Misspelled OPTIONAL params (`ip_adress`) currently succeed silently — the most dangerous trap found — DONE 2026-07-16 |
| 1.7 | Narrow bare `catch (Exception)` in `EquipmentCatalogSearcher.cs` reflection helpers to `EngineeringException`/`TargetInvocationException`, merging with the existing `OpennessReflection` helpers | worker Openness/ | Bugs currently masquerade as empty search results — DONE 2026-07-16 |

## Phase 2 — Structural (the big wins; ~1-2 weeks)

| # | Change | Where | Why |
|---|--------|-------|-----|
| 2.1 | **Persistent worker process**: keep the worker alive across requests (request loop already exists in worker `Program.cs:34` — the client kills it by closing stdin at `OpennessWorkerClient.cs:635`). Single `Attach()`, managed project open/close, health check + restart-on-crash, `SemaphoreSlim` serialization of requests | `OpennessWorkerClient.cs` | Fixes all 3 CRITICALs at once (re-attach per call, leaked project handles, concurrent mutation races); cuts preview→apply wall-clock far below the 10-min token TTL; makes 50-item batches practical (today: up to 3N spawns per write batch) — DONE 2026-07-16 |
| 2.2 | Interim (if 2.1 is deferred): add `SemaphoreSlim(1,1)` around `SendAsync` now | `OpennessWorkerClient.cs:614` | One-line mitigation for the concurrency CRITICAL — DONE 2026-07-15 |
| 2.3 | **Bound read payloads**: `depth`/`startPath` on `browse_project_tree`, `maxResults` on `search_equipment_catalog` + `read_cross_references`, plus a server-side byte budget with an explicit "truncated — narrow with plcName/filter/startPath" trailer | worker readers + batch schema | Only finding that can hard-kill a small model's session (README's own smoke test batches tree+hw+xref+catalog into one call) — DONE 2026-07-16 |
| 2.4 | Collapse lifecycle preview/apply pairs: calling a write tool WITHOUT a token returns the preview + token instead of an error → 16 tools become 10 | `Tools/ProjectLifecycleTools.cs` | Removes the "which preview matches this apply" lookup; kills the asymmetric-naming trap (`preview_write_batch`/`apply_write_batch` vs `preview_open_project`/`open_project`) — DONE 2026-07-16 |
| 2.5 | Evict expired tokens (sweep on `CreatePreview` is enough — no timer needed); validate token BEFORE the expensive N-spawn state re-read in apply | `WriteSafetyService.cs:16-36`, `BatchTools.cs:94-110` | Unbounded memory growth; dead tokens currently cost a full read pass — DONE 2026-07-16 |

## Phase 3 — Simplification (behavior-preserving refactors)

| # | Change | Where | Est. reduction |
|---|--------|-------|----------------|
| 3.1 | ~~Extract per-op descriptor + one generic executor for the six lifecycle preview/apply pairs.~~ **DROPPED 2026-07-20** — Phase 2.4 already collapsed `ProjectLifecycleTools.cs` from 374 to 125 lines. The six tools are ~12 lines each and the shared machinery lives in `WriteSafetyTooling`; what remains is genuinely per-operation. A descriptor table would add indirection without removing duplication. | `ProjectLifecycleTools.cs` (125 lines) | Resolved by 2.4 |
| 3.2 | Consolidate the 3 near-identical project-path binding checks into `ProjectSessionBinding` | `OpennessWorkerClient.cs`, `ProjectSessionBinding.cs` | drift risk — DONE 2026-07-20 |
| 3.3 | Collapse the double dispatch: `BatchWorkerInvoker` maps operation strings onto 20+ near-identical `OpennessWorkerClient` wrappers that only set `WorkerRequest` fields — build `WorkerRequest` directly from the batch item | `OpennessWorkerClient.cs:25-359`, `BatchWorkerInvoker.cs` | ~250 lines — **DEFERRED 2026-07-20**; design retained in `docs/superpowers/specs/2026-07-20-phase3-simplification-design.md` |
| 3.4 | ~~Merge `EquipmentCatalogSearcher`'s private reflection helpers (~90 lines) into `OpennessReflection`~~ **DROPPED 2026-07-20** — Phase 1.7 already did the merge. The remaining privates are `HasReadableProperty` (3 lines), `ReadStringProperty` (a 2-line passthrough already delegating to `OpennessReflection`), and two non-reflection helpers. ~10 lines of residual value. | worker Openness/ | Resolved by 1.7 |
| 3.5 | Share the host presentation `JsonSerializerOptions` (was duplicated byte-identically in 3 files) via `TiaMcpServer/Json/TiaJson.cs`. **Corrected scope:** the original entry claimed one config duplicated in 4 files. There are two distinct configs — presentation (3 host copies, deduped) and wire/IPC (`PersistentWorkerTransport` + worker `Program.cs`, which differ deliberately and live in separate processes; sharing them would require a `System.Text.Json` PackageReference on the dependency-free `Contracts` assembly). Wire options intentionally left per-process. | host | consistency — DONE 2026-07-20 (presentation only) |
| 3.5b | Inject `WriteSafetyService` via DI instead of the static audit path (registration already exists in `Program.cs`) | host | **DONE 2026-07-20 (Round 4, Task 2).** The tool layer receives the service through DI, so tests inject a temporary audit directory. |
| 3.6 | `WorkerRequest` god DTO (47 fields): defer full split — flat is a defensible MCP trade-off — but group with `#region` per operation family and add a comment mapping fields→operations | Contracts | documentation — DONE 2026-07-20 |

## Found during live testing against TIA Portal V21 (2026-07-20)

**Pre-Task-2 audit contamination is resolved — DONE via 3.5b (Round 4, Task 2).**
39 of 42 records in `%LOCALAPPDATA%\TiaMcpServer\audit` were produced by `dotnet test`, not by
real TIA usage. Before Task 2, the tool layer used the production audit directory, and every test run
appended real records — recognizable by `projectPath` values pointing into `TiaMcpServer.Tests\bin\`
and FakeWorker scripted keywords (`ok`, `hang`, `worker-error`) in `target`. The audited tool layer
now receives `WriteSafetyService` through DI, and tests inject a temporary audit directory.

This diluted the forensic record for PLC-mutating operations to ~7% signal, and a test run could be
mistaken for real engineering activity. It was the concrete justification for **3.5b** above: the
`WriteSafetyService(getUtcNow, tokenLifetime, auditDirectory)` constructor already supported
redirecting the audit directory, but the tool layer could not use that test-specific directory before
Task 2. DI now makes that isolation available to the tests.


Also confirmed live, all working as designed: bounded reads with `depth`/`startPath`/`maxResults`
plus the explicit truncation trailer (2.3); per-item batch isolation, where a failing `compile_check`
did not stop two sibling reads; `messages` arrays surfacing partial-read degradation rather than
silently returning defaults (1.3/1.4) — `read_hardware_config` reported 20 unreadable device
addresses, and `read_cross_references` reported "does not expose the cross-reference service"
instead of an empty result an agent would misread as "no unused objects"; single-use safety tokens
rejecting replay with the self-recovery instruction (0.3); and audit records whose
`requestedInputHash` matches the issuing preview exactly.

Separately, `compile_check` failed live with "Object 'PlcSoftware' does not expose a Compile
method" against the installed `tia-mcp 2.3.0` — already fixed on `main` by ae8af80, confirming that
fix addresses a real-hardware failure and that 2.3.0 predates it.

## Follow-ups discovered during Phase 3 (2026-07-20)

Documenting the `WorkerRequest` field→operation contract (3.6) surfaced two more instances of the
same "declared but never forwarded" bug class Phase 0.4 found with `newName`. Round 4 resolved the
software-side behavior and added forwarding coverage for every declared field; the hardware question
for tag creation remains deferred.

- **`deviceItemName` on `configure_network_device`**: `BatchOperationRequest.cs` describes it
  unscoped ("Optional device item name; defaults to deviceName when omitted"), but
  `ConfigureNetworkDeviceAsync` has no such parameter and `BatchWorkerInvoker` never passes one.
  Only `add_network_device` forwards it. An agent setting it on `configure_network_device` has it
  silently dropped. **Resolved (Round 4, Task 7):** catalog validation now rejects
  `deviceItemName` for `configure_network_device` rather than accepting and dropping it.
- **`externalAccessible` / `externalVisible` / `externalWritable` / `isSafety` on `create_tag`**:
  described as generic "Optional tag attribute", but only `update_tag` forwards them. `create_tag`
  drops all four. **Resolved interim behavior (Round 4):** `create_tag` now errors when any of
  these attributes is supplied instead of silently dropping it. Whether Openness supports forwarding
  them at tag-creation time remains a hardware-gated decision.

A forwarding test now covers every declared field, preventing another declared-but-unforwarded field
from silently reaching the worker boundary.

Three further follow-ups, all raised by the Phase 3 final review and deliberately left undone:

- **Enforce the `WorkerRequest` forwarding comments with a test.** **DONE (Round 4):** the field→
  operation forwarding map is now tested for every declared field.
- **Collapse `BatchPayloadBudget.ReadBatchResponseLength` into `BatchResultFormatter.ReadBatch`.**
  It currently hand-mirrors that method's envelope purely to predict its output length. Phase 3
  made the two share `TiaJson.Presentation` so they cannot drift on serializer settings, but the
  duplicated envelope shape remains. Replacing the body with
  `BatchResultFormatter.ReadBatch(results).Length` removes it, at the cost of one extra
  serialization per budget probe — measure before adopting.
- **`TiaJson.Presentation.MakeReadOnly()`.** **DONE (Round 4):** presentation serialization options
  are frozen, protecting the safety-token `requestedInputHash` format.

## Deferred / explicitly not planned

- Openness `Transaction` and `ExclusiveAccess` APIs, authentication/authorization-event subscriptions,
  server-push/long-polling MCP notifications, and exposing `doctor` diagnostics as an MCP-callable tool
  (it remains CLI-only via `tia-mcp doctor`) — all out of Phase 5 production scope (AC-044); Phase 6+
  candidates.
- Splitting `WorkerRequest` into per-operation DTOs (churn > value while the protocol is stable).
- MCP protocol-level error signaling instead of text results (needs SDK investigation; revisit after 1.1).
- `NetworkDeviceConfigurator` speculative-reflection "UNVERIFIED SDK CALL" paths: verify against real
  Openness V21 API on the TIA machine and pin exact method signatures (needs hardware access).
- **Next round (needs TIA Portal hardware):** forward `externalAccessible`/`externalVisible`/
  `externalWritable`/`isSafety` on `create_tag` if Openness V21 permits setting them at tag-creation
  time — Round 4 narrowed this to that single question by making the fields an explicit error
  instead of a silent drop. Same session should verify the `NetworkDeviceConfigurator`
  "UNVERIFIED SDK CALL" reflection paths and decide whether `deviceItemName` is meaningful for
  `configure_network_device`.

## Testing gaps to close alongside

- The fake-worker executable test harness now covers the timeout path and persistent-worker restart logic
  through `OpennessWorkerClientIntegrationTests` — DONE 2026-07-16. Stderr propagation, malformed JSON,
  and Win32Exception launch failure are also covered.
- Batch validation aggregation (0.2) and unknown-property rejection (1.6) are pure-logic → plain xunit.
- The 146 existing tests are contract/formatting tests; none exercise a worker process.

## Suggested sequencing

Phase 0 → 1.2 + 1.6 (the two false-success traps) → 2.2 (one-line concurrency guard) → rest of
Phase 1 → 2.1 persistent worker → 2.3 payload bounds → 2.4 tool collapse → Phase 3 opportunistically
alongside whichever files each phase already touches.
## Session binding now protects the default case — DONE 2026-07-20 (Round 4)

The chosen fix binds a session from the worker-reported active-project path after its first
successful call; the worker report is the ground truth. For project-scoped operations, once bound, a
call that names a different `projectPath` is rejected unless `open_project` uses `forceRebind=true`.

Read-side project-open policy completes the other half of the fix for project-scoped read paths: they
will not switch the project currently open in TIA Portal. They return: "TIA Portal currently has
project 'A' open, but this request targets 'B'. Read operations never switch projects. Omit
projectPath to use the open project, or call open_project to switch." `get_project_status(projectPath)`
is a known exception deferred to Round 5 because its lifecycle RPC also serves guarded write-state
probes; do not use it to switch projects. Use `open_project` for a deliberate session switch.

## Read-side project-open policy — DONE 2026-07-20 (Round 4)

Project-scoped read paths use the read-side open policy. A request targeting a project other than the
one open in TIA Portal is refused; callers omit `projectPath` to use the active project or use
`open_project` to switch. `get_project_status(projectPath)` is the known deferred exception: it still
uses the lifecycle RPC shared with guarded write-state probes and can request the supplied path. Do
not rely on it for session switching; use `open_project` instead. Splitting the user-facing status
read from the write-side probe remains a Round 5 task.






### Smaller follow-ups from the same review

- **No test for the declined-bind warning branch** in `OpennessWorkerClient`. Reaching it needs a
  controlled interleaving, not a race: add a `delay` FakeWorker scenario, start the call, and bind
  from the test thread while the await is provably pending. `ProjectSessionBinding` is `sealed` with
  non-virtual methods, so the test-double route is closed without touching production code.
- **`ProjectPathNormalization`'s exception fallback is untested**, and being `internal` it is now
  harder to reach directly. Note that `Path.GetFullPath`'s throwing behaviour differs between net48
  (where the worker runs) and net8.0 (where the tests run), so a net8.0 test cannot characterise what
  the worker actually does with an odd path.
- **FakeWorker scenario keys are literal Windows paths** (`TiaMcpServer.FakeWorker/Program.cs:286,293`)
  because `projectPath` doubles as the dispatch key. A separate `scenarioKey` field would age better.
- **`Stamp`'s comment claims its stderr write lands in this response's warnings**, which depends on
  drain timing — the same assumption the containment above would rest on. Verify or soften.
- **Duplicate tests in `ProjectSessionBindingTests.cs`** (`FirstExplicitProjectPathResolvesWithoutBindingTheSession`
  vs `TryResolve_DoesNotAdoptTheRequestedPath`; `DifferentProjectPathIsRejectedAfterBinding` vs
  `TryResolve_StillRejectsADifferentPathOnceBound`) — inherited from the plan's mandated test set.
- **`TiaJson.cs`** — the static-constructor comment duplicates the field's XML-doc rationale.
- **`GetStatus`'s graceful `IsOpen=false` branch** looks unreachable: `EnsureProject` throws first.
  Predates Round 4; will be revisited by the read-tool/write-probe split above.

### Process note

The `get_project_status` route was missed because Task 5's scope was defined by *mechanism*
("operations going through `WithProject`") rather than by *capability* ("everything that can cause a
project to open"). Two of Round 4's most serious findings — this one and the divergence
mis-specification above — were only visible from a whole-branch view; neither could have been caught
by reviewing a single task's diff. Worth budgeting for a broad review pass on any change that spans
a process boundary.

## CI and coverage foundation — DONE 2026-07-23 (Phase 5 Plan 1)

Solution builds are serialized (`-m:1`), a scoped coverage gate is wired into CI (collect → enforce
locally at `>= 0.80` → Codecov upload, reporting-only), and all three are pinned by named tests in
`CiWorkflowTests`/`CoverageThresholdScriptTests`.

A gap was found and fixed mid-implementation: `TiaMcpServer.Tests` has no `ProjectReference` to
`TiaMcpServer` (deliberate — a normal reference would drag `TiaMcpServer.csproj`'s `BeforeTargets="Build"`
hook into building the net48 Openness worker against real `Siemens.Engineering*.dll`). Host code
instead reaches the test project via `<Compile Include>` and compiles into the `TiaMcpServer.Tests`
module, so assembly-scoped Coverlet filters (`[TiaMcpServer]*`) could never see it — the real Cobertura
report contained only `TiaMcpServer.Contracts` (92%), while all host logic was silently swallowed by the
`[TiaMcpServer.Tests]*` exclude. Fixed with namespace-scoped filters instead (`[TiaMcpServer.Tests]TiaMcpServer.*`
include, carving back out `TiaMcpServer.Tests.*` and `TiaMcpServer.OpennessWorker.*`) plus
`IncludeTestAssembly=true` (Coverlet defaults this `false`, which alone would have made the filter
change a no-op). New aggregate: 0.836.

### Follow-up architectural task (flagged by the final whole-branch review, not fixed in Plan 1)

The fix above is correct today but couples the coverage gate's meaning to a namespace-naming
convention rather than assembly identity: a future host class placed outside the `TiaMcpServer.*`
namespace would be silently excluded from coverage (undercounting real code, potentially masking a
genuine regression without failing any test), and a test-support class placed outside
`TiaMcpServer.Tests.*` would be silently counted as production. The durable fix is a real
`ProjectReference` from `TiaMcpServer.Tests` to `TiaMcpServer` with a build path that stays stub-safe
(doesn't force the net48 Openness worker's real Siemens-DLL build during `dotnet test`). Scope that as
its own task rather than folding it into a later phase's unrelated work. A cheap interim guard in the
meantime: a test that fails if any instrumented type in the test assembly matching include-minus-exclude
is itself a `[Fact]`/`[Theory]`-bearing class, turning silent scope drift into a loud one.

### Smaller follow-ups from the same review

- **`ReadRunCommandBlocks` (`CiWorkflowTests.cs`) misses inline `- run: <command>` YAML shorthand** —
  the key-position guard rejects any line where text before `run:` is non-blank, which includes the
  `- ` array-item marker. A solution build written in that shorthand (no sibling `name:`) would escape
  the `-m:1` enforcement entirely. Not used by any current workflow; widen the guard to also accept a
  lone leading `- ` if that style is ever introduced.
- **`GetRepositoryRoot()` is duplicated verbatim** between `CiWorkflowTests.cs` and
  `CoverageThresholdScriptTests.cs`, and its 4-level `../` walk assumes the standard
  `bin/<config>/<tfm>/` output layout. Extract to one shared test helper.
- **The threshold script accepts `NaN`/`Infinity` as a passing line-rate** (`NaN -lt $min` is `false`
  in .NET) — unreachable with real Cobertura output, but an explicit `IsNaN`/`IsInfinity` guard is
  cheap insurance.
- **`CoverageThresholdScriptTests.RunScript` reads stdout then stderr with sequential `ReadToEnd()`
  before `WaitForExit()`** — deadlock-prone in theory if either buffer fills; harmless today given the
  script's one-line output. Switch to async reads if the script's output ever grows.
- Status/error message interpolation in the threshold script isn't invariant-culture (cosmetic; moot
  on GitHub's en-US-locale runners).
- Worst-covered (0% line-rate) host classes, now visible now for the first time thanks to the fix
  above, noted as a possible future test-writing target: `EnvironmentVariableService`,
  `FileSystemService`, `ProcessEnumerationService`, `RegistryService`, `WindowsIdentityService` (thin
  OS-adapter classes).

## Lifecycle and response integrity — DONE 2026-07-23 (Phase 5 Plan 2)

Closed the `WorkerFailureCategories` vocabulary (`validation_error`, `binding_conflict`, `state_changed`,
`worker_operation_failed`, `worker_timeout`, `worker_crashed`, `postcondition_failed`) across every
guarded write path; split the user-facing `get_project_status` read from the internal
`probe_project_status_for_lifecycle` write-state probe so status reads are side-effect-free and never
open or switch projects; introduced an explicit `BindingTransition` model so session binding only
adopts worker-reported ground truth, never caller input; fixed a `save_project_as` divergence where a
successful SaveAs could leave the host and worker bound to different projects; and made
`save_project_as(rebind:false)` a rejected `validation_error` before any preview, token, or Siemens
call. Automated gates pass: full suite green at the Plan 2 tip (branch
`fix/phase5-02-lifecycle-response-integrity`, commit `66cce7b`), 506/506.

**Certification evidence recorded:** Task 2 of the Phase 5 Plan 4 certification plan recorded live
TIA Portal V21 evidence for the externally observable lifecycle cases. The internal-only timeout,
crash, and null-binding cases remain primarily covered by the automated FakeWorker suite; the
acceptance report records each scope boundary explicitly.

## PLC block-write repairs — DONE 2026-07-25 (Phase 5 Plan 3 + block-write-format-repair follow-up)

Plan 3 (`docs/superpowers/plans/2026-07-23-phase5-03-plc-block-write-repairs.md`) added block-bundle
parsing/staging validation (reject missing/duplicate documents, unsafe filenames, path traversal),
postcondition verification for `update_block_logic` and `create_block` (compile/re-export checks
instead of trusting Siemens' import return value), and SCL source generation. Automated gates passed
at the Plan 3 tip (branch `codex/phase5-03-plc-block-write-repairs`, commit `e65dc64`), full suite
582/582.

Live manual testing on 2026-07-25 (see `priv/MCP_TOOL_TEST_REPORT_2026-07-25.md`) found Plan 3's
postcondition checks did not catch a real corruption bug: `BlockExporter.Export()` omitted a newline
before each `--- FILE: ... ---` marker after the first, so `BlockImportBundleParser`'s
multiline-anchored delimiter regex could not see any delimiter past the first, and any multi-document
block round trip silently corrupted. Also found `create_block` failed outright for `language=SCL`
(schema-invalid template) and had no working input for `blockType=GlobalDB`.

The 2026-07-25 block-write-format-repair follow-up plan
(`docs/superpowers/plans/2026-07-25-block-write-format-repair.md`, Tasks 1-7, commits `d105dfb`..`81b73fc`)
fixed all three: `BlockBundleFormat.Compose` now guarantees a newline before every marker after the
first; block-document import routes Simatic ML XML through `BlockImportRouting` with a
non-authoritative-document guard; `GlobalDB` creation now defaults to/requires `language="DB"`;
SCL/STL compile units use a schema-valid empty `<NetworkSource />` instead of a raw-text
`StructuredText` node. Automated gates pass: full suite 615/615 at commit `81b73fc`.

**Certification evidence recorded:** Task 2 of the Phase 5 Plan 4 certification plan confirmed the
authoritative-document byte-identical and edited `update_block_logic` paths, malformed-bundle
non-mutation, and SCL `create_block` resolution/compilation on TIA Portal V21. The report retains
the exact scope caveats for non-authoritative document companions and unexercised block types; those
are evidence boundaries, not known unresolved product defects. README now documents the verified
recovery behavior rather than carrying a stale pending-live caveat.

## Phase 5 certification documentation — DONE 2026-07-25 (Phase 5 Plan 4 Tasks 1–4)

The repository documentation and the authorized source `tia-portal-mcp` skill now describe the
ten-tool public surface, self-previewing lifecycle writes, non-binding status reads, required
`save_project_as(rebind:true)`, categorized failures, separate warnings, and verified block-write
behavior. The installed plugin cache was not modified. The Phase 5 exit still requires the Plan 4
graph/review and final automated acceptance gates; Phase 6 exclusions below remain unchanged.

## Complete read-only project metadata for get_project_status - DONE 2026-08-08

`get_project_status` now returns the extended read-only project metadata surface on TIA Portal V21.
`ProjectStatusInfo.Metadata` is additive and backward compatible (absent when no project is open),
populated only by the direct read path: the write-side lifecycle probe payloads and their
safety-token binding are byte-for-byte unchanged.

New fields: `copyright`, `family`, multilingual `comment` (all translations, culture name per
translation, source order preserved), `languageSettings` (`languages` / `activeLanguages` as
culture names, nullable `editingLanguage` / `referenceLanguage`), `historyEntries` (text and
date-time, verbatim, no dedup, deterministically capped at 200 with an explicit
`historyTruncated` flag), `usedProducts` (`{name, version}`, no inference or silent dedup), and
`compilationSettings` (`isCompilationEnabled` reads of `PlcSimulationSettingsProvider` and
`VirtualPlcSettingsProvider` via `GetService<T>()`). An unavailable provider/value degrades to null
with a response warning rather than a fabricated default; only `EngineeringException` is caught for
degradation; unrelated errors fail normally. Read-only enforced by source contract: the reader
never saves, sets attributes, deletes, opens, closes, or uses `ExclusiveAccess`, and compile/build
pass both against the CI stubs (`/p:UseTiaPortalReferenceStubs=true`) and the real V21 assemblies.
Full suite green at this tip: 1980/1980.

Review fixes (PR #24, review 4892632840): `historyTruncated` is now tri-state — `false` (read,
below cap), `true` (read, capped), `null` (history unavailable/could not be read); the history
reader `break`s as soon as the first entry beyond the cap is detected instead of enumerating the
rest; the successful `get_project_status` response is capped by the existing standalone 60000
character budget with an explicit truncation marker; and lifecycle post-write verification now uses
a narrowly-scoped internal basic-status read (`get_basic_project_status` /
`GetBasicStatusReadOnly`) so `open_project`, `create_project`, `save_project`, `save_project_as`,
and `archive_project` never perform the extended metadata read after a write. The history cap was
lowered from 1000 to 200 entries so a full history payload stays well inside the response budget.

## Structured read-only PLC I/O map for read_hardware_config - DONE 2026-08-13

`read_hardware_config` now supports opt-in, read-only structured I/O extraction. A default read
(with no flags) is byte-identical to earlier versions: `DeviceItemInfo.IoDetails` is
`JsonIgnore(WhenWritingNull)` so it is absent from default output and from internal network-write
safety-token state hashes, which stay lightweight and unchanged.

New request options: `deviceName` (ordinal-ignore-case, exactly-one-match device filter), `plcName`
(exact ordinal PLC selection for tag matching), `includeIoDetails` (structured `ioDetails` with
addresses and channels), and `includeTagMatches` (requires `includeIoDetails`). New Contracts DTOs
`DeviceItemIoDetailsInfo`/`IoAddressInfo`/`IoChannelInfo`/`IoTagMatchInfo` carry raw Openness
evidence: byte-based address `startAddress`/`length`, bit-based channel
`channelAddressBits`/`channelWidthBits`, dynamic `context`, and ordinal `controllerNames`;
unreadable scalars stay null and are never fabricated. Pure TIA-free `IoLogicalAddressFormatter`
(bit/byte/word/dword parse + format with strict alignment and casing/whitespace normalization,
rejecting `%M`/DB/symbolic-only) and `IoTagMatcher` (exact normalized interval + I/O-area equality,
no overlap or first-match fallback, multiple tags per channel) live in `TiaMcpServer.Contracts`.

The worker reads `DeviceItem.Addresses`/`Channels` and `AddressControllers` through the typed
Openness API (present in the CI stubs), reads the dynamic `ChannelAddress`/`ChannelWidth`/`Context`
attributes through guarded `GetAttribute`, resolves the PLC deterministically (exact `plcName`, or
the single PLC when omitted), builds the tag index once per selected PLC via `TagTableReader`, and
matches tags only when the controller association deterministically names the selected PLC — never
across controllers. The payload contract validates every non-null collection and nested object
inside `ioDetails` and rejects an explicit null collection as `protocol_error` without echoing the
payload. FakeWorker scenarios, a separately authorized read-only live acceptance run, and the full
I/O-map test surface were added. Full suite green at this tip: 2136/2136.

## Opt-in hardware configuration pagination - DONE 2026-08-29

`read_hardware_config` now has an opt-in paged path selected only by `pageSize` or `cursor`; the
unpaged canonical request, worker method, and output remain unchanged. The public sequence counts
stable devices followed by stable subnets, with separate arrays and exact totals. Host-process
HMAC cursors bind filters, resolved project, bound/unbound host snapshot, worker session, candidate
snapshot, and offset; continuation calls reuse the envelope-only worker session identity and can
remain explicitly unbound.

The worker enumerates the complete descriptor set before materializing a bounded window. The host
strictly decodes that typed evidence and projects the largest complete canonical prefix at or below
60,000 characters; the ordinary 180,000-character batch budget remains independent. Oversized
diagnostics or a first oversized entity become bounded omissions with no offset advance. Entity
subjects are all-or-nothing: the full approved subject is retained only when it fits.

Offline contract and FakeWorker coverage includes exact reconstruction, duplicate names, nested
diagnostics, trimming/resume behavior, cursor tamper/filter/binding/session/snapshot/offset
failures, malformed worker payloads, process-key invalidation, and byte-for-byte unpaged
equivalence. A separately authorized read-only acceptance procedure was contract-tested but was
not executed against live TIA Portal V21 before its removal; live pagination acceptance remains
unverified.

## PR 6 project-tree safety scopes — mandatory live acceptance completed (2026-09-06)

Typed exact project-tree selectors now cover `create_block`, `create_block_group`, and
`delete_block_group`. They preserve PLC-global versus Software Unit ownership, validate canonical
parents and ancestors, bind only relevant requested-name occupancy, include authoritative XML for
occupied or descendant blocks, and fail closed on malformed or conflicting worker payloads.
Identical selectors share a read only within one preview or apply phase and expand back into the
original operation order; apply always reads fresh state under the pinned project binding.

The
[guarded live acceptance report](superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md)
records successful Inventory, Preview, and authorized Apply/restoration/compile evidence against
the exact startup-bound TIA Portal V21 project for both PLC-global and Software Unit fixtures.
Occupied-block content, descendant-block content, requested-name occupancy, and relevant
descendant membership drift rejected stale tokens with `state_changed`; unrelated sibling-tree
drift preserved the original token. Six restoration hash pairs per owner matched, all 52
record-level byte comparisons per owner passed before compile, and six compile checks per owner
reported `Success` with 0 errors and 0 warnings.

Repository-auditable contracts, implementation, tests, harness, and this report remain distinct
from the redacted, git-ignored live observations. No save or persistence behavior, plant
acceptance, or physical-hardware commissioning is claimed.

Remaining deferrals are unchanged:

- Broader snapshot narrowing: unchanged and out of scope.
- `start_plc` / `stop_plc`: unchanged and out of scope.

## PR 5 tag-operation safety scopes — mandatory live acceptance completed (2026-09-05)

Typed exact selectors now cover all eight tag/table/user-constant write operations with relevant
cross-kind/name/address collision probes, phase-local read deduplication and ordered expansion,
fresh apply reads, and no cross-phase cache. Focused tests passed 83/83, current-state FakeWorker
tests 24/24, harness contracts 7/7, the full offline suite 2691/2691, and both stub and real V21
builds completed with 0 warnings/errors.

The [guarded live acceptance report](superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md)
records successful `PreviewOnly`, `DriftAndRestore`, and `ApplyAndRestore` modes against the exact
disposable copy. Same-object and relevant name/address collision drift rejected stale tokens with
`state_changed`; unrelated sibling drift preserved the original target token; one unchanged-token
apply succeeded and observed `heaterStages=4`; no-save discard preserved the saved baseline; and
the clean source project was restored open. The initial sandbox visibility failure and two
deterministic lifecycle binding conflicts occurred before mutation and were not uncertain writes.

Deferred scope remains explicit: multilingual per-tag comment binding; public `list_tag_tables`
completeness changes; broader snapshot narrowing; Software Unit namespace-aware collisions; and
PLC `start_plc` / `stop_plc` safety work. This bounded evidence is not plant acceptance.

## PR 3 update_tag exact safety snapshot — mandatory live acceptance completed (2026-09-05)

The missing external-flag safety-state gap for `update_tag` is closed in the offline contract,
worker, FakeWorker, and registered write path: a strict exact-target snapshot captures resolved
PLC/table/tag identity and mutable values, requires the observed worker session identity, and
rejects a requested unreadable external flag before a preview token can be issued. The legacy
best-effort public `list_tag_tables` response is intentionally unchanged until PR 5.

The guarded disposable-copy run is recorded in the
[`PR 3 update_tag safety snapshot acceptance report`](superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md).
The mandatory live V21 read, preview, authorized `ExternalVisible` flag-only drift, stale-token
`state_changed` rejection, and restoration all passed. The optional unavailable-target probe was
`NOT RUN` because no distinct target exposing an unavailable flag was established. The flag was
restored to its original value, but TIA retained `isModified = true`; no save, close, or discard
was performed.
PR 5 selector narrowing, multilingual per-tag comment binding, and PLC `start_plc`/`stop_plc`
work remain deferred.

## PR 1 explicit MCP tool annotations — live acceptance completed (2026-09-01)

The registered 4-tool read-only and 14-tool read-write MCP surfaces now expose the approved
client-facing write metadata, with the server-enforced preview/token/apply safety model unchanged.
The separately authorized, non-mutating host-level harness completed with exit code `0` against
the current-branch read-only and read-write hosts. Its live evidence is recorded at
[`docs/superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md`](superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md).

This completion applies only to the exact host, attached TIA Portal session, and disposable
project documented in that report. Offline, stub, FakeWorker, and static harness-contract evidence
remain separate development evidence; neither evidence class substitutes for the other. PLC
`start_plc` and `stop_plc` remain deferred and out of scope.

## PR 2 registered-tool delegation — live acceptance completed (2026-09-01)

`Program.cs` now registers the split `ProjectReadTools`, `ReadBatchTools`,
`ProjectWriteTools`, and `WriteBatchTools` owners directly. The legacy
`ProjectLifecycleTools` and `BatchTools` types remain compatibility wrappers only.
The separately authorized preview-only harness completed against the real TIA Portal V21 host,
proving that all nine required registered tool names were present, a successful
`execute_read_batch` baseline read, and token-bearing generic-batch and lifecycle previews without
applying either token. The harness filters `tools/list` to the required-name allowlist; this is not
a claim that the complete read-write host surface contains only nine tools. The
sanitized live evidence is recorded in the
[`PR 2 registered-tool delegation acceptance report`](superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md).

This completion does not authorize or complete compatibility-wrapper deletion. PLC `start_plc`
and `stop_plc` safety hardening also remains deferred. No apply, project save, PLC mode change,
production acceptance, or plant acceptance is claimed.

## PR 4 bounded structured preview diffs — live acceptance completed (2026-09-05)

The [PR 4 acceptance report](superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md)
records completed live Preview and authorized Apply/restore/compile acceptance against a disposable
TIA Portal V21 project. The first Apply target exposed TIA canonicalization from `T#1s` to `T#1S`:
its byte-identity check failed, and guarded no-save close/reopen cleanup recovered the clean disk
state. A read-only-selected UDT target then passed Preview, temporary Apply, restoration with
byte-identical public re-exports, and compile with zero errors and zero warnings across two PLCs;
final guarded no-save close/reopen cleanup returned the project to `isModified=False`.

The close/open lifecycle operations were cleanup and recovery only, not PR 4 feature acceptance.
The report does not claim plant or production acceptance, disk project-byte identity, saved-project
acceptance, or semantic equivalence beyond the exact exported-text checks performed.
