# ADR-0012: TIA Portal is the authority on its own rejections; the server guards only what Portal does not report

**Status:** Accepted
**Decided:** 2026-08-06 to 2026-10-30   **Recorded:** 2026-10-30
**Area:** write-safety
**Current home:** [ARCHITECTURE: PLC writes](../ARCHITECTURE.md#plc-writes-plc_write); [ARCHITECTURE: HMI writes](../ARCHITECTURE.md#hmi-writes-hmi_write); [ARCHITECTURE: canonical safety flow (network writes)](../ARCHITECTURE.md#canonical-safety-flow-network-writes); [Network](../SupportedOperations/NETWORK_OPERATIONS_SUMMARY.md); [HMI](../SupportedOperations/HMI_OPERATIONS_SUMMARY.md); [PLC](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md)

## Context
- Network writes began as token-gated preview/apply (subnet lifecycle, Phase 4), then moved to the guarded single-call pipeline. `plc_write` and `hmi_write` followed onto the pipeline, and each domain grew its own planner, in-call state tracking and read-back verifier.
- Each domain then checked the same fact up to three times: a host planner guard, a worker precondition, and Portal itself. Planning read whole inventories and re-read them before dependent items.
- The host rules also hid Portal's real answer: Siemens exceptions were flattened to message text under a generic category, so a caller never saw why Portal refused.
- Probing showed Portal rejects most of these cases natively, but a few it silently ignores or only reports later, so "delete every check" was not safe either.

## Decision
The server guards only a write that Portal neither rejects nor reports afterwards. Anything Portal rejects, or reports on validation or compile, is left to Portal and surfaced verbatim.
- A Siemens `EngineeringTargetInvocationException` thrown inside a mutation worker method is the closed category `engineering_rejected`; other Siemens `EngineeringException` stays `worker_operation_failed`; `portal_lost` wins over both. The exception type decides, never message text, because messages follow the Portal UI language. Both carry the bounded `engineeringError` evidence.
- Retired: name-collision and existence guards, worker duplicate preconditions, per-item re-planning, and per-item read-back verification (content re-export excepted). Planning keeps only target resolution for updates and deletes, and the reads delete-consequence guards need.
- Kept, because Openness cannot supply them: delete-consequence info guards, the unreadable-evidence block guard, PLC address-overlap and content-hash concurrency, and `plc_ob_singleton_exists`. The singleton guard stayed because Portal's own rejection was never reachable through the tool.
- Silent-ignore cases keep one adjacent check: the PLC `update_tag` rename and the Network subnet rename are read back and fail `postcondition_failed`.
- A shared `TrustedWriteVerifier` (in `Safety/Pipeline`) replaces the per-domain verifiers: a completed item is `trusted`, or `superseded` when a later item changed it again. PLC, Network and HMI use it; VCI does not.
- Write contracts move to `2.0` for PLC, Network and HMI; the read contracts do not change.
- The HMI 1.0 planner (full tag/alarm inventory, locate-time existence and bound-tag rules, replan, `HmiWriteVerifier` read-back with per-object Validate) and its four-guard set (`hmi_name_collision`, `hmi_state_unverifiable`, two delete info guards) are gone; 2.0 has three guards. The after-the-fact HMI check is `hmi_read` `validate` plus the Portal UI compile.
- Rejected alternatives:
  - Keep host guards for fast, friendly errors: duplicates Portal, and the rules drift from what Portal accepts.
  - Per-domain small verifiers (Network approach A): two near-identical verifiers, with the extraction deferred to a third.
  - Fold the three Network worker result types into one: they describe different outcomes.
  - One `mutate_subnet` operation or a separate subnet tool: weaker schemas and audit than three explicit operations.
  - Two-PR split or throwaway spike for HMI: a live probe checkpoint inside one PR gave the same evidence earlier.

## Consequences
- Planning is cheaper (one snapshot or target-only reads) and callers get Portal's verbatim diagnostics.
- Partial application is possible with no rollback; failed items carry no verification entry, so callers re-read before retrying. Collisions Portal accepts silently (for example duplicate IP or PN name) surface only in compile.
- `engineering_rejected` does not prove a setter was reached, because the mutation flag spans the whole worker method. Collision directions beyond the live-tested ones are unverified.
- PLC content importers and VCI still flatten Siemens failures without `engineeringError`. A new silent-ignore finding gets a targeted read-back, not a returned host guard.
- Typed Openness calls replaced reflection in Network attachment ([E-4 acceptance](../superpowers/acceptance/reports/2026-10-25-e4-typed-network-connections-acceptance.md)).
- Later work built on this: [ADR-0004](0004-guarded-single-call-write-pipeline.md) (pipeline), [ADR-0011](0011-fail-closed-surface-and-portal-loss.md) (`portal_lost` precedence).

## Evidence
- In tree: [PLC live verification](../superpowers/acceptance/reports/2026-10-26-plc-write-native-rejections-live-verification.md); [Network live verification](../superpowers/acceptance/reports/2026-10-29-network-write-native-rejections-live-verification.md); [Network Phase 4 live](../superpowers/acceptance/reports/2026-09-21-network-phase4-current-revision-live.md); [Network guarded-write offline validation](../superpowers/acceptance/reports/2026-10-03-network-json-guarded-write-offline-validation.md); [Network B-4 live](../superpowers/acceptance/reports/2026-10-19-network-b4-live-verification.md); [HMI write spike](../superpowers/acceptance/reports/2026-10-13-hmi-write-spike.md); [HMI write live acceptance](../superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md); [HMI checkpoint probes](../superpowers/acceptance/reports/2026-10-29-hmi-write-checkpoint-probes.md); [HMI native-rejections live verification](../superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md); [E-4 typed network connections](../superpowers/acceptance/reports/2026-10-25-e4-typed-network-connections-acceptance.md)
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-08-06-network-phase4-subnet-lifecycle-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-08-06-network-phase4-subnet-lifecycle.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-21-pr1-network-phase4-finish.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-26-wave1-n1-network-device-creation.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-03-network-json-guarded-write-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-03-network-json-guarded-write.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-13-hmi-write-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-13-hmi-write.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-25-e1-e2-worker-context-gates.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-25-e4-typed-network-connections.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-25-e5-shared-network-vocabulary.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-25-plc-write-native-rejections.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-26-network-write-native-rejections-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-26-network-write-native-rejections.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-29-hmi-write-native-rejections-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-29-hmi-write-native-rejections.md
