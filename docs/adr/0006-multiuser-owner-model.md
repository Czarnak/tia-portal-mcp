# ADR-0006: Multiuser local sessions run through a typed common root with fail-closed ownership

**Status:** Accepted
**Decided:** 2026-09-28 to 2026-10-09   **Recorded:** 2026-10-30
**Area:** multiuser
**Current home:** [ARCHITECTURE: Internal active project context](../ARCHITECTURE.md#internal-active-project-context), [Project attachment and binding](../ARCHITECTURE.md#5-project-attachment-and-binding), [Multiuser reference](../SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md)

## Context
TIA V21 Multiuser local sessions had to be supported without a second process, a new read tool or a
repo-owned wrapper over the Siemens object model. Standalone code held a concrete `Project`, and
Openness remoting keeps all access in the worker. Local sessions are shared collaborative state
backed by a Project Server: closing, discarding or committing one can lose or publish other
people's work, and Siemens behavior was only discoverable live.

## Decision
- One internal `ActiveProjectContext` holds the Siemens `ProjectBase` engineering root, derived from a typed lifecycle owner (standalone project or local session), so root and owner cannot disagree. Content services take `ProjectBase`; capability gates decide which container kinds are eligible.
- Local identity is the typed `.amc21` engineering path plus continuous owner evidence. An `.als21` path is opener input, recorded as nullable provenance only after a verified open, never identity. Mode, remote identity and active connection stay unknown or null without a supported join.
- Discovery and inspection are `bind_project` actions: no new tool, no new envelope, no adoption.
- Adoption and reattachment grant no ownership. `LocalSession.Close()` is never cleanup, and there is no generic session close or automatic terminal cleanup. A worker-owned local source blocks opening a different project; a borrowed one needs proved preservation.
- A local save is `LocalSession.Save()` with an adjacent identity re-check and never updates from or checks in to the server. Create, save-as, archive and close stay standalone-only; `ProjectCapabilityCatalog.OperationFor` is the single worker-method to capability map for host and worker gates.
- Delivery is serial (context, inventory, open, reads, writes), each operation-bearing step with its own live evidence, on the guarded single-call pipeline with no token mechanism.
- Rejected: a repository-owned project facade (duplicates a large, changing API surface); separate Multiuser implementations of each operation (duplicated behavior, tests and fixes); a separate read tool for inventory (maintainer preference for `bind_project` only); `.als21` or filenames as identity (not recoverable on cold adoption); reflection as primary architecture (documented last resort only).

## Consequences
- One content implementation serves both container kinds; standalone behavior is preserved.
- Local sessions can be read, edited, compiled and saved locally, but cannot be closed, discarded or committed through the tool; terminal actions belong to Portal.
- Same-path or ambiguous owners fail closed. Reliable distinction of two sessions of one server project remains an open follow-up in the [improvement log](../IMPROVEMENT_LOG.md).
- Live acceptance was scoped by maintainer decision, so some matrix cases (full offline, headless/race) are recorded limits in the reports, not passes.
- Undelivered: Project Server and session lifecycle, locks and markings as mutation preconditions, discard and remote commit (planned PR7-9, a `multiuser_write` catalogue, no fabricated `CheckIn`/`Update`). Their design stays in the kept [engineering design spec](records/2026-09-28-multiuser-engineering-design.md); successor plans start from it and from merged `main`.

## Evidence
- In tree: [PR2 live](../superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md), [PR3 offline](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md), [PR3 live](../superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md), [PR4 live](../superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md), [PR5 live](../superpowers/acceptance/reports/2026-10-09-multiuser-pr5-live-verification.md), [PR6 live](../superpowers/acceptance/reports/2026-10-09-multiuser-pr6-live-verification.md).
- Kept original: [multiuser engineering design spec](records/2026-09-28-multiuser-engineering-design.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [PR2 active project context plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-05-multiuser-pr2-active-project-context.md)
  - [PR3 read-only inventory plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-05-multiuser-pr3-read-only-inventory.md)
  - [PR4 open existing sessions plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-07-multiuser-pr4-open-existing-sessions.md)
  - [Multiuser engineering design spec](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-28-multiuser-engineering-design.md)
