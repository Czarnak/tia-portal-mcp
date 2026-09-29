# JSON Contract Phase 1b: Required-Member Enforcement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three hand-written required-member validators with one generic worker-payload
reader, and switch the network payloads to explicit nulls in the same change.

**Architecture:** `CanonicalJson` gains a worker-payload reader. It uses the strict options plus
`RespectNullableAnnotations` and a type-info modifier that makes every settable member required,
except members declared conditional with `[JsonIgnore(WhenWritingNull)]`. The reader accepts only
payload roots that write explicit nulls. The worker loses its nine `RequiredMemberEnforcement`
markers. The JSON-shape layers in the Network, hardware-page, and project-tree contracts are
deleted, and the three rules they express that are not "member present" move into the typed
validators.

**Tech Stack:** C# (net10.0 host, net48 worker, netstandard2.0 Contracts), System.Text.Json
10.0.12, xUnit.

**Spec:** [2026-09-28-json-contract-phase1b-required-members-design.md](../specs/2026-09-28-json-contract-phase1b-required-members-design.md)

## Global Constraints

- No tool output byte changes, and no `network_write` token hash changes.
- Every rejection the deleted code makes today stays a rejection, with category `protocol_error`
  and the existing fixed message.
- The batch tools are excluded. Do not change `TiaMcpServer/Batch/*` decodes, `OperationBatch*`,
  or any `BatchRedesign` marker.
- `ToolMigration` markers stay; they belong to Phases 2-3.
- Contracts classes stay classes. Do not convert them to records and do not add `required`.
- Run `dotnet` in PowerShell, not Git Bash, because Bash mangles `/p:` switches. Build with
  `-m:1`.
- Commit messages are one-line conventional commits with no body and no trailers.
- Navigate code with jCodemunch: call `jcodemunch_guide` first. Use `Read` only on a file about to
  be edited.
- The test project links host files explicitly. `CanonicalJson.cs` and all three payload contracts
  are already linked, so no `.csproj` change is expected. A new host file would need a
  `<Compile Include>`.
- Known flakes: `ProjectTreeLiveHarnessContractTests.Pagination_RejectsBrokenProgressBeforeAnotherCall`
  and `DoctorPackageVerificationScriptTests` can fail under the full parallel run and pass alone.
  Re-run them alone before treating a failure as real.
- Baseline at `269f9b9`: 3858 tests.

## File Structure

| File | Change |
| --- | --- |
| `TiaMcpServer/Json/CanonicalJson.cs` | Add the worker-payload read options, the required-member modifier, and two entry points |
| `TiaMcpServer.Contracts/LegacyNullOmissionAttribute.cs` | Remove `LegacyNullOmissionReason.RequiredMemberEnforcement` |
| Seven Contracts roots (see Task 3) | Remove the `RequiredMemberEnforcement` marker |
| `TiaMcpServer.OpennessWorker/NetworkAttributeProbeInfo.cs`, `.../Openness/SubnetLifecycleMutationProbeService.cs` | Remove the marker |
| `TiaMcpServer/Network/NetworkPayloadContract.cs` | Use the reader; delete the JSON-shape layer; move the `ioDetails` and attribute-value rules into the typed validators |
| `TiaMcpServer/Network/HardwarePagePayloadContract.cs` | Use the reader; delete the shape layer; add null-element checks |
| `TiaMcpServer/ProjectTree/ProjectTreeWorkerPayloadContract.cs` | Use the reader; delete the shape layer; add null-element checks |
| `TiaMcpServer/Worker/ProjectRebindStatePayloadContract.cs` | Use the reader |
| `TiaMcpServer.Tests/Json/CanonicalJsonWorkerPayloadTests.cs` | New: reader tests |
| `TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs` | New: pins every `[JsonIgnore(WhenWritingNull)]` member in Contracts |
| `TiaMcpServer.Tests/Worker/WorkerPayloadNullPolicyRegisterTests.cs` | Remove the seven network entries |
| `NetworkPayloadContractTests.cs`, `DeviceItemIoDetailsContractTests.cs`, `NetworkIntrospectionContractTests.cs`, `NetworkSubnetLifecyclePayloadContractTests.cs`, `HardwarePagePayloadContractTests.cs`, `ProjectTreeWorkerPayloadContractTests.cs` | Characterization and moved-rule tests |
| `docs/roadmap/json-contract.md`, `docs/ARCHITECTURE.md` §7a, `AGENTS.md`, `docs/IMPROVEMENT_LOG.md` | Documentation (Tasks 7 and 9) |

## Task Order

Task 3 comes before Tasks 4-6 because the reader refuses roots that are still marked. Task 3 is
safe on its own: the hand-written validators already accept explicit nulls, since a present
member satisfies them.

---

### Task 1: Characterization tests for today's rejections

**Files:** the six contract test files in the File Structure table (modify only).

**Interfaces:**
- Consumes: the existing `Project`/`Decode` entry points of the three contracts, and the existing
  test helpers in each file.
- Produces: a `[Theory]` per contract that feeds a mutated payload and asserts rejection.
  Task 1 ends with this whole set green; Tasks 4-6 must keep it green unchanged.

- [ ] **Step 1:** For each case below, first find out whether an existing test already asserts that
  rejection. Add only the cases that are missing. Build each payload by mutating a valid fixture:
  remove a member, set it to `null`, or change its JSON kind.
  - `list_network_objects`:
    - each root member missing: `items`, `totalCount`, `returnedCount`, `nextCursor`;
    - each `items[]` member missing: `kind`, `selectable`, `selector`, `evidence`, `diagnostics`.
  - Selector path segments: each of `index`, `name`, `positionNumber`, `typeIdentifier` missing,
    in the list `items[].selector`, the inspect `target`, and a hardware-config nested `selector`.
  - `read_hardware_config`:
    - `ioDetails` missing with `includeIoDetails: true`;
    - `ioDetails` present with `includeIoDetails: false`;
    - `addresses` or `channels` missing;
    - `controllerNames` missing, not an array, or containing a `null` or a number;
    - `tagMatches` missing;
    - each `tagMatches[]` member missing, or not a string.
  - `inspect_network_object`:
    - `attributes[].value` missing when `availability` is `available`;
    - `value` missing `kind` or `value`;
    - an enum value missing `typeName`, `symbol`, or `numericValue`.
  - Subnet lifecycle result: each of `subnetId`, `name`, `networkDeviceCount`,
    `networkDeviceCountUnchanged` missing.
  - Hardware page:
    - each of the nine root members missing;
    - `messages[]` containing `null` or a non-string;
    - a candidate that is `null` or not an object;
    - a candidate missing `offset`, `device`/`subnet`, or `messages`;
    - `device`/`subnet` not an object;
    - a candidate's `messages[]` containing `null`.
  - Project tree:
    - root missing `startSelector`, `depth`, or `roots`;
    - a selector segment missing `nodeType` or `name`, or holding a non-string;
    - a node missing `name`, `nodeType`, `details`, or `children`;
    - a node that is not an object;
    - a `null` element in `roots[]`, `children[]`, or `startSelector[]`.
- [ ] **Step 2:** Run the suite:
  `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PayloadContract|FullyQualifiedName~IoDetails|FullyQualifiedName~Introspection"`.
  Expected: every new case **passes** on current code. A case that fails means today's code does
  *not* reject that shape. Record it in the execution notes, do not add a check, and remove the
  case. Task 1 pins existing behavior only.
- [ ] **Step 3:** Commit `test: pin current worker payload shape rejections`.

### Task 2: Worker-payload reader and conditional-member register

**Files:** modify `TiaMcpServer/Json/CanonicalJson.cs`. Create
`TiaMcpServer.Tests/Json/CanonicalJsonWorkerPayloadTests.cs` and
`TiaMcpServer.Tests/Json/ConditionalMemberRegisterTests.cs`.

**Interfaces:**
- Produces:
  - `public static T CanonicalJson.DeserializeWorkerPayload<T>(string json)`
  - `public static (T Value, string Text, JsonElement Element) CanonicalJson.NormalizeWorkerPayload<T>(string json, Action<T>? validate = null)`
- Both throw `InvalidOperationException` when `WorkerJson.OmitsNullMembers(typeof(T))` is true.
- Both share the existing `Deserialize`/`Normalize` pipeline (duplicate-property rejection,
  canonical text) and differ only in read options.
- Private pieces:
  - a `WorkerPayloadRead` options instance: `StrictRead` settings plus
    `RespectNullableAnnotations = true` plus a `DefaultJsonTypeInfoResolver` with one modifier;
  - `static void RequireDeclaredMembers(JsonTypeInfo typeInfo)`. For object types, it sets
    `IsRequired = true` on every property that has a setter (including `init`), unless the
    property carries `[JsonIgnore(Condition = WhenWritingNull)]`.

- [ ] **Step 1: Failing reader tests.** One `[Fact]` each, using test-local DTOs unless a real
  Contracts type is named.
  - Rejected:
    - a missing member at the top level, in a nested object, and inside an array element;
    - an explicit `null` in a non-nullable string, and in a non-nullable list;
    - an unknown member;
    - a duplicate property.
  - Accepted:
    - `null` in a nullable member;
    - a `WhenWritingNull` member that is absent, and one that is present;
    - `NetworkAttributeValueInfo` with `"value": null`, since `[JsonIgnore(Never)] object?` is
      required but nullable;
    - `HardwarePaginationInfo` (a positional record) with `nextCursor` absent;
    - a `CatalogEntryInfo[]` root.
  - Rejected for the real Contracts types above:
    - `HardwarePaginationInfo` missing any other member;
    - a `CatalogEntryInfo[]` element missing a member.
  - Refused: a test-local DTO marked `[LegacyNullOmission(LegacyNullOmissionReason.BatchRedesign)]`
    makes both entry points throw `InvalidOperationException`.
  - Normalize output: `NormalizeWorkerPayload` returns the same canonical text as `Normalize` for
    a valid payload.
- [ ] **Step 2:** Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~CanonicalJsonWorkerPayloadTests`.
  Expected: it fails to compile because the entry points are missing.
- [ ] **Step 3:** Implement the options, the modifier, and the entry points in `CanonicalJson.cs`.
- [ ] **Step 4:** Re-run. Expected: PASS. Then run `CanonicalJsonTests`, which must be unchanged.
- [ ] **Step 5: Conditional-member register.** Reflect over the Contracts assembly for every
  property with `[JsonIgnore(Condition = WhenWritingNull)]`. Assert the set equals exactly:
  - `DeviceItemInfo.IoDetails`
  - `HardwareConfigInfo.Pagination`
  - `HardwarePaginationInfo.NextCursor`
  - `WorkerResponse.BlockImportOutcome`
  
  The failure message says that a new conditional member must be added here, and that its
  description must state when the member appears. Run it: PASS.
- [ ] **Step 6:** Commit `feat(json): add worker payload reader with required members`.

### Task 3: Network payloads write explicit nulls

**Files:**
- Remove the marker from `HardwareConfigInfo`, `HardwarePageCandidateResultInfo`,
  `NetworkObjectInspectionInfo`, `CatalogEntryInfo`, `AddDeviceResultInfo`,
  `ConfigureNetworkDeviceResultInfo`, and `SubnetLifecycleResultInfo`, all under
  `TiaMcpServer.Contracts/`.
- Remove it from `NetworkAttributeProbeInfo` and `SubnetLifecycleMutationProbeResult` in the
  worker.
- Remove the enum member from `LegacyNullOmissionAttribute.cs`.
- Update `WorkerPayloadNullPolicyRegisterTests.cs`.

**Interfaces:** after this task, `WorkerJson.OmitsNullMembers` returns `false` for all nine roots.

- [ ] **Step 1:** Update the register test: remove the seven network entries, and update its
  summary comment (Phase 1b done, Phases 2-3 and the batch redesign remain). Run it. Expected:
  FAIL, because the markers are still present.
- [ ] **Step 2:** Remove the nine markers and the enum member. Build the solution with `-m:1`.
- [ ] **Step 3:** Run the full suite. Expected: green except known flakes.
  - A failure caused by a FakeWorker fixture that now renders `null` in a non-nullable member
    (typically caught by an existing `RequireNotNull`) is fixed in the fixture.
  - If that null is legitimate Openness behavior, the contract member becomes nullable instead,
    and the execution notes say why.
  - Run `FakeWorkerWireParityTests` explicitly.
- [ ] **Step 4:** Commit `feat(worker): write explicit nulls in network payloads`.

### Task 4: Network contract uses the reader

**Files:** modify `TiaMcpServer/Network/NetworkPayloadContract.cs`. Tests go in the Network test
files from Task 1.

**Interfaces:**
- Consumes: `NormalizeWorkerPayload<T>` and `DeserializeWorkerPayload<T>` (Task 2).
- `DecodeHardwareConfig(string payload)` keeps its signature. Its typed value and canonical
  rendering are unchanged, so token hashes are unchanged.
- `ValidateHardwareConfig(HardwareConfigInfo, bool? includeIoDetails)` gains the `ioDetails` rule.

- [ ] **Step 1: Failing moved-rule tests.** These must reach the typed validators, so the
  payloads are otherwise valid and have every member present.
  - A device item nested two levels deep:
    - with `IoDetails` null under `includeIoDetails: true` → `protocol_error`;
    - with `IoDetails` present under `false` → `protocol_error`;
    - both are accepted when `includeIoDetails` is unspecified (the `DecodeHardwareConfig` path).
  - An `available` attribute whose `value` is `null` → `protocol_error`.
  - A `null` element in `controllerNames[]` → `protocol_error`.
  
  Run them. Expected: they pass today through the JSON layer. Temporarily disable the JSON-layer
  call in a scratch edit to see them fail, then revert. This confirms the typed path is what the
  test exercises once the layer is gone.
- [ ] **Step 2:** Switch every decode to the reader: the operation `Decode` switch, `Decode<T>`,
  `DecodeHardwareConfig`, `DecodeObjectList`, `DecodeObjectInspection`,
  `DecodeSubnetLifecycleResult`, and the `NetworkEnumValueInfo` decode inside `ValidateEnumValue`.
- [ ] **Step 3:** Delete the JSON-shape layer:
  - `RequireJsonMembers`;
  - both overloads each of `ValidateRequiredPathIndexMembers` and
    `ValidateRequiredHardwarePathMembers`;
  - `ValidateRequiredListMembers`, `ValidateDeviceItemJson`, `ValidateIoDetailsJson`,
    `ValidateRequiredAttributeValueMembers`;
  - the `JsonDocument` pre-pass in `DecodeSubnetLifecycleResult`;
  - the `TryGetProperty` probing in `ValidateEnumValue`, which keeps only the `JsonValueKind.Object`
    check before the reader decode.
- [ ] **Step 4:** Add the moved rules to the typed validators:
  - `ioDetails` versus the request, recursively through `DeviceItemInfo.Items`;
  - an `available` attribute has a non-null `value`;
  - no `null` element in `controllerNames[]`.
  
  Update the "contract types initialize their collections" comment, which no longer describes the
  mechanism.
- [ ] **Step 5:** Remove each `RequireNotNull(member, …)` whose member is declared non-nullable,
  since the reader now enforces it. Keep every element check (`RequireNotNull(element, "…[]")`) and
  every check on a nullable member.
- [ ] **Step 6:** Run the Network test files, `NetworkStructuredProtocolTests`, and
  `NetworkToolsTests`. Expected: PASS with the Task 1 characterization tests unmodified. Record
  the file's new line count in the execution notes.
- [ ] **Step 7:** Commit `refactor(network): enforce required members through the reader`.

### Task 5: Hardware-page contract uses the reader

**Files:** modify `TiaMcpServer/Network/HardwarePagePayloadContract.cs` and
`HardwarePagePayloadContractTests.cs`.

**Interfaces:** `Decode(NetworkOperationRequest, WorkerCallResult, HardwarePageContinuationInfo?)`
keeps its signature and result type.

- [ ] **Step 1: Failing moved-rule tests.** In otherwise complete payloads, put a `null` element in
  each of: root `messages[]`, `deviceCandidates[]`, `subnetCandidates[]`, and a candidate's
  `messages[]`. Each → `protocol_error`. Confirm they exercise the typed path, as in Task 4
  Step 1.
- [ ] **Step 2:** Replace `ValidateRequiredJsonShape` + `CanonicalJson.Deserialize` with
  `DeserializeWorkerPayload<HardwarePageCandidateResultInfo>`. Delete `ValidateRequiredJsonShape`,
  `RequiredRootMembers`, `RequireMembers`, `RequireObject`, `RequireArray`, and
  `RequireStringArray`.
- [ ] **Step 3:** Add the null-element checks to `Validate`.
- [ ] **Step 4:** Run `HardwarePage*` and `HardwarePagination*` tests. Expected: PASS, with Task 1
  unmodified.
- [ ] **Step 5:** Commit `refactor(network): decode hardware pages through the reader`.

### Task 6: Project-tree and rebind-state contracts use the reader

**Files:** modify `TiaMcpServer/ProjectTree/ProjectTreeWorkerPayloadContract.cs`,
`TiaMcpServer/Worker/ProjectRebindStatePayloadContract.cs`, and their tests.

**Interfaces:**
- `ProjectTreeWorkerPayloadContract.Decode(WorkerCallResult, IReadOnlyList<ProjectTreeSelectorSegment>?, int?)`
  is unchanged.
- The `ProjectRebindStatePayloadContract` public surface is unchanged.

- [ ] **Step 1: Failing tests.**
  - Tree: for each `null` element that Task 1 found rejected by the JSON layer only, the typed
    path must also reject it: `roots[]`, `children[]`, `startSelector[]`. Confirm as in Task 4
    Step 1.
  - Rebind state: a payload missing any member → rejected. This is new strictness: the type is
    already explicit-null.
- [ ] **Step 2:** Tree: switch to `DeserializeWorkerPayload<ProjectTreeBrowseResultInfo>`. Delete
  `ValidateRequiredJsonShape`, `ValidateNodeJson`, `RequireMembers`, `RequireObject`,
  `RequireArray`, and `RequireString`. Add the null-element checks to `Validate`/`ValidateNode`
  where they are missing.
- [ ] **Step 3:** Rebind state: switch to `DeserializeWorkerPayload<ProjectRebindStateInfo>`.
- [ ] **Step 4:** Run `ProjectTree*` and `ProjectRebindState*` tests, plus
  `ToolOutputContractConformanceTests`. Expected: PASS.
- [ ] **Step 5:** Commit `refactor(tree): decode worker payloads through the reader`.

### Task 7: Documentation

**Files:** `docs/roadmap/json-contract.md`, `docs/ARCHITECTURE.md` (§7a), and `AGENTS.md`.

- [ ] **Step 1:** Roadmap:
  - Status line: 1b complete.
  - Current State: required members are enforced one way; the network roots are explicit-null;
    three null-omission reasons become two.
  - Rewrite Phase 1b around the reader, replacing "move the older mutable Contracts classes to
    records".
  - Add a **"Later: compile-time completeness"** section. It adopts C# `required init` members
    (not positional records) per type, once every decode path of that type writes explicit nulls,
    together with test-data builders. It gives the `CompileCheckReport` / `BlockImportOutcomeInfo`
    reason. It notes that it is unverified whether System.Text.Json recognizes the netstandard2.0
    `RequiredMemberAttribute` polyfill.
- [ ] **Step 2:** ARCHITECTURE §7a: describe the worker-payload reader, the rule, the refusal of
  marked roots, the conditional-member convention and its register, and the callers that stay on
  `Deserialize` (batch snapshots, cursor).
- [ ] **Step 3:** AGENTS.md "Structured JSON contract rules": add the rule — *a host decode of a
  worker payload whose root writes explicit nulls goes through the worker-payload reader; every
  member is required unless declared conditional with `[JsonIgnore(WhenWritingNull)]`, and a new
  conditional member is added to `ConditionalMemberRegisterTests`, with a description stating when
  it appears*. Check that the "Worker payload JSON" convention no longer implies a
  network-specific reason.
- [ ] **Step 4:** Verify that every relative link in the three files resolves.
- [ ] **Step 5:** Commit `docs: record JSON contract phase 1b`.

### Task 8: Full verification

- [ ] **Step 1:** `dotnet build TiaMcpServer.sln -m:1` (real V21 assemblies). Expected: 0 errors
  and no new warnings beyond the 7 existing xUnit2031.
- [ ] **Step 2:** `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true`, which
  is the CI path. Expected: 0 errors.
- [ ] **Step 3:** `dotnet test TiaMcpServer.Tests`. Expected: all green except known flakes, which
  are re-run alone. Record the total test count against the 3858 baseline.
- [ ] **Step 4:** Review the full diff against `main`. Check that:
  - no `TiaMcpServer/Batch/*` file changed;
  - no `ToolMigration` or `BatchRedesign` marker changed;
  - no tool response type changed;
  - no validator was deleted beyond the JSON-shape layer.
  
  Then request code review (superpowers:requesting-code-review) and address findings before
  Task 9.

### Task 9: Live verification with the locally installed build

This is the definition of done. The pull request is not opened before this task passes.

- [ ] **Step 1:** Pack and install the branch build as the `tia-mcp` global tool following
  `docs/development/packaging.md`. That means a PowerShell pack against the real V21 assemblies,
  with version `<tag>-local.<n>.g<sha>` from `git describe --tags --long`, then
  `verify-doctor-package.ps1`, stopping `tia-mcp`, swapping the tool, and checking
  `tia-mcp --version`.
- [ ] **Step 2:** Ask the user to reconnect the `tia-portal` MCP server (`/mcp`) and to have TIA
  Portal open.
- [ ] **Step 3: Reads,** on
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\SimpleProject\SimpleProject.ap21`:
  - `network_read`:
    - `read_hardware_config` default;
    - `read_hardware_config` with `includeIoDetails: true`;
    - a paged `read_hardware_config` (first page plus continuation);
    - `search_equipment_catalog`;
    - `list_network_objects`;
    - `inspect_network_object` on at least one object that has a null-valued attribute.
  - `browse_project_tree`: a first page and a continuation.
- [ ] **Step 4: Writes,** on a scratch copy created with `save_project_as` into the existing
  `_mcp_test` folder and bound to the session. A `network_write` preview then apply for each of:
  `add_network_device`, `configure_network_device`, `create_subnet`, `update_subnet`,
  `delete_subnet`.
- [ ] **Step 5:** Pass criteria:
  - zero `protocol_error` results;
  - each response has its declared shape.
  
  If a `protocol_error` traces to an Openness `null` in a non-nullable member, make that member
  nullable, add a regression test, and repeat Tasks 8-9.
- [ ] **Step 6:** Append **Acceptance notes (live)** to this plan: installed version string,
  project, every call with its outcome, and any contract change made. Add a completed-work entry
  to `docs/IMPROVEMENT_LOG.md`.
- [ ] **Step 7:** Commit `docs: record phase 1b live acceptance`.

## Execution notes (2026-09-29)

- **Task 2 sequencing.** `CatalogEntryInfo` kept its marker until Task 3, and the reader refuses
  an array whose element type is marked, so Task 2 could not test a real `CatalogEntryInfo[]`
  root. Task 2 used a test-local array root (accepted, and rejected when an element misses a
  member) plus a test-local marked-element refusal. The real `CatalogEntryInfo[]` cases landed in
  Task 3, after the marker removal. The design did not change.
- **Rejection tests stopped reaching their rules (Task 4).** The reader rejects an incomplete
  payload before any typed validator runs, so fixtures that omitted nullable members no longer
  reached the rule they were named for. Task 4 completed 16 test fixtures and the FakeWorker
  network success fixture with explicit nulls, and added
  `NetworkPayloadContractTests.Rules.cs`. It has 122 cases, each a single mutation of one of
  14 accepted bases, and each asserts the validator named in the diagnostic. Temporarily
  disabling rules showed that exactly the matching cases fail. Tasks 5 and 6 use the same
  pattern: assert that the base is accepted, that the reader accepts the mutated payload, and that
  the typed rule rejects it.
- **Diagnostic trade-off.** A payload the reader rejects logs `validators=Decode` rather than the
  name of a validator. This is inherent to one generic reader and was accepted.
- **Documented loosening.** An explicit `ioDetails: null` on a read that did not request IO details
  is now accepted, because `ioDetails` is a conditional member. Omitting it is still accepted, and
  a non-null `ioDetails` on an unrequested read is still rejected.
- **Task 5.** `HardwarePagePayloadContract.Validate` already had every null-element check, so
  Step 3 changed no production code. The `Subnet()` test fixture turned out to be rejected by the
  public re-projection, so the subnet tests now start from a valid `ExplainedSubnetRoot` base.
- **Task 6 premise was wrong.** `ProjectRebindStateInfo` already had `[JsonRequired]` on all five
  members, so the reader adds no new strictness there. The rebind-state change is a one-line swap
  plus reader-level and explicit-null tests. The tree already had its null-element checks. A null
  `startSelector` segment is caught by `ProjectTreeNodeTypes.Validate`, which is load-bearing:
  disabling it lets a `NullReferenceException` escape `Decode`. `payload.Roots is null` is now
  unreachable and was kept.
- **Line counts.** `NetworkPayloadContract.cs` 1268 → 831, `HardwarePagePayloadContract.cs`
  258 → 175, `ProjectTreeWorkerPayloadContract.cs` 277 → 184,
  `ProjectRebindStatePayloadContract.cs` 33 → 33.
- **Final-review fixes.**
  - `a21d4c0`: `NetworkDeviceCreator.ReadString` falls back with a warning when Openness returns
    a null name or type identifier after `CreateWithItem`. Without it, the now-explicit null would
    have been rejected after the device had already been created.
  - `4de84ef`: reverts a `SubnetLifecycleService` guard from the same commit, which had no
    effect.
  - `48078cf`: the subnet-offset test now reaches `candidate.Offset < payload.TotalDevices`, the
    Task 1 subnet cases start from the valid base, and the reader has `itemPath[]` segment rows.
  - `ac6de1a` updates stale test comments, and `4e5eebc` corrects documentation wording.
- **Verification.** Task 8: the real-assembly and stub builds had 0 errors and the 7 known
  xUnit2031 warnings. The full suite passed 4199/4199 at `4e5eebc`, up from the 3858 baseline.
  `4de84ef` touches only worker code, which the tests do not cover; it builds with 0 errors.

## Acceptance notes (live, 2026-09-29)

- **Installed version:** `3.0.1-local.33.g4de84ef`. It was packed against the real V21 assemblies,
  `verify-doctor-package.ps1` passed, and it was installed as the `tia-mcp` global tool.
- **Portal and project:** TIA Portal V21 (PID 34524) with
  `C:\Users\LCZ\Desktop\RnD\plc-prompt-injections\_mcp_test\SimpleProject_copy\SimpleProject_copy.ap21`.
  This is a disposable copy of `SimpleProject` that the user made and opened.
- **Deviation:** reads and writes both ran on that copy. The plan had the reads on
  `SimpleProject` and the writes on a `save_project_as` copy. The session was bound with
  `open_project` before the writes.

| # | Call | Outcome |
| --- | --- | --- |
| 1 | `get_project_status` | Succeeded; project open, unmodified. |
| 2 | `network_read` `read_hardware_config` (default) | Succeeded; 5 devices, 3 subnets, 110 messages; no `pagination` and no `ioDetails` member. |
| 3 | `network_read`, 5 operations: `read_hardware_config` with `includeIoDetails`, the same plus `includeTagMatches`, `pageSize: 3`, `search_equipment_catalog` ("CPU 1511", `maxResults: 5`), `list_network_objects` (all six kinds) | The two whole-project IO-detail reads were `omitted`, not `failed` (`resultExceededItemCharLimit`: 72,874 and 72,993 characters against the 60,000 per-item cap). Page 1: 3 devices, 0 subnets, `nextCursor`. Catalog: 5 entries plus the "more may exist" warning. List: 50 of 89, `nextCursor`. |
| 4 | `network_read`, 4 operations: hardware page 2, list page 2, `ET 200SP station_1` with `includeIoDetails`, `S7-1500/ET200MP station_1` with `includeIoDetails` and `includeTagMatches` | All succeeded. Page 2: 2 devices and 1 subnet (one combined sequence), `nextCursor`. List page 2: 39 items, `nextCursor: null`. `ioDetails` on all 21 and 11 device items. |
| 5 | `network_read`, 2 operations: hardware page 3, `ET 200SP station_1` with `includeIoDetails` and `includeTagMatches` | Both succeeded. Page 3: 0 devices and 2 subnets, `pagination` without `nextCursor`. The tag-match read reported "More than one PLC exists" and empty `tagMatches`. |
| 6 | `network_read` `ET 200SP station_1` with `includeIoDetails`, `includeTagMatches`, and `plcName: PLC_LAD` | Succeeded; 22 tag matches across four IO modules. |
| 7 | `network_read` `inspect_network_object` on subnet `B9DF-1`, node `E1` of `ET 200SP station_1`, and IO system `B9DF-2`/1 | All succeeded. Unrepresentable attributes carry `value: null` with a diagnostic. |
| 8 | `network_read` `inspect_network_object` on node `IE2` of `SINAMICS G_1` (no subnet) | Succeeded. `ConnectedSubnet` is `{"kind":"null","typeName":null,"value":null}`, the null-valued attribute case. |
| 9 | `browse_project_tree` (`pageSize: 15`) | Succeeded; 15 of 132 nodes, `nextCursor`. |
| 10 | `browse_project_tree` continuation | Succeeded; offset 15, 15 nodes, `nextCursor`. |
| 11 | `browse_project_tree` with `startSelector` Device/`ET 200SP station_1` > PlcSoftware/`PLC_LAD`, `depth: 1` | Succeeded; 1 node, the selector echoed in `query`, `nextCursor: null`. |
| 12 | `network_write` preview before binding | `binding_conflict`, as expected for an unbound session. |
| 13-14 | `open_project` preview and apply | Succeeded; binding verified at revision 1. |
| 15-16 | `network_write` preview and apply: `create_subnet` `MCP1b_Net` (Ethernet), then `add_network_device` CPU 1511-1 PN `OrderNumber:6ES7 511-1AK00-0AB0/V1.7` as `MCP1b_Station`/`MCP1b_PLC` | Both succeeded. Subnet `B9DF-3`, `networkDeviceCountUnchanged: true`. The device result read back `MCP1b_Station`, `Rail_0`, `System:Device.S71500`, with `warnings: []`. |
| 17 | `network_read` `list_network_objects` nodes of `MCP1b_Station` | Succeeded; 1 node, not selectable because its interface has no type identifier. |
| 18 | `network_read` `read_hardware_config` `deviceName: MCP1b_Station` | Succeeded; node `E1`, no subnet. |
| 19-20 | `network_write` preview and apply: `configure_network_device` node `E1` of `MCP1b_Station`, subnet `B9DF-3`, IP address `192.168.50.10`, `pnDeviceName` `mcp1b-plc` | Succeeded. `appliedSettings` holds Address and Subnet. `skippedSettings` holds PnDeviceName, which TIA makes read-only while name auto-generation is on. |
| 21-22 | `network_write` preview and apply: `update_subnet` `B9DF-3` name → `MCP1b_Net_Renamed` | Succeeded; the name read back as `MCP1b_Net_Renamed`. |
| 23-24 | `network_write` preview and apply: `delete_subnet` `B9DF-3`, with the node still connected | Succeeded; `networkDeviceCountUnchanged: true` (6). |
| 25 | `network_read` `list_network_objects` subnets | Succeeded; 3 subnets, `B9DF-3` gone. |

**Result:** passed.
- No call returned `protocol_error`, and no operation failed.
- The two `omitted` items came from the host's per-item output cap, and the per-device re-runs
  in calls 4-6 succeeded. The single `binding_conflict` was the expected precondition.
- Every response had its declared shape:
  - operation results are JSON objects;
  - conditional members appear only when their condition holds (`pagination` on paged reads
    only, no `nextCursor` on the last page, `ioDetails` only with `includeIoDetails`);
  - every other member is present, including explicit nulls.

**Contract changes:** none, and no member was made nullable.

**Not covered live:**
- The `NetworkDeviceCreator` null fallback did not fire, because Openness returned the name and
  type. That branch is verified only by the build and the final review.
- The tag-match path's unguarded Openness strings were not exercised with a null.

The copy was left bound, with the added `MCP1b_Station` device and unsaved changes;
`network_write` never saves.
