# Standalone Project Tools Design

**Date:** 2026-07-31
**Status:** Approved in design review

## Objective

Separate project-level reads from the generic `execute_read_batch` surface. The MCP server will expose project status, project-tree browsing, and compilation checks as standalone tools, while the read batch remains focused on PLC, hardware, catalog, cross-reference, tag-table, block, and type data.

This is an immediate public API break. The removed batch operations will not have aliases, deprecation warnings, or migration-specific error handling.

## Current State

The public project surface currently mixes two shapes:

- `get_project_status` is already a standalone tool but is duplicated in `execute_read_batch`.
- `browse_project_tree` and `compile_check` exist only as `execute_read_batch` operations.
- The six project lifecycle writes (`open_project`, `create_project`, `save_project`, `save_project_as`, `archive_project`, and `close_project`) are already standalone self-previewing tools.

All three project reads already have dedicated `OpennessWorkerClient` methods and worker dispatch methods. The refactor therefore changes the .NET 8 host surface only; it does not change the host/worker protocol or Siemens Openness implementation.

## Public Tool Surface

### Project reads

`ProjectReadTools` is registered in both read-only and read-write modes and exposes:

1. `get_project_status(projectPath?)`
   - Reads status and metadata without opening, switching, or binding a project.
   - Retains `ReadOnly=true`, `Destructive=false`, and `OpenWorld=false` metadata.

2. `browse_project_tree(projectPath?, depth?, startPath?)`
   - Reads the bounded project hierarchy through the existing `BrowseProjectTreeAsync` client method.
   - Retains `ReadOnly=true`, `Destructive=false`, and `OpenWorld=false` metadata.
   - Rejects `depth < 1` as `validation_error` before worker access.

### Project engineering

A new `ProjectEngineeringTools` MCP tool type is registered only in read-write mode and exposes:

1. `compile_check(projectPath?, plcName?, blockPath?)`
   - Invokes the existing Siemens compilation path and returns compiler messages.
   - Uses `ReadOnly=false`, `Destructive=false`, and `OpenWorld=false` metadata because compilation is an engineering action, not a pure read.
   - Does not use the preview/apply safety-token flow. This preserves the current `compile_check` safety behavior; the refactor only changes its entry point.

The underlying `OperationAccessPolicy` continues to deny `compile_check` in read-only mode as defense in depth, even though the tool is not registered there.

### Project lifecycle writes

The six lifecycle-write tools and their safety-token behavior remain unchanged.

### Tool counts

- Read-only mode: 3 tools (`get_project_status`, `browse_project_tree`, and `execute_read_batch`).
- Read-write mode: 12 tools (the 3 read-only tools, `compile_check`, 6 lifecycle-write tools, and 2 write-batch tools).

## Generic Read-Batch Surface

`execute_read_batch` retains exactly these six operations:

1. `read_hardware_config`
2. `search_equipment_catalog`
3. `read_cross_references`
4. `get_block_content`
5. `list_tag_tables`
6. `get_type_content`

The following operations are removed from `BatchOperationCatalog` and `BatchWorkerInvoker`:

- `get_project_status`
- `browse_project_tree`
- `compile_check`

Submitting any removed name to `execute_read_batch` produces the existing generic unknown-operation validation response, including the current valid read-operation list. Validation fails before any worker call.

Both registered and backward-compatible `execute_read_batch` descriptions must list exactly the six remaining operations and must not mention the removed operations as valid batch items.

## Batch Schema Cleanup

The flat `BatchOperationRequest` DTO no longer needs the project-tree-only properties:

- `Depth`
- `StartPath`

They are removed from the batch schema and become direct parameters of the standalone `browse_project_tree` tool. The corresponding JSON-schema and request-serialization tests are updated.

`BlockPath`, `PlcName`, and `ProjectPath` remain in `BatchOperationRequest` because retained batch operations still use them. Their descriptions must no longer claim `compile_check` as a batch consumer.

Batch payload-truncation and omission guidance must also stop recommending `depth` or `startPath`, since those inputs will no longer exist on `execute_read_batch`.

## Data Flow

The standalone tools are thin host adapters:

```text
MCP tool
  -> existing OpennessWorkerClient method
  -> existing newline-delimited JSON worker request
  -> existing .NET Framework Openness worker handler
  -> WorkerCallResult structured envelope
```

The mappings are:

- `get_project_status` -> `GetProjectStatusAsync`
- `browse_project_tree` -> `BrowseProjectTreeAsync`
- `compile_check` -> `CompileCheckAsync`

No worker method strings, request contract properties, Openness handlers, session-binding rules, or timeout/crash behavior change.

## Response Budget

Today, `browse_project_tree` and `compile_check` inherit the read batch's 60,000-character per-item payload cap. Moving them to standalone tools must not make their successful responses unbounded.

A focused standalone read-result budget helper will:

- leave failures, failure categories, resolved-path behavior, and warnings unchanged;
- leave successful payloads at or below 60,000 characters;
- append an explicit truncation marker when a payload exceeds the cap;
- provide tool-specific narrowing guidance:
  - `browse_project_tree`: use `depth` and `startPath`;
  - `compile_check`: use `plcName` and `blockPath`.

The final result is serialized through the existing `WorkerCallResult.ToEnvelopeText()` structured envelope.

## Validation and Error Handling

- `browse_project_tree(depth < 1)` returns a categorized `validation_error` without invoking the worker.
- Worker validation failures, binding conflicts, timeouts, crashes, and warnings keep their existing `WorkerCallResult` categories and text.
- `compile_check` is absent from read-only tool registration and remains blocked by `OperationAccessPolicy` if invoked internally in read-only mode.
- Removed batch operations return the existing batch-level unknown-operation error before execution begins.
- A failure in one retained read-batch item continues not to stop other valid items.

## Testing Strategy

Implementation follows TDD. Each production change begins with a focused failing test.

### Standalone tool tests

- `ProjectReadTools` exposes `get_project_status` and `browse_project_tree` with the expected MCP metadata and parameters.
- `ProjectEngineeringTools` exposes only `compile_check` with engineering-action metadata.
- Tool methods forward every parameter to the existing `OpennessWorkerClient` method.
- `browse_project_tree` rejects invalid depth before worker access.
- Successful oversized tree and compile payloads are truncated with the correct hint.
- Structured failures and warnings survive standalone formatting unchanged.

### Access-mode and schema tests

- Read-only mode exposes exactly 3 tools.
- Read-write mode exposes exactly 12 tools.
- `compile_check` is not registered in read-only mode.
- The underlying client policy still rejects `compile_check` in read-only mode.
- Injected services do not appear in the public standalone tool schemas.
- `depth` and `startPath` no longer appear in the batch request schema.

### Batch-removal tests

- The catalog contains exactly the six retained read operations.
- `get_project_status`, `browse_project_tree`, and `compile_check` are rejected as unknown read-batch operations.
- Both `execute_read_batch` descriptions include every retained operation and none of the removed names.
- `BatchWorkerInvoker` no longer contains dispatch arms for the removed operations.
- Existing batch field-forwarding, independent-failure, and payload-budget invariants continue to pass for retained operations.

### Verification

Run:

1. Focused tests during each red/green cycle.
2. Serialized stub build with `-m:1` and `UseTiaPortalReferenceStubs=true`.
3. Full `TiaMcpServer.Tests` suite.
4. The repository's scoped 80% coverage threshold through `scripts/verify-coverage-threshold.ps1`.
5. Documentation link/name/scope checks.

No live TIA Portal test is required because the worker protocol and Openness implementation do not change. Static and FakeWorker verification will be reported separately from runtime TIA evidence.

## Documentation Changes

Update all current public-surface descriptions, including:

- `README.md`
- `docs/ARCHITECTURE.md`
- `docs/SupportedOperations/README.md`
- `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`
- any other current `docs/SupportedOperations/` page that lists the affected entry points

Documentation must:

- state that the server exposes 12 tools in read-write mode;
- list `get_project_status`, `browse_project_tree`, and `compile_check` as standalone tools;
- classify `compile_check` as a read-write-mode engineering operation;
- list exactly the six retained `execute_read_batch` operations;
- replace batch-based project-tree and compilation examples with standalone calls;
- remove batch narrowing guidance that mentions `depth` or `startPath`;
- preserve the distinction between static verification and live TIA behavior.

## Non-Goals

- No aliases or deprecation period for removed batch operations.
- No dedicated project-read batch.
- No batching of lifecycle writes.
- No changes to safety-token behavior.
- No new project operations.
- No worker protocol or Siemens Openness changes.
- No live TIA Portal execution as part of this host-surface refactor.
