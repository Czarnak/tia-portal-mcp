# Remove redundant worker confirmation implementation plan

> **For agentic workers:** Use subagent-driven-development for the scoped implementation and independent review.

**Goal:** Implement Q5 by removing the internal worker confirmation flag while preserving access authorization and lifecycle user confirmation.

**Architecture:** Delete the request field, constant caller assignments and worker fences. Keep the existing policy catalogs, binding checks and host lifecycle elicitation.

**Tech Stack:** C#, .NET 10 host/tests, .NET Framework 4.8 worker, xUnit.

**Spec:** Q5 in `docs/adr/records/2026-10-17-codebase-simplification-review.md` and the user's explicit implementation request.

## Constraints and review focus

- Preserve host and worker access-policy authorization, including unknown-operation denial.
- Preserve read-write lifecycle acceptance/decline/cancel behavior and full-mode policy handling.
- Preserve native Portal confirmation handling and all unrelated validation.
- Remove the obsolete wire field from serialized requests and update affected test fixtures.
- No live engineering behavior is being changed; assess offline evidence before deciding whether live testing is needed.

## Task 1: Remove the internal flag and update its consumers

**Files:** `TiaMcpServer.Contracts/Worker/WorkerRequest.cs`, `TiaMcpServer/Worker/OpennessWorkerClient.cs`, `TiaMcpServer.OpennessWorker/Program.cs`, affected tests under `TiaMcpServer.Tests`.

- [x] Add a request-serialization regression and observe its expected failure before production edits.
- [x] Remove `Confirm`, all constant assignments, the worker fences and `ProjectLifecycle`'s `requiresConfirm` parameter.
- [x] Remove the obsolete source test and adapt unsupported Multiuser-key coverage without weakening rejection.
- [x] Run focused request, access-policy, Multiuser, schema and lifecycle confirmation tests.
- [x] Build serially and run the complete non-packaging suite.
- [x] Update durable documentation where necessary; validate docs and inspect the diff.
- [ ] Commit the complete implementation.

## Task 2: Review and publish

- [x] Have an independent adversarial reviewer inspect the whole change, including authorization and confirmation preservation.
- [x] Resolve material findings and repeat only relevant checks after changes.
- [ ] Prune this transient plan after recording the durable result.
- [ ] Push the new branch and open a pull request; do not merge.

## Execution record

- Branch: `refactor/remove-worker-confirm`, baseline `07bad2db`.
- Scope is fully specified by Q5 and the user; no new design decisions or dependencies are needed.
- Test and production edits have separate ownership; root serializes builds, tests and Git writes.
- RED: `WorkerJsonTests.Request_OmitsRetiredConfirmMember` failed as expected because the serialized request contained `confirm:false` (1 failed test).
- Serial real-assembly solution build passed with zero errors and nine nullable warnings already present before production edits.
- GREEN: focused request/policy/Multiuser/schema/lifecycle confirmation suite passed 385/385 tests, with no skips.
- Independent adversarial review passed for spec compliance and code quality, with no actionable findings. Offline verification is sufficient before push; the PR must disclose the template's maintainer live-verification step before merge and avoid claiming manually mixed host/worker compatibility.
- The first complete-suite tool call exceeded the context-mode 300-second RPC limit. A persistent-terminal run exposed three old JSON `confirm` assertions and one embedded fake-worker fence; that known-red run was stopped, all four expectations were removed, and the adversarial reviewer approved the scoped follow-up. A cross-file-type test/fixture scan found no remaining internal wire dependency. Production source did not change after its passing solution build.
- The expanded focused suite, including all subnet lifecycle tests and the corrected lease-concurrency fixture, passed 564/564 tests with no skips. Final complete-suite execution uses persistent terminal session `7966` and a TRX logger to avoid the MCP response limit.
- Final full non-packaging suite: 7,590 passed, zero failed, zero skipped, in 6 minutes 31 seconds; persistent terminal exited 0. No production source changed after the passing serial solution build. Packaging files were unchanged, so packaging tests were not required.
