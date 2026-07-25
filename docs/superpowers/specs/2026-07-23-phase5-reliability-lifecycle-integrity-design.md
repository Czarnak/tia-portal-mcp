# Phase 5 Reliability and Lifecycle Integrity Design

**Date:** 2026-07-23
**Status:** Approved
**Scope:** TIA Portal MCP host, shared contracts, net48 Openness worker, tests, CI, and repository documentation

## Objective

Phase 5 makes project lifecycle behavior and the two broken PLC block-write paths reliable before the project adds Openness transactions or broader runtime diagnostics.

The approved Option 1 boundary is reliability-first: it includes the lifecycle split, the three live defects, warning propagation, and the quality gates needed to certify them. "Option 1" defers transactions and broad event diagnostics to Phase 6; it does not reduce Phase 5 to the lifecycle split alone.

The phase is complete only when:

- user-facing reads cannot open, switch, close, or rebind a TIA project;
- lifecycle writes use a separate internal state-probe path without weakening preview/token/apply safety;
- `save_project_as` cannot leave host and worker bound to different projects;
- `update_block_logic` and SCL `create_block` work against TIA Portal V21 and compile successfully;
- the automated suite and a live TIA Portal acceptance run pass.

## Evidence and Motivation

Round 4 established worker-ground-truth session binding, read-side open policy, structured warnings, serializer stability, and batch-field forwarding. It intentionally deferred `get_project_status(projectPath)` because the user-facing read still shares a worker RPC with lifecycle write-state probes. Closing that route naively previously broke preview and apply for save, save-as, archive, and close.

The subsequent live MCP test report found three defects that must be resolved before transaction behavior can be tested credibly:

1. `save_project_as(rebind:false)` can strand the MCP session because Siemens `SaveAs` changes the actually open project even when host bookkeeping does not rebind.
2. `update_block_logic` fails even for a byte-identical round trip because its multi-file bundle is staged incorrectly before `ImportFromDocuments`.
3. `create_block` with `language: "SCL"` fails because the import has no compile unit.

The current worker already handles `Notification`, `Confirmation`, and `Disposed` internally, but it does not expose reusable structured event diagnostics. It has no `ExclusiveAccess`, `Transaction`, or authentication-event implementation. Those capabilities remain valuable, but they are not Phase 5 prerequisites.

## Architectural Constraints

- Preserve the two-process architecture: .NET 8 MCP host plus persistent .NET Framework 4.8 Openness worker over newline-delimited JSON.
- Preserve one serialized worker request at a time.
- Preserve the existing ten-tool MCP surface. Data writes continue through batch tools; lifecycle writes remain self-previewing single tools.
- Preserve preview, single-use safety token, `confirm=true`, state-hash validation, and audit JSONL behavior.
- Never automatically retry a write after a worker timeout, crash, broken pipe, or protocol desynchronization.
- Treat the worker-reported resolved project path as ground truth after a successful lifecycle transition.
- Build the solution with `-m:1`; parallel solution builds can race on worker copy targets.
- Use TIA Portal V21 with real Openness assemblies for runtime acceptance. Stub builds prove compilation and protocol behavior, not Siemens runtime semantics.

## Scope

### Included

1. CI build serialization and an enforced coverage gate.
2. Separation of the user-facing project-status read from internal lifecycle write-state probing.
3. Worker-ground-truth binding for open, create, save-as, and other lifecycle transitions.
4. Safe handling of the unsupported `save_project_as(rebind:false)` request.
5. Repair of `update_block_logic` multi-document staging.
6. Repair of SCL block creation.
7. Direct lifecycle warning propagation.
8. Automated and live TIA Portal acceptance coverage.
9. README, improvement roadmap, and source skill documentation alignment.

### Excluded

- Openness `Transaction` and `ExclusiveAccess` support.
- Atomic batch writes or rollback guarantees.
- Generic event subscriptions, server push, or long polling.
- Authentication-event handling.
- MCP exposure of the `doctor` command.
- Hardware-gated network/tag investigations deferred by Round 4.
- New top-level MCP tools.
- Refactoring `BatchWorkerInvoker`, splitting `WorkerRequest` into per-operation DTOs, or unrelated cleanup.

The excluded Openness transaction and runtime-diagnostics work becomes Phase 6 after Phase 5 live acceptance passes.

## Lifecycle Architecture

### User-facing status read

`get_project_status` becomes strictly side-effect-free.

The host resolves the request against the current `ProjectSessionBinding` before invoking the worker. A requested path that conflicts with the bound path is rejected as `binding_conflict`. The worker status operation inspects only the project already open in its `TiaPortalSession`; it must not call `OpenProject`, `EnsureProject`, or any helper that can open or switch projects.

When no project is open, the worker returns `ProjectStatusInfo { IsOpen = false }`. Supplying a path does not authorize the read to open that project. The caller must use `open_project` for that transition.

An unexpected difference between the host binding and worker-reported active path is returned as a warning. A read never adopts the divergent path silently.

### Internal lifecycle state probe

Guarded lifecycle tools need a separate internal worker operation to obtain preview/apply state. This probe is not registered as an MCP tool and is callable only through `OpennessWorkerClient` lifecycle methods.

The internal probe retains the controlled open behavior required by the existing save, save-as, archive, and close flows. Its name and request operation must make the side effect explicit; it must not reuse the user-facing status operation. Host tests must prove that every guarded lifecycle write uses the internal probe and that direct `get_project_status` does not.

### Binding transitions

Successful lifecycle transitions bind using `WorkerResponse.ResolvedProjectPath`, after normalization. Caller-supplied paths are intent, not ground truth.

The rules are:

- a failed worker call does not change host binding;
- a successful read does not change binding;
- a successful open, create, or rebind-capable lifecycle write may change binding using the worker-reported path;
- a response without a required resolved path fails its postcondition instead of binding the caller-supplied path;
- divergence warnings remain distinct from operation failures.

## `save_project_as` Contract

The `rebind` parameter remains in the Phase 5 schema for compatibility, but only `true` is supported.

`rebind:false` is rejected during input validation, before preview generation and before any safety token is issued. The error explains that Siemens `SaveAs` changes the active project and directs the caller to use `rebind:true`. It is safer to reject the request than to silently reinterpret `false` as `true`.

For `rebind:true`:

1. Validate the target directory and project name.
2. Generate preview state through the internal lifecycle probe.
3. Bind the safety token to the normalized source path, target, input, and current state.
4. Repeat the probe and validate/consume the token during apply.
5. Invoke Siemens `SaveAs` once.
6. Require the worker to locate and report the copied project path.
7. Bind the host to that reported path only after success.
8. Return a postcondition failure if the copied path cannot be established.

The parameter is documented as deprecated and unsupported when false. Removal is reserved for a later breaking release.

## Block-Write Repairs

### `update_block_logic`

The worker must parse the exported multi-document bundle into an immutable collection of document descriptors. Each descriptor contains the logical document name, safe filename, expected extension, and contents.

Before calling Siemens:

- reject missing or duplicate document names;
- reject rooted paths, traversal segments, and filenames outside the operation's temporary directory;
- materialize every document using the exact filename later passed to `ImportFromDocuments`;
- verify every staged file exists;
- preserve deterministic document ordering.

After a successful import, the worker compiles or re-exports the target block to verify the postcondition. If verification fails, the operation returns the `postcondition_failed` failure category plus an explicit warning that the project may have changed. It must not return a successful result.

Temporary files are removed in `finally`. Cleanup failure is a warning unless it prevents determining the operation result.

### SCL `create_block`

SCL creation must generate a valid source containing at least one compile unit for the requested block type and name. It must not reuse an empty or LAD-oriented import representation.

The worker validates block type, language, name, and destination group before import. After import, it verifies the block exists and compiles it. Any partial or ambiguous outcome returns failure rather than success.

## Response and Failure Model

Phase 5 preserves the existing response envelope and adds stable category codes where necessary. Categories augment the user-facing message; they do not create new tools or expose raw Siemens exception details.

- `validation_error`: invalid path, unsafe staged filename, malformed document bundle, or unsupported `rebind:false`.
- `binding_conflict`: requested path differs from the bound project.
- `state_changed`: safety-token state no longer matches.
- `worker_operation_failed`: Siemens rejected the operation and the worker session remains usable.
- `worker_timeout`: request timed out; write outcome may be uncertain.
- `worker_crashed`: worker exited or protocol state was lost; write outcome may be uncertain.
- `postcondition_failed`: the API call returned but the required project/block state could not be verified.

Timeout and crash messages explicitly instruct the caller to inspect current state before retrying. The host does not automatically retry writes.

Worker warnings are preserved in direct lifecycle results and remain separate from errors. Warning count and size use the existing response-budget limits. Authentication credentials, secrets, and raw protected-project information are never added to warnings or audit output.

## Testing Strategy

All implementation tasks follow RED, GREEN, and refactor. Existing tests must remain green at each task boundary.

### Unit and contract tests

- Read-status policy cannot select an open/switch decision.
- Internal write probes retain the required lifecycle behavior.
- `rebind:false` fails before token generation.
- Binding uses worker-reported paths and never caller input after a transition.
- Failure categories serialize and render consistently.
- Block-bundle parsing rejects missing, duplicate, rooted, traversal, mismatched, and unsafe filenames.
- SCL creation always produces a non-empty compile unit.
- MCP schemas continue to exclude injected services and internal probe operations.
- Lifecycle tool count remains seven; total MCP tool count remains ten.

### FakeWorker integration tests

- Direct status never issues an open-project operation.
- Lifecycle preview and apply use the internal state probe.
- Successful SaveAs binds to the copied worker-reported path.
- Failed SaveAs preserves the original binding.
- Direct lifecycle responses include worker warnings.
- Crash, timeout, malformed JSON, and protocol-desynchronization paths are not retried.
- Safety-token misuse and state changes still fail before write execution.

### Live TIA Portal V21 acceptance

Tests operate on a disposable copy of a known project and record the exact TIA Portal and Openness versions.

1. With project A open, `get_project_status(projectPath=B)` refuses without opening B.
2. With no project open, status returns `isOpen:false` and does not open the supplied path.
3. Save, archive, close, and safe SaveAs preview/apply flows still work.
4. `rebind:false` cannot issue a token or invoke Siemens.
5. `rebind:true` leaves host and worker bound to the copied project.
6. A byte-identical `update_block_logic` round trip succeeds and compiles.
7. An edited `update_block_logic` round trip succeeds and compiles.
8. Malformed block input leaves the original block unchanged.
9. SCL block creation succeeds and compiles.
10. The following recoverable failures require no manual TIA Portal recovery: status binding conflict, rejected `rebind:false`, invalid SaveAs input rejected before execution, malformed block bundle rejected before import, and Siemens import failure reported while the worker remains connected. Worker crash and timeout are excluded from this criterion because their write outcome may be uncertain; those scenarios require state inspection before a caller-initiated retry.

## CI and Quality Gates

The phase begins by measuring the current coverage baseline. Missing tests are added until line coverage is at least 80% for the instrumentable production assemblies `TiaMcpServer` and `TiaMcpServer.Contracts`, excluding test assemblies, `TiaMcpServer.FakeWorker`, generated code, and the net48 Openness worker. The worker cannot execute against Siemens assemblies in CI; its Phase 5 requirements are mapped to stub compilation, protocol integration tests, and the mandatory live TIA acceptance run. CI publishes the exclusions with the report and fails when the scoped aggregate line rate is below `0.80`.

Required verification commands:

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1
dotnet test TiaMcpServer.sln
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj --collect:"XPlat Code Coverage" --settings TiaMcpServer.Tests/coverage.runsettings --results-directory TestResults
```

`TiaMcpServer.Tests/coverage.runsettings` selects Cobertura output and the assembly exclusions above. A deterministic CI step reads the generated Cobertura `<coverage line-rate>` value and exits non-zero below `0.80`; the Codecov upload remains reporting-only and is not the enforcement mechanism.

CI and publish builds use serialized solution build semantics where the worker copy targets can race. The full Round 4 acceptance suite runs as regression coverage for lifecycle/binding changes.

Before completion:

- review the complete branch diff across host, contracts, worker, tests, CI, and documentation;
- confirm no hardcoded secrets or credentials;
- confirm all external inputs are validated at the MCP and worker boundaries;
- confirm writes are never automatically retried;
- confirm safety-token and audit behavior is unchanged except for documented new fields;
- refresh `graphify-out` after code changes;
- perform a final code and security review.

## Delivery Order

1. Serialize CI builds and establish/enforce the coverage baseline.
2. Add the internal lifecycle probe and make status reads side-effect-free.
3. Adopt worker-reported paths for all successful lifecycle binding transitions.
4. Reject `save_project_as(rebind:false)` and harden SaveAs postconditions.
5. Repair `update_block_logic` staging and verification.
6. Repair SCL block creation and compile verification.
7. Surface direct lifecycle warnings.
8. Align README, improvement roadmap, schemas, and known-issues text.
9. Run full automated, regression, and live TIA Portal acceptance.

Each delivery item is independently reviewable and must leave the automated suite green.

## Documentation

Repository documentation changes include:

- update `README.md` lifecycle semantics and known issues;
- update `docs/IMPROVEMENT_PLAN.md` with Phase 5 outcome and Phase 6 backlog;
- add a live acceptance report under `docs/superpowers/acceptance/reports/`;
- update source MCP skill documentation to describe the actual batched tool surface.

Installed plugin-cache files are not edited. Skill documentation must be changed in its owning source repository or package and requires separate authorization if that repository is outside this workspace.

## Phase 6 Handoff

After Phase 5 live acceptance, Phase 6 can design:

- transaction eligibility by write operation;
- explicit sequential versus atomic execution mode bound into safety tokens;
- Openness `ExclusiveAccess` acquisition and lock-contention errors;
- transaction commit/rollback outcome reporting;
- structured notification, confirmation, disposed, and authentication events;
- bounded event capture or polling with timeout and cancellation;
- dialog-decision audit records and cleanup guarantees.

Phase 6 must not assume every Openness operation is transaction-compatible. It begins with an operation-by-operation capability inventory against the V21 API.

## Acceptance Criteria

Phase 5 is accepted when all of the following are true:

1. Direct project-status reads cannot reach a worker path that opens or switches projects.
2. Save, save-as, archive, and close preview/apply still work through an internal state probe.
3. Host binding changes only after success and always uses worker-reported paths.
4. `save_project_as(rebind:false)` is rejected before preview/token creation.
5. Safe SaveAs cannot strand the MCP session.
6. `update_block_logic` succeeds for no-op and edited live round trips and compiles.
7. SCL block creation succeeds live and compiles.
8. Direct lifecycle warnings reach MCP callers.
9. Worker crashes/timeouts never trigger automatic write retries.
10. The MCP surface remains ten tools and lifecycle surface remains seven tools.
11. Automated tests pass with at least 80% enforced coverage.
12. The serialized Release build, full Round 4 regression suite, and live TIA Portal V21 acceptance run pass.
13. README, improvement roadmap, known issues, and acceptance report match implemented behavior.
