# VCI tools live acceptance — 2026-10-16

**Candidate:** `4f745081c07708f743ad72fbb914b1e539c62454`, branch `feat/vci-tools`, installed package `4.0.0-local.102.g4f74508`.
**Verdict:** not accepted. Live testing found a reproducible Unified HMI mapping-path defect. The read-write functional and controlled-crash checks below are finished; a completed whole-PLC export and thrown-leaf import rollback remain unqualified. The candidate was kept unchanged throughout testing.

This report follows [design §8](../../specs/2026-10-15-vci-tools-design.md#8-live-acceptance) and [implementation Task 13](../../plans/2026-10-15-vci-tools.md#task-13-offline-gate-and-live-acceptance). Fixture roles are anonymous; raw selectors, project paths, source exports, request/response documents and audit records remain private.

## Provenance and scope

The maintainer installed and restarted the candidate, opened a disposable standalone project, requested specification testing, and authorized the project actions required for verification. Testing used TIA Portal V21 `2100.0.121.1`. All engineering calls were serialized. The installed host and worker were checked before auxiliary-client launches:

| Binary | SHA-256 |
| --- | --- |
| Host | `e7cdc60714ad0743869ee1afae23877714297a3961af22226f3d3f911101a3ce` |
| Worker | `917bcf61de6770071b565418f725b46b233400e4954d7c60617d2e281c225b25` |

The primary installed connector ran in full mode. Separate short-lived clients exercised read-only and read-write modes and controlled worker timeout without changing persistent configuration. They communicated with the installed C# server over MCP; native Openness stayed in the .NET Framework worker. A separate native helper changed only the author field of a temporary fixture block, to preserve its identity while producing a project-side change. Its access dialog was approved by the maintainer. A helper-only assembly-resolution failure was diagnosed and corrected before that mutation; it is not VCI server evidence.

The existing offline gate at this candidate passed both reference-stub and real-reference builds and all **6,848 tests**. Changed executable coverage was **1,242/1,274 (97.49%)**, with a conservative denominator including uninstrumented changed lines still **82.31%**. These results qualify source/build behavior, not live acceptance.

Before mutation, a recovery archive was created and its integrity checked. The counts and roots of four pre-existing workspaces were captured privately for preservation checks. Only acceptance workspaces and a temporary mixed PLC group were created. No PLC download or online control was performed.

## Observed results

| Check | Evidence and result |
| --- | --- |
| Mixed PLC scope, all six writes | Temporary LAD FC, SCL FC and global DB compiled. Export requesting SCL succeeded for 3 leaves with 2 fallbacks. Re-export skipped all 3 as already mapped. Create, both synchronization directions, mapping deletion and workspace deletion succeeded. All six operations and a preview were also exercised in read-write mode without elicitation. |
| Read surfaces and formats | Workspace/mapping inventories and supported-format reads succeeded. LAD supported SimaticML/SimaticSD, SCL supported SCL/SimaticML/SimaticSD, and global DB supported DB/SimaticML/SimaticSD. |
| PLC mirrored layout | `<device>/<software>/Program blocks/<group>/<leaf>` was observed. The deterministic PLC tool alias `Blocks` is not the VCI category name. |
| One-sided changes | In-place author edit produced `ProjectObjectChanged`: import skipped `targetChanged:1`, export synchronized 1. A workspace source comment produced `WorkspaceFileChanged`: export skipped `targetChanged:1`, import synchronized 1. Both scenarios passed in full and read-write modes. Imported SCL compiled successfully and source readback contained the marker in both runs. |
| Conflict | Native status reported both change flags. Both directions returned `skipped.conflict:1`, `succeeded:0`, `projectModified:false` in full and read-write modes. |
| Leftover collision and ordered stopping | Deleting mappings retained files. In both writable modes, an actual create/export/delete batch created its workspace, blocked export on 3 leftover file keys, and skipped the later delete. The earlier creation remained applied and was cleaned up separately. |
| Persistence | Save, close and reopen succeeded in both writable modes. Acceptance mappings (3 PLC, 1 HMI) persisted, as did the original workspace counts. The read-write client received and accepted exactly 3 lifecycle forms with boolean `confirm:true` under standing project authorization. This proves the protocol exchange, not human prompt display. |
| Unified HMI content | Only SimaticSD was supported. Export produced `.hmi.js` and `.hmi.yml` under `<device>/<software>/Scripts/`. In both writable modes, a reversible script comment imported successfully and appeared in a fresh export. Restoring the original script, importing and freshly exporting again produced an exact byte match, with private hashes retained. |
| Unified HMI mapping read | **Failed in both writable modes:** unscoped inventory returned a path omitting the HMI software segment. The valid module scope returned zero mappings despite a live mapping; the emitted path was rejected as an invalid selector. See defect D1 below. |
| Read-only mode | Exactly 8 tools, including `vci_read` and excluding `vci_write`. Binding and VCI inventory succeeded. Calling the hidden write tool returned JSON-RPC unknown-tool rejection. |
| Whole-PLC planning | Default-timeout client completed a dry run in **14.068 seconds**: **614 leaves**, **10 format fallbacks**. Planning reported no guards. |
| Whole-PLC default timeout | Startup confirmed 5 minutes. The call returned `worker_timeout` after **312.935 seconds** overall, including planning, with a matching last-attempted leaf/format/operation breadcrumb. Verification made no reads and reported `notVerified`. The next read was refused with `binding_conflict`. Independent state inspection found **180 retained mappings** and no mapping for the last-attempted leaf; no replay was attempted. |
| Whole-PLC raised timeout | Startup confirmed 15 minutes. A fresh-workspace export also returned `worker_timeout`, after **913.408 seconds** overall including planning, with matching breadcrumb and `notVerified`. Its already-queued read was refused with `binding_conflict`. After the maintainer saved and reopened the fixture, **322 mappings** remained. This is a measured raised-limit timeout, not a completed whole-PLC export; neither partial export was replayed. |
| Controlled Portal-loss path | With explicit maintainer approval at that moment, one SimaticSD export of the identified SCL OB returned typed `portal_lost` after **11.053 seconds**, `failed:1`, and the exact `lastAttempted`. Root `error:null` and MCP `isError:false` retained the attempted-failure contract. Verification was `notVerified`; the next read was refused with `binding_conflict`. A fresh Siemens crash archive reports `Reason="Openness"`, `InvalidOperationException`, and the missing `IExportProvider` service in `ExportCommandLogic.PerformExport`, matching the spike. No retry was made. Recovery and cleanup succeeded as described below. |
| Thrown-leaf transaction rollback | No reliable throwing import case has been established. Missing files are rejected before transactional import, and malformed SCL is known to import silently. No rollback claim is made from static tests or the earlier spike. |

## D1 — HMI mapping paths omit the software owner

The same Unified script module was exported and synchronized successfully using its browse selector, `Device → HmiSoftware → ScriptModule`. Unscoped `list_mappings` returned one mapping with path `Device → ScriptModule`. A query scoped to the valid browse selector returned `total:0`. Reusing the emitted mapping path in `supported_formats` was rejected as `invalid_selector` before worker dispatch.

The source explains the discrepancy: `VciReader.MappingPath` delegates to `VciScopeResolver.PathOf` over the native parent chain, while `ListMappings` filters with a segment-prefix comparison. Inference from that code and the returned path: the traversal misses the software segment that the public selector grammar requires. The native parent chain was not separately instrumented. This is a read-contract defect; successful script export/import does not qualify scoped mapping reads. The candidate was kept unchanged during testing. A fix needs a selector-valid owner path and a regression covering both returned path and scoped inventory, followed by a rebuilt-candidate live recheck.

## Contract, audit and restoration

Canonical text/structured documents with retained raw text were compared after JSON parsing; text escaping differences were not treated as document mismatches. Final reconciliation matched **51 saved `vci_write` response documents to 51 unique audit records**, all with valid response hashes. Actual full-mode writes used `policy` (22); full previews used `none` (5); read-write actual calls used `none` (21), as did read-write previews (3). No equality failures or unmatched records were found. A private audit snapshot preserves this reconciliation.

An initial PLC source-replacement probe recreated the temporary SCL object and Siemens removed its mapping. That attempt is excluded from one-sided-change evidence. The fixture was remapped, then the identity-preserving author edit established the status results reported above.

The initial full-mode HMI, one-sided-change/conflict, collision and persistence evidence was subsequently repeated in read-write mode. Both whole-PLC jobs and the controlled crash also used read-write. The original supported-format inventory was collected through the full-mode connector; no mode-specific native-format difference is claimed.

Before the crash, all four acceptance workspaces and the temporary mixed PLC group were deleted, the group selector returned `target_not_found`, and the restored project was saved. The original workspace names, roots and mapping counts matched the baseline exactly. A separate empty fault workspace was then created and saved; its SimaticML probe returned typed `failed:1` without a project mutation, while the SimaticSD dry run covered exactly one leaf.

After the approved crash, the TIA GUI was restarted locally because this server attaches only to a running Portal. A fresh read-write MCP session reopened the exact saved fixture, found the saved fault workspace with zero mappings, deleted it and saved. Final inventory again matched all four original workspace names, roots and counts. Final project status was **open and unmodified**. The primary connector was explicitly rebound and its VCI read succeeded. Exported test files and recovery material remain private on disk; no acceptance workspace or temporary PLC group remains in the project.

## Remaining observations and limits

**O1 — old-session recovery:** the primary session first rejected its stale pre-crash identity, then reported no running Portal. After the GUI restart, `open_project(forceRebind:true)` in that same session still failed with `binding_conflict` because the returned identity lacked a project path; inventory confirmed that no project had opened. A fresh MCP session opened the saved fixture successfully, after which the primary connector could explicitly bind it. Recovery therefore required a fresh session in this run. No root-cause or remediation claim is made from this observation.

**O2 — whole-scope performance:** neither the 5-minute nor the 15-minute export completed. Source review found two scans of the growing `MappedObjects` collection per new export leaf, in addition to lookup work; that can produce quadratic native enumeration. It is a plausible performance contributor, not a profiled explanation of the observed duration or variance from the spike. Completion at a higher timeout and a controlled performance comparison were not attempted.

Full acceptance is blocked by D1; O1 and O2 remain follow-ups. HMI runtime execution/compilation, local sessions, STL/GRAPH, unknown native status, and thrown-leaf transaction rollback are not qualified by this report. Current user documentation remains deferred until the live findings are resolved or explicitly accepted by the maintainer. No application-code fix, candidate reinstall, push or pull request was performed during this live run.
