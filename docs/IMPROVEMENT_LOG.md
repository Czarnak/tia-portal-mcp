# Improvement Log — tia-portal-mcp

Open engineering follow-ups for this project, started 2026-07-15. Resolved items are removed; the
latest completed entry stays at the end.
Historical implementation, qualification and acceptance records remain indexed in
[the documentation map](README.md). Workstream C follow-up merge identities and verification
are recorded in the [orchestration ledger](adr/0008-tool-surface-consistency.md).
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

Left out of [OB creation](adr/0001-plc-content-exchange-and-creation.md) by design; none blocks
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

Found while finishing [PR A](adr/0004-guarded-single-call-write-pipeline.md); none blocks it.

- `totally-integrated-claude` skill: tracked in the skill-migration entry above.
- `list_tag_tables`: when `tableName`/`folderPath` match nothing but part of the tree was unreadable, the incomplete inventory is returned without an explicit "not found among readable tables" message.
- `read_cross_references` budget: the trim never recurses inside a kept child; a bare source over budget (pathological names or messages) is still withheld by the renderer; the standalone fallback guidance omits `filter`.
- `read_cross_references`: a `maxResults` cut does not count into `omittedSourceCount`; the shared incomplete message mentions "unused-object audit" for every filter.
- `read_cross_references`: legacy `referencedAsName` is kept beside `referencedAs` (it carries call-site text when `referencedAs` is null); decide whether to keep it.
- The Plc protocol diagnostic duplicates Network's private `TryWriteProtocolDiagnostic` (missing `validators` field, 512 cap); share one helper.
- `CrossReferenceTargetResolver.MatchOne` duplicates `ProjectTreeFilter.ResolveOne`'s predicate (drift is pinned by a round-trip test).
- Unverified cross-reference owner kinds (spec Appendix B): technology objects are verified but not shipped; TO instance DBs and Classic HMI tags were not present in the spike project. Unified `HmiTag` shipped on 2026-10-12 through the provisional `{hmi}` selector.

## Open: hmi_read follow-ups (2026-10-12)

Found while building and live-accepting [`hmi_read`](adr/0009-hmi-read-contract.md)
([live acceptance report](superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md)) and
its [completion track](adr/0009-hmi-read-contract.md)
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
  and alarm-text parameter fields remain unsupported. HMI write 2.0 removes the host's member-binding
  refusal and its required second-call workaround; native assignment determines acceptance. A successful
  existing-tag rebind followed by a new-member alarm has not been demonstrated live. Direct case-only tag rename can fail at the
  Siemens setter; caller-owned intermediate-name renames are explicit operations, not server magic.
- **Read-side native error evidence.** `HmiReadLog.Guard` and `HmiTagLookup.FindUnique` preserve their
  earlier read messages (`describe=false`); write paths opt into typed `engineeringError` evidence.
  Qualify a separate read-contract change before exposing equivalent native details from `hmi_read`.
- **Delete-planning alarm inventory.** L4's four successful cross-reference reports alone identify all
  four expected alarm trigger users. Evaluate removing the additional alarm read from delete planning,
  retaining incomplete-evidence blocking and the initial 200-query cap. The current implementation
  still performs that read; one fixture does not prove every reference kind is covered.
- **Connection-only native assignment.** The public request rule still requires `plcTag` with
  `connection`. The underlying setter's behavior for a connection-only assignment remains unprobed;
  do not remove that request rule without a separate native probe.
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

Found while building and live-accepting [PR B](adr/0004-guarded-single-call-write-pipeline.md)
([validation report](superpowers/acceptance/reports/2026-10-07-plc-write-validation.md)); none
blocks it. Known limits are also listed in the
[PLC operations summary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md#known-limits).

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
  - `PlcSoftwareLocator` keeps two near-identical device walkers and a duplicated name filter.
  - `RequireReadableValue`'s `EngineeringException` branch and nested system-block group recursion are untested.
  - No behavioural test drives `BlockImporter`/`PlcTypeImporter` into a hash mismatch; the `IndexOf` source-order contracts are brittle.
  - Duplicated "No block exists at…" message; the XML path resolves the target twice.
  - `PlcTypeTargetResolver` throw sites are covered only by a source contract.
  - `blockImportOutcome` on failures sits outside the document-budget drop order.
  - `ConditionalMemberRegisterTests` scans one host type by hand.
  - `Apply` re-runs `Resolve` (double resolution per item); `RequireTable` is called twice in one case.
  - The `"Type"` kind is a string literal instead of a `PlcNameRules` constant.
  - The `StaleContentHash` test's "before any write" assertion can never fail.
  - `HiddenAt` is over-conservative: a skipped sibling makes a miss in a fully read group unverifiable (block, never a wrong not-found).
  - Effect-presentation omission reason is ambiguous (diff versus whole effect).
  - The `uiOpen` list was not edited for the `plc_write` probes (default branch used).
  - Rename `Start`→`Go` then create `Start` supersedes the rename on the shared key, so `Go` is never verified; verifier and planner supersession keys differ.
  - `WorkerTransportFailureGuidanceTests` rows for retired names only test the unknown-operation path.

## Open: plc_write native rejections follow-ups (2026-10-26)

Left open by the [native rejections plan](adr/0012-portal-as-rejection-authority.md)
and its [live verification](superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md).

- **VCI adopt the same error contract.** HMI write 2.0 now retains typed native errors and its partial
  mutation outcome; Network's migration is [complete within its recorded scope](#completed-network_write-native-rejections-2026-10-29).
  `vci_write` still wraps Siemens exceptions without `engineeringError`. Its separate migration
  should record a timing baseline, native rejection evidence and a contract version bump.
- **VCI onto the shared verifier.** PLC, Network and HMI now use `TrustedWriteVerifier` with
  `IWriteVerificationPolicy` and their domain policies. VCI remains separate; retain its
  domain-specific mutation outcomes, verification needs and qualification gates when migrating it.
- **Content importers still flatten Siemens errors.** `update_block_logic` and `update_type_content`
  import failures, and the exporters, are re-wrapped as `worker_operation_failed` with no
  `engineeringError`. These wrap sites do not populate `WorkerOperationException.EngineeringError`;
  they would call `WorkerExecution.Describe(ex)`, and the category choice there needs the same
  type-based rule.
- **`engineeringError` bounds are silent.** `WorkerExecution.Describe` cuts a message at 4,000
  characters and the inner chain at 8 entries without a marker, which contradicts "verbatim". Drop the
  caps or add a truncation marker.
- **`plc_ob_singleton_exists` is retained, not proven redundant.** Portal's own response to a duplicate
  singleton OB was never observed because no `plc_write` input reaches it. Retiring the guard needs a
  probe outside `plc_write` (a direct Openness or TIA Scripting import of a second OB 80).
- **Namespace coverage remains scoped.** Additional probes cover cross-table tag creation, a constant against an existing tag, a tag against an FC and child-folder tag-table creation against an existing root-table name. Cross-group block duplicates, the reverse cross-kind directions and arbitrary pairs of child
  folders remain unverified; do not generalize those four rejection cases.
- **`update_tag` partial application (review finding, not exercised live).** An unsupported external-access flag can fail mid-update, after a
  rename or data-type change in the same item has applied. The rename read-back compares ordinally; if
  Portal ever normalised the case of a requested name, a case-only rename would fail
  `postcondition_failed`.
- **Failed non-content items have no verification entry** in PLC, Network or HMI. Add a `not_verified`
  check only if a visible entry is wanted; inspect actual state before retrying a partial failure.
- **Verification wording and tests.** `BlockMutationService` still logs "resolved and compiled" and
  sets `reExportSucceeded:true` without a re-export; a comment in `PlcWriteRoundtripScenario.cs`
  still says "failed verification"; the FakeWorker `update_tag` allows a colliding rename, so no MCP
  test pins the `postcondition_failed` item shape; the mapping test lacks an
  `EngineeringObjectDisposedException` row and host-side absence of `engineeringError` is asserted
  only for one rejection kind.
- **Short-path block key.** The "undetermined" block key in `PlcWriteVerifier` is a path-shape
  heuristic (two segments) tied to `PlannedPath`; the conservative rule is documented in code.
  The unreachable independent-item replan cache and its direct no-op test were removed at closeout.
- **Post-write `contentHash` is not reproducible from content** (the export carries a generated id).
  Documented; callers take it from the verification or a fresh `plc_read`. Making it deterministic
  would need the sanitizer to cover the generated id.
- **Pre-existing flake.** 14 CLI startup tests (`RemovedOptionTests`, `WorkerTimeoutStartupTests`)
  fail under parallel load and have no `[Collection]`.

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
edit/restoration evidence. The [qualified contract](adr/0003-network-domain-identity-and-subnet-lifecycle.md)
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

These came out of the Phase 1b final review ([plan](adr/0005-canonical-json-contract.md)).
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
  remains deferred; the [current review findings](adr/records/2026-10-17-codebase-simplification-review-findings.md)
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

Ideas left over from the [test-suite runtime design](adr/0013-toolchain-and-test-infrastructure.md).
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

## Completed: plc_write native rejections (2026-10-26)

[Plan](adr/0012-portal-as-rejection-authority.md), [live verification](superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md).
`plc_write` stopped pre-checking create collisions; the recorded duplicate tag/table/block/group
cases, four additional namespace directions and default-table delete failed `engineering_rejected` with a bounded
`engineeringError`. Write contract `2.0`; `plc_read` stays `1.0`. Final fix `e7803a4` clarifies uncertainty after a failed later non-content item, removes unreachable independent-item replan storage and narrows tool claims; the final default suite passed 7717 tests with 7 pre-existing warnings. Its host changes are offline-qualified; the added native probes used installed `7445c5f`, whose worker code is unchanged.

- **Removed** (`534937a..7445c5f`, production files): 492 lines removed, 280 added. Largest:
  worker `PlcWritePreconditions.cs` -209/+8, `PlcWriteVerifier.cs` -101/+37, `PlcWorkingState.cs`
  -74/+64, `PlcNameRules.cs` -34/+2, `PlcWritePlanner.cs` -14/+32. Tests: -258/+658 lines (plus +576 in the
  regenerated `output-schemas.json` snapshot); FakeWorker -7/+38. Guards `plc_name_collision`,
  `plc_block_exists`, `plc_default_tag_table` and `plc_attribute_unreadable` are gone; verification
  keeps only the content-hash re-export.
- **Timing** (audit `durationMs`, `full` mode, `PLC_1` of a disposable copy with 1 block, 2 tag tables,
  2 tags; 4 PLCs and 111 top-level tree nodes in the project; medians in ms; Inspector wall-clock not
  measured). Before = `main` at `02ffe5a`; Set C = `7445c5f`, the protocol-complete quiet run.

  | Case | Root before | Root after | Item sum before | Item sum after | Remainder before | Remainder after |
  | --- | --- | --- | --- | --- | --- | --- |
  | T1 `create_tag` | 17,826 | 11,442 (-36%) | 5,201 | 5,178 | 12,673 | 6,264 (-51%) |
  | T2 10 items | 137,669 | 119,732 (-13%) | 124,185 | 112,428 | 13,484 | 6,437 (-52%) |
  | T3 `update_block_logic` | 54,933 | 55,378 (+1%) | 37,318 | 38,335 | 18,356 | 17,073 (-7%) |
  | T4 `delete_block_group` | 18,814 | 12,017 (-36%) | 5,325 | 5,472 | 13,718 | 6,545 (-52%) |
  | T5 T2 `dryRun` | 5,975 | 6,308 (+6%) | 0 | 0 | 5,975 | 6,308 |

  Sets A (noisy machine) and B (off-protocol replicate) are in the report as context. The remainder
  (planning plus verification) roughly halves for T1, T2 and T4; the content path (T3) is unchanged.
- **Retained limits:** cross-folder duplicate tag tables untested (L2); Portal's duplicate singleton OB
  rejection unreachable through the tool (L6); an `update_tag` unsupported-flag failure can follow an
  applied rename or type change; a create into a missing parent group or folder now fails at mutation;
  14 CLI startup tests are flaky under parallel load (pre-existing).

## Completed: network_write native rejections (2026-10-29)

[Plan](adr/0012-portal-as-rejection-authority.md),
[live verification](superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md).
`network_write` is contract `2.0`; `network_read` stays `1.0`. Planning reads one initial hardware
snapshot. Per-item replanning and immediate/final native read-back verification are removed;
successful prefix items use `TrustedWriteVerifier`, shared with PLC through separate domain policies,
and report `trusted` or `superseded`. Failed/skipped items have no verification entry. Configure stops
at its first native rejection and retains typed `appliedSettings`; native exception type determines
`engineering_rejected` or `worker_operation_failed`, with bounded `engineeringError`.

- **Removed** (Task 6, `8090ded..a353ab8`, production files only): 1,387 lines removed, 423 added,
  net −964. Largest removals: `NetworkWriteVerifier.cs` -286, `NetworkWritePayloadBudget.cs`
  -223/+52, `NetworkMutationVerifier.cs` -194, `NetworkPayloadContract.cs` -182/+34 and
  `PlcWriteVerifier.cs` -125. Shared `TrustedWriteVerifier`, `IWriteVerificationPolicy` and the two
  domain policies replace duplicated verifier logic. The later scoped L3 rename fix adds seven
  production lines: the live-qualified stage `8090ded..75c0568` is 1,387 removed / 430 added,
  net −957. The later host-only boundary fixes bring the current aggregate to 1,387 removed /
  455 added, net −932. L3 compares
  the already-read subnet name before transaction commit and fails `postcondition_failed` when a
  colliding rename was ignored; it adds no getter or RPC.
- **Qualification:** installed `4.2.0-local.79.g75c0568` passed all 17 expected functional outcomes.
  The Task 7 fixture/offline closeout before the host follow-up changed only FakeWorker and tests; its full Release
  non-packaging suite passed 7,665 tests with no failures or skips. Linked host/contracts coverage
  was 12,530/13,174 lines (95.11%, above the 80% gate); this aggregate excludes worker code.
- **Host-only review follow-up:** raw Network outcomes and worker-response carriers are checked
  before permissive decoding. Malformed responses fail without request replay or worker replacement.
  Twelve malformed-wire regression cases were observed failing before the fixes; sixteen new
  controls and the focused 517-test set then passed. Worker/contracts source remains the live-tested
  `75c0568` version. These host changes have offline evidence only; they were not installed,
  live-tested or timed again. The [report](superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md)
  records their final full-suite/coverage gate separately.
- **Timing** (audit `durationMs`, `full` mode, disposable fixture; medians in ms): five discarded
  warm-ups and three measured rounds of N1–N5 used installed source `75c0568`, before the host
  follow-up. The
  [before input](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-27-network-write-timing-before.md)
  and [after input](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-network-write-timing-after.md)
  retain the frozen requests, project inventory and collection protocol.

  | Case | Root before | Root after | Item sum before | Item sum after | Remainder before | Remainder after |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: |
  | N1 | 113,088 | 72,668 | 968 | 681 | 112,120 | 71,987 |
  | N2 | 627,273 | 81,174 | 516,709 | 11,385 | 114,207 | 69,790 |
  | N3 | 115,959 | 75,940 | 6,945 | 1,319 | 109,932 | 74,267 |
  | N4 `dryRun` | 57,230 | 70,591 | 0 | 0 | 57,230 | 70,591 |
  | N5 | 113,739 | 74,545 | 4,383 | 5,638 | 109,356 | 68,907 |

  Each column's median is independent; remainder is root duration minus item-duration sum for
  each sample, not a direct planning measurement. N2 root time fell 87.1%, while N4 root time
  rose 23.3% and N5 item time rose 28.6%. Cache and machine/session state were uncontrolled
  between collection windows, so these observations do not establish universal or causal gains.
- **Retained limits:** a same-call connect then delete uses the initial snapshot for the delete
  guard; it does not include the new connection. Fresh duplicate-IP/PROFINET-name writes were
  accepted and public `compile_check` was silent; that tool compiles PLC software only. The
  maintainer separately observed hardware-compile collision diagnostics. The literal
  `Invalid PN Name!` was accepted/normalized, with a prior maintainer clean-compile observation.
  These accepted/compile-silent observations remain evidence, not new native-rule emulation.
  Native acceptance outside the recorded matrix and HMI/VCI migration remain open; inspect failed
  items with `network_read` before retrying because applied properties can remain.

## Completed: hmi_write native rejections (2026-10-30)

The [plan](adr/0012-portal-as-rejection-authority.md) and
[scoped acceptance report](superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md)
record write contract 2.0, read contract 1.1, typed Siemens errors with conditional partial outcomes,
three retained guards and shared `trusted`/`superseded` verification. Planning uses initial,
sequential public-read evidence and caller-order simulation, with no per-item re-plan or automatic
post-write reads/Validate. Callers inspect state, Validate and compile in the HMI UI separately.

- **Removed:** Task 5's frozen production/FakeWorker count was +215/-564. The final aggregate
  against analysis baseline `0b2640a` is +255/-569 across 19 production/FakeWorker files, net -314.
  `HmiWriteVerifier.cs` removes 277 lines; the domain policy adds 40. Later changes include the R1
  shared-verifier fix and captured fixtures, so these totals describe different checkpoints.
- **Qualification:** all 35 standalone cases ran on installed `.112`/`96a28b7`: 21 O1 rejections,
  three O2 accepted invalid alarms, five healthy controls, two worker refusals and L1–L4. R1's
  outcome-bearing `target_not_found` partial update is separately live-qualified on `.113`/`31d1a44`:
  earlier success becomes `superseded`. R2 bounds reuse of the 35-case evidence; it is not a full
  `.113` rerun. R3 records the maintainer's six repeated-compile waivers. Five fresh standalone UI
  compiles and the L5 local-session compile are captured; L4's two missing-tag errors are expected.
  L5 restores its full inventory without save/check-in. Its terminal helper exit is unknown after
  interruption; canonical cleanup/restoration evidence and subsequent maintainer actions remain distinct.
- **Offline gate:** 7,644 Release default tests pass with no failures/skips; linked host/contracts
  coverage is 12,311/12,952 lines (95.05%, above 80%). The serial stub build has no errors and the
  existing seven warnings. The real-assembly package/verifier pass; packaging-category tests were
  not required because packaging/product project files did not change.
- **Timing:** five discarded warm-ups and 15 measured calls replay the frozen H1–H5 workloads on
  `.113`. The [before input](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-timing-before.md)
  used installed `75c0568`, not analysis baseline `0b2640a`; the
  [after input](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after.md) records
  that deviation, the two client changes and unchanged workload bytes. Medians are in milliseconds:

  | Case | Root before | Root after | Item sum before | Item sum after | Remainder before | Remainder after |
  | --- | ---: | ---: | ---: | ---: | ---: | ---: |
  | H1 | 119,492 | 12,117 | 12,775 | 6,365 | 106,717 | 5,385 |
  | H2 | 574,416 | 128,725 | 363,268 | 69,850 | 170,527 | 34,924 |
  | H3 | 160,490 | 33,934 | 16,995 | 6,843 | 143,325 | 28,036 |
  | H4 `dryRun` | 95,049 | 34,542 | 0 | 0 | 95,049 | 34,542 |
  | H5 | 142,877 | 17,824 | 16,124 | 5,862 | 126,753 | 11,962 |

  Each median is computed independently; remainder is root minus item sum per sample. Root medians
  are 63.66–89.86% lower in these observations. Three samples, variable native/cache/session state
  and the non-equivalent baseline prevent universal or isolated causal performance claims. All 107
  client calls succeed; final independent status, saved hash and installed binary hashes are unchanged.
- **Retained limits:** no native O3 was detected in the captured comparisons. Broader races, Portal
  loss during HMI mutation and cleanup/transport faults were not induced. Connection-only assignment,
  the read-side error-evidence gap and optional delete-planning alarm-read removal remain open above.
  A setter may partially mutate before its stage appears in `appliedProperties`; inspect failed items
  before retrying. VCI's error/verifier migration and PLC content-importer error flattening remain open.
