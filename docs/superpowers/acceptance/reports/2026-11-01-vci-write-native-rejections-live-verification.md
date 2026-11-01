# VCI write native rejections — live verification, 2026-11-01

**Verdict:** scoped PASS for the 20 final variants represented by C1–C10 and L1–L6 below. All 21 measured timing samples qualify under the unchanged Stage 0 protocol. Performance is mixed: creation and export improved, while the two synchronization workloads were slower. The saved fixture was restored within the observations described below.

## Provenance and scope

The candidate is commit `4057b44a062d6199ecf6861e174d730b1d948c0e`, package `4.2.0-local.160.g4057b44a`. The package's repository commit matches that source. The installed host and worker match the package payloads, and the short temporary copy used for capture matches both installed files. No Siemens Engineering DLL is present in the package. This continuation used the installed package; it did not rebuild the product or repeat Task 7's offline qualification or packaging tests.

Package SHA-256: `97fd78de76ff5dde57f18191b20ee2ffaa8d3214b305fd93ea8eaedb1e3ce85a`. Host SHA-256: `2584feb74424a9744938d9c103008f87398d6cb4bc0ecf84e8d8c2f786ee80d1`. Worker SHA-256: `a769c4e9f4542223cbc522b6cf73d61e773f8eeb92a998f3a27f9607483aa9e8`.

The machine ran TIA Portal V21 executable build `2100.0.121.1`; its visible menus were English. Each capture client launched the candidate in `full` mode with a 15-minute worker timeout and 1,000,000-character item/document budgets, all supplied through the CLI. Client request timeout was 7,200 seconds. Reads returned contract `1.0`; writes returned `2.0`. Capture used a disposable standalone project and the saved protected-FC fixture revision. Project identities and PLC source contents are not published here.

Before timing, the saved project hash matched Stage 0, the project was open and unmodified, `base` had 216 mappings, and independent previews found the same 3-, 10- and 58-leaf scopes. The tag-table scope started at 23; the unchanged T5 preparation added ten temporary tables to produce 33 leaves with ten mapped. T6 preparation reduced the base inventory to 209 mappings and changed three mapped DB files. Every measured mutation was followed by the original discard/reopen and file restoration sequence.

Stage 0 used baseline commit `8713a476ec509360b7cc18083e76d21275fc1c92` and temporary probe commit `7275a3fe745d3b8426947dabe8ef3d3ef8f0b3b7`. Its retained qualification established 21 baseline timing samples, B1–B8 and supplements, 16 primary probe variants, a protected-FC supplement and seven native path-length variants. It established the duplicate-name native refusal, allowed nested roots, know-how rejection and the observed complete XML path boundary. Its native overwrite observations motivated the accepted file-key policy; they were not a guarantee that arbitrary collisions are harmless.

## Timing comparison

The 13 original timing macro files were replayed unchanged: one excluded startup call, one warm-up per workload and three measured calls per workload. The original `summarize.py` and `validate_timing_protocol.py` required no version or verification-literal changes. Both baseline and candidate medians were independently recomputed from the three samples. Each root, item-sum and remainder median is calculated independently; remainder is calculated per sample before taking its median, rather than subtracting the two displayed medians.

Values are seconds, baseline → candidate. Root durations come from audit v2, excluding the independent before/after reads and restoration work.

| Case and workload | Root median | Item-sum median | Remainder median | Root change |
| --- | --- | --- | --- | --- |
| T1 — create workspace | 3.313 → 0.528 | 0.497 → 0.472 | 2.816 → 0.056 | −84.1% |
| T2 — export three leaves | 12.726 → 10.103 | 7.024 → 6.386 | 5.596 → 3.822 | −20.6% |
| T3 — create, export ten, delete three mappings, export again | 15.797 → 11.151 | 14.950 → 11.108 | 0.847 → 0.043 | −29.4% |
| T4 — preview T3 | 0.039 → 0.042 | 0.000 → 0.000 | 0.039 → 0.042 | +0.003 s |
| T5 — synchronize three changed tables, skip seven equal | 74.073 → 87.047 | 44.589 → 52.024 | 31.425 → 35.572 | +17.5% |
| T6 — import three changed DBs | 22.214 → 25.124 | 13.606 → 15.271 | 8.608 → 9.853 | +13.1% |
| T7 — export 58 leaves | 261.084 → 154.438 | 149.788 → 93.911 | 111.296 → 60.631 | −40.8% |

All seven workloads have three valid measured calls, matching outcomes and restoration checks; all seven warm-ups also qualify. The replay validated 103 exact canonical-document/audit pairs. The synchronization regressions are retained as measured. This is a three-sample comparison of these builds on one machine, not a causal attribution or a statistical performance guarantee.

## Contract and lifecycle verdicts

This is the single verdict table. C1, C2 and C6 each summarize their stated variants; the remaining rows each represent one final case.

| ID | Observed result | Verdict |
| --- | --- | --- |
| C1 | Exact and case-variant duplicate names reached Portal and returned `engineering_rejected` with `EngineeringTargetInvocationException` and the uniqueness diagnostic. The newly requested root remained empty; existing mappings/files were preserved. | PASS |
| C2 | Exact, case-variant and trailing-separator shared roots were blocked by `vci_workspace_exists`. No new workspace was adopted or created. | PASS |
| C3 | A nested-root workspace was created with zero mappings. Native `.vci/workspace.vci.config` metadata was created inside that root; unrelated baseline bytes stayed unchanged. | PASS |
| C4 | Mixed seven-leaf export returned `engineering_rejected`, six successes and one failed inconsistent OB. One failure entry named the rejected leaf and retained its native type; the six exported mappings were `Equal`. | PASS |
| C5 | The protected FC produced the know-how refusal with native engineering evidence, no exported file and no mapping. | PASS |
| C6 | The complete 259-character `.xml` path exported and mapped successfully; 260 characters was blocked by `vci_name_not_representable`, with no export. | PASS |
| C7 | Export to the read-only mapped file returned the expected native rejection. The file's bytes were unchanged. | PASS |
| C8 | Malformed XML import returned `failed:2`, `succeeded:0`, one native failure entry and `projectModified:false`. Both observed PLC sources stayed unchanged and both targeted compiles were clean. | PASS |
| C9 | The missing leaf was listed as `worker_operation_failed` without native engineering evidence. One valid DB imported, the missing leaf's PLC source stayed unchanged, eight equal mappings were skipped, and targeted compiles were clean. | PASS |
| C10 | Re-running the C4 export skipped the six mapped successes, listed the same native failure and created no additional export files. | PASS |
| L1 | All six operations on one workspace completed. The earlier five verification entries were `superseded`, the last `trusted`; the workspace was absent afterward, files remained, the observed PLC source was unchanged and its compile was clean. | PASS |
| L2 | A dependent export onto a stray file was accepted with no collision guard or server-raised failure. Two mappings were `Equal`; only the other intended export added a file. The copied stray's bytes stayed unchanged. | PASS |
| L3 | The mixed export failed after three completed items; three later items were skipped. Root `success:false`, `error:null` and MCP `isError:false` retained failures and `engineeringError`. Earlier entries were `superseded`; the shared `partial_write_no_rollback` info guard identified the failed item. Seven completed exports remained mapped and `Equal`. | PASS |
| L4 | Preview returned the dependent warning and first-item effect only, with no batch or verification. No workspace or files were created. Absence of dependent worker planning is qualified by Task 6's offline tests, not inferred from this capture. | PASS |
| L5 | A first-item export onto a stray file was accepted without a collision guard or server-raised failure. Its mapping was `Equal`; the copied file's bytes stayed unchanged. | PASS |
| L6 | `sync_to_project` retained `vci_import_unvalidated`; one DB imported, nine equal mappings were skipped, only the intended observed PLC source changed, and both targeted compiles were clean. | PASS |

Failed write items had no verification entry. In particular, C4's failed single item had an empty verification list with verification success true, while root success was false: that result does not verify the failed item's final state. Independent reads supplied the observations above.

## Capture corrections and restoration

Two setup attempts were excluded from final L-case qualification and retained privately. An L1 preflight requested source for a LAD FC and was rejected before any L1 write; its accepted retry used XML. An L2 setup used `DB` instead of the observed public `GlobalDB` selector type: the temporary workspace was created, but export was refused before worker mutation. Recovery discarded that workspace, removed only its created fixture root, reopened the saved project and verified the baseline before retrying L2. No candidate mutation logic changed and no uncertain mutation was replayed.

The case analyzer also needed regression-tested corrections for a valid empty offset-200 page accompanying a complete empty first page, native workspace metadata on successful creation, and the shared partial-write info guard. Original captured responses were preserved. Unknown guards, incomplete page groups, arbitrary file changes and incorrect native evidence still fail analysis. Final qualification combines 14 C variants, L1 and the remaining five L variants from their respective preserved sessions; all 20 pass current analysis.

After every final case the client completed discard/reopen restoration and verified the saved-file hash and open/unmodified state. Final independent checks found the same six workspace names, roots and mapping counts; all 216 base mapping records were unchanged and `Equal`. Names, sizes and SHA-256 hashes for 1,249 files across the base and five pre-existing workspace roots matched the initial matrix capture exactly. The final saved-project hash was also independently compared with the configured saved revision. Capture clients exited; the project remained open and unmodified.

## Rulings applied and limits

- Workspace conflict protection remains exact-root only, case-insensitive with trailing separators ignored. Duplicate names reach native creation; nested roots are allowed. A failed native create can leave its requested empty root.
- Portal owns existing-file and mapping-key behavior. Overwrite, companion-file deletion and disruption of another mapping inside the authorized key directory are accepted possibilities. The L2/L5 strays were valid copies matching the same leaf: these cases qualify acceptance of an occupied key, not destructive overwrite of differing content, companion deletion or another mapping's disruption. Stage 0 supplies the earlier overwrite observations.
- Directory segments and containment remain guarded; leaf policy retains path boundaries and alternate-data-stream prevention. The XML limit counts the complete path with its four-character extension; other formats retain the eight-character reserve. Only the stated plain-name 259/260 candidate boundary was exercised here.
- Dependent items have no inventory, counts or evidence guards of their own; operation info guards remain. Completed items use shared workspace-wide `trusted`/`superseded` verification without native read-back. A failed outcome-bearing attempt can supersede earlier success.
- Post-capture source review corrected the documentation and public tool-description text from scope order to recording order. Synchronization can record a later leaf's prefilter failure before an earlier queued leaf fails natively; the first recorded leaf failure controls accumulated-failure classification. A global scope-order guarantee is not made. This mixed-phase ordering was not induced live here, and mutation logic was preserved. The read-only workspace-create planning recheck also deliberately retains `worker_operation_failed` with `engineeringError` for target-invocation exceptions; `portal_lost` still takes precedence.
- No live 25-failure/cap/trimming case, broader format or directory-name matrix, crash, timeout, race or Portal-loss probe was performed. Portal-loss and the absence of dependent worker planning remain offline qualifications. Only standalone/full mode was exercised here.
- C8 qualifies the observed malformed-XML/two-candidate outcome. It does not establish rollback at every native throw location or after arbitrary earlier imports. Imports were followed by targeted block compiles, not a whole-project compile.
- Captures are sequential observations, not atomic snapshots or proof of full project-state equality. File-boundary checks cover captured roots; unrelated filesystem paths and PLC objects were not exhaustively observed. No out-of-key write or wrong-object import was found within the captured scope.
- F28 (XML read after VCI re-import returning `.s7dcl`) and F29 (counters appearing to carry over between items) remain deferred observations from Stage 0; this continuation does not establish their cause or fix.
