# ADR-0007: Access modes are capability presets, lifecycle confirmation follows the mode, and content acts only on an explicitly bound project

**Status:** Accepted
**Decided:** 2026-08-28 to 2026-10-22   **Recorded:** 2026-10-30
**Area:** write-safety
**Current home:** [ARCHITECTURE: host startup and access modes](../ARCHITECTURE.md#2-host-startup-and-access-modes); [ARCHITECTURE: project attachment and binding](../ARCHITECTURE.md#5-project-attachment-and-binding); [ARCHITECTURE: write safety](../ARCHITECTURE.md#8-write-safety); [Project operations: explicit binding](../SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md)

## Context
- Write access was a set of mode names gating individual tools. PR #29 review found that a missing session identity was treated differently by the real worker, FakeWorker and test fixtures, and that a same-path `forceRebind` kept the stale verified identity.
- A third `full` tier and a `--confirm-with-user` switch followed. Lifecycle tools then prompted only when a rare acknowledge guard fired, so open, create, save and archive never asked anyone.
- A read-write session could not bind without a configured `--project` (a status read could promote a configured project): no registered tool established a verified binding, yet documents claimed reads did. Requested paths could implicitly open projects without a prompt.
- Content tools accepted a per-call `projectPath`, so one call could name a project other than the one the session was verified against.

## Decision
- Access tiers are capability-class presets. The ceiling is enforced at discovery, host dispatch and worker dispatch, never by per-tool allow lists. Unknown operations and invalid modes are denied in every mode. One shared mode-name helper and one shared tool registration serve production and tests.
- `OperationPolicyCatalog.RequiresExpectedSessionIdentity` is the single fail-closed authority for whether an operation may omit a session identity, consumed by the real worker and FakeWorker alike and separate from access authorization. A supplied identity is always validated. A same-path `forceRebind` rotates to a fresh unverified binding revision.
- Confirmation derives from the access mode, not a startup flag. Read-write asks once per actual lifecycle call; full never asks; block guards stop the call in every mode. The flag, the `acknowledge` parameter and safety tokens are removed.
- Lifecycle runs on the guarded single-call pipeline with state re-resolved after the prompt, so a stale accepted call is denied and not replayed. Whether a domain confirms every call is a per-domain property, so in-project edit domains do not inherit a prompt per edit.
- No implicit opens: only `open_project` and `create_project` open a project, in any mode. `bind_project`, permitted in every mode, adopts or switches to an already-open project and never opens, creates, saves or closes.
- Content tools and bound-project lifecycle tools take no `projectPath`. An unbound call fails closed with `binding_conflict`, gated once in the host worker client by capability. Only `bind_project` and `open_project` keep a public path.
- MCP `readOnly`/`destructive`/`openWorld` annotations are conservative, advisory client hints and never relax server enforcement, including on `dryRun`.
- Rejected: per-tool or name-based permission lists (drift from real capability); keeping `--confirm-with-user` (a second switch that contradicted the mode); letting reads bind or open implicitly (silent project change with no prompt); a per-call path on content tools (two sources of truth for the target).

## Consequences
- Read-write and full advertise the same tools and differ only in whether lifecycle calls prompt; audit records the confirmation as user, policy or none.
- A new operation must be classified in the catalog or it is denied. Tests must use the real tool surface and honest identity fixtures, not manufactured identities.
- Clients without form elicitation cannot run read-write lifecycle calls. Clients may prompt on dry runs because of the destructive hints.
- A caller must `bind_project` before any content call; guidance strings name it first.
- The earlier confirm-switch and full-only lifecycle tiers were superseded within this era by the mode-derived rule above. Multiuser inspection and local sessions later extended `bind_project` without changing the model (see [ADR-0006](0006-multiuser-owner-model.md)); the removal of tokens is covered by [ADR-0004](0004-guarded-single-call-write-pipeline.md).

## Evidence
- In tree: [annotations live report](../superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md); [lifecycle offline validation](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md); [lifecycle live validation](../superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md); [tiers and bind_project live validation](../superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [PR #29 binding repair design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-08-28-pr29-binding-findings-repair-design.md)
  - [PR #29 binding repair plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-28-pr29-binding-findings-repair.md)
  - [Explicit MCP tool annotations plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-01-pr1-explicit-mcp-tool-annotations.md)
  - [Access modes (Phase 1b) plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-30-write-safety-phase1b-access-modes.md)
  - [Guarded lifecycle (JSON contract Phase 3) plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-30-json-contract-phase3-lifecycle.md)
  - [Lifecycle tiers and project binding design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-01-lifecycle-tiers-and-project-binding-design.md)
  - [Lifecycle tiers and project binding plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-02-lifecycle-tiers-and-project-binding.md)
  - [C-0 bound-project content tools plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-22-c0-bound-project-content-tools.md)
