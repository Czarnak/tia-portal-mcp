# ADR-0001: PLC content is exchanged as strict single-object external source and created by server-owned rules

**Status:** Accepted
**Decided:** 2026-07-26 to 2026-10-09   **Recorded:** 2026-10-30
**Area:** plc
**Current home:** [ARCHITECTURE: PLC writes](../ARCHITECTURE.md#plc-writes-plc_write); [PLC operations](../SupportedOperations/PLC_OPERATIONS_SUMMARY.md); [import/export options](../SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md)

## Context
- SimaticML is the only round-trip format at first and is far larger than the equivalent source text.
- Openness external-source import has no multiplicity switch: it creates every object a file declares, and `CreateFromFile` leaves a visible `PlcExternalSource` node in the project.
- Openness V21 has no `CreateOB`. OB import uses the supplied number verbatim; a duplicate singleton imports silently and only a full compile fails.
- Siemens types cannot be referenced by the test project, so any logic left in the Siemens shells has no offline coverage.

## Decision
- UDT, global DB and SCL FB/FC/OB are readable and writable as `format=source` behind a language-aware eligibility gate; other languages stay on XML. Type operations default to `source`, block operations to `xml`; a default flips only for all block languages at once.
- A source write is a strict update, never an upsert: the document declares exactly one object and its name equals the resolved target, checked by a comment- and string-aware declaration scanner before the project is touched.
- Reads choose multiplicity (`withDependencies`, default off). A dependency-closure read is context only and is refused on write, so what a default read returns is what a write accepts.
- `GenerateOptions` and `GenerateBlockOption` are passed explicitly on every path; the temporary external-source node is removed by a scope, and a leftover node is a postcondition failure.
- `update_block_logic` reports a closed typed `blockImportOutcome` with a conservative mutation boundary and `ContentRelation` left unknown, instead of implying the exported document equals the request.
- Each component splits into a Siemens-free, unit-tested half and a thin Siemens shell.
- `create_block` accepts `blockType` OB with a closed `obEventClass`; the server chooses the number (singleton base, or lowest free for multi-instance). One shared Contracts table and rule serve host planning and worker, so preview equals applied and the worker result wins. Singleton collisions are blocked by `plc_ob_singleton_exists`.
- Rejected: upsert semantics for multi-object source (would create unaddressed objects the preview never named). Rejected: caller-chosen or renumbered OB numbers, event-specific OB parameters, `SynchronousCycle`, Motion Control, redundancy and ProDiag OBs, and CPU-support modelling (Portal refuses unsupported classes with its own message). Rejected: routing `create_block` through external source (a working path with no gain).
- Rejected: STL on source (different `.awl` file type, no evidence).

## Consequences
- Callers get readable, round-trippable source for UDT, DB and SCL with one object per write; multi-object source is refused.
- Rules that still bind code: the Siemens-free/shell split, explicit options on every export path, re-resolve a block after any import (it is destroyed and recreated), and never let callers choose OB numbers.
- Live traps for future harnesses: a stale handle after block recreation reads as a false attribute reset; Portal renames a colliding member to `<name>_1`, so probes need per-run unique names.
- Live findings the design relies on: the two-argument `GenerateSource` defaults to no dependencies; an interface-changing FB update keeps instance DBs; non-optimized DBs have no offset column, so no offset handling exists.
- `plc_ob_singleton_exists` is kept because Portal's own rejection was never reachable through the tool; retiring it needs a probe outside `plc_write` (see [ADR-0012](0012-portal-as-rejection-authority.md)).
- Type-content reads sit in the read-only capability set, consistent with block-content reads, so they are available in every access mode ([ADR-0007](0007-access-tiers-lifecycle-confirmation-and-binding.md)).
- The write path later moved onto the guarded pipeline ([ADR-0004](0004-guarded-single-call-write-pipeline.md)); the content exchange rules above are unchanged by it.

## Evidence
- In tree: [OB creation spike](../superpowers/acceptance/reports/2026-10-08-ob-creation-spike.md); [OB creation live acceptance](../superpowers/acceptance/reports/2026-10-09-ob-creation-live-acceptance.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [UDT/DB external-source design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-07-26-udt-db-external-source-design.md)
  - [UDT/DB external-source plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-07-26-udt-db-external-source.md)
  - [SCL external-source design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-07-27-scl-external-source-design.md)
  - [SCL external-source plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-07-27-scl-external-source.md)
  - [Truthful block-import outcomes plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-27-wave2-i2-truthful-block-import-outcomes.md)
  - [Type-content binding policy plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-27-wave2-t2-type-content-binding-policy.md)
  - [OB creation design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-08-ob-creation-design.md)
  - [OB creation plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-08-ob-creation.md)
