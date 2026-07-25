# Acceptance Test Report — Phase 5 Certification Audit

**Branch / commit:** `docs/phase5-04-certification-documentation` / `2c7bd5065a331daae656645ef7e924efe2305c33`  
**AC document:** `docs/superpowers/acceptance/2026-07-23-phase5-reliability-lifecycle-integrity.md`  
**Date:** 2026-07-26  
**Report:** `docs/superpowers/acceptance/reports/2026-07-26-phase5-certification-audit.md`

---

## Audit basis and exception

This is a reconciliation audit, not a new TIA Portal test run. The user explicitly directed that no TIA Portal live/API tests be run, stated that the already-confirmed live results were satisfactory, and authorized proceeding on that basis. Therefore, a criterion labelled **PASS (approved live exception)** has prior recorded live evidence in the Phase 5 acceptance report, but was not rerun at this commit. It is an explicit user-approved certification exception, not fresh independent execution evidence.

Fresh, non-live verification at this head is independently recorded as follows:

- Serialized stub build: `dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true` — passed.
- Full test assembly: `dotnet vstest TiaMcpServer.Tests\\bin\\Debug\\net8.0\\TiaMcpServer.Tests.dll '--Logger:Console;Verbosity=normal'` — **620/620 passed**, 0 failed, 0 skipped.
- Scoped coverage: one Cobertura report; `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` — **0.8578** line rate, passed.
- Security remediation: `0b7a62f` redacts postcondition exception text at the former caller-visible sink; `BlockPostconditionVerifierTests` includes a sensitive-message regression. Its focused test set passed **5/5** and is included in the 620-test suite.
- Graph: `graphify update .` completed after the security fix; resulting artifacts are at `2c7bd50`, with no later C# change.

The earlier report at `2026-07-23-phase5-reliability-lifecycle-integrity.md` remains the source for the previous live V21 evidence and its caveats. This report supersedes only its final certification verdict.

---

## Results

| ID | Description | Test Type | Result | Evidence |
|---|---|---|---|---|
| AC-001 | Serialized CI/publish solution builds | Logic | PASS | `CiWorkflowTests`; current serialized stub build passed. |
| AC-002 | Scoped coverage configuration | Logic | PASS | `coverage.runsettings` plus current sole Cobertura report. |
| AC-003 | 80% coverage threshold | Logic | PASS | Current scoped line rate `0.8578 >= 0.80`; threshold script passed. |
| AC-004 | Persistent serialized two-process worker | Logic | PASS | Existing architecture/transport regression coverage included in 620/620 suite. |
| AC-005 | Ten-tool / seven-lifecycle-tool surface | Logic | PASS | MCP schema regression coverage included in 620/620 suite. |
| AC-006 | Status cannot switch A to B | API | PASS (approved live exception) | Prior live evidence and automated `ProjectOpenPolicy` coverage; prior run used a bound-session precondition variant. |
| AC-007 | Status does not open supplied path | API | PASS (approved live exception) | Prior live no-project scenario passed; user approved no rerun. |
| AC-008 | Status reads only open project | API | PASS (approved live exception) | Prior live evidence and automated coverage; prior run used a bound-session precondition variant. |
| AC-009 | Lifecycle uses internal state probe | API | PASS | FakeWorker operation-name regression coverage included in 620/620 suite. |
| AC-010 | Guarded lifecycle preview/apply flows | API | PASS (approved live exception) | Prior V21 save, SaveAs, archive, close, and open evidence; user approved no rerun. |
| AC-011 | Failed lifecycle call preserves binding | Logic | PASS | Binding regression coverage included in 620/620 suite. |
| AC-012 | Successful read remains non-binding | Logic | PASS | Non-binding status regression coverage included in 620/620 suite. |
| AC-013 | Successful transition binds worker truth | Logic | PASS | Resolved-path binding regression coverage included in 620/620 suite. |
| AC-014 | Rebind without resolved path fails | Logic | PASS | `postcondition_failed` regression coverage included in 620/620 suite. |
| AC-015 | SaveAs `rebind:false` rejected before write | API | PASS (approved live exception) | Prior V21 rejection evidence plus automated guard coverage; user approved no rerun. |
| AC-016 | Supported SaveAs binds copied project | API | PASS (approved live exception) | Prior V21 preview/apply and canonical copied-path evidence; user approved no rerun. |
| AC-017 | Failed SaveAs preserves original binding | API | PASS | FakeWorker SaveAs failure coverage included in 620/620 suite. |
| AC-018 | Unverifiable SaveAs path is failure | Logic | PASS | `postcondition_failed` / warning regression coverage included in 620/620 suite. |
| AC-019 | Success preserves warnings | API | PASS | Warning propagation regression coverage included in 620/620 suite. |
| AC-020 | Warnings do not mask failures | Logic | PASS | Categorized-failure regression coverage included in 620/620 suite. |
| AC-021 | Warning budget is enforced | Logic | PASS | Warning cap/truncation regression coverage included in 620/620 suite. |
| AC-022 | Safety-token protections preserved | API | PASS (approved live exception) | Prior live reused-token evidence plus automated token cases; user approved no rerun. |
| AC-023 | Missing/duplicate bundle docs rejected | Logic | PASS (approved live exception) | Prior duplicate-document V21 evidence plus automated missing-document coverage; user approved no rerun. |
| AC-024 | Unsafe bundle paths rejected | Logic | PASS | `BlockImportStager` negative-path coverage included in 620/620 suite. |
| AC-025 | Deterministic exact staging filenames | Logic | PASS | Deterministic staging regression coverage included in 620/620 suite. |
| AC-026 | Byte-identical block update round trip | API | PASS (approved live exception) | Prior authoritative `.xml` round trip passed; previous all-document bundle caveat is retained; user accepted results and waived rerun. |
| AC-027 | Edited block update compiles | API | PASS (approved live exception) | Prior V21 edited-bundle import, re-export, and compile evidence; user approved no rerun. |
| AC-028 | Malformed block input cannot alter block | API | PASS (approved live exception) | Prior V21 deterministic rejection and unchanged re-export evidence; user approved no rerun. |
| AC-029 | Failed postcondition is not success | Logic | PASS | Coordinator failure regression coverage included in 620/620 suite. |
| AC-030 | SCL source has a valid compile unit | Logic | PASS (approved live exception) | Prior V21 FC evidence plus automated supported-type generation coverage; user approved no rerun. |
| AC-031 | SCL create-block compiles in V21 | API | PASS (approved live exception) | Prior V21 create, resolve, compile, and cleanup evidence; user approved no rerun. |
| AC-032 | Failure categories are deterministic/safe | Logic | PASS | Category rendering regression coverage included in 620/620 suite; AC-041 remediation removes the identified diagnostic disclosure. |
| AC-033 | Timeout write is never retried | API | PASS | FakeWorker timeout request-count regression coverage included in 620/620 suite. |
| AC-034 | Crash/protocol-loss write is never retried | API | PASS | FakeWorker crash/protocol request-count regression coverage included in 620/620 suite. |
| AC-035 | Recoverable failures need no manual recovery | API | PASS (approved live exception) | Prior same-session V21 follow-up-read evidence plus automated remainder; user approved no rerun. |
| AC-036 | Automated and Round 4 regression suites green | API | PASS | Serialized stub build passed; current assembly **620/620** passed; scoped coverage **0.8578** passed. |
| AC-037 | Live evidence identifies Siemens runtime | API | PASS (approved live exception) | Prior report records Windows/TIA/Openness/project provenance and cleanup; user approved no rerun. |
| AC-038 | Repository documentation matches behavior | Logic | PASS | Task 4 documentation/schema verification passed; documentation remains at this head. |
| AC-039 | Source skill updated; cache untouched | Logic | PASS | Authorized source checkout commit `ed6ebce`; validator passed; installed cache was not modified. |
| AC-040 | Graph reflects final code | Logic | PASS | Post-security-fix `graphify update .` artifacts committed in `2c7bd50`; no subsequent C# change. |
| AC-041 | No secrets/protected-data leakage | Logic | PASS | Former P2 `SEC-P5-W1B-001` remediated in `0b7a62f`; focused redaction test **5/5** and full **620/620** suite passed; static audit finds no remaining caller-visible raw postcondition exception text. |
| AC-042 | External inputs validated at both boundaries | Logic | PASS (approved live exception) | Prior V21 `rebind:false`/malformed-bundle evidence plus automated negative-path coverage; user approved no rerun. |
| AC-043 | Audits avoid uncertain-outcome false success | Logic | PASS (approved live exception) | Prior audit/live evidence plus timeout/crash/postcondition automated coverage; user approved no rerun. |
| AC-044 | Phase 6 capabilities remain deferred | Logic | PASS | Production diff/schema and `IMPROVEMENT_PLAN.md` retain the documented Phase 6 exclusions. |
| AC-045 | Bound status divergence warns without binding | Logic | PASS | `WarnOnBindingDivergence` regression coverage included in 620/620 suite. |

---

## Summary

**Total criteria:** 45  
**Passed with fresh non-live evidence:** 28  
**Passed under explicit user-approved live no-rerun exception:** 17  
**Failed:** 0  
**Blocked:** 0

## Certification verdict

**CERTIFIABLE WITH EXPLICIT USER-APPROVED LIVE-TEST EXCEPTION.** All 45 criteria have recorded evidence at `2c7bd5065a331daae656645ef7e924efe2305c33`. The prior P2 security finding is fixed and regression-tested; the serial build, full test suite, coverage threshold, and graph freshness are current. The 17 TIA-dependent criteria are accepted on the user's explicit confirmation of prior satisfactory live results and instruction not to rerun them. This is not equivalent to a fresh end-to-end V21 certification run.
