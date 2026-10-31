# VCI native-rejection Stage 0 — 2026-10-31

**Status:** Stage 0 capture is complete within the limits below: 21 qualified baseline timing samples, B1–B8 and supplements, 16 original probe variants, the later [protected-FC probe](2026-10-31-vci-write-stage0/protected-supplement/README.md) and seven [length-only path variants](2026-10-31-vci-write-stage0/path-supplement/README.md). Know-how-specific refusal and the observed SimaticML path boundary are qualified within those fixtures. The live 25-failure case remains unqualified. Rulings 1–3 are approved; the new path evidence supports ruling 5, and rulings 5–6 remain before the implementation plan.

**Design:** [approved protocol](../../../specs/2026-10-30-vci-write-native-rejections-design.md), §§8–9. Stage 0 supplies evidence and maintainer rulings before an implementation plan. The product refactor, candidate timing and L1–L6 acceptance remain outstanding.

**Evidence:** [archive and offline checks](2026-10-31-vci-write-stage0/README.md), [summary](2026-10-31-vci-write-stage0/summary.json), [timing qualification](2026-10-31-vci-write-stage0/timing-qualification.json), [import proof](2026-10-31-vci-write-stage0/import-proof.json), [restoration](2026-10-31-vci-write-stage0/restoration.json), and [anonymized inputs](2026-10-31-vci-write-stage0/case-inputs.json). Raw project identities, full project-status metadata/history and PLC source exports remain private; lifecycle actor names are anonymized, while ordinary state timestamps remain.

## Provenance and offline verification

| Item | Recorded value |
| --- | --- |
| Baseline source | `8713a476ec509360b7cc18083e76d21275fc1c92` |
| Baseline package | `4.2.0-stage0.baseline2.g8713a47` |
| Throwaway probe source | `7275a3fe745d3b8426947dabe8ef3d3ef8f0b3b7` |
| Probe package | `4.2.0-stage0.probe.g7275a3fe` |
| Portal executable build / UI | `2100.0.121.1` / English |
| Access mode | `full` |
| Worker timeout | 15 minutes, CLI source |
| Capture-client timeout | 7,200 seconds |
| Value/document budgets | 1,000,000 characters each, CLI |
| Baseline non-packaging suite | 7,644 passed; zero failed or skipped |
| Probe focused VCI suite | 539 passed; two expected workspace-conflict assertions failed |
| Probe non-packaging suite | 7,642 passed; exactly the same two expected failures; no unexpected failures |
| Package checks | Installed host and worker hashes match their package payloads; no Siemens Engineering DLLs in either package |

The probe changes only VCI exception-note capture and the workspace-name/root conflict checks specified in §9.3. It retains format, path, collision, scope, status and transaction behavior. It does not implement the new error or verification contract. The two failed tests explicitly assert the disabled name/root-conflict behavior; their failures are expected, not a green probe suite. A separate existing writer root-conflict test also refuses a missing parent, so its pass does not isolate root-conflict protection.

Both builds use separate temporary local installations. The globally configured installation was preserved. Deeply nested local installation paths failed to launch the worker; a short temporary installation worked. Package inspection confirmed the worker was present, so no product packaging defect is claimed.

[Build provenance](2026-10-31-vci-write-stage0/build-provenance.json) retains exact package, installed host and worker SHA-256 hashes and test commands. Packaging-category tests were not run: no product project file changed. The probe's two expected failures are `VciPlannerTests.RootComparison_IgnoresCaseAndTrailingSeparator` and `VciPlannerTests.Create_DuplicateNameIsConflictRegardlessOfRoot`.

## Method and fixture

All native requests were serialized through the public MCP tools and their actual worker. There was no standalone Openness harness, deliberate Portal-loss probe, direct device/library-container export, or PLC SimaticSD export. Build and test processes had finished before measured calls.

The diagnostic build triggered an Openness access dialog. A one-shot **Yes** was accepted under the maintainer's explicit authorization; **Yes to all** was not used. All content calls stayed pinned to the selected disposable standalone project.

The saved timing fixture contains one PLC, a `base` workspace with 216 mappings, ten mapped control DBs, separate three- and ten-leaf export scopes, and a reserved 58-leaf scope for T7. The larger export scope implements “about 50” with 58 leaves. T3/T4 use six items: create a workspace, export ten leaves, delete three mappings as individual items, then export the same ten under another relative directory. These exact inputs must be retained for the candidate comparison.

Each timing case has a warm-up and three measured calls. The startup/discard call and fixture preparation are excluded. No slow sample was dropped: T3 root durations were 29,980, 13,579 and 15,797 ms. Each median is computed independently from audit v2 root duration, the sum of item durations, and their per-call remainder.

Every measured response was checked against its original canonical text and structured document, correlated with its original audit response/hash/timestamp, and checked against the case-specific protocol. The protocol validator reports all seven cases valid, with three validated runs each. Preview files were unchanged. Public open/unmodified status and workspace inventory confirmed restoration after every run.

Restoration closes without saving, restores the owned workspace from its saved file copy while the project is closed, removes the case's temporary workspace root when applicable, reopens and warms the workspace inventory. A full-copy rehearsal retained `Equal` status for all ten control mappings. Final checks found all 216 base mappings `Equal`, the project open/unmodified, all 217 base files unchanged, and all 1,032 files and directory inventories in five pre-existing workspaces unchanged. The retained backup contributes another 217 files. These checks restore the saved Stage 0 fixture, which intentionally includes the new control blocks and workspace; an unchanged AP21 hash alone does not prove full project-state equality.

### Qualified timing controls

The initial source-update T5 warm-up was rejected as a control: replacing source content recreated three DB objects and the scoped mapping view no longer selected those objects. Its synchronization performed no changed-leaf work. It contributes no measured sample.

Qualified T5 instead creates ten temporary tag tables, maps them, and adds one Bool tag to three tables through `plc_write`, preserving table identity. The timed scope expands over 33 tag tables, of which ten are mapped. Each measured call synchronizes three and skips seven equal mappings. Its pre-call base count is 226; restoration returns to 216.

Qualified T6 removes seven control mappings outside the timed call, leaving exactly three mapped DBs. Three workspace start values change from zero to one. Each measured call imports all three, skips none, and is followed by independent source reads and three successful zero-error block compilations. Its pre-call base count is 209; restoration returns to 216. The earlier ten-mapped-leaf warm-up is excluded.

## Baseline timing

Seconds; three samples per row. Every column is a separate median, so the remainder median need not equal the root median minus the item median.

| Case | Qualified work | Root | Item sum | Remainder |
| --- | --- | ---: | ---: | ---: |
| T1 | Create one workspace | 3.313 | 0.497 | 2.816 |
| T2 | Export three unmapped leaves | 12.726 | 7.024 | 5.596 |
| T3 | Six-item dependent sequence; exports 10, deletes 3, re-exports 3 and skips 7 already mapped | 15.797 | 14.950 | 0.847 |
| T4 | T3 preview; no mutation | 0.039 | 0.000 | 0.039 |
| T5 | Ten mapped tag-table leaves; three synchronized, seven equal | 74.073 | 44.589 | 31.425 |
| T6 | Three mapped DBs imported; nine post-run compilations total, all zero-error | 22.214 | 13.606 | 8.608 |
| T7 | Export 58 unmapped leaves with 216 initial mappings | 261.084 | 149.788 | 111.296 |

These establish the before baseline. No candidate timing, speedup or before/after comparison has been performed.

## Baseline functional observations

| Case | Observed result | Qualification |
| --- | --- | --- |
| B1 | All 30 leaves exported: 30 succeeded, zero failed | The intended inconsistent-leaf negative input was not established |
| B2 | Safety FB candidate exported successfully | Its know-how protection kind was not independently established; this does not qualify the protected-block negative case |
| B3 | Blocked by `vci_name_not_representable` | Local path barrier observed; native 256-character boundary not reached |
| B4 | Read-only tag-table file: one failed leaf, zero succeeded; generic `worker_operation_failed` | Scoped refusal observed; the 219-file before/after manifests had no additions or changed length/hash entries |
| B5, unknown datatype | Well-formed XML imported both changed files; two succeeded, eight equal, `projectModified:true` | Semantic invalidity was accepted; compilation of the invalid DB then reported one error, while the valid DB compiled successfully |
| B5, malformed XML supplement | Mismatched closing tag: zero succeeded, two failed, eight equal, `projectModified:false`; generic failure | Both scoped post-import source hashes matched their before hashes, and both DBs compiled with zero errors: scoped rollback observed |
| B6 | Missing file plus valid changed file: one succeeded, one failed, eight equal, `projectModified:true` | Missing-file target source unchanged; valid target source changed to value one; both compiled with zero errors |
| B7 | Both duplicate-name and shared-root requests blocked by `vci_workspace_exists` | Baseline host/worker protection observed; native acceptance/refusal awaits P1/P2 |
| B8 | All 30 leaves exported: 30 succeeded, zero failed | No 25-failure live qualification or native message-length sample |
| B2-OB supplement | Safety OB export: one failed leaf, zero succeeded, no fallback | Probe notes identify native inconsistency refusal; protection kind remains unverified |
| B1-OBmix supplement | Seven-leaf Safety scope: one failed and six succeeded, no fallback | Reachable mixed export demonstrates continuation within the failed item; it is not a 25-failure fixture |
| B2-protected supplement | Supplied protected FC: one failed leaf, zero succeeded, no fallback; old generic error | Paired diagnostic run explicitly identifies know-how protection; restored saved FC and base files preserved |

Attempted baseline failures retain the old generic per-leaf message and counts, without native exception reasons. Their canonical attempted-failure documents use `error:null`; guard refusals are separate blocked documents.

### Why the original 25-failure input is unqualified

Source-content initialization invokes a whole-PLC compile in the baseline importer, which may have compiled the original 25 SCL FCs before B1/B8. A separate freshly created SCL FC also exported successfully before its first explicit focused compile. “New and not explicitly compiled” therefore did not establish a VCI export rejection on this fixture; its native consistency flag was not independently proved. The subsequent export after focused compilation skipped the already-mapped leaf, so it was not a second native export comparison.

The one-failure Safety mixed scope is a qualified alternative for continuation/error surfacing. It does not establish 25 native failures, the proposed 20-entry cap, five omitted entries, or 25 native message lengths.

### Original path-boundary limitation and native follow-up

`VciMirroredPath.FirstProblem` requires the full path without an extension plus the reserved eight extension characters to be at most 259. A SimaticML `.xml` path can therefore reach at most 255 characters under this check. The requested 256–259-character SimaticML input is blocked before native export. The approved probe patch retains this barrier, so P6 cannot settle Siemens' native 256-character boundary without an additional maintainer ruling. No barrier was weakened.

The maintainer subsequently requested testing before ruling. A separate length-only diagnostic disabled only this numeric clause, retaining name/segment, containment, collision, format and workspace checks. In the [paired native matrix](2026-10-31-vci-write-stage0/path-supplement/README.md), complete `.xml` paths of 255–259 characters exported correctly with exact paths and `Equal` mappings; 260 and 263 characters raised native ETI, leaving no file/mapping and six empty directories. The native error text is generic, with no explicit length reason. This establishes observed behavior for the tested FC/SimaticML layout; fixed four-character extensions cannot distinguish a stem `<256` limit from a complete-path `<260` limit. Other formats and longer directory paths remain unqualified.

## Native probe observations

| Case | Observed result | Qualification |
| --- | --- | --- |
| P1 exact and case variant | Both rejected by `EngineeringTargetInvocationException`: “The workspace name must be unique” | O1. Existing inventory/files unchanged. Each attempt leaves the newly created empty destination root; retirement approved, implementation pending |
| P2 exact, case and trailing separator | All accepted; a new zero-mapping workspace shares the existing base root; base retains 216 mappings | O2 observable sharing, without a native warning. No files/directories changed. The design requires retaining the shared-root guard |
| P3 nested root | Accepted; new workspace has zero mappings; one metadata file and two directories created within its nested root | O2 observable nesting. Existing files and base mappings unchanged; no O3 effect observed in this create-only scope |
| P4 original 30-leaf input | 30 exported, 30 `Equal`, zero exceptions/fallbacks | Confirms acceptance; does not qualify the intended inconsistent-FC fixture |
| P4-OBmix supplement | Seven Safety leaves: one native ETI refusal, six exported and `Equal`, no fallback | Qualified mixed inconsistent-block rejection and continuation. Failed leaf's message explicitly says the object is not consistent |
| P5 Safety FB | Exported and `Equal`; no exception/fallback | Acceptance recorded; know-how protection is unverified. Public block metadata exposes no dedicated protection flag |
| P5-OB supplement | One native ETI inconsistency refusal, zero exports, no fallback | Four empty export directories remain; no export file. This qualifies inconsistency, not know-how protection |
| P5-protected supplement | Supplied FC: one native ETI from `Workspace.ExportObject`, explicitly “know-how protected”; one failed leaf, no file/mapping/fallback | O1 know-how-specific refusal qualified for this saved FC/SimaticML target; four empty directories removed during restoration |
| P6 long path | `vci_name_not_representable` blocks; no filesystem changes | Kept barrier confirmed; native 256-character boundary unreachable |
| P6 length-only follow-up | Five exact XML outputs at full lengths 255–259; native ETI/no file/map at 260 and 263 | Separate paired seven-case supplement qualifies this observed boundary and shows the old guard blocks four valid paths; other path protections stayed enabled |
| P7 read-only file | One native ETI from `MappedObject.Synchronize`; one failed leaf, zero successes; file bytes unchanged | O1 under the induced read-only condition. Native text itself gives no read-only/access reason |
| P8 semantic-invalid XML | Two imported, eight equal; no exception; compile errors `[1,0]` | O2. Invalid datatype accepted; valid peer changes from zero to one. Compilation is required to detect the invalid import |
| P8 malformed XML | One native ETI; two failed candidates, zero imported, eight equal, `projectModified:false` | O1 plus scoped rollback. Both source texts/hashes unchanged and both compile successfully. One thrown leaf is distinct from two rolled-back candidate counts |
| P8 missing file | One import, one failed leaf, eight equal, `projectModified:true`; no leaf exception | Status-based `FailMissing`, followed by partial commit. Missing peer stays zero; valid peer becomes one; both compile successfully |
| P9 original 30-leaf input | 30 exported and `Equal`; zero failed leaves or native messages | Twenty-five native failures, message variation and live cap/omission behavior remain unqualified |

All ten paired baseline/probe primary request documents match exactly, including the corrected read-only and malformed-import cases. Every attempted probe still uses the old `worker_operation_failed` contract; ETI types are evidence in diagnostic notes, not an implemented `engineeringError` or `engineering_rejected` response.

The later protected-FC pair also matches exactly. Its [separate evidence](2026-10-31-vci-write-stage0/protected-supplement/README.md) contains 26 original canonical responses/eight audited writes, preserves the maintainer's new saved FC and all 217 base files, and records the fixture revision for candidate timing. No timing run was repeated or requalified by that supplement.

Captured native outer messages have original, pre-anonymization lengths of 102 characters (duplicate name), 98 (read-only sync), 144 (inconsistent Safety OB) and 341 (malformed import). They have no inner-chain entries. Per-leaf failures also carry a separate 66-character aggregate `WorkerOperationException` note; that wrapper is not another native leaf failure. P9 supplies no 25-message length/variation sample.

The protected-FC supplement adds a 232-character native outer message with no inner chain and the same separate 66-character aggregate wrapper. The length-only supplement adds two generic 96-character native `ExportObject` messages, each with no inner chain and its separate 66-character aggregate wrapper.

No O3 effect was observed in these scopes. Accepted shared/nested-root creates do not qualify later writes through those roots. All mutating cases were restored before the next case.

## Maintainer decisions and completion boundary

All six rulings are settled following the 2026-10-31 walkthrough and 2026-11-01 clarification. The protected-FC supplement resolves ruling **4**. The length-only supplement informed ruling **5**; the maintainer chose to retain a corrected length diagnostic, remove export collision prevention and delegate leaf filename rules to Portal. The maintainer also accepted ruling **6** and confirmed that compilation remains a separate explicit tool call.

1. Retire the duplicate-name half of `vci_workspace_exists` based on both O1 variants, accepting the empty-root side effect.
2. Retain the shared-root half: all three variants were accepted. Keep nested-root behavior as the design's record-only case; later writes through aliases/nested roots were not probed.
3. Use the inconsistent Safety OB/mixed scope for reachable export-failure and continuation fixtures. Accept the live 25-failure coverage gap; require automated fake cases for the 20-entry cap, five omitted entries, exact counts and trimming. Candidate acceptance remains separate.
4. Use the supplied protected FC as the qualified know-how-specific refusal fixture. Its native message explicitly confirms protection; the earlier Safety FB did not establish it.
5. Retain the numeric length diagnostic and correct its conservative calculation: complete SimaticML `.xml` paths through 259 characters succeeded, while 260 and 263 failed with a generic native message. Other formats remain unqualified. Remove export collision prevention, accepting native overwrite as the tool contract; the earlier spike observed silent overwrite, companion-file deletion and disruption of another mapping. Remove leaf filename-rule duplication while retaining containment and protection of original directory-name boundaries before the writer creates directories. Native filename messages and problematic directory names were not qualified by the length-only probe.
6. Use malformed XML as the throwing import/rollback fixture, and missing-file import as the separate partial-commit fixture. Keep `compile_check` as a separate explicit call after import, not part of import execution: well-formed semantic invalidity was accepted. All three paired baseline/probe cases are already captured with source-hash comparisons and separate compilation results.

These rulings supersede the original design's collision-blocking and filename-validation expectations, including the original L2/L5 blocked/no-overwrite cases. Closing Stage 0 as an evidence record does not qualify the candidate's new failure list/cap, trusted/superseded verifier, dropped replans, contract 2.0 or revised final L matrix. The implementation plan must incorporate the settled rulings.

## Evidence checks and cleanup

Original canonical/audit correlation is verified before redaction. Stored original response hashes describe original unredacted text; they are not hashes of anonymized archive bytes. The archive omits full PLC source exports and project-status metadata/history. The hash-only import proof contains 21 before/after comparisons and 21 compile reports; the curated restoration record retains the necessary final state/file checks. Matrix presence is separate from negative-case qualification.

Publication checks validated 217 selected original audit pairs and 2,162 archived artifacts, all 21 timing samples, all ten exact baseline/probe request pairs, all three helper self-checks, local document links and the published file checksums. The identifier/token-pattern scan found no retained originals. Independent post-stop review additionally checked all 593 raw canonical responses, 233 raw audit pairs and all 16 probe restorations; setup records outside the case filter account for the larger private totals.

The later path supplement adds 124 original canonical responses/56 audited writes and 448 published artifacts, seven exact request pairs and 14 clean restorations. It uses source `b792c2e8`; its diagnostic Contracts DLL copies were independently verified. The temporary package/branch were removed and the safe checkout restored after the native run.

The test clients stopped normally. The diagnostic tool was uninstalled and `spike/vci-native-probes` deleted after its exact commit was preserved in a verified private Git bundle and published patch. The app refused to archive its primary checkout and refused to attach the ordinary source checkout as a managed worktree. The retained clean managed checkout was returned to production source; it contains no active probe patch. The saved Stage 0 project/workspace fixture and file backup remain available for the candidate comparison, and the global installed tool remains unchanged.

On 2026-11-01 the unchanged production source passed a fresh focused VCI run: 541 passed, zero failed/skipped. The three public evidence self-checks also passed. The import proof has 21 before/after hash comparisons, 21 separate compile reports and no pending cases; malformed-XML rollback, missing-file partial commit and accepted semantic invalidity are already qualified for the recorded baseline/probe. This verification added no live mutations or timing samples.

Remaining work is the implementation plan/refactor and candidate timing/acceptance, including the automated failure-cap cases and revised collision expectations. Native Portal-loss, crash and race probes were excluded by protocol.
