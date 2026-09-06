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
| 2026-09-01 | [Write-safety preview and registered-surface hardening](specs/2026-09-01-write-safety-hardening-design.md) |
| 2026-08-28 | [Issue #31 project completeness and hardware pagination](specs/2026-08-28-issue-31-project-completeness-pagination-design.md) |
| 2026-08-28 | [PR #29 binding findings repair](specs/2026-08-28-pr29-binding-findings-repair-design.md) |
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
| 2026-09-01 | [PR 1 — explicit MCP tool annotations](plans/2026-09-01-pr1-explicit-mcp-tool-annotations.md) |
| 2026-09-01 | [PR 2 — registered-tool delegation](plans/2026-09-01-pr2-registered-tool-delegation.md) |
| 2026-09-01 | [PR 3 — exact `update_tag` safety snapshot](plans/2026-09-01-pr3-update-tag-safety-snapshot.md) |
| 2026-09-01 | [PR 4 — bounded structured preview diff](plans/2026-09-01-pr4-structured-preview-diff.md) |
| 2026-09-01 | [PR 5 — tag-operation safety scopes](plans/2026-09-01-pr5-tag-operation-safety-scopes.md) |
| 2026-09-01 | [PR 6 — project-tree safety scopes](plans/2026-09-01-pr6-project-tree-safety-scopes.md) |
| 2026-08-29 | [Hardware configuration pagination](plans/2026-08-29-hardware-pagination.md) |
| 2026-08-28 | [Project enumeration completeness](plans/2026-08-28-project-enumeration-completeness.md) |
| 2026-08-28 | [PR #29 binding findings repair](plans/2026-08-28-pr29-binding-findings-repair.md) |
| 2026-08-15 | [PR #27 review fixes](plans/2026-08-15-pr27-review-fixes.md) |
| 2026-08-06 | [Network Phase 4 — subnet lifecycle](plans/2026-08-06-network-phase4-subnet-lifecycle.md) |
| 2026-08-04 | [Network Phase 3 — identity and introspection](plans/2026-08-04-network-operations-phase3-identity-introspection.md) |
| 2026-08-02 | [Network Phase 2 — JSON contract](plans/2026-08-02-network-operations-phase2-json-contract.md) |
| 2026-08-01 | [Network Phase 1](plans/2026-08-01-network-operations-phase1.md) |
| 2026-07-31 | [Standalone project tools](plans/2026-07-31-standalone-project-tools.md) |
| 2026-07-27 | [SCL external-source support](plans/2026-07-27-scl-external-source.md) |
| 2026-07-26 | [UDT and DB external-source support](plans/2026-07-26-udt-db-external-source.md) |

## Acceptance reports

PR 5 tag-operation safety scopes completed its offline/FakeWorker, static harness-contract, and
guarded live TIA Portal V21 acceptance. The report records all three successful modes, exact
saved-baseline verification, source restoration, and the bounded deferred scope. See the
[current acceptance boundary](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md#tag-safety-acceptance-boundary).

PR 6 project-tree safety scopes completed its offline/FakeWorker, static harness-contract, and
guarded live TIA Portal V21 acceptance for both PLC-global and Software Unit owners. The report
records successful Inventory, Preview, authorized Apply/restoration, byte-equivalent restoration,
and final compile evidence. See the
[current acceptance boundary](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-tree-safety-acceptance-boundary).

Evidence from completed live runs against TIA Portal V21, plus prepared reports whose live gate is
explicitly pending.

| Date | Document |
| --- | --- |
| 2026-09-06 | [PR 6 — project-tree safety scopes — mandatory live PASS](acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md) |
| 2026-09-05 | [PR 5 — tag-operation safety scopes — mandatory live PASS](acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md) |
| 2026-09-05 | [PR 3 — exact `update_tag` safety snapshot — mandatory live PASS](acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md) |
| 2026-09-05 | [PR 4 — structured preview diff — live Preview and authorized Apply/restore/compile acceptance completed](acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md) |
| 2026-09-01 | [PR 2 — registered-tool delegation — live](acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md) |
| 2026-09-01 | [PR 1 — explicit MCP tool annotations — live](acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md) |
| 2026-08-14 | [Structured I/O map defect fixes — live](acceptance/reports/2026-08-14-io-map-defect-fixes-live.md) |
| 2026-08-01 | [Network Phase 1 — rerun](acceptance/reports/2026-08-01-16-56-00-network-operations-phase1-rerun.md) |
| 2026-08-01 | [Network Phase 1](acceptance/reports/2026-08-01-16-48-48-network-operations-phase1.md) |
