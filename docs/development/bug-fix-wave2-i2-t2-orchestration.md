# Wave 2 I2/T2 Orchestration Handoff

This runbook is the future-session handoff for the two Wave 2 lanes that are qualified and
independently implementable from the 2026-09-27 planning baseline:

- **I2:** truthful `update_block_logic` outcomes for issue #72.
- **T2:** `get_type_content` binding/access policy for issue #82.1.

It supplements the general mechanics in [Bug-fix wave orchestration](bug-fix-wave-orchestration.md)
and the approved [open-bug delivery design](../superpowers/specs/2026-09-26-open-bug-parallel-pr-delivery-design.md).
Where the older runbook hardcodes the four-lane Wave 1 barrier, this document controls I2/T2.

## Authority and exclusions

The approved inputs are:

- [I2 truthful block-import outcomes plan](../superpowers/plans/2026-09-27-wave2-i2-truthful-block-import-outcomes.md)
- [T2 type-content binding policy plan](../superpowers/plans/2026-09-27-wave2-t2-type-content-binding-policy.md)

Plan approval authorizes only the implementation and local verification described in each plan.
It does not authorize push, PR creation, issue edits, merge, live TIA access, or mutation of any
project. Obtain the required authority at each external/live boundary.

This planning change must itself be reviewed, committed, pushed, and merged to `origin/main` before
creating either implementation worktree. Record the merged planning commit and the blob hashes of
both plans plus this handoff. Refuse dispatch from an uncommitted, unmerged, or different plan.

Do not create a branch, worktree, owner assignment, implementation task, qualification run, PR, or
merge for B2 or N2. B2 still requires approved Q75 and current-main planning; N2 still requires
approved Q77 and current-main planning. I2 must merge and pass integration before B2 may claim
worker `Program.cs` or the other shared response files.

## Current-main gate

The planning snapshot was clean `main` at `9b0fb50f`, after Wave 1 PRs #84-#87 merged. A future
session treats that hash only as provenance, never as permission to use a stale base.

Before assigning either lane:

1. Confirm the primary checkout has no unrelated local changes requiring preservation.
2. Switch the primary checkout to `main` if necessary and fetch/pull `origin/main` with a
   fast-forward-only operation.
3. Record the exact base commit and tree hash.
4. Refresh issues #72 and #82 plus linked PRs. Stop if another implementation already owns a slice.
5. Re-read the approved design and both lane plans from the refreshed tree.
6. Diff every planned production/test/document file from `9b0fb50f` to current `main`. If an
   interface, response envelope, policy seam, or test assumption changed, revise and reapprove the
   affected plan before implementation.
7. Confirm I2 and T2 whole-file allowlists are still disjoint.

Do not use a documentation-planning branch as an implementation base. Each PR targets `main`
directly.

## Owners and worktrees

Create one isolated branch/worktree per lane, using the managed worktree mechanism when available:

| Lane | Branch | Plan | Live class |
| --- | --- | --- | --- |
| I2 | `fix/truthful-block-import-outcomes` | I2 plan | Required mutating acceptance before merge |
| T2 | `fix/type-content-binding-policy` | T2 plan | No live prerequisite and no live action authorized |

Use the same refreshed `main` base if both lanes launch together. Never stack one lane on the
other. Assign one implementation owner per worktree and a different read-only reviewer. Every
worker prompt must say that other work exists, whole-file ownership is exclusive, and the worker
must preserve and accommodate all edits outside its allowlist.

The lane plan's allowlist is exhaustive. Line-level separation does not permit two owners of one
file. On an unexpected file need, move the lane to `PAUSED_REPLAN`; do not make the edit first.

## Fixed ownership boundary

I2 owns its required worker capability, typed import-outcome contract and validator, all three target
invocation boundaries, temporary source-node lifecycle, import diagnostic sanitizer, structural
compiler-availability observation, block import/verification flow, pre-import worker refusal
decoration, typed host dispatch provenance and synthesis, worker response propagation,
host/generic-batch result propagation, focused tests/FakeWorker scenarios, and
`docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`.

T2 owns only `OperationPolicyCatalog.cs`, `TiaPortalSession.cs`, its policy/access/transport/type
tests, and its new source-contract test. It owns no SupportedOperations file because current
documentation already describes `get_type_content` as a read.

Shared repository files reserved from both lanes include `WorkerRequest.cs`,
`BatchOperationRequest.cs`, safety-token/audit classes, `README.md`, and
`docs/ARCHITECTURE.md`. I2 exclusively owns `WorkerProtocol.cs`, worker `Program.cs`,
`CompileChecker.cs`, `ExternalSourceScope.cs`, `OpennessWorkerClient.cs`, `WorkerCallResult.cs`,
`BatchWorkerInvoker.cs`, `WorkerResponse.cs`, FakeWorker `Program.cs`, the test project file, and
generic batch-result files until its integration gate completes.

## Lane state ledger

Maintain a local ledger with base/head/tree hashes, current state, gate evidence, and authority.
Do not put exact disposable-project paths or names in tracked files.

| State | Meaning |
| --- | --- |
| `PLANNED` | Approved plan exists on eligible current `main`; no worktree edits yet |
| `ACTIVE` | Owner is executing one plan task in its worktree |
| `PAUSED_REPLAN` | Allowlist, interface, scope, or base assumption changed |
| `IMPLEMENTATION_COMPLETE` | Plan tasks committed; worktree clean |
| `REVIEWED` | Independent findings resolved on the recorded head |
| `OFFLINE_READY` | Focused, full stub, real-reference where available, package, docs, and diff gates passed |
| `FROZEN` | Exact I2 base/head/tree recorded; no further edits or concurrent merge allowed |
| `LIVE_ACCEPTED` | Authorized I2 live scenario passed on the exact frozen head |
| `PR_READY` | Required evidence is complete and remote-write approval may be requested |
| `MERGED` | Authorized PR merge completed |
| `INTEGRATED` | Current-main post-merge gates passed |

T2 moves from `OFFLINE_READY` to `PR_READY` only after incorporating current `main`, repeating all
candidate gates, and completing independent review on that exact candidate head. It does not require
`FROZEN` or `LIVE_ACCEPTED`.
I2 cannot move to `PR_READY` without both.

## Dispatch protocol

Use `superpowers:subagent-driven-development` for implementation unless the user selects the native
execution method. In either method, execute tasks in plan order, use focused RED/GREEN evidence,
and make the plan's conventional commit after each task. Do not combine lane commits.

The initial implementation prompt for each lane includes:

```text
Implement <LANE> from <PLAN> in branch/worktree <BRANCH/WORKTREE>.
Read the approved design, this orchestration handoff, and the full lane plan before editing.
Your whole-file allowlist is exactly the plan's allowlist; all other files are reserved.
You are not alone in the codebase. Preserve other work, do not revert unrelated edits, and pause
before any unexpected file need. Follow each RED/GREEN step, run builds serially, commit after each
task using the specified message, and report commands, exit codes, commit SHA, changed files, and
remaining offline/real-reference/live limitations. Do not push, open a PR, merge, edit issues, or
run TIA without explicit authority.
```

After every task commit, a fresh reviewer checks:

- task requirements and exact interfaces;
- focused test evidence, including the observed RED rather than only final GREEN;
- failure categories, sanitization, compatibility, and no retry/rollback claims;
- changed files against whole-file ownership; and
- whether a finding changes the plan or only the implementation.

Resolve findings in the same lane, rerun affected tests, and commit the fix separately when it is
reviewable. An agent report is not verification; the orchestrator inspects the diff and reruns the
required command.

## Shared execution constraints

- Run independent source review and test authoring in parallel when safe, but serialize restore,
  build, test, pack, ports, worker-copy targets, and all TIA operations.
- No live operation is used to create the RED. Focused offline/FakeWorker tests establish RED/GREEN.
- Keep exact paths, selectors, raw traces, and fixture identity in ignored local evidence. Tracked
  reports use aliases such as `Fixture A`.
- Never treat stub compilation, FakeWorker success, static-source tests, package inspection, or a
  historical live run as current V21 acceptance.
- A timeout or lost response does not authorize replay. Inspect project/session state first and
  obtain fresh authority when the original authorization no longer covers the next action.

## Offline readiness

For each lane, run its focused commands and then the full serial commands in its plan. Record:

- command, working directory, exit code, and test totals;
- installed V21 reference-build result or an explicit unavailable limitation;
- the sole generated package and doctor-package verification result;
- `git diff --check`, `git status --short`, branch head/tree, and exact changed-file list;
- documentation/link audit and absence of exact fixture names/paths; and
- independent review disposition.

`OFFLINE_READY` requires a clean worktree and all committed task heads. Any code change after a
gate invalidates that lane's review and affected evidence.

## Independent merge policy

There is no I2/T2 wave-wide combined candidate. The approved design permits independent later-wave
merges, and these plans explicitly choose that route:

- T2 may request PR/push/merge authority after `OFFLINE_READY`, current-main incorporation, focused
  and full stub gates, the real-reference build when available, package/documentation/diff gates,
  and repeated independent review all pass on the exact candidate head.
- I2 may continue implementation in parallel, but must refresh from current `main` before its live
  freeze if T2 merged first.
- Once I2 is `FROZEN` or live authorization is pending, do not merge T2 or any other lane. A base or
  head change closes the I2 live gate.
- If I2 becomes live-ready before T2, either complete I2 freeze/live/merge first or keep I2
  unfrozen while T2 merges. Never qualify one I2 commit and merge another.

Create exactly one PR per lane, each targeting `main`. PR creation and every push require explicit
remote-write authority. Read back the remote base/head/title/body after creation or update.

Use:

- I2 title `fix(plc): report truthful block import outcomes`; body `Refs #72` until accepted live
  evidence permits `Closes #72`.
- T2 title `fix(policy): align type content read binding`; body `Refs #82` and explicitly names
  slice #82.1. Never close the umbrella issue from T2.

Serialize merges. After each authorized merge, pull current `main`, inspect the merge result, and
run that lane's focused filter plus the serial stub build/full tests. Do not start a dependent lane
or release a reserved file until the merged lane is `INTEGRATED`.

## I2 live gate

Only I2 has a mandatory live gate. Before requesting authorization:

1. Incorporate current `main`; rerun I2 focused/full/reference/package gates and independent review.
2. Freeze and record exact base, lane head, tree, package, and test evidence.
3. Verify the worktree is clean and no process will mutate it.
4. Present the exact disposable `.ap21`, requested preview/apply/restoration actions, and evidence
   location to the user. Obtain fresh authorization for that target and those actions.

Execute the I2 plan's two scenarios serially on the exact frozen head: baseline, ordinary Simatic ML
preview/apply, independent read/compile/audit/status, restoration, then source-format preview/apply,
temporary source-node removal proof, independent evidence, and final restoration or discard. Use
unchanged operation lists/tokens and retain later-item skip proof. Validate target-call fields, source
artifact lifecycle, typed outcome, post-read, audit, and final project status; process exit alone is
insufficient.

Any head/tree/base change, unexpected target state, transport ambiguity, incomplete restoration,
or missing evidence closes the gate. Do not patch and continue under the same authorization.

## Completion and successor handoff

The current-scope wave is complete only when both PRs are `INTEGRATED`, or when the user explicitly
removes a lane from scope and the ledger records why. Final reporting lists every merge SHA and
verification command, separates offline/real-reference/live evidence, and retains unresolved issue
slices.

Only after I2 integration may a future planning session refresh `main`, review Q75, and write B2's
current-main plan. N2 remains separately gated by Q77. Do not copy an I2/B2 or T2/N2 plan forward
without rechecking the exact merged code and evidence.
