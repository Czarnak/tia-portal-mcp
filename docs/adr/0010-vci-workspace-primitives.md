# ADR-0010: VCI is exposed as standalone-only workspace primitives over leaf-expanded scopes

**Status:** Accepted
**Decided:** 2026-10-14 to 2026-10-16   **Recorded:** 2026-10-30
**Area:** vci
**Current home:** [ARCHITECTURE: VCI reads and writes](../ARCHITECTURE.md#vci-reads-and-writes); [VCI operations summary](../SupportedOperations/VCI_OPERATIONS_SUMMARY.md)

## Context
- The TIA Portal Version Control Interface (workspaces, mapped objects, export and synchronize) needed an MCP surface.
- A live spike showed Openness works on leaves only, container arguments are unsafe, and a whole-PLC export runs for minutes.
- Status costs real time per mapping; a SIMATIC SD export can crash the Portal process; import rewrites engineering objects.
- A killed worker cannot say what it was doing, and a Portal crash leaves the session with no usable project.
- Live testing of the first candidate (2026-10-16) found a Unified HMI mapping-path defect, slow collision scans on export, and a broken same-session recovery after a Portal restart.

## Decision
- The tools are primitives. Git, workspace upkeep, `connect`, workspace groups, VCI settings and library scopes stay with the user or out of scope; the tool states its limits instead of orchestrating.
- Reads are a separate `vci_read` (all modes), because status is costly and the standalone-only gate does not fit a general read tool. Writes are `vci_write`, on the guarded pipeline ([ADR-0004](0004-guarded-single-call-write-pipeline.md)). Both are standalone `.ap21` only, since there is no experience base for local-session differences.
- Scopes are uncapped, but the worker expands them to leaves and Openness never receives a container. Results are counts plus the last attempted leaf, not per-leaf detail; which leaves differ is a `vci_read` question.
- Import runs in one ExclusiveAccess transaction after a status prefilter; every other write runs plain. Sync touches only source-changed leaves and skips conflicts; an unknown status is treated as a conflict.
- Before each leaf the worker overwrites a request-matched breadcrumb file, so the host can name the last attempted leaf after a timeout kill. A Portal loss returns `portal_lost` and invalidates the binding at once (see [ADR-0011](0011-fail-closed-surface-and-portal-loss.md)). Recovery reopens only after fresh empty-attachment proof and an explicit `open_project` with force-rebind.
- HMI mapping paths are repaired by project/device/software ownership proof, never by guessed names or cached native handles.
- Rejected: reads inside an existing read tool (cost and gating differ); a scope cap (users export whole devices); export transactions or exclusive locks to speed things up (no evidence, UI lock); replacing export collision checks with a snapshot, count check or handle cache (a stale view could overwrite files).

## Consequences
- The surface is small and safe by construction: no container reaches Openness, and a lost Portal yields a named last leaf and a defined recovery path.
- Long exports stay slow. The export scan grows with leaves times mappings; this was recorded, not fixed, and no speedup is claimed.
- Local sessions, server projects, software units, system blocks, library scopes and classic HMI remain unsupported.
- VCI keeps its own scope vocabulary and a private path policy; consolidating the duplicated path rules is an open follow-up in [IMPROVEMENT_LOG](../IMPROVEMENT_LOG.md).
- Live acceptance failed the frozen first candidate and passed a scoped follow-up of the repaired one; wider native coverage is untested.

## Evidence
- In tree: [VCI spike](../superpowers/acceptance/reports/2026-10-15-vci-spike.md); [VCI live acceptance](../superpowers/acceptance/reports/2026-10-16-vci-tools-live-acceptance.md).
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - [VCI tools design](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-15-vci-tools-design.md)
  - [VCI tools plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-15-vci-tools.md)
  - [VCI live findings fixes plan](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-16-vci-live-findings-fixes.md)
