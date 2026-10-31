# ADR-0011: The tool surface fails closed, and Portal loss is classified once at the worker boundary

**Status:** Accepted
**Decided:** 2026-10-17 to 2026-10-20   **Recorded:** 2026-10-30
**Area:** process
**Current home:** [ARCHITECTURE: project attachment and binding](../ARCHITECTURE.md#5-project-attachment-and-binding); [ARCHITECTURE: worker transport and execution](../ARCHITECTURE.md#6-worker-transport-and-execution); [ARCHITECTURE: defense-in-depth access enforcement](../ARCHITECTURE.md#4-defense-in-depth-access-enforcement)

## Context
- A static review of the codebase on 2026-10-17 (area reviewers, each checked by an adversarial verifier) found that the costliest problems were cross-area inconsistencies, not local defects.
- Content tools accepted a per-item `projectPath`, so one call could mix projects or silently act on a project other than the bound one.
- Unknown or misplaced root arguments were handled differently per tool: some dropped them silently, writes returned plain text although every tool declares a canonical output schema.
- `NonRecoverableException` (Portal gone) surfaced as `worker_operation_failed` in most domains, `binding_conflict` in Multiuser, and `portal_lost` only in VCI, so the host could not invalidate binding and cursors uniformly.
- The worker also re-checked a `Confirm` flag that the host always sets, and one JSON-null mechanism existed only for tools that had already migrated away from it.
- The maintainer decided six questions (Q1-Q6) on 2026-10-17; Q3 was implemented and live-tested afterwards.

## Decision
- **Q6, bound-only content tools:** content tools act only on the bound project and take no `projectPath`. An unbound session fails closed with `binding_conflict` ("bind first"). `projectPath` remains only where it selects or creates a target. Shipped as a MINOR release.
- **Q4, one rejection shape:** every argument-shape error on all tools returns that tool's own canonical document with `validation_error` and `isError:true`, and writes no audit record. Rejected: keeping writes as plain text (violates the rule that `error` is non-null whenever `isError` is true).
- **Q3, one Portal-loss mapping:** the worker boundary maps `NonRecoverableException` to `portal_lost` for every domain. An escaping `EngineeringException` maps there only when the session observed unexpected disposal of the current Portal attachment during that execution (cached event evidence, never message text or native liveness probes). Broad recovery catches use one exclusion filter so they cannot absorb fatal loss. The host invalidates binding and cursors without replay. The VCI writer keeps its outcome-bearing catch.
- **Q5, no worker `Confirm` fence:** the shared policy catalogs already gate both processes, so the worker flag is redundant and its messages point at an argument that no longer exists. Decided; removal was left as backlog work.
- **Q2, retire `LegacyNullOmission`:** every tool is structured, so the mechanism has no production use. Decided; retirement was left as backlog work.
- **Q1, keep the worker-only probes** until `update_io_system` acceptance, and log their retirement rather than delete them now.

## Consequences
- One `portal_lost` vocabulary across PLC, Network, HMI, VCI, Multiuser and lifecycle; recovery is explicit (reopen, rebind), and cursors from before the loss stay rejected as `cursor_binding_mismatch`.
- Content tools cannot be pointed at another project by argument; switching is only `bind_project` or `open_project` (see [ADR-0007](0007-access-tiers-lifecycle-confirmation-and-binding.md)). Argument-shape handling was then derived once for all tools ([ADR-0008](0008-tool-surface-consistency.md)).
- Any new broad `catch` in worker recovery code must use the shared recoverability filter, and a new domain must not invent its own loss category.
- Classification depends on event evidence of disposal, so a loss that raises no observed disposal and no `NonRecoverableException` stays `worker_operation_failed`.
- Live evidence is scoped: native write interruption, inventory-fault injection and the exact native exception type were never exercised live (see the B-16/Q3 report).
- Q1, Q2 and Q5 follow-ups, and the other review workstream items, are tracked in the review plan kept under `records/`, not here.

## Evidence
- In tree: [B-16/Q3 live verification](../superpowers/acceptance/reports/2026-10-20-b16-q3-worker-portal-loss-live-verification.md), which includes the final installed candidate's standalone HMI read interruption (`portal_lost`, immediate `cursor_binding_mismatch` for HMI and tree cursors, recovery by reopen and rebind with the same host and worker).
- In tree: [lifecycle tiers and bind_project](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md); [Multiuser PR2 active context](../superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md); [Network B-4](../superpowers/acceptance/reports/2026-10-19-network-b4-live-verification.md); [Network object cursors C-5](../superpowers/acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md).
- Kept originals: [review plan with Q1-Q6](records/2026-10-17-codebase-simplification-review.md); [findings appendix](records/2026-10-17-codebase-simplification-review-findings.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [2026-10-17-codebase-simplification-review.md](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-17-codebase-simplification-review.md)
  - [2026-10-17-codebase-simplification-review-findings.md](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-17-codebase-simplification-review-findings.md)
  - [2026-10-19-worker-portal-loss.md](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-19-worker-portal-loss.md)
