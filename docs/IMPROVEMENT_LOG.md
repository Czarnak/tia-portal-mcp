# Improvement Log — tia-portal-mcp

Open engineering follow-ups for this project, started 2026-07-15. Resolved items are removed.
Historical implementation, qualification and acceptance records remain indexed in
[the documentation map](README.md). Workstream C follow-up merge identities and verification
are recorded in the [orchestration ledger](superpowers/plans/2026-10-24-workstream-c-followups-orchestration.md#completion-ledger).
An accepted implementation does not establish unexecuted native or plant acceptance.

## Open: VCI follow-ups (2026-10-16)

The [VCI reference](SupportedOperations/VCI_OPERATIONS_SUMMARY.md) documents the delivered
standalone-only surface and the [live report](superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md)
keeps the frozen candidate's failed verdict separate from the repaired candidate's scoped pass.
The following remain open:

- Library scopes and `connect` for unlinked workspace files; workspace groups and VCI settings.
- Local-session support, software units and system blocks. Mapping inventory may show excluded
  mappings, but current write scopes cannot select those owners.
- MCP progress notifications for long operations.
- Consolidating the VCI path policy with the existing private path guards only after their
  reserved-name and containment rules can be reconciled without weakening them.
- Export scan growth: the explicit collision checks scale with leaves × mappings, and native
  `Find` complexity is unknown. The bounded three-leaf follow-up passed; there is no established
  whole-project throughput or scan-reduction fix. Preserve the late-collision barrier while
  investigating performance.
- UI layout parity (issue #145) was observed for S7-1500, ET 200SP, a Unified panel, a generic
  PC station and an IPC. PC stations are recognized by device type pattern only; other PC-based
  families, devices inside project-tree device folders, and non-English workspace-language
  category names (`Program blocks`, …) remain unverified against UI exports.
- Native `SimaticSD` exporter crash, abrupt-crash recovery timing, Unknown status, thrown-leaf
  rollback, concurrent live remapping/status races and current-candidate HMI import/runtime
  behavior remain unqualified within the live report's stated scope.

## Open: large PLC block follow-ups (issue #127, 2026-10-15)

The value and response budgets became startup options (`--max-item-chars`/`--max-document-chars`,
`TIA_MCP_MAX_ITEM_CHARS`/`TIA_MCP_MAX_DOCUMENT_CHARS`; raise-only, defaults 60,000/180,000, at most
1,000,000), so a block above 60,000 characters can be read with its `contentHash` and written back.
Not yet live-verified; the issue reporter offered to test. Left out by design:

- **Paged content reads and partial writes.** The issue's options 2 and 3 (`plc_read` slices that
  carry the whole-document hash; a range or patch variant of `update_block_logic` reassembled on the
  host) remain open. Revisit only if clients without file access still hit their own tool-result
  or argument limits on real blocks.
- **Doctor and installer.** `tia-mcp doctor` neither accepts nor reports the budget options, and
  `tia-mcp install` does not write them; users edit the client configuration by hand.
- **Fixed limits.** Project-tree pages, `network_read` hardware pages and the worker compile report
  keep their own 60,000-character limits.
- **Escaping inflation.** `CanonicalJson` uses the default encoder, so `'`, `"`, `<`, `>`, `&`, `+`
  and non-ASCII characters count six characters each; SCL and XML exports measure noticeably larger
  than their text. A relaxed encoder would shrink them but changes delivered-document hashes in the
  audit; it needs a contract review.

## Open: create_block OB follow-ups (issue #75, 2026-10-09)

Left out of [OB creation](superpowers/specs/2026-10-08-ob-creation-design.md) by design; none blocks
the shipped behavior.

- **Renumbering.** No `set_block_number` operation exists: a created OB keeps the number the server
  picked, and a wrong or unwanted number needs TIA Portal. Add it when a workflow needs caller-chosen
  or changed OB numbers.
- **OB event parameters.** Created OBs use TIA defaults. `CyclicInterrupt` cyclic time and phase
  offset, priority, the `TimeOfDay` schedule and `HardwareInterrupt` triggers cannot be set (the
  live compile of a new hardware-interrupt OB reports the "no triggers defined" warning).
- **`SynchronousCycle`.** Not offered: it does not compile on any CPU that accepts it until an
  isochronous IO system is assigned, which no tool here can configure.
- **Live evidence limits.** The S7-1200 G2 PLC was not exercised in the 2026-10-09
  [live acceptance](superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md); the
  Phase 0 spike covered it by import only.

## Open: Multiuser successor operations

PR 2 implements the internal active project context and typed lifecycle owners on the merged
PR 1 foundation. Its frozen standalone candidate is offline qualified and live accepted within
the bounds in the [acceptance report](superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md).
PR 3 implements six explicit discovery/inventory actions through `bind_project` without a new
tool or binding mutation. Its combined offline qualification is recorded in the
[PR 3 report](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md).
PR 3 live acceptance was subsequently finished and passed by maintainer decision on 2026-10-06;
the [live report](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md)
preserves executed observations and non-blocking unexecuted cases. Successor Multiuser operations remain open.
The [Multiuser boundary](SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md) distinguishes
this partial delivery from Issue #65 completion. PR4 now delivers exact `.amc21` adoption,
basic local status/startup assertions and explicit `.als21` opening. Its
[October 8 report](superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md)
records offline qualification and scoped maintainer live acceptance; both noninteractive
offline read-write opens failed because Siemens dialogs required dismissal. Full offline,
headless/race and other matrix rows remain unexecuted. PR5 delivers local-session content reads
and fixes the unmapped `hmi_*` capability check that refused `hmi_read` for every container; see the
[October 9 report](superpowers/acceptance/reports/2026-10-09-multiuser-pr5-live-verification.md).
PR6 delivers local-session PLC/Network writes, `compile_check` and `LocalSession.Save()` through
`save_project`; the
[PR6 report](superpowers/acceptance/reports/2026-10-09-multiuser-pr6-live-verification.md)
covers Multiuser and Exclusive sessions in `full` mode. Read-write local-save confirmation,
worker-opened and offline sessions are unexercised live. Markings, terminal actions and server
mutation remain later work. Preserve
`bind_project`, host binding ID/revision, configured-selector checks, detach ownership, guarded
lifecycle confirmation, and audit v2. Capability descriptions remain descriptive, not permission.

## Open: Reliable local-session distinction for one server project (2026-10-07)

The PR 4 prerequisite probes distinguished two observed local owners by their different typed
`LocalSession.Project.Path` engineering `.amc21` paths. This does not establish a durable local
session ID or a supported join to Project Server inventory. Qualify a supported owner/session
identity for different local sessions associated with the same server project before relaxing
PR 4's fail-closed handling of same/ambiguous paths, owner replacement, or unknown remote identity.
An explicit `.als21` opener remains provenance; filenames and directory hierarchy establish
neither cold-adoption identity nor engineering/exclusive mode.

Distinguishing multiple sessions bound to the same server project remains future work;
PR4's observed AMC paths and successful opens do not close this follow-up or Issue #65.

## Open: totally-integrated-claude tia-portal-mcp skill migration

Update the plugin's source `tia-portal-mcp` skill in a separately authorized plugin change to teach
the current 8/19/19 tool counts (`plc_read` and `read_cross_references` replace the retired `execute_read_batch`;
`plc_write` replaces `preview_write_batch`/`apply_write_batch`, with `content` plus
`expectedContentHash` instead of `yamlContent`/`sourceContent` and no safety token; full adds no
tools and PLC run/stop is gone), `bind_project` for already-open projects, no implicit opens, per-call
read-write lifecycle confirmation, full policy confirmation, removed startup switch/agent
confirmation arguments, audit v2, and binding-scoped tree cursors. Preserve the distinction between
functional live evidence and human dialog observations. This repository task changes no installed
plugin cache or user configuration.

## Open: PLC read and cross-reference follow-ups (PR A)

Found while finishing [PR A](superpowers/plans/2026-10-05-plc-read-and-cross-references.md); none blocks it.

- `totally-integrated-claude` skill: tracked in the skill-migration entry above.
- `list_tag_tables`: when `tableName`/`folderPath` match nothing but part of the tree was unreadable, the incomplete inventory is returned without an explicit "not found among readable tables" message.
- `read_cross_references` budget: the trim never recurses inside a kept child; a bare source over budget (pathological names or messages) is still withheld by the renderer; the standalone fallback guidance omits `filter`.
- `read_cross_references`: a `maxResults` cut does not count into `omittedSourceCount`; the shared incomplete message mentions "unused-object audit" for every filter.
- `read_cross_references`: legacy `referencedAsName` is kept beside `referencedAs` (it carries call-site text when `referencedAs` is null); decide whether to keep it.
- The Plc protocol diagnostic duplicates Network's private `TryWriteProtocolDiagnostic` (missing `validators` field, 512 cap); share one helper.
- `CrossReferenceTargetResolver.MatchOne` duplicates `ProjectTreeFilter.ResolveOne`'s predicate (drift is pinned by a round-trip test).
- Unverified cross-reference owner kinds (spec Appendix B): technology objects are verified but not shipped; TO instance DBs and Classic HMI tags were not present in the spike project. Unified `HmiTag` shipped on 2026-10-12 through the provisional `{hmi}` selector.

## Open: hmi_read follow-ups (2026-10-12)

Found while building and live-accepting [`hmi_read`](superpowers/specs/2026-10-07-hmi-read-design.md)
([live acceptance report](superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md)) and
its [completion track](superpowers/specs/2026-10-11-hmi-read-completion-design.md)
([live acceptance report](superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md),
observations O1-O6); none blocks it. Known limits are also listed in the
[HMI operations summary](SupportedOperations/HMI_OPERATIONS_SUMMARY.md#known-limits). Items the
completion track closed remain in its linked acceptance report.

- **Every item re-locates its HMI (O1, open).** The historical large-project baseline measured about
  6 s per `validate screenItems` item and 20.6 min for 180 screens. The HMI-write track retains locator
  entries, but cache hits still freshly traverse for current project membership and selector
  uniqueness. Entries clear on next locator use after an owner change. The planned traversal saving
  is not delivered; a safe invalidation/reuse design and measured benefit remain follow-ups.
  The maintainer explicitly chose "Retain current cache; leave O1 open." The
  [acceptance report](superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md) keeps
  archival-baseline performance observations separate from functional acceptance; strict matched
  write comparisons and repeated owner-transition gates remain unqualified, and the single-trace
  10% memory-return condition was not met.
- **`get_tag` dotted-name hint (O2).** `get_tag` with a dotted UDT member name fails
  `target_not_found` without the "UDT member paths are not supported" hint the `{hmi}`
  cross-reference selector gives.
- **HMI-write scope follow-ups.** Connection creation/edit, acknowledgment tags, `EventText1`–`9`
  and alarm-text parameter fields remain unsupported. Existing-tag rebinding that introduces a new
  member followed by an alarm on that member in the same call is conservatively rejected before
  mutation; use binding update, read tag/members, then another call. Extend update-dependent member
  planning only with fresh proof before alarm mutation. Direct case-only tag rename can fail at the
  Siemens setter; caller-owned intermediate-name renames are explicit operations, not server magic.
- **`open_project` dry run misses an open project (O3, pre-existing).** A dry run while another project
  is open in the same Portal reports no guard and `willCloseSource:false`; the actual call then fails
  `worker_operation_failed` "Another project is already open."
- **Slowdown and worker crash with a validation error in the project (O6, unexplained).** With a
  broken tag binding in the project, every Openness call slowed sharply (one-screen `validate
  screenItems` 172-189 s instead of about 6 s), and a 21-screen batch ended `worker_crashed` on its
  second screen; run alone, that screen succeeded. Hypothesis (not established): an unfinished
  background compile.
- **Last page keeps its snapshot.** A cursor continuation that reaches the last page leaves its
  snapshot in the worker store until LRU eviction or the 10-minute idle expiry, so it still takes one of
  the 4 slots.
- **Worker `{hmi}` resolver trusts the host.** `CrossReferenceTargetResolver` passes `hmiName` to
  `HmiSoftwareLocator.FindUnique`, where null means "the only HMI"; the required, nonblank `hmiName`
  is enforced only by host validation. Reject a null `hmiName` in the worker as well.
- **Non-integral `Double` not observed live.** A tag's non-integral initial value arrives as
  `System.String`; the explicit `G15`/`G17`, `G7`/`G9` encoding is still covered offline only.
- **Live evidence gaps.** Non-default connection drivers (no OPC UA connection was available), live
  validation warnings, non-default Object values, and a held object deleted between two pages
  (covered offline by the snapshot store tests).
- **Redaction coverage for unseen drivers.** The secret-name denylist (`password`, `passwd`,
  `passphrase`, `pwd`, `secret`, `token`, `credential`, `privatekey`) is inference, not observed: only
  S7-1200/1500 drivers were seen live. Check OPC UA and third-party drivers' property names and
  `InitialAddress` forms (for example credentials embedded in a URL) when such a project is available.
- **Portal exit after detach.** In spike run 2, TIA Portal exited about 4 s after the worker detached.
  The cause (a user close, or an exit on detach) was not determined and it was not reproduced; a
  possible crash-on-detach hazard to watch for.

## Open: plc_write follow-ups (PR B, 2026-10-07)

Found while building and live-accepting [PR B](superpowers/plans/2026-10-06-plc-write.md)
([validation report](superpowers/acceptance/reports/2026-10-07-plc-write-validation.md)); none
blocks it. Known limits are also listed in the
[PLC operations summary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md#known-limits).

- **Software-unit blocks counted in the CPU namespace (M2, confirmed false live).** `create_tag`
  named `SU_DB` is blocked as `plc_name_collision` against `PLC_LAD/Units/Test_SU/Blocks/SU_DB`.
  The maintainer confirmed that unpublished unit blocks cannot be referenced outside the unit. It
  fails closed. Exclude unpublished unit blocks from the CPU namespace, and check the related
  worker minor where a unit-scoped create checks the top-level PLC namespace instead of the unit's.
- **Per-target inventory completeness (D7).** Completeness exists only at the inventory root, so
  one unreadable table or device item makes `plc_state_unverifiable` fire for every item in the
  call. Track completeness per PLC/table so unrelated targets can plan.
- **User-constant null-value conflation.** An unreadable constant value is reported only as
  `Value == null`, which cannot be told apart from a genuinely empty value; update and delete of
  such a constant block conservatively. Add an explicit readability marker to the worker payload.
- **`plc_read` content reads became unique-PLC (D5).** The shared block/type target resolvers now
  use `FindUnique`, so a content read on a project with several PLCs and no `plcName` fails
  `target_ambiguous` instead of reading the first match. Intended (the read hash and the write check
  hit the same object); record it in release notes.
- **Network write messages still use the unconditional 512-character compaction** that `plc_write`
  dropped. Align Network with the PLC budget, which omits only whole values.
- **Guard reservation ceiling.** `MeasureProtectedCore` reserves two guards per item, but a PLC item
  can fire about four, so `Compose` could throw after mutation. Network has the same ceiling.
- **Diagnostic strings are not counted by `AnyOverItemLimit`.** One diagnostic over 60,000
  characters inside a document under 180,000 is delivered intact instead of omitted.
- **Result echo differs from the effect.** `create_block` reports `blockType:"GLOBALDB"` and a group
  path without `Blocks/` in its result, unlike the effect. `removes.blocks` order is
  enumeration-dependent between a dry run and the apply.
- **Pipeline note: late acknowledge guards.** A late acknowledge guard identical to one the user
  already accepted still blocks the dependent item in read-write (`GuardDecisions.DecideLate`). No
  PLC guard is acknowledge-level today, so `plc_write` is unaffected.
- **plc_write review minors (Tasks 1–8),** one line each:
  - `PlcNameRulesTests` lacks the "same name, different kind/container, self passed" collision case.
  - `PlcSoftwareLocator` keeps two near-identical device walkers and a duplicated name filter.
  - `RequireReadableValue`'s `EngineeringException` branch and nested system-block group recursion are untested.
  - No behavioural test drives `BlockImporter`/`PlcTypeImporter` into a hash mismatch; the `IndexOf` source-order contracts are brittle.
  - `RequireNameUnoccupied` uses Siemens `Blocks.Find` (case sensitivity unverified) but `OrdinalIgnoreCase` for groups.
  - Duplicated "No block exists at…" message; the XML path resolves the target twice.
  - `PlcTypeTargetResolver` throw sites are covered only by a source contract.
  - The created-earlier check keys on folder path (the planner catches the missed case).
  - `blockImportOutcome` on failures sits outside the document-budget drop order.
  - `ConditionalMemberRegisterTests` scans one host type by hand.
  - `Apply` re-runs `Resolve` (double resolution per item); `RequireTable` is called twice in one case.
  - The `"Type"` kind is a string literal instead of a `PlcNameRules` constant.
  - The `StaleContentHash` test's "before any write" assertion can never fail.
  - `HiddenAt` is over-conservative: a skipped sibling makes a miss in a fully read group unverifiable (block, never a wrong not-found).
  - Effect-presentation omission reason is ambiguous (diff versus whole effect).
  - `VerifyStructural` reports `actual:"absent"` for `target_ambiguous`.
  - The `uiOpen` list was not edited for the `plc_write` probes (default branch used).
  - Rename `Start`→`Go` then create `Start` supersedes the rename on the shared key, so `Go` is never verified; verifier and planner supersession keys differ.
  - `WorkerTransportFailureGuidanceTests` rows for retired names only test the unknown-operation path.

## Open: Deeper project-tree resolver optimization (Issue #32 follow-up)

The user accepted the combined offline and read-only live v3 evidence on 2026-09-12, with an
explicit live ambiguity waiver. The
[accepted report](superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md)
records the timing distributions, multi-page completion, evidence boundaries, and waiver.

The shipped v3 initial browse performs one net48 typed/scoped walk, then strict host decode,
post-filter flattening, bounded immutable snapshot storage, and canonical page projection;
continuations use the authenticated process-local cursor and cached snapshot with zero worker calls.
A deeper direct Openness selector resolver and depth-pruned walk is a measured follow-up only. It is
not shipped behavior and should proceed only if additional measurements justify its benefit without
changing the approved public contract. Acceptance of the current implementation does not complete
this optimization.

## Open: Network Phase 5 public IO-system editing

PR 2's 2026-09-26 guarded worker-only V21 matrix verified exact PN/DP controller interface
owners, their distinct master PLC hardware compile targets, baseline compiles, and one-field
edit/restoration evidence. The [qualified contract](superpowers/specs/2026-09-24-network-phase5-qualified-contract.md)
admits PN/DP `Name` and `Number` plus PN `UseIoSystemNameAsDeviceNameExtension` for later
public planning. PN `MultipleUseIoSystem` remains excluded after linked PN-name evidence became
unavailable following a committed edit; `MaxNumberIWlanLinksPerSegment` remains unproved.
The [acceptance report](superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md)
records two reverse `Name` runs whose worker edit/compile succeeded but whose final harness
status failed solely on transient project-size drift, and the clean close-without-save/reopen
result. No failed hardware compile was induced. `update_io_system` remains unshipped. The next
public slice still needs a fresh plan from merged `main`, strict TDD, the canonical safety and
audit gate, and its own live public-path acceptance.
The live Portal matrix ran at `03bf387`, not at the later branch HEAD. After
the post-live host/test project-reference and CI changes, no `.cs` or `.ps1`
file changed; the rebuilt dependency manifest still matched, although five
of 53 core PE files differed. The user accepted the changes as having no live
impact. A fresh full coverage run passed 3,371/3,371 at line rate 0.9409
against the 0.80 threshold, and both serial builds passed with zero errors
and seven existing warnings. PR 2 is merge-ready by the explicit
source/behavioral-equivalence carry-forward, not by binary identity or a
current-HEAD Portal rerun. No merge was performed.

## Open: JSON contract Phase 1b follow-ups

These came out of the Phase 1b final review ([plan](superpowers/plans/2026-09-28-json-contract-phase1b-required-members.md)).
None of them blocks the merge.

- **Tag-match null path (predates Phase 1b).** `TagTableReader` → `HardwareTagIndexResolver` →
  `HardwareIoMapReader` pass Openness strings unguarded into the non-nullable `IoTagMatchInfo`
  members `Name`, `DataType`, and `TableName`. A null from Openness makes the whole
  `read_hardware_config` result `protocol_error`. That fails closed, as before the branch. A worker
  fallback with a message would keep the read usable.
- **Worker version mismatch.** The host now requires the network payloads to write explicit
  nulls, so the payloads of a worker built before Phase 1b are rejected as `protocol_error`. Host
  and worker ship in one package, so this fails safe. An optional protocol capability would turn
  it into an explicit version-mismatch message.
- **`update_subnet` null or blank read-back name.** If Openness reads back no name after the update
  has committed, the host rejects the result as `protocol_error`. `postcondition_failed` would
  describe it better, but that is new behavior and was kept out of Phase 1b.
- **File sizes.** The original review flagged `NetworkPayloadContract` and its tests for size and
  repeated full selector/evidence fixtures. Reassess the current split before choosing a refactor.
- **Test precision.** The five tag-match rejection cases in `NetworkIoMapPayloadContractTests` have
  no accepted control. `NetworkPayloadContractTests.Rules.cs` asserts that the validator appears
  anywhere in the diagnostic chain; asserting `chain[0]` would be tighter.

## Open: network_write live-defect follow-ups (2026-10-05)

Deferred by user decision while fixing the live `network_write`/`network_read` defects; none
blocks that branch.

- **A5 null `DeviceItem` TypeIdentifier in selectors.** TIA reports a null type identifier for some
  device items. The hardware read no longer reports it as a message; whether a null
  `typeIdentifier` is optional selector evidence is still undecided.
- **Device-scoped subnet scoping for `read_hardware_config`.** A `deviceName`-filtered read still
  returns every project subnet. The message fixes shrank the unfiltered test-project read from
  108,229 to 87,512 characters, still above the 60,000-character per-item limit. Measure before
  scoping subnets to the selected device.

## Open: earlier binding and test-maintenance follow-ups

Unresolved notes retained from the July reviews after removing their completed parent entries:

- **`ProjectPathNormalization` exception fallback.** Pin the trimmed-literal fallback when
  `Path.GetFullPath` throws. Its behavior differs between net48 (the worker) and net10.0 (host
  tests), so a host test alone cannot characterize every worker path failure.
- **FakeWorker scenario keys are literal Windows paths.** `projectPath` doubles as a dispatch
  key; a separate scenario key remains a possible test-harness cleanup.
- **`Stamp` stderr timing.** Its comment claims a diagnostic stderr write lands in the current
  response's warnings, which depends on drain timing. Verify or soften that claim.
- **Duplicate binding tests.** `DifferentProjectPathIsRejectedAfterBinding` and
  `TryResolve_StillRejectsADifferentPathOnceBound` still cover the same rejection path.
- **Direct batch dispatch.** The Phase 3 proposal to remove near-identical worker-client wrappers
  remains deferred; the [current review findings](superpowers/plans/2026-10-17-codebase-simplification-review-findings.md)
  describe the surviving worker-client duplication (HOSTCORE-15).

## Open: CI coverage and workflow follow-ups

- **Coverage follows namespaces rather than assembly identity.** The linked-source test project
  uses include-minus-exclude namespace filters, so a misplaced production class can be silently
  excluded and a misplaced test helper included. Consider a real stub-safe host project reference,
  or an interim guard rejecting test-bearing classes in the measured production scope.
- **Inline `- run:` YAML shorthand.** `CiWorkflowTests.ReadRunCommandBlocks` rejects the leading
  `- ` marker, so that syntax would bypass its serial-build enforcement. Current workflows do not
  use it; widen the parser if the style is introduced.
- **Duplicate repository-root helpers.** `CiWorkflowTests` and `CoverageThresholdScriptTests`
  repeat a four-level parent walk that assumes the standard `bin/<config>/<tfm>/` layout.
- **Nonfinite coverage rates.** `verify-coverage-threshold.ps1` accepts parsed `NaN`/`Infinity`;
  the less-than threshold check does not reject them. Real Cobertura output has not exposed this.
- **Sequential process stream reads.** `CoverageThresholdScriptTests.RunScript` reads stdout then
  stderr before waiting, which can deadlock if either buffer fills. Its present one-line output is
  small; switch to concurrent reads if output grows.
- **Threshold diagnostics use the current culture.** Cosmetic on the en-US CI runners; use an
  invariant format if those diagnostics become machine-consumed.
- **OS-adapter coverage.** The original review identified `EnvironmentVariableService`,
  `FileSystemService`, `ProcessEnumerationService`, `RegistryService` and `WindowsIdentityService`
  as future coverage targets. Re-measure before carrying forward the old zero-coverage claim.

## Open: test-suite runtime, possible improvements (2026-10-25)

Ideas left over from the [test-suite runtime design](superpowers/specs/2026-10-24-test-suite-runtime-design.md).
None is planned; each is a candidate to weigh against its cost and risk if the serial part of
the suite becomes a problem again.

- **Per-worker FakeWorker environment.** Most of the serial tail ("Real worker process" and
  "Mcp protocol serial") is serial only because FakeWorker reads its scenario from process-wide
  environment variables, which `FakeWorkerBinding` and the other helpers set. Passing those
  settings to each launched worker instead (for example an opt-in extra-environment parameter on
  `PersistentWorkerTransport`/`OpennessWorkerClient`) could let most of those classes run in
  parallel. It would touch about 50 test classes and the transport, so the gain needs to justify
  the change.
- **Guard blind spot.** `ProcessEnvironmentIsolationTests` recognizes `McpProtocolTestHarness`
  binding a startup project only from the named `startupProjectPath:` argument. No test passes it
  positionally today.
- **PowerShell scripted workers.** `NetworkObjectPaginationCoordinatorTests`,
  `LifecycleIdentityContinuityTests` and `WriteSafetyLeaseConcurrencyTests` stay serial for
  PowerShell start-up contention; replaying their scripts through FakeWorker could remove that reason.
- **Concurrent full suites.** One run peaks at about 1.4 GB, which limits running several full
  suites at once (for example in parallel worktrees).

## Open: native qualification and deferred scope

- **External attributes on `create_tag`.** Only `update_tag` accepts `externalAccessible`,
  `externalVisible` and `externalWritable`; `create_tag` rejects them. Qualify whether native V21
  creation can set them before adding support. Multilingual per-tag comment binding remains
  outside the earlier tag-scope acceptance.
- **Network reflection paths.** `NetworkDeviceConfigurator` still marks subnet and IO-system
  connection calls `UNVERIFIED SDK CALL`. Pin their supported V21 signatures and decide whether
  `deviceItemName` has a meaningful configuration use before changing that surface.
- **Block-path device qualifiers containing `/`.** The parser splits that character, so such a
  device name cannot qualify a block path; use the PLC software name. The grouped-PLC compile
  fix did not address this case.
- **Standalone canonical producer qualification.** The
  [Phase 2 offline report](superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md)
  did not claim full live producer compatibility. Later scoped status/compile observations do not
  establish unexecuted producer cases or plant acceptance.
- Authentication/authorization-event subscriptions, server-push/long-polling MCP notifications
  and an MCP-callable `doctor` remain deferred; `doctor` is CLI-only.
- Splitting `WorkerRequest` into per-operation DTOs remains deferred while the protocol is stable.
