# Project history — specs, plans, and acceptance reports

**This tree is development-process history, not current documentation.**

It holds the design specs, implementation plans, and live acceptance reports produced while
building features. Read it to understand *why* something was built the way it was, or to audit
what was verified and when.

Do not read it as a description of how the server behaves today:

- A document describes the state of the world **on its date**. Later work may have superseded it.
- Designs here were sometimes revised during implementation. The code, [ARCHITECTURE.md](../ARCHITECTURE.md),
  and [SupportedOperations](../SupportedOperations/README.md) are the authorities on current behavior.
- Nothing here is a support document. For using the server, start at the [documentation index](../README.md).

These files are kept for auditability and are intentionally not pruned.

## Specs

Design documents, written before implementation.

| Date | Document |
| --- | --- |
| 2026-08-08 | [VCI workspace Phase 1 — live characterization and contract baseline](specs/2026-08-08-vci-workspace-phase1-design.md) |
| 2026-08-07 | [Documentation reorganization](specs/2026-08-07-docs-reorganization-design.md) |
| 2026-08-06 | [Network Phase 4 — subnet lifecycle](specs/2026-08-06-network-phase4-subnet-lifecycle-design.md) |
| 2026-08-03 | [Network Phase 3 — identity and introspection](specs/2026-08-03-network-operations-phase3-identity-introspection-design.md) |
| 2026-08-02 | [Network Phase 2 — structured JSON contract](specs/2026-08-02-network-operations-phase2-json-contract-design.md) |
| 2026-08-01 | [Network Phase 1](specs/2026-08-01-network-operations-phase1-design.md) |
| 2026-07-31 | [Standalone project tools](specs/2026-07-31-standalone-project-tools-design.md) |
| 2026-07-27 | [SCL external-source support](specs/2026-07-27-scl-external-source-design.md) |
| 2026-07-26 | [UDT and DB external-source support](specs/2026-07-26-udt-db-external-source-design.md) |

## Plans

Task-level implementation plans derived from the specs above.

| Date | Document |
| --- | --- |
| 2026-08-09 | [VCI workspace Phase 1 — mutation probe](plans/2026-08-09-vci-workspace-phase1-mutation-probe.md) |
| 2026-08-08 | [VCI workspace Phase 1 — read-only probe](plans/2026-08-08-vci-workspace-phase1-read-probe.md) |
| 2026-08-06 | [Network Phase 4 — subnet lifecycle](plans/2026-08-06-network-phase4-subnet-lifecycle.md) |
| 2026-08-04 | [Network Phase 3 — identity and introspection](plans/2026-08-04-network-operations-phase3-identity-introspection.md) |
| 2026-08-02 | [Network Phase 2 — JSON contract](plans/2026-08-02-network-operations-phase2-json-contract.md) |
| 2026-08-01 | [Network Phase 1](plans/2026-08-01-network-operations-phase1.md) |
| 2026-07-31 | [Standalone project tools](plans/2026-07-31-standalone-project-tools.md) |
| 2026-07-27 | [SCL external-source support](plans/2026-07-27-scl-external-source.md) |
| 2026-07-26 | [UDT and DB external-source support](plans/2026-07-26-udt-db-external-source.md) |

## Acceptance reports

Evidence from live runs against TIA Portal V21.

| Date | Document |
| --- | --- |
| 2026-08-09 | [VCI workspace Phase 1 — read-only probe](acceptance/reports/20260809T072541470Z-d49e4677-vci-workspace-phase1-read-probe.md) |
| 2026-08-01 | [Network Phase 1 — rerun](acceptance/reports/2026-08-01-16-56-00-network-operations-phase1-rerun.md) |
| 2026-08-01 | [Network Phase 1](acceptance/reports/2026-08-01-16-48-48-network-operations-phase1.md) |
