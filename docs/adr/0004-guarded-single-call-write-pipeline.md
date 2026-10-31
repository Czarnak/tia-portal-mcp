# ADR-0004: Writes run as one guarded call; safety tokens are retired

**Status:** Accepted
**Decided:** 2026-09-01 to 2026-10-21   **Recorded:** 2026-10-30
**Area:** write-safety
**Current home:** [ARCHITECTURE §8: guarded write pipeline](../ARCHITECTURE.md#guarded-write-pipeline-foundation-and-lifecycle); [PLC writes](../ARCHITECTURE.md#plc-writes-plc_write); [Lifecycle continuity](../ARCHITECTURE.md#lifecycle-continuity); [PLC reference](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md)

## Context
- The first write-safety design was a generic `preview_write_batch` that returned an apply token bound to a hash of a typed state snapshot. PRs 1-6 hardened it with preview diffs and per-operation snapshot scopes.
- A token gives no consent: the agent previews and applies with `confirm=true` in the same turn, so it confirms itself. MCP places consent in the client.
- The state hash was fragile by construction (volatile export fields had to be stripped), dependent items could not be batched (#79), preview quality was uneven (#78), and snapshot readers grew with every operation.
- A probe of the installed V21 `OnlineProvider` found no public PLC Start/Stop and no authoritative RUN/STOP read, so the reflected control calls could not be trusted.

## Decision
- Every write is one call through a shared pipeline: validate, verified binding under a pinned lease, plan, guards, stop here on `dryRun:true`, mutate, verify, audit. A `dryRun` flag replaces the preview tool; the consequence report becomes the `effects` of both dry runs and applied writes.
- Stale-state protection is a required, format-tagged `expectedContentHash` (If-Match style) on content replacement only, checked at plan time and again by the worker.
- Asking the user uses MCP elicitation, the only mechanism that reached the user in all three clients tested (Claude Code, Codex CLI, MCP Inspector). The `anthropic/requiresUserInteraction` marker was dropped because only Claude Code honoured it. Anything other than an explicit accept denies, and the server never claims a human saw the prompt. Which tools elicit follows the access mode ([ADR-0007](0007-access-tiers-lifecycle-confirmation-and-binding.md)).
- Safety tokens, the token core, `SafetyRead` snapshot readers, the generic batch tools, the legacy write audit stream and PLC start/stop are removed. Generic batches split into `plc_read`, `read_cross_references` and `plc_write`.
- PLC header metadata is read through typed `PlcBlock` properties only, never `GetAttribute`, which can throw over remoting and stall.
- Follow-up contracts on the same pipeline:
  - Rebind never discards unsaved work: a modified worker-owned source is refused at preview and re-checked immediately before close; nothing is auto-saved; unreadable state fails closed; a modified UI-owned source may stay open.
  - Response budgeting is one shared `GuardedWriteResponseBudget` with typed per-domain adapters that keep their own omission priorities and guard reserves.
  - Retired request fields (for example `update_tag.isSafety`) are rejected by the generic unknown-member rule, not by a retired-member list.
- Rejected alternatives:
  - Keep tokens with a stronger hash: still no consent, still fragile.
  - Approval marker on tool annotations: honoured by one client only.
  - A separate preview tool: two calls whose state can drift apart.
  - A per-domain budget copy in each new domain: four copies had already diverged (bug B-10).

## Consequences
- Safety rests on validation, transparent effects, content hashes and client-side consent, not on a server-enforced token. Stale-state protection outside content hashes is limited, because the agent and the maintainer share the open project.
- New write domains build on `Safety/Pipeline`; tokens, snapshot readers and a `SafetyRead` catalog must not return. Network, PLC, HMI and VCI all use the pipeline.
- Clients that prompt on destructive tools also prompt on dry runs. PLC run/stop stays out of scope unless Siemens exposes a public RUN/STOP surface.
- Later narrowed by [ADR-0012](0012-portal-as-rejection-authority.md): guard severities reduced to block/info outside lifecycle, and collision guards, replanning and read-back removed. The per-domain budget priorities and reserves are in [ARCHITECTURE](../ARCHITECTURE.md).

## Evidence
- In tree: [PR2 tool delegation](../superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md); [PR3 update_tag snapshot](../superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md); [PR4 preview diff](../superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md); [PR5 tag scopes](../superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md); [PR6 project-tree scopes](../superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md); [Project tree v3](../superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md); [Lifecycle tiers and bind_project](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md); [Network guarded write offline](../superpowers/acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md); [PLC read and cross-references](../superpowers/acceptance/reports/2026-10-05-plc-read-and-cross-references-validation.md); [PLC write](../superpowers/acceptance/reports/2026-10-07-plc-write-validation.md); [PLC native rejections](../superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md)
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-01-write-safety-hardening-design.md (OnlineProvider finding)
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-01-pr2-registered-tool-delegation.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-01-pr3-update-tag-safety-snapshot.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-01-pr4-structured-preview-diff.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-01-pr5-tag-operation-safety-scopes.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-01-pr6-project-tree-safety-scopes.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-13-issue-30-plc-block-header-metadata-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-13-issue-30-plc-block-header-metadata.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-26-wave1-k1-batch-failure-categories.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-26-wave1-l1-lifecycle-rebind-preview-safety.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-26-wave1-p1-plc-read-integrity.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-29-write-safety-redesign-design.md (Appendix A: elicitation client spike)
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-29-write-safety-phase1-foundation.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-05-plc-read-write-and-cross-references-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-05-plc-read-and-cross-references.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-06-plc-write.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-21-c10-update-tag-is-safety.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-21-guarded-write-budget.md
