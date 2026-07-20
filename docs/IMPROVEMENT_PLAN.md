# Improvement Plan — tia-portal-mcp (2026-07-15)

Consolidated from three parallel reviews (C# correctness, silent-failure hunt, AI-agent usability audit)
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
| 3.5b | Inject `WriteSafetyService` via DI instead of the `.Shared` static (registration already exists in `Program.cs`) | host | **PROMOTED 2026-07-20 — fixes a real bug, see below.** Still costs threading the service through the static `WriteSafetyTooling` API and 6 MCP tool signatures |
| 3.6 | `WorkerRequest` god DTO (47 fields): defer full split — flat is a defensible MCP trade-off — but group with `#region` per operation family and add a comment mapping fields→operations | Contracts | documentation — DONE 2026-07-20 |

## Session binding does not protect the default case (found live 2026-07-20)

The session-binding guard is off in the workflow the server itself recommends. `tia-mcp doctor`
reports "No project binding configured. Tools will use the project currently open in TIA Portal" —
and in that state the guard never engages, because `ProjectSessionBinding.TryResolve(null)` returns
the (null) binding without adopting anything. A session that always omits `projectPath` stays
unbound indefinitely.

The first call that *does* pass an explicit `projectPath` is then adopted unconditionally, whatever
it is. Reproduced against a live V21 instance with `SimpleProject.ap21` open in the GUI:

1. `get_project_status` with no `projectPath` — succeeds, session still unbound.
2. `browse_project_tree` with `projectPath` pointing at an unrelated real project — accepted, and
   the worker attempted to **open that other project alongside the user's**, warning
   "Leaving user-opened project '…SimpleProject.ap21' open; opening '…LibReadTest.ap21' alongside it."
3. Only TIA Portal's own refusal stopped it: "Unable to open project … Another project is already
   open."

So the sole thing preventing a hallucinated or mistyped path from retargeting the session was an
external backstop. With no project open in the GUI, step 2 would have silently opened a different
project and operated on it. The failed attempt also left that session unable to issue write
previews; a fresh session recovered.

By contrast, once the session *is* explicitly bound, the guard works correctly and rejects before
attempting anything — verified live, including the unified wording from 3.2:
"This MCP session is already bound to project 'A' and cannot use 'B'. Call open_project with
forceRebind=true to rebind this session, or start a new MCP session for a different TIA project."

Candidate fixes (not yet chosen): adopt the active project's path as the binding after the first
successful call that resolved it — the path is already known, `get_project_status` returns it — or
require `forceRebind=true` before accepting any explicit path that differs from the project
currently open in the GUI. Note this is pre-existing behavior, unchanged by Phase 3.

## Found during live testing against TIA Portal V21 (2026-07-20)

**The test suite writes into the production audit trail.** Measured on a real machine:
39 of 42 records in `%LOCALAPPDATA%\TiaMcpServer\audit` were produced by `dotnet test`, not by
real TIA usage. `ProjectLifecycleTools` calls `WriteSafetyService.Shared.AppendAudit(...)` on the
static singleton; `TiaMcpServer.Tests` links that file and exercises those tools, so every test run
appends real records — recognizable by `projectPath` values pointing into `TiaMcpServer.Tests\bin\`
and FakeWorker scripted keywords (`ok`, `hang`, `worker-error`) in `target`.

This dilutes the forensic record for PLC-mutating operations to ~7% signal, and a test run could be
mistaken for real engineering activity. It is the concrete justification for **3.5b** above: the
`WriteSafetyService(getUtcNow, tokenLifetime, auditDirectory)` constructor already supports
redirecting the audit directory, but no test that goes through the tool layer can reach it while the
tools resolve `.Shared` statically. Fixing 3.5b lets the tests inject a temp directory.

Interim mitigation if 3.5b stays deferred: have the audit writer no-op when the process is a test
host, or point `WriteSafetyService.Shared` at a temp directory from a test fixture.

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
same "declared but never forwarded" bug class Phase 0.4 found with `newName`. Neither is fixed —
both need a decision, and the second needs the real Openness API to answer.

- **`deviceItemName` on `configure_network_device`**: `BatchOperationRequest.cs` describes it
  unscoped ("Optional device item name; defaults to deviceName when omitted"), but
  `ConfigureNetworkDeviceAsync` has no such parameter and `BatchWorkerInvoker` never passes one.
  Only `add_network_device` forwards it. An agent setting it on `configure_network_device` has it
  silently dropped. Fix is either scoping the description or forwarding the field.
- **`externalAccessible` / `externalVisible` / `externalWritable` / `isSafety` on `create_tag`**:
  described as generic "Optional tag attribute", but only `update_tag` forwards them. `create_tag`
  drops all four. Whether to forward them depends on whether Openness supports setting these at
  tag-creation time — needs verification on the TIA machine.

A catalog invariant test asserting every operation's declared fields are a subset of its forwarded
fields would make this class unrepresentable; that assertion is part of the deferred 3.3 design.

Three further follow-ups, all raised by the Phase 3 final review and deliberately left undone:

- **Enforce the `WorkerRequest` forwarding comments with a test.** The field→operation map can be
  re-derived deterministically in ~25 lines: walk `OpennessWorkerClient.cs`, extract every
  `request.X =` inside each `SendBoundProjectRequestAsync` lambda and every `new WorkerRequest`
  initializer, invert to field→operations, and assert it agrees with the doc comments. The test
  project already links that source file. This is cheaper than the 3.3 catalog invariant, does not
  depend on 3.3 landing, and would have caught the `deviceItemName` error above before review.
- **Collapse `BatchPayloadBudget.ReadBatchResponseLength` into `BatchResultFormatter.ReadBatch`.**
  It currently hand-mirrors that method's envelope purely to predict its output length. Phase 3
  made the two share `TiaJson.Presentation` so they cannot drift on serializer settings, but the
  duplicated envelope shape remains. Replacing the body with
  `BatchResultFormatter.ReadBatch(results).Length` removes it, at the cost of one extra
  serialization per budget probe — measure before adopting.
- **`TiaJson.Presentation.MakeReadOnly()`.** The field is a public mutable `JsonSerializerOptions`
  whose formatting feeds the safety-token `requestedInputHash`. Realistic harm is low (mutation only
  succeeds before first use, and preview/apply share the instance so tokens stay self-consistent),
  but a static constructor calling `MakeReadOnly()` turns the "keep this stable" comment into a
  guarantee.

## Deferred / explicitly not planned

- Splitting `WorkerRequest` into per-operation DTOs (churn > value while the protocol is stable).
- MCP protocol-level error signaling instead of text results (needs SDK investigation; revisit after 1.1).
- `NetworkDeviceConfigurator` speculative-reflection "UNVERIFIED SDK CALL" paths: verify against real
  Openness V21 API on the TIA machine and pin exact method signatures (needs hardware access).

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
