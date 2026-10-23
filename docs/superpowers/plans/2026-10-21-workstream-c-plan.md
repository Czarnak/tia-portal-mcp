# Workstream C: PR sequence and wave-1 plans

Source: Workstream C of the [codebase simplification review](2026-10-17-codebase-simplification-review.md), re-surveyed against `main` @ `03a2363` on 2026-10-21 (after A, all of B, D-2 and E-3 merged). Each item ships as its own PR, so each gets one release-notes line.

## Survey result

All eleven items are still valid. Changes since the review:

- **C-0** no longer has to fix HOSTDOMAINS-14; B-9 already passes the pinned path in `PlcWriteDomain.MutateAsync`.
- **C-2** loses its single-project rule once C-0 deletes `projectPath`. What remains is the shared 50-item/256-character rules and one message wording.
- **C-0's unbound gate cannot live in `ProjectSessionBinding.TryResolveWithSnapshot`**, as the review suggested. Several callers must keep working unbound: `get_project_status`, post-close verification, the lifecycle probes and test helpers. The gate belongs in `OpennessWorkerClient`, keyed by method class.

## Decisions (maintainer, 2026-10-21)

- **C-1 lands before C-0.** This supersedes the review's "C-0 before C-1". C-1 derives each tool's allowed root keys from its input schema, so when C-0 removes a `projectPath` parameter, the server rejects it automatically. In the old order, seven tools would silently ignore a stale root `projectPath` until C-1 merged. During that gap, `close_project({projectPath: other})` would close the bound project.
- **C-1 scope is root and nested.** Every argument-shape error returns the tool's own canonical document with `validation_error`, `isError: true`, and no audit record. That covers unknown or mistyped root keys, missing required arguments, unknown or mistyped item members and retired item members.
- **JSON null on an optional boolean is accepted only where the schema advertises null.** Today that is only `bind_project.forceRebind`. Treating null as "omitted" would make `dryRun: null` execute a write.
- **No `contractVersion` bump** for C-1.
- **Defaults stated to the maintainer, not objected to:**
  - C-3 adds no `TransportLoss` set, because it would have one consumer.
  - C-8 keeps the raw `hmi_*` names in the capability list, so there is no contract change.
  - C-10 relies on the existing unknown-member rejection rather than a retired-member message.

## PR sequence

| Wave | PR | Depends on | Contract change | Live TIA |
|---|---|---|---|---|
| 1 | C-1 strict arguments (M) | none | yes: 11 tools close, 5 change rejection shape | no; real-client smoke |
| 1 | C-3 failure-category constants (S) | none | no | no |
| 1 | C-4 non-interactive guard rule (S) | none | no | no |
| 1 | C-7 read cancellation (S) | none | behaviour only | no |
| 1 | C-8 worker method lists (S) | none | no | light dispatch smoke |
| 1 | C-10 remove `update_tag` `isSafety` (S) | none | yes: field removed | no |
| 2 | C-0 bound-project-only content tools (L) | C-1 | yes (MINOR) | yes |
| 3 | C-2 shared batch rules (S) | C-0 | message text | no |
| 3 | C-6 shared cursor plumbing (S) | C-0 | no | no |
| 4 | C-5 host-sealed `list_network_objects` cursor (M) | C-0, C-6 | cursor values, new `cursor_binding_mismatch` | scoped smoke |
| 4 | C-9 paging default and matrix (S) | C-5 | `read_hardware_config` cursor-only default 50→100 | no |

Why this order:

- **C-0 is the hub.** Its file overlap with the others: C-1 16 files, C-2 8, C-6 7, C-5 6, C-7 6, C-10 6, C-9 4.
- **C-2 and C-6 go after C-0.** C-0 deletes the code regions they would otherwise rewrite.
- **C-5 goes after C-6.** It can then use the shared seal/open helper instead of adding a fourth copy.
- **C-9 goes last.** Its paging matrix documents the final paging behaviour.

### Wave-1 merge order

Each PR is developed in its own worktree from `main`. The order is C-3, C-4, C-10, C-8 (after its smoke), C-7, then C-1 last.

C-1 touches the most shared files: C-7's four read tools, and the `PlcGuardedWriteMcpTests` that C-10 also uses. Merging it last means it rebases once. C-8 and C-10 edit different regions of the worker `Program.cs`.

### Decisions still open (later waves)

- **C-0:** RESOLVED and implemented (decisions by the maintainer on 2026-10-22, live gate passed 2026-10-23); see the [C-0 plan](2026-10-22-c0-bound-project-content-tools.md).
  - `projectPath` is removed from `get_project_status` and from `save_project_as`'s source assertion.
  - An unbound batch read returns one call-level root error.
  - "Bind first" uses `binding_conflict`.
  - The audit `projectPath` is the pinned bound path.
  - `search_equipment_catalog` is gated too.
  - The client's `projectPath` parameters go end to end.
  - Deferred: the worker-side identity backstop, and the tree-cursor message bug, which gets its own PR.
- **C-2:** RESOLVED (maintainer, 2026-10-23); see the [C-2 plan](2026-10-23-c2-shared-batch-rules.md).
  - No single-project rule: C-0 removed every item `projectPath`.
  - One `OperationValidation(IsValid, Category, Error)` record replaces the four result shapes. It keeps the member name `Error`, so existing callers keep compiling.
  - All three oversized-`operationId` echo sites are fixed: the HmiWrite/VciWrite prefix, PLC's cross-item check and `WriteRun`'s duplicate check. `WriteRun.ValidateInput` uses the shared rules.
  - Deferred: the unbounded operation-name echo, logged as a follow-up.
- **C-6:** RESOLVED (maintainer, 2026-10-23); see the [C-6 plan](2026-10-23-c6-shared-cursor-plumbing.md).
  - With `preserveUnboundEpoch` removed, unbound cursors keep their epoch everywhere. This is the tree/HMI rule, which hardware pages now adopt.
  - Categories and messages stay byte-identical.
  - The logged tree-cursor message bug does not exist as described. The nearest real defect, oversized tree/HMI cursors, is re-logged and gets its own PR. `Seal` keeps the size data for it.
- **C-5:**
  - Bind the worker session identity in the sealed state, or not.
  - Foreign-process category. Recommended: `invalid_cursor`.
- **C-9:** also change `list_network_objects`' default from 50 to 100, or not.

## Wave-1 plans

- [C-1 Root and nested argument strictness](2026-10-21-c1-closed-tool-arguments.md)
- [C-3 Failure-category constants](2026-10-21-c3-failure-categories.md)
- [C-4 Non-interactive guard rule](2026-10-21-c4-noninteractive-guards.md)
- [C-7 Read batch cancellation](2026-10-21-c7-read-cancellation.md)
- [C-8 Shared HMI/VCI worker method lists](2026-10-21-c8-worker-method-lists.md)
- [C-10 Remove `update_tag` `isSafety`](2026-10-21-c10-update-tag-is-safety.md)

## Wave-3 plans

C-2 and C-6 share no source files, so they run in parallel worktrees and can merge in either order. Each adds an evidence bullet to the review plan and edits the improvement log, so the second merge needs a trivial docs rebase.

- [C-2 Shared batch rules](2026-10-23-c2-shared-batch-rules.md)
- [C-6 Shared cursor plumbing](2026-10-23-c6-shared-cursor-plumbing.md)

## Wave-1 execution (maintainer, 2026-10-21)

- **One worktree per PR.** Each PR runs in its own worktree under `.worktrees/`, on a new branch cut from `main` after the plans merge. The orchestrator coordinates and implements nothing itself.
- **C-1 uses subagent-driven development.** Tasks 1–4 each get a fresh implementer and a task review, followed by a whole-branch review. The other five PRs use inline execution: one implementer subagent runs the plan, then one fresh reviewer on the most capable model checks the branch.
- **Three phases.**
  1. Implementation runs in parallel across worktrees, with focused tests only.
  2. Each branch's full suite runs serialized, one worktree at a time.
  3. Reviews and fixes follow.
- **Maintainer gates.** The live gates (the C-1 client smoke and the C-8 dispatch smoke) are not implementer tasks. They run one at a time, with maintainer authorization, before those plans' docs tasks.
- **Pushing.** Nothing is pushed without maintainer approval.

Each plan repeats the constraints below verbatim in its own Global Constraints, plus its item-specific rules. The reason: subagent-driven execution gives an implementer only its own task text, and gives the reviewer only the plan's Global Constraints.

## Global constraints (apply to every wave-1 plan)

- **Shell:** run `dotnet` from PowerShell. Bash mangles `/p:` flags.
- **Build:** `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`. The baseline is the seven existing test-assembly nullable warnings and no new warnings.
- **Full suite:**
  - Run serialized, outside the sandbox.
  - Use process-local `TEMP`/`TMP` under ignored `TestResults/<id>/temp`.
  - Collect coverage with `TiaMcpServer.Tests/coverage.runsettings` and the single-thread xUnit settings from `.github/workflows/ci.yml`.
  - `scripts/verify-coverage-threshold.ps1 -MinimumLineRate 0.80` must pass.
  - Zero failures and zero skips.
- **Test-first:** observe a runtime failure before implementing, not just a compile error, wherever the plan names one. A pure refactor records a green baseline with the named pinning tests.
- **Test linking:** `TiaMcpServer.Tests` links host sources through `<Compile Include>`. `Safety/Pipeline`, `Vci`, `Openness/Hmi` and `Openness/Vci` use globs, and Contracts is a `ProjectReference`. Check the csproj before adding a host file.
- **Commits:** commit after each task, as short one-line conventional commits with no body.
- **Docs come last.** Each PR also adds an evidence sub-bullet under its item in the review plan, in the B-item style.
- **No TIA project names or paths** in the repo, including disposable copies.
- **Live runs:** use the installed candidate, which is separate from the dev build. Never dispose `TiaPortalProcess`.
- **jCodemunch:** `find_references` returned false empties for some C# consumers during planning. Confirm consumers with `search_text` or `git grep` before deleting a member.
