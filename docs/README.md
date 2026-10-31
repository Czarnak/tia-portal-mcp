# Documentation

Everything in this tree, grouped by who it is for. **A new document under `docs/` is not
complete until it is listed here** — that rule is what keeps this index trustworthy.

For the project overview, tool list, and quick start, see the [README](../README.md).

## Using the server

| Document | What you will find |
| --- | --- |
| [Installation](guides/installation.md) | Requirements, installation/doctor, explicit binding, access modes, and mode-derived lifecycle confirmation |
| [MCP client configuration](guides/mcp-client-configuration.md) | Client configuration, bind/open migration, lifecycle confirmation, guarded Network and `plc_write` migration, block paths, response size limits, and worker timeout |
| [Troubleshooting](guides/troubleshooting.md) | Common failures, and TIA Portal V21 behaviors verified against a real installation |
| [Supported operations](SupportedOperations/README.md) | Every operation by area — project, PLC, devices, network, HMI, and more |

## Understanding the design

| Document | What you will find |
| --- | --- |
| [Architecture](ARCHITECTURE.md) | Two-process topology, tier enforcement, explicit binding/cursor invalidation, transport, canonical JSON, mode-derived confirmation/audit v2, and testing |
| [Architecture decision records](adr/README.md) | Why the design is as it is, in project order |
| [Network write native rejections plan](adr/0012-portal-as-rejection-authority.md) | Implementation and scoped qualification complete: write 2.0/read 1.0, shared trusted verification, typed partial failures, the L3 rename fix and live-derived offline fixtures. [Design](adr/0012-portal-as-rejection-authority.md); [initial observations](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-28-network-write-native-rejections-live-cases.md), [17-case functional rerun](superpowers/acceptance/reports/inputs/2026-10-29-network-write-native-rejections-live-rerun.md), [before timing](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-27-network-write-timing-before.md) and [after timing](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-network-write-timing-after.md) retain exact evidence and adaptations |
| [Network native rejections live verification](superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md) | Scoped 17-case PASS, maintainer rulings, corrected L11c/L13mini fixtures, final 7,681-test gate/95.11% host/contracts coverage, separately offline-qualified host-protocol follow-up, three-run timing comparison including slower preview, verified restoration and retained snapshot/native-fault limits |
| [Network operations and paging matrix](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md#paging-matrix) | Cross-tool paging fields, defaults, bounds, scan/continuation semantics and per-domain recovery references |
| [HMI operations (`hmi_read`, `hmi_write`)](SupportedOperations/HMI_OPERATIONS_SUMMARY.md) | The 20 read-only WinCC Unified operations and nine writes, read DTO additions, paging/completeness, guards, partial failures, verification and limits |
| [HMI native rejections qualification](superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md) | Scoped qualification complete: version boundaries, 31-variant matrix, R1 partial-failure fix, R2 evidence reuse, accepted R3 compile reduction, L1–L5, 7,644 tests/95.05% coverage, timing and retained limits. [Design](adr/0012-portal-as-rejection-authority.md); [plan](adr/0012-portal-as-rejection-authority.md) |
| [HMI write candidate standalone evidence](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-live-cases.md) | All 31 P variants and L1–L4 on installed `96a28b7`, exact native rejection comparisons, public validation, restoration and retained limits. [Archive and checks](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-live-cases/README.md); [15-child validation supplement](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-validation-supplement.json); [focused R1 live proof on `31d1a44`](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-r1-live.json) |
| [HMI candidate UI compiles](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-ui-compiles/README.md) | Five fresh standalone maintainer reports on build 113, six explicit R3 waivers, cancelled-hold restoration and separate L5 linkage; exact counts are retained only when supplied |
| [HMI local-session qualification](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-local-session/README.md) | L5 create/read/Validate, clean maintainer compile, owned cleanup, exact 7-table/72-tag/78-alarm inventory restoration and truthful unknown terminal-exit provenance |
| [HMI timing after](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after.md) | Five discarded warm-ups and 15 measured calls on installed `31d1a44`, independently computed root/item/remainder medians and verified restoration. [Replay, evidence and offline checks](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/README.md) |
| [HMI write installed-version timing baseline](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-timing-before.md) | Five discarded warm-ups and fifteen measured H1–H5 calls on installed `75c0568`; independent audit medians, full fixture restoration and the explicit deviation from the design's `0b2640a` baseline. [Replay and evidence](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-timing-before/README.md) |
| [HMI native-rejection checkpoint probes](superpowers/acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md) | All 31 P1–P22 variants: 21 native rejections, eight accepted, two worker refusals; per-variant UI follow-up in the report. [Native summary](superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-native-summary.json), [full trace (gzip)](superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-native-probes.jsonl.gz), [original probe source](superpowers/acceptance/reports/inputs/2026-10-29-hmi-native-probe/README.md), [UI probe source](superpowers/acceptance/reports/inputs/2026-10-29-hmi-ui-compile-probe/README.md), [P10 UI trace](superpowers/acceptance/reports/inputs/2026-10-29-hmi-ui-P10.jsonl.gz), [P12-discrete UI trace](superpowers/acceptance/reports/inputs/2026-10-29-hmi-ui-P12-discrete.jsonl.gz), [installed preflight](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-baseline-preflight.json), [P7](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-installed-P7.json) and [initial-run final restoration](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-final-restoration.json) preserve evidence |
| [VCI operations (`vci_read`, `vci_write`)](SupportedOperations/VCI_OPERATIONS_SUMMARY.md) | Standalone workspace and mapping operations, formats, sync rules, path guards, recovery and evidence limits |
| [hmi_read completion live acceptance](superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md) | Cursor paging and its failure categories, name-scoped `validate` categories, the provisional `{hmi}` cross-reference selector, runtime-settings allow-list, local-session paging, the 42-row regression, and observations O1-O6 |
| [hmi_write live write spike](superpowers/acceptance/reports/2026-10-13-hmi-write-spike.md) | Binding recipe (`Connection` then `PlcTag`), no connection auto-creation, name scopes, cascading deletes, the undeletable default table, per-property text form (`EventText` formatted, `InfoText`/`Comment` plain), `ConditionValue` types, dangling references, `SetAttributes` atomicity and per-tag cost |
| [VCI live spike](superpowers/acceptance/reports/2026-10-15-vci-spike.md) | Leaf-only Openness VCI, container rejections and the Portal crashes with their cause, scope timing, transactional import, silent overwrite and silent malformed import, status semantics, persistence, and what was not executed |
| [VCI tools live acceptance](superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md) | Frozen-candidate functional, timeout and crash evidence; follow-up fixes, offline gates, scoped live passes, recovery/restoration and retained export limits |
| [VCI live findings fix plan](adr/0010-vci-workspace-primitives.md) | HMI mapping repair, export-cost and recovery investigations, per-fix commits, offline gates and the subsequently authorized live follow-up |
| [Codebase simplification review plan](adr/records/2026-10-17-codebase-simplification-review.md) | Agreed decisions Q1–Q6 (bound-project-only content tools, canonical argument rejection, worker-wide `portal_lost`), six workstreams of dead code, defects, tool consistency and consolidation, PR order; [verified findings appendix](adr/records/2026-10-17-codebase-simplification-review-findings.md) |
| [Workstream C plan](adr/0008-tool-surface-consistency.md) | Tool-surface consistency PR sequence (four waves, C-1 before C-0), decisions and shared constraints; wave-1 plans: [C-1](adr/0008-tool-surface-consistency.md), [C-3](adr/0008-tool-surface-consistency.md), [C-4](adr/0008-tool-surface-consistency.md), [C-7](adr/0008-tool-surface-consistency.md), [C-8](adr/0008-tool-surface-consistency.md), [C-10](adr/0004-guarded-single-call-write-pipeline.md); wave-2 plan: [C-0](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md); wave-3 plans: [C-2](adr/0008-tool-surface-consistency.md), [C-6](adr/0008-tool-surface-consistency.md); wave-4 plans: [C-5](adr/0003-network-domain-identity-and-subnet-lifecycle.md), [C-9](adr/0008-tool-surface-consistency.md), with parallel worktrees, serialized full tests and C-5 merging first |
| [Guarded-write budget implementation record (B-10 / D-2)](adr/0004-guarded-single-call-write-pipeline.md) | Effective-limit metadata fix and shared host budgeting, preserved domain omission/recovery policies, four implementation steps and completed offline verification |
| [D-1 shared batch read implementation record](adr/0005-canonical-json-contract.md) | Approved [design](adr/0005-canonical-json-contract.md), shared host runner/read envelopes, four domain migrations, unchanged schema/canonical responses, 7,618 passing tests and independent review; offline-qualified |
| [PLC write native rejections plan](adr/0012-portal-as-rejection-authority.md) | Approved plan to let TIA Portal reject PLC name collisions, existing targets and protected objects: `engineering_rejected` category with a verbatim `engineeringError` object, retired duplicate guards and worker preconditions, planning limited to consequence-guard reads, content-hash-only verification; baseline and post-change call timing from audit `durationMs`; live verification before documentation; implemented and live-verified on 2026-10-26, unmerged |
| [PLC write native rejections live verification](superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md) | Scoped PASS on a disposable standalone copy: L1-L11 matrix (native rejections L1-L5/L10) with bounded `engineeringError`, the silent colliding-rename finding and its `postcondition_failed` read-back fix, the content-hash probe, before/after call timing (Set C primary, project size, noisy and off-protocol sets labelled), rulings R1-R18, the four exact C1-C4 namespace directions and observed cleanup, and retained limits; [additional collision input](superpowers/acceptance/reports/inputs/2026-10-26-plc-write-collision-namespace-probes.md) uses the unchanged installed worker; new host fixes are offline-qualified. Other inputs are the baseline and after timing, the L-case captures and the closeout run under `superpowers/acceptance/reports/inputs/` |
| [D-3 / D-4 / D-5 shared refactor implementation record](adr/0005-canonical-json-contract.md) | Shared write records/lazy validation, payload-failure helpers and direct Network read executor injection; parallel implementation, reviewed reflection-fixture repair, unchanged schemas/canonical responses, 7,631 passing tests and 95.09% scoped coverage; offline-qualified |
| [E-1 / E-2 worker context gates implementation record](adr/0012-portal-as-rejection-authority.md) | Shared project-open/context gates, preserved caller policies and restrictions, 7,676 passing tests, 95.09% scoped coverage and independent review; offline-qualified without live TIA testing |
| [E-4 typed network connections implementation record](adr/0012-portal-as-rejection-authority.md) | Verified V21 typed calls, refreshed references, offline qualification and scoped native connection/rejection checks; disposable fixture restoration verified, fault/mode limits retained in the [native record](adr/0012-portal-as-rejection-authority.md) |
| [E-5 shared network vocabulary implementation record](adr/0012-portal-as-rejection-authority.md) | Shared exact-value constants, independent host validation and completed A -> B -> C handoff; 7,696 ordinary and 39 packaging tests passed with 95.09% scoped coverage; offline-qualified |
| [Workstream C follow-ups orchestration and completion ledger](adr/0008-tool-surface-consistency.md) | Exact PR1–PR6 merge identities, offline qualification/review evidence, PR7 cleanup/disposal records and retained live-testing limits; the shared plan preserves branch ownership and serialized qualification |
| [Worker Portal-loss implementation record (B-16 and Q3)](adr/0011-fail-closed-surface-and-portal-loss.md) | Shared exception mapping, fatal propagation and retained mutation outcomes; scoped live testing accepted by the maintainer on 2026-10-20, with candidate-specific evidence and untested limits retained |
| [Network B-4 live verification](superpowers/acceptance/reports/2026-10-19-network-b4-live-verification.md) | Scoped installed-candidate subnet/IO-system selector regression, expected missing-target failures, unchanged fixture state, and retained offline-only fault-case limits |
| [C-5 network-object cursor live verification](superpowers/acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md) | Scoped PASS for installed standalone paging, tamper rejection, same-owner reverify and fresh continuation; package provenance, unchanged fixture state and retained fault/mode/local-session limits |
| [hmi_write live acceptance](superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md) | Exact candidate and package provenance, offline gate, both-mode functional evidence and restoration, scoped timing/memory comparisons, native tag case-only rename limits, and cache retention with O1 open |
| [Multiuser local sessions, inventory and acceptance boundary](SupportedOperations/MULTIUSER_OPERATIONS_SUMMARY.md) | Exact AMC adoption, ALS opening/basic status, persistent inventory, preservation guards and remaining session limits |
| [OB creation live acceptance (issue #75)](superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md) | All 15 `obEventClass` values on an S7-1500 with planned equal to actual numbers, duplicate singleton blocked in `dryRun` and actual, S7-1200 refusal text, STL FB/FC/OB, clean full compile apart from the HardwareInterrupt trigger warning |
| [OB creation Phase 0 spike (issue #75)](superpowers/acceptance/reports/2026-10-08-ob-creation-spike.md) | SimaticML import under the SimaticSD default, number and duplicate-singleton behavior, STL `SetENOAutomatically` rejection, S7-1200 and S7-1200 G2 class support |
| [PR4 live definition](superpowers/acceptance/2026-10-07-multiuser-pr4-live-definition.md) | Original L01–L19 expectations, installed-public-tool protocol and scoped acceptance rules |
| [PR5 local-session reads live verification](superpowers/acceptance/reports/2026-10-09-multiuser-pr5-live-verification.md) | Candidate/provenance, every enabled read on a cold-adopted local session, standalone regression including Unified HMI, refused compile/write, and unexercised cases |
| [PR6 local-session writes live verification](superpowers/acceptance/reports/2026-10-09-multiuser-pr6-live-verification.md) | Candidate/provenance, every PLC/Network write, compile and `LocalSession.Save()` on Multiuser and Exclusive sessions, standalone regression, source-import observation, and unexercised cases |
| [PR4 scoped live acceptance](superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md) | Frozen candidate/provenance, online recovery, offline Siemens-dialog failures, maintainer ruling, audits, restoration and unexecuted cases |
| [PR 3 offline qualification](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md) | Combined candidate, serial suite/coverage, installed-reference compilation and local package checks; live acceptance passed by maintainer decision |
| [PR 3 installed-tool live acceptance](superpowers/acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md) | Finished and passed by maintainer decision on October 6; all six inspection actions, zero-project/standalone prerequisites, binding/cursors, worker-loss recovery and recorded evidence limitations |
| [PR 2 standalone live acceptance](superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md) | Offline qualification, frozen live candidate, selection/lifecycle/headless evidence and restored fixtures |

## Building and contributing

| Document | What you will find |
| --- | --- |
| [Contributing](../CONTRIBUTING.md) | Contribution workflow, branch focus, commit message format, pull requests |
| [Building from source](development/building.md) | Restore, build, test with coverage, and run the server locally |
| [Test suite runtime design](adr/0013-toolchain-and-test-infrastructure.md) | Measured run times and coverage, parallel CI command, conditional packaging step, serial-collection trim and its guard, deferred per-worker FakeWorker environment |
| [Local MCP sandbox testing](development/local-mcp-testing.md) | MCP Inspector, actual-by-default Network inputs, frozen Network fixtures/restoration boundaries, and lifecycle gates |
| [Packaging](development/packaging.md) | Build the NuGet package and install a local branch build as the `tia-mcp` global tool |
| [Measuring write timing](development/measuring-write-timing.md) | The shared `scripts/timing` client: run a write workload against a disposable project, validate the archive, read the output files |
| [Codebase review runbook](development/codebase-review.md) | How to review the whole repository across several runs: written design rules as tests, domain reviews end to end, rule sweeps, reviews of changes since the baseline, findings ledger, verification order and rotation |

Agent-facing build and convention reference lives in [AGENTS.md](../AGENTS.md).

## Direction

| Document | What you will find |
| --- | --- |
| [Roadmap](../ROADMAP.md) | Directional priorities for the project as a whole |
| [Network operations roadmap](roadmap/network-operations.md) | Phased delivery of the network tool surface and its JSON contract |
| [Export/import format roadmap](roadmap/export-import-format.md) | Source-format exchange for UDTs, data blocks, and SCL |
| [JSON contract roadmap](roadmap/json-contract.md) | One structured JSON output contract across the tool surface: target envelope, migration phases, the conformance guard, and the retired batch tools |
| [Improvement log](IMPROVEMENT_LOG.md) | Unresolved follow-ups only; completed work remains in the linked implementation and acceptance records |

## Project history

[`superpowers/`](superpowers/README.md) holds design specs, implementation plans, and live
acceptance reports produced while building features. It is historical process material, not
current documentation — see its index for what is there and how to read it.

Planned: the [docs lifecycle and project-memory compaction directions](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-30-docs-lifecycle-and-adr-compaction-design.md)
describe how this material will be reduced to ADRs, kept reports and raw Openness evidence.

Latest implementation entry: [Multiuser PR4 — open existing sessions](adr/0006-multiuser-owner-model.md)
records implemented/offline-qualified `.amc21` adoption and `.als21` opening, with scoped
maintainer acceptance on October 8. The report retains failed noninteractive offline opening
and unexecuted matrix rows; Issue #65 and successor session operations remain open.

Latest process entry: the [HMI native-rejections design](adr/0012-portal-as-rejection-authority.md), [plan](adr/0012-portal-as-rejection-authority.md) and [scoped acceptance record](superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md). Write 2.0 retains typed native errors and partial outcomes, uses initial public-read evidence with caller-order simulation, and shares trusted verification. The full 35-case matrix belongs to installed `96a28b7`; focused R1, accepted manual compile scope, L5 and timing belong to `31d1a44`. R2 bounds that reuse; R3 preserves six missing fresh compile observations. The default suite passes 7,644 tests with 95.05% scoped coverage. Three-sample timing medians are lower in all five recorded workloads, with the non-equivalent `75c0568` baseline and native/cache variability retained as limits. Current behavior is in the [HMI reference](SupportedOperations/HMI_OPERATIONS_SUMMARY.md).

Previous process entry: the [Network native-rejections design](adr/0012-portal-as-rejection-authority.md), its [completed plan](adr/0012-portal-as-rejection-authority.md) and [acceptance record](superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md). Task 7 fixtures pin the real L11c subnet-connection and L13mini address rejections. After the later host raw-outcome validation and tool-description corrections, the final offline suite passed 7,681 tests with 95.11% scoped coverage and scoped re-review passed. The 17-case live rerun and timing belong to the installed `75c0568` candidate within their documented limits, including the L3 scoped fix, snapshot delete consequences and slower preview. Current behavior is in the [Network reference](SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md); VCI verifier migration remains separate.

Previous process entry: the [VCI tools and configurable worker timeout design](adr/0010-vci-workspace-primitives.md), its [configurable worker timeout implementation plan](adr/0013-toolchain-and-test-infrastructure.md), its [VCI tools implementation plan](adr/0010-vci-workspace-primitives.md), and the [VCI live spike](superpowers/acceptance/reports/2026-10-15-vci-spike.md). Worker timeout implementation and documentation are complete. The [VCI live report](superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md) preserves the frozen candidate's failed verdict and timeout/crash evidence, then records the follow-up fixes, offline qualification and scoped live passes with restoration. Export safety scans and whole-project/native-crash limits remain; current behavior is in the [VCI reference](SupportedOperations/VCI_OPERATIONS_SUMMARY.md).

Previous process entry: the [hmi_write design](adr/0012-portal-as-rejection-authority.md) and its [implementation plan](adr/0012-portal-as-rejection-authority.md), built on the [hmi_write live write spike](superpowers/acceptance/reports/2026-10-13-hmi-write-spike.md), with the [scoped live acceptance report](superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md). Functional verification is complete within its limits; cache retention is a maintainer decision with O1 open and partial performance gates. Before it: the [hmi_read completion design](adr/0009-hmi-read-contract.md), its [implementation plan](adr/0009-hmi-read-contract.md) and its [installed-tool live acceptance report](superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md).

Previous process entries: the [`create_block` OB creation design](adr/0001-plc-content-exchange-and-creation.md) (issue #75), its [Phase 0 spike report](superpowers/acceptance/reports/2026-10-08-ob-creation-spike.md), its [implementation plan](adr/0001-plc-content-exchange-and-creation.md) and its [installed-tool live acceptance report](superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md).

Previous process entries: the [hmi_read design](adr/0009-hmi-read-contract.md) (with its dated post-implementation corrections), its [spike report](superpowers/acceptance/reports/2026-10-07-hmi-read-spike.md) and its [installed-tool live acceptance report](superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md).

Previous process entries: the [PLC read/write and cross-references design](adr/0004-guarded-single-call-write-pipeline.md),
its [PR A `plc_read` and `read_cross_references` plan](adr/0004-guarded-single-call-write-pipeline.md)
with its [validation report (offline and live-accepted)](superpowers/acceptance/reports/2026-10-05-plc-read-and-cross-references-validation.md),
and its [PR B `plc_write` plan](adr/0004-guarded-single-call-write-pipeline.md), which retires the batch
tools, PLC run/stop and the token core, with its
[validation report (offline and live-accepted in read-write and full)](superpowers/acceptance/reports/2026-10-07-plc-write-validation.md).

Previous process entries: the [approved Network discovery and interface-qualified node identity repair design](adr/0003-network-domain-identity-and-subnet-lifecycle.md)
and its [implemented/offline-qualified plan](adr/0003-network-domain-identity-and-subnet-lifecycle.md)
address the failed live Network write run. The [fresh validation and handoff report](superpowers/acceptance/reports/2026-10-04-network-discovery-and-interface-node-identity-repair-validation.md) records passing offline qualification and partial live verification; full acceptance remains pending owner-evidence and unverified-operation follow-ups.

Earlier process entries: the [approved Network JSON contract and guarded write design](adr/0012-portal-as-rejection-authority.md)
and its [completed offline implementation plan (live acceptance pending)](adr/0012-portal-as-rejection-authority.md),
with [corrected-candidate offline validation, independent review, coverage and package provenance](superpowers/acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md),
the [lifecycle in read-write, mode-derived confirmation, and runtime project binding design](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md)
(amends the redesign's §4.6 and §4.11) and its [implementation plan](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md),
with [live acceptance in all three access modes; human read-only dialog observation recorded](superpowers/acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md),
the [write-safety redesign design (retiring preview→apply tokens)](adr/0004-guarded-single-call-write-pipeline.md)
and its [Phase 1 foundation plan](adr/0004-guarded-single-call-write-pipeline.md)
and [Phase 1b access-mode implementation plan](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md),
the [JSON contract Phase 2 standalone tools implementation plan](adr/0005-canonical-json-contract.md)
and its [offline validation report (live V21 acceptance pending)](superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md)
and [live verification of status, compilation scopes, and failure/rejection responses (complete producer gate still open)](superpowers/acceptance/reports/2026-10-01-json-contract-phase2-live-verification.md)
and the implementation record for [JSON contract Phase 3 guarded lifecycle / write-safety Phase 2](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md)
and its [offline validation record](superpowers/acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md)
and [authorized live lifecycle validation](superpowers/acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md),
the [Multiuser Engineering design](adr/records/2026-09-28-multiuser-engineering-design.md)
and its [PR 1 reference and contract foundation plan](adr/0013-toolchain-and-test-infrastructure.md)
and [PR 2 active project context plan](adr/0006-multiuser-owner-model.md)
and [PR 3 inventory through bind_project plan (offline-qualified; live acceptance passed)](adr/0006-multiuser-owner-model.md),
the [JSON contract Phase 1b required-member enforcement design](adr/0005-canonical-json-contract.md)
and its [implementation plan](adr/0005-canonical-json-contract.md),
the [JSON contract Phase 1a worker-wire plan](adr/0005-canonical-json-contract.md),
[Open bug parallel pull-request delivery design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-26-open-bug-parallel-pr-delivery-design.md),
with current Wave 2 plans for [I2 truthful block-import outcomes](adr/0001-plc-content-exchange-and-creation.md)
and [T2 type-content binding policy](adr/0001-plc-content-exchange-and-creation.md),
plus Wave 1 plans for [L1 lifecycle rebind and preview safety](adr/0004-guarded-single-call-write-pipeline.md),
[K1 per-item batch failure categories](adr/0004-guarded-single-call-write-pipeline.md),
[P1 PLC compile and cross-reference read integrity](adr/0004-guarded-single-call-write-pipeline.md),
and [N1 network-device creation names](adr/0012-portal-as-rejection-authority.md),
[Network Phases 4-6 completion and delivery design](adr/0003-network-domain-identity-and-subnet-lifecycle.md),
its [PR 1 Network Phase 4 finish plan](adr/0012-portal-as-rejection-authority.md),
the [PR 2 Phase 5 fixture qualification and compile-contract plan](adr/0003-network-domain-identity-and-subnet-lifecycle.md),
the [Phase 5 qualified IO-system contract](adr/0003-network-domain-identity-and-subnet-lifecycle.md),
the [Phase 5 PR 2 worker-only V21 qualification report](superpowers/acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md),
the [Network Phase 4 current-revision live report (bounded PASS on 2026-09-24, prior failed attempt retained)](superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md),
[Issue #30 PLC block header metadata design](adr/0004-guarded-single-call-write-pipeline.md),
its [implementation plan](adr/0004-guarded-single-call-write-pipeline.md),
[.NET 10 migration and dependency alignment design](adr/0013-toolchain-and-test-infrastructure.md),
its [implementation plan](adr/0013-toolchain-and-test-infrastructure.md),
[issue #32 scalable project-tree browsing v3 design](adr/0002-bounded-project-and-hardware-reads.md),
its [implementation plan](adr/0002-bounded-project-and-hardware-reads.md),
and [accepted read-only live report with explicit ambiguity waiver (2026-09-12)](superpowers/acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md),
[write-safety preview and registered-surface hardening design](adr/0004-guarded-single-call-write-pipeline.md),
with separate plans for [PR 1 explicit MCP tool annotations](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md),
[its completed live acceptance report](superpowers/acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md),
[PR 2 registered-tool delegation](adr/0004-guarded-single-call-write-pipeline.md),
[its completed live acceptance report](superpowers/acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md),
[PR 3 exact `update_tag` safety snapshots](adr/0004-guarded-single-call-write-pipeline.md),
[its completed mandatory live acceptance report](superpowers/acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md),
[PR 4 bounded structured preview diffs](adr/0004-guarded-single-call-write-pipeline.md),
[its completed live Preview and authorized Apply/restore/compile acceptance report (2026-09-05)](superpowers/acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md),
[PR 5 tag-operation safety scopes](adr/0004-guarded-single-call-write-pipeline.md),
[its completed mandatory live acceptance report (2026-09-05)](superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md),
[PR 6 project-tree safety scopes](adr/0004-guarded-single-call-write-pipeline.md),
and [its completed mandatory live acceptance report (2026-09-06)](superpowers/acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md);
[project completeness and hardware pagination design](adr/0002-bounded-project-and-hardware-reads.md),
its [PR 1 project enumeration completeness plan](adr/0002-bounded-project-and-hardware-reads.md),
the [PR 2 hardware pagination plan](adr/0002-bounded-project-and-hardware-reads.md),
[PR #29 binding findings repair design](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md),
and its [implementation plan](adr/0007-access-tiers-lifecycle-confirmation-and-binding.md).

The [tag-safety acceptance boundary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md#tag-safety-acceptance-boundary)
and the [project-tree safety acceptance boundary](SupportedOperations/PLC_OPERATIONS_SUMMARY.md#project-tree-safety-acceptance-boundary)
now describe the guarded `plc_write` pipeline that replaced the PR 5 and PR 6 token-bound snapshots;
those two live reports remain historical evidence for the retired token flow only.
