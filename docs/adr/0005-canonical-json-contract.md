# ADR-0005: Worker payloads carry a declared null policy and are decoded by one reader that enforces required members

**Status:** Accepted
**Decided:** 2026-09-28 to 2026-10-25   **Recorded:** 2026-10-30
**Area:** json-contract
**Current home:** [ARCHITECTURE: the canonical JSON seam](../ARCHITECTURE.md#7a-the-opt-in-canonical-json-seam-and-the-network-phase-23-structured-contract); [JSON contract roadmap](../roadmap/json-contract.md)

## Context
- Worker payloads mixed omitted and explicit nulls, and each host serializer chose its own options, so the FakeWorker could disagree with the real worker.
- Required members were enforced by three hand-written validators (Network, hardware page, project tree) that duplicated member lists and could drift from the DTOs.
- Nine payload roots carried a null-omission marker only because those validators existed.
- The four read tools repeated one flow (validate, authorize, bind, read items, budget, deliver) and its envelope.
- A status or compile result cut mid-JSON at the character limit could not be a typed result.

## Decision
- The wire format is defined once (`WorkerJson`): a payload root writes explicit nulls unless it carries `[LegacyNullOmission(reason)]`, a registered exception with a stated reason. Worker, host transport and FakeWorker all use it.
- Required-ness is enforced by a dedicated worker-payload reader, not by the CLR types: every settable member is required unless it is `WhenWritingNull`, and conditional members are pinned by a register test. A rule that depends on the decode path cannot live on a type that travels under two null policies (a compile report is a root and also nested in a null-omitting import envelope).
- Standalone tools (`get_project_status`, `compile_check`) get a single-result outcome envelope: decode before budget, omit the whole value rather than cut it, and treat compiler errors as a completed diagnostic with the report retained.
- The four read tools share one `BatchReadTool` runner; the write domains share plan records and failure helpers; the Network executor is injected.
- Rejected: positional records with `RespectRequiredConstructorParameters` (hundreds of worker, FakeWorker and test construction sites, net48 sites only a live run exercises); a domain-service interface or inheritance for the read flow (more machinery than the repetition justified); extracting only the response records (leaves four copies of the flow).

## Consequences
- A new payload or tool reuses the shared gate; a new member is required by default and becoming optional is a visible, registered act.
- The reader gives no compile-time completeness (a default value still passes); compiler-enforced `required` members were left as a later layer, not a replacement.
- The reader refuses a null-omitting root, so a payload cannot be both marked legacy and strictly read.
- The read-runner and write-record refactors were behavior-preserving, pinned by frozen canonical responses and the output-schema snapshot.
- Narrowed later: every tool is now structured and the legacy register is empty ([ADR-0008](0008-tool-surface-consistency.md)).

## Evidence
- In tree: [Phase 2 offline validation](../superpowers/acceptance/reports/2026-09-30-json-contract-phase2-offline-validation.md); [Phase 2 live verification](../superpowers/acceptance/reports/2026-10-01-json-contract-phase2-live-verification.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [Phase 1a worker wire plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-28-json-contract-phase1a-worker-wire.md)
  - [Phase 1b required-members design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-28-json-contract-phase1b-required-members-design.md)
  - [Phase 1b required-members plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-28-json-contract-phase1b-required-members.md)
  - [Phase 2 standalone tools plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-30-json-contract-phase2-standalone-tools.md)
  - [D-1 batch read tool design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-25-d1-batch-read-tool-design.md)
  - [D-1 batch read tool plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-25-d1-batch-read-tool.md)
  - [D-3/D-4/D-5 shared refactors plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-25-d3-d4-d5-shared-refactors.md)
