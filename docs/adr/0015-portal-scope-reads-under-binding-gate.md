# ADR-0015: Content tools stay gated on the verified binding and may read Portal-scope objects of the bound Portal

**Status:** Accepted
**Decided:** 2026-11-04   **Recorded:** 2026-11-06
**Area:** binding
**Supersedes:** the "content acts only on the bound project" wording of [ADR-0007](0007-access-tiers-lifecycle-confirmation-and-binding.md) and [ADR-0011](0011-fail-closed-surface-and-portal-loss.md); their other decisions remain in force.
**Current home:** [ARCHITECTURE: Library reads](../ARCHITECTURE.md#library-reads-library_read); [Library operations summary](../SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md)

## Context

Global libraries belong to the TIA Portal instance, not to a project. `library_read` (issue #218) reads them, and the rule that content tools act only on the bound project had no place for a Portal-scope object. An exception for global reads would have meant either a second, ungated path to the Portal or a public Portal selector on a content tool.

## Decision

Every content tool call, `library_read` included, needs a verified binding; an all-global batch with no binding fails whole with `binding_conflict`. The binding pins the Portal as well as the project, so a global read uses the bound Portal and the tool takes no Portal argument. Global and project library items take the same bound route (`SendBoundProjectRequestAsync`, worker `WithActiveContext`), with the same identity check and fail-closed gates.

Rejected: a `portalProcessId` argument or an unbound global route. It would add a second selector and a path outside the binding gate that ADR-0007 makes the single place content access is authorized and that ADR-0011 makes fail closed.

The rule is therefore: content tools act only through the verified binding, and may read Portal-scope objects of the bound Portal.

## Consequences

- Global library reads need a project to be open and bound. Reading them with no project open is an open follow-up.
- A global read can still lose its Portal: loss is classified once at the worker boundary, as for every other content tool.
- The Portal identity is not part of a write lease or confirmation recheck, which compare the project binding only. A later write tool on Portal-scope objects must settle that before it ships.
- Capability labels follow the bound-project route, not the object's scope (`library_read` is reported as `projectContent`); see the operations summary.

## Evidence

- [Library read live verification](../superpowers/acceptance/reports/2026-11-06-library-read-live-verification.md)
