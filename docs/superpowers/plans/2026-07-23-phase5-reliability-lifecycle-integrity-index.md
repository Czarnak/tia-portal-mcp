# Phase 5 Reliability and Lifecycle Integrity Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved Phase 5 reliability improvements without expanding the ten-tool MCP surface or weakening write safety.

**Architecture:** Execute four ordered plans. Plan 1 establishes deterministic CI gates. Plan 2 separates read-only status from lifecycle probing and hardens response/binding semantics. Plan 3 repairs PLC block import and SCL generation. Plan 4 updates documentation and certifies the complete phase against the approved 45 acceptance criteria.

**Tech Stack:** .NET 8 MCP host, .NET Standard 2.0 contracts, .NET Framework 4.8 Openness worker, Siemens TIA Portal V21 Openness, xUnit, FakeWorker IPC integration tests, PowerShell, GitHub Actions, Coverlet/Cobertura, graphify.

## Authoritative Inputs

- Design: `docs/superpowers/specs/2026-07-23-phase5-reliability-lifecycle-integrity-design.md`
- Approved criteria: `docs/superpowers/acceptance/2026-07-23-phase5-reliability-lifecycle-integrity.md`
- Manual evidence: `priv/MCP_TOOL_TEST_REPORT.md`
- Round 4 baseline: `priv/ROUND4_SUMMARY.md`

## Ordered Plans

1. `docs/superpowers/plans/2026-07-23-phase5-01-ci-quality-foundation.md`
   - AC-001–AC-003
   - Serialized solution builds, scoped coverage, deterministic 80% gate.
2. `docs/superpowers/plans/2026-07-23-phase5-02-lifecycle-response-integrity.md`
   - AC-004–AC-022, AC-032–AC-035, AC-042–AC-045
   - Side-effect-free status, internal lifecycle probe, worker-ground-truth binding, SaveAs safety, categories, warnings, and no-retry guarantees.
3. `docs/superpowers/plans/2026-07-23-phase5-03-plc-block-write-repairs.md`
   - AC-023–AC-031, with shared AC-032, AC-042, AC-043 coverage
   - Safe deterministic document staging, update verification, and compilable SCL block creation.
4. `docs/superpowers/plans/2026-07-23-phase5-04-certification-documentation.md`
   - Final certification and fresh evidence for AC-001–AC-045
   - Full regression, live V21 acceptance, documentation, graph refresh, security review, and external source-skill authorization gate.

## Global Constraints

- Follow strict TDD for every production change: add a test, run it and record RED, add the minimum implementation, rerun GREEN, then refactor.
- Run solution builds serially:

  ```powershell
  dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
  ```

- Preserve the persistent two-process host/worker architecture, the exact ten-tool MCP surface, and the exact seven lifecycle MCP methods.
- Preserve preview/token/confirm/apply, single-use token, state-hash, and audit protections.
- Never automatically retry a write after timeout, crash, pipe loss, malformed protocol, or unverifiable postcondition.
- Treat worker `ResolvedProjectPath` as the only binding ground truth after successful open/create/SaveAs transitions. Never fall back to caller intent.
- Validate all untrusted paths, document names, block types, languages, and response fields at boundaries.
- Keep new data immutable: get-only properties, read-only collections, and new result objects instead of mutation.
- Do not edit installed plugin-cache files. The source `tia-portal-mcp` skill update requires the owning source checkout plus explicit user authorization.
- Do not remove live-defect wording from `README.md` until the corresponding live V21 criteria pass.
- After code changes, run `graphify update .` and certify the graph against the final code commit.
- Use task-scoped review during implementation and one consolidated whole-branch review after all implementation tasks, matching the established project workflow.

## Program Exit Gate

Phase 5 is complete only when:

- [ ] All four plans are complete in order.
- [ ] Every approved criterion AC-001 through AC-045 has fresh evidence.
- [ ] Restore, serialized build, full tests, and scoped line coverage gate all pass.
- [ ] Round 4 regression coverage passes unchanged.
- [ ] Live V21 acceptance passes on a disposable project copy.
- [ ] No protected audit state, source project, credential, or installed plugin cache was modified outside the approved scope.
- [ ] Final branch review has no unresolved CRITICAL or HIGH findings.
