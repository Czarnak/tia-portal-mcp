# Independent Import Compilation Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Preserve successful block imports when post-import compilation fails and return compilation evidence separately.
**Architecture:** Reuse the typed block import outcome; change the success gate while preserving compilation and final verification.
**Tech Stack:** C#, net48 worker, net10.0 host and xUnit.
**Spec:** docs/superpowers/specs/2026-11-08-import-compilation-design.md

## Constraints and review focus

- Branch fix/issue-221-import-compile-results, local main 5af42c14.
- Preserve source/XML compilation scopes and diagnostic sanitization.
- Cover compiler errors, unavailable evidence, actual import/final-read failures, and batch continuation.
- Preserve fatal context loss; do not broaden to PLC types or block creation.
- Serialize builds/tests and live TIA operations with the GlobalDB worktree.

## Task 1: Separate block import and compilation success

Files: BlockPostconditionVerifier.cs; verifier/coordinator/PLC outcome and batch tests; operation references and tool guidance.

- [x] Add regressions for completed imports with failed/unavailable compilation; observe RED.
- [x] Change the import-success gate with the smallest implementation; observe GREEN.
- [x] Verify preserved compile evidence, final verification, actual failures, and later batch execution.
- [x] Document independent compile results.
- [x] Run focused tests, installed V21 build, non-packaging suite and docs verification.
- [x] Review independently, fix material findings, and commit (0dd0089f).

## Decisions

- User supplied the implementation outcome and execution split; continue without another design-approval round.

## Verification and rulings

- RED: verifier/compiler/coordinator regressions failed as expected: 5 failed, 39 passed.
- GREEN: the same focused command passed 44/44.
- Installed V21 serial solution build passed with 8 warnings and 0 errors.
- Full non-packaging suite passed 8,404/8,404 (6m01s).
- Docs verification passed with 0 errors and 2 transient spec/plan warnings.
- Independent review found no actionable findings.
- Ruling: reuse existing compilation fields and retain source/XML compile scopes; narrower scope would hide diagnostics and a new envelope would duplicate the contract.
- Ruling: preserve sanitization; compile_check remains the route for detailed messages.
- Ruling: leave PLC type import, block creation, and broader pre-existing exception policies unchanged; those have separate contracts outside the requested block-update complaint.
- Ruling: retain the internal lost-session observation as portal_lost; accepting a fatal loss as ordinary unavailable compilation would invalidate the safety boundary.
- Ruling: use an installed-assembly build for native acceptance rather than a redundant stub build; no Siemens API signatures changed.

- Native acceptance: successful source import retained one compile error, the next batch item executed, target hash/readback and target-only compile passed, fixtures were freshly absent, and restored PLC compilation had 0 errors. No save.
