# ADR-0003: Network is an independent domain with exact, fail-closed identity and gated phase delivery

**Status:** Accepted
**Decided:** 2026-08-01 to 2026-10-23   **Recorded:** 2026-10-30
**Area:** network
**Current home:** [ARCHITECTURE §7a: canonical JSON seam and Network structured contract](../ARCHITECTURE.md#7a-the-opt-in-canonical-json-seam-and-the-network-phase-23-structured-contract); [Phase 3 identity seam](../ARCHITECTURE.md#phase-3-read-identity-and-introspection-seam); [Phase 4 subnet lifecycle seam](../ARCHITECTURE.md#phase-4-subnet-lifecycle-seam); [Network reference](../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md)

## Context
- Network operations began inside the generic batch tools, returning escaped JSON strings a caller had to parse twice.
- Network writes change hardware configuration, so selecting the wrong device, interface or node is worse than refusing.
- Live runs showed node IDs are unique only within their owning interface, and that optional metadata is sometimes unavailable on valid hardware.
- IO-system editing and generic attribute writes were wanted next, but which fields can be safely written and compiled was unknown.

## Decision
- Network operations leave the generic batches at once, with no compatibility aliases or deprecation period, and become `network_read`/`network_write` with their own request, catalog and invoker over a domain-neutral operation-batching kernel. Network does not depend on the batch domain.
- Results are single-layer structured JSON on the canonical seam (see [ADR-0005](0005-canonical-json-contract.md)). MCP `isError:true` is reserved for whole-tool failure before a usable batch result; an attempted batch that fails reports `success:false` with `isError:false`. `network_write` performs no hidden post-write hardware read.
- Selectors are exact and fail closed: nodes are qualified by their owning interface, list and inspect reads use typed selectors, and continuation cursors are host-sealed. Structural discovery failure blocks writes; unavailable optional metadata is reported but never vetoes a write.
- `list_network_objects` was kept only after it passed a measurable live retention gate.
- Subnets are managed by three explicit operations rather than one generic mutation, on the guarded pipeline ([ADR-0004](0004-guarded-single-call-write-pipeline.md)); Portal's own rejections are the authority ([ADR-0012](0012-portal-as-rejection-authority.md)).
- Phases 4-6 ship as a serial, just-in-time train of pull requests: each successor is planned from merged `main` after its predecessor's report is reviewed, every behavior change needs its own scope-specific live gate, and a live mutation is never the failing test.
- Undo is deferred to a separate feasibility investigation, not rejected: Siemens transactions can roll back only an open transaction, saving or going online clears the undo history, and no direct Undo entry point was found in the V21 documentation, which is not proof that none exists. No undo tool or automatic batch rollback was added.
- Rejected alternatives:
  - Keep aliases in the generic batches during migration: two contracts to keep consistent for no caller benefit.
  - Global node-ID selectors: ambiguous across interfaces on real hardware.
  - Let optional metadata gate writes: blocks valid configurations for information the write does not need.

## Consequences
- Every later structured tool reuses this seam and fail-closed selector posture; callers supply qualified selectors and restart paging after a stale cursor.
- Phase 5 (IO-system editing) is unshipped. Its qualified field set, hardware-compile target rule and planned post-commit failure contract are recorded only in the qualified-contract spec at the archive tag; widening that scope needs new live evidence. The open item is in [IMPROVEMENT_LOG](../IMPROVEMENT_LOG.md).
- Phase 6 attribute writes, if built, start from a positive closed allowlist.

## Evidence
- In tree: [Phase 1 offline acceptance](../superpowers/acceptance/reports/2026-08-01-16-48-48-network-operations-phase1.md); [Phase 1 rerun](../superpowers/acceptance/reports/2026-08-01-16-56-00-network-operations-phase1-rerun.md); [Phase 5 PR2 qualification](../superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md); [JSON contract Phase 2 offline](../superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md); [Phase 2 live](../superpowers/acceptance/reports/2026-10-01-json-contract-phase2-live-verification.md); [Phase 3 live](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md); [Phase 3 offline](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md); [Discovery and node-identity repair](../superpowers/acceptance/reports/2026-10-04-network-discovery-and-interface-node-identity-repair-validation.md); [C-5 sealed cursors live](../superpowers/acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md)
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-08-01-network-operations-phase1-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-01-network-operations-phase1.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-08-02-network-operations-phase2-json-contract-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-02-network-operations-phase2-json-contract.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-08-03-network-operations-phase3-identity-introspection-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-04-network-operations-phase3-identity-introspection.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-21-network-phases-4-6-delivery-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-24-network-phase5-qualified-contract.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-24-pr2-network-phase5-qualification.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-04-network-discovery-and-interface-node-identity-repair-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-04-network-discovery-and-interface-node-identity-repair.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-23-c5-sealed-network-object-cursors.md
  - Undo note: https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-03-network-json-guarded-write-design.md (§12)
