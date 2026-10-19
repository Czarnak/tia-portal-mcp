# Network B-4 scoped live verification

Date: 2026-10-19. Verdict: **PASS for the executed read-only regression scope**. Native unreadable-candidate and enumeration-failure paths were not reproduced live; their evidence remains offline.

## Candidate and authorization

- Implementation: `140dd5fd4399007d9defa2dd4c8aa87f85af5d6a` on `fix/network-selector-incomplete-evidence`.
- Installed host and running worker both report `4.1.1-local.3.g140dd5f`. Installed binary paths, SHA-256 hashes and process identities are retained privately. The same worker remained present after the checks.
- The maintainer installed the local version, opened a disposable fixture and explicitly authorized proceeding with live testing. Inspection found one open Portal; binding selected its exact observed PID and AP21 path.
- Fixture A is a standalone project. It was open and unmodified at binding. Actual project names, paths and selectors are confined to ignored local evidence under `TestResults/network-b4-live-20261019/`.

## Executed checks

| Check | Observed result |
| --- | --- |
| Subnet and IO-system discovery | Three subnets and three IO systems, covering two PROFINET systems and one PROFIBUS system. All six objects were selectable, with no selector diagnostics and no continuation. |
| Exact selector round-trip | All six selectors copied from discovery succeeded in `inspect_network_object`; requested `Name` attributes were available and inspection messages were empty. |
| Optional IO-system constraints | All nine inspections succeeded: number-only, number plus name, and number plus index for each of the three systems. Each resolved to the expected discovered identity. |
| Subnet isolation | Two systems sharing the same number and name on different subnets resolved independently through their qualified selectors. This does not exercise duplicate identities within one subnet. |
| Missing number, index or name; name case | Twelve deliberately nonmatching selectors returned per-item `target_not_found`, including exact case-sensitive name matching on all three systems. |
| Missing subnet | A nonexistent subnet ID returned `target_not_found` both for a subnet selector and for an IO-system selector. |
| Failure isolation | A valid IO-system inspection after the fourteen negative cases succeeded in the same batch. Counts were fourteen failed, one succeeded, zero skipped and zero omitted; root `success:false`, `error:null` and MCP `isError:false` retained the typed batch contract. |
| Hardware readback | A bounded first hardware page identified a device filter; a subsequent filtered hardware read returned all three subnets and their three IO systems. Their subnet IDs, IO-system numbers and names matched discovery. The hardware result also reported one blank device-item-name diagnostic, outside the subnet/IO-system identity checks. |
| Response consistency | All nine retained response documents had JSON-equivalent canonical text and structured content. No operation result was omitted. |
| Final preservation | Repeated discovery was identical to the initial six-object inventory. Project status still reported the same path, open state, `isModified:false` and unchanged last-modified time. |

In total, thirty selector inspections produced sixteen successful selections and fourteen expected missing-target failures. Four additional Network read operations covered initial/final discovery and the two hardware reads. No project write, lifecycle mutation, compile or online PLC operation was invoked, so no restoration was required.

Private response documents, exact inspection requests, provenance and a verification manifest remain in the ignored evidence directory. Public records use Fixture A rather than its real identity.

## Evidence limits

- No native subnet identity, IO-system number or constrained IO-system name became unreadable. Consequently, live `target_evidence_mismatch` on incomplete candidates is **not** established by this run.
- No recoverable collection-enumeration failure, same-namespace duplicate ambiguity, or Portal-loss condition was induced. The offline regression tests cover these selection/exception cases; this live fixture does not substitute for them.
- This run exercised the installed connector configuration and one standalone root. It does not establish additional access-mode or local-session coverage.
- B-16 and Q3 remain separate work. This report does not qualify their outer exception handling or worker-wide `portal_lost` behavior.

The [B-4 implementation record](../../plans/2026-10-17-codebase-simplification-review.md) retains the offline gate: 6,977 passing tests, 23 focused regression cases, 33/33 added executable resolver/helper lines covered, and successful stub and installed-V21-reference builds. Those suites were not repeated for this documentation-only follow-up.
