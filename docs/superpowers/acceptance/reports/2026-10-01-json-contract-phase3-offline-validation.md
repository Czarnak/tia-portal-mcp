# JSON Contract Phase 3 / Write-Safety Phase 2 — Validation Record

Status: offline implementation qualification and the installed-V21 reference build passed.
Subsequent separately authorized runtime, client, artifact, and restoration evidence is recorded in
the [live lifecycle validation](2026-10-01-json-contract-phase3-live-validation.md).
This report preserves the offline code/protocol evidence boundary.

## Candidate and authorization

- Branch: `phase3-json-contract`, the maintainer's requested current branch.
- Implementation starting base: `6b10dc28812947fc802cf96c8abc6dfa8d981ffa`.
- Qualified code/test candidate: `8f0e26f83db4faf6cd243fef7489f6419db3925b`.
- Later plan/report/documentation commits do not change the qualified production/test sources.
- The maintainer requested implementation of the [lifecycle plan](../../plans/2026-09-30-json-contract-phase3-lifecycle.md),
  authorized subagents as useful, and requested a commit after each implementation/fix step.
- The scope is the six lifecycle tools, seven lifecycle guard IDs, shared binding/confirmation
  integration, typed results/verification, conformance retirement, and documentation. Network and
  legacy batch token paths remain active. No release/tag or redesign-wide completion is claimed.

## Verification

| Gate | Result |
| --- | --- |
| Restore | Passed using the ignored workspace cache; `final-restore-workspace-cache.log` |
| Serial Release solution build with reference stubs | Passed, zero errors and seven pre-existing xUnit2031 warnings, 19.07 seconds; `final-stub-build-green.log` |
| Focused binding, pipeline, lifecycle, producer, and registered protocol regressions | Passed selected overlapping runs: pipeline 162/162, combined caller/protocol 395/395, expanded caller 32/32, response-cap repair 115/115, and packaging-cache regression 3/3 |
| Final full host Release suite | 4,640 passed, zero failed or skipped, 7 minutes 35 seconds; `final-full-coverage-pass.log` and `final-PASS-TestResults/phase3-full-pass.trx` |
| Existing 80% line coverage threshold | Passed: Cobertura line rate `0.9386`, 9,997/10,650 lines; branch rate `0.8578`. Both collector copies have identical SHA hashes |
| Changed-document links and package README absolute links | Passed 13-file, 182-link validation; no unresolved file links or relative package README links, balanced fences, report indexed |
| Final `git diff --check` | Passed; no whitespace errors |
| Installed-V21 Release solution Rebuild | Passed with `UseTiaPortalReferenceStubs=false` and the installed `PublicAPI/V21/net48` assemblies; zero errors, seven pre-existing xUnit2031 warnings, 36.44 seconds. Compile/reference evidence only, not live acceptance |
| Independent final review | One P2 total-document cap finding reproduced with four failing regressions and resolved in `4595c4b`; focused 115/115 passed. Final fix diff reviewed with no unresolved findings |

The primary coverage artifact is
`final-PASS-TestResults/2c785ebd-9d12-4e41-ac29-e87882c83f72/coverage.cobertura.xml` in the ignored
execution workspace. Optional added-production executable-line analysis found 435/469 covered
lines (92.75%), limited to instrumented host/contracts lines; it is not worker/Siemens runtime
coverage. Focused sets overlap and must not be summed into a separate suite total.

## Implemented contract

Lifecycle calls use the shared single-call pipeline with public `dryRun` and `acknowledge` inputs,
full-only access, explicit lifecycle binding preparation, and the existing pinned lease. The
default verified-project gate remains the policy for ordinary project writes. Source ownership,
source/destination identity, save-as rebinding, and fail-closed recovery remain part of the
lifecycle boundary. Default-on confirmation ignores agent acknowledgement and requires form
elicitation `accept` plus boolean `confirm:true` for fired acknowledge guards; the opt-out path
requires the exact fired set. Hard blocks and dry runs never elicit. State is re-resolved after
acceptance before mutation.

Responses use one canonical document in text and `structuredContent`, with typed single outcomes
and basic-status verification. Pre-mutation rejection has top-level `error` and MCP `isError:true`;
an attempted mutation/verification failure has `success:false`, `error:null`, and `isError:false`,
retaining typed evidence. Every call, including dry runs and refusals, has one guarded audit record
with the exact response/hash and `user` versus `agent` satisfaction provenance. Accepted client
elicitation does not establish that a human saw a dialog.

Result/verification values use whole-value omission at 60,000 canonical characters; the complete
document is capped at 180,000. Omission-only does not reverse successful execution/verification.
Callers recover missing evidence by status reads or artifact inspection, never mutation replay.
The document cap also covers preview/blocked metadata: source status or effects can be omitted
whole, and oversized guard messages retain the catalog consequence plus an explicit omission marker
while guard IDs, severity, operation IDs, acknowledgement state, phase, and verdict remain. Omission notices disclose reduced evidence rather
than presenting shortened paths as complete targets.
Caller cancellation can occur after mutation; known outcomes are retained in the audit while
cancellation propagates, so current state still needs inspection before any new call.

## Execution decisions

- The initial full-suite run exposed obsolete legacy-contract assertions and filesystem sandbox
  denials. The affected tests were migrated to the guarded lifecycle/wire contracts; the final
  host-suite result is recorded separately in the verification table.
- Restore/build uses an ignored workspace NuGet cache under `priv/json-contract-phase3/packages`.
  The existing worker dependency-copy rule concatenates `NuGetPackageRoot`, so the parent build
  command supplies that property with a trailing separator. Packaging tests launch child builds
  that do not inherit a command-line MSBuild property: the first host run exposed the same missing
  separator in those children. The final host commands also set process-local `NUGET_PACKAGES`
  for the workspace restore cache and environment `NuGetPackageRoot` with the trailing separator
  so child builds inherit it. Earlier diagnostic
  runs are retained. This is an environment workaround, without a production-source or
  dependency-version change.
  The diagnostic host run passed 4,637 tests and failed three packaging-cache tests, with zero
  skips; it is not the acceptance run. The final corrected run passed all 4,640.
- Independent review reproduced a total-document cap gap for oversized preview/blocked path
  evidence. The regression fix extends budgeting to whole source-status/effects evidence and
  detailed guard messages while preserving the guard/verdict/provenance contract. Final reviewed
  candidate and gates are recorded above; this fix does not establish live acceptance.

## Frozen live acceptance still required

Freeze the code/base candidate after all offline gates and the installed-V21 reference build.
Obtain fresh authorization for the exact disposable source and destination targets; use fixture
aliases in tracked material. The pending matrix must cover:

- Open, create, save, save-as, archive, and close, including persisted artifact checks and restoration.
- `closes_source_project`, `discards_unsaved_source_changes`, `discards_unsaved_changes`,
  `archive_without_save`, `archive_discards_restorable_data`, `archive_inside_project_folder`, and
  `target_exists`; exercise existing-target refusal for both create and save-as.
- Actual client acceptance/decline, unsupported-capability refusal, and dry runs that neither mutate
  nor elicit.
- Typed mutation/verification outcomes, resulting binding identity, and audit evidence.

After timeout, crash, disconnect, or possible mutation, inspect current state and artifacts before
deciding on any new authorized call. Never automatically replay an unknown lifecycle outcome.
Any code/base change invalidates frozen acceptance evidence. Offline/FakeWorker tests, a reference
build, and a rendered prompt do not establish all runtime, client, persistence, and restoration
requirements. PLC download, runtime/plant acceptance, Network/batch migration, publishing, push,
and merge are outside this validation record.
