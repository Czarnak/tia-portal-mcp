# Documentation

Everything in this tree, grouped by who it is for. **A new document under `docs/` is not
complete until it is listed here** — that rule is what keeps this index trustworthy.

For the project overview, tool list, and quick start, see the [README](../README.md).

## Using the server

| Document | What you will find |
| --- | --- |
| [Installation](guides/installation.md) | Requirements, `dotnet tool install`, the `doctor` command, registering with an MCP client, and the read-only / read-write access modes |
| [MCP client configuration](guides/mcp-client-configuration.md) | Client configuration reference and how block paths are addressed |
| [Troubleshooting](guides/troubleshooting.md) | Common failures, and TIA Portal V21 behaviors verified against a real installation |
| [Supported operations](SupportedOperations/README.md) | Every operation by area — project, PLC, devices, network, HMI, and more |

## Understanding the design

| Document | What you will find |
| --- | --- |
| [Architecture](ARCHITECTURE.md) | Two-process topology, access-mode enforcement, worker transport, batch and network execution, the canonical JSON seam, write safety, and testing |

## Building and contributing

| Document | What you will find |
| --- | --- |
| [Contributing](../CONTRIBUTING.md) | Contribution workflow, branch focus, commit message format, pull requests |
| [Building from source](development/building.md) | Restore, build, test with coverage, and run the server locally |
| [Local MCP sandbox testing](development/local-mcp-testing.md) | The MCP Inspector loop against a disposable project copy |
| [Packaging](development/packaging.md) | Build the NuGet package and install a local branch build as the `tia-mcp` global tool |

Agent-facing build and convention reference lives in [AGENTS.md](../AGENTS.md).

## Direction

| Document | What you will find |
| --- | --- |
| [Roadmap](../ROADMAP.md) | Directional priorities for the project as a whole |
| [Network operations roadmap](roadmap/network-operations.md) | Phased delivery of the network tool surface and its JSON contract |
| [Export/import format roadmap](roadmap/export-import-format.md) | Source-format exchange for UDTs, data blocks, and SCL |
| [Improvement log](IMPROVEMENT_LOG.md) | Open follow-ups above, completed engineering work below |

## Project history

[`superpowers/`](superpowers/README.md) holds design specs, implementation plans, and live
acceptance reports produced while building features. It is historical process material, not
current documentation — see its index for what is there and how to read it.

Latest process entries: [write-safety preview and registered-surface hardening design](superpowers/specs/2026-09-01-write-safety-hardening-design.md),
with separate plans for [PR 1 explicit MCP tool annotations](superpowers/plans/2026-09-01-pr1-explicit-mcp-tool-annotations.md),
[its completed live acceptance report](superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md),
[PR 2 registered-tool delegation](superpowers/plans/2026-09-01-pr2-registered-tool-delegation.md),
[its completed live acceptance report](superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md),
[PR 3 exact `update_tag` safety snapshots](superpowers/plans/2026-09-01-pr3-update-tag-safety-snapshot.md),
[its completed mandatory live acceptance report](superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md),
[PR 4 bounded structured preview diffs](superpowers/plans/2026-09-01-pr4-structured-preview-diff.md),
[its pending live acceptance report](superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md),
[PR 5 tag-operation safety scopes](superpowers/plans/2026-09-01-pr5-tag-operation-safety-scopes.md),
and [PR 6 project-tree safety scopes](superpowers/plans/2026-09-01-pr6-project-tree-safety-scopes.md);
[project completeness and hardware pagination design](superpowers/specs/2026-08-28-issue-31-project-completeness-pagination-design.md),
its [PR 1 project enumeration completeness plan](superpowers/plans/2026-08-28-project-enumeration-completeness.md),
the [PR 2 hardware pagination plan](superpowers/plans/2026-08-29-hardware-pagination.md),
[PR #29 binding findings repair design](superpowers/specs/2026-08-28-pr29-binding-findings-repair-design.md),
and its [implementation plan](superpowers/plans/2026-08-28-pr29-binding-findings-repair.md).
