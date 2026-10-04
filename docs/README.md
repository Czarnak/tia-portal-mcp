# Documentation

Everything in this tree, grouped by who it is for. **A new document under `docs/` is not
complete until it is listed here** — that rule is what keeps this index trustworthy.

For the project overview, tool list, and quick start, see the [README](../README.md).

## Using the server

| Document | What you will find |
| --- | --- |
| [Installation](guides/installation.md) | Requirements, installation/doctor, explicit binding, 5/15/15 modes, and mode-derived lifecycle confirmation |
| [MCP client configuration](guides/mcp-client-configuration.md) | Client configuration, bind/open migration, lifecycle confirmation, guarded Network migration, and block paths |
| [Troubleshooting](guides/troubleshooting.md) | Common failures, and TIA Portal V21 behaviors verified against a real installation |
| [Supported operations](SupportedOperations/README.md) | Every operation by area — project, PLC, devices, network, HMI, and more |

## Understanding the design

| Document | What you will find |
| --- | --- |
| [Architecture](ARCHITECTURE.md) | Two-process topology, tier enforcement, explicit binding/cursor invalidation, transport, canonical JSON, mode-derived confirmation/audit v2, and testing |

## Building and contributing

| Document | What you will find |
| --- | --- |
| [Contributing](../CONTRIBUTING.md) | Contribution workflow, branch focus, commit message format, pull requests |
| [Building from source](development/building.md) | Restore, build, test with coverage, and run the server locally |
| [Local MCP sandbox testing](development/local-mcp-testing.md) | MCP Inspector, actual-by-default Network inputs, frozen Network fixtures/restoration boundaries, and lifecycle gates |
| [Packaging](development/packaging.md) | Build the NuGet package and install a local branch build as the `tia-mcp` global tool |
| [Bug-fix wave orchestration](development/bug-fix-wave-orchestration.md) | Branch/worktree ownership, wave barriers, offline/live gates, PR creation, and merge protocol |
| [Wave 2 I2/T2 orchestration handoff](development/bug-fix-wave2-i2-t2-orchestration.md) | Current-scope ownership, independent merge policy, I2 live gate, and future-session dispatch instructions |

Agent-facing build and convention reference lives in [AGENTS.md](../AGENTS.md).

## Direction

| Document | What you will find |
| --- | --- |
| [Roadmap](../ROADMAP.md) | Directional priorities for the project as a whole |
| [Network operations roadmap](roadmap/network-operations.md) | Phased delivery of the network tool surface and its JSON contract |
| [Export/import format roadmap](roadmap/export-import-format.md) | Source-format exchange for UDTs, data blocks, and SCL |
| [JSON contract roadmap](roadmap/json-contract.md) | One structured JSON output contract across the tool surface: target envelope, migration phases, the conformance guard, and the batch-tool exclusion |
| [Improvement log](IMPROVEMENT_LOG.md) | Open follow-ups above, completed engineering work below |

## Project history

[`superpowers/`](superpowers/README.md) holds design specs, implementation plans, and live
acceptance reports produced while building features. It is historical process material, not
current documentation — see its index for what is there and how to read it.

Latest process entry: the [Network discovery and interface-qualified node identity repair design](superpowers/specs/2026-10-04-network-discovery-and-interface-node-identity-repair-design.md)
addresses the failed live Network write run; written spec review and implementation are pending.

Earlier process entries: the [approved Network JSON contract and guarded write design](superpowers/specs/2026-10-03-network-json-guarded-write-design.md)
and its [completed offline implementation plan (live acceptance pending)](superpowers/plans/2026-10-03-network-json-guarded-write.md),
with [corrected-candidate offline validation, independent review, coverage and package provenance](superpowers/acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md),
the [lifecycle in read-write, mode-derived confirmation, and runtime project binding design](superpowers/specs/2026-10-01-lifecycle-tiers-and-project-binding-design.md)
(amends the redesign's §4.6 and §4.11) and its [implementation plan](superpowers/plans/2026-10-02-lifecycle-tiers-and-project-binding.md),
with [live acceptance in all three access modes; human read-only dialog observation recorded](superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md),
the [write-safety redesign design (retiring preview→apply tokens)](superpowers/specs/2026-09-29-write-safety-redesign-design.md)
and its [Phase 1 foundation plan](superpowers/plans/2026-09-29-write-safety-phase1-foundation.md)
and [Phase 1b access-mode implementation plan](superpowers/plans/2026-09-30-write-safety-phase1b-access-modes.md),
the [JSON contract Phase 2 standalone tools implementation plan](superpowers/plans/2026-09-30-json-contract-phase2-standalone-tools.md)
and its [offline validation report (live V21 acceptance pending)](superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md)
and [live verification of status, compilation scopes, and failure/rejection responses (complete producer gate still open)](superpowers/acceptance/reports/2026-10-01-json-contract-phase2-live-verification.md)
and the implementation record for [JSON contract Phase 3 guarded lifecycle / write-safety Phase 2](superpowers/plans/2026-09-30-json-contract-phase3-lifecycle.md)
and its [offline validation record](superpowers/acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md)
and [authorized live lifecycle validation](superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md),
the [Multiuser Engineering design](superpowers/specs/2026-09-28-multiuser-engineering-design.md)
and its [PR 1 reference and contract foundation plan](superpowers/plans/2026-09-28-multiuser-pr1-reference-contract-foundation.md),
the [JSON contract Phase 1b required-member enforcement design](superpowers/specs/2026-09-28-json-contract-phase1b-required-members-design.md)
and its [implementation plan](superpowers/plans/2026-09-28-json-contract-phase1b-required-members.md),
the [JSON contract Phase 1a worker-wire plan](superpowers/plans/2026-09-28-json-contract-phase1a-worker-wire.md),
[Open bug parallel pull-request delivery design](superpowers/specs/2026-09-26-open-bug-parallel-pr-delivery-design.md),
with current Wave 2 plans for [I2 truthful block-import outcomes](superpowers/plans/2026-09-27-wave2-i2-truthful-block-import-outcomes.md)
and [T2 type-content binding policy](superpowers/plans/2026-09-27-wave2-t2-type-content-binding-policy.md),
plus Wave 1 plans for [L1 lifecycle rebind and preview safety](superpowers/plans/2026-09-26-wave1-l1-lifecycle-rebind-preview-safety.md),
[K1 per-item batch failure categories](superpowers/plans/2026-09-26-wave1-k1-batch-failure-categories.md),
[P1 PLC compile and cross-reference read integrity](superpowers/plans/2026-09-26-wave1-p1-plc-read-integrity.md),
and [N1 network-device creation names](superpowers/plans/2026-09-26-wave1-n1-network-device-creation.md),
[Network Phases 4-6 completion and delivery design](superpowers/specs/2026-09-21-network-phases-4-6-delivery-design.md),
its [PR 1 Network Phase 4 finish plan](superpowers/plans/2026-09-21-pr1-network-phase4-finish.md),
the [PR 2 Phase 5 fixture qualification and compile-contract plan](superpowers/plans/2026-09-24-pr2-network-phase5-qualification.md),
the [Phase 5 qualified IO-system contract](superpowers/specs/2026-09-24-network-phase5-qualified-contract.md),
the [Phase 5 PR 2 worker-only V21 qualification report](superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md),
the [Network Phase 4 current-revision live report (bounded PASS on 2026-09-24, prior failed attempt retained)](superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md),
[Issue #30 PLC block header metadata design](superpowers/specs/2026-09-13-issue-30-plc-block-header-metadata-design.md),
its [implementation plan](superpowers/plans/2026-09-13-issue-30-plc-block-header-metadata.md),
[.NET 10 migration and dependency alignment design](superpowers/specs/2026-09-12-dotnet-10-migration-design.md),
its [implementation plan](superpowers/plans/2026-09-12-dotnet-10-migration.md),
[issue #32 scalable project-tree browsing v3 design](superpowers/specs/2026-09-06-issue-32-scalable-project-tree-browsing-v3-design.md),
its [implementation plan](superpowers/plans/2026-09-06-issue-32-scalable-project-tree-browsing-v3.md),
and [accepted read-only live report with explicit ambiguity waiver (2026-09-12)](superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md),
[write-safety preview and registered-surface hardening design](superpowers/specs/2026-09-01-write-safety-hardening-design.md),
with separate plans for [PR 1 explicit MCP tool annotations](superpowers/plans/2026-09-01-pr1-explicit-mcp-tool-annotations.md),
[its completed live acceptance report](superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md),
[PR 2 registered-tool delegation](superpowers/plans/2026-09-01-pr2-registered-tool-delegation.md),
[its completed live acceptance report](superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md),
[PR 3 exact `update_tag` safety snapshots](superpowers/plans/2026-09-01-pr3-update-tag-safety-snapshot.md),
[its completed mandatory live acceptance report](superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md),
[PR 4 bounded structured preview diffs](superpowers/plans/2026-09-01-pr4-structured-preview-diff.md),
[its completed live Preview and authorized Apply/restore/compile acceptance report (2026-09-05)](superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md),
[PR 5 tag-operation safety scopes](superpowers/plans/2026-09-01-pr5-tag-operation-safety-scopes.md),
[its completed mandatory live acceptance report (2026-09-05)](superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md),
[PR 6 project-tree safety scopes](superpowers/plans/2026-09-01-pr6-project-tree-safety-scopes.md),
and [its completed mandatory live acceptance report (2026-09-06)](superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md);
[project completeness and hardware pagination design](superpowers/specs/2026-08-28-issue-31-project-completeness-pagination-design.md),
its [PR 1 project enumeration completeness plan](superpowers/plans/2026-08-28-project-enumeration-completeness.md),
the [PR 2 hardware pagination plan](superpowers/plans/2026-08-29-hardware-pagination.md),
[PR #29 binding findings repair design](superpowers/specs/2026-08-28-pr29-binding-findings-repair-design.md),
and its [implementation plan](superpowers/plans/2026-08-28-pr29-binding-findings-repair.md).

PR 5's [current tag-safety acceptance boundary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md#tag-safety-acceptance-boundary)
now links its completed guarded live V21 result while keeping public table-list completeness,
broader snapshots, Software Unit namespace collisions, multilingual comments, PLC control, and
plant acceptance explicitly outside the accepted scope.

PR 6's [current project-tree safety acceptance boundary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-tree-safety-acceptance-boundary)
links the completed PLC-global and Software Unit guarded live V21 results while keeping broader
snapshot narrowing, PLC start/stop, save or persistence behavior, and plant acceptance outside the
accepted scope.
