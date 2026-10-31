# Docs lifecycle and ADR compaction — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. **Two maintainer gates (Task 7 and Task 11) stop the run; do not pass them without the maintainer's written answers.**

**Goal:** Implement [the directions spec](../specs/2026-10-30-docs-lifecycle-and-adr-compaction-design.md): collapse process material in PR diffs, enforce a lifecycle for new plans/specs/evidence, compact 109 plans/specs into 13 ADRs plus at most 4 kept originals, prune non-Openness evidence, remove history from `AGENTS.md`, and leave the docs tree with zero broken links.

**Architecture:** Additive, reversible work first (tag, `.gitattributes`, checker, lifecycle rule, timing script, ADR scaffolding), then maintainer-gated decisions, then ADR drafting, then mechanical link rewriting, and the destructive removal last as its own commit. A stdlib-Python checker (`scripts/verify-docs.py`, already written and unit-tested during planning) is the "no broken links" gate for every step. Everything removed stays reachable through the annotated tag `docs-archive-2026-10-30`.

**Tech Stack:** Markdown, git attributes, Python 3.11+ (stdlib only, docs tooling only), GitHub Actions (ubuntu), xunit (one pinning test in `CiWorkflowTests`). No product code changes.

**Branch:** `chore/docs-lifecycle-and-adr-compaction` (already checked out; carries spec, plan, implementation).

**Execution order (differs from the task numbers):** 0 (including Gate 0) → 1 → 2 → 3 → 4 → 5 → **7 (Gate 1)** → 6 → 8 → 9 → 10 → **11 (Gate 2)** → 12 → 13 → 14. Gate 1 comes before Task 6 and Task 9 because both consume its answers. Python is invoked as `python3` below; on Windows without that alias use `py -3` or `python` (keep `-I`). Pushes, the tag push, `gh pr create` and remote-branch deletion are outward-facing: stop and get the maintainer's explicit go-ahead at each, never batch them.

**Deliberate deviation from the spec wording:** the spec says evidence is "pruned after merge"; the plan defaults to pruning in the PR's last commit (D2) so `main` never holds it. D2 is asked at Gate 0 before the rule is written into `AGENTS.md`.

---

## 0. What the planning review established

Evidence for everything below is in `docs/superpowers/evidence/2026-10-30-docs-compaction-recon/` (transient; deleted in Task 13 like all evidence, and recoverable from the tag):

| File | Content |
| --- | --- |
| `recon-result.json` | Full output of the 38-agent review: per-cluster ADR candidates, keep-verbatim candidates, risks; evidence classification per file; `AGENTS.md` audit; lifecycle, timing, layout and collapse analyses |
| `adr-source-map.json` | The 13 consolidated ADRs → their source plans/specs and reports (105 of 109 files mapped) |
| `verify_docs.py`, `test_verify_docs.py` | The tested checker and its 12 unit tests (copied into `scripts/` in Task 3) |
| `rewrite_links.py` | The tested one-off link rewriter used in Task 10 (CRLF-preserving; skips fenced and inline code) |
| `link_inventory.py` | Link-matrix prototype; regenerate the full inventory with `python -I link_inventory.py --repo . --out <tmpdir>` (the 1.9 MB output is not stored) |

Facts the plan relies on:

- **Size.** `docs/superpowers/` = 109 plans/specs (55,119 lines), 42 reports (5,923 lines), `reports/inputs/` = 76 files, 3.48 MB, plus `acceptance/2026-10-07-multiuser-pr4-live-definition.md`. `.git` pack is 14 MB, so only reviewability matters.
- **Links.** 174 link records from live docs (`AGENTS.md`, `README.md`, `ROADMAP.md`, `docs/**` outside superpowers) point into plans/specs/evidence; `docs/README.md` alone has 173 `superpowers/` references. 71 links from 25 report files point at plans (23), specs (10), evidence (38). 3 links are broken today (reports → removed `scripts/live-test-*.ps1`); the link inventory counts 2 more in `plans/2026-09-28-json-contract-phase1a-worker-wire.md`, but they sit inside inline code, which the checker ignores. Nothing in code, tests, CI or scripts references `docs/superpowers` (verified); `.gitignore:76` mentions `.superpowers/` (a different local dir). One test comment and `NETWORK_OPERATIONS_SUMMARY.md` reference an evidence file by name (Task 10 greps for it).
- **Collapse attribute.** Only `linguist-generated` hides files in diffs; `linguist-documentation`/`-vendored` do not. A bare directory pattern matches nothing; `/dir/**` is required (verified with `git check-attr`). Whether **deleted** and **renamed** files collapse is undocumented, so Task 2 tests it on a real draft PR.
- **Tag trap (verified).** `scripts/install-local-tool.ps1:48` runs `git describe --tags --long` and requires `^v?\d+\.\d+\.\d+-\d+-g<sha>$`; `docs/development/packaging.md:81` documents the same. An archive tag reachable from `main` and nearer than `v4.2.0` would make that script throw. Task 1 adds `--match 'v[0-9]*'`. `publish.yml` triggers on `v*`, so the tag name must not start with `v`.
- **Plugin default location.** superpowers 5.1.0 `brainstorming` and `writing-plans` default to `docs/superpowers/specs|plans/` and both say "user preferences … override this default"; `using-superpowers` ranks `AGENTS.md` above skills. Only `AGENTS.md` can change agent behavior (no `CLAUDE.md` exists; Codex reads `AGENTS.md`).
- **CI today.** `ci.yml` runs the full Windows build on every PR including docs-only PRs. No link/docs checker exists. The `main` ruleset has no required status checks, so any new check informs and does not block.
- **Evidence.** No file in `inputs/` outside the HMI native-probe family records native Openness calls. Raw Openness (`native-probes` and `ui-P10` checked individually; the other `ui-P*` files were structure-checked against P10 and sampled, not each read in full): `2026-10-29-hmi-write-native-probes.jsonl.gz` (802,186 B), eight `2026-10-29-hmi-ui-P*.jsonl.gz` (207,727 B total), the `2026-10-29-hmi-native-probe/` source (40,409 B), and `2026-10-29-hmi-ui-P19-compile.png` (23,178 B, the only UI proof). About 1.07 MB of 3.48 MB is kept; the rest is MCP-level traces, preflights, restoration dumps and timing outputs.
- **Timing tooling.** The two HMI timing directories are byte-identical except 2 lines of `client.py` (expected version `4.2.0-local.79.g75c0568` vs `4.2.0-local.113.g31d1a44`; contract `'1.0'` vs `'2.0'`) and 1 line of `self_test.py`. `validate_archive.py`, `workloads.json` and the nested `.gitattributes` are identical. The reports need only the output summaries, not the tooling.

### Content that exists only in plans/specs and must be migrated before deletion

Task 6 handles these. Each is a deliberate migration, not an accident of the removal:

1. **Open backlog only in the codebase-simplification review**: items D-6, D-7, D-8, E-6, E-7, E-8, F-1..F-4 and the Q1 probe retirement exist only in `plans/2026-10-17-codebase-simplification-review.md` (D-6 ≈ line 169, E-6 ≈ 198, F-1 ≈ 217); the `-findings.md` appendix uses HOSTCORE-N / WORKERCORE-N IDs → `docs/IMPROVEMENT_LOG.md`.
2. **E-4 typed network connections acceptance record** (candidate `b5e29e4`, four cases, restoration deviation) has no report → write a short report under `acceptance/reports/`.
3. **B-16/Q3 portal-loss record** (`plans/2026-10-19-worker-portal-loss.md`) is the only live-verification record, cited by 7 live docs including `AGENTS.md` → keep-verbatim candidate or promote to a report (maintainer, Gate 1).
4. **Per-domain omission priority and guard-reserve table** exists only in `plans/2026-10-21-guarded-write-budget.md` → `docs/ARCHITECTURE.md`.
5. **`OnlineProvider` finding** (no public PLC Start/Stop or RUN/STOP read) and the **elicitation client-spike matrix** (Claude Code, Codex, Inspector) exist only in the 2026-09-01 hardening and 2026-09-29 redesign specs → carry in the `guarded-single-call-write-pipeline` ADR.
6. **Undelivered multiuser PR7–9 design** (`multiuser_write`, no-CheckIn/Update rule, proposed error categories, offline-session scope) exists only in `specs/2026-09-28-multiuser-engineering-design.md` → keep-verbatim candidate or `docs/roadmap/` entry.
7. **Undo-feasibility note** (Siemens transactions; history cleared by save) in `specs/2026-10-03-network-json-guarded-write-design.md` §12 → one sentence in the Network ADR.
8. **Unshipped `update_io_system` contract** (`specs/2026-09-24-network-phase5-qualified-contract.md`, 91 lines) → keep-verbatim candidate.
9. **Stale reference pages the ADRs would contradict**: `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md` (still says FB/FC/OB are not `format=source` eligible) and the rebind-guard text in `PROJECT_OPERATIONS_SUMMARY.md` (still mentions tokens). Fix them before an ADR cites them.
10. **`2fb5e9a` HMI read-interruption facts** appear in `AGENTS.md`, the plans and `docs/guides/troubleshooting.md` only, not in `ARCHITECTURE.md` or a report → confirm the portal-loss ADR or record carries them before `AGENTS.md` is stripped.

---

## 1. Decisions needed from the maintainer

Recommended defaults are used by the plan; Gate 1 (Task 7) and Gate 2 (Task 11) collect the answers.

| # | Decision | Recommendation |
| --- | --- | --- |
| D1 | Which 2 plans + 2 specs are kept verbatim | Candidates ranked by the review (strongest first): **specs** `2026-09-28-multiuser-engineering-design` (only record of undelivered PR7–9), `2026-09-29-write-safety-redesign-design` (consent model, elicitation matrix), `2026-10-29-hmi-write-native-rejections-design` (guard rule, O1–O3 protocol), `2026-09-24-network-phase5-qualified-contract` (91 lines, unshipped contract); **plans** `2026-10-19-worker-portal-loss` (only B-16/Q3 record), `2026-10-17-codebase-simplification-review-findings` (only if the backlog is not triaged into `IMPROVEMENT_LOG.md`). The maintainer chooses; the plan only needs "≤ 2 plans, ≤ 2 specs". |
| D2 | When transient files are pruned | **Last commit of the producing PR** (`docs: prune working files`), so `main` never holds them and the aggregate diff is code plus durable docs. Alternative: prune in a follow-up PR after merge (spec wording "pruned after merge"); the `main` push check in Task 3 then fails until it is done. Merge commits keep branch history either way. |
| D3 | Fate of `docs/superpowers/` | **Keep the directory name**; reports and `inputs/` stay in place (no rewrite of ~103 report/input links); `plans/`, `specs/`, `evidence/` become transient (empty on `main`). A rename stays a mechanical follow-up. |
| D4 | ADR set | The 13 in Task 8; merge or split at the gate. |
| D5 | Evidence items marked "maintainer decides"; plc/network/hmi timing medians to inline or drop; whether to accept or redact the Windows profile name `C:\Users\LCZ\...` that appears about 141 times in `2026-10-29-hmi-write-native-probes.jsonl.gz` (redaction alters raw evidence; the repo's rule against private paths in tracked files applies) | Table in Task 9; defaults listed there. Ask at Gate 1. |
| D6 | Python for docs/timing tooling | Accept Python 3.11+ (stdlib) for `scripts/verify-docs.py` and `scripts/timing/`; PowerShell stays the default for everything that builds or tests. |
| D7 | Timing script scope | HMI-first with `--tool` as the extension point; workload file shipped as an example; data archives not kept. |
| D8 | Tag name | `docs-archive-2026-10-30`. |
| D9 | Collapse test outcome | Task 2 result decides whether deletions collapse; fallback in Task 2. |
| D10 | Fail on transient files in non-draft PRs too (`--fail-on-transient` when `github.event.pull_request.draft == false`) | Default: warn on PRs, fail on `main` push only (no required checks exist either way). |

Gate 0 (end of Task 0) asks D2, D3, D6, D7, D8, D10; Gate 1 (Task 7) asks D1, D4, D5; D9 is observed in Task 2. Each `gate*-decisions.md` records the maintainer's reply **verbatim with its date**, never the plan's defaults. Those files and `link-map.json` are created after the archive tag, so they are not in it; they survive only in branch history and the PR description (Task 14 copies them there).

---

## 2. Target layout

```
docs/adr/README.md                 index table, boundary rules, history pointer (~40 lines)
docs/adr/TEMPLATE.md               one-page ADR template
docs/adr/NNNN-<slug>.md            13 ADRs
docs/adr/records/<orig>.md         the kept plans/specs, verbatim + one-line banner
docs/superpowers/README.md         reports + evidence index and the transient-area rules (stub)
docs/superpowers/acceptance/reports/*.md                kept, unchanged except link fixes
docs/superpowers/acceptance/reports/inputs/README.md    index of kept raw Openness evidence
docs/superpowers/acceptance/reports/inputs/<kept>       raw Openness files only
docs/superpowers/{plans,specs,evidence}/                transient: empty on main
scripts/verify-docs.py, scripts/tests/test_verify_docs.py
scripts/timing/measure_write_timing.py, scripts/timing/test_measure_write_timing.py, scripts/timing/workloads/hmi-write.json
docs/development/measuring-write-timing.md
.github/workflows/docs.yml
```

Rules (these go into `AGENTS.md`, `CONTRIBUTING.md`, the PR template, Task 4):

- New spec → `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`; plan → `docs/superpowers/plans/YYYY-MM-DD-<topic>.md`; evidence → `docs/superpowers/evidence/YYYY-MM-DD-<topic>/`. These are the plugin defaults on purpose: an agent that ignores `AGENTS.md` still lands in a collapsed, checked place.
- Before the PR merges, a final commit deletes everything the PR added under those three directories. First move what survives: a lasting decision → `docs/adr/` (one ADR); behavior → `ARCHITECTURE.md`/`SupportedOperations`; follow-ups → `IMPROVEMENT_LOG.md`; the live-verification report → `acceptance/reports/`; **verified raw Openness call/response files** → `acceptance/reports/inputs/` (listed in its `README.md`).
- Never link a transient file from outside `docs/superpowers/`; cite it as "PR #N, commit `<sha>`".

---

### Task 0: Baseline and commit the planning material

**Files:**
- Commit: `docs/superpowers/plans/2026-10-30-docs-lifecycle-and-adr-compaction.md`, `docs/superpowers/evidence/2026-10-30-docs-compaction-recon/*`
- Modify: `docs/superpowers/README.md` (add this plan row)

- [ ] **Step 1: Confirm the branch and a clean baseline**

```bash
git status -sb
python3 -I docs/superpowers/evidence/2026-10-30-docs-compaction-recon/verify_docs.py --repo . | tail -1
```
Expected: branch `chore/docs-lifecycle-and-adr-compaction`; checker prints `3 error(s)` (the 3 pre-existing broken report links) and one `transient working file present` warning per file under `plans/`, `specs/`, `evidence/` (all extensions, tracked or not; about 116 now). Any other error count means the tree changed; investigate before continuing.

- [ ] **Step 2: Add the plan to the history index**

In `docs/superpowers/README.md`, add as the first row of the Plans table (below `| --- | --- |`):

```markdown
| 2026-10-30 | [Docs lifecycle and project-memory compaction — implementation plan (in progress)](plans/2026-10-30-docs-lifecycle-and-adr-compaction.md) |
```

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers
git commit -m "docs: add docs-compaction implementation plan and recon evidence"
```

- [ ] **Step 4: GATE 0 — ask the maintainer D2, D3, D6, D7, D8, D10 (stop here)**

Present each with its recommendation (§1). Record the replies verbatim in `docs/superpowers/evidence/2026-10-30-docs-compaction-recon/gate0-decisions.md` and commit. If D2 is "prune after merge", Task 4 words the rule accordingly and Task 13 moves to a follow-up PR; if D8 differs, replace `docs-archive-2026-10-30` everywhere (it is used in this plan, the workflow and the checker invocation) before Task 1.

---

### Task 1: Archive tag and the `git describe` fix

**Files:**
- Modify: `scripts/install-local-tool.ps1:48`
- Modify: `docs/development/packaging.md:81`

- [ ] **Step 1: Reproduce the trap (proves the fix is needed)**

```bash
git tag docs-archive-probe HEAD
git describe --tags --long
git tag -d docs-archive-probe
```
Expected: `docs-archive-probe-0-g<sha>` printed, which fails the script's `^v?\d+\.\d+\.\d+-…` regex.

- [ ] **Step 2: Restrict the describe to release tags**

`scripts/install-local-tool.ps1` line 48: change

```powershell
        $described = (git describe --tags --long).Trim()
```
to
```powershell
        $described = (git describe --tags --long --match 'v[0-9]*').Trim()
```
and in `docs/development/packaging.md` line 81 change `git describe --tags --long` to `git describe --tags --long --match 'v[0-9]*'` (read the surrounding sentences; if the text explains the regex, add "release tags only; archive tags are ignored").

- [ ] **Step 3: Verify with a probe tag**

```bash
git tag docs-archive-probe HEAD
git describe --tags --long --match 'v[0-9]*'
git tag -d docs-archive-probe
```
Expected: `v4.2.0-<n>-g<sha>`.

- [ ] **Step 4: Commit and create the local tag**

The tag must sit on a commit that still contains every plan, spec and evidence file (nothing is removed until Task 13). Create it **now**, once, and never move it.

```bash
git add scripts/install-local-tool.ps1 docs/development/packaging.md
git commit -m "build: match release tags only when deriving the local tool version"
git tag -a docs-archive-2026-10-30 -m "Pre-compaction docs snapshot; plans, specs and non-Openness evidence are removed after this commit; see docs/adr/README.md"
git ls-tree -r --name-only docs-archive-2026-10-30 -- docs/superpowers/plans | wc -l
```
Expected: `76` (the 75 plans plus this one).

- [ ] **Step 5: STOP — ask the maintainer before pushing**

A tag on the shared remote is effectively irreversible: every ADR permalinks it, and deleting or moving it silently breaks them. Only after an explicit yes:

```bash
git push origin chore/docs-lifecycle-and-adr-compaction
git push origin docs-archive-2026-10-30
```
The tag push must happen before the PR is opened and before `docs.yml` first runs, or its permalink checks fail on CI.

---

### Task 2: Workstream A — `.gitattributes` collapse rule and a real PR test

**Files:**
- Modify: `.gitattributes` (root; the working copy is CRLF under `core.autocrlf=true` while the index is LF, so just keep the file's existing style)

- [ ] **Step 1: Add the rule block**

Append (`git check-attr` in Step 2 proves the attributes parse):

```gitattributes

# Process material is collapsed by default in GitHub diffs (linguist-generated). Everything
# removed from the tree stays reachable through the tag docs-archive-2026-10-30. A bare
# directory pattern matches nothing, so '/**' is required. Reports are deliberately not matched.
/docs/superpowers/specs/** linguist-generated
/docs/superpowers/plans/** linguist-generated
/docs/superpowers/evidence/** linguist-generated
/docs/superpowers/evidence/** -text
/docs/superpowers/acceptance/reports/inputs/** linguist-generated
```

- [ ] **Step 2: Verify path matching locally**

```bash
git check-attr linguist-generated -- docs/superpowers/plans/2026-10-30-docs-lifecycle-and-adr-compaction.md docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-ui-P19-compile.png docs/superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md AGENTS.md
```
Expected: `set` for the first two, `unspecified` for the report and `AGENTS.md`.

- [ ] **Step 3: Commit, then STOP and ask before creating the throwaway PR**

```bash
git add .gitattributes
git commit -m "chore: collapse process material in PR diffs"
```
Pushing, `gh pr create` and the remote-branch delete in Step 5 are outward-facing: get an explicit yes first. The maintainer must also observe the GitHub UI in Step 4, so this step ends with their reply.

```bash
git push origin chore/docs-lifecycle-and-adr-compaction
git switch -c tmp/collapse-test
git rm docs/superpowers/plans/2026-08-15-pr27-review-fixes.md
git mv docs/superpowers/specs/2026-09-26-open-bug-parallel-pr-delivery-design.md docs/superpowers/specs/2026-09-26-open-bug-parallel-pr-delivery-design-renamed.md
echo "x" >> docs/superpowers/plans/2026-10-30-docs-lifecycle-and-adr-compaction.md
echo "x" >> docs/superpowers/acceptance/reports/2026-10-08-multiuser-pr4-live-verification.md
git commit -am "test: collapse behavior probe (do not merge)"
git push origin tmp/collapse-test
gh pr create --draft --base chore/docs-lifecycle-and-adr-compaction --head tmp/collapse-test --title "TEST ONLY: diff collapse probe" --body "Throwaway; verifies linguist-generated on deleted, renamed, edited and untouched paths."
```

- [ ] **Step 4: Maintainer checklist (record answers in the PR comment, then close the PR and delete the branch)**

In Files changed, note for each of: deleted plan, renamed spec, edited plan, edited report (negative control), `AGENTS.md`-class file: collapsed or expanded by default; banner text; whether "Load diff" works; whether the header `N files changed` / `+/-` totals still count collapsed files; whether the file tree lists them. Compare against `gh pr view <n> --json files,additions,deletions`.

- [ ] **Step 5: Decide the fallback if deletions do not collapse**

Spec criterion 1 is about "code first, process material collapsed". If deleted files stay expanded, the final removal commit is huge but is its own commit (Task 13), reviewable per commit; record this in the PR description and keep the rule for the added/edited case. If renames or deletions behave differently only when the attribute exists on the base branch, land Task 2's commit on `main` first as a tiny PR and rebase.

```bash
gh pr close <number> --delete-branch
git switch chore/docs-lifecycle-and-adr-compaction
git branch -D tmp/collapse-test
```
(`gh pr close --delete-branch` removes the remote branch; do not push-delete it separately.)

---

### Task 3: The docs checker, its CI workflow and a pinning test

**Files:**
- Create: `scripts/verify-docs.py`, `scripts/tests/test_verify_docs.py`, `.github/workflows/docs.yml`
- Modify: `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs`

The checker was written and run during planning: against today's tree it reports exactly the 3 known broken report links and no unreachable file. It lists files with `git ls-files -co --exclude-standard`, so **new untracked files are checked before `git add`**. It checks inline links outside fenced code and inline code, anchors stripped, directory targets resolving to their `README.md`, `blob/main/…` URLs mapping to files in the tree, `blob/<archive-tag>/…` and `tree/<archive-tag>/…` URLs existing in the tag (`git cat-file -e`), every `docs/**/*.md` reachable from `docs/README.md`, and **any file of any extension** under `docs/superpowers/{plans,specs,evidence}/` being a warning (an error with `--fail-on-transient`).

- [ ] **Step 1: Install the tested files**

```bash
mkdir -p scripts/tests
cp docs/superpowers/evidence/2026-10-30-docs-compaction-recon/verify_docs.py scripts/verify-docs.py
cp docs/superpowers/evidence/2026-10-30-docs-compaction-recon/test_verify_docs.py scripts/tests/test_verify_docs.py
```
In `scripts/tests/test_verify_docs.py` change the loader line so it finds the hyphenated script one directory up:

```python
HERE = os.path.dirname(os.path.abspath(__file__))
SPEC = importlib.util.spec_from_file_location("verify_docs", os.path.join(HERE, "..", "verify-docs.py"))
```

- [ ] **Step 2: Run the tests**

```bash
python3 -I scripts/tests/test_verify_docs.py
```
Expected: `Ran 12 tests … OK` (clean tree, broken link, anchors/external ignored, fenced and inline code ignored, unreachable doc, directory index, transient warn/fail, non-Markdown transient file, untracked files, `tree/` and `blob/` archive-tag permalinks).

- [ ] **Step 3: Fix the 3 pre-existing broken report links, then baseline**

In `docs/superpowers/acceptance/reports/2026-09-01-pr5-tag-operation-safety-scopes-live.md:14`, `2026-09-06-issue-32-project-tree-v3-live.md:15` and `2026-09-21-network-phase4-current-revision-live.md:99`, turn the dead `scripts/live-test-*.ps1` link into plain inline code (keep the text, drop the link), so the Docs workflow is green from this task on.

```bash
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | tail -3
```
Expected: `0 error(s)` and about 116 transient warnings (the exact number is the count of files under `plans/`, `specs/`, `evidence/`).

- [ ] **Step 4: Add the workflow (advisory on PRs, strict on `main`)**

Create `.github/workflows/docs.yml` (copy the `actions/checkout` major version used in `ci.yml`, currently `v7`):

```yaml
name: Docs

permissions:
  contents: read

on:
  push:
    branches: ["main"]
    paths: &docs-paths
      - "docs/**"
      - "**/*.md"
      - ".gitattributes"
      - "scripts/verify-docs.py"
      - "scripts/tests/**"
      - ".github/workflows/docs.yml"
  pull_request:
    branches: ["main"]
    paths: *docs-paths

jobs:
  verify-docs:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout repository
        uses: actions/checkout@v7
        with:
          fetch-depth: 0

      - name: Test the checker
        run: python3 -I scripts/tests/test_verify_docs.py

      - name: Verify docs
        run: >
          python3 -I scripts/verify-docs.py
          --archive-tag docs-archive-2026-10-30
          ${{ github.event_name == 'push' && '--fail-on-transient' || '' }}
```

On PRs transient files are warnings; on a `main` push they fail. If D10 was accepted, use `${{ (github.event_name == 'push' || github.event.pull_request.draft == false) && '--fail-on-transient' || '' }}` for the last line instead. Do not add the job to branch protection (the ruleset has no required checks).

- [ ] **Step 5: Pin the workflow with a test**

Read `PackagingTests_RunOnlyInPathFilteredWorkflowWithoutCoverage` in `TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs` and add a sibling `DocsWorkflow_IsPathFilteredAndRunsTheChecker` asserting: `.github/workflows/docs.yml` exists; it contains a `paths:` filter; it runs `scripts/verify-docs.py`; it does not contain `dotnet`. Reuse that file's helper for the repository root and for loading a workflow; do not add a new helper. Then:

```bash
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --no-build --filter "FullyQualifiedName~CiWorkflowTests"
```
Expected: all pass (the new test and the existing workflow enumeration). The stub flag is needed on a machine without TIA Portal.

- [ ] **Step 6: Commit**

```bash
git add scripts/verify-docs.py scripts/tests .github/workflows/docs.yml TiaMcpServer.Tests/Diagnostics/CiWorkflowTests.cs
git commit -m "ci: add docs link, index and transient-file checker"
```

---

### Task 4: Workstream B — lifecycle rule where work happens

**Files:**
- Modify: `AGENTS.md` (lifecycle section and the docs rules only; history stripping is Task 12), `CONTRIBUTING.md` (~lines 70–80), `.github/pull_request_template.md`, `docs/development/codebase-review.md` (lines 12 and 14, and step 5), `docs/ARCHITECTURE.md` §11, `docs/superpowers/README.md` (header only; the body is Task 12)

Write the pre-merge prune rule only if D2 (Gate 0) was "last commit of the PR"; otherwise say "prune in a follow-up PR after merge".

- [ ] **Step 1: Add the lifecycle section to `AGENTS.md`**

Insert directly above "## Documentation layout" (keeps the 9 existing superpowers links intact until Task 12):

```markdown
## Plans, specs and evidence (lifecycle)

These rules override the default output locations of the superpowers skills (brainstorming: "Write the validated design (spec) to"; writing-plans: "Save plans to") and of any similar tool.

- Specs go to `docs/superpowers/specs/YYYY-MM-DD-<topic>-design.md`, plans to `docs/superpowers/plans/YYYY-MM-DD-<topic>.md`, run evidence to `docs/superpowers/evidence/YYYY-MM-DD-<topic>/`. These three directories are a transient per-PR working area: collapsed in diffs and empty on `main`.
- Never link a transient file from outside `docs/superpowers/`; cite it as "PR #N, commit `<sha>`".
- Before pushing documentation changes, run `python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`.
- Before the PR merges, delete everything you added there in a final commit (`docs: prune working files`). First move what must survive: a lasting decision to `docs/adr/` (one ADR, see its README), behavior to `docs/ARCHITECTURE.md` or `docs/SupportedOperations/`, follow-ups to `docs/IMPROVEMENT_LOG.md`, the live-verification report to `docs/superpowers/acceptance/reports/`, and verified raw Openness call/response files to `docs/superpowers/acceptance/reports/inputs/` (listed in its `README.md`). MCP-level traces, preflights, restoration dumps and timing outputs are not kept; use `scripts/timing/` for measurements.
```

Also reword the two "documentation rules" bullets at the end of `AGENTS.md` ("An unlisted document is unreachable", the `docs/README.md` rule) to the reachability wording used in Steps 2–3. Do not renumber anything else yet; Task 12 keys its edits to headings and to `git show docs-archive-2026-10-30:AGENTS.md` line numbers.

- [ ] **Step 2: `CONTRIBUTING.md`**

The sentences are hard-wrapped across lines 73–74 and 76–77, so edit by reading the paragraph, not by literal search. Replace "`docs/superpowers/` is historical process material and is not current documentation." with:
"`docs/superpowers/` holds working plans, specs and evidence for one PR, which delete them before merge (see `AGENTS.md`), plus the live-verification reports. Decisions live in `docs/adr/`."
and reword the rule "A new document under `docs/` is not complete until it is listed in docs/README.md" to "…is not complete until it is reachable from `docs/README.md`, directly or through the index of its directory (`scripts/verify-docs.py` checks this in CI)."

- [ ] **Step 3: PR template**

In `.github/pull_request_template.md`: change the Summary comment "Link the design or plan document if there is one." to "Describe the design here; cite working files as commit SHAs, they are deleted before merge." In the Documentation list, reword "Every new file under `docs/` is listed in `docs/README.md`" to "Every new file under `docs/` is reachable from `docs/README.md`" and add:

```markdown
- [ ] No plan, spec or evidence file is left under `docs/superpowers/{plans,specs,evidence}/` (pruned in the last commit; raw Openness evidence promoted to `acceptance/reports/inputs/`)
- [ ] A lasting decision is recorded as an ADR in `docs/adr/` (or: none)
```

- [ ] **Step 4: `codebase-review.md` and `ARCHITECTURE.md` §11**

`docs/superpowers/README.md`: replace the header text "These files are kept for auditability and are intentionally not pruned" and "This tree is development-process history" with one sentence: plans/specs/evidence here are transient per-PR working files (see `AGENTS.md`); the reports below are kept.
`docs/development/codebase-review.md`: step 5 becomes "**Plan.** Write a transient plan under `docs/superpowers/plans/` (not listed in the index), record durable decisions as an ADR, open findings in `docs/IMPROVEMENT_LOG.md` and mechanical rules as tests." Lines 12 and 14 (links to the two review plan files) are rewritten in Task 10 once the link map exists; leave a note in your commit message.
`docs/ARCHITECTURE.md` §11: append "When a change reverses or narrows a recorded decision, add an ADR that supersedes it in the same PR and update the affected sections here; do not edit the old ADR's body."

- [ ] **Step 5: Verify and commit**

```bash
git add AGENTS.md CONTRIBUTING.md .github/pull_request_template.md docs/development/codebase-review.md docs/ARCHITECTURE.md docs/superpowers/README.md
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | tail -1
git commit -m "docs: define the lifecycle of plans, specs and evidence"
```
Expected: `0 error(s)`.

---

### Task 5: Shared timing script

**Files:**
- Create: `scripts/timing/measure_write_timing.py`, `scripts/timing/test_measure_write_timing.py`, `scripts/timing/workloads/hmi-write.json`, `docs/development/measuring-write-timing.md`
- Modify: `docs/README.md` (one row), `docs/development/local-mcp-testing.md` (one link)
- Source (read, do not copy blindly): `docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-timing-after/{client.py,validate_archive.py,self_test.py,workloads.json,README.md}`

The two archived toolchains differ only by `EXPECTED_VERSION`, the `contractVersion` assertion (`client.py` line 53) and one `self_test.py` fixture. Replace the per-PR copies with one script. Build it from `client.py` + `validate_archive.py` + `self_test.py`, preserving their behavior (installed global tool launched with `--access-mode full --max-item-chars 1000000 --max-document-chars 1000000 --worker-timeout-minutes 15`; per-request deadline 900 s writing `pending-call.json` and stopping without replay; text block equals `structuredContent`; audit v2 match by identical `responseText` in `%LOCALAPPDATA%\TiaMcpServer\audit\writes-*.jsonl`; `rootMs`/`itemSumMs`/`remainderMs`; no overwrite of existing result files).

- [ ] **Step 1: Write the failing self-test first**

`scripts/timing/test_measure_write_timing.py` (stdlib `unittest`; it loads the script by path with `importlib.util.spec_from_file_location`, like `scripts/tests/test_verify_docs.py`, because `python3 -I` does not put the script directory on `sys.path`) must cover, using in-memory fixtures (no live server): (a) `parse_args(["validate", "dir"])` and `parse_args(["run", "--tool", "hmi_write", "--contract-version", "2.0", "--expected-version", "X", "--project", "p.ap21", "--project-sha256", "<64 hex>", "--out", "o"])`; (b) a summary built from a synthetic schedule of `cases × runs` rows (run 0 discarded) produces medians equal to a hand-computed value; (c) `validate` rejects an archive whose request/response counts do not equal the schedule-derived counts (replaces the literals 20/22/66/15/5); (d) the project hash mismatch refuses to start. Run it and confirm it fails because `measure_write_timing.py` does not exist (`FileNotFoundError`).

```bash
python3 -I scripts/timing/test_measure_write_timing.py
```

- [ ] **Step 2: Implement the script**

CLI (parameters replace every PR-specific literal): subcommands `run`, `validate <archive-dir>`, `self-test`; flags `--tool` (default `hmi_write`), `--contract-version`, `--expected-version`, `--tool-path` (default `~/.dotnet/tools/tia-mcp.exe`), repeatable `--server-arg` (default as above), `--request-timeout` (900), `--project` / `--project-sha256` (env fallback `HMI_TIMING_PROJECT`, `HMI_TIMING_PROJECT_SHA256`), `--portal-pid` (startup `bind_project`, which the archived flow required a hand-sent command for), `--workloads FILE`, `--cases`, `--runs` (default 4, run 0 discarded), `--warm-read FILE`, `--expect-inventory FILE`, `--out DIR`. The scheduler (restore → warm read → workload per case and run) lives inside `run`; the archived driver that fed `client.py` its stdin commands was never committed. Parameterize every PR-specific literal, including the asserted `serverInfo` (`{'name': 'TiaMcpServer', 'version': '4.2.0.0'}` in `client.py`) and the literals 20/22/66/15/5 in `validate_archive.py`. The scheduler, summary and median code were never committed (only `client.py`, `validate_archive.py`, `self_test.py` exist), so write them fresh and unit-test them offline. Keep `client.py`'s stdin-command loop only behind `run --stdin`. State "Python 3.11+, stdlib only, Windows" in the header.

- [ ] **Step 3: Ship the workload example and the doc**

Copy `workloads.json` to `scripts/timing/workloads/hmi-write.json` (fixture names `CP1029_*`, `HM1029_*`, `HMI_RT_1` stay: they document the disposable project). `docs/development/measuring-write-timing.md` must state "covered by offline tests only; not live-verified" unless the maintainer runs a smoke measurement against a disposable project (ask at the end of this task; do not claim live verification otherwise). Contents: purpose, prerequisites (installed tool at the expected version, disposable project, Portal PID), one worked command, output files, `validate` usage, and the statement that archived outputs are not kept in-tree (tag `docs-archive-2026-10-30`). List it in `docs/README.md` under "Building and contributing" and link it from `docs/development/local-mcp-testing.md`.

- [ ] **Step 4: Run, verify and commit**

```bash
python3 -I scripts/timing/test_measure_write_timing.py
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | tail -1
git add scripts/timing docs/development docs/README.md
git commit -m "chore: add shared write-timing script replacing per-PR copies"
```
Expected: tests `OK`; `0 error(s)`. The archived timing directories are removed in Task 13; their data stays in the tag. Only the HMI medians are inlined into a report (Task 9 Step 2); the plc/network timing files are removed without inlining unless Gate 1 (D5) asks otherwise.

---

### Task 6: Migrate content that exists only in plans/specs

**Files:** see the numbered list in §0. Modify `docs/IMPROVEMENT_LOG.md`, `docs/ARCHITECTURE.md`, `docs/SupportedOperations/IMPORT_EXPORT_OPTIONS_SUMMARY.md`, `docs/SupportedOperations/PROJECT_OPERATIONS_SUMMARY.md`; create `docs/superpowers/acceptance/reports/2026-10-25-e4-typed-network-connections-acceptance.md`.

- [ ] **Step 1: Triage the open backlog (item 1).** Read `plans/2026-10-17-codebase-simplification-review-findings.md` (the open items D-6, D-7, D-8, E-6, E-7, E-8, F-1..F-4, Q1 probe retirement) and add one `docs/IMPROVEMENT_LOG.md` entry per still-open item (heading, one-sentence description, pointer to the ADR `fail-closed-surface-and-portal-loss` once it exists; leave the ADR link as a TODO list in your commit message and fix it in Task 8). Items already fixed are dropped. If the maintainer keeps the findings appendix verbatim (D1), skip this step.
- [ ] **Step 2: E-4 report (item 2).** From `plans/2026-10-25-e4-typed-network-connections.md` extract the native acceptance record (candidate `b5e29e4`, four cases, restoration deviation) into the new short report, using the structure of an existing report (`acceptance/reports/2026-10-23-c5-network-object-cursors-live-verification.md` is a 44-line model). Add its row to the reports table in `docs/superpowers/README.md`.
- [ ] **Step 3: Budget table (item 4).** Move the per-domain omission-priority and guard-reserve table from `plans/2026-10-21-guarded-write-budget.md` into `docs/ARCHITECTURE.md` next to the existing budget text; verify the numbers against `StructuredOperationBatchPayloadBudget` in the source before copying.
- [ ] **Step 4: Fix the stale reference pages (item 9).** `IMPORT_EXPORT_OPTIONS_SUMMARY.md` (~line 118, "Not supported" for FB/FC/OB source) and the rebind-guard text in `PROJECT_OPERATIONS_SUMMARY.md`: correct against `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md` and the code.
- [ ] **Step 5: Record items 5, 6, 7, 8, 10 in the ADR drafting briefs** (Task 8 consumes them). Items 3, 6 and 8 were decided at Gate 1 (Task 7, which runs before this task): kept verbatim, promoted to a report/roadmap entry, or dropped.
- [ ] **Step 6: Verify and commit**

```bash
git add docs
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | tail -1
git commit -m "docs: preserve plan-only content before compaction"
```
Expected: `0 error(s)`.

---

### Task 7: GATE 1 — maintainer review of the compaction set (execute after Task 5, before Task 6; stop here)

Present, in one message, and wait for written answers:

1. **D1** the keep-verbatim shortlist (§1 D1) with the review's one-line reasons (from `recon-result.json` → `clusters.*.keepVerbatimCandidates`), asking for exactly ≤ 2 plans and ≤ 2 specs and what to do with Task 6 items 3, 6, 8.
2. **D4** the 13-ADR table in Task 8, plus the three plans/specs that map to no ADR (`plans/2026-08-15-pr27-review-fixes.md`, `specs/2026-08-07-docs-reorganization-design.md`, `specs/2026-09-26-open-bug-parallel-pr-delivery-design.md`) and the files listed under `droppableWithoutAdr` in `recon-result.json` (36 entries across the 7 clusters; some repeat the unmapped three): confirm drop.
3. **D5** the evidence "maintainer decides" table from Task 9 (defaults in bold), the timing-medians question, and the profile-name redaction question.
4. The Task 2 outcome (D9) and any change to D2, D3, D6, D7, D8, D10 since Gate 0.

Record the answers verbatim, with the date, in `docs/superpowers/evidence/2026-10-30-docs-compaction-recon/gate1-decisions.md` and commit it. Compaction relevance is the maintainer's decision; Tasks 6, 8, 9 and 10 do not start without this file.

---

### Task 8: ADR scaffolding and drafting

**Files:**
- Create: `docs/adr/README.md`, `docs/adr/TEMPLATE.md`, `docs/adr/0001-…0013-….md`
- Modify: `docs/README.md` (one row), `AGENTS.md` pointer (Task 12)

- [ ] **Step 1: Template and boundary rules**

`docs/adr/TEMPLATE.md`:

```markdown
# ADR-NNNN: <decision, as a short sentence>

**Status:** Accepted | Superseded by [ADR-MMMM](MMMM-slug.md) | Deprecated
**Decided:** YYYY-MM-DD (or a range for a multi-step era)   **Recorded:** 2026-10-30
**Area:** write-safety | binding | json-contract | network | plc | hmi | vci | multiuser | process
**Supersedes:** ADR-KKKK (omit if none)
**Current home:** link to the ARCHITECTURE section or SupportedOperations reference that describes today's behavior

## Context
Why a decision was needed: the forces and the failure or limit that triggered it. At most 8 lines. No task lists.

## Decision
What was chosen, in the present tense, in 3-10 lines. Rejected alternatives, one line each, with the reason.

## Consequences
- What this made possible.
- What it cost or forbids (rules that still bind code today, kept short).
- What later superseded or narrowed it, if anything.

## Evidence
- In tree: relative links to kept live-verification reports and kept raw evidence.
- Original material, removed from `main`, kept at tag `docs-archive-2026-10-30`: one absolute permalink per removed plan/spec,
  `https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/plans/<file>.md`
```

`docs/adr/README.md`: 6–10 lines of intro (what an ADR is here, how to add one, admission test), the **content-boundary table**, then ONE table `No | Decision | Status | Area | Decided | Record` (the `Record` column links a kept original in `records/`), and the history pointer paragraph. Boundary rules to state verbatim:

- Each fact has one home: `ARCHITECTURE.md` = how the system works now; ADR = why, what was rejected, what the choice still forces; `IMPROVEMENT_LOG.md` = open follow-ups and the latest completed entry; reports = what was verified.
- An ADR states no field lists, schemas, limits, thresholds, test counts, timings, case matrices, candidate hashes, follow-ups or task lists; it links instead.
- An ADR is immutable once Accepted except Status/Superseded-by lines and link fixes; a reversal is a new ADR.
- Admission test for a new ADR: a decision with a considered alternative or lasting constraint that a future maintainer could plausibly reverse by mistake. Bug fixes, refactors and plain feature additions do not get one.
- Length cap: one screen per ADR (~60 lines).

- [ ] **Step 2: The 13 ADRs (draft in this order, numbering chronological by start of the decision era; renumber if Gate 1 changes the set)**

Sources and reports come from `adr-source-map.json` (paths there are relative to `docs/superpowers/`; 9 of its `reports` entries are bare file names under `acceptance/reports/`; the map is keyed by slug in a different order than this table, so use the numbers below, not the map order); the per-candidate context/decision/consequences drafts are in `recon-result.json` → `clusters.<cluster>.adrCandidates[]` (use as raw material, never as final text). Every ADR must cite at least its removed originals in Evidence.

| ADR | Slug | Source plans/specs (count) | Must also carry |
| --- | --- | --- | --- |
| 1 | `plc-content-exchange-and-creation` | 8 | External-source rules (strict single-object writes, explicit `GenerateOptions`, leftover-node postcondition), OB creation by event class with server-chosen numbers; live-spike traps (stale handle after block recreation, `<name>_1` auto-rename) |
| 2 | `bounded-project-and-hardware-reads` | 7 | Standalone tools, completeness pagination, cursor/snapshot reads |
| 3 | `network-domain-identity-and-subnet-lifecycle` | 12 | Independent domain on the canonical kernel, exact fail-closed identity, subnet lifecycle, Phase 5/6 delivery gates; the undo-feasibility sentence (Task 6 item 7); pointer to ADR 12 for native rejections |
| 4 | `guarded-single-call-write-pipeline` | 18 | Token retirement and why, pipeline order, unsaved-work protection on rebind, shared budget, retired-field rejection; `OnlineProvider` finding and elicitation spike matrix (Task 6 item 5) |
| 5 | `canonical-json-contract` | 7 | Declarative null policy, reader-enforced required members, worker wire |
| 6 | `multiuser-owner-model` | 4 | Typed `ProjectBase` root, owner model, fail-closed ownership, no terminal cleanup, staged delivery; undelivered PR7–9 pointer to the kept spec or roadmap (Gate 1) |
| 7 | `access-tiers-lifecycle-confirmation-and-binding` | 8 | Capability-class modes, confirmation follows mode, explicit binding, content tools bound-only |
| 8 | `tool-surface-consistency` | 10 | Workstream C: closed arguments, shared batch rules, cursor plumbing, paging defaults |
| 9 | `hmi-read-contract` | 4 | Batched read-only Unified contract, opt-in snapshot cursors |
| 10 | `vci-workspace-primitives` | 3 | Standalone-only primitives, leaf-expanded scopes, breadcrumb-based loss recovery; points to ADR 11 for `portal_lost` |
| 11 | `fail-closed-surface-and-portal-loss` | 3 | Q1–Q6 decisions, canonical validation errors, `portal_lost` semantics; the `2fb5e9a` HMI read-interruption facts (Task 6 item 10) |
| 12 | `portal-as-rejection-authority` | 16 | One principle for PLC, Network and HMI writes: Portal is the authority, `engineering_rejected` by exception type, never message text; what was removed (collision/exists guards, per-item re-plan, read-back); must say plainly that the HMI 1.0 planner and 4-guard set are gone |
| 13 | `toolchain-and-test-infrastructure` | 5 | Asymmetric .NET 10 cutover, raise-only worker timeout, test-suite runtime policy, source-owned reference stubs |

Unmapped by an ADR (dropped unless the maintainer says otherwise): `plans/2026-08-15-pr27-review-fixes.md`, `specs/2026-08-07-docs-reorganization-design.md`, `specs/2026-09-26-open-bug-parallel-pr-delivery-design.md`.

- [ ] **Step 3: Draft by delegation, review by the maintainer**

One subagent per ADR (13 independent tasks; all fit in parallel). Each gets: this section's boundary rules, its row, its source files, its candidate draft, its reports, and the three-point instruction "verify each claim against the source plan/spec and against `docs/ARCHITECTURE.md`; point to the current home instead of restating behavior; quote nothing you did not read". Output is the ADR file only. After drafting run a review pass per ADR (a second agent that tries to refute each Decision sentence against the original files) and list refutations in the PR description.

- [ ] **Step 4: Kept originals**

For each file chosen at Gate 1: **copy** it (`cp`, not `git mv`; the originals are deleted in Task 13, and moving now would break the many links to them before Task 10 retargets them) to `docs/adr/records/<same basename>`, prepend the line `> Kept verbatim; frozen 2026-10-30; not current documentation. Links to removed siblings point at tag docs-archive-2026-10-30.`, and rewrite only its links (sibling → tag permalink; reports → new relative depth). Fill the `Record` column of the owning ADR. `docs/adr/README.md` must link `TEMPLATE.md` and every ADR and record, or the checker reports them unreachable.

- [ ] **Step 5: Index and verify**

Add `| [Architecture decision records](adr/README.md) | Why the design is as it is, in project order |` to `docs/README.md` (the full README rewrite is Task 12).

```bash
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | tail -3
git add docs/adr docs/README.md
git commit -m "docs: add ADR set compacted from plans and specs"
```
Expected: `0 error(s)` (the checker sees the new files before `git add`; ADRs reachable via `docs/README.md` → `docs/adr/README.md`; permalinks resolve in the tag).

---

### Task 9: Evidence classification and `inputs/README.md`

**Files:**
- Create: `docs/superpowers/acceptance/reports/inputs/README.md`

Runs after Gate 1; it consumes the recorded D5 answers. Per-file verdicts are in `recon-result.json` → `evidence[].entries[]` (path, category, `rawOpennessVerified`, `howVerified`, recommendation, reason). Defaults:

| Disposition | Items |
| --- | --- |
| **Keep (raw Openness; `native-probes` and `ui-P10` read individually, the other `ui-P*` files structure-checked against P10)** | `2026-10-29-hmi-write-native-probes.jsonl.gz`; `2026-10-29-hmi-ui-P10`, `P12-analog`, `P12-discrete`, `P12-table`, `P13`, `P19`, `P20-missing-member`, `P20-missing-tag` `.jsonl.gz`; `2026-10-29-hmi-native-probe/` (source that maps stage labels to native calls); `2026-10-29-hmi-ui-P19-compile.png` (UI proof; maintainer-supplied) |
| **Maintainer decides (default in bold)** | `2026-10-29-hmi-ui-compile-probe/` (source of the ui-P traces; **keep**), `2026-10-29-hmi-write-native-summary.json` (readable index of the raw trace; **keep**), `2026-10-30-hmi-write-native-rejections-live-cases.md` (31-variant table cited by `HMI_OPERATIONS_SUMMARY.md`; **fold into the report, remove**), `2026-10-26-plc-write-collision-namespace-probes.md` (only record of four collision directions cited as scoped; **keep**), `2026-10-29-network-write-native-rejections-live-rerun.md` (deep-linked from `NETWORK_OPERATIONS_SUMMARY.md` L11c and a test comment; **keep**), `2026-10-26-plc-write-live-closeout.md` (**remove**), `2026-10-30-hmi-write-timing-after.md` (report cites its median table; **inline the table into the report, remove**), `acceptance/2026-10-07-multiuser-pr4-live-definition.md` (a protocol, not evidence; **keep in place**) |
| **Remove (MCP-level traces, preflights, restoration/status dumps, timing outputs, tooling duplicates)** | everything else, including both timing directories (tooling replaced by Task 5), `2026-10-28-network-write-native-rejections-live-cases.md` (superseded by the rerun; 374 KB), `2026-10-29-hmi-write-baseline-preflight.json`, `…-final-restoration.json`, `…-installed-P7.json`, `…-ui-P10-cleanup-audit.json`, `…-ui-final-status.json`, and the `2026-10-30-hmi-write-native-rejections-*` directories and JSON |

Note for the index: the archived probe source was edited after the run only to remove the project path, so the recorded executable SHA-256 does not match the archived source. The `ui-P*` case records say `uiCompile: 'pending maintainer return'`; the maintainer's UI outcomes exist only in the checkpoint report text and the P19 PNG.

- [ ] **Step 1: Write `inputs/README.md`** — first line: "Only verified raw Openness call/response evidence is kept here; everything else is at tag `docs-archive-2026-10-30`." Then one table `File or directory | Report it backs | Raw Openness (how verified) | Notes` covering exactly the kept set recorded at Gate 1, plus a paragraph on the SHA-256 and `pending maintainer return` caveats above. Link `inputs/README.md` from `docs/superpowers/README.md` (Task 12 keeps that link) so the checker finds it. If Gate 2 changes the set, update this file in the same commit as the change.
- [ ] **Step 2: Move folded content before removal.** Where a default says "inline into the report" (HMI live-cases table, timing-after medians), add the short table to the existing report and fix the report's link in Task 10.
- [ ] **Step 3: Verify the nested attributes survive.** The five nested `.gitattributes` (`* -text`) live in directories being removed; kept evidence files are binary or byte-exact. Add `/docs/superpowers/acceptance/reports/inputs/** -text` to the root `.gitattributes` so kept raw evidence is never EOL-normalized after the nested files disappear.

```bash
git check-attr text -- docs/superpowers/acceptance/reports/inputs/2026-10-29-hmi-write-native-probes.jsonl.gz
git add .gitattributes docs/superpowers
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | tail -1
git commit -m "docs: index kept raw Openness evidence"
```
Expected: `0 error(s)`.
Expected: `text: unset`.

---

### Task 10: Rewrite links (everything except `AGENTS.md`, which is Task 12)

**Files:** every `*.md` outside the transient dirs that links to a removed file; `TiaMcpServer.Tests/Network/NetworkGuardedWriteMcpTests.cs` (comment only, ~line 218, if the evidence file it names is removed).

Policy (mechanical; the checker proves completeness):

| Source | Target removed | Replacement |
| --- | --- | --- |
| live docs (`ROADMAP.md`, `docs/**` not under `superpowers/`) | plan/spec covered by an ADR | relative link to that ADR (anchor dropped) |
| `README.md` | covered by an ADR or kept | absolute `https://github.com/Czarnak/tia-portal-mcp/blob/main/docs/adr/…` URL |
| live docs | kept original | relative link to `docs/adr/records/<file>` (anchor kept) |
| live docs, anything under `docs/superpowers/acceptance/` (reports and non-report files in `inputs/`) | evidence or uncovered plan/spec | absolute tag permalink `https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/<path>` (anchor kept; `/tree/` for a directory) |
| reports | any removed plan/spec, including ADR-covered ones | absolute tag permalink (a report cites the plan as its protocol; its wording is unchanged); a kept original links to `docs/adr/records/` |

- [ ] **Step 1: Build `link-map.json`**

From `adr-source-map.json` (ADR membership; prefix its paths with `docs/superpowers/`), an explicit slug → ADR-number table taken from Task 8, the Gate 1 kept list and the Gate 1 evidence verdicts, write `docs/superpowers/evidence/2026-10-30-docs-compaction-recon/link-map.json`:

```json
{
  "docs/superpowers/plans/2026-07-26-udt-db-external-source.md": {"adr": "docs/adr/0001-plc-content-exchange-and-creation.md"},
  "docs/superpowers/specs/2026-09-29-write-safety-redesign-design.md": {"record": "docs/adr/records/2026-09-29-write-safety-redesign-design.md"},
  "docs/superpowers/acceptance/reports/inputs/2026-10-28-network-write-native-rejections-live-cases.md": {"tag": true}
}
```
Generate the `adr` entries with a one-off script from `adr-source-map.json` (invert it: file → ADR file by the Task 8 numbering). Every removed plan/spec/evidence **file and every removed or partly removed directory that is linked** (for example `acceptance/reports/inputs/2026-10-29-hmi-write-timing-before`) needs a key; default `tag: true`. A kept original has both its `record` entry and, if it also belongs to an ADR, an `adr` entry (`record` wins). Build the evidence entries from the list of files that Task 13 will delete (`git ls-tree -r --name-only HEAD -- docs/superpowers/acceptance/reports/inputs` minus the kept set).

- [ ] **Step 2: Run the rewriter in dry-run, review the whole output, then apply**

`rewrite_links.py` (in the recon evidence directory; tested on a scratch repo for anchors, directory targets, CRLF preservation, fenced and inline code, README absolute URLs and `record`-over-`adr` precedence) rewrites inline links only. Its docstring states the map format and policy. Write the dry-run output to a file and read all of it (about 245 rewrites):

```bash
RECON=docs/superpowers/evidence/2026-10-30-docs-compaction-recon
python3 -I $RECON/rewrite_links.py --repo . --map $RECON/link-map.json --tag docs-archive-2026-10-30 > $RECON/rewrites.txt
tail -1 $RECON/rewrites.txt
python3 -I $RECON/rewrite_links.py --repo . --map $RECON/link-map.json --tag docs-archive-2026-10-30 --apply | tail -1
git diff --stat | tail -1
```
Reference-style links and backtick path mentions are not handled; Step 3 catches them. Query-string links (`?plain=1`) are skipped; grep for them.

- [ ] **Step 3: Fix what the rewriter cannot**

```bash
grep -rnE "superpowers/(plans|specs|evidence)|reports/inputs/" --include=*.md . | grep -vE "^\./docs/superpowers/(plans|specs|evidence)/|^\./docs/adr/" > /tmp/leftover-mentions.txt; wc -l /tmp/leftover-mentions.txt
grep -rnE "live-rerun|live-cases|superpowers" --include=*.cs TiaMcpServer TiaMcpServer.Tests
```
Hand-fix: backtick path mentions in `docs/ARCHITECTURE.md` (lines ~1038, 1345), `docs/roadmap/network-operations.md:57,229`, `docs/roadmap/export-import-format.md:174,200` and `docs/roadmap/json-contract.md` status narrative (trim "Status:" history that now lives in ADRs); `docs/development/codebase-review.md` lines 13–15 (state the baseline commit and link ADR 11 or the tag); the three dead `scripts/live-test-*.ps1` report links (make them plain code text); the test comment (point it at the kept file or the tag URL).

- [ ] **Step 4: Verify and commit**

```bash
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | grep -v "transient" | tail -5
```
Expected: `0 error(s)`. This only proves the links that exist; it cannot prove that no link still points at a file about to be deleted, because those files are still on disk. Task 11 Step 1 proves that with a dry removal. Then:

```bash
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
git add -A docs README.md ROADMAP.md TiaMcpServer.Tests
git commit -m "docs: retarget links to ADRs, kept records and the archive tag"
```
(`rewrites.txt` and `link-map.json` are committed with the rest of the recon evidence and deleted in Task 13.)

---

### Task 11: Dry removal, then GATE 2 — maintainer sign-off before anything is deleted (stop here)

- [ ] **Step 1: Prove no link still points at a file Task 13 will delete**

In a scratch worktree (never in the main checkout), run Task 13 Steps 1–2 and the strict checker:

```bash
git worktree add ../tia-portal-mcp-dryrun HEAD
cd ../tia-portal-mcp-dryrun
# repeat Task 13 Step 1 and Step 2 here, then:
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient
cd - && git worktree remove --force ../tia-portal-mcp-dryrun
```
Expected: `0 error(s), 0 warning(s)`. Any `broken link` or `not reachable` finding is a missed rewrite (or an unlinked `inputs/README.md` / `TEMPLATE.md`); fix it in the main checkout and repeat. (`git worktree remove --force` deletes only the scratch worktree.)

- [ ] **Step 2: Present to the maintainer:** (1) the evidence verdict table from Task 9 with the maintainer-decides defaults; (2) the final ADR files for review (the maintainer reads or spot-checks them; deciding relevance is theirs); (3) the output of `git diff --stat docs-archive-2026-10-30 -- docs | tail -1` as the deletion preview, and the list of files to be removed (`git ls-tree -r --name-only docs-archive-2026-10-30 -- docs/superpowers/plans docs/superpowers/specs docs/superpowers/acceptance/reports/inputs`) minus the kept set. Record the answers verbatim, with the date, in `gate2-decisions.md` next to `gate1-decisions.md`. Nothing is deleted before this file exists.

---

### Task 12: `AGENTS.md` loses its history; `docs/README.md` and `docs/superpowers/README.md` shrink

**Files:** `AGENTS.md`, `docs/README.md`, `docs/superpowers/README.md`

The audit (`recon-result.json` → `crossCutting.agents-history.blocks[]`) classified every block. Apply by hand. Line numbers refer to the file as of the archive tag (`git show docs-archive-2026-10-30:AGENTS.md`); Task 4 added lines above "Documentation layout", so locate rows below by heading or text, not by number:

- [ ] **Step 1: `AGENTS.md` — strip history, keep rules**

| Lines | Action |
| --- | --- |
| 1–3 | Keep (optionally drop `; PLC run/stop was removed`) |
| 5–16 (`bind_project` inspections) | Delete `Current counts are 8/19/19; PR3 had 7/17/17.` (counts already on line 3); reword "PR3" to "inspection actions"; keep all rules (discovery never adopts, exact raw-key selectors, foreign-PID refusal, budgeted binding value, no lifecycle elicitation) |
| 17–22 | Delete the PR3 live-authorization paragraph and its report link |
| 24–31 | Keep rules (typed owner by observed `.amc21` identity, null ALS provenance, continuous owner identity, descriptive capabilities, worker-owned sources block a different open); delete the "remain open" sentence (it is in `IMPROVEMENT_LOG.md`) |
| 32–35 | Delete (candidate `3772eda` acceptance narrative and report link) |
| 37–43 | Keep rules (`WithEngineeringRoot`, `ProjectCapabilityCatalog.OperationFor` is the only worker-method→capability map); delete the live sentence and link |
| 45–54 | Keep rules (writes/compile/save on local sessions, `LocalSession.Save()` identity re-check, never updates from/checks in to the server, `StandaloneOnlyProbes`, create/save-as/archive/close standalone-only); delete the live sentence and link |
| 56–116, 120, 123–127, 129–132, 134–164, 200–212, 228–239 | Keep as is (current rules; the "removed/retired" sentences double as do-not-reintroduce guards, keep them) |
| 118 (PLC) | Strip `, kept because Portal's own rejection was never reachable through the tool` and `scoped live cases cover …` |
| 119 (Network) | Strip the "scoped acceptance" link |
| 121 (HMI) | Strip the qualification/version sentences (`Qualification is complete within the recorded R1/R2/R3 scope: …`, `maintainer chose to retain them and leave O1 open`) and the "qualification record" link |
| 122 (VCI) | Strip the "scoped live report" link |
| 128 (Portal loss) | Strip the B-16/Q3 record link and the `Installed 2fb5e9a …` / `The maintainer accepted … 2026-10-20 …` sentences, **after** confirming the `portal-loss` ADR (or kept record) carries the `2fb5e9a` facts (Task 6 item 10) |
| 166–198 | Rename the heading to "Structured JSON contract rules"; drop "first tools on the opt-in canonical JSON contract" framing, line 192 "This is exactly the Phase 1 defect Phase 2 removed", and 196–198 "Every registered tool is structured today… split into `plc_read`/`plc_write`"; keep all six rules. Grep `CONTRIBUTING.md`, `docs/README.md`, `docs/development/codebase-review.md`, `docs/roadmap/json-contract.md` for the old heading's anchor first |
| 226 (docs table row) | Reword to: `docs/superpowers/` — live-verification reports under `acceptance/reports/`; transient working area for plans, specs, evidence (empty on `main`); add a row `docs/adr/` — architecture decision records, project history |

Restructure the heading above the multiuser paragraphs (lines 5–54 sit under "Two-process architecture") so the rules remain findable, e.g. a "### Multiuser (local sessions)" subsection.

- [ ] **Step 2: Add the history pointer** (replace the removed narrative; ≤ 10 lines)

```markdown
## Project history

This file states current rules only; it records no history. To find why something is the way it is:
- Decisions and project evolution: `docs/adr/README.md` (each ADR cites the original plans/specs).
- What was verified live, with scope and limits: the reports in `docs/superpowers/acceptance/reports/`; kept raw Openness evidence in its `inputs/README.md`.
- Open follow-ups: `docs/IMPROVEMENT_LOG.md`. Current design: `docs/ARCHITECTURE.md`. Per-area contracts: `docs/SupportedOperations/`.
- Removed plans, specs and evidence: `git show docs-archive-2026-10-30:<path>`; find a path with `git ls-tree -r --name-only docs-archive-2026-10-30 -- docs/superpowers | grep <term>`, or search history with `git log -S"<term>" -- docs/`.
Do not copy acceptance results or dated "live verified" statements into this file.
```
Expected result: ~220 lines (from 239), no `docs/superpowers/plans|specs` link, no dated acceptance narrative.

- [ ] **Step 3: `docs/README.md`**

Delete the ~40 superpowers rows in "Understanding the design" (keep Architecture, the paging-matrix row and the HMI/VCI/Multiuser reference rows); delete the "Project history" prose block (about lines 88–189) and the "planned compaction" paragraph; add a "Decisions and history" section with two rows: `[Architecture decision records](adr/README.md)` and `[Live verification reports and raw evidence](superpowers/README.md)`; delete the "Test suite runtime design" row (or re-point to `adr/records/` if kept); replace the top rule with the reachability wording from Task 4. Target ≈ 60 lines (from 189).

- [ ] **Step 4: `docs/superpowers/README.md`**

Rewrite as: a stub explaining the transient working area (rules link to `AGENTS.md`), the history pointer to `docs/adr/README.md`, and the existing **reports table** (keep all 42 rows, add the E-4 report and the live-definition document, and add a link to `acceptance/reports/inputs/README.md`; drop the Specs and Plans tables, 84 rows, and the PR 5/6 token-flow prose except a one-line note on those two reports).

- [ ] **Step 5: Verify and commit**

```bash
wc -l AGENTS.md docs/README.md docs/superpowers/README.md
grep -nE "[0-9]{4}-[0-9]{2}-[0-9]{2}|live verified|accepted by maintainer" AGENTS.md
git add AGENTS.md docs
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 | grep -v transient | tail -5
git commit -m "docs: move history out of AGENTS.md and slim the indexes"
```
Expected: line counts near 220 / 60 / ≈70; remaining date hits in `AGENTS.md` are rule text only (read each); no new errors.

---

### Task 13: Removal (its own commit, last)

Only after `gate2-decisions.md` exists and the Task 11 dry removal was clean.

- [ ] **Step 1: Remove superseded evidence and the timing directories**

```bash
KEEP='<newline-separated list of kept names from gate2-decisions.md>'
cd docs/superpowers/acceptance/reports/inputs
for f in *; do case "$f" in README.md) ;; *) grep -qxF "$f" <<<"$KEEP" || git rm -rq -- "$f";; esac; done
cd -
```
Verify `git ls-files docs/superpowers/acceptance/reports/inputs` lists exactly the Gate 2 set plus `README.md`.

- [ ] **Step 2: Remove plans, specs and recon evidence** — kept originals already moved to `docs/adr/records/` in Task 8.

```bash
git rm -rq docs/superpowers/plans docs/superpowers/specs docs/superpowers/evidence
```
This deletes this plan, the directions spec, the recon evidence, the gate decision files, `link-map.json` and `rewrites.txt`; the plan, spec and recon data stay in the tag, while the gate files, `link-map.json` and `rewrites.txt` (created after the tag) survive only in branch history and the PR description (re-run `git ls-tree docs-archive-2026-10-30 -- docs/superpowers/plans/2026-10-30-docs-lifecycle-and-adr-compaction.md` to confirm). If Gate 1 keeps this plan or the spec, move it to `docs/adr/records/` instead.

- [ ] **Step 3: Strict verification — this is the "no broken links" gate**

```bash
python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30 --fail-on-transient
python3 -I scripts/tests/test_verify_docs.py
git ls-files docs/superpowers | grep -E "^docs/superpowers/(plans|specs|evidence)/" | wc -l   # the checker already fails on these; this is a belt-and-braces count
```
Expected: `0 error(s), 0 warning(s)`; tests `OK`; `0`.

- [ ] **Step 4: Commit** (one commit, no other changes)

```bash
git commit -m "docs: remove compacted plans, specs and non-Openness evidence (archived at docs-archive-2026-10-30)"
```

---

### Task 14: Final verification, success criteria and PR

- [ ] **Step 1: Full test run** (docs-only branch, but scripts/tests and the CI pin changed)

```bash
dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests --no-build --filter "Category!=Packaging"
```
Expected: build succeeds, tests pass. The product `.csproj`, `Directory.Build.props`, `global.json`, `verify-doctor-package.ps1`, `verify-reference-stubs.ps1` and `reference-stubs/` are untouched, so the packaging filter is not required (`scripts/install-local-tool.ps1` is not in that list).

- [ ] **Step 2: Walk the spec's success criteria with commands**

| Criterion | Check |
| --- | --- |
| Representative PR shows code first, process material collapsed | Task 2 result recorded in the PR description |
| Retained plans/specs in single digits; ADRs cover the removed ones; each ADR points at the tagged original | `ls docs/adr/records \| wc -l` ≤ 4; `ls docs/adr/0*.md \| wc -l` = 13; `grep -L "docs-archive-2026-10-30" docs/adr/0*.md` prints nothing |
| `AGENTS.md` has no history narrative, only a pointer | Task 12 Step 5 greps; manual read of the diff |
| No broken links; documentation index matches layout | Task 13 Step 3 |
| Raw Openness evidence intact; removed evidence recoverable | `git ls-files docs/superpowers/acceptance/reports/inputs \| wc -l` equals the Gate 2 count; `git cat-file -e docs-archive-2026-10-30:docs/superpowers/acceptance/reports/inputs/2026-10-28-network-write-native-rejections-live-cases.md` exits 0 |
| New work follows the lifecycle without manual policing | `docs.yml` run on this PR is green; `AGENTS.md`, `CONTRIBUTING.md` and the PR template carry the rule |

- [ ] **Step 3: Open the PR** with a body that includes: the commit order (so reviewers read commit by commit: tag/attributes/checker/rule/timing/ADRs/links/AGENTS/removal), the collapse-test outcome, the ADR refutation list from Task 8, the Gate 1 and Gate 2 decisions, and the line "Original material: tag `docs-archive-2026-10-30`". Include the verbatim Gate 0/1/2 decisions (their files do not survive Task 13). Opening the PR is outward-facing: ask first. Do not merge; the maintainer reviews. After merge, nothing else is required (D2 default); if the maintainer chose post-merge pruning, the push check on `main` is the reminder.

---

## Self-review against the spec

| Spec item | Task |
| --- | --- |
| A. Review view (attributes, confirm on a test PR) | 2 |
| B. Lifecycle rule enforced where work happens (agent instructions, PR template, default output location) | 3, 4 |
| C. Review plans/specs, pick keepers, compact to ADRs | 7, 8 |
| C. Review each evidence file, keep raw Openness, remove rest | 9, 11, 13 |
| C. Keep reports, fix their links | 10 |
| C. Shared timing script, retire per-PR copies | 5, 13 |
| C. Tag the pre-removal state | 1 |
| D. `AGENTS.md` history out, pointer in | 12 |
| D. Align indexes and the "every doc listed" rule; ~200 links | 3, 4, 10, 12 |
| Principle: ADRs hold decisions, not steps; no fourth overlapping place | 8 (boundary table), Task 6 (migrations) |
| Principle: maintainer decides relevance | Gates 1 and 2 |
| Principle: each step leaves no broken links | checker run in every task |
| Principle: cheap reversible first, hands-on review last | task order |
| Open question: ADR layout/naming | §2, Task 8 |
| Open question: evidence lifecycle mechanism and trigger | Tasks 3, 4 (checker on push to `main`, PR-template items, AGENTS rule) |
| Open question: timing script location | Task 5 |
| Open question: doctor/packaging/CI references | Verified none; Task 1 fixes the one real coupling (`git describe`) |
| Open question: attribute choice | Task 2 |

Known limits to state in the PR: the `main` check informs rather than blocks (no required checks exist); pruning depends on agents reading `AGENTS.md` (the plugin-default path plus the checker are the safety net); retrospective ADRs are inferences from deleted documents, which is why each cites its originals and why Task 8 includes a refutation pass.
