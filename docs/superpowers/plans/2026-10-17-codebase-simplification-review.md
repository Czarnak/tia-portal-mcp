# Codebase review 2026-10-17: simplification and consistency plan

Baseline: `main` @ `f0c79f8` (after the VCI tools merge). This was a static review: 8 area reviewers, each followed by an adversarial verifier. No builds, tests or live runs. Full evidence for every ID is in the [findings appendix](2026-10-17-codebase-simplification-review-findings.md). All decisions Q1–Q6 below were agreed with the maintainer on 2026-10-17.

**Result:** 116 findings. 61 confirmed, 53 partly confirmed (the verifier corrected the scope or the fix; the corrected version is used here), 2 refuted (TOOLS-15, WORKERDOMAINS-15). After merging cross-area duplicates there are about 85 distinct items. They fall into six workstreams (A–F). Order A → B → C keeps each PR small and leaves contract changes for last.

## Decisions needed before execution

| # | Decision | Findings | Recommendation |
|---|---|---|---|
| Q1 | Retire the three worker-only probes (~1,900 lines)? They are documented as temporary acceptance tooling. Git-ignored harness scripts still drive them. | WORKERCORE-3, WORKERNET-3 | Do not delete now. Log the retirement in IMPROVEMENT_LOG, tied to `update_io_system` acceptance. `probe_subnet_lifecycle_mutations` and `probe_network_object_attributes` can go earlier if the Phase 3/4 harnesses are finished. |
| Q2 | Retire the `[LegacyNullOmission]` mechanism? Since every tool migrated, nothing in production uses it. | CONTRACTS-2 | Retire it. The register is empty, and AGENTS.md already says every tool is structured. |
| Q3 | Make `portal_lost` worker-wide, by mapping `NonRecoverableException` once in `Program.Execute`? Today only VCI uses it; the other domains report `worker_operation_failed`, and Multiuser reports `binding_conflict`. | WORKERCORE-4, WORKERDOMAINS-16, WORKERNET-7 | Yes. The host already invalidates binding on `portal_lost`. Adds a failure category for PLC, Network, HMI and lifecycle. Implemented; the maintainer accepted scoped live testing as finished on 2026-10-20. The [B-16/Q3 record](2026-10-19-worker-portal-loss.md) retains candidate-specific evidence and untested cases; existing typed write outcomes remain. |
| Q4 | Rejection shape for unknown root arguments on read and lifecycle tools: plain text, as the writes do, or the canonical envelope? | TOOLS-1, TOOLS-2 | **Revised:** use one rule for all 19 tools. An argument-shape error returns the tool's own canonical document, with `error.category = validation_error` and `isError: true`. Writes then lose the plain-text exception documented at json-contract.md:296. Keep "no audit record for argument-shape errors". Reason: all 19 tools declare an `OutputSchemaType`, and json-contract.md:104-115 requires `error` to be non-null whenever `isError` is true. |
| Q5 | Worker `Confirm` fence: remove it, or set it centrally? The host always sends `true`. The worker checks it in 12 handlers and skips it for HMI and VCI. | HOSTCORE-2, WORKERCORE-2 | Remove it. The policy catalogs already gate both processes. Also reword the "Set confirm=true" messages, which point at a public argument that no longer exists. |
| Q6 | **Reframed (maintainer, 2026-10-17):** content tools should run only within the bound project, so they should not take a `projectPath` at all. The mixed-path question then disappears. | TOOLS-3, HOSTDOMAINS-14 | Remove the per-item `projectPath` from the 8 batch tools, and the root `projectPath` from `read_cross_references`, `compile_check`, `browse_project_tree` and save/archive/close. An unbound session fails with "bind first". `projectPath` stays only where it selects or creates a target: `bind_project`, `open_project`, `create_project`, and the `save_project_as` destination. See new item C-0. |

## Workstream A: dead code and leftovers (S each, no wire or contract change)

Deletions only, with matching test updates. One PR, or one commit per bullet.

- **Dead wire fields:** `WorkerRequest.AllowTiaConfirmations` (~26 host assignments) and `WorkerRequest.ForceRebind`. Neither is read by the worker or FakeWorker. (HOSTCORE-1, WORKERCORE-1, CONTRACTS-1)
- **Leftovers of the removed generic batch apply:** `StructuredOperationBatchExecutionEngine.ApplyWritesAsync`, used only by 3 tests. Move those tests to `WriteExecution`, and add a `StructuredOperationItem.Skipped` factory. (HOSTDOMAINS-6, HOSTCORE-5)
- **Leftovers of the legacy text contract:** `StandaloneToolResultFormatter`, `WorkerCallResult.ToText`/`ToEnvelopeText`, and the stale `TiaJson` doc. Keep `TiaJson` itself. (HOSTCORE-6)
- **Leftovers of the removed safety snapshot:** `BlockExporter.ExportForSafety` and the `requireAuthoritativeXml` branches. (WORKERDOMAINS-4)
- **Overloads used only by tests:** `BlockImportCoordinator.Execute` (legacy), `BlockPostconditionVerifier.Verify(evidence)`, the `ProjectSessionBinding` members `BoundProjectPath`/`BindingState`/`TryResolve`/`IsBoundTo`, `ExecutableResolver`'s legacy method, the `TagTableReader.ReadAll` lossy reader and `IoTagMatcher`. (WORKERDOMAINS-5, CONTRACTS-6, HOSTCLI-8, WORKERDOMAINS-10, CONTRACTS-5, and the `.github/labeler.yml:95` comment)
- **Unreachable or overwritten logic:**
  - `ReadNode` computes selectors that `NetworkNodeReadSelectorBuilder.Apply` always overwrites, and the per-device `Apply` pass is redundant. (WORKERNET-4)
  - `NodeAt` is unused. (WORKERNET-12)
  - The MiMoCode `--json` branch and text-matched exit codes are unreachable. (HOSTCLI-6)
  - The null-block branches, the duplicate PLC lookup and a pass-through coordinator in Block are unreachable or redundant. (WORKERDOMAINS-18)
  - The `?? ReadWrite` fallback on the non-nullable `AccessPolicy` is dead. (TOOLS-6)
  - Smaller items: HOSTCORE-8, HOSTDOMAINS-7, CONTRACTS-10, CONTRACTS-14.
- **Stale docs and messages about removed features:** PLC run/stop, the legacy audit stream, the batch tools, and the read-only banner's "PLC control". (HOSTCORE-7, HOSTCLI-14, TOOLS-14). Per the docs-last rule, this goes in the PR's final task.

Verification: `dotnet build -m:1 /p:UseTiaPortalReferenceStubs=true` and `dotnet test`. No live run is needed.

## Workstream B: correctness defects found during review (fix with a failing test first)

Ranked by impact.

1. **Doctor and server disagree on access mode.** With an invalid `TIA_MCP_ACCESS_MODE`, doctor quietly reports read-write while the server refuses to start. Fix: give `AccessModeParser.Parse` an env getter and use it from `DoctorCliParser`. (HOSTCLI-1; high, S)
2. **`compile_check` can compile the wrong PLC.** It uses root-only, first-match `PlcSoftwareLocator.FindAll`. Fix: use `FindUnique`/`FindEveryPlc`, then delete `FindAll`. This closes IMPROVEMENT_LOG M5. Live check with a PLC inside a device group. (WORKERDOMAINS-1; high, S)
3. **Installer launch arguments.** `McpLaunchSpec.Arguments` is built and never read, and the MiMoCode guide drops `--tia-project`. Fix: have the installers consume `spec.Arguments`. (HOSTCLI-4; S)
4. **Unreadable subnet and IO-system candidates.** They are skipped silently in `MatchSubnet` and `ResolveIoSystem`, which can produce a false unique match or a false `target_not_found`. Fix: one `SubnetIdentity` helper; return `evidence_mismatch` when candidates are incomplete, as `MatchDevice` already does. This changes behavior; needs live verification. (WORKERNET-2, WORKERNET-6; M)
   - Implemented offline on 2026-10-19: shared exact subnet-identity reads across discovery, hardware reads and selection; incomplete required identity or recoverable enumeration failures now return `target_evidence_mismatch`. Existing IO-system index/name constraints and optional names are preserved; Portal loss and programming errors propagate through the touched paths. Source-linked offline regression tests reproduce the previous false-match/absence behavior and verify the fix. The subsequently authorized [scoped read-only live regression passed](../acceptance/reports/2026-10-19-network-b4-live-verification.md) on the installed candidate; native unreadable-candidate, enumeration-failure and same-namespace ambiguity cases remain offline-only. B-16 and Q3 remain separate.
   - Offline evidence: 6,977/6,977 tests passed with no skips in the completed serial run; all 23 focused regression cases passed, covering 32/32 added executable resolver lines and the new helper's 1/1 executable line. Serial stub and installed-V21-reference solution builds passed. Both builds report seven nullable warnings in the test assembly: five HMI warnings and two from the newly linked resolver's existing device-item path. Independent review found no issues. Earlier sandbox/interrupted runs had subprocess timeouts; the completed run used process-local `TEMP`/`TMP` under ignored `TestResults/network-b4/temp`, outside the sandbox after stopping only the verified test process trees.
5. **Write gate loses the failure category.** It turns every binding-gate failure (for example `worker_timeout`) into `binding_conflict`, and uses a different "binding moved" rule than the lifecycle path. Fix: keep `gate.FailureCategory` and share one `IsConsistent` predicate. (HOSTCORE-4; S)
   - Implemented offline on 2026-10-21: ordinary writes preserve the gate's failure category, falling back to `binding_conflict` only when absent. Both write and lifecycle preparation now use `WriteBindingPreparation.IsConsistent`: an existing verified binding must retain its full identity; only a configured source may become verified for the same normalized project path at exactly the next revision. Regression tests reproduced 19 failures before the fix; all 71 focused cases passed afterward, with full line/branch coverage of the shared predicate. The four write references document the failure categories. Subsequently authorized live evidence is recorded below.
   - Offline evidence: 7,116/7,116 tests passed with no skips; the serial stub solution build passed with seven existing nullable warnings in the test assembly. Coverage exercised all 8 changed executable production lines and all 8 branches of the shared predicate. Independent review found no issues. The initial sandbox suite was stopped after repeated unrelated PowerShell subprocess timeouts; the completed suite ran outside the sandbox with process-local `TEMP`/`TMP` under ignored `TestResults/b5/temp`.
   - Scoped live verification passed on 2026-10-21 against installed `4.1.2-local.1.g27e5155` (`27e5155`), one already-open disposable standalone fixture A, in `full` mode. Valid unbound previews from all four write tools returned `binding_conflict`; bound PLC, Network, HMI and VCI previews and `save_project(dryRun:true)` succeeded. A fresh installed host with `--project` grounded revision 0 to verified revision 1 and retained that exact binding on a second preview. Interrupting only a second temporary host's worker during grounding returned `worker_crashed`, with the audit still identifying the configured/unverified revision 0 and no effects or batch execution. Explicit binding recovery and another PLC preview then succeeded.
   - Live preservation and limits: all 13 scoped guarded-call responses matched their single audit record's exact text and SHA-256 hash, with text/structured semantic parity. The original Portal and MCP worker remained alive; both temporary hosts and their workers exited. The same project remained open with `isModified:false`, and neither proposed workspace directory was created. No actual content write, save, compile or project close was performed. A preliminary VCI request hit expected missing-parent validation and was corrected. Private evidence is under ignored `TestResults/b5-live/`. Native timeout, Portal-loss, protocol-error and competing-revision cases remain offline-only; read-write mode was not exercised live. The initial tree cursor expired before the final check, so cursor continuation was not tested; the open project file could not be hashed through `Get-FileHash` while locked by Portal.
6. **Temp-directory cleanup can turn a successful export into a failure.** `Directory.Delete` runs unguarded in 3 exporter `finally` blocks. Fix: one `TryDeleteTempDirectory` helper that writes a stderr warning. (WORKERDOMAINS-6; S)
   - Implemented on 2026-10-21: block XML/source exports and PLC data type exports share `TemporaryDirectoryCleanup.TryDeleteTempDirectory`. Deletion failures produce a stderr warning through the existing worker warning capture, preserving exported content or the original export exception. Existing import and verification cleanup policies remain unchanged.
   - Regression evidence: after extracting the original unguarded deletion unchanged, two real Windows locked-file cases failed as expected: cleanup discarded successful content and replaced an original `portal_lost` exception with `IOException`. All four new cases then passed with the guard, covering recursive deletion, missing directories and both locked-file outcomes; helper line/branch coverage is 100%. All 38 focused export/import cleanup cases and the serial stub solution build passed; the build retains seven existing test-assembly nullable warnings.
   - Full offline evidence: 7,124/7,124 tests passed with no skips; CI-scoped line coverage is 94.31%, passing the 80% threshold. The complete run was serialized outside the sandbox with process-local `TEMP`/`TMP` under ignored `TestResults/b6/temp`; logs, TRX and coverage reports are under `TestResults/b6/`.
   - Live TIA verification is not needed for B-6: the changed behavior is filesystem cleanup after export, reproduced offline with a real file lock; no Siemens export call or public contract changed. No live testing was performed. A failed deletion can leave temporary export files behind, with a warning for diagnosis.
7. **HMI writers lose the error category.** `HmiTagWriter.Failure` re-wraps a `target_not_found` as `worker_operation_failed`. Fix: preserve the category and share the create-with-cleanup path with `HmiAlarmWriter`. (WORKERDOMAINS-11; S)
   - Implemented on 2026-10-21: `HmiWriteRun` shares tag/alarm creation, identity-checked cleanup, property tracking and failure wrapping. Categorized failures retain their category and warnings alongside the partial HMI outcome; caught `EngineeringException` failures retain `worker_operation_failed`. `HmiCleanupStates` names the existing wire values without changing them. Tag-table writes reuse the property/failure helpers with their original exception filter.
   - Regression evidence: nine new cases failed with the original category loss, then passed after the fix. Both focused runs passed all 730 HMI/worker-execution cases, including create collisions, missing/replaced cleanup targets, cleanup failures and retained partial updates. Coverage exercised all 61 changed executable lines; the new helper's 35 executable lines and all branches are covered. Host validation already covers alarm trigger mode, condition, priority and bit number, so the optional wider exception filters were not added.
   - Full offline evidence: 7,133/7,133 tests passed with no skips; CI-scoped line coverage is 94.31%, passing the 80% threshold. The serial stub solution build passed with seven existing test-assembly nullable warnings. The full suite ran serialized outside the sandbox with process-local `TEMP`/`TMP` under ignored `TestResults/b7/temp`; logs, TRX and coverage reports are under `TestResults/b7/`. Independent review found no issues and agreed that live testing is unnecessary for this change.
   - Live TIA verification is not needed for B-7: the defect is worker exception wrapping, reproduced through the real text mapper with Siemens doubles. The extracted cleanup retains the native calls, their order and identity checks. No live testing was performed; native cleanup failures can still leave a created object, and failed updates can retain earlier changes as documented.
8. **Caller errors reported as `worker_operation_failed`.** Lifecycle path, name and archive-mode validation should return `validation_error`; missing-PLC cases in `CompileChecker` should return `target_not_found`. (WORKERCORE-5, WORKERDOMAINS-7; S)
   - Implemented offline on 2026-10-21: lifecycle file/directory/name checks and archive-mode parsing now throw `WorkerOperationException(ValidationError)`; PLC-scope compilation with no matching PLC throws `TargetNotFound`. The existing lifecycle input checks are isolated in a source-linked partial file for executable regressions. Their call sites, validation precedence and native mutation order are unchanged, as are genuine runtime failures and the generic worker exception mapper.
   - Regression evidence: 19 expected failures were reproduced before the exception changes, then all 98 focused lifecycle-input, compile, worker-mapping and lifecycle-identity cases passed. Independent review found no issues. No live TIA verification is needed: the defect is managed error classification, exercised before lifecycle mutation or after empty PLC discovery. No live testing was performed; wider native lifecycle and compile behavior is outside this change.
   - Full offline evidence: 7,157/7,157 tests passed with no skips; CI-scoped line coverage is 94.31%, passing the 80% threshold. The serial stub solution build passed with seven existing test-assembly nullable warnings. A separate 98-case worker coverage run exercised all 38 executable lines and every branch of the extracted validation checks, plus both lines of the missing-PLC throw. The full suite ran serialized outside the sandbox with process-local `TEMP`/`TMP` under ignored `TestResults/b8/temp`; logs, TRX and coverage reports are under `TestResults/b8/`.
9. **`PlcWriteDomain.MutateAsync` ignores the pinned `projectPath`.** The other three domains pass it. (HOSTDOMAINS-14; S)
   - Implemented offline on 2026-10-21: the domain passes the pinned path through `PlcWorkerInvoker.InvokeWriteAsync` to all 14 PLC writes, including block/type content helpers. A null common path retains the item-path fallback, matching Network; the original operation is not modified.
   - Regression evidence: all 14 path-override cases failed before the fix. After review strengthened the fallback assertions, all 236 PLC-focused cases passed, including mixed explicit/omitted paths, pinned-path precedence and mismatched fallback rejection before dispatch. Independent review found no remaining issues.
   - Full offline evidence: 7,172/7,172 tests passed with no skips; CI-scoped line coverage is 94.31%, passing the 80% threshold, and all 18 changed executable production lines were covered. The serial stub solution build passed with seven existing test-assembly nullable warnings. The full run used process-local `TEMP`/`TMP` under ignored `TestResults/b9/temp`; TRX and coverage artifacts are under `TestResults/b9/`.
   - No live TIA verification is required for this fix: the changed behavior is host argument forwarding, covered through the real host client and FakeWorker. The worker's Siemens calls, verified binding gates and mutation semantics are unchanged. The appendix's suggested live smoke would add native PLC evidence beyond this fix's scope. No live testing was performed.
10. **Budget code uses the static limit.** It stamps the static `MaxDocumentChars` into omission metadata instead of the effective limit passed in. This only shows when the limit is overridden; fold the fix into D-2. (HOSTDOMAINS-1, part)
    - Implemented offline with D-2 on 2026-10-21: diagnostic omission metadata uses the resolved per-call document limit in all four domains. The property already reflects startup configuration; the fixed mismatch concerns explicit per-call overrides. Network's separate 512-character cap is preserved. Eight regressions reproduced the old mismatch before the fix; see the [implementation record](2026-10-21-guarded-write-budget.md) for completed offline evidence. No live TIA verification was performed.
11. **Network selector rules drift.** The request catalog and the payload contract encode the selector field matrix separately, and they already disagree for `NetworkInterface`. Fix: one `NetworkSelectorRules` table, plus a test that both sides agree. (HOSTDOMAINS-11; M)
12. **Diagnostics swallow exceptions.** Group-membership failures are reported as "group does not exist". Fix: add a "could not verify" state. (HOSTCLI-9; S)
    - Implemented offline on 2026-10-21: a non-null `OpennessGroupMembership.ErrorMessage` now means membership could not be verified, and doctor reports Warning `Group membership could not be verified: <message>` with the message in evidence `error`. "Does not exist" (Failed) is reported only when both group-name forms are unmapped (`IdentityNotMappedException`); another lookup failure on one form no longer hides a mapping by the other. Redundant catch clauses were removed. `ApplicationInfoService` and process enumeration are unchanged, as the verifier recommended.
    - Regression evidence: the unverifiable-membership check case failed before the fix (Failed instead of Warning); three cases cover the name-form classification. All 236 Diagnostics cases passed. Independent review found no issues.
    - Full offline evidence: 7,189/7,189 tests passed with no skips; CI-scoped line coverage is 94.86%. The serial stub Release build passed with the seven existing test-assembly nullable warnings. The run used process-local `TEMP`/`TMP` under ignored `TestResults/b12/temp`. No live TIA verification is needed: doctor does not attach to TIA Portal, and only its error-path classification changed.
13. **Transport lifetime.** `Dispose` is unsynchronized, `SendAsync` has no disposed check, the timeout race is hand-rolled, and the client semaphore is never disposed. Do not touch `TiaPortalProcess`. (HOSTCORE-11; S)
14. **Startup accepts any configured project path.** A relative or wrong-extension `--project`/env path is accepted and resolved against the client's working directory. Fix: one shared `Validate(path)`. (HOSTCLI-2; S)
15. **Publish workflow.** It packs without running tests, runs restore/pack without `-m:1`, and interpolates `inputs.version` into pwsh text. Fix: pass inputs via `env:`, and add a build and test step or `needs: ci`. (HOSTCLI-12; S)
16. **Network reads absorb Portal loss.** A broad `catch (Exception)` swallows `NonRecoverableException`. Fix: one `OpennessExceptions.IsRecoverable` filter. Lands with Q3. (WORKERNET-7; S)
    - Implemented with Q3 on 2026-10-19: shared fatal-exception filtering, central `portal_lost` mapping, original fatal inventory exception preservation and host recovery regressions. Existing typed write outcomes and cleanup/stamping contracts remain as agreed. The maintainer accepted scoped live testing as finished on 2026-10-20; the [implementation record](2026-10-19-worker-portal-loss.md) retains offline evidence, candidate-specific live observations and untested limits.

## Workstream C: consistency across tools (contract changes; one tool-surface PR)

- **C-0 Bound-project-only content tools (Q6).**
  - Delete `IOperationBatchItem.ProjectPath`, the `projectPath` field specs in the 8 catalogs, the 4–5 same-project checks and most of `WriteProjectPaths`.
  - Content tools resolve only the session binding. A configured startup `--project` still counts, because it is grounded by `get_project_status` first. With no binding, `TryResolveWithSnapshot` (ProjectSessionBinding.cs:144-156) currently either dispatches the caller's path or falls back to the Portal's open project; instead it should fail closed.
  - This also fixes HOSTDOMAINS-14 and simplifies the project-tree cursor "repeat the same project" rule.
  - Fix the tool descriptions that say "If omitted, uses the project currently open in TIA Portal" (CrossReferenceReadTools.cs:32, ProjectEngineeringTools.cs:18, ProjectReadTools.cs:45).
  - Ships as a new MINOR release (maintainer decision, 2026-10-17). Callers that already omit `projectPath` see no change. Live-verify a standalone project, a local session and an unbound session. Do C-0 before C-1, so the strict key sets are derived from the final schemas.
- **C-1 Root-argument strictness for all 19 tools.** One `CreateClosedTool` helper derives the allowed and boolean keys from the tool's input schema. Delete `NetworkWriteArgumentValidatingTool`. 12 tools currently drop unknown keys silently; for example, `plc_read({projectPath: other})` silently reads the bound project. Apply one JSON-null rule for optional booleans. (TOOLS-1, TOOLS-2, TOOLS-13; M; depends on Q4)
- **C-2 One batch rule set.** Add `OperationBatchRules` in OperationBatches: 50 items, 256-character IDs, a single-project check via `WriteProjectPaths.Normalize`, and one message wording. Today there are 6 copies, and VCI/HMI-write hard-code `50`/`256`. Add one `OperationValidation` result record. (TOOLS-3, HOSTDOMAINS-2, TOOLS-4; M; depends on Q6)
- **C-3 Failure-category constants.** Add `WorkerFailureCategories.PreconditionRefusals` and `TransportLoss` sets. Replace the raw `"portal_lost"` and similar literals in the HMI and VCI verifiers, `HmiWorkingState` and `HmiWritePlanner`. Keep VCI's whole-batch verification, which is deliberate, and comment why. (TOOLS-9, HOSTDOMAINS-13, CONTRACTS-4; S)
- **C-4 Non-interactive guard rule for all four data-write domains.** Network and PLC enforce "no acknowledge guards"; HMI and VCI do not. Fix: `WriteGuardCatalog.NonInteractive(...)`, or enforce it in the pipeline from `ConfirmsEveryCall=false`. (HOSTCORE-13; S)
- **C-5 Sealed `list_network_objects` cursor.** It is the only paged read whose cursor is unsigned and encoded by the worker. Fix: move it to host sealing with `AuthenticatedCursorProtector`, as hardware pages already do. (WORKERNET-5; M; wire change for cursor values only)
- **C-6 Shared cursor plumbing, no behavior change.** One `Open`/`Seal` helper with a single `CursorException(category)`, shared `IsValidHostBinding`/`SameProject`/`IsLowerSha256`. Each codec keeps its documented foreign-process category. (HOSTCORE-12, HOSTDOMAINS-9; S)
- **C-7 Cancellation.** Pass the MCP `CancellationToken` through the four batch read tools, or delete the dead token parameters. (TOOLS-12; S)
- **C-8 Worker method-name lists.** Add `HmiReadMethods.All`. Register HMI through loops in `OperationPolicyCatalog`, as VCI is. Use guard patterns in the worker switch. (WORKERCORE-8, CONTRACTS-3; S)
- **C-9 Paging vocabulary.** `pageSize`, `limit` and `offset` differ, and the defaults are 50 vs 100. Short term: a paging matrix in docs/SupportedOperations and network's cursor-only default set to 100. Rename only at a major contract bump. (TOOLS-11; S now, L later)
- **C-10 `update_tag` `isSafety`.** The parameter is advertised but always fails in the worker. Remove it from the spec end to end, so host validation rejects it. (WORKERDOMAINS-9; S)

Verification: schema snapshot and conformance tests, then a scoped live smoke test of one read and one write per domain in read-write and full modes.

## Workstream D: host structural consolidation (no wire change)

Each item is one PR. D-2 uses existing write response records and can proceed independently of D-1; the D-1-first ordering remains for the other record-based consolidations.

- **D-1 Generic batch read tool.** `BatchReadTool.RunAsync<TItem>` plus one `OperationBatchReadResponse` and one error record replace four pasted skeletons (PLC, Network, HMI, VCI). Expose the effective access mode once. (TOOLS-5, HOSTDOMAINS-3; M)
- **D-2 Generic guarded-write budget.** `GuardedWriteResponseBudget` in Safety/Pipeline, with domain hooks for the roots each domain can omit. The HMI and VCI copies are verbatim; PLC and Network differ only in those roots. (HOSTDOMAINS-1; M)
  - Implemented and offline-qualified on 2026-10-21: shared canonical sizing, compaction bookkeeping, item/document omission stages, trusted-status restoration and protected-core construction; typed adapters preserve PLC diff/import-outcome priority, HMI cleanup messages, Network diagnostic/recovery policies and all domain guard reserves. Network's prepared reservation calculations remain unchanged. All 7,185 tests passed with zero skips; CI-scoped line coverage is 94.32%, and all 141 changed executable production lines were covered. Differential comparisons matched the B-10-corrected baseline; independent review found no actionable issues. The [implementation record](2026-10-21-guarded-write-budget.md) retains commands, warnings and evidence scope. No live TIA verification was performed.
- **D-3 Generic write-domain records.** `PlanResult<TEffect>`, `ReplanResult<TEffect>`, `WriteCall.FromItems` and `WriteValidation.Compose`. `PlcWriteDomain` and `HmiWriteDomain` are line-for-line twins. (HOSTDOMAINS-4, TOOLS-10; M)
- **D-4 Shared payload-contract failure path.** `StructuredOperationItem.Failed(...)` plus one projection helper for the protocol-error diagnostic. (HOSTDOMAINS-5; M)
- **D-5 `network_read` through DI.** Inject `NetworkReadOperationExecutor`. Delete the static `ConditionalWeakTable`, the compatibility cursor codec and `RegisterExecutor`. (TOOLS-7, HOSTDOMAINS-8; S)
- **D-6 Split `OpennessWorkerClient` (2,557 lines).** First deduplicate the pinned-binding staleness check, which appears 4 times with stale "after preview" text, and the configured-project grounding, which appears 3 times. Then split by concern: binding, lifecycle and per-domain wrappers. (HOSTCORE-3, then HOSTCORE-15; M, then L)
- **D-7 Smaller host items:**
  - `StructuredStandaloneResult` dispatches on tool-name strings. (TOOLS-8)
  - `WriteRun` hard-codes a lifecycle type check. (HOSTCORE-9)
  - The transport parses each response up to 3 times and runs VCI checks. (HOSTCORE-10)
  - Audit provenance uses bare literals. (HOSTCORE-14)
  - Network files over 1,000 lines. (HOSTDOMAINS-12)
  - The SHA-256 hex helper is duplicated. (HOSTDOMAINS-10)
- **D-8 CLI:**
  - `--flag value` parsing is copied 4 times; use `StartupOptionReader.ReadSingle` and one `StartupOptions.Parse` shared by server and doctor. (HOSTCLI-3, HOSTCLI-11)
  - The four installer classes, their interface and the registry become one `ClientProfile` table. (HOSTCLI-5)
  - The COMSPEC lookup is copied 3 times. (HOSTCLI-7)
  - Doctor checks repeat discovery and report one root cause several times. (HOSTCLI-10)
  - The worker companion-file list is kept in 3 places. (HOSTCLI-13)
  - Doctor formats booleans inconsistently. (HOSTCLI-15)

## Workstream E: worker structural consolidation

- **E-1 One open-project gate.** `ProjectOpenGate.Require(...)` replaces 3 copies of the `ProjectOpenPolicy.Decide` switch. Two of the copies hard-code `ReadWrite`; pass the real mode everywhere. (WORKERCORE-6; S)
- **E-2 One active-context wrapper.** `WithActiveContext` also requires the Portal handle. Delete `WithSubnetLifecycleProject` and route the subnet handlers and `WriteVci` through it. Clarify the AGENTS.md sentence about `WithEngineeringRoot`. (WORKERCORE-7; S)
- **E-3 Network Openness read helpers.** One `NetworkOpennessRead` helper with a single exception filter replaces 5–6 private copies across HardwareConfigReader, NetworkObjectIndexReader, CommunicationConnectionReader, HardwareIoMapReader and the resolver. Share the device-name filter; preserve the distinct enumeration policies per the verifier-corrected WORKERNET-13. (WORKERNET-1, WORKERNET-8, WORKERNET-13; M). Do this before B-4, which uses it.
  - Implemented on 2026-10-19: shared typed identity, optional scalar/enum and exact attribute reads, ordinal diagnostic deduplication, and hardware device-name filtering. Recoverable `EngineeringException` values degrade with diagnostics; `NonRecoverableException` and programming errors propagate from the helper. Negative identity numbers remain unusable. Page descriptor traversal still fails closed; attribute inspection still isolates faults; catalog enumeration stays best effort. B-4, B-16 and Q3 remain separate work, including the outer catches and worker-wide `portal_lost` mapping. No live TIA verification was performed.
  - Offline evidence: 6,954/6,954 tests passed with no skips; serial stub and installed-V21-reference solution builds passed with five existing HMI nullable warnings. CI-scoped line coverage is 16,003/16,968 (94.31%), passing the 80% gate; the shared helper has 94/94 executable lines and 38/38 branches covered. Independent review found no remaining issues after updating the moved source-contract assertions. Coverage collection required process-local `TEMP`/`TMP` under ignored `TestResults/network-e3/temp`; the default sandbox temp directory denied Coverlet hit-file moves, so its zero-hit reports are invalid evidence.
- **E-4 Typed Openness calls.** Replace the reflection `InvokeFirstAvailable` "UNVERIFIED SDK CALL" connect helpers with typed `ConnectToSubnet`/`ConnectToIoSystem` calls after checking the V21 API. Add both to the reference stubs. (WORKERNET-9; S, plus an SDK documentation check)
- **E-5 Shared network constants.** `NetworkVerificationStatuses`, `NetworkSettingKeys` and subnet type identifiers in Contracts, shared by worker and host. (WORKERNET-10; S)
- **E-6 Split `Program.cs` (1,644 lines).** Move about 850 lines of Network and PLC handlers into `NetworkWorkerDispatch` and `PlcWorkerDispatch`, mirroring HMI and VCI. Merge the two format normalizers. Do this after Q5 and Q1. (WORKERCORE-9; L)
- **E-7 Domain helpers:**
  - One software-discovery walker, starting with the targeted strict-failure fix for CrossReferences and ProjectTree. (WORKERDOMAINS-2)
  - Shared `FindSoftwareUnit` and group-path walk between the Block and PlcType resolvers. (WORKERDOMAINS-3)
  - Tag helpers copied into `TagMutationService`. (WORKERDOMAINS-8)
  - Block node-type mapping copied 3 times. (WORKERDOMAINS-13)
  - Compile constants and serializer options duplicated. (WORKERDOMAINS-14)
  - `VciWriter.Write` is one 190-line method. (WORKERDOMAINS-17)
  - Optional HMI group-match alignment. (WORKERDOMAINS-12)
- **E-8 Session and hygiene:**
  - `TiaPortalSession` has a dead overload and duplicated connect guards. (WORKERCORE-10)
  - Multiuser inventory validates selectors twice and attaches to the Portal 3 times per request. (WORKERCORE-11)
  - Access mode is parsed twice, ReadWrite and Full presets are identical, one exception is reported twice, and there is a non-English comment. (WORKERCORE-12)
  - Large Network files. (WORKERNET-11)

Verification: unit tests, then a live regression of each touched domain against a disposable project copy. Per memory, never dispose `TiaPortalProcess` and never write project names into the repo.

## Workstream F: Contracts, FakeWorker and tests

- **F-1 Retire `LegacyNullOmission`** (after Q2). (CONTRACTS-2; M)
- **F-2 Move `ProjectSessionBinding` into the host.** It is host-only mutable state that lives in Contracts. Add it to the test csproj Compile Include list. Update the AGENTS.md wording, which calls Contracts "shared DTOs" although it also holds worker-only logic. (CONTRACTS-6, CONTRACTS-7; S)
- **F-3 Split `FakeWorker/Program.cs` (4,201 lines, ~25 mutable globals).** Extract Network, Lifecycle and Block scenario classes, following the existing `PlcWriteRoundtripScenario`. Use one response builder. (CONTRACTS-8, CONTRACTS-9; L)
- **F-4 Test infrastructure:**
  - One repository-root/source-file locator; there are about 16 copies using 3 strategies. (CONTRACTS-11)
  - One canonical-document reader and one `CreateClient`; there are about 15 and 17 copies. (CONTRACTS-12)
  - Merge TestUtilities into TestSupport, and make `FakeWorkerLocator` prefer the current configuration over a stale Debug build. (CONTRACTS-13)
  - Effort S–M each.

## Suggested PR order

1. **A** (dead code) as one PR, then **B-1**, **B-2** and **B-3** (CLI and compile defects) as three separate PRs, so each fix gets its own release-notes line. Small and safe; B-2 needs a short live check.
2. **E-3 + B-4 + B-16 + Q3** (network read helpers, unreadable candidates, worker-wide `portal_lost`). Needs live verification.
3. **B-5 to B-15**, grouped by area.
4. **C** (tool surface contract), after Q4 and Q6. Docs last, after live acceptance.
5. **D-1 → D-4 → D-5**, then **E-1, E-2, E-5**.
6. Large splits: **D-6, E-6, F-3**. Then **F-1, F-2, F-4**.
7. Retire the probes (Q1) when its trigger is met.

## Limits of this review

- Static only. No finding was reproduced by a failing test or a live run. Workstream B items need a red test first.
- The partly confirmed findings use the verifier's corrected scope. A few cited line numbers were off by a few lines; the appendix has the corrected locations.
- jCodemunch does not index `.md`, `.csproj` or `.py`. Reviewers read docs and csproj files directly. The git-ignored harness scripts were only noticed via `.gitignore`.

