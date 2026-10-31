# ADR-0002: Large project and hardware reads are bounded, complete and cursor-paged

**Status:** Accepted
**Decided:** 2026-07-31 to 2026-09-06   **Recorded:** 2026-10-30
**Area:** json-contract
**Current home:** [ARCHITECTURE: Project enumeration completeness](../ARCHITECTURE.md#project-enumeration-completeness), [Project-tree v3 snapshot and continuation seam](../ARCHITECTURE.md#project-tree-v3-snapshot-and-continuation-seam), [Hardware configuration pagination seam](../ARCHITECTURE.md#hardware-configuration-pagination-seam)

## Context
Project status, tree browsing and compile shared the generic read batch with unrelated reads, so the
standalone tools were split out to remove duplication and give a cleaner tool surface; the per-item value cap was inherited. Issue #31 showed that grouped devices and system blocks
were never enumerated, and that a complete hardware read would still exceed the per-value budget by
orders of magnitude once they were. Issue #32 showed that tree browsing on large projects repeated
expensive Openness walks and returned one unbounded result.

## Decision
- Project reads become standalone tools with their own budgeted result; the batch operations are removed outright, with no aliases or migration shims.
- One worker-local ordered device enumerator feeds both hardware and tree reads, so "complete project" has a single definition. Grouped devices stay flat `Device` nodes; system-block hierarchy membership is marked without claiming provenance.
- Hardware pagination is opt-in and uses a split seam: the worker enumerates stable descriptors and materializes a window; the host sizes pages on the canonical envelope, which only it can measure.
- Project-tree v3 is one breaking contract (a typed `startSelector`, flat node pages). The first call does one scoped walk; the host keeps an immutable in-memory snapshot, and continuations make no worker call.
- Cursors are authenticated, process-local and pinned to the binding. A page holds whole entities or reports an omission, never a truncated entity.
- Rejected: dual legacy/structured modes (union schema, doubled maintenance); raising the budget (floods clients, does not bound model context); worker-held or disk snapshots (tie paging to worker lifetime, or persist project data); re-observing per page (recreates the cost); string `startPath` or overlapping selectors (escaping and mismatch rules); partial direct-Openness pruning (latency depends on undocumented coverage).

## Consequences
- Large projects can be read completely in bounded pages without repeated Portal traversal.
- Paging gives a stable ordered set, not a transaction: later project changes do not alter a cached tree, and hardware cursors detect set drift rather than freeze it. Callers restart from page one when a cursor is unavailable.
- Cursors do not survive host restart, expiry or a binding change; these are explicit failures, never a silent re-walk. Continuations must never trigger an implicit new walk.
- Siemens objects stay in the net48 worker; worker payloads stay typed and are never echoed on rejection.
- Narrowed later: the generic read batch no longer exists, and project reads are now bound-project tools (see [ADR-0007](0007-access-tiers-lifecycle-confirmation-and-binding.md), [ADR-0005](0005-canonical-json-contract.md)). Direct-Openness tree pruning stays a measured, open follow-up in the [improvement log](../IMPROVEMENT_LOG.md).

## Evidence
- In tree: [project-tree v3 live](../superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md); [PR6 project-tree safety scopes live](../superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md); [network phase 1](../superpowers/acceptance/reports/2026-08-01-16-48-48-network-operations-phase1.md) and [rerun](../superpowers/acceptance/reports/2026-08-01-16-56-00-network-operations-phase1-rerun.md) (offline, no live Portal; they cover the hardware read path).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-07-31-standalone-project-tools-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-07-31-standalone-project-tools.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-08-28-issue-31-project-completeness-pagination-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-28-project-enumeration-completeness.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-29-hardware-pagination.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-06-issue-32-scalable-project-tree-browsing-v3-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-06-issue-32-scalable-project-tree-browsing-v3.md
