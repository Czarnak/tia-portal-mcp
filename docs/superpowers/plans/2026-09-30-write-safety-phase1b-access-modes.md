# Write-Safety Redesign Phase 1b — Access Modes Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax. Delegation requires session authorization; commits and remote writes require an explicit user request.

**Goal:** Add a capability-based `full` access mode, enforce the narrower `read-write` ceiling, and register the confirmation setting for subsequent guarded-tool migrations.

**Architecture:** Keep the shared capability catalog as the host/worker authority. Share tool registration between production startup and protocol tests, propagate the exact mode to the worker, and resolve confirmation once at startup. Existing writes keep their token flow until their migration phase.

**Tech Stack:** .NET 10 host/tests, netstandard2.0 contracts, net48 Openness worker, MCP SDK, xUnit and FakeWorker; no new dependencies.

**Spec:** [Accepted write-safety redesign](../specs/2026-09-29-write-safety-redesign-design.md), §§4.6, 4.11 and Phase 1b.

**Status:** Approved for implementation by the maintainer, 2026-09-30. Branch: `feature/full-access-mode`, based on local `main` at `6e152fa928dc171c264b7baaf5eb232b51a76871` (Phase 1 merged through PR #98). Commit after each implementation or fixing step and use subagents as independent reviewers. Remote writes and live TIA execution require separate authorization.

## Global Constraints

- Three-layer enforcement: tool discovery, host authorization before worker activity, worker authorization before handler dispatch.
- Capability classes, not tool-name permission lists, define the ceiling.
- `read-only` behavior and its four public tools remain unchanged. Keep `SafetyRead` while legacy token flows use it; deletion belongs to later phases.
- Preserve the server/worker startup default of `read-write`; its permissions narrow. Preserve the install command's separate `read-only` default.
- CLI access mode overrides `TIA_MCP_ACCESS_MODE`; equivalent repeated values remain valid and conflicting values remain errors.
- `--confirm-with-user` defaults to on. The maintainer selected bare `--confirm-with-user` for on and `--confirm-with-user=false` for off in this discussion.
- No `--user-approval`, approval marker, new token surface, new tool schema, dependency change, version bump or release tag.
- Build serially: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true` from PowerShell.
- Live TIA execution is not part of this plan's acceptance gate. FakeWorker and linked worker-authorization tests do not prove live Siemens behavior.

## Agreed intent and proposed implementation decisions

The agent may edit and compile the project the human is working in, while the human owns persistence in `read-write`. Explicit lifecycle operations and PLC runtime control require `full`. Startup `--project`, an initially unattached read's `projectPath` (`OpenRequested`), and attachment to the project already open in TIA remain available in `read-write`; these paths must not switch or close an existing project.

| Mode | Allowed capabilities | Current public surface |
| --- | --- | --- |
| `read-only` | `Observe`, `TemporaryExport`, transitional `SafetyRead` | Four read tools |
| `read-write` | Read capabilities plus `Compile`, `ProjectMutation` | Eight tools: reads, `compile_check`, legacy write batch pair, `network_write` |
| `full` | Read-write capabilities plus `ProjectLifecycle`, `OnlineControl` | Fourteen tools: additionally six lifecycle tools; PLC control remains inside the legacy batch surface |

The following choices make the accepted design concrete and were implemented with the execution decisions below:

- Append `McpAccessMode.Full`, preserving existing enum values. Deny unknown operations and invalid enum values in every preset; `full` permits all **classified** capabilities.
- Use one contracts helper for spelling/parsing mode names across host, worker, diagnostics and audit. Do not add a `--full` alias: `--access-mode full` and `TIA_MCP_ACCESS_MODE=full` suffice.
- Share production tool registration with the MCP test harness rather than maintain a second registration branch in tests.
- Confirmation is a server-startup setting in Phase 1b. Phase 2 connects it to guarded lifecycle calls, as the spec's delivery section states. Phase 1b does not add elicitation to legacy token tools or alter `WriteExecution`'s acknowledgement behavior.
- Accept `--confirm-with-user=true|false` and bare `--confirm-with-user`. Do not add an environment variable, positional boolean form, negated alias, installer option, or doctor option for confirmation in this phase.

## Review Focus

1. PLC start/stop inside an otherwise allowed batch must be rejected before snapshots or mutations; hiding lifecycle tools alone is insufficient. Task 2 owns the test.
2. A direct lifecycle method call, including preview, must not bootstrap, recover or rebind before its access check. Task 2 owns the test.
3. `full` must survive worker launch/restart and appear as `full` in audit/diagnostics, rather than be relabeled `read-write`. Tasks 1 and 2 own the tests.
4. Read-write initial attachment must remain possible, but a read naming another project must not switch or close the active project. Task 2 owns the regression tests.
5. Malformed or conflicting confirmation values must fail startup and never silently disable confirmation or consume the next option. Task 4 owns the tests.

## Task 1: Shared presets and mode names

**Files:** Modify `TiaMcpServer.Contracts/McpAccessMode.cs`, `OperationCapability.cs`, `OperationPolicyCatalog.cs`. Create `TiaMcpServer.Contracts/McpAccessModeNames.cs`. Create `TiaMcpServer.Tests/Safety/AccessModeTierTests.cs`; update catalog expectations in `ReadOnlyModeTests.cs`.

**Interfaces:** Keep `OperationPolicyCatalog.IsAllowed(McpAccessMode mode, string operation)`. Add `McpAccessModeNames.ToName(McpAccessMode mode) -> string` and `TryParse(string? value, out McpAccessMode mode) -> bool`; exact names are `read-only`, `read-write`, `full`. `ToName` throws `ArgumentOutOfRangeException` for invalid enum values; `TryParse` trims and compares case-insensitively.

- [x] Write `Presets_EnforceEveryKnownCapability`, iterating every catalog operation and asserting the table above. Explicitly assert `compile_check` and `update_tag` are allowed in read-write; all six lifecycle names, `start_plc`, `stop_plc`, and the two internal lifecycle probes are denied there and allowed in full.
- [x] Write `UnknownOperations_AndInvalidModes_AreDenied`: empty/unknown operations are denied in all three modes; `(McpAccessMode)999` denies a known mutation. Write `ModeNames_RoundTripAllPresets` and `ModeNames_RejectUnknownValues` for all three exact strings, trimmed/case variants, and invalid enum values.
- [x] Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~AccessModeTierTests`; observe relevant failing assertions before changing production code.
- [x] Append `Full`; replace the read-write unconditional allow with immutable capability presets. Keep current operation classifications and expected-session-identity rules. Implement the names helper and update comments describing the ceiling.
- [x] Rerun the focused tests and `ReadOnlyModeTests`; split the former “read-write allows all” expectation into read-write ceiling and full classified-operation coverage. Preserve all read-only assertions.

## Task 2: Host/worker enforcement and exact propagation

**Files:** Modify `TiaMcpServer/Safety/OperationAccessPolicy.cs`, `TiaMcpServer/Batch/BatchOperationCatalog.cs`, `TiaMcpServer/Network/NetworkOperationCatalog.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer.OpennessWorker/WorkerOperationAuthorization.cs`, `TiaMcpServer/Tools/ProjectWriteTools.cs`, `TiaMcpServer/Safety/Pipeline/WriteAudit.cs`, and the test-only launch recorder in `TiaMcpServer.FakeWorker/Program.cs`. Create `TiaMcpServer.Tests/Safety/ReadWriteModeCeilingTests.cs`. Update `ReadOnlyModeHardeningTests.cs`, `Worker/OpennessWorkerClientIntegrationTests.cs`, `Safety/Pipeline/JsonlWriteAuditSinkTests.cs` and existing lifecycle protocol fixtures as needed.

**Interfaces:** Existing `Authorize`, `ValidateAccessMode`, worker `ParseAccessMode` and `AllowsTiaConfirmations` signatures remain. All mode labels and the transport's `--access-mode <value>` use Task 1's names helper. `WriteAuditRecord.ModeName` keeps its signature and delegates spelling to that helper. The FakeWorker fixture optionally appends `{ processId, args }` JSONL at startup to a temporary path from test-only `TIA_MCP_FAKE_WORKER_LAUNCH_LOG`; it does nothing when absent and never writes logging data to protocol stdout. Restore this test environment value and serialize its tests using the existing fixture rules.

- [x] Write `ReadWrite_DeniesLifecycleBeforeTransport` and `ReadWrite_DeniesOnlineControlBeforeTransport`: for every prohibited operation, assert `access_denied` and zero worker requests/process startup. Exercise a mixed legacy batch containing an allowed mutation and `start_plc` or `stop_plc`; reject the whole request before collecting snapshots or applying the allowed item.
- [x] Write `LifecyclePreview_ReadWriteCannotBootstrapOrRebind`: invoke each lifecycle entry point directly under read-write with a worker executable that would fail if launched; assert denial and unchanged binding, including configured-unverified and invalidated open-project bindings.
- [x] Write `WorkerAuthorization_ReadWriteCeilingMatchesHost` using the linked real `WorkerOperationAuthorization` implementation. Full permits classified lifecycle/control methods; read-only still denies compile/mutation/lifecycle/control; malformed and conflicting explicit worker modes still fall back to read-only.
- [x] Add `WorkerLaunch_PropagatesFullAcrossRestart`: use the FakeWorker launch recorder and existing restart fixture, observing different process IDs and `--access-mode full` in both argument arrays. Follow existing binding invalidation/re-grounding rules after restart. Add `AuditMode_FullIsNotReadWrite`. Keep TIA dialog auto-confirmation disabled in read-only and enabled in both writable modes; this worker setting is separate from MCP elicitation.
- [x] Extend existing FakeWorker binding scenarios with `ReadWrite_InitialAttachmentStillWorks` and `ReadWrite_ReadCannotSwitchAttachedProject`: startup path/read path/UI-open attachment succeeds where currently supported; another requested project fails with `binding_conflict`, and no open/switch/close request is issued. Preserve read-only opening restrictions.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ReadWriteModeCeilingTests|FullyQualifiedName~ReadOnlyModeHardeningTests|FullyQualifiedName~OpennessWorkerClientIntegrationTests|FullyQualifiedName~JsonlWriteAuditSinkTests"`; confirm failures identify the old shortcuts.
- [x] Remove `BatchOperationCatalog.ValidateAccessMode`'s read-write early return. Use shared presets for each operation. Put lifecycle capability checks at the six public entry points before any bootstrap, recovery, snapshot or token work, retaining existing legacy result formatting. Correct mode-specific errors in host/batch/network/worker paths, exact worker launch spelling, worker parsing and audit labels.
- [x] Rerun the tests. Update lifecycle/full-access fixture setup to explicitly use `Full`; keep in-project edit/network fixtures on `ReadWrite`. Do not change production defaults or weaken binding/identity checks to repair tests.

## Task 3: Production tool discovery uses the ceiling

**Files:** Create `TiaMcpServer/Tools/McpToolRegistration.cs`. Modify `TiaMcpServer/Program.cs`, `TiaMcpServer.Tests/TestSupport/McpProtocolTestHarness.cs`, `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`, tool mode descriptions, and `ReadOnlyModeTests.cs`. Create `TiaMcpServer.Tests/Tools/AccessModeDiscoveryTests.cs`.

**Interfaces:** Add `McpToolRegistration.WithAccessModeTools(this IMcpServerBuilder builder, McpAccessMode mode) -> IMcpServerBuilder`. Both `Program.Main` and `McpProtocolTestHarness.StartProductionSurfaceAsync` call it. Transport registration stays with its existing owner.

- [x] Write protocol `ToolsList_AdvertisesOnlyTheModeSurface`: exact current names/counts are four/eight/fourteen. All modes advertise `get_project_status`, `browse_project_tree`, `execute_read_batch`, `network_read`. Read-write/full additionally advertise `compile_check`, `preview_write_batch`, `apply_write_batch`, `network_write`. Only full advertises `open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, `close_project`.
- [x] Write `ToolsCall_CannotReachHiddenLifecycleTools`, calling every hidden lifecycle name through an MCP client under both restricted modes; assert the SDK rejects the call and the worker is untouched. Online-control rejection remains per-operation in Task 2, since those operations have no standalone advertised tool today.
- [x] Run `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~AccessModeDiscoveryTests`; establish the current discovery mismatch.
- [x] Move existing registration into the shared extension: always reads; compile/data writes when their capabilities are allowed; lifecycle tools when `ProjectLifecycle` is allowed. Add the explicit test source link for the new file if the existing project includes do not cover it. Replace both duplicated registration blocks and update descriptions to name writable tiers accurately.
- [x] Rerun discovery and existing tool-output conformance tests. Preserve all tool annotations, input/output schemas, legacy registrations and token signatures.

## Task 4: CLI plumbing and immutable confirmation setting

**Files:** Modify `TiaMcpServer/Cli/AccessModeParser.cs`, `HostArgumentFilter.cs`, `DoctorCliParser.cs`, `DoctorCommand.cs`, `Cli/Install/InstallCliParser.cs`, `Cli/Install/InstallCommand.cs`, `TiaMcpServer/Program.cs` and `TiaMcpServer.Tests/TiaMcpServer.Tests.csproj`. Create `TiaMcpServer/Cli/UserConfirmationParser.cs`, `TiaMcpServer/Safety/Pipeline/UserConfirmationOptions.cs`, and `TiaMcpServer.Tests/Cli/UserConfirmationParserTests.cs`. Update existing CLI/installer/host-filter tests and diagnostics mode reporting.

**Interfaces:** `UserConfirmationParser.Parse(string[] args) -> UserConfirmationParseResult`, a record with `bool IsValid`, `bool ConfirmWithUser`, `string? Error`. `UserConfirmationOptions(bool ConfirmWithUser = true)` is an immutable singleton in `TiaMcpServer.Safety.Pipeline`. Extend the existing host argument filter to remove the accepted confirmation forms after validation, preserving its existing callers.

- [x] Write confirmation tests: absent flag -> true; bare flag -> true; `=true` -> true; `=false` -> false; case-insensitive booleans accepted. Empty/unknown/`0`/`1` values -> invalid; equivalent repeats allowed; contradictory repeats -> invalid. Bare flag before `--project` remains true and leaves the project option intact.
- [x] Write `ConfirmationArguments_AreRemovedBeforeGenericHostParsing` for bare and equals forms, retaining unrelated project/logging arguments. Add a DI smoke test resolving `UserConfirmationOptions` after parsing no args and explicit false; assert true and false respectively.
- [x] Extend access-mode tests for `--access-mode full`, `--access-mode=full`, environment `full`, CLI-over-environment precedence, equivalent repeats, conflicts with existing aliases, and invalid values. Assert default remains read-write. Doctor reports `full`; install accepts `full` and forwards it to each existing client adapter while its default stays read-only. Restore environment values in `finally` and follow existing serialization rules for environment-mutating tests.
- [x] Run `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~UserConfirmationParserTests|FullyQualifiedName~ReadOnlyModeHardeningTests|FullyQualifiedName~InstallCliParserTests|FullyQualifiedName~ClientInstallerTests"`; observe failures for the new inputs.
- [x] Implement strict confirmation parsing, use Task 1's mode parser, update startup filtering, and register options before building the host. Print resolved settings only to stderr. Update help/diagnostics and install mode validation; do not add a confirmation installer/doctor option or a new environment variable.
- [x] Rerun CLI and discovery suites. Keep `UserConfirmation.For(McpServer, TimeSpan)` and its current outcome tests unchanged. Record the Phase 2 handoff: default-on ignores agent acknowledgement for acknowledgement-severity guards, requires elicitation support, and accepts only `accept` plus `confirm: true`; off uses exact-set agent acknowledgement. Phase 2 owns pipeline integration and that behavior's tests.

## Task 5: Documentation, review and acceptance

**Files:** Modify `README.md`, `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/guides/installation.md`, `docs/guides/mcp-client-configuration.md`, `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`, `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, `docs/IMPROVEMENT_LOG.md`, the design's status/delivery notes, and the two documentation indexes as appropriate.

- [x] Document all three tiers, the defaults, initial attachment behavior, and migration: configurations needing save/close/create/open/archive or PLC start/stop must select `full`. Explain that the eight-tool legacy read-write surface still contains the batch tool, but disallows its PLC-control operations.
- [x] Document confirmation syntax and the transitional boundary accurately: the setting is available now; guarded-tool migrations begin consuming it in Phase 2. Existing token tools still use their old flow. Client `accept` does not prove that a human saw a dialog.
- [x] Update mode/tool counts and user-facing descriptions wherever they describe the active surface. Keep README cross-document links absolute GitHub URLs, other document links relative, and all new documents indexed. Mark Phase 1b complete only after its code and acceptance checks pass.
- [x] Run the serial stub build, then `dotnet test TiaMcpServer.Tests` and the current CI coverage/documentation checks. Use the checks actually present in the current checkout; do not resurrect a removed script from an older plan. Expected: zero build/test failures and the repository's current coverage threshold met.
- [x] Review the whole diff against the capability table and five review-focus cases. Verify no unknown method, internal lifecycle probe, direct call, batch item or worker restart bypasses a tier; no mode is relabeled; no generic-host argument parsing regression; no premature token retirement or elicitation promise.
- [x] Record precise offline evidence in the improvement log. No release/package tag is cut during the migration window. Stop for plan review before implementation; do not commit, push, run live TIA or change client registrations without the applicable user authorization.

## Execution and review handoff

Execute Tasks 1-4 serially because policy, transport and discovery share contracts; finish with Task 5. One branch and one eventual Phase 1b PR are sufficient. Preserve unrelated workspace work. If independent agent review is requested later, review the whole finished diff, especially the discovery/dispatch seam and legacy batch ceiling.

The maintainer approved this plan and local commits before execution. Tasks 1–5 are complete. Scope expansion into elicitation-backed writes, token retirement, new client installer flags or broader TIA dialog policy requires an amendment to the plan and the relevant design phase.

## Completion and execution decisions — 2026-09-30

- Implemented on `feature/full-access-mode`, with a commit after every implementation/fix step. The approved plan was committed first as `0402055`; no remote write or live execution was authorized or performed.
- Native Windows commands replaced POSIX skill helper scripts. Focused test filters were broadened where needed to cover the affected fixture families; equivalent behavioral probes satisfy the checklist.
- Preserved the actual public name `execute_read_batch`; the draft's `read_batch` spelling was incorrect. Tool schemas, annotations and legacy token signatures are preserved.
- Classified two previously omitted dispatch methods: `update_type_content` as `ProjectMutation` and `get_basic_project_status` as `ProjectLifecycle`. Null/blank/unknown methods and invalid presets fail closed. An omitted client policy now resolves to read-write instead of bypassing authorization.
- Lifecycle/control fixtures explicitly select full; null-client fixtures use real full clients with absent executables. The all-operation field-forwarding fixture uses full because it includes PLC controls. Dedicated in-project edit/network fixtures retain read-write. No token, binding, audit or identity assertions were weakened.
- Replaced the obsolete source-text registration assertion with existing real-protocol 4/8/14 discovery coverage. A reviewer recommended a worker-switch source completeness test; current dispatch/catalog parity was checked twice (56/56), and no new source-text detector was added. Capability/unknown-method behavior and batch catalog completeness are automated; a future dispatch-registry refactor can enforce single-source completeness structurally.
- Independent reviews covered presets, enforcement, CLI and the whole branch. Review fixes include canonical worker labels, zero-dispatch assertions, process-environment serialization, and a shared production confirmation DI seam. No approval marker or new confirmation installer/doctor option was added.
- Fresh serial Release stub build passed with 7 existing analyzer warnings. Full offline suite: **4414 passed, 0 failed, 0 skipped**. CI scoped line coverage: **93.76%**, threshold **80%**; branch coverage **85.48%**. The existing coverage gate passed, direct/transitive NuGet audit reported no vulnerable packages, and changed-document links validated.
- Phase 1b only provides immutable confirmation configuration. Phase 2 owns elicitation enforcement: default-on ignores agent acknowledgement and requires supported elicitation accepting `confirm: true`; absent capability/decline/cancel/timeout deny, and off uses exact-set agent acknowledgement. Offline evidence does not establish live Siemens or client acceptance.
