# D-1 shared batch read tool design

**Status:** Scope and approach approved in chat on 2026-10-25; recorded here for implementation planning. No product changes or runtime verification have been performed.

**Authority:** [D-1 in the codebase simplification review](../plans/2026-10-17-codebase-simplification-review.md#workstream-d-host-structural-consolidation-no-wire-change). Current source inspected at clean `main`, `cfdda59`, on 2026-10-25.

## Intent and scope

Replace the repeated orchestration and response/error records in `plc_read`, `network_read`, `hmi_read` and `vci_read` with one host-side implementation. Deliver one behavior-preserving refactor PR with smaller domain wrappers and no new dependencies.

The existing `OpennessWorkerClient.AccessPolicy.Mode` already exposes the effective access mode and is used by all four wrappers. That part of the historical proposal needs no additional API.

## Approach

Use a static `BatchReadTool.RunAsync<TItem>` with catalog delegates and a per-item `Func<TItem, Task<StructuredOperationItem>>` callback. Reuse `StructuredOperationBatchExecutionEngine`, `StructuredOperationBatchPayloadBudget` and `StructuredToolResult`; the helper owns the common sequence and response composition. One `OperationBatchReadResponse` and one `OperationBatchReadError` replace the four read envelopes and four read-only error records.

The approved choice extracts the whole repeated flow. Extracting only the records would leave four copies of that flow; a domain-service interface or inheritance hierarchy would add machinery beyond D-1. Neither is part of the chosen design.

Keep the attributed domain entry points, their existing public parameters and descriptions, and their small internal `Error` and `ApplyBudget` adapters. Catalog validation, worker invocation, typed payload projection and retry guidance remain domain-owned. The implementation plan settles internal signatures and migration order.

## Compatibility requirements

- Preserve the order: catalog validation, access validation, verified binding, sequential independent reads, budgeting, canonical delivery. Never invoke domain execution or materialize Network's executor before the three admission gates pass.
- Preserve root members, explicit nulls, empty warning arrays, categories and exact diagnostic/retry messages. Preserve HMI contract version `1.1`; PLC, Network and VCI remain `1.0`.
- A rejected call has `batch:null`, a root error and `isError:true`. A batch that ran has `error:null` and `isError:false`, including failed or omitted items; root `success` reflects final delivered item statuses.
- Preserve item order and continuation after worker/payload failures. Preserve current cancellation checks between items, exception propagation and in-flight behavior. Do not add cancellation forwarding or replay.
- Preserve HMI cursor projection reading `BindingSnapshot` after the worker call returns. Preserve Network's current registered/cached executor, pagination coordinators, protected cursors and compatibility codec.
- Budget the exact shared response envelope with existing effective limits and domain guidance. Keep input requests unchanged and omit values whole.
- Preserve the advertised tool schemas. `McpToolSchemaTests.OutputSchemasMatchMain` already compares the complete production surface to `Tools/Snapshots/output-schemas.json` under default and `2025-11-25` protocol versions. Keep that baseline unchanged.
- Continue producing text and structured content from one final canonical serialization through `StructuredToolResult`.

## Boundaries

D-4 payload-contract failure consolidation and D-5 Network dependency injection remain separate. Do not change write/lifecycle records, policy catalogs, binding rules, worker contracts or dispatch, CLI configuration, dependencies, or worker/native behavior. This work does not add concurrency, caching, a domain interface or new public operations.

Retain PLC content/import result records and Network's other response models when removing their read envelope/error declarations. HMI and VCI response files can be deleted when empty; update explicit test links where applicable.

## Acceptance

Use existing schema snapshots plus deterministic pre-refactor canonical-response characterization. Exercise rejection precedence, access denial, independent failures, cancellation, effective budgets, domain guidance and cursor regressions. Build serially, run focused gates, then run the non-packaging suite once with scoped coverage at least `80%`; inspect coverage of materially changed helper logic. Review the complete diff independently.

No live TIA test is required for this scope. Offline/FakeWorker evidence proves the host refactor's compatibility; it does not establish new native acceptance. If implementation requires changing an excluded behavior or the public schema baseline, stop and reassess the scope rather than accepting the drift.

Execution method is not selected. See the [implementation plan](../plans/2026-10-25-d1-batch-read-tool.md).
