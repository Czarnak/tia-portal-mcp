# E-4 typed network connections scoped live acceptance

Date: 2026-10-25. Verdict: **scoped PASS for the executed standalone, `full`-mode cases on the installed candidate**.
Native fault injection, other access modes and other container kinds are not qualified by this run.

## Candidate and authorization

- Installed candidate: `b5e29e4558a36653950734d95b6a7c3263fd9c46` (E-4 with workstream A integrated, before E-5), package `4.2.0-local.24.gb5e29e4`. Both installed binaries matched the E-4 worktree's Release build outputs byte for byte: host SHA256 `8211DEEBE3D085D280FC44C58CD48BD077F8485098689F294314AF847EAC8333`, worker SHA256 `774287DD60CDF7750E6689AD4BFEB12E7CA2C160DB12ABF4808C3405AD11BF44`.
- The installed Siemens Base assembly kept version `21.0.0.0`, file version `2100.0.121.1` and the previously reviewed SHA256 `8C12B0FA70C298F1CD1221105880752AB204ED63C9F6D9DCB68833D977BCB5D0`. No rebuild or production-source edit was needed during acceptance.
- The maintainer stated that the local E-4 tool was installed, a disposable project was open and live testing could proceed. The sole open project was bound by exact observed path and Portal PID and reported `isModified:false`. Public records call it **Fixture A**; paths, selectors, raw requests, responses and receipts remain in ignored local evidence.
- The original IO endpoint had one selectable node, complete attachment evidence and no configured communication connections. Because an already-attached node cannot be moved by this public connect operation, a temporary IO station of the same observed supported type, **Probe A**, was created on existing networks and controllers. Its fresh baseline proved both subnet and IO attachment absent.

## Executed checks

| Case | Observed result |
| --- | --- |
| Invalid IO-system number with a requested scalar change | Host preflight returned `target_not_found`; no mutation was entered. A fresh scalar read retained the original values. This is dependency-denial evidence, not native exception evidence. |
| Attach the original already-connected node to another subnet, with dependent IO attachment requested | Native subnet connection was rejected. The typed result retained `skippedSettings.Subnet`; `skippedSettings.IoSystem` stated that IO attachment was suppressed. All final preservation checks passed and an independent hardware read exactly matched the original endpoint and subnet inventory. |
| Attach initially unattached Probe A to a compatible subnet and IO system | Root and item success were true; `appliedSettings` contained exactly `Subnet` and `IoSystem`, with no skipped settings. Immediate and final checks for `Subnet`, `IoSystemSubnet` and `IoSystemNumber` passed. An independent hardware read confirmed both relationships and controller membership. TIA also adapted the probe's IP address to the destination network. |
| Reapply Probe A's observed address, then request another IO assignment while its connector was already attached | The native IO call was rejected. `appliedSettings.Address` retained the successful same-value setter; `skippedSettings.IoSystem` retained the native diagnostic. This does not claim a changed address was rolled back. Applied-setting verification and preservation of the prior IO attachment passed; an independent hardware read exactly matched the preceding connected-probe snapshot. |

Both native rejections returned typed attempted-operation failures (`success:false`, `error:null`,
`phase:"applied"`) with successful preservation verification. These expected failures passed their
acceptance checks; they are not reported as successful write operations. No native fault was
manufactured and no failed call was automatically replayed. Immediate verification and
`skippedSettings` named here are the contract at that candidate; the current Network contract is in
the [Network reference](../../../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md).

## Restoration and receipt verification

The planned restoration route was discard/reopen of the initially unmodified disposable copy, since
the public configure operation has no unassign action. The baseline and the route were captured
before mutation, but explicit approval for the destructive close was obtained only after the live
cases, when automatic approval review blocked the first `close_project(saveBeforeClose:false)`
request. The plan's original requirement to obtain that approval in advance therefore remained
unmet. The close ran only after the maintainer explicitly approved closing without saving,
reopening the same exact file and verifying restoration. Both succeeded; closed status was
independently observed before opening.

After reopening, Fixture A was open and unmodified at the same path with its original saved
modification timestamp. A fresh hardware read of the original endpoint and all four subnet/IO
inventories exactly matched the captured baseline. Node inventory returned to 108 and an exact
lookup of Probe A returned the expected `target_not_found`. This proves the scoped in-memory
restoration; no byte-identical project-file claim is made because TIA held the descriptor
exclusively during the initial hash attempt.

All eight entered guarded calls (one preview, one dependency denial, three attempted configure
calls, one temporary-device creation, and close/open restoration) matched exactly one audit v2
record each. Delivered response SHA256 values recomputed correctly, all eight text/structured
document pairs matched, and recorded item statuses and confirmation origins matched the receipts.
The approval-review rejection did not enter the server pipeline.

## Evidence limits

- The successful path and natural engineering rejections were exercised once on one IO-station type.
- Native `InvalidOperationException`, fatal loss, timeout, ambiguous live identities, other equipment or firmware, read-write and read-only modes and local sessions were not tested.
- No project save, hardware or PLC compile, download, online connection or PLC runtime action ran. This is not commissioning or plant acceptance.
- Offline qualification is separate evidence. The ordinary suite passed 7,678/7,678, scoped line coverage was 12,973/13,642 (95.09%) and the packaging suite passed 39/39; the coverage scope excludes worker/native code, and metadata tests establish API compatibility rather than successful Siemens mutations.
- E-4 was subsequently merged through [PR #193](https://github.com/Czarnak/tia-portal-mcp/pull/193); this record remains limited to its frozen candidate and executed cases.
