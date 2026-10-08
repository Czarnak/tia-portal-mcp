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
| 2026-10-08 | [`create_block` OB creation, issue #75 (approved; Phase 0 spike done)](specs/2026-10-08-ob-creation-design.md) |
| 2026-10-05 | [PLC read/write tools and standalone cross-references (approved; PR A and PR B implemented and live-accepted)](specs/2026-10-05-plc-read-write-and-cross-references-design.md) |
| 2026-10-04 | [Network discovery and interface-qualified node identity repair (implemented; full live acceptance pending)](specs/2026-10-04-network-discovery-and-interface-node-identity-repair-design.md) |
| 2026-10-03 | [Network JSON contract and guarded writes (offline-qualified; live acceptance pending)](specs/2026-10-03-network-json-guarded-write-design.md) |
| 2026-10-01 | [Lifecycle in read-write, mode-derived confirmation, and runtime project binding](specs/2026-10-01-lifecycle-tiers-and-project-binding-design.md) |
| 2026-09-29 | [Write-safety redesign: retiring preview→apply tokens](specs/2026-09-29-write-safety-redesign-design.md) |
| 2026-09-28 | [Multiuser Engineering](specs/2026-09-28-multiuser-engineering-design.md) |
| 2026-09-28 | [JSON contract Phase 1b — required-member enforcement](specs/2026-09-28-json-contract-phase1b-required-members-design.md) |
| 2026-09-26 | [Open bug parallel pull-request delivery](specs/2026-09-26-open-bug-parallel-pr-delivery-design.md) |
| 2026-09-26 | [Network Phase 5 qualified IO-system contract for later public slices](specs/2026-09-24-network-phase5-qualified-contract.md) |
| 2026-09-21 | [Network Phases 4-6 completion and delivery](specs/2026-09-21-network-phases-4-6-delivery-design.md) |
| 2026-09-13 | [Issue #30 PLC block header metadata](specs/2026-09-13-issue-30-plc-block-header-metadata-design.md) |
| 2026-09-12 | [.NET 10 migration and dependency alignment](specs/2026-09-12-dotnet-10-migration-design.md) |
| 2026-09-06 | [Issue #32 scalable project-tree browsing v3](specs/2026-09-06-issue-32-scalable-project-tree-browsing-v3-design.md) |
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
| 2026-10-08 | [`create_block` OB creation, issue #75 (awaiting review)](plans/2026-10-08-ob-creation.md) |
| 2026-10-07 | [Multiuser PR4 — open existing sessions (implemented/offline-qualified; scoped maintainer live acceptance)](plans/2026-10-07-multiuser-pr4-open-existing-sessions.md) |
| 2026-10-05 | [Multiuser PR 3 — inventory through bind_project (offline-qualified; live acceptance passed)](plans/2026-10-05-multiuser-pr3-read-only-inventory.md) |
| 2026-10-05 | [Multiuser PR 2 — active project context](plans/2026-10-05-multiuser-pr2-active-project-context.md) |
| 2026-10-06 | [PLC write, PR B — `plc_write`, batch-tool and token-core retirement (implemented; live-accepted)](plans/2026-10-06-plc-write.md) |
| 2026-10-05 | [PLC read and standalone cross-references, PR A (implemented; live-accepted)](plans/2026-10-05-plc-read-and-cross-references.md) |
| 2026-10-04 | [Network discovery and interface-qualified node identity repair (offline-qualified; partial live verification)](plans/2026-10-04-network-discovery-and-interface-node-identity-repair.md) |
| 2026-10-03 | [Network JSON contract and guarded writes (implementation/offline qualification complete; live acceptance pending)](plans/2026-10-03-network-json-guarded-write.md) |
| 2026-10-02 | [Lifecycle in read-write and runtime project binding](plans/2026-10-02-lifecycle-tiers-and-project-binding.md) |
| 2026-09-30 | [Write-safety redesign Phase 1b — access modes](plans/2026-09-30-write-safety-phase1b-access-modes.md) |
| 2026-09-29 | [Write-safety redesign Phase 1 — foundation](plans/2026-09-29-write-safety-phase1-foundation.md) |
| 2026-09-28 | [Multiuser PR 1 — reference and contract foundation](plans/2026-09-28-multiuser-pr1-reference-contract-foundation.md) |
| 2026-09-28 | [JSON contract Phase 1b — required-member enforcement](plans/2026-09-28-json-contract-phase1b-required-members.md) |
| 2026-09-28 | [JSON contract Phase 1a — worker wire normalization](plans/2026-09-28-json-contract-phase1a-worker-wire.md) |
| 2026-09-27 | [Wave 2 I2 — truthful block-import outcomes](plans/2026-09-27-wave2-i2-truthful-block-import-outcomes.md) |
| 2026-09-27 | [Wave 2 T2 — type-content binding policy](plans/2026-09-27-wave2-t2-type-content-binding-policy.md) |
| 2026-09-26 | [Wave 1 L1 — lifecycle rebind and preview safety](plans/2026-09-26-wave1-l1-lifecycle-rebind-preview-safety.md) |
| 2026-09-26 | [Wave 1 K1 — per-item batch failure categories](plans/2026-09-26-wave1-k1-batch-failure-categories.md) |
| 2026-09-26 | [Wave 1 P1 — PLC compile and cross-reference read integrity](plans/2026-09-26-wave1-p1-plc-read-integrity.md) |
| 2026-09-26 | [Wave 1 N1 — network-device creation names](plans/2026-09-26-wave1-n1-network-device-creation.md) |
| 2026-09-24 | [PR 2 — Network Phase 5 fixture qualification and compile contract](plans/2026-09-24-pr2-network-phase5-qualification.md) |
| 2026-09-21 | [PR 1 — Network Phase 4 finish and current-revision live acceptance](plans/2026-09-21-pr1-network-phase4-finish.md) |
| 2026-09-13 | [Issue #30 PLC block header metadata](plans/2026-09-13-issue-30-plc-block-header-metadata.md) |
| 2026-09-12 | [.NET 10 migration and dependency alignment](plans/2026-09-12-dotnet-10-migration.md) |
| 2026-09-06 | [Issue #32 scalable project-tree browsing v3](plans/2026-09-06-issue-32-scalable-project-tree-browsing-v3.md) |
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

## Acceptance definitions

| Date | Document |
| --- | --- |
| 2026-10-07 | [Multiuser PR4 — installed-public-tool live definition and original L01–L19 expectations](acceptance/2026-10-07-multiuser-pr4-live-definition.md) |

## Acceptance reports

PR 5 and PR 6 qualified token-bound safety snapshots that `plc_write` has since replaced; their
reports are evidence for the retired token flow only.

PR 5 tag-operation safety scopes completed its offline/FakeWorker, static harness-contract, and
guarded live TIA Portal V21 acceptance. The report records all three successful modes, exact
saved-baseline verification, source restoration, and the bounded deferred scope. See the
[current acceptance boundary](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md#tag-safety-acceptance-boundary).

PR 6 project-tree safety scopes completed its offline/FakeWorker, static harness-contract, and
guarded live TIA Portal V21 acceptance for both PLC-global and Software Unit owners. The report
records successful Inventory, Preview, authorized Apply/restoration, byte-equivalent restoration,
and final compile evidence. See the
[current acceptance boundary](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-tree-safety-acceptance-boundary).

Evidence from completed and incomplete live runs against TIA Portal V21, plus prepared reports whose
live gate is explicitly pending.

| Date | Document |
| --- | --- |
| 2026-10-08 | [OB creation Phase 0 spike (issue #75) — SimaticML import passes under the SimaticSD default; import never renumbers and accepts duplicate singletons; STL rejects `SetENOAutomatically`; S7-1200 import refuses unsupported classes, S7-1200 G2 accepts all](acceptance/reports/2026-10-08-ob-creation-spike.md) |
| 2026-10-08 | [Multiuser PR4 — scoped maintainer acceptance, offline dialog failures, audits and restoration](acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md) |
| 2026-10-08 | [`hmi_read` installed-tool live acceptance — all 20 operations, the error/edge paths and the re-check PASS (42/42), read-only; Medium redaction finding D1 and Low D2 fixed and re-verified live, Low D3 (scan performance) accepted, non-integral `Double` not exercised live](acceptance/reports/2026-10-08-hmi-read-live-acceptance.md) |
| 2026-10-07 | [`hmi_read` live spike S1–S10 — read-only probe of four Unified Comfort panels and one Unified PC station; default `limit`, depth cap, Classic type, variant encoding, and the `HmiTag.ConfirmationType` Portal crash](acceptance/reports/2026-10-07-hmi-read-spike.md) |
| 2026-10-07 | [PLC write (PR B) — offline qualification and live acceptance in read-write and full](acceptance/reports/2026-10-07-plc-write-validation.md) |
| 2026-10-06 | [Multiuser PR 3 — offline qualification; live acceptance passed by maintainer decision](acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md) |
| 2026-10-06 | [Multiuser PR 3 — installed-tool live acceptance finished and passed; observations and non-blocking evidence limitations](acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md) |
| 2026-10-05 | [Multiuser PR 2 — offline qualification and bounded standalone live acceptance](acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md) |
| 2026-10-05 | [PLC read and standalone cross-references (PR A) — offline pass and read-only live PASS with recorded limits](acceptance/reports/2026-10-05-plc-read-and-cross-references-validation.md) |
| 2026-10-05 | [Network discovery and interface-qualified identity repair — offline pass, partial live verification and PR handoff](acceptance/reports/2026-10-04-network-discovery-and-interface-node-identity-repair-validation.md) |
| 2026-10-04 | [Network JSON contract and guarded writes — corrected-candidate offline validation; live acceptance pending](acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md) |
| 2026-10-03 | [Lifecycle tiers and runtime binding — all three modes live-accepted; human read-only Openness dialog observation recorded](acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md) |
| 2026-10-01 | [JSON contract Phase 3 — guarded lifecycle live validation](acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md) |
| 2026-10-01 | [JSON contract Phase 3 — offline validation](acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md) |
| 2026-10-01 | [JSON contract Phase 2 — bounded live verification; producer gate remains open](acceptance/reports/2026-10-01-json-contract-phase2-live-verification.md) |
| 2026-09-30 | [JSON contract Phase 2 — offline validation](acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md) |
| 2026-09-26 | [Network Phase 5 PR 2 — bounded worker-only V21 qualification](acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md) |
| 2026-09-24 | [Network Phase 4 PR 1 — bounded current-revision live PASS; 2026-09-23 failed attempt retained](acceptance/reports/2026-09-21-network-phase4-current-revision-live.md) |
| 2026-09-12 | [Issue #32 — project-tree browsing v3 — accepted read-only PASS with explicit live ambiguity waiver](acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md) |
| 2026-09-06 | [PR 6 — project-tree safety scopes — mandatory live PASS](acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md) |
| 2026-09-05 | [PR 5 — tag-operation safety scopes — mandatory live PASS](acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md) |
| 2026-09-05 | [PR 3 — exact `update_tag` safety snapshot — mandatory live PASS](acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md) |
| 2026-09-05 | [PR 4 — structured preview diff — live Preview and authorized Apply/restore/compile acceptance completed](acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md) |
| 2026-09-01 | [PR 2 — registered-tool delegation — live](acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md) |
| 2026-09-01 | [PR 1 — explicit MCP tool annotations — live](acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md) |
| 2026-08-14 | [Structured I/O map defect fixes — live](acceptance/reports/2026-08-14-io-map-defect-fixes-live.md) |
| 2026-08-01 | [Network Phase 1 — rerun](acceptance/reports/2026-08-01-16-56-00-network-operations-phase1-rerun.md) |
| 2026-08-01 | [Network Phase 1](acceptance/reports/2026-08-01-16-48-48-network-operations-phase1.md) |
