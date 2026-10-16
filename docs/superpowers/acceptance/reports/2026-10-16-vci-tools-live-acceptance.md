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

## Follow-up implementation and offline gate — 2026-10-16

**Code candidate:** `d10f1e3dcf384e587874c40a6a677b24d0dce557`, still on `feat/vci-tools`. The original frozen-candidate verdict above remains unchanged. The maintainer subsequently authorized live rechecks after implementation; those rechecks are pending at this offline checkpoint. The installed tool has not been updated.

| Finding | Follow-up change and evidence |
| --- | --- |
| D1 | `fb6a812` resolves Unified script mappings through project/device/software ownership, compares native identities and resolves emitted selectors back to the same object. Reader/resolver regressions passed 67/67, including skipped software parent, scoped round trip, equal wrappers and ambiguous/unreadable ownership. |
| O1 | `d10f1e3` permits forced invalidated-source recovery only after successful coherent Portal-only status and the existing fresh zero-owner proof. Normal complete-project promotion remains strict. Atomic compare-and-clear preserves the exact source revision. New-PID preview/actual recovery reproduced the old failure before the fix; malformed evidence, owners, identity drift and in-flight same-path binding changes are covered offline. Native disposal/restart timing remains to be checked. |
| O2 | `327d1cd` adds count characterization and late-collision regressions; production export is unchanged. For two new leaves and 20 existing mappings, four explicit fresh key scans visit 82 mappings and read each key property 82 times. Two native-API-shaped `Find` calls are counted separately; the double's linear traversal does not establish native `Find` complexity. Removing the final check made both late mapping/file collision regressions fail. No safe scan reduction or live speedup was demonstrated. |

The explicit writer key checks scale with leaves × mappings; they remain before and after directory creation. An existing-directory shortcut was also assessed and left unimplemented because a concurrent directory-to-file change alters its pre-export refusal barrier; native behavior in that race was not qualified. Filesystem call counts are source control-flow estimates, not measured OS-call or latency evidence.

The final source passed **6,894/6,894 tests**, zero failures/skips, with serialized test collections. A focused corrected coverage run passed **778/778**. Against the follow-up production diff, coverage was **68/72 changed executable lines (94.44%)**, or **68/79 (86.08%)** when counting unrepresented substantive changed lines conservatively. Against the branch merge-base it was **1,457/1,494 (97.52%)**, conservatively **1,457/1,760 (82.78%)**. The conservative denominator includes declarations and continuations without sequence points; these are line metrics, not branch coverage. The initial collector omitted linked host/worker source; it was corrected with test-assembly inclusion before deriving these percentages.

Clean serialized solution rebuilds against both reference stubs and installed V21 assemblies passed with zero errors and five existing nullable warnings in unchanged HMI tests. The copied real-reference worker hash matches its build output, and no Siemens DLL was copied into the worker directory. Independent task reviews approved all three tasks; broad final review remains the next gate.

| Follow-up binary | SHA-256 |
| --- | --- |
| Built host | `cc2af97ce8b535e5ca0c1cf2015e253c4e34240a6d609bcb2a10fbc6275b54b1` |
| Built worker | `d27cdcbc4e2c70abfcd50a70412a0d69938b74a4804ba9d53a0b9a8d22f1745e` |

The HMI fix was committed after its focused gate while a broader diagnostic run was unresolved; subsequent serialized full runs closed that gap. The temporary process ruling risked a broader regression requiring rework, not an acceptance claim. No live follow-up, reinstall, push or pull request had occurred at this checkpoint. Current VCI user-documentation migration remains deferred.

## Completed follow-up checks — 2026-10-16

**Tested source:** `a26b45fea8a2ee47623a90b72e21b4edaf8f0fb9` on `feat/vci-tools`. **Verdict: the affected follow-up checks passed, within the limits below.** The original frozen candidate's failed verdict and partial bulk-export evidence remain unchanged.

Broad final review found two additional synchronization defects. Separate fixes `ac919aa` and `a26b45f` now re-resolve the original selected leaf and check mapping ownership with `object.Equals` in both directions, and read plain workspace-sync status once immediately before each leaf's synchronization. Deterministic regressions first reproduced both failures. The one scoped final re-review approved both fixes, with no remaining Critical/Important finding in that delta. Import retains its specified status prefilter outside ExclusiveAccess and its single transaction.

The combined source passed **6,906/6,906 tests**, zero failures/skips, in **8m31s**. Corrected linked-source coverage passed **790/790**. Follow-up coverage is **90/94 changed executable lines (95.74%)**, conservatively **90/104 (86.54%)**; feature-branch coverage is **1,469/1,503 (97.74%)**, conservatively **1,469/1,770 (82.99%)**. The conservative measure includes substantive added lines without sequence points, including declarations and continuations. These are line measures rather than branch coverage.

Clean serialized stub and installed-V21-reference rebuilds passed with zero errors and five unchanged HMI nullable warnings. The real worker build and host copy have identical hashes, with no Siemens DLL copies. No application source changed during these live checks.

| Tested binary | SHA-256 |
| --- | --- |
| Host | `b118b9d4c131ec9972e93c6ecf6ae4804eaf45f21754094f74d251a1da17738a` |
| Worker | `3c05d48406ace3618661262b1ae7fd17e1a51f94c5f4ca72108132aee6b37cfe` |

### Native observations

The rebuilt candidate ran through private stdio clients, using the net48 worker, against the sole currently open disposable standalone fixture. Writes used `read-write`; the HMI read comparison also used `full`. Both writable discovery lists contained 19 tools. The installed connector's binaries and configuration were unchanged. A human approved the initial Openness access dialog.

| Check | Result |
| --- | --- |
| Recoverable baseline | The fixture was initially open/unmodified, with workspace counts **676, 3, 0, 1**. The first archive destination was rejected by native TIA because its 153-character path exceeded the 143-character limit. Inspection found no archive files and an unmodified project; a separate shorter-path archive succeeded. Its **67,656,195 bytes**, SHA-256 `a2b507e7cd10860537f8fba9062be8f33f9fd726832eab7f05114b7615941869`, and all **1,550 ZIP entries** passed integrity checking. |
| D1 HMI mapping | One existing Unified script exported successfully with one format fallback. Scoped and unscoped inventories each returned exactly one mapping with **Device → HmiSoftware → ScriptModule**, identical to the requested selector, and `Equal` status. `supported_formats` accepted the emitted selector and returned `SimaticSD`. The same checks passed in both writable modes. The two status-bearing inventory reads took **34.051s** in read-write and **33.802s** in full. Existing HMI source content was untouched. |
| O2 bounded export | A three-leaf group preview completed in **1.119s**. Actual export completed **3/3** in **11.108s**, with three SCL-to-SimaticML fallbacks. A deliberate healthy repeat skipped all three as `alreadyMapped` in **1.583s**, with no content change. These are complete MCP-call timings, including planning/verification, rather than isolated native API timings. |
| Sync fixture | A fresh temporary PLC tag table contained one unaddressed `Int` constant. Its supported format was `SimaticML`; export succeeded **1/1** in **4.584s**. Equal mappings skipped one leaf in each direction. |
| Project-only change | Updating the temporary constant produced `Unequal` / `ProjectObjectChanged`. Import skipped `targetChanged:1`; workspace sync succeeded once. Exported XML readback contained the updated value. |
| Workspace-only change | Editing only the temporary exported XML produced `Unequal` / `WorkspaceFileChanged`. Workspace sync skipped `targetChanged:1`; import succeeded once in a combined **8.618s** call. Project readback contained the imported value and the mapping remained selectable with fresh `Equal` status. The selected PLC compiled successfully with **zero errors and warnings** in **28.028s**. |
| Conflict preservation | Different temporary values on both sides produced both native change flags. Each direction skipped `conflict:1`, with zero synchronized leaves. Fresh project readback and the unchanged workspace-file hash proved that both targets were preserved. |
| Cleanup before recovery | All acceptance mappings/workspaces and the temporary table were removed. Fresh reads matched the original workspace names, roots and counts exactly and reported `target_not_found` for the temporary table. Explicit save succeeded; status was open/unmodified. Exported files and the archive were retained privately. |
| O1 same-host recovery | The exact observed Portal closed gracefully while the same MCP host stayed running. Its next status read was refused with `binding_conflict`; non-adopting inventory after restart showed one new PID with no project path and retained `invalidated` binding. One actual `open_project` of the exact saved fixture with `forceRebind:true` then succeeded in that original host in **22.460s**, including successful lifecycle verification. No recovery call was replayed. |
| Final restoration | The reopened fixture was open/unmodified, the binding was verified against the new Portal, the original workspace names/roots/counts matched, and the temporary table remained absent. The installed connector was explicitly rebound and independently reported open/unmodified after both auxiliary clients shut down. |

All **58 captured tool responses** (56 candidate calls and two installed-connector restoration calls) had identical text and structured documents. The **12 VCI write calls** each matched exactly one audit-v2 record and its delivered response text/hash; the same held for all **20 audited write calls** across VCI, PLC and lifecycle. VCI confirmation was `none` / `not_requested`. Four lifecycle form requests each received client `accept` plus boolean `confirm:true` under the maintainer's standing authorization; this is protocol evidence, not proof of a displayed human prompt.

### Retained limits

- Production export retains its safety scans. The bounded timings establish neither a speedup nor completed whole-project export. Native `Find` complexity remains unknown; the original bulk timeouts remain relevant evidence limits.
- Recovery was checked after a graceful saved-fixture restart. The known native SimaticSD SCL-OB crash was not repeated or fixed, and abrupt-crash timing on this candidate remains unexecuted.
- Remapped-key and inter-leaf status races were reproduced deterministically offline. Live concurrent races were not forced; sequential identity/status/native calls are not atomic against external edits. Import's prescribed prefilter window remains.
- Native thrown-leaf transaction rollback, Unknown status, current-candidate HMI import/runtime behavior, other unexecuted formats and local-session contexts retain their earlier limitations. The successful PLC compile qualifies the temporary import scenario only.
- The broad tag-table read correctly omitted a 63,605-character value under the 60,000-character budget; narrowed reads supplied the fixture evidence. A partial or omitted response was never counted as a complete inventory.

The original user-documentation task is complete in `b81d5c6`, with connected current-guide count corrections in `477ef7d`; independent documentation review approved spec compliance and quality. The [VCI operation reference](../../../SupportedOperations/VCI_OPERATIONS_SUMMARY.md) records current behavior and limits. Application source and tested binary hashes remain unchanged.

Two process rulings were made: the HMI fix could be committed after its focused regression while the full gate remained unresolved, risking a broader regression and later rework; subsequent serialized full runs closed that gap. Reference documentation followed the affected live gate while retaining the full-matrix limits, risking completion before broader maintainer acceptance and a later documentation revision. Neither ruling declares full live acceptance. No installation, push, pull request or merge occurred.
