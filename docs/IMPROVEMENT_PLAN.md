# Improvement Plan — tia-portal-mcp (2026-07-15)

Consolidated from three parallel reviews (C# correctness, silent-failure hunt, AI-agent usability audit)
plus manual verification of the highest-stakes claims. Test suite: 146/146 green (pure-logic tests only;
no integration coverage of the worker or IPC layer).

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
| 0.1 | Unknown-operation error lists all valid names for the batch category | `Batch/BatchOperationCatalog.cs:144` | Most common dead-end for weak models; `CrossReferenceFilterNames.cs:43` already sets the precedent |
| 0.2 | Aggregate ALL batch validation errors instead of failing on the first | `BatchOperationCatalog.cs:77-112` | One round-trip fixes N mistakes instead of N round-trips |
| 0.3 | Append recovery instruction ("tokens are single-use, expire after 10 min; call preview_* again") to all 7 token-rejection texts; state the 10-min TTL in preview tool descriptions + README | `Safety/WriteSafetyService.cs:87-124`, `Batch/BatchTools.cs`, `Tools/ProjectLifecycleTools.cs`, README | Agent can self-recover without inference |
| 0.4 | Fix schema-facing description drift: `newName` falsely claims user-constant rename (`BatchOperationRequest.cs:47`, `BatchWorkerInvoker.cs:46` never forwards it — implement or remove the claim); `blockPath` omits `compile_check` scoping; `filter` lists 2 of 4 values and doesn't name its operation; `plcName` doesn't say which ops honor it | `Batch/BatchOperationRequest.cs` | The flat DTO makes descriptions the load-bearing contract |
| 0.5 | README drift: add `get_project_status` to batch read list (line 18); document `forceRebind` escape hatch; unify the two "already bound" error texts (`ProjectSessionBinding.cs:42` should mention forceRebind like `OpennessWorkerClient.cs:580` does) | README, Contracts | Three sources of truth currently disagree |
| 0.6 | Log audit-write failures to stderr/ILogger instead of bare `catch { }` | `Safety/WriteSafetyService.cs:164` | Silent loss of audit trail on a PLC-mutating tool |

## Phase 1 — Error propagation correctness (~2-4 days)

| # | Change | Where | Why |
|---|--------|-------|-----|
| 1.1 | Replace the `"Error:"` string-prefix convention with a structured result record (`Success`, `Payload`, `Error`) threaded through client → batch → safety → tools | `OpennessWorkerClient.cs:552`, `BatchExecutionEngine.cs:10`, `WriteSafetyTooling.cs:30,58,83`, `ProjectLifecycleTools.cs` (5 sites), `BatchTools.cs:139` | Any payload legitimately starting with "Error:" is misclassified; false-success bugs become structurally impossible |
| 1.2 | **False-success writes**: `NetworkDeviceCreator.cs:23-31` must fail the response when `CreateWithItem` throws (today: buried warning + `Success=true`, and batch stop-on-failure doesn't stop). `NetworkDeviceConfigurator.cs:71-74` must fail when ALL requested settings were skipped | worker Openness/ | CRITICAL: agent believes a device exists that doesn't; audit log records success |
| 1.3 | Surface worker stderr: attach non-empty stderr as `warnings` on `WorkerResponse` and/or log via host ILogger; route per-item "Skipping X" degradation messages into `messages` arrays on the payload DTOs (pattern already used correctly by `CrossReferenceReader` and `CompileChecker`) | `OpennessWorkerClient.cs:633-656`, ~20 catch sites in worker readers | `browse_project_tree`/`read_hardware_config` can silently return partial trees today |
| 1.4 | `HardwareConfigReader` fallback defaults (`0`/`""`/`null`) indistinguishable from real values → nullable + `messages` marker | `HardwareConfigReader.cs:260-297` | Agent can't tell "read failed" from "actual value" |
| 1.5 | Add `Win32Exception` to the client catch filters with an actionable message (missing .NET FX 4.8 / corrupt openness-worker folder); include exception type name in the worker's catch-all error; wrap `SaveProjectAsAsync` like its siblings | `OpennessWorkerClient.cs:392,431,556,630`, worker `Program.cs:86-90` | The one prerequisite failure that surfaces as a raw protocol error today |
| 1.6 | Reject unknown JSON properties on batch items (`JsonUnmappedMemberHandling.Disallow` or equivalent SDK option) | host serializer config | Misspelled OPTIONAL params (`ip_adress`) currently succeed silently — the most dangerous trap found |
| 1.7 | Narrow bare `catch (Exception)` in `EquipmentCatalogSearcher.cs` reflection helpers to `EngineeringException`/`TargetInvocationException`, merging with the existing `OpennessReflection` helpers | worker Openness/ | Bugs currently masquerade as empty search results |

## Phase 2 — Structural (the big wins; ~1-2 weeks)

| # | Change | Where | Why |
|---|--------|-------|-----|
| 2.1 | **Persistent worker process**: keep the worker alive across requests (request loop already exists in worker `Program.cs:34` — the client kills it by closing stdin at `OpennessWorkerClient.cs:635`). Single `Attach()`, managed project open/close, health check + restart-on-crash, `SemaphoreSlim` serialization of requests | `OpennessWorkerClient.cs` | Fixes all 3 CRITICALs at once (re-attach per call, leaked project handles, concurrent mutation races); cuts preview→apply wall-clock far below the 10-min token TTL; makes 50-item batches practical (today: up to 3N spawns per write batch) |
| 2.2 | Interim (if 2.1 is deferred): add `SemaphoreSlim(1,1)` around `SendAsync` now | `OpennessWorkerClient.cs:614` | One-line mitigation for the concurrency CRITICAL |
| 2.3 | **Bound read payloads**: `depth`/`startPath` on `browse_project_tree`, `maxResults` on `search_equipment_catalog` + `read_cross_references`, plus a server-side byte budget with an explicit "truncated — narrow with plcName/filter/startPath" trailer | worker readers + batch schema | Only finding that can hard-kill a small model's session (README's own smoke test batches tree+hw+xref+catalog into one call) |
| 2.4 | Collapse lifecycle preview/apply pairs: calling a write tool WITHOUT a token returns the preview + token instead of an error → 16 tools become 10 | `Tools/ProjectLifecycleTools.cs` | Removes the "which preview matches this apply" lookup; kills the asymmetric-naming trap (`preview_write_batch`/`apply_write_batch` vs `preview_open_project`/`open_project`) |
| 2.5 | Evict expired tokens (sweep on `CreatePreview` is enough — no timer needed); validate token BEFORE the expensive N-spawn state re-read in apply | `WriteSafetyService.cs:16-36`, `BatchTools.cs:94-110` | Unbounded memory growth; dead tokens currently cost a full read pass |

## Phase 3 — Simplification (behavior-preserving refactors)

| # | Change | Where | Est. reduction |
|---|--------|-------|----------------|
| 3.1 | Extract per-op descriptor + one generic executor for the six lifecycle preview/apply pairs (today the `target`/`requestedInput` objects are hand-built TWICE per op and must stay byte-identical or the token hash breaks) | `ProjectLifecycleTools.cs` (374 lines) | ~200 lines; removes a latent drift bomb (partially subsumed by 2.4) |
| 3.2 | Consolidate the 3 near-identical project-path binding checks into `ProjectSessionBinding` | `OpennessWorkerClient.cs:562-582` vs `ProjectSessionBinding.cs:16-68` | drift risk |
| 3.3 | Collapse the double dispatch: `BatchWorkerInvoker` maps operation strings onto 20+ near-identical `OpennessWorkerClient` wrappers that only set `WorkerRequest` fields — build `WorkerRequest` directly from the batch item | `OpennessWorkerClient.cs:25-359`, `BatchWorkerInvoker.cs` | ~250 lines |
| 3.4 | Merge `EquipmentCatalogSearcher`'s private reflection helpers (~90 lines) into `OpennessReflection` | worker Openness/ | ~90 lines |
| 3.5 | Single shared `JsonSerializerOptions` (currently duplicated in 4 files); inject `WriteSafetyService` via DI instead of `.Shared` static (registration already exists in `Program.cs:18`) | host | consistency |
| 3.6 | `WorkerRequest` god DTO (40+ fields, 28 methods): defer full split — flat is a defensible MCP trade-off — but group with `#region` per operation family and add a comment mapping fields→operations | Contracts | documentation |

## Deferred / explicitly not planned

- Splitting `WorkerRequest` into per-operation DTOs (churn > value while the protocol is stable).
- MCP protocol-level error signaling instead of text results (needs SDK investigation; revisit after 1.1).
- `NetworkDeviceConfigurator` speculative-reflection "UNVERIFIED SDK CALL" paths: verify against real
  Openness V21 API on the TIA machine and pin exact method signatures (needs hardware access).

## Testing gaps to close alongside

- Zero integration coverage of `OpennessWorkerClient` ↔ worker IPC. Add a **fake worker executable**
  test harness (echoes scripted JSON) to cover: timeout path, stderr propagation, malformed JSON,
  Win32Exception launch failure, persistent-worker restart logic (once 2.1 lands).
- Batch validation aggregation (0.2) and unknown-property rejection (1.6) are pure-logic → plain xunit.
- The 146 existing tests are contract/formatting tests; none exercise a worker process.

## Suggested sequencing

Phase 0 → 1.2 + 1.6 (the two false-success traps) → 2.2 (one-line concurrency guard) → rest of
Phase 1 → 2.1 persistent worker → 2.3 payload bounds → 2.4 tool collapse → Phase 3 opportunistically
alongside whichever files each phase already touches.
