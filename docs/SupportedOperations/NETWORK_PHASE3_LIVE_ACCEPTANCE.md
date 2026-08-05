# Network Phase 3 Live Acceptance

Status: **Design review required** after the 2026-08-05 run against the prepared, disposable TIA
Portal V21 acceptance fixture at source revision
`702103a5e5223c23db7e0918cdaf12ffd3de38f1`.

The required fixture matrix, payload repeatability, list-value measurement, and raw probe all
completed. The separate `list_network_objects` retention condition passed. Final Phase 3
stabilization does not yet pass the plan's stricter connection-identity rule because only three of
eight observed communication connections had complete selectors. A reviewed acceptance-contract
revision or a selector implementation change is required before this page can record a pass.

This gate covers the read-only Phase 3 identity and introspection contract. It does not authorize
or certify a save, compile, download, network write, commissioning action, or live-hardware
outcome. The harness did not capture the exact V21 product build, so this result is scoped to V21
rather than a specific maintenance build.

## Execution and provenance

The four modes of `scripts/live-test-network-phase3.ps1` were run sequentially against the active
fixture. Every command exited 0 and wrote a timestamped JSON evidence file:

| Mode | Evidence | Result |
|---|---|---|
| `Matrix` | `artifacts/live-network-phase3/20260805-224308818-matrix.json` | Required fixture matrix complete. |
| `Repeatability` | `artifacts/live-network-phase3/20260805-224143879-repeatability.json` | Discovery payload and all targeted inspections repeatable. |
| `MeasureListValue` | `artifacts/live-network-phase3/20260805-224429434-measurelistvalue.json` | Retention condition 2 passed. |
| `RawProbe` | `artifacts/live-network-phase3/20260805-224534336-rawprobe.json` | Required raw metadata probes complete. |

Each artifact reports a clean source tree, the same revision for source, launched host, and
launched worker, and `finalCommittedCodeExercised:true`. The artifacts retain the executable
hashes and complete per-request evidence. They are intentionally stored under the gitignored
`artifacts/live-network-phase3/` directory rather than committed as product documentation.

## Required fixture matrix

The matrix made 32 MCP requests across 21 discovery pages and observed every required fixture
kind:

| Required fixture | Public kind | Inspected attributes |
|---|---|---:|
| Nested device item | `deviceItem` | 24 |
| Network interface | `networkInterface` | 18 |
| Ethernet node | `node` | 13 |
| Ethernet subnet | `subnet` | 8 |
| PROFINET IO system | `ioSystem` | 6 |
| Communication connection | `communicationConnection` | 18 |

Every required kind therefore exercised discovery followed by targeted inspection through the
public MCP protocol. The connection fixture included a selectable HMI connection. Connection
identity was not complete for every connection in the project; incomplete entries remained
visible and unselectable as required by the contract.

## Repeatability

The same discovery and targeted inspection inputs were executed twice without changing the
project:

| Compared document | First run | Second run | Result |
|---|---:|---:|---|
| Discovery payload (`result`, `omission`, `truncation`) | 161,422 characters | 161,422 characters | Identical SHA-256 `7e2ef41361cc20a27bd8e46299f329b83b3687aa3c82f090365397fd567ad813` |
| Typed discovery result | 160,477 characters | 160,477 characters | Identical SHA-256 `c2761c45a266631b44e283653ea23fa6ceb051b3adbc6b9e94fee396b1ce9696` |
| Six targeted inspections | Same canonical bytes | Same canonical bytes | All identical |

`repeatabilityPassed` is therefore true for the network payload and selectors. The complete MCP
envelopes were 167,450 and 167,407 characters and were intentionally not byte-identical: the
first page of the first walk contained the one-time attachment warning
`Connected to running TIA Portal instance.`, while the second walk did not. The 43-character
envelope difference is exactly the serialized warning. The warning remains public evidence; it is
excluded only from the payload-repeatability decision because it describes process attachment,
not discovery state.

## `list_network_objects` retention decision

The full discovery walk returned 208 objects over 21 pages: 85 selectable objects with complete
selectors and 123 explicitly unselectable objects. All selectable entries carried selectors. The
full canonical MCP result was 167,450 characters and took 67,648 ms in this fixture.

| Approved gate | Measurement | Decision |
|---|---|---|
| 1. Complete selectors when `read_hardware_config` exceeds 60,000 characters | `read_hardware_config` was 139,717 characters, but discovery correctly retained 123 objects without complete selectors. | Not met. |
| 2. Representative targeted query is at least 50% smaller and preserves every matching selector | Device `ET 200SP station_2`: 56,366 versus 167,450 characters, a 66.34% reduction; every matching selector was preserved. | **Met.** |
| 3. Connection-only query returns all connection selectors under budget and avoids one full-tree call | 3 selectable selectors were preserved from 8 observed connections; result was at most 6,692 characters, but measured `fullTreeCallsAvoided` was 0. | Not met. |

Because approved condition 2 passed, `list_network_objects` is retained. The decision does not
depend on treating incomplete identity as complete: the operation's value is the bounded targeted
query while preserving explicit `selectable:false` entries and diagnostics.

## Raw attribute metadata

The internal read-only raw probe observed `name`, `accessMode`, `supportedClrTypeNames`, and
`observedClrValueType` for every required fixture kind. Both read-only and read-write metadata
were observed for all six kinds. Raw metadata entry counts were 24 for the nested device item, 18
for the network interface, 13 for the node, 7 for the subnet, 6 for the IO system, and 14 for the
communication connection.

The public inspection results remained inside the closed `null`, `string`, `boolean`, `integer`,
`number`, and `enum` value vocabulary. Availability and diagnostics remained per attribute; an
unreadable or unrepresentable value did not suppress later attributes. The raw probe is a
worker-only acceptance diagnostic and is not present in MCP tool discovery or the public
`network_read` operation catalog.

## Explicit coverage limits

- No PROFIBUS interface, DP master system, or other PROFIBUS/DP fixture was observed. This run
  neither proves nor disproves those paths.
- Only the HMI communication-connection class satisfied the required selectable connection
  fixture. S7, FDL, ISO, ISO-on-TCP, PTP, TCP, and UDP connection classes were not live-tested.
- Eight communication connections were observed, but only three had complete public selector
  evidence. The other five remain visible as `selectable:false`; the contract does not invent
  missing identity.
- Static tests and the Siemens-stub build cover host contracts and Siemens-free logic. They do not
  replace this live Openness evidence, and this live fixture does not replace commissioning or
  physical-hardware acceptance.
