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

## Open: Multiuser successor operations

PR 2 implements the internal active project context and typed lifecycle owners on the merged
PR 1 foundation. Its frozen standalone candidate is offline qualified and live accepted within
the bounds in the [acceptance report](superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md).
PR 3 implements six explicit discovery/inventory actions through `bind_project` without a new
tool or binding mutation. Its combined offline qualification is recorded in the
[PR 3 report](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md).
PR 3 live acceptance was subsequently finished and passed by maintainer decision on 2026-10-06;
the [live report](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
preserves executed observations and non-blocking unexecuted cases. Successor Multiuser operations remain open.
The [Multiuser boundary](SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md) distinguishes
this partial delivery from Issue #65 completion. Public `.als21` selection/open and local-session
lifecycle operations remain later work. Preserve
`bind_project`, host binding ID/revision, configured-selector checks, detach ownership, guarded
lifecycle confirmation, and audit v2. Capability descriptions remain descriptive, not permission.

## Open: totally-integrated-claude tia-portal-mcp skill migration

Update the plugin's source `tia-portal-mcp` skill in a separately authorized plugin change to teach
6/16/16 tool counts (`plc_read` and `read_cross_references` replace the retired `execute_read_batch`), `bind_project` for already-open projects, no implicit opens, per-call
read-write lifecycle confirmation, full policy confirmation, removed startup switch/agent
confirmation arguments, audit v2, and binding-scoped tree cursors. Preserve the distinction between
functional live evidence and human dialog observations. This repository task changes no installed
plugin cache or user configuration.

## Open: PLC read and cross-reference follow-ups (PR A)

Found while finishing [PR A](superpowers/plans/2026-10-05-plc-read-and-cross-references.md); none blocks it.

- Network: `PlcSoftwareLocator.FindAll` (used by Network) never sees PLCs in device groups; `FindEveryPlc` does.
- `totally-integrated-claude` skill: update for `plc_read` / `read_cross_references`, the retired `execute_read_batch`, and counts 6/16/16.
- `list_tag_tables`: when `tableName`/`folderPath` match nothing but part of the tree was unreadable, the incomplete inventory is returned without an explicit "not found among readable tables" message.
- `read_cross_references` budget: the trim never recurses inside a kept child; a bare source over budget (pathological names or messages) is still withheld by the renderer; the standalone fallback guidance omits `filter`.
- `read_cross_references`: a `maxResults` cut does not count into `omittedSourceCount`; the shared incomplete message mentions "unused-object audit" for every filter.
- `read_cross_references`: legacy `referencedAsName` is kept beside `referencedAs` (it carries call-site text when `referencedAs` is null); decide whether to keep it.
- The Plc protocol diagnostic duplicates Network's private `TryWriteProtocolDiagnostic` (missing `validators` field, 512 cap); share one helper.
- `CrossReferenceTargetResolver.MatchOne` duplicates `ProjectTreeFilter.ResolveOne`'s predicate (drift is pinned by a round-trip test).
- Unverified cross-reference owner kinds (spec Appendix B): technology objects and Unified `HmiTag` are verified but not shipped; TO instance DBs and Classic HMI tags were not present in the spike project.

## Open: Deeper project-tree resolver optimization (Issue #32 follow-up)

The user accepted the combined offline and read-only live v3 evidence on 2026-09-12, with an
explicit live ambiguity waiver. The
[accepted report](superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md)
records the timing distributions, multi-page completion, evidence boundaries, and waiver.

The shipped v3 initial browse performs one net48 typed/scoped walk, then strict host decode,
post-filter flattening, bounded immutable snapshot storage, and canonical page projection;
continuations use the authenticated process-local cursor and cached snapshot with zero worker calls.
A deeper direct Openness selector resolver and depth-pruned walk is a measured follow-up only. It is
not shipped behavior and should proceed only if additional measurements justify its benefit without
changing the approved public contract. Acceptance of the current implementation does not complete
this optimization.

## Open: Network Phase 5 public IO-system editing

PR 2's 2026-09-26 guarded worker-only V21 matrix verified exact PN/DP controller interface
owners, their distinct master PLC hardware compile targets, baseline compiles, and one-field
edit/restoration evidence. The [qualified contract](superpowers/specs/2026-09-24-network-phase5-qualified-contract.md)
admits PN/DP `Name` and `Number` plus PN `UseIoSystemNameAsDeviceNameExtension` for later
public planning. PN `MultipleUseIoSystem` remains excluded after linked PN-name evidence became
unavailable following a committed edit; `MaxNumberIWlanLinksPerSegment` remains unproved.
The [acceptance report](superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md)
records two reverse `Name` runs whose worker edit/compile succeeded but whose final harness
status failed solely on transient project-size drift, and the clean close-without-save/reopen
result. No failed hardware compile was induced. `update_io_system` remains unshipped. The next
public slice still needs a fresh plan from merged `main`, strict TDD, the canonical safety and
audit gate, and its own live public-path acceptance.
The live Portal matrix ran at `03bf387`, not at the later branch HEAD. After
the post-live host/test project-reference and CI changes, no `.cs` or `.ps1`
file changed; the rebuilt dependency manifest still matched, although five
of 53 core PE files differed. The user accepted the changes as having no live
impact. A fresh full coverage run passed 3,371/3,371 at line rate 0.9409
against the 0.80 threshold, and both serial builds passed with zero errors
and seven existing warnings. PR 2 is merge-ready by the explicit
source/behavioral-equivalence carry-forward, not by binary identity or a
current-HEAD Portal rerun. No merge was performed.

## Open: JSON contract Phase 1b follow-ups

These came out of the Phase 1b final review ([plan](superpowers/plans/2026-09-28-json-contract-phase1b-required-members.md)).
None of them blocks the merge.

- **Tag-match null path (predates Phase 1b).** `TagTableReader` → `HardwareTagIndexResolver` →
  `HardwareIoMapReader` pass Openness strings unguarded into the non-nullable `IoTagMatchInfo`
  members `Name`, `DataType`, and `TableName`. A null from Openness makes the whole
  `read_hardware_config` result `protocol_error`. That fails closed, as before the branch. A worker
  fallback with a message would keep the read usable.
- **Worker version mismatch.** The host now requires the network payloads to write explicit
  nulls, so the payloads of a worker built before Phase 1b are rejected as `protocol_error`. Host
  and worker ship in one package, so this fails safe. An optional protocol capability would turn
  it into an explicit version-mismatch message.
- **`update_subnet` null or blank read-back name.** If Openness reads back no name after the update
  has committed, the host rejects the result as `protocol_error`. `postcondition_failed` would
  describe it better, but that is new behavior and was kept out of Phase 1b.
- **File sizes.** `TiaMcpServer/Network/NetworkPayloadContract.cs` is 831 lines and
  `TiaMcpServer.Tests/Network/NetworkPayloadContractTests.cs` is 1281 lines. The test file repeats
  the full 16-member selector and 19-member evidence objects about 20 times.
- **Test precision.** The five tag-match rejection cases in `NetworkIoMapPayloadContractTests` have
  no accepted control. `NetworkPayloadContractTests.Rules.cs` asserts that the validator appears
  anywhere in the diagnostic chain; asserting `chain[0]` would be tighter.

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
| 2.3 | **Bound read payloads**: `depth`/`startPath` on the historical v2 `browse_project_tree`, `maxResults` on `search_equipment_catalog` + `read_cross_references`, plus a server-side byte budget with an explicit "truncated — narrow with plcName/filter/startPath" trailer | worker readers + batch schema | Only finding that can hard-kill a small model's session (README's own smoke test batches tree+hw+xref+catalog into one call) — DONE 2026-07-16. The `startPath` input and truncation-trailer response were removed by the v3.0.0 project-tree cutover. |
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


Also confirmed live for the historical v2 surface, all working as designed: bounded reads with `depth`/`startPath`/`maxResults`
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

## Project-tree v3 public cutover — offline implementation completed (2026-09-08)

The sole public `browse_project_tree` surface now uses the v3.0 canonical envelope, typed
`startSelector`, flat parent-first nodes, complete-node pagination, authenticated process-local
cursors, and bounded immutable snapshots. The v2 bare nested array, `startPath`, project-tree
`deviceName`, and `details.Path` contracts were removed atomically. Cursor-free calls make one typed
worker observation; continuations use the cached snapshot and make zero worker calls.

Offline tests cover strict input and worker decoding, selector ambiguity and failures, canonical
text/structured equality, snapshot/cursor authentication and bounds, page projection, public schema,
and legacy rejection. The maintained read-only PowerShell harness also has a static source contract
and parser gate. At the time of this offline entry, the harness had not been executed and no live
TIA Portal project had been opened or attached for it. The subsequent accepted live result is
recorded separately below; the deeper resolver optimization remains open near the top of this log.

## Project-tree v3 read-only live acceptance — DONE 2026-09-12 (Issue #32)

The user accepted the [combined offline and live evidence](superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md):
Fixture A's selected-Device median improved by 54.44%, and Fixture B supplied additional 62.57%
median-reduction evidence plus complete 890-node full and 804-node selected trees over five pages
each. Eight live continuations, canonical/size/structure checks, deep-leaf equivalence, and exact
missing/invalid selector categories passed. The accepted one-initial-worker/zero-continuation-worker
requirement combines offline integration/source proof with observed live paging, not live IPC counts.

The live `target_ambiguous` check was explicitly WAIVED, not passed: the user's acceptance ruling
recognizes that TIA name uniqueness prevents the required duplicate-name fixture through normal
engineering. Raw harness failures at that fixture guard and the first late-approval timeout remain
in private ignored evidence. Project and selector identities are omitted from tracked documentation
at the user's request. No project save, mutation, PLC control, deployment, or plant acceptance is
claimed. The deeper direct resolver and depth-pruned walk remain deferred.

## .NET 10 host migration — offline implementation completed (2026-09-13)

The modern side of the two-process runtime now targets `net10.0`: the MCP host, tests, and
FakeWorker use the stable .NET 10 SDK boundary, while the shared contracts remain
`netstandard2.0` and the Siemens Openness worker remains `net48`. The direct dependency set is
`ModelContextProtocol` 2.2.0, `System.Text.Json` 10.0.12 on both legacy-boundary projects,
`Microsoft.SourceLink.GitHub` 10.0.401, and `Microsoft.Extensions.Hosting`,
`Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, and
`Microsoft.Extensions.Logging.Abstractions` 10.0.12. SourceLink resolves
`Microsoft.Build.Tasks.Git` 10.0.401; the worker explicitly retains `System.ValueTuple` 4.6.2 and
packages exactly one required `System.IO.Pipelines.dll` companion.

The framework-dependent global-tool package was built and verified under
`tools/net10.0/any/`; the separate `win-x64` release was published self-contained and single-file.
Offline gates passed a serial Release build with the Siemens reference stubs, the focused package
and workflow tests, the strict NuGet package verifier, and file-by-file inspection of both release
layouts. The verified artifacts contained the expected worker payload and no Siemens DLLs. No live
TIA Portal harness or test was run, so this entry makes no live-runtime, project, device, or plant
acceptance claim.

## Issue #30 PLC block header metadata — offline implementation completed (2026-09-13)

- `browse_project_tree` block nodes now expose non-blank `HeaderAuthor`, `HeaderVersion`,
  `HeaderFamily`, and `HeaderName` values through typed `PlcBlock` properties.
- Producer, strict-decoder, FakeWorker IPC, cached continuation, canonical MCP, budget, Release
  build/test/coverage, and package-content gates passed. Maintained docs distinguish reported
  metadata from verified provenance.
- Live TIA Portal V21 acceptance remains a separate read-only authorization gate.

## Issue #30 PLC block header metadata — read-only live acceptance completed (2026-09-17)

The separately authorized `BlockHeaders` harness passed against the already-open TIA Portal V21
project using a fresh current-main baseline and a real-V21 candidate build. Each side completed
three 123-node snapshots, with identical tree identity, canonical text/structured equality, valid
ordering, and completed pagination. This fixture fits one page at `pageSize=200`; it does not add
live continuation coverage. Exact user, system, software-unit, and blank-header cases verified all
four header fields, engineering-name/`HeaderName` divergence, system-block identity, software-unit
membership, and omission of the declared blank author/family/header-name fields.

The accepted cases recorded these exact observations:

- User block `MotorSoftstart`: `HeaderAuthor=LCZ`, `HeaderVersion=0.0.0.0`,
  `HeaderFamily=Motors`, `HeaderName=3`.
- System block `G7_RT_Plus_1_V6`: `IsSystemBlock=true`, `HeaderVersion=0.1`.
- Software-unit block `HartCommandsRdWrInRun`: `HeaderAuthor=Codex`, `HeaderVersion=0.1`,
  `HeaderFamily=Communication`, `HeaderName=1`, `SoftwareUnit=Test_SU`.
- Blank-header block `StateMachine`: `HeaderVersion=0.1`; `HeaderAuthor`, `HeaderFamily`,
  and `HeaderName` absent.

Typed-version observations were `0.0.0.0` for the selected user block and `0.1` for the selected
system, software-unit, and blank-header blocks. In particular, the blank-header case still reported
`HeaderVersion=0.1` while omitting `HeaderAuthor`, `HeaderFamily`, and `HeaderName`. These are
observations of this project and build, not universal Siemens defaults or guarantees.

Ignored evidence under `artifacts/issue-30-live-20260917/` contains the strict final manifest,
`main-baseline-final.json`, `candidate-final.json`, raw responses, host/worker SHA-256 provenance,
and timing/size comparisons. Baseline/candidate median times were 893.4/1034.7 ms; the candidate's
first read took 84.2 seconds, followed by 1034.7 and 848.3 ms. Median initial-response sizes were
17463/19141 characters. No performance threshold was applied. An earlier candidate deployment
timed out before a browse response; its evidence was retained, and the successful retry followed a
serial rebuild against the installed real V21 assemblies. These observations do not establish the
cause of the earlier timeout or the first-read delay.

The harness ran with `--read-only` and invoked only `browse_project_tree`. No save, compile,
import/export, project mutation, PLC control, or plant acceptance is claimed.

## Network Phase 4 current-revision public live acceptance — bounded PASS (2026-09-24)

After the 2026-09-23 connected-delete failure, the user confirmed the disposable copy was closed
without saving and reopened, then logged into safety. On the frozen PR 1 candidate, the guarded
public MCP harness passed Inventory, Preview, all eight Ethernet/PROFIBUS lifecycle operations
(including both originally connected deletes), and a separate final Inventory. The final read
observed zero subnets and the same 81 aggregate hardware devices. Portal reported unsaved changes;
the harness did not save, compile, or download. The root count of 10 comes from lifecycle results
without an independent pre-Apply baseline, and retained-device identities and node/IO-system
attributes were not independently read back. The earlier failure remains in the
[live report](superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md).

The executable repair commit had passed full Debug and Release coverage gates. This docs-only
candidate re-pin passed 298 focused tests and a normal-user Debug real-reference host build; a
sandbox full run returned 3094/3098 with four environment failures. The user directed this turn
to the live gate, so host retry, Release, and coverage were not repeated on the re-pinned HEAD.

## Network Phase 4 contract repair — static implementation completed (2026-09-23)

Focused automated gates passed for four contract repairs: update/delete now require exact ordinal
`target.kind: "subnet"`; all four raw success members, including `networkDeviceCount`, are required
before typed normalization; late zero/multiple worker matches return `postcondition_failed`; and
delete resolves, type-checks, and captures a nonblank name from one transaction-local subnet object
before `Delete()`. The subsequent failed attempt and successful rerun are recorded in the completed entry above.

## JSON contract Phase 1b required-member enforcement — live acceptance completed (2026-09-29)

One worker-payload reader in `CanonicalJson` now enforces required members for the Network,
hardware-page, project-tree, and rebind-state decodes. It replaces the hand-written JSON-shape
layers in the first three contracts. The seven network roots and two worker probes now write
explicit nulls, and `LegacyNullOmissionReason.RequiredMemberEnforcement` is gone.
- `NetworkPayloadContract.cs` went from 1268 to 831 lines, and `HardwarePagePayloadContract.cs`
  from 258 to 175.
- The builds are clean, and the full suite passed 4199/4199, up from 3858.
- One behavior loosens, as documented: an explicit `ioDetails: null` on a read that did not
  request IO details is now accepted.
- A payload the reader rejects logs `validators=Decode` instead of a validator name.

The branch build `3.0.1-local.33.g4de84ef` was installed as the `tia-mcp` global tool and run
against TIA Portal V21 on a disposable copy of `SimpleProject`, 25 calls in all.
- **Reads:** the hardware configuration (default, IO details, tag matches, and three pages), the
  catalog search, the object list (two pages), four inspections including a `kind: "null"`
  attribute, and the project tree (two pages plus a start selector).
- **Writes:** `open_project`, then a `network_write` preview and apply for each of
  `create_subnet`, `add_network_device`, `configure_network_device`, `update_subnet`, and
  `delete_subnet`.

No call returned `protocol_error`. Every response had its declared shape, and no contract changed.
The `NetworkDeviceCreator` null fallback added by the final review did not fire, because Openness
returned the device name and type, so that branch is verified by the build only. The copy was
left with unsaved changes; nothing was saved, compiled, or downloaded. The per-call record is in
the [plan's acceptance notes](superpowers/plans/2026-09-28-json-contract-phase1b-required-members.md#acceptance-notes-live-2026-09-29).

## Write-safety redesign Phase 1 foundation — completed (2026-09-29)

The guarded write pipeline (`TiaMcpServer/Safety/Pipeline/`) now exists beside the token flow, and
`execute_read_batch` content reads return a format-tagged `contentHash`. No registered write tool
uses the pipeline yet; Phases 2–4 move the tools onto it.
- The full suite passed 4314/4314. The host builds clean; the solution build was blocked only by
  running `TiaMcpServer` processes locking the output DLL, not by a compile error.
- **Spike (Appendix A of the spec):** elicitation reached the user in Claude Code, Codex (default
  approval), and MCP Inspector; Codex unrestricted mode answers `decline`, so it fails closed. The
  `anthropic/requiresUserInteraction` marker works only in Claude Code, so it was dropped.
- The spike probes, the marker helper, and their tests are deleted. `UserConfirmation` stays for
  Phases 1b and 2.

## Write-safety redesign Phase 1b access modes — offline acceptance completed (2026-09-30)

At that checkpoint the presets distinguished read-only observation (4 tools), read-write
in-project edits/compilation (8), and full lifecycle/PLC runtime control (14). The Oct3 lifecycle-tier
and binding delivery below supersedes those counts and lifecycle permissions; startup and installer
defaults remain read-write and read-only respectively.

- Discovery uses the same registration helper in production and protocol tests. Host denial
  precedes binding, snapshots, token handling and worker activity; worker authorization is
  independent. Tests cover hidden lifecycle calls, mixed PLC-control batches, direct previews,
  initial attachment without cross-project worker dispatch, and full-mode launch/restart.
- That checkpoint introduced an immutable default-on confirmation singleton and parser; the
  subsequent mode-derived confirmation delivery removed both. Legacy Network/batch tokens remain.
- Fresh Release stub solution build passed with 7 existing xUnit2031 analyzer warnings and no
  errors. The complete offline suite passed **4414/4414**, with no skips, using the current CI
  coverage settings and serialized collections. Scoped line coverage was **93.76%** (9833/10487),
  above the **80%** gate; branch coverage was 85.48%. The existing threshold script passed.
- The direct/transitive NuGet vulnerability check reported no vulnerable packages in any of the
  five projects using the current sources. Changed-document links and README absolute links
  passed local validation. No dependency or version change was required.
- Independent preset, enforcement, CLI and whole-branch reviews were used. Their fixes include
  null-method denial, lifecycle post-write status classification, stronger dispatch assertions,
  serialized environment tests, and production DI coverage. Static dispatch/catalog inspection
  matched all 56 worker methods; unknown operations remain denied in every mode.
- No live TIA operation, client-registration change, package tag, release, push or merge was
  performed. Stub/FakeWorker evidence covers policy, protocol and IPC behavior only.

The [implementation record](superpowers/plans/2026-09-30-write-safety-phase1b-access-modes.md)
contains the completed checklist and execution decisions; the [design delivery note](superpowers/specs/2026-09-29-write-safety-redesign-design.md#6-delivery-phases)
records the Phase 2 confirmation handoff.

## JSON contract Phase 2 standalone tools — implemented, live acceptance pending (2026-09-30)

`get_project_status` and `compile_check` now advertise concrete output schemas and deliver one
canonical document in text and `structuredContent`. Version `1.0` uses a typed single result
with `status`, `value`, `failure`, and `omission`. Status includes the full metadata model with
explicit nulls. Compilation success means compilation passed; compiler errors and unavailable
states retain their report with `success:false`, `error:null`, and MCP `isError:false`.

The dedicated `ProjectStatusResultInfo` root keeps lifecycle/probe bytes unchanged. The compile
root writes nulls, while its nested import envelope retains its own null policy. Strict worker
decoding precedes whole-value omission at 60,000 canonical characters; the complete document is
capped at 180,000. Host-only failure provenance distinguishes an attempted operation from
identity rejection before dispatch. Compilation authorization precedes its binding gate.

Focused offline verification passed 244 tests covering payload validation, boundaries,
metadata, compiler producer behavior, compatibility and real SDK schemas/discovery/calls. The
[implementation plan](superpowers/plans/2026-09-30-json-contract-phase2-standalone-tools.md)
tracks the final solution, coverage and independent-review gates. The [offline validation report](superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md)
records the passing Release stub build, 4,513 passing tests, 93.94% line coverage, and two
independent reviews without findings. Live V21 producer compatibility
requires separate authorization for a disposable fixture. No package version/tag or remote
publication is part of this migration step; Phase 3 remains a separate successor.

## JSON contract Phase 3 / write-safety Phase 2 — offline qualification completed (2026-10-01)

At that checkpoint the six lifecycle tools gained guarded single-call writes, concrete structured
schemas, typed mutation/verification outcomes, and one canonical audit record per call. All seven
lifecycle guard IDs were covered. That source used configurable elicitation and an agent
confirmation-list fallback; the Oct3 delivery supersedes those confirmation semantics.
Hard blocks and dry runs never elicit. Binding preparation preserves the exact source/destination,
ownership, recovery and save-as transitions, with a fresh post-acceptance state check. Client-returned
acceptance does not prove a human saw a dialog.

Attempted mutation/verification failure retains typed evidence with `success:false`, `error:null`,
and MCP `isError:false`; pre-mutation rejection retains top-level error and `isError:true`.
Whole-value and document budgets are 60,000/180,000 canonical characters, including large effects
and guard metadata. Omission markers retain guard identity, acknowledgement, phase, and verdict;
missing evidence calls for status/artifact inspection, never mutation replay. Lifecycle wrapper/token
paths are retired; Network/batch tokens remain active.

The qualified code/test candidate is `8f0e26f83db4faf6cd243fef7489f6419db3925b`. The final host Release
suite passed **4,640/4,640**, with no failures or skips. The existing **80%** line-coverage gate passed
with Cobertura line rate **0.9386** (9,997/10,650 lines), branch rate 0.8578. Serial Release stub and
installed-V21 reference Rebuilds passed with zero errors and seven pre-existing xUnit2031 warnings.
Independent review's P2 metadata-cap finding was reproduced and fixed with RED→GREEN evidence;
the final fix diff has no unresolved findings. Package-cache and child-build environment corrections
were command-only, without production-source or dependency-version changes.

The [implementation record](superpowers/plans/2026-09-30-json-contract-phase3-lifecycle.md) and
[validation report](superpowers/acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md)
record the offline gates and diagnostic runs. The separately authorized
[2026-10-01 live validation](superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md)
passed for that source; these offline/reference checks alone never proved live behavior. The
later tier/confirmation/binding change invalidated that frozen evidence for its new candidate;
Task13 replaced it as recorded below. The earlier passing evidence is retained as history.

## Lifecycle tiers and bind_project — implemented, live acceptance completed (2026-10-03)

The tool counts are 5/15/15. `bind_project` selects an already-open project in every mode, with
force for a different configured or previous binding; reads never bind/switch, and only
open/create can open projects. Worker ownership is lost on detach. Read-only never opens,
creates, saves or closes. Doctor warns for unbound writable sessions; project-tree cursors reject
binding changes, including switching away and back. Each actual read-write lifecycle call asks
once through form elicitation; full uses policy without server elicitation. Block guards stop every
mode and dry runs never mutate/prompt. Old startup confirmation switches and agent confirmation
lists were removed. Audit v2 records confirmation by `user`, `policy`, or `none`.

Phase A shipped separately at `85aec97`; the combined frozen source is
`3bb504b4647cdd57677323eb44aac3305a33aafd`. Its offline checkpoint passed **4,946 tests** with
**93.24%** linked host/contracts line coverage, above the 80% gate; this excludes live Siemens code.
The [Oct3 report](superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md)
replaces Oct1 acceptance for the new candidate: all three functional mode groups passed, six
lifecycle operations and seven guards were exercised in each writable mode, 27 full and 42
read-write audit v2 records matched canonical responses/hashes, and read-only returned five tools
and preserved both project states through binding/cursor checks. Artifacts, restoration and
graceful host/worker exit evidence are bounded by that report. The maintainer subsequently reported
no new TIA dialog in read-only mode, completing Task13's separate human observation. Their
conclusion that the dialog appears once for an unchanged worker is not a general runtime guarantee:
the recorded native worker changed from PID25148 in read-write to PID28428 in read-only.
Task14's current documentation/spec updates are complete. Programmatic acceptance remains distinct
from recorded human observations; no functional production fix or live replay was required.
No release/tag, Network/batch retirement, headless/Multiuser, crash/timeout, archive retrieval,
PLC/plant acceptance, plugin update, or remote publication is claimed.

## Multiuser PR 1 reference and contract foundation — implemented (2026-10-03)

Replaced opaque broad compile references with reviewed minimal declarations under
`reference-stubs/`, retaining the two assembly names, version `21.0.0.0`, and public-key token
`29bfe5fdf4ba5d3b` through a public-only delay-signing key. The former references already
contained Multiuser types. A locked compile-only probe checks the intended API surface;
default verification checks identity and canonical raw-source-hash metadata without updating
tracked artifacts, while explicit `-Update` replaces only the two known DLLs. Scoped CRLF
attributes preserve raw-byte provenance across clean checkouts; whole-PE hashes remain
diagnostic across compiler patch versions.

Passive contracts write explicit nulls and remain Siemens-free. No public tool, worker operation,
binding integration, `.als21` lifecycle behavior, or permission was added. Generated stubs remain
compile-only and excluded from runtime output; all Siemens binaries remain excluded from
packages, while the net48 worker uses installed real assemblies. Stub, installed-reference,
contract, package, and live evidence remain distinct. The final qualification and independent
review gate acceptance of this foundation; internal context integration remains PR 2's dependency.
See [building from source](development/building.md#generated-openness-references) for the procedure.

## Multiuser PR 2 active project context — offline qualified (2026-10-05)

The worker session now stores one typed active context and lifecycle owner, with a common
`ProjectBase` engineering root and a standalone-only bridge for existing content services.
Selection, live identity/generation, ownership, explicit lifecycle resolution, and cleanup
are covered by source-linked boundary tests. No production caller activates local/server
contexts, and no public `.als21` or Multiuser operation is enabled.

Continuation repaired four stale source assertions in three test files without changing
production behavior. The final serialized suite passed **5036/5036**, with **93.96%** line
coverage above the existing 80% gate. Reference provenance/drift checks, serial stub build,
installed-V21 solution and API-probe compilation, and package/doctor exclusion passed.
Both compile modes had zero warnings/errors; package verification found one canonical
15-file worker payload and no Siemens DLLs. Independent review found no Important/Critical
production issue. See the [qualification/fixture record](superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md)
for preserved failed-run evidence, exact retained real-reference hashes, and disposable scope.
Offline changes are committed with explicit user authorization. Current-candidate standalone
live acceptance and restoration remain open; Issue #65 is not complete.

## Multiuser PR 2 standalone live accepted (2026-10-05)

After explicit exact-fixture authorization, frozen candidate `a47bf0d` passed bounded live
standalone qualification: 131 captured tool calls, including 64 lifecycle calls across
read-only/read-write/full. Canonical documents and all lifecycle audit hashes matched.
Selection/cursor continuity, six lifecycle previews/applies, decline/cancel, ownership loss,
dirty-source/last-client blocks, external identity changes, typed attempted failures and
restoration were observed. A/B are clean and open at their original paths; H was saved with
its original comment, independently reopened clean, then closed. Test helpers exited.

The [acceptance report](superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md)
retains installed hashes, private evidence locations, deviations and failures. Root-project
comments substituted for PLC edits; external changes used an independent Openness client;
form acceptance was scripted. Independent review found no Important/Critical behavior issue.
No production change or expensive suite replay followed these documentation-only results.
Public Multiuser operations and Issue #65 remain open.

## Multiuser PR 3 discovery and inventory — offline qualified and live accepted (2026-10-06)

`bind_project` retains default/null binding and adds six explicit inspection actions without
changing 6/16/16 discovery. Exact raw-key selectors, persistent Portal-only attachment, local
foreign-PID refusal, genuine identity-loss invalidation, canonical typed payloads and whole-value
budgets are covered offline. Current-machine/current-user sessions and non-atomic lock observations
remain bounded; ALS21/session/content/marking/mutation delivery and Issue #65 completion stay open.

Frozen code `29468ba` passed 5,536/5,536 serial tests with coverage; unchanged 80% line gate passed
at 93.83%. Installed V21 compile-only probe/solution and source-reference drift passed; existing
Step7 `Tags.cs:112` CS0108 remains deferred. Local package `3.0.1-local.316.gfba3cc7` passed layout/leak
checks and includes the maintained README. The [offline report](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md)
records exact commands, scoped/changed coverage, hashes and limits. Documentation review and
scoped session-ID clarification re-review passed; final whole-branch review is separate.
Task 5 live acceptance was subsequently finished and passed by maintainer decision on 2026-10-06.
The [live report](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
records 73 installed calls and retains unexecuted cases as non-blocking evidence limitations.
