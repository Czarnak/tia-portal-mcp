# ADR-0016: library_write ships in stages, prompts only for global-library lifecycle, and draws no ownership line

**Status:** Accepted
**Decided:** 2026-11-06   **Recorded:** 2026-11-07
**Area:** library
**Supersedes:** none. It settles the Portal-identity consequence recorded in [ADR-0015](0015-portal-scope-reads-under-binding-gate.md); the other ADR-0015 decisions remain in force.
**Current home:** [ARCHITECTURE: Library writes](../ARCHITECTURE.md#library-writes-library_write); [Library operations summary](../SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md)

## Context

Issue #218 proposed 25 library write operations. They range from lifecycle and folders, where Portal refuses most bad requests (native-rejection probes, 2026-11-06), to versioned types and `UpdateProject`, which killed Portal or removed unused instances in probes. One tool also mixes Portal-scope objects (global libraries) with project and PLC objects, and its lifecycle is not the project lifecycle that ADR-0007 gates with a prompt on every call.

## Decision

- **Staged by risk.** `library_write` ships 12 operations: global lifecycle, folders and master copies. Versioned types, instance updates, replace and moves follow in later PRs, because they carry hazards (version validation, dependency closure, `UpdateProject`) that must not delay the rest.
- **The prompt follows the items.** In read-write, a call containing at least one global-library lifecycle operation asks once; a content-only call never asks; full never asks. This needed two pipeline hooks on `IWriteDomain` (`ConfirmsCall(items)`, `CapabilityOperations(items)`), whose defaults leave the other write domains unchanged. Capability is per item: anything touching the project library or a PLC is standalone-only, global-only items follow the global capability.
- **No ownership distinction.** `save_global`, `close_global` and `archive_global` act on any user global library open in the bound Portal, whoever opened it. The protection is the named prompt and a required `closeAction`, not a worker-owned/borrowed ledger. Global-library lifecycle is Portal-scope and is not covered by the project rule that create, save-as, archive and close are standalone-only.
- **Portal identity needs no new check.** The binding snapshot already carries the Portal process id, so the lease, the confirmation rechecks and the per-request gates cover global writes.
- **Portal stays the refusal authority** (ADR-0012). The tool owns only what Portal does not report: required `closeAction` (silent discard), the `.zal21` archive extension, master-copy collisions and renames, the `ThrowIfExists` instantiation mode and duplicate library names.
- **No intra-call dependencies, no snapshot drop.** Created, opened and retrieved libraries have unpredictable paths, so later items cannot target them in the same call. Library read snapshots are not dropped after a write; continuations already mark deleted rows.

## Consequences

- Callers split dependent work into two calls (for example `open_global`, then `create_folder`).
- A client that auto-accepts prompts removes the lifecycle protection; discard stays visible through the `library_discards_changes` guard.
- The next library PR reuses the hooks, the capabilities and the typed outcome validation.

## Evidence

- [Native-rejection probes](../superpowers/acceptance/reports/2026-11-06-library-write-native-probes.md)
- [Library write live verification](../superpowers/acceptance/reports/2026-11-07-library-write-live-verification.md)
