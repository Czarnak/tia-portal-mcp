# Documentation

Everything in this tree, grouped by who it is for. **A new document under `docs/` is not
complete until it is reachable from this index**, directly or through the index of its
directory — that rule is what keeps this index trustworthy.

For the project overview, tool list, and quick start, see the [README](../README.md).

## Using the server

| Document | What you will find |
| --- | --- |
| [Installation](guides/installation.md) | Requirements, installation/doctor, explicit binding, access modes, and mode-derived lifecycle confirmation |
| [MCP client configuration](guides/mcp-client-configuration.md) | Client configuration, bind/open migration, lifecycle confirmation, guarded Network and `plc_write` migration, block paths, response size limits, and worker timeout |
| [Troubleshooting](guides/troubleshooting.md) | Common failures, and TIA Portal V21 behaviors verified against a real installation |
| [Supported operations](SupportedOperations/README.md) | Every operation by area — project, PLC, devices, network, HMI, and more |

## Understanding the design

| Document | What you will find |
| --- | --- |
| [Architecture](ARCHITECTURE.md) | Two-process topology, tier enforcement, explicit binding/cursor invalidation, transport, canonical JSON, mode-derived confirmation/audit v2, and testing |
| [Network operations and paging matrix](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#paging-matrix) | Cross-tool paging fields, defaults, bounds, scan/continuation semantics and per-domain recovery references |
| [HMI operations (`hmi_read`, `hmi_write`)](SupportedOperations/HMI_OPERATIONS_SUMMARY.md) | The 20 read-only WinCC Unified operations and nine writes, read DTO additions, paging/completeness, guards, partial failures, verification and limits |
| [VCI operations (`vci_read`, `vci_write`)](SupportedOperations/VCI_OPERATIONS_SUMMARY.md) | Standalone workspace and mapping operations, formats, sync rules, path guards, recovery and evidence limits |
| [Multiuser local sessions, inventory and acceptance boundary](SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md) | Exact AMC adoption, ALS opening/basic status, persistent inventory, preservation guards and remaining session limits |

## Building and contributing

| Document | What you will find |
| --- | --- |
| [Contributing](../CONTRIBUTING.md) | Contribution workflow, branch focus, commit message format, pull requests |
| [Building from source](development/building.md) | Restore, build, test with coverage, and run the server locally |
| [Local MCP sandbox testing](development/local-mcp-testing.md) | MCP Inspector, actual-by-default Network inputs, frozen Network fixtures/restoration boundaries, and lifecycle gates |
| [Packaging](development/packaging.md) | Build the NuGet package and install a local branch build as the `tia-mcp` global tool |
| [Measuring write timing](development/measuring-write-timing.md) | The shared `scripts/timing` client: run a write workload against a disposable project, validate the archive, read the output files |
| [Codebase review runbook](development/codebase-review.md) | How to review the whole repository across several runs: written design rules as tests, domain reviews end to end, rule sweeps, reviews of changes since the baseline, findings ledger, verification order and rotation |

Agent-facing build and convention reference lives in [AGENTS.md](../AGENTS.md).

## Direction

| Document | What you will find |
| --- | --- |
| [Roadmap](../ROADMAP.md) | Directional priorities for the project as a whole |
| [Network operations roadmap](roadmap/network-operations.md) | Phased delivery of the network tool surface and its JSON contract |
| [Export/import format roadmap](roadmap/export-import-format.md) | Source-format exchange for UDTs, data blocks, and SCL |
| [JSON contract roadmap](roadmap/json-contract.md) | One structured JSON output contract across the tool surface: target envelope, migration phases, the conformance guard, and the retired batch tools |
| [Improvement log](IMPROVEMENT_LOG.md) | Open follow-ups and the latest completed entry; history lives in ADRs, acceptance reports and the archive tag |

## Decisions and history

| Document | What you will find |
| --- | --- |
| [Architecture decision records](adr/README.md) | Why the design is as it is, in project order; each ADR cites the original plans and specs |
| [Live verification reports and raw evidence](superpowers/README.md) | Kept live-verification reports with their scope and limits, and the index of kept raw Openness evidence |
