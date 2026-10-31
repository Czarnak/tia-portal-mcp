# Live verification reports and raw evidence

This directory keeps the live-verification reports under [`acceptance/reports/`](acceptance/reports/)
and the raw Openness evidence indexed in [`acceptance/reports/inputs/README.md`](acceptance/reports/inputs/README.md).
Its `specs/`, `plans/` and `evidence/` directories are a transient per-PR working area, empty on
`main`; the rules are in [`AGENTS.md`](../../AGENTS.md) ("Plans, specs and evidence").

A report describes the state of the world **on its date**; later work may have superseded it. The
code, [ARCHITECTURE.md](../ARCHITECTURE.md) and [SupportedOperations](../SupportedOperations/README.md)
are the authorities on current behavior. For why the design is as it is, and the project's history,
start at the [architecture decision records](../adr/README.md). Removed plans and specs remain at the
`docs-archive-2026-10-30` tag.

## Acceptance definitions

| Date | Document |
| --- | --- |
| 2026-10-07 | [Multiuser PR4 — installed-public-tool live definition and original L01–L19 expectations](acceptance/2026-10-07-multiuser-pr4-live-definition.md) |

## Acceptance reports

The PR 5 and PR 6 safety-scope reports (2026-09-05/06) qualified token-bound snapshots that `plc_write` has since replaced; they are evidence for the retired token flow only.

| Date | Document |
| --- | --- |
| 2026-10-30 | [HMI write native rejections — scoped qualification complete: version boundaries, 31-variant matrix, R1 partial-failure fix, R2 evidence reuse, accepted R3 compile reduction, L1–L5, timing and retained limits](acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md) |
| 2026-10-29 | [Network write native rejections — scoped 17-case PASS, maintainer rulings, corrected L11c/L13mini fixtures, three-run timing comparison, verified restoration and retained snapshot/native-fault limits](acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md) |
| 2026-10-29 | [HMI native-rejection checkpoint probes — all 31 P1–P22 variants: 21 native rejections, eight accepted, two worker refusals; per-variant UI follow-up](acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md) |
| 2026-10-26 | [PLC write native rejections — scoped PASS on a disposable standalone copy: L1–L11 matrix, silent colliding-rename finding and its read-back fix, content-hash probe, before/after timing, C1–C4 namespace directions and retained limits](acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md) |
| 2026-10-25 | [E-4 typed network connections — scoped PASS on installed `b5e29e4`: two native engineering rejections, one successful subnet and IO attachment, dependency denial; restoration by discard/reopen with the late-approval deviation; native fault injection untested](acceptance/reports/2026-10-25-e4-typed-network-connections-acceptance.md) |
| 2026-10-23 | [C-5 network-object cursors — scoped PASS for installed standalone paging, tamper rejection, same-owner reverify and fresh continuation; retained fault/mode/local-session limits](acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md) |
| 2026-10-20 | [B-16/Q3 worker Portal-loss handling — scoped live verification: `portal_lost` and immediate cursor invalidation for Network, PLC, VCI and local-session reads on `e804e6f`, HMI read interruption and same-worker recovery on `2fb5e9a`; maintainer acceptance 2026-10-20 with untested limits](acceptance/reports/2026-10-20-b16-q3-worker-portal-loss-live-verification.md) |
| 2026-10-19 | [Network B-4 — scoped installed-candidate subnet/IO-system selector regression, expected missing-target failures, unchanged fixture state and retained offline-only fault-case limits](acceptance/reports/2026-10-19-network-b4-live-verification.md) |
| 2026-10-16 | [VCI tools live acceptance — frozen-candidate functional, timeout and crash evidence; follow-up fixes, scoped live passes, recovery/restoration and retained export limits](acceptance/reports/2026-10-16-vci-tools-live-acceptance.md) |
| 2026-10-15 | [VCI live spike — leaf-only Openness VCI, container rejections and Portal crashes, scope timing, transactional and silent malformed import, status semantics, persistence, and what was not executed](acceptance/reports/2026-10-15-vci-spike.md) |
| 2026-10-14 | [`hmi_write` live acceptance — candidate and package provenance, both-mode functional evidence and restoration, scoped timing/memory, native tag case-only rename limits, cache retention with O1 open](acceptance/reports/2026-10-14-hmi-write-live-acceptance.md) |
| 2026-10-13 | [`hmi_write` live write spike — binding recipe, no connection auto-creation, name scopes, cascading deletes, the undeletable default table, per-property text form, `SetAttributes` atomicity and per-tag cost](acceptance/reports/2026-10-13-hmi-write-spike.md) |
| 2026-10-12 | [`hmi_read` completion live acceptance — PASS with recorded limits: cursor paging (21 s first page, under 1 s per continuation), cursor failure categories, `memberTags`/`loggingTags`/`screenItems` with a real `Dynamization` finding, `{hmi}` cross-references, runtime-settings allow-list, Classic-only failure, 42-row regression; worker restart surfaces `binding_conflict` instead of `snapshot_unavailable` (accepted); observations O1-O6](acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md) |
| 2026-10-09 | [OB creation live acceptance (issue #75) — all 15 classes created on an S7-1500 with planned = actual numbers, duplicate singleton blocked in `dryRun` and actual, S7-1200 refusal carries TIA's text, STL FB/FC/OB created, full compile clean except the HardwareInterrupt trigger warning; no defects](acceptance/reports/2026-10-09-ob-creation-live-acceptance.md) |
| 2026-10-09 | [Multiuser PR6 local-session writes — every PLC/Network write, compile and `LocalSession.Save()` on Multiuser and Exclusive sessions, standalone regression and unexercised cases](acceptance/reports/2026-10-09-multiuser-pr6-live-verification.md) |
| 2026-10-09 | [Multiuser PR5 local-session reads — every enabled read on a cold-adopted local session, standalone regression including Unified HMI, refused compile/write and unexercised cases](acceptance/reports/2026-10-09-multiuser-pr5-live-verification.md) |
| 2026-10-08 | [OB creation Phase 0 spike (issue #75) — SimaticML import passes under the SimaticSD default; import never renumbers and accepts duplicate singletons; STL rejects `SetENOAutomatically`; S7-1200 import refuses unsupported classes, S7-1200 G2 accepts all](acceptance/reports/2026-10-08-ob-creation-spike.md) |
| 2026-10-08 | [Multiuser PR4 — scoped maintainer acceptance, offline dialog failures, audits and restoration](acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md) |
| 2026-10-08 | [`hmi_read` installed-tool live acceptance — all 20 operations, the error/edge paths and the re-check PASS (42/42), read-only; Medium redaction finding D1 and Low D2 fixed and re-verified live, Low D3 (scan performance) accepted, non-integral `Double` not exercised live](acceptance/reports/2026-10-08-hmi-read-live-acceptance.md) |
| 2026-10-07 | [`hmi_read` live spike S1–S10 — read-only probe of four Unified Comfort panels and one Unified PC station; default `limit`, depth cap, Classic type, variant encoding, and the `HmiTag.ConfirmationType` Portal crash](acceptance/reports/2026-10-07-hmi-read-spike.md) |
| 2026-10-07 | [PLC write (PR B) — offline qualification and live acceptance in read-write and full](acceptance/reports/2026-10-07-plc-write-validation.md) |
| 2026-10-06 | [Multiuser PR 3 — offline qualification; live acceptance passed by maintainer decision](acceptance/reports/2026-10-06-multiuser-pr3-offline-validation.md) |
| 2026-10-06 | [Multiuser PR 3 — installed-tool live acceptance finished and passed; observations and non-blocking evidence limitations](acceptance/reports/2026-10-06-multiuser-pr3-live-verification.md) |
| 2026-10-05 | [Multiuser PR 2 — offline qualification and bounded standalone live acceptance](acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md) |
| 2026-10-05 | [PLC read and standalone cross-references (PR A) — offline pass and read-only live PASS with recorded limits](acceptance/reports/2026-10-05-plc-read-and-cross-references-validation.md) |
| 2026-10-05 | [Network discovery and interface-qualified identity repair — offline pass, partial live verification and PR handoff](acceptance/reports/2026-10-04-network-discovery-and-interface-node-identity-repair-validation.md) |
| 2026-10-04 | [Network JSON contract and guarded writes — corrected-candidate offline validation; live acceptance pending](acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md) |
| 2026-10-03 | [Lifecycle tiers and runtime binding — all three modes live-accepted; human read-only Openness dialog observation recorded](acceptance/reports/2026-10-03-lifecycle-tiers-bind-project-live-validation.md) |
| 2026-10-01 | [JSON contract Phase 3 — guarded lifecycle live validation](acceptance/reports/2026-10-01-json-contract-phase3-live-validation.md) |
| 2026-10-01 | [JSON contract Phase 3 — offline validation](acceptance/reports/2026-10-01-json-contract-phase3-offline-validation.md) |
| 2026-10-01 | [JSON contract Phase 2 — bounded live verification; producer gate remains open](acceptance/reports/2026-10-01-json-contract-phase2-live-verification.md) |
| 2026-09-30 | [JSON contract Phase 2 — offline validation](acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md) |
| 2026-09-26 | [Network Phase 5 PR 2 — bounded worker-only V21 qualification](acceptance/reports/2026-09-24-network-phase5-pr2-qualification.md) |
| 2026-09-24 | [Network Phase 4 PR 1 — bounded current-revision live PASS; 2026-09-23 failed attempt retained](acceptance/reports/2026-09-21-network-phase4-current-revision-live.md) |
| 2026-09-12 | [Issue #32 — project-tree browsing v3 — accepted read-only PASS with explicit live ambiguity waiver](acceptance/reports/2026-09-06-issue-32-project-tree-v3-live.md) |
| 2026-09-06 | [PR 6 — project-tree safety scopes — mandatory live PASS](acceptance/reports/2026-09-01-pr6-project-tree-safety-scopes-live.md) |
| 2026-09-05 | [PR 5 — tag-operation safety scopes — mandatory live PASS](acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md) |
| 2026-09-05 | [PR 3 — exact `update_tag` safety snapshot — mandatory live PASS](acceptance/reports/2026-09-01-pr3-update-tag-safety-snapshot-live.md) |
| 2026-09-05 | [PR 4 — structured preview diff — live Preview and authorized Apply/restore/compile acceptance completed](acceptance/reports/2026-09-01-pr4-structured-preview-diff-live.md) |
| 2026-09-01 | [PR 2 — registered-tool delegation — live](acceptance/reports/2026-09-01-pr2-registered-tool-delegation-live.md) |
| 2026-09-01 | [PR 1 — explicit MCP tool annotations — live](acceptance/reports/2026-09-01-pr1-explicit-mcp-tool-annotations-live.md) |
| 2026-08-14 | [Structured I/O map defect fixes — live](acceptance/reports/2026-08-14-io-map-defect-fixes-live.md) |
| 2026-08-01 | [Network Phase 1 — rerun](acceptance/reports/2026-08-01-16-56-00-network-operations-phase1-rerun.md) |
| 2026-08-01 | [Network Phase 1](acceptance/reports/2026-08-01-16-48-48-network-operations-phase1.md) |
