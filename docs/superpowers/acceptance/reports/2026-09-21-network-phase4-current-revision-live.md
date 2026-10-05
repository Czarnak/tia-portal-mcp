# Network Phase 4 PR 1 — current-revision TIA Portal V21 live run

**Verdict: bounded live PASS on 2026-09-24.** After the 2026-09-23 failed attempt, the user
confirmed that the disposable copy was closed without saving and reopened. The user logged into
safety and authorized a fresh guarded public MCP Inventory, Preview, and Apply on that copy. All
eight requested subnet lifecycle operations succeeded, including deletion of the two originally
connected subnets. A separate final Inventory observed zero subnets and the same aggregate hardware
device count. The earlier failure remains documented below as a separate historical attempt.

## 2026-09-24 successful rerun

| Item | Observed identity or result |
| --- | --- |
| Frozen candidate | Branch `network-phase-4`; HEAD `56e2248eacca44ea55c7d246d3c8ceb589b353b9`; tree `76807bb5ea80959a4da2126c63b3c42bca79279a` |
| Candidate manifest | `artifacts/live-network-phase4/candidate.json`, SHA-256 `1e989967a7fd787e17153787ea22a6a8c21254296a7f7fd4ad09bfa4fe1d8550` |
| Guarded public harness | `scripts/live-test-network-phase4-subnets.ps1`, SHA-256 `862b708bb521787d6aaa2311d6b50c08de2c3a717670359b831ac368184af078` |
| Fixture and Portal | Same user-authorized disposable V21 copy, Fixture A; Portal PID `28248`. The exact project path and session identities remain in ignored artifacts. The `.ap21` marker retained its observed length of `151382` bytes and LastWriteTimeUtc `2026-08-07T18:58:09.7858364Z` after Apply. |
| Observation window | Initial Inventory `2026-09-24T07:31:20.971594Z` through separate final Inventory `2026-09-24T07:48:22.4735591Z`. The Apply artifact was generated at `2026-09-24T07:45:18.9950649Z`; these artifact timestamps do not establish exact operation start and end times. |

| Public stage | Observed result |
| --- | --- |
| Inventory | Requested and observed project paths and PID matched. Portal reported `isModified=false`; paged `network_read` counted 81 aggregate hardware devices and connected Ethernet `590-2` (89 named nodes) and PROFIBUS `590-3` (2 named nodes). This aggregate count is distinct from the root project-device count. |
| Preview | Portal still reported `isModified=false`. Public `network_write(confirm=false)` previewed two Ethernet/PROFIBUS creates, two exact connected updates, and both exact connected deletes without mutation. Two negative cases returned `validation_error`; the bogus subnet selector returned `postcondition_failed`. Safety tokens are redacted in the ignored artifact. |
| Apply: create and update | Fresh preview/token cycles preceded each confirmed group. Ethernet and PROFIBUS creates yielded `590-4` and `590-5`; both corresponding updates succeeded. Each group returned two of two successful results and a post-read of 81 aggregate hardware devices. |
| Apply: delete isolated | Exact deletes of `590-4` and `590-5` succeeded two of two. The group post-read contained only the two original connected subnets and 81 aggregate hardware devices. |
| Apply: delete connected | Exact deletes of Ethernet `590-2` and PROFIBUS `590-3` succeeded two of two. The group post-read contained zero subnets and 81 aggregate hardware devices. |
| Final independent Inventory | A separate public read observed zero subnets, 81 aggregate hardware devices, and Portal `isModified=true`. |

The four-entry write audit agrees with the Apply artifact: every group has `success=true`,
`succeeded=2`, and zero failed, skipped, or omitted operations.

Every one of the eight successful operation results had exactly the public lifecycle fields
`subnetId`, `name`, `networkDeviceCount`, and `networkDeviceCountUnchanged`; each reported root
`networkDeviceCount=10` and `networkDeviceCountUnchanged=true`. The Apply artifact records a final
root count of 10 from those lifecycle results, **without an independent pre-Apply root count**.
The before/after aggregate hardware count of 81 is independently observed, but does not establish
root-device identity or per-device invariance. The harness did not read individual retained-device
identities, node attributes, IO-system attributes, or cleared relationships after connected delete.

The harness invoked no project save, compile, or download. The unchanged `.ap21` marker length and
timestamp do not prove every file or in-memory relationship was unchanged; Portal explicitly
reported unsaved modifications at the final read. No persistence, hardware compile, download,
online commissioning, physical-device behavior, or plant acceptance is claimed.

The executable Phase 4 repairs at `6ce302ea6d096aefd92dfcacceff7711a09d8e71` had already
passed 298/298 focused and 3098/3098 full Debug tests, real-reference and stub Release builds,
and 3098/3098 Release coverage tests with line rate 0.9294 against the 0.80 threshold. Today's
docs-only candidate re-pin did **not** repeat that full gate: 298 focused tests passed; a sandbox
full Debug run reported 3094/3098 with four known environment failures; the normal-user Debug
real-reference host build then succeeded. Host retry, Release builds, and coverage were skipped
under the user's instruction to run the live gate. The live PASS is limited to the observed public
MCP path and fixture, not a fresh full Task 7 automated-gate claim on the docs-only HEAD.

The local artifacts are ignored; these hashes identify the files inspected, without publishing
the exact project path or safety tokens:

| Evidence | SHA-256 |
| --- | --- |
| `artifacts/live-network-phase4/candidate.json` | `1e989967a7fd787e17153787ea22a6a8c21254296a7f7fd4ad09bfa4fe1d8550` |
| `artifacts/live-network-phase4/20260924-093120980-inventory.json` | `e7d3e18e6ab72ed9bab3aa80548476197852f60e5b0c1d0c54011b7b9e6b4740` |
| `artifacts/live-network-phase4/20260924-093430781-preview.json` | `38106b693432f9f676ef21fa2064c62e6e592ed767adc1057734b0cef2a50437` |
| `artifacts/live-network-phase4/20260924-094519008-apply.json` | `d85789fe3c143de199867527ac00a928ce565d4a60e4ff45708dc5bbebd31064` |
| `artifacts/live-network-phase4/20260924-094822489-inventory.json` | `9880414377f1bed1809409d2813fb7eb22a7fb8cb8ca7af220942666c6dce86c` |
| `%LOCALAPPDATA%/TiaMcpServer/audit/2026-09-24.jsonl` | `5bb5e70bed50b62272f89f2f5d174aacbe5568d651aef0ce2d2b9ee11b2e4bb1` |

## 2026-09-23 prior attempt — incomplete

The first authorized Apply completed isolated Ethernet and PROFIBUS create, update, and delete
operations, then TIA Portal rejected connected Ethernet delete for missing safety-program
modification permission and skipped connected PROFIBUS delete. The post-failure read reported
`isModified=true`. This attempt did not establish connected deletion. The user subsequently
confirmed that the copy was closed without saving and reopened before the successful rerun above.

### Candidate and fixture

| Item | Observed identity |
| --- | --- |
| Branch and tested commit | `network-phase-4`, `6ce302ea6d096aefd92dfcacceff7711a09d8e71` |
| Tested tree | `8bfe9660196eed274bc43bf0ad60d5d63e27120a` |
| Guarded harness SHA-256 | `862b708bb521787d6aaa2311d6b50c08de2c3a717670359b831ac368184af078` |
| Frozen candidate manifest SHA-256 | `ed5bc16b9f684395724ef5a4986ec542ebfcb855566d708ccc277082ddbe4d18` |
| Host | `TiaMcpServer/bin/Debug/net10.0/TiaMcpServer.dll`; FileVersion `1.0.0.0`, ProductVersion `1.0.0+6ce302ea6d096aefd92dfcacceff7711a09d8e71` |
| Worker | `TiaMcpServer/bin/Debug/net10.0/openness-worker/TiaMcpServer.OpennessWorker.exe` (`net48`); FileVersion `1.0.0.0`, ProductVersion `1.0.0+6ce302ea6d096aefd92dfcacceff7711a09d8e71` |
| TIA installation | Portal process PID 29400 and installed `Siemens.Engineering.Base.dll`; executable and DLL file/product versions `2100.0.121.1` |
| Fixture | The one user-authorized disposable V21 copy, **Fixture A** (basename withheld); length `151382` bytes; LastWriteTimeUtc `2026-08-07T18:58:09.7858364Z` |
| Fixture path SHA-256 | `d2c294bd48e4f9a5b7c267700f05e80692171f2a4d5d2e00865f9eef3004ef3a` — SHA-256 of the UTF-8 bytes of the canonical absolute path, **not a project-content hash** |
| Connected delete selectors | Exact Ethernet `subnetId` `590-2` and PROFIBUS `subnetId` `590-3`, verified in Inventory and explicitly authorized before Apply |

The host DLL and copied worker EXE FileVersion/ProductVersion values were read **after the run**
from the unchanged executable candidate paths; the live artifacts did not capture those binary
version fields. The exact observed project path and session identity remain in ignored Inventory
artifacts. The open `.ap21` was exclusively locked, so its content SHA-256 was unavailable.

The harness source was an **unversioned user-provided saved copy** at the ignored
`priv/live-test-network-phase4-subnets.ps1` locator. Before adaptation its SHA-256 was
`daf16d40ba50ee2fc33ab6c6a71ab28e9be23118dad0aee47e10ae56bf20a97d` and its computed
Git blob ID was `9a1b2cdcb079494e35dc067929ea96b453be3610`. No historical source commit or original
Git blob was established. The restored, adapted harness is
[`scripts/live-test-network-phase4-subnets.ps1`](../../../../scripts/live-test-network-phase4-subnets.ps1)
at the frozen SHA-256 above; its computed Git blob ID is
`d55a1fc990fd9ccf16bef7502a658f5fd964560a`.

The final candidate passed 298/298 focused Debug tests and 3098/3098 full Debug tests. Release
builds with both reference stubs and installed V21 assemblies passed with zero errors and nine
existing warnings each. Fresh Release coverage passed 3098/3098 tests, with line rate 0.9294
against the 0.80 threshold. These are offline and compile gates; the live verdict below is separate.

The recorded final-candidate observation window is **2026-09-23 20:45:09.7665639Z** (Inventory
artifact) through **2026-09-23 21:05:52.6680927Z** (post-failure Inventory artifact). The
artifacts do not provide an exact Apply start/end timestamp. `get_project_status` returned a null
project `Version`; that field cannot be used as the Portal installation version.

### Public MCP observations

| Stage | Result |
| --- | --- |
| Inventory | The requested project path, observed project/session path, and PID matched. Portal `isModified=false`. Paged public `network_read` observed 81 aggregate hardware devices and exactly two connected subnets: Ethernet `590-2` with 89 connected nodes and PROFIBUS `590-3` with 2. This aggregate device count is not the root project-device count. |
| Preview | Public `network_write(confirm=false)` previewed two isolated creates, two exact connected updates, and deletion of the two exact connected selectors. Three safety tokens were recorded only as `[REDACTED]`. No confirmed write occurred in Preview. |
| Negative cases | Two validation cases returned `validation_error`; the late-drift case returned `postcondition_failed`. |
| Apply, isolated groups | Two isolated creates produced IDs `590-4` and `590-5`; their two updates and two deletes succeeded. Every successful lifecycle result contained exactly `subnetId`, `name`, `networkDeviceCount`, and `networkDeviceCountUnchanged`; each reported root `networkDeviceCount=10` and `networkDeviceCountUnchanged=true`. The public pre-read cannot independently establish the baseline root count. |
| Apply, connected group | Ethernet delete `590-2` failed as `worker_operation_failed` when Siemens denied `Subnet.Delete()` for missing permission to modify the safety program at F-CPU or project level. PROFIBUS delete `590-3` was skipped with `earlierOperationFailed`. No connected delete succeeded. The harness stopped; it wrote no successful Apply artifact and did not retry. |
| Post-failure read | A fresh read-only Inventory observed Portal `isModified=true`, 81 aggregate hardware devices, both original connected subnets with their original observed connected-node counts, and no isolated test subnets. |

The four-entry write audit contains the three isolated success groups and the failed/skipped
connected group. Successful result shapes and counts were checked from that audit. The audit does
not turn the failed connected operation into a pass. The prior public-path Phase 4 run remains
evidence only for its older recorded commit.

### Local evidence and boundary

Paths below are sanitized repository-relative locators or the standard per-user audit locator.
Exact project paths and raw protocol records remain ignored locally. SHA-256 values identify the
files inspected for this prior attempt. The `candidate.json` locator was later updated for the
2026-09-24 rerun; its old hash below records the prior content, not the current file.

| Evidence | SHA-256 |
| --- | --- |
| `artifacts/live-network-phase4/candidate.json` | `ed5bc16b9f684395724ef5a4986ec542ebfcb855566d708ccc277082ddbe4d18` |
| `artifacts/live-network-phase4/20260923-224509780-inventory.json` | `85422611c2480db56dd2c66e60354e21132b570e348b0787895b3b8c7f92608b` |
| `artifacts/live-network-phase4/20260923-224912689-preview.json` | `27951529718da1a5812770869857204bfce5ad4daae55b8d8d2a6b82f3ddc87b` |
| `artifacts/live-network-phase4/20260923-230552678-inventory.json` | `a6345cbffe1bb4b2b578a9fa14e9449b92658e3fe25df4d432f794681c4f6ab0` |
| `%LOCALAPPDATA%/TiaMcpServer/audit/2026-09-23.jsonl` | `d0c53aea8566fc29a947f4ad7c417deae8d2a5a9da1104ba41e3ef6138f5f493` |

The harness invoked no project save, compile, or download and requested no device lifecycle
operation. The `.ap21` file length and last-write timestamp observed after the failed Apply matched
the pre-run values. This does not prove all project files or in-memory relationships are unchanged.
In particular, Portal reported unsaved modifications, and a Siemens exception cannot prove that no
internal side effect preceded rejection. At the time of this failed attempt, no restoration or
discard had been verified. The user later confirmed the copy was closed without saving and reopened
before the 2026-09-24 rerun. That rerun used a new Preview/Apply sequence; the failed Apply was not
replayed.

This 2026-09-23 attempt did not establish successful connected-subnet deletion or its
postconditions. The later 2026-09-24 rerun establishes the bounded live PASS described above;
neither run establishes save/persistence behavior, hardware compile, download, online
commissioning, physical-device behavior, or plant acceptance.
