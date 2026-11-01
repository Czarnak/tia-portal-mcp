# ADR-0014: VCI adopts native rejection evidence and accepts Portal's overwrite behavior

**Status:** Accepted
**Decided:** 2026-10-31 to 2026-11-01   **Recorded:** 2026-11-01
**Area:** vci
**Supersedes:** [ADR-0010](0010-vci-workspace-primitives.md)'s export collision prevention and [ADR-0012](0012-portal-as-rejection-authority.md)'s VCI exclusions; their other decisions remain in force.
**Current home:** [ARCHITECTURE: VCI reads and writes](../ARCHITECTURE.md#vci-reads-and-writes); [VCI operations summary](../SupportedOperations/VCI_OPERATIONS_SUMMARY.md)

## Context

VCI flattened native failures and separately checked file-key collisions, duplicate workspace names
and post-write existence. Those checks hid Portal's answer and repeated work across a large export.
Stage 0 showed native duplicate-name and export refusals, allowed nested roots and native behavior
at occupied file keys. The maintainer accepted Portal's overwrite behavior while retaining shared-root,
directory and containment boundaries. That changes the collision decision in ADR-0010 and brings
VCI into the rejection-authority and shared-verifier policy from ADR-0012.

## Decision

VCI surfaces typed native failure evidence and preserves partial outcomes through Portal-loss
classification, while its read-only workspace-create recheck retains read classification. Diagnostics
preserve encounter precedence across prefilter and synchronization, not global selected-leaf order.
It shares the pipeline's trusted/superseded verification without native read-back;
callers inspect current mappings, files and compile results separately. Later same-workspace items
execute without their own inventory plan or re-plan. Export leaves existing-file and mapping-key
behavior to Portal, including overwrite, companion-file deletion and disruption of another mapping
inside the authorized directory. Exact shared-root protection, directory boundaries and containment
remain server responsibilities; duplicate workspace names reach Portal.

Rejected: retain collision prevention for friendly errors or file preservation. It duplicates
native behavior, preserves repeated scans and contradicts the accepted native-overwrite contract.

## Consequences

- Native diagnostics survive instead of becoming generic failures; partial execution stays visible.
- An occupied key can change files or another mapping. Callers must account for those effects
  and inspect state before retrying a failed or uncertain write.
- Trusted verification is completion evidence, not proof of final native state. Import still needs
  an explicit compile, and project changes still need an explicit save.
- Removing collision scans does not guarantee a speedup; measured synchronization regressions and
  untested native fault locations remain bounded by the qualification report.

## Evidence

- [Native-rejection live verification and Stage 0 provenance](../superpowers/acceptance/reports/2026-11-01-vci-write-native-rejections-live-verification.md)
- [Original VCI live acceptance](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md)
