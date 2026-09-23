# Network Phase 4 PR 1 — current-revision TIA Portal V21 live run

**Verdict: INCOMPLETE.** Public Inventory and Preview completed on the frozen PR 1 candidate. One
authorized Apply completed isolated Ethernet and PROFIBUS create, update, and delete operations.
TIA Portal rejected the connected Ethernet delete for missing safety-program modification
permission; the connected PROFIBUS delete was skipped. Neither connected delete passed. This is a
record of the attempted live gate, not full Phase 4 live acceptance.

## Candidate and fixture

| Item | Observed identity |
| --- | --- |
| Branch and tested commit | `network-phase-4`, `6ce302ea6d096aefd92dfcacceff7711a09d8e71` |
| Tested tree | `8bfe9660196eed274bc43bf0ad60d5d63e27120a` |
| Guarded harness SHA-256 | `862b708bb521787d6aaa2311d6b50c08de2c3a717670359b831ac368184af078` |
| Frozen candidate manifest SHA-256 | `ed5bc16b9f684395724ef5a4986ec542ebfcb855566d708ccc277082ddbe4d18` |
| Host | `TiaMcpServer/bin/Debug/net10.0/TiaMcpServer.dll`; FileVersion `1.0.0.0`, ProductVersion `1.0.0+6ce302ea6d096aefd92dfcacceff7711a09d8e71` |
| Worker | `TiaMcpServer/bin/Debug/net10.0/openness-worker/TiaMcpServer.OpennessWorker.exe` (`net48`); FileVersion `1.0.0.0`, ProductVersion `1.0.0+6ce302ea6d096aefd92dfcacceff7711a09d8e71` |
| TIA installation | Portal process PID 29400 and installed `Siemens.Engineering.Base.dll`; executable and DLL file/product versions `2100.0.121.1` |
| Fixture | The one user-authorized disposable V21 copy, **Fixture A**, basename `296324S_SGRE_V13_V21_1.ap21`; length `151382` bytes; LastWriteTimeUtc `2026-08-07T18:58:09.7858364Z` |
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

## Public MCP observations

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

## Local evidence and boundary

Paths below are sanitized repository-relative locators or the standard per-user audit locator.
Exact project paths and raw protocol records remain ignored locally. SHA-256 values identify the
files inspected for this report.

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
In particular, Portal reports unsaved modifications, and a Siemens exception cannot prove that no
internal side effect preceded rejection. No restoration or discard has been verified. Fixture A
must be discarded or restored and re-inventoried before it can serve as a clean baseline for any
fresh mutating attempt. Such an attempt requires a fresh explicit exact-target decision and a new
Preview/Apply sequence; the failed Apply must not be replayed automatically.

This run does not establish successful connected-subnet deletion, retained-device relationship
clearing, final root-device invariance across connected deletion, save/persistence behavior,
hardware compile, download, online commissioning, physical-device behavior, or plant acceptance.
No full current-revision Phase 4 live PASS is claimed.
