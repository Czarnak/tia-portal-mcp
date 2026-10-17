# Codebase Review Runbook

This runbook describes how to review the whole repository for simplification, dead code, good
practice and consistency between tools. The codebase is too large for one useful pass (about 60,000
production lines across host, worker, contracts and FakeWorker), so a review is a series of runs.

It covers review only. A review run changes no code. Implementation follows a plan the maintainer
approved, under the repository [agent instructions](../../AGENTS.md). Never write TIA project names
or private project paths into findings or tracked files.

The last full review is the
[2026-10-17 simplification review plan](../superpowers/plans/2026-10-17-codebase-simplification-review.md)
(baseline `main` @ `f0c79f8`), with its
[verified findings appendix](../superpowers/plans/2026-10-17-codebase-simplification-review-findings.md).

## Why not split by project or only by area

- Host, worker and contracts are halves of the same features. A review split by project cuts every
  feature at the process boundary, and that boundary is where inconsistencies live.
- A review split by area finds local issues well: dead code, duplication, oversized files. It misses
  issues that cross areas. In the 2026-10-17 run, the most valuable findings crossed areas. One
  example was a per-item `projectPath` on content tools. Another was `NonRecoverableException`
  being mapped to four failure categories. The dead `AllowTiaConfirmations` field was reported
  separately by three reviewers, while the cross-tool reviewer saw only the host side.
- Verification is required. In that run the verifiers corrected 53 of 116 findings and refuted 2.
- The biggest correction came from the maintainer, not the code. Reviewers cannot check a design
  rule that is not written down.

## The method

### 1. Write the rules down first

Keep a short list of design rules that reviewers check against. Rules that can be checked
mechanically become tests next to
`TiaMcpServer.Tests/Tools/ToolOutputContractConformanceTests.cs`. CI then enforces them, so the
next review does not have to rediscover them.

Starting list. The rules marked *(target)* come from the 2026-10-17 decisions and are not yet
implemented:

- Content tools act only on the session binding and take no `projectPath`. `projectPath` exists
  only where it selects or creates a target: `bind_project`, `open_project`, `create_project`, and
  the destination of `save_project_as`. *(target, Q6)*
- Every tool has a closed input schema. An argument-shape error returns the tool's canonical
  document with `validation_error` and `isError: true`, and writes no audit record. *(target, Q4)*
- Failure categories come from `WorkerFailureCategories` constants, never string literals.
- Openness catch blocks never absorb `NonRecoverableException`. Portal loss is `portal_lost` in
  every domain. *(target, Q3)*
- Batch shape rules (1–50 items, unique operation IDs up to 256 characters) are defined once.
- A new worker method maps through `ProjectCapabilityCatalog.OperationFor`, and its name is defined
  once in Contracts.

### 2. Review one domain end to end

One slice is one domain followed through every layer:

1. the MCP tool;
2. the catalog;
3. the write pipeline (`Safety/Pipeline`);
4. `OpennessWorkerClient`;
5. the worker dispatch and its Openness code;
6. the payload contract back on the host;
7. the domain's Contracts, FakeWorker scenario and tests.

Compare each slice against a reference domain, normally the newest and cleanest (HMI or VCI). A
finding then reads "PLC does X, the reference does Y", not "this looks inconsistent". Keep a slice
to roughly 5,000–10,000 production lines, so that one reviewer can read it fully.

### 3. Sweep one rule across the whole repository

A sweep checks one concern everywhere. Examples: failure categories, binding and project paths,
argument validation, exception filters, constants versus literals, cursors, guard definitions.
Sweeps are cheap with jCodemunch searches and catch the drift that domain reviews miss.

### 4. Between full passes, review only what changed

Record the baseline commit of each review. Next time, review `git diff <baseline>..HEAD` together
with the code that change affects (jCodemunch `get_blast_radius`), checked against the rule list.
Reserve full passes for milestones, such as before a major release.

### 5. Keep a findings ledger

Findings keep stable IDs and a status: open, fixed, or won't-fix with a reason. Give reviewers the
won't-fix and accepted items, so they do not report them again. The worker-only probes (Q1) are
accepted for now.

### 6. Run the pipeline in this order

1. **Find.** Domain reviewers and rule sweepers work read-only, with jCodemunch per the
   [agent instructions](../../AGENTS.md). Markdown and `.csproj` files are not indexed; read them
   directly.
2. **Merge duplicates** across slices before verification, so the same finding is not verified
   several times.
3. **Verify.** For every finding, an adversarial verifier tries to refute it. The verifier checks
   string dispatch, JSON serialization, tests, FakeWorker and git-ignored harness scripts, and
   checks deliberate decisions in AGENTS.md and `docs/`.
4. **Decide.** Bring design questions to the maintainer before writing proposals for them.
5. **Plan.** Write the plan under `docs/superpowers/plans/`, list it in the
   [documentation index](../README.md), and update the rule list and the ledger.

Each run fits within 8 concurrent agents: one or two reviewers per slice, one sweeper, and one
verifier per reviewer.

## Suggested rotation

| Run | Domain review | Rule sweep |
| --- | --- | --- |
| 0 (once) | Baseline inventory: tools × rules matrix, dead-code scan, file sizes | Turn the agreed decisions into rules and tests |
| 1 | PLC, including Block | Failure categories and exception handling |
| 2 | Network | Binding and project paths |
| 3 | HMI and VCI | Argument validation and rejection shape |
| 4 | Lifecycle, binding and Multiuser | Constants and duplicated helpers |
| 5 | CLI, diagnostics and CI | Test infrastructure and FakeWorker structure |
| Later | Changes since the baseline only | Re-run the rule tests |
