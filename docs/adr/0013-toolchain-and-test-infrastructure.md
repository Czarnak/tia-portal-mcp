# ADR-0013: The toolchain moves asymmetrically, and its build and test infrastructure is source-owned and policy-bound

**Status:** Accepted
**Decided:** 2026-09-12 to 2026-10-24   **Recorded:** 2026-10-30
**Area:** process
**Current home:** [ARCHITECTURE.md section 1](../ARCHITECTURE.md#1-process-topology), [section 2](../ARCHITECTURE.md#2-host-startup-and-access-modes), [section 10](../ARCHITECTURE.md#10-testing); [building guide](../development/building.md#generated-openness-references)

## Context
- The host, tests and FakeWorker were on net8.0 with seven open, unmergeable Dependabot PRs. The Openness worker is pinned to net48 and cannot move.
- CI must build without TIA Portal. The tracked `ref/` binaries were opaque and unreviewable, and Multiuser needed a locked compile surface.
- A fixed worker timeout and a multi-minute full test run became operational pains.

## Decision
- Retarget only the host, tests and FakeWorker to net10.0, in one bisectable migration PR. Contracts stay netstandard2.0 and the worker stays net48. A stable SDK is pinned in `global.json` without prerelease. The NuGet tool stays framework-dependent and platform-agnostic; the standalone release stays self-contained win-x64. The System.IO.Pipelines assembly becomes a required worker companion file (a consequence of the new System.Text.Json graph), not a relaxed allowlist.
- Compile-only Siemens reference assemblies are generated from reviewable C# stubs under `reference-stubs/`. A drift gate compares a canonical source hash embedded in assembly metadata, not file bytes. Stubs are compile evidence only, never runtime or live evidence.
- The worker request timeout is one raise-only value, parsed once at startup (CLI over environment over default), applied to every request including `hello`; no per-method override.
- The default test run is parallel and keeps coverage collection and the coverage gate. Packaging tests are tagged and run in their own conditional CI workflow. Test classes that set FakeWorker process-environment variables stay serial, protected by a guard test.
- Rejected: merging Dependabot PRs one by one (non-mergeable, duplicated, no workflow evidence); landing dependencies before the framework move (a second compatibility boundary for no user benefit); dual-targeting net8.0 and net10.0 (duplicates tool-package and path-sensitive verification while leaving the real boundary, net48, unchanged); tracked opaque binaries (unreviewable); per-method timeouts (deferred without evidence of need); lowering coverage or adding retries to make a parallel CI green.

## Consequences
- Dependency bumps arrive as one coherent baseline.
- Stub changes are reviewable and CI-verifiable. Touching a product `.csproj`, `Directory.Build.props`, `global.json`, `reference-stubs/` or the verification scripts also requires the Packaging test run. Live behavior must still be proven on a real Portal.
- A raised timeout delays detection of a hung worker start, including `hello`; the docs must say so.
- Test-runtime gain is bounded by the serial FakeWorker tail. A class that sets a `TIA_MCP_FAKE_WORKER_*` variable must not leave its serial collection. Further runtime ideas live in `IMPROVEMENT_LOG.md`.
- The migration spec's ModelContextProtocol version pin is stale; only the decision to adopt the 2.x line stands.

## Evidence
- In tree: no kept live-verification reports; this ADR rests on offline and static evidence only.
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`:
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-09-12-dotnet-10-migration-design.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-12-dotnet-10-migration.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-09-28-multiuser-pr1-reference-contract-foundation.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/2026-10-15-configurable-worker-timeout.md
  - https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-24-test-suite-runtime-design.md
