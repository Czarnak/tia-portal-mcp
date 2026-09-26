# PR 2 Network Phase 5 Fixture Qualification and Compile Contract Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Qualify real PROFINET and DP IO-system fixtures, safe edit and restoration values, and the narrowest hardware compile target; freeze the Phase 5 contract without shipping a public write operation.

**Architecture:** Keep public discovery on `network_read`. Reuse the existing worker-only `probe_network_object_attributes` for raw metadata. Add one temporary, guarded worker-only qualification operation for an exact IO-system selector, one attribute change at a time, and hardware compilation of the proven master PLC `DeviceItem` on its controller interface owner path. Record the live matrix and the contract for the later public slices.

**Tech Stack:** C# / .NET 10 host, netstandard2.0 contracts, net48 Siemens Openness worker, xUnit, PowerShell, TIA Portal V21.

**Spec:** [Network Phases 4-6 completion and delivery design](../specs/2026-09-21-network-phases-4-6-delivery-design.md), especially PR 2 and the shared live-evidence rules.

**Planning baseline:** `main` at `10077c9a1a6eb48118097da2bed55d00bdec1487`. Recheck this before execution. The merged Phase 4 [acceptance report](../acceptance/reports/2026-09-21-network-phase4-current-revision-live.md) is a bounded live PASS; it does not establish IO-system attribute or hardware-compile behavior.

## Global Constraints

- This PR is qualification and contract work. It adds no public MCP tool or `network_write` operation. `network_read` remains the public inspection path.
- Keep the exact disposable project filename, absolute path, and project name out of every tracked document, test fixture, source comment, and commit message. Call it **Fixture A**. Store its exact path, selectors, and unredacted protocol records only in ignored local evidence; a report may use a path hash and non-identifying aliases.
- The read-only planning inspection on 2026-09-24 observed one selectable PROFINET IO system and one selectable DP master system in Fixture A. Their modeled `Name` and `Number` were available and read/write. PROFINET `MultipleUseIoSystem` and `UseIoSystemNameAsDeviceNameExtension` were available and read/write; `MaxNumberIWlanLinksPerSegment` was an unknown attribute in that fixture. The attached MCP executable was not proven to be built from this `main`, so repeat the inventory on the frozen PR 2 candidate.
- The only non-deferred PROFINET dynamic candidates are `MultipleUseIoSystem`, `UseIoSystemNameAsDeviceNameExtension`, and `MaxNumberIWlanLinksPerSegment`. The last stays excluded unless another live fixture proves its metadata, safe value, and restoration. No DP dynamic attribute enters scope.
- Siemens V21 documents `DeviceItem` compilation as hardware and `Device` compilation as hardware plus software. Prove the IO controller interface's exact, unique owning `DeviceItem`, then choose the deepest strict ancestor `DeviceItem` on that owner path that directly hosts the unique master `PlcSoftware` through `SoftwareContainer` and exposes `ICompilable`. Confirm that the containing `Device` has exactly one PLC software instance. Fail closed on absent, ambiguous, or unreadable PLC role, ancestor identity, service, or pre/post continuity. Never fall back to the containing `Device` or another network participant. The existing `compile_check` is a PLC-software path, not this gate.
- The read-only PN-B and DP-B evidence at `6cc1d67` found a unique interface owner, `ICompilable` on its immediate strict ancestor, and one PLC software instance in the containing Device. It did not prove that the ancestor directly hosts the master PLC software; no Compile or Apply is authorized by that candidate evidence.
- Every executable change uses focused RED, smallest GREEN, then relevant/full tests. An offline, FakeWorker, stub, or reference build is not live TIA evidence. A live mutation is never a TDD RED.
- A live compile or mutation needs fresh authorization for the exact disposable copy, IO-system selector, attribute, requested value, and restoration/discard route immediately before execution. Approval of this plan does not grant that authorization.
- The guarded probe never saves, downloads, commissions, changes PLC mode, or retries after ambiguous transport loss. Its script performs a read-only Preview before Apply and binds Apply to the unchanged exact request, current value, candidate, and session. This internal diagnostic does not introduce a parallel public safety-token mechanism; PR 3 must use the shared `CanonicalWriteSafety` gate. Keep safety tokens, credentials, exact paths, and unredacted raw records out of tracked artifacts.
- Freeze a clean executable candidate before live use. A later change to production, test, or harness source invalidates the live evidence. A documentation-only evidence commit is allowed after an audit proves those paths unchanged.
- Do not tag, release, publish a package, or start PR 3 planning before PR 2 merges and its evidence is reviewed.

Primary API references: [V21 compilation and supported target scopes](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-for-projects-and-project-data/compiling-a-project?contentId=Lh19wmmzQOEBZSyi3sS~RQ), [PROFINET IO-system attributes](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/accessing-attributes-of-a-profinet-io-system?contentId=_NPxKEJbHcdixMSyJc3zWg), and [DP master-system attributes](https://docs.tia.siemens.cloud/r/en-us/v21/tia-portal-openness-api-for-automation-of-engineering-workflows/tia-portal-openness-api/functions-on-networks/accessing-attributes-of-a-dp-mastersystem?contentId=qPCN8leT2RJPiJ_H7L30nw). Treat those as API guidance; the fixture-specific truth comes from Task 5.

## Review Focus

1. **Wrong project or stale session:** the probe refuses a mismatched `ExpectedSessionIdentity` before a setter or compile; Task 2 tests the policy and Task 4 tests the harness guard.
2. **Selector drift after `Name` or `Number` changes:** a restore call uses the new canonical selector and exact new expected value; Tasks 3 and 5 check this.
3. **Wrong compile scope:** an absent or ambiguous interface owner, master PLC host, compiler service, or identity continuity fails closed; Task 3 tests the separate owner and compiler selectors, and Task 5 records live scope.
4. **Accepted edit followed by compile error:** preserve `mutationCommitted: true`, applied state, bounded compile diagnostics, and a stop-before-next-operation decision; Tasks 3 and 6 pin the contract.
5. **Dynamic side effects:** compare all five candidate attributes before and after a one-field change; exclude a candidate if unrelated values or device names change without a safe, proven restoration path; Task 5 records the decision.

## File Map

- Create `TiaMcpServer.Contracts/IoSystemQualificationProbeInfo.cs`: closed worker-only request, scalar, and result DTOs.
- Modify `TiaMcpServer.Contracts/WorkerRequest.cs`: one optional probe request field.
- Modify `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`: classify `probe_io_system_qualification` as `ProjectMutation`, requiring expected session identity and read-write mode.
- Create `TiaMcpServer.Tests/Network/IoSystemQualificationProbeContractTests.cs`: request shape, allowlist, type, access-mode, and session guards.
- Create `TiaMcpServer.OpennessWorker/Openness/IoSystemQualificationProbeService.cs`: exact resolution, separate interface owner and master PLC hardware target, one-field edit, post-read, and bounded hardware compile evidence.
- Modify `TiaMcpServer.OpennessWorker/Program.cs`: dispatch the worker-only probe; never register it in the public catalog.
- Create `TiaMcpServer.Tests/Network/IoSystemQualificationWorkerContractTests.cs`: transaction/compile ordering and fail-closed worker-source contract where Siemens behavior cannot be simulated.
- Create `scripts/live-test-network-phase5-qualification.ps1`: guarded Inventory, Compile, and one-field Apply/restore evidence modes against a frozen candidate.
- Create `TiaMcpServer.Tests/Network/NetworkIoSystemQualificationLiveHarnessScriptTests.cs`: parse and guard tests that never execute the script.
- Create `docs/superpowers/specs/2026-09-24-network-phase5-qualified-contract.md` after qualification: reviewed Phase 5 scope and exact public failure contract.
- Create `docs/superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md` after the live run: sanitized candidate, fixture, metadata, compile, and restoration matrix.
- Modify `docs/ARCHITECTURE.md`, `docs/roadmap/network-operations.md`, `docs/IMPROVEMENT_LOG.md`, `docs/README.md`, and `docs/superpowers/README.md` to identify the temporary worker-only probe, the evidence boundary, and the new documents. Do not mark `update_io_system` as supported.

### Task 1: Re-establish current-main fixture and source provenance

**Files:** None tracked. Write raw records only under `artifacts/live-network-phase5-pr2/`, which is ignored.

**Interfaces:** Consumes the merged PR 1 report and `network_read`. Produces a private fixture manifest with aliases PN-A and DP-A, exact IO-system and controller interface owner selectors, baseline values, project-path hash, and a separate proposed master PLC compiler target for each.

- [ ] **Step 1: Verify the base.** Run `git status --short --branch`, `git rev-parse HEAD`, and `git diff --check`; require a clean `main` descended from the planning baseline. Review any later `main` changes before using this plan.
- [ ] **Step 2: Read only through the built current-main MCP.** Use paged `network_read.list_network_objects` for `subnet` and `ioSystem`, then `inspect_network_object` on PN-A and DP-A. Capture complete returned selectors, subnet `NetworkType`, all five PROFINET candidate attributes, DP `Name`/`Number`, availability, access, type, value, and diagnostics. Read `get_project_status` before and after; require the same canonical project path and `isModified=false`.
- [ ] **Step 3: Use the existing worker-only raw metadata probe.** Query the two exact selectors with `probe_network_object_attributes`. Compare `supportedClrTypeNames`, observed CLR value type, and access mode against public inspection; preserve disagreements as exclusions or open findings, never normalize them away.
- [ ] **Step 4: Identify owner and compiler candidates.** Correlate each IO system's parent/controller evidence with the paged hardware tree and exact controller interface `deviceItem` selector. Separately record candidate strict ancestor selector, PLC software identity and direct `SoftwareContainer` host evidence, containing-Device PLC count, and compile service. Keep full selectors and paths in the ignored manifest. If any proof is absent or ambiguous, stop before Task 3's compile call and record the gap. The `6cc1d67` ancestor-service and Device-wide PLC-count observations alone do not complete this proof.
- [ ] **Step 5: Audit the read-only evidence.** Record candidate commit/tree, host/worker hashes, Portal V21 version/build, fixture path hash, timestamps, and `isModified=false`. The earlier attached-MCP read is reconnaissance only; it cannot substitute for this candidate proof.

### Task 2: Add a closed worker-only probe request and policy

**Files:** Create `TiaMcpServer.Contracts/IoSystemQualificationProbeInfo.cs`, `TiaMcpServer.Tests/Network/IoSystemQualificationProbeContractTests.cs`; modify `TiaMcpServer.Contracts/WorkerRequest.cs`, `TiaMcpServer.Contracts/OperationPolicyCatalog.cs`.

**Interfaces:** `WorkerRequest.IoSystemQualification` carries `Mode` (`compileBaseline` or `setAndCompile`), the exact `NetworkObjectSelectorInfo` IO-system target, and for `setAndCompile` one `AttributeName`, `ExpectedValue`, and `DesiredValue`. The scalar has a closed `Kind` (`string`, `integer`, `boolean`) with exactly one corresponding non-null value. Existing `WorkerRequest.Confirm` and `ExpectedSessionIdentity` are mandatory for this method. `IoSystemQualificationProbeValidator.Validate(WorkerRequest)` returns a validation message or `null`; Task 3 calls it before any Siemens object access.

- [ ] **Step 1: Write focused RED tests.** Add theories that reject `confirm=false`, missing expected session identity, a non-`ioSystem` target, blank `subnetId`, missing/negative `number`, extra selector fields, unsupported attribute names, mismatched scalar kinds, equal expected/desired values, and value fields in `compileBaseline`. Assert `OperationPolicyCatalog.GetCapability("probe_io_system_qualification") == OperationCapability.ProjectMutation` and that no public network catalog advertises the method.

    ```csharp
    var request = new WorkerRequest
    {
        Method = "probe_io_system_qualification",
        Confirm = false,
        IoSystemQualification = new IoSystemQualificationProbeInfo
        {
            Mode = "compileBaseline",
            Target = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.IoSystem,
                SubnetId = "subnet-a",
                Number = 1
            }
        }
    };
    Assert.NotNull(IoSystemQualificationProbeValidator.Validate(request));
    Assert.Equal(OperationCapability.ProjectMutation,
        OperationPolicyCatalog.GetCapability(request.Method));
    ```
- [ ] **Step 2: Run only the new tests.** Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~IoSystemQualificationProbeContractTests`; require failures attributable to the absent DTO/policy, not infrastructure.
- [ ] **Step 3: Implement the minimum contract.** The closed attribute vocabulary is `Name:string`, `Number:integer`, `MultipleUseIoSystem:boolean`, `UseIoSystemNameAsDeviceNameExtension:boolean`, and `MaxNumberIWlanLinksPerSegment:integer`. Keep mode and field comparisons ordinal. Reject every other field and any unsupported scalar shape before worker dispatch.

    ```csharp
    public sealed class IoSystemQualificationScalarInfo
    {
        public string Kind { get; set; } = string.Empty;
        public string? StringValue { get; set; }
        public int? IntegerValue { get; set; }
        public bool? BooleanValue { get; set; }
    }
    // Validate requires exactly one value member matching Kind, plus a
    // same-kind expected/desired pair for setAndCompile.
    ```
- [ ] **Step 4: Re-run focused tests and refactor.** Repeat the Step 2 command. Keep this DTO worker-only; do not modify `NetworkOperationCatalog`, `network_read`, or `network_write` schemas.

### Task 3: Implement exact worker qualification and hardware compile

**Files:** Create `TiaMcpServer.OpennessWorker/Openness/IoSystemQualificationProbeService.cs`, `TiaMcpServer.Tests/Network/IoSystemQualificationWorkerContractTests.cs`; modify `TiaMcpServer.OpennessWorker/Program.cs`. Add only the test-project source links needed for Siemens-free validation helpers.

**Interfaces:** `Program` dispatches only the literal `probe_io_system_qualification`, calls the existing session/project validation path, and then calls `IoSystemQualificationProbeService.CompileBaseline` or `.SetAndCompile`. The service returns a typed bounded result with original and post-write IO-system selectors, separate verified controller interface owner and compiler-target selectors/identity evidence, per-attribute before/after observations, compile state/error/warning counts, bounded messages, `mutationCommitted`, and restoration guidance. Task 4 serializes this result to private evidence.

- [ ] **Step 1: Write focused RED tests.** Pin dispatch, the protected-operation classification, one exact selector resolution, unique controller interface owner, and separate deepest strict ancestor compiler target that directly hosts the unique master `PlcSoftware` and exposes `ICompilable` (never a first match). Fail closed on absent/ambiguous/unreadable role, identity, service, or pre/post continuity; test no setter on expected-value/metadata mismatch and the order `ExclusiveAccess` -> target proof -> transaction -> setter -> `CommitOnDispose` -> post-read/re-proof -> `ICompilable.Compile()`. Add a test that no compile is invoked before commit. Source-contract assertions are appropriate only for Siemens-specific ordering that cannot run without Portal; test pure validation and result bounding as executable logic.

    ```csharp
    var mutationBody = ExtractMethodBody(source, "SetAndCompile");
    var commit = mutationBody.IndexOf("transaction.CommitOnDispose();", StringComparison.Ordinal);
    var compile = mutationBody.IndexOf("CompileHardware(", StringComparison.Ordinal);
    Assert.True(commit >= 0 && compile > commit);
    Assert.DoesNotContain("project.Save(", source, StringComparison.Ordinal);
    ```

    Implement `ExtractMethodBody` in the test file using the same balanced-brace source extraction pattern as `NetworkSubnetLifecycleWorkerServiceContractTests`; it must isolate the named method rather than search the whole service.
- [ ] **Step 2: Observe RED.** Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~IoSystemQualificationWorkerContractTests`; retain the failure reason.
- [ ] **Step 3: Implement baseline compile.** Resolve the IO system by the existing `NetworkObjectSelectorResolver` and prove its unique controller interface owner. On that owner's path, select the deepest strict ancestor `DeviceItem` that directly hosts the unique master `PlcSoftware` through `SoftwareContainer` and has `ICompilable`; confirm exactly one PLC software instance in the containing `Device`. Re-resolve both selectors and software identity immediately before compilation. Return bounded owner, target, and compiler results. Fail closed if any proof or continuity is missing or unreadable; never substitute PLC software, the containing `Device`, or another participant. `compileBaseline` has no setter and no project save.

    ```csharp
    var compiler = ((IEngineeringServiceProvider)provenMasterPlcDeviceItem)
        .GetService<ICompilable>();
    if (compiler is null)
        throw new WorkerOperationException(
            WorkerFailureCategories.WorkerOperationFailed,
            "The proven master PLC hardware item has no compile service.");
    var compilerResult = compiler.Compile();
    ```
- [ ] **Step 4: Implement one-field set and compile.** Under exclusive access, re-resolve the IO system, unique interface owner, and proven master PLC compiler target/software identity before the setter; re-read the exact expected value and writable metadata and take a five-attribute before snapshot. Set only the requested field inside one transaction, and request commit only after the setter succeeds. After commit, resolve/read the applied state, compute the new canonical selector when `Name` or `Number` changed, and re-prove the same interface owner, compiler `DeviceItem`, and PLC software identity before compiling. A post-commit proof or compile failure remains a known committed mutation (`mutationCommitted: true`) in the internal result. If transport fails before a response, the outcome is unknown and the caller must inspect, not retry.

    ```csharp
    using (var exclusive = portal.ExclusiveAccess())
    {
        var target = RequireExactIoSystem(project, request.Target);
        var ownerAndCompiler = RequireMasterPlcCompilerTarget(project, target);
        var before = ReadFiveAttributeSnapshot(target);
        RequireExpectedValueAndWritableMetadata(before, request);
        using (var transaction = exclusive.Transaction(project, "Qualify IO system"))
        {
            ApplySingleField(target, request);
            transaction.CommitOnDispose();
        }
        var applied = ReadAppliedStateAndNewSelector(project, request);
        var verified = RequireSameMasterPlcCompilerTarget(project, applied.Target, ownerAndCompiler);
        var compile = CompileHardware(verified.CompilerDeviceItem);
    }
    ```

    These named helpers are private service methods. `ReadAppliedStateAndNewSelector` must not reuse a precommit object reference after an identity edit.
- [ ] **Step 5: Bound evidence.** Cap message count, per-message text, and total serialized result; report omitted counts. Avoid raw exception dumps, secrets, or project paths in returned diagnostics. Preserve enough state to distinguish precommit rejection, committed compile error, and compile success.
- [ ] **Step 6: Run focused and integration tests.** Repeat Step 2, then run the relevant worker authorization, network selector, and payload tests. Review the diff for any accidental public registration or direct Siemens call in the net10 host.

### Task 4: Guard and contract-test the live procedure

**Files:** Create `scripts/live-test-network-phase5-qualification.ps1` and `TiaMcpServer.Tests/Network/NetworkIoSystemQualificationLiveHarnessScriptTests.cs`.

**Interfaces:** The script consumes a gitignored candidate manifest, an explicit absolute `.ap21` path, and an exact approved PN-A or DP-A selector. `Inventory` and `Preview` are non-mutating; Preview records the exact proposed one-field request, current value, and separate interface owner and master PLC compiler-target selectors/evidence in ignored records. `Compile` and `Apply` require both an explicit effectful switch and an exact confirmation phrase that names the mode and fixture alias. Compile and Apply reject any owner, compiler-target, or PLC software identity drift; Apply also rejects drift from the Preview request or baseline. It writes ignored, timestamped JSON and a redacted console summary.

- [ ] **Step 1: Write parser/guard RED tests.** Parse the script with PowerShell's parser without running it. Require a non-mutating default; reject missing path, mismatched candidate SHA, unexpected project/session identity, missing confirmation phrase, unsupported field, a second field in one Apply, or missing/drifted owner, compiler-target, or PLC software identity. Assert the script never invokes `save_project`, download, PLC mode control, or public `network_write` for qualification.
- [ ] **Step 2: Run focused tests and observe RED.** `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~NetworkIoSystemQualificationLiveHarnessScriptTests` must fail because the guarded procedure is absent.
- [ ] **Step 3: Implement the minimum script.** Follow the existing Phase 4 harness's candidate hash, path identity, token redaction, and ignored-artifact patterns. Run public `network_read` for Inventory/Preview and direct worker IPC only for the two worker-only probes. Carry separate owner and compiler-target selectors and PLC software identity through Preview, Compile, and Apply; fail closed on missing proof or drift. Recheck candidate commit/tree and script SHA before launching the worker. Record the exact `ExpectedSessionIdentity` in the private request, never in tracked docs.

    ```powershell
    if ($Mode -in @('Compile', 'Apply') -and
        (-not $AllowEffectfulQualification -or
         $ConfirmationPhrase -cne "QUALIFY FIXTURE A $Mode")) {
        throw 'Effectful qualification requires its exact confirmation phrase.'
    }
    # Apply also compares the current candidate, session, selector, and
    # one-field request with the ignored Preview record before worker IPC.
    ```
- [ ] **Step 4: Re-run focused tests and a parser check.** Neither action may execute Compile or Apply. Include a static test that the two public network tools and their schemas did not change.

### Task 5: Freeze the executable candidate and run the authorized live matrix

**Files:** No tracked edits during the live run. Write raw artifacts under `artifacts/live-network-phase5-pr2/`.

**Interfaces:** Consumes the final Tasks 2-4 tree and explicit authorization for the exact disposable copy and chosen one-field values. Produces an immutable matrix for Task 6.

- [ ] **Step 1: Finish offline gates.** Run focused tests, the full suite, both serial builds, coverage threshold, PowerShell parser checks, and `git diff --check`. Record pass/fail counts, warnings, and exact candidate SHA/tree. Do not call these live evidence.

    ```powershell
    dotnet test TiaMcpServer.Tests
    dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
    dotnet build TiaMcpServer.sln -m:1 /p:TiaPortalV21Dir="C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48"
    pwsh -NoProfile -File scripts/verify-coverage-threshold.ps1
    git diff --check
    ```
- [ ] **Step 2: Freeze a clean candidate.** Require clean production, test, and harness paths. Hash the host, worker, script, and candidate manifest. The script must reject any later mismatch instead of rewriting the manifest.
- [ ] **Step 3: Obtain fresh exact-target authorization.** Present the privately recorded project path, PN-A/DP-A selectors, each proposed original/new value, separate controller interface owner and proven master PLC compiler-target selectors/identity evidence, and restoration or discard route. Do not infer authorization from this plan or the earlier read-only inspection.
- [ ] **Step 4: Establish baseline.** Confirm exact path/session and `isModified=false`; run public Inventory and raw metadata. Prove the unique interface owner, direct master PLC software host, containing-Device PLC count, compiler service, and target continuity before the authorized baseline Compile of that master PLC `DeviceItem` for each IO system. Read project status again. If compile changes the project state, use a fresh discarded/reopened disposable copy before the first setter. If baseline compilation already fails, retain its diagnostics and do not attribute a later error to the tested edit without a distinguishable delta. Do not mutate or restore until the baseline passes; stop for design review if target proof fails.
- [ ] **Step 5: Qualify PN-A `Name`.** Take a fresh read-only Preview and exact authorization, choose a collision-free temporary name from the inventory, apply once, record the new selector, five-attribute and affected-device snapshots, post-read/compile, then restore using that new selector and compile/read again. Exclude the field if an unintended side effect has no proven restoration.
- [ ] **Step 6: Qualify PN-A `Number`.** Repeat Step 5 with a free candidate number proven by current inventory. Record the changed selector and compile result. Do not intentionally choose a known-invalid value to manufacture a failure.
- [ ] **Step 7: Qualify DP-A `Name`.** Use the DP fixture's exact selector, independent Preview, authorization, post-read, compile, and restoration/discard proof.
- [ ] **Step 8: Qualify DP-A `Number`.** Use a free DP number from current inventory and the same independent evidence and restoration gates.
- [ ] **Step 9: Qualify `MultipleUseIoSystem`.** Require matching live raw metadata, a safe alternate boolean, before/after snapshots of all five attributes and affected device names, post-read, compile, and restoration/discard proof. Exclude it if a side effect cannot be safely restored.
- [ ] **Step 10: Qualify `UseIoSystemNameAsDeviceNameExtension`.** Use its own Preview and authorization. Re-read `MultipleUseIoSystem` and writable metadata immediately before the setter; reject the request if its dependency makes the attribute non-writable. Capture the same snapshots and restoration evidence as Step 9.
- [ ] **Step 11: Decide `MaxNumberIWlanLinksPerSegment`.** Keep it excluded while the current fixture reports `unknownAttribute`. Only a separate exact fixture with readable/writable metadata and safe restoration can change that verdict. Do not test a DP dynamic attribute.
- [ ] **Step 12: Audit evidence boundaries.** Verify the final project state or documented discard, the audit trail, all candidate hashes, result-message bounds, no project save/download, and no production/test/harness edit after the frozen live candidate. Mark each field approved or excluded with a specific reason. If a safe change naturally produces a compile failure, retain the known-committed evidence; otherwise mark that live failure path unverified for PR 3's acceptance gate.

### Task 6: Freeze the Phase 5 contract and publish durable PR 2 evidence

**Files:** Create `docs/superpowers/specs/2026-09-24-network-phase5-qualified-contract.md` and `docs/superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md`; modify the documentation files in the File Map.

**Interfaces:** Produces the reviewed contract that PR 3 and PR 4 will consume. It does not implement `update_io_system`.

- [ ] **Step 1: Write the candidate matrix.** For PN and DP modeled fields and each of the three named PN dynamic candidates, state observed CLR/wire type, modeled/dynamic source, access, availability, exact selector shape, tested alternate value class, separate controller interface owner and proven master PLC compile-target evidence, restoration outcome, and approved/excluded verdict. Store private exact values and raw traces in ignored artifacts; tracked docs use fixture aliases and sanitized evidence.
- [ ] **Step 2: Freeze the future public contract.** Specify old-selector safety binding; returned new selector after identity edits; transaction commit before hardware compile; no rollback or automatic retry; `postcondition_failed` with `mutationCommitted: true`, typed applied before/after state and bounded compile evidence for a known committed compile failure; ordinary failure `result: null`; ambiguous transport outcome with inspect-before-retry guidance; sequential batch stop and audit semantics. Include the exact field vocabulary and applicability proven by Task 5, without enlarging it from documentation alone.
- [ ] **Step 3: Update current docs and indexes.** State that Phase 5 editing is still unshipped. Link both new historical documents in `docs/README.md` and `docs/superpowers/README.md`. Describe the temporary worker-only probe in architecture and its planned retirement with the final live-harness cleanup. Update the roadmap and improvement log with the qualification result and remaining gaps.
- [ ] **Step 4: Validate documentation.** Run `git diff --check`; verify every new document is indexed, links resolve, the exact project filename/path/name is absent, and no acceptance sentence promotes the preliminary attached-MCP read or an offline build to current-candidate live evidence.
- [ ] **Step 5: Final PR 2 review.** Compare the final diff with the frozen candidate, verify any post-live change is evidence/documentation only, and check the report against raw hashes. PR 2 is merge-ready only when both PN and DP fixtures, compile scope, safe restoration/discard, and every candidate verdict are evidenced. Any product defect found live gets a dedicated TDD repair PR under the delivery design before successor planning.

## Execution Handoff

Implement PR 2 only after this plan is reviewed. Obtain fresh exact-target authorization immediately before the Task 5 live Compile or Apply modes. After PR 2 merges and its final evidence is reviewed, write the PR 3 plan from the new `main` rather than reusing assumptions from this document.
