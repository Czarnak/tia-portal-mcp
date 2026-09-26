# Bug-Fix Wave Orchestrator Runbook

This runbook turns the approved [parallel bug-fix delivery design](../superpowers/specs/2026-09-26-open-bug-parallel-pr-delivery-design.md)
into an operational procedure for an agentic orchestrator. It is written for Wave 1, but the same
barriers apply to later waves after their own plans are approved.

It coordinates work; it does not authorize implementation, remote writes, issue changes, merges,
or live TIA Portal operations. Follow the repository [agent instructions](../../AGENTS.md), the
[build instructions](building.md), and each lane plan for exact commands and acceptance details.
Do not copy private project paths into tracked documentation or GitHub. Assign one unique, stable,
privacy-safe alias per actual target and scenario, for example `Lifecycle Fixture A`, `Lifecycle
Fixture B`, `PLC Fixture C`, and `Network Fixture D`. Keep the one-to-one alias mapping, exact
targets, and raw traces only in ignored local evidence.

## Purpose and decision hierarchy

The orchestrator is responsible for four outcomes:

1. every lane is developed from `main` in an isolated branch and managed worktree;
2. concurrent lanes never share write ownership of a file;
3. no live TIA test starts until the entire wave is implementation-complete and qualified offline;
4. every resulting PR targets `main` directly and remains independently visible in release history.

When instructions conflict, stop and apply this order:

1. the user's latest explicit instruction and exact-scope authorization;
2. the nearest applicable `AGENTS.md` instructions;
3. the approved wave design;
4. the lane's approved implementation plan;
5. this runbook.

Do not reinterpret a lower-level document to broaden authority granted by a higher-level source.
In particular, a plan's live-acceptance section describes evidence that may be collected later; it
does not authorize that work.

## Wave 1 lane matrix

All four branches start from the same verified `origin/main` after the documentation-planning PR
has merged. No lane starts from another feature branch.

| Lane | Issues and slice | Branch | Detailed plan | Live requirement |
| --- | --- | --- | --- | --- |
| L1 | #70, #81, lifecycle slice of #78 | `fix/lifecycle-rebind-preview-safety` | [Lifecycle rebind and preview safety](../superpowers/plans/2026-09-26-wave1-l1-lifecycle-rebind-preview-safety.md) | Required after the wave barrier, using `Lifecycle Fixture A`/`Lifecycle Fixture B` |
| K1 | Failure-category item #82.7 | `fix/batch-failure-categories` | [Batch failure categories](../superpowers/plans/2026-09-26-wave1-k1-batch-failure-categories.md) | None, but K1 must still pass every applicable readiness gate before any other lane goes live |
| P1 | #71, #73, compile/cross-reference slice of #82.2 | `fix/plc-read-integrity` | [PLC read integrity](../superpowers/plans/2026-09-26-wave1-p1-plc-read-integrity.md) | Required after the wave barrier on `PLC Fixture C`; serialize read-only and engineering checks |
| N1 | #76 | `fix/network-device-creation-names` | [Network-device creation names](../superpowers/plans/2026-09-26-wave1-n1-network-device-creation.md) | Required after the wave barrier on `Network Fixture D` |

`Refs`/`Closes` policy is addressed below. The umbrella issues #78 and #82 remain open until all of
their slices are complete.

## Roles and authority

| Role | Responsibilities | Write authority |
| --- | --- | --- |
| Wave orchestrator | Verify the base, create managed worktrees, dispatch owners, maintain the status ledger, schedule gates, assemble the combined candidate, coordinate GitHub operations, and serialize merges | Coordination checkout and explicitly authorized integration/fan-in branches only; no lane implementation |
| Lane owner | Execute one approved plan task-by-task, keep the worktree clean, commit granular changes, and report evidence | Exactly one lane branch/worktree and its whole-file allowlist |
| Independent reviewer | Review plan conformance, behavior, safety, tests, docs, and the exact diff; report findings to the owner | Read-only; never fixes findings directly in the lane worktree |
| Live operator | Execute the approved live program against the frozen combined candidate and capture evidence | Live actions only after the wave gate is open and exact targets/actions are freshly authorized |
| Fan-in owner | Reconcile shared maintained documentation after implementation PRs merge | A new docs-only branch from then-current `main` |

One person or agent may hold more than one role at different times, but may not bypass the role
boundaries. In particular, the orchestrator must not become a second concurrent writer in a lane,
and the implementer must not approve its own independent review.

## Preflight

Do not create implementation worktrees until every item below is true.

- [ ] The approved design, all four Wave 1 plans, and this runbook are merged into `main`.
- [ ] Refresh remote state and record the exact `origin/main` commit that will be the common wave base.
- [ ] Confirm the coordinating checkout is clean and has no unrelated user work.
- [ ] Refresh each issue, linked PR, labels, and open/closed state. Record discrepancies; do not silently plan from stale issue text.
- [ ] Confirm each lane plan still matches the code and documentation on the recorded base.
- [ ] Inventory attached/managed worktrees and reuse only a suitable free checkout. Never overwrite or manually delete an in-use worktree.
- [ ] Confirm the four plan allowlists are disjoint as whole files. A different section of the same file is still an overlap.
- [ ] Create the wave ledger and assign one lane owner plus a different reviewer for every lane.
- [ ] Schedule builds and tests according to the repository's serial-execution rules. Source editing may proceed in parallel; shared build, port, process, or TIA resources may not.
- [ ] Set `live_wave_gate=CLOSED`.

Record at least the following before dispatch:

| Field | Required value |
| --- | --- |
| Wave | `Wave 1` |
| Base | Exact `origin/main` SHA |
| Lane | L1, K1, P1, or N1 |
| Branch | Exact branch from the lane matrix |
| Managed worktree | Identity and absolute local path |
| Owner/reviewer | Assigned identities |
| Plan revision | Plan path plus blob or commit SHA |
| Allowlist | Exact whole-file list from the plan |
| State | Initially `QUEUED` |
| Head/evidence | Empty until produced |

## Create and assign managed worktrees

Create one managed worktree per lane from the recorded base. The branch attached to each worktree
must match the lane matrix. Verify all four base SHAs before allowing any edit.

The isolation model is:

```text
recorded origin/main
|- fix/lifecycle-rebind-preview-safety   -> L1 owner/worktree
|- fix/batch-failure-categories         -> K1 owner/worktree
|- fix/plc-read-integrity               -> P1 owner/worktree
`- fix/network-device-creation-names    -> N1 owner/worktree
```

Rules:

- one write-capable owner per lane worktree;
- every command for that lane uses the assigned absolute worktree path;
- no implementation agent edits the coordinating checkout;
- no lane owner edits, rebases, merges, pushes, or cleans another lane;
- reviewers inspect read-only and send findings back to the lane owner;
- replacement owners are assigned only after the orchestrator inspects and records the preserved worktree state;
- managed worktrees are archived through the worktree lifecycle tooling, never removed with shell deletion.

## Dispatch prompt contract

Every lane prompt must include all of the following. Do not rely on an agent inferring the boundary
from the branch name.

- exact lane, issues/slice, branch, base SHA, and absolute worktree path;
- links to the approved design, this runbook, and the lane plan;
- the complete production, contract, test, fixture, and maintained-documentation allowlist;
- explicit reserved files and neighbouring lanes;
- the instruction that other agents are working concurrently and their changes must be preserved;
- task-by-task RED/GREEN/refactor and granular commit expectations from the plan;
- required focused, full-offline, real-reference, package, documentation, and diff gates, including which are legitimately not applicable;
- a ban on push, PR mutation, merge, live TIA work, or issue changes unless separately assigned and authorized;
- stop conditions: newly required file, base drift, ambiguous contract, unrelated dirty state, destructive recovery, or uncertain mutation outcome;
- the required handoff format: head SHA, commits, changed files, command/evidence summary, review findings, unresolved risks, and lane state.

Reusable prompt skeleton:

```text
Own lane <LANE> only in <ABSOLUTE_WORKTREE> on branch <BRANCH>, based on <BASE_SHA>.
You are not alone in the repository. Preserve other agents' changes and do not edit outside this
plan's whole-file allowlist: <ALLOWLIST>. Read <DESIGN>, <RUNBOOK>, and <PLAN> before editing.
Execute one plan task at a time using a deterministic RED, the smallest GREEN, scoped refactoring,
focused verification, diff review, and the plan's granular commit. Do not push, create/update a PR,
merge, alter issues, or run live TIA tests. If any required file is outside the allowlist, stop and
report PAUSED_REPLAN. Return the branch/head SHA, commit list, changed files, verification evidence,
review status, and remaining risks.
```

## Whole-file ownership and pause/replan rule

The plan allowlist is a hard write boundary, not a suggestion.

1. Before each task, the owner lists the files it expects to read and edit.
2. The owner may read across the repository but writes only allowlisted files.
3. A newly discovered need for any other file moves the lane immediately to `PAUSED_REPLAN`.
4. The owner does not create a duplicate helper, parallel contract, compatibility shim, or second
   documentation location to avoid the ownership conflict.
5. In Wave 1, waiting for the same-wave owner to merge is not an option because no lane merges
   before the combined barrier. Close the live gate and either obtain review and approval for a
   whole-file ownership/plan amendment before freeze, or remove/defer the affected lane and obtain
   approval for the redefined wave barrier.
6. Later waves may wait for an owner PR to merge only when their approved plans explicitly allow
   it. Work resumes from the appropriate updated `main`, with a revised allowlist and refreshed
   tests. Never transfer a file informally between active lanes.

If an agent accidentally changes an unowned file, stop both write activity and cleanup. Inspect the
file and worktree first so another agent's work is not reverted. Preserve the evidence, identify the
actual owner, and replan the recovery.

## Lane implementation and review loop

Each lane follows its detailed plan. The orchestrator enforces this loop per task:

1. **Baseline:** verify branch/base, clean status, and current task scope.
2. **RED:** add or select the narrowest deterministic regression and observe the expected failure.
   A TDD RED is always offline; it is never a live TIA operation.
3. **GREEN:** make the smallest allowlisted change that satisfies the regression.
4. **Refactor:** improve only code touched by the task while focused tests remain green.
5. **Verify:** run the task's focused tests and inspect the scoped diff.
6. **Commit:** make the plan's granular conventional commit. Do not combine plan tasks merely to
   reduce commit count.
7. **Review:** an independent reviewer examines the commit range and returns findings without
   editing the worktree.
8. **Repair:** the lane owner reproduces valid findings, adds/updates a failing test where feasible,
   fixes them, commits the repair separately, and requests reviewer confirmation.
9. **Advance:** begin the next task only when the current task has no unresolved finding that would
   invalidate its contract.

After the final task, run the plan's full focused and offline gates, required real-V21-reference
build, package verification, documentation checks, allowlist/diff checks, and branch self-review.
Use `N/A` only when the plan explicitly makes a gate unnecessary, and record the reason. Stub,
FakeWorker, static-source, build, or package success is never live evidence.

## Status ledger and transitions

Keep source readiness, remote PR state, and live state as separate columns. At minimum track:

| Lane | Base SHA | Head SHA | Worktree clean | Allowlist clean | Focused | Full offline | Real reference | Package | Docs/diff | Independent review | Source state | PR state | Live result |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| L1 | | | | | | | | | | | `QUEUED` | `NOT_OPEN` | `NOT_RUN` |
| K1 | | | | | | | `N/A` only per plan | | | | `QUEUED` | `NOT_OPEN` | `N/A` |
| P1 | | | | | | | | | | | `QUEUED` | `NOT_OPEN` | `NOT_RUN` |
| N1 | | | | | | | | | | | `QUEUED` | `NOT_OPEN` | `NOT_RUN` |

Normal source-state transitions are:

```text
QUEUED
  -> WORKTREE_READY
  -> IMPLEMENTING
  -> OFFLINE_GATING
  -> INDEPENDENT_REVIEW
  -> IMPLEMENTATION_COMPLETE
  -> FROZEN
  -> CANDIDATE_INCLUDED
  -> LIVE_QUALIFIED or LIVE_NOT_APPLICABLE
  -> MERGE_READY
  -> MERGED
```

`REWORK` returns to `IMPLEMENTING`. `PAUSED_REPLAN`, `BLOCKED`, and `OUTCOME_UNKNOWN` are stop
states and never advance automatically. A PR may be opened after independent offline readiness if
authorized, but `PR_OPEN` does not imply `FROZEN`, live qualification, or merge readiness.

## Hard wave barrier for live TIA acceptance

Initialize and display the following state prominently in every wave update:

```text
live_wave_gate=CLOSED
```

It remains closed until **all four** branches, including K1, meet every applicable condition:

- every planned implementation and repair commit exists;
- the worktree is clean and the branch contains only allowlisted changes;
- focused and full offline tests are green;
- required stub and real-reference builds are green;
- required package verification is green;
- maintained documentation and diff checks are green;
- independent review is complete and all actionable findings are resolved;
- no lane is `REWORK`, `PAUSED_REPLAN`, `BLOCKED`, or `OUTCOME_UNKNOWN`.

An early-ready L1, P1, or N1 branch **must not run live TIA tests by itself**. It waits at
`IMPLEMENTATION_COMPLETE`. K1 has no live action, but incomplete K1 work still keeps the whole wave
gate closed.

When all conditions are met, freeze the four exact heads. Record:

```text
wave_base_sha=<origin/main SHA>
l1_sha=<exact frozen head>
k1_sha=<exact frozen head>
p1_sha=<exact frozen head>
n1_sha=<exact frozen head>
live_wave_gate=CLOSED
```

Tagging is optional; immutable recorded SHAs are mandatory. Do not amend, rebase, merge into, or
add a commit to a frozen lane while constructing or testing the candidate.

## Build the combined-wave candidate

Create a temporary local-only integration branch in a separate managed worktree from a fresh
verification of `origin/main`. Do not push it and do not open it as a fifth implementation PR.

1. Record the candidate base SHA.
2. Merge the four frozen heads in the deterministic lane order L1, K1, P1, N1 unless a different
   order was reviewed and recorded before candidate construction.
3. If a merge conflicts, do not resolve it only on the candidate. Set `live_wave_gate=CLOSED`, move
   the responsible lane to `PAUSED_REPLAN`, and repair the source branch through its normal loop.
4. Record every source SHA, the merge order, candidate commit SHA, and candidate tree SHA.
5. Confirm the candidate diff is exactly the union of the four lane allowlists.
6. Run the strict union of the lane plans' final offline checks, plus the repository-level serial
   restore/build/test/package and documentation/diff gates described by the repository instructions.
7. Obtain an independent integration review of the combined diff and results.

If the candidate is clean and green, change wave status to `READY_FOR_LIVE_AUTHORIZATION`. This is
still not permission to touch TIA Portal:

```text
live_wave_gate=CLOSED
wave_status=READY_FOR_LIVE_AUTHORIZATION
```

Any lane-head change, unexpected upstream/base drift before live qualification, conflict
resolution, or candidate content change invalidates the candidate and all evidence tied to it.
Close the gate, discard the candidate as an integration artifact, refreeze all four heads, rebuild
it, and rerun the full combined offline gate. After qualification, the authorized serial merges of
the exact frozen lane heads do not retroactively invalidate accepted candidate evidence when every
cumulative current-main tree matches the corresponding accepted candidate content and its gate
passes. Any mismatch or unrelated upstream change still invalidates the evidence.

## Open and execute the live program

Only after the combined candidate is ready, request fresh authorization for the exact live program.
The request must identify the exact disposable targets, selectors, ordered operations, expected
postconditions, and restoration/discard route. Maintain an ignored local live manifest that maps
each unique shared alias to exactly one target; never reuse an alias for a different project or
scenario. Tracked docs and GitHub summaries use only those stable privacy-safe aliases.

Set `live_wave_gate=OPEN` only when all of the following are simultaneously true:

- the frozen SHA manifest still matches all four branches and the candidate;
- the candidate remains clean and its offline evidence is current;
- no other TIA, PLC, PLCSIM, worker, or fixture activity can interfere;
- fresh authorization names the exact targets and actions about to run;
- the live operator has a written sequential run order and evidence checklist.

Run every authorized Wave 1 live scenario **sequentially against the same combined candidate**.
Never run one lane from its feature worktree and another from the candidate. Before each scenario,
confirm the candidate identity, capture baseline state, and confirm that the preceding scenario was
restored or intentionally left in the documented state. Follow each lane plan for its checks; do not
broaden them opportunistically.

Capture authoritative evidence appropriate to the operation: post-read results, audit entries,
project status, created identities, diagnostics/completeness, and restoration or discard outcome.
Process exit, stdout, or an MCP success envelope alone is insufficient. Keep exact paths and raw
private traces in ignored local evidence; publish only sanitized summaries.

Close the gate immediately after the authorized sequence completes, fails, is interrupted, or
becomes ambiguous:

```text
live_wave_gate=CLOSED
```

Mark each lane `LIVE_QUALIFIED` or `LIVE_NOT_APPLICABLE` only after its evidence has been reviewed
against the frozen candidate manifest.

## Failure and unknown-outcome handling

- **Ordinary offline failure:** keep the affected lane in `REWORK`; no live authorization request.
- **Review finding:** return it to the same lane owner. A repair commit changes the head and requires
  a new independent review plus all affected gates.
- **Required out-of-allowlist file:** move to `PAUSED_REPLAN`; do not work around ownership.
- **Combined-candidate conflict or failure:** close the gate and repair source branches. Never make
  a candidate-only production fix.
- **Live assertion failure with known state:** stop the sequence, preserve evidence, restore only by
  the authorized route, and decide whether source rework is required.
- **Timeout, worker crash, lost response, or disconnect after a possible mutation:** mark
  `OUTCOME_UNKNOWN`. Do not retry. Inspect authoritative target state, audit, project status, and
  restoration state first. Any replay requires a new decision and fresh exact-target authorization.
- **Lane-head/content change or unexpected base drift after candidate creation:** invalidate the
  candidate and its live evidence, return the wave to the closed barrier, and repeat
  freeze/candidate/offline/live qualification. The recorded serial merge of exact frozen heads is
  the only exception, and only while cumulative tree comparisons and current-main gates pass.
- **Partial GitHub mutation:** read the remote resource back before retrying. Do not create duplicate
  branches or PRs to recover from an uncertain response.

Never describe a blocked, skipped, unavailable, partial, or unknown result as accepted evidence.

## Publish and open the four PRs

Remote writes require explicit authorization. Use the configured GitHub MCP for branch publication,
PR creation/update, and read-back. If the required GitHub operation is unavailable, stop rather than
silently switching to a different remote-write mechanism.

For each lane:

1. Verify the local branch name and exact head SHA.
2. Push only that branch. Never push the temporary combined candidate.
3. Create one PR with `base=main` and the lane branch as `head`.
4. Include the lane plan, scoped issue references, allowlist, commit summary, offline/reference/
   package evidence, review status, and sanitized live status in the body.
5. Read the PR back through GitHub and verify repository, base, head, title, body, labels, commit SHA,
   and changed-file list. Confirm no file belongs to another lane.
6. Record the PR URL and remote head SHA in the ledger.

Use `Refs` while an issue still lacks any required implementation, maintained documentation, review,
or live evidence:

- L1 uses `Refs #78`; use `Refs #70` and `Refs #81` until their complete evidence is accepted. Never
  close umbrella #78 from L1.
- K1 uses `Refs #82` only; it never closes umbrella #82.
- P1 uses `Refs #82`; keep #71 and #73 as references until their complete acceptance is present.
  Never close umbrella #82 from P1.
- N1 uses `Refs #76` until the distinct-name live acceptance is accepted.

Change a single-issue reference to `Closes` only when that issue's whole contract, maintained docs,
review, and required evidence are complete. That metadata change also requires remote-write
authorization. Release-note visibility comes from each branch being merged through its own PR into
`main`, not from the `Closes` keyword.

## Serialized merge and current-main gates

Merge one PR at a time. Before each merge, reconstruct or inspect the exact proposed merge result
against the current `main` and run its relevant offline gate. A passing check from the wave's old
base is not sufficient.

Prefer keeping the frozen lane commits unchanged while validating their merge result against the
new base. If an affected lane must actually be rebased, merged with `main`, or repaired, its head
changes: close the wave gate, invalidate the combined candidate/live evidence, rerun review and
gates, and rebuild the combined candidate before continuing. Force-push and merge remain separately
authorized remote writes.

After each merge:

- refresh `origin/main` and record the new SHA;
- verify the merged PR and issue state by reading them back from GitHub;
- run the current-main offline integration gate;
- construct and test the next PR's merge result against that current `main`;
- compare the cumulative source tree with the corresponding portion of the accepted combined
  candidate; stop on unexplained drift.

After the fourth merge, require the resulting `main` source tree to match the accepted combined
candidate's four-lane content, then rerun the complete serial offline gate. Do not infer final-main
health from the four independent PR checks.

## Documentation fan-in

Shared maintained documents are reconciled after the four implementation PRs merge. Create a new
docs-only branch from the then-current `main`; do not add fan-in changes to a still-open lane.

- reconcile only the shared docs reserved by the design, such as `README.md` and
  `docs/ARCHITECTURE.md`;
- preserve lane-owned supported-operation updates already merged;
- describe only behavior supported by merged code and accepted evidence;
- update documentation indexes when a new document was intentionally added;
- run link, formatting, diff, and current-main offline checks appropriate to the change;
- open a separate PR against `main` and read it back through GitHub.

If the fan-in review exposes a code or evidence discrepancy, do not paper over it. Reopen the
relevant technical lane or create an explicitly approved follow-up.

## Cleanup

Cleanup begins only after the relevant PR is merged and no review or recovery work still depends on
its checkout.

- [ ] Record final PR, merge commit, issue state, and evidence locations.
- [ ] Confirm every lane working tree is clean or that all remaining state is intentionally preserved.
- [ ] Archive each managed lane worktree with its PR association; do not delete it manually.
- [ ] Archive the temporary candidate only after final-main tree comparison and evidence handoff.
- [ ] Keep private live artifacts out of tracked files and PR bodies.
- [ ] Delete remote branches only when explicitly authorized and repository policy calls for it.
- [ ] Mark the shared docs fan-in complete.
- [ ] Begin planning the next wave only from the resulting verified `main`.

## Final orchestrator checklist

- [ ] The documentation-planning PR is merged and the exact common `origin/main` base is recorded.
- [ ] Four managed worktrees exist, with one owner and one branch each.
- [ ] Every dispatch includes the complete plan, allowlist, stop rules, and no-live instruction.
- [ ] Whole-file ownership remains disjoint; overlaps moved to `PAUSED_REPLAN`.
- [ ] Every task followed RED/GREEN, scoped review, granular commit, and independent review loops.
- [ ] All four lanes are implementation-complete, clean, and green on every applicable offline gate.
- [ ] `live_wave_gate=CLOSED` remained in force while any lane was incomplete.
- [ ] All four lane SHAs, the base SHA, merge order, candidate SHA, and candidate tree SHA are frozen.
- [ ] The combined candidate passed final offline and independent integration review.
- [ ] Fresh exact-target authorization was obtained only after candidate readiness.
- [ ] All live TIA scenarios ran sequentially against the combined candidate; no early-ready lane ran alone.
- [ ] Unknown outcomes were inspected and never automatically retried.
- [ ] Each lane PR was pushed separately, opened against `main`, and read back through GitHub.
- [ ] `Refs` and `Closes` reflect actual evidence; umbrella issues remain open until all slices finish.
- [ ] Merges were serialized and every current-main integration gate passed.
- [ ] Final `main` matches the accepted four-lane content and passes the complete offline gate.
- [ ] Shared maintained documentation was reconciled through a separate fan-in PR.
- [ ] Managed worktrees and evidence were archived safely.
