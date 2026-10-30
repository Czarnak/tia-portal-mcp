# Docs lifecycle and project-memory compaction — design (directions)

**Status:** draft directions, 2026-10-30. This spec fixes intent and decisions only. The
implementation plan is written afterwards, locally, following a full codebase and documentation
review; anything marked *open* below is settled there, not here.

**Branch:** `chore/docs-lifecycle-and-adr-compaction`. It carries this spec, the plan and the
implementation of both.

## 1. Problem

`docs/superpowers/` is about 80k lines, roughly half the size of the C# source and about
eleven times the rest of `docs/`. Two consequences follow:

- **Review signal.** Large feature PRs show their documentation as most of the diff (for
  example PR 203: about 9.5k superpowers lines against about 2.5k lines of C#). Most of it was
  raw evidence files, not plans or specs. Reviewing the actual code evolution between PRs is
  hard.
- **Stale material mixed with project memory.** Plans are execution checklists whose value ends
  at merge, and they partly duplicate code. Reports and a few decision records are genuinely
  useful later, but nothing separates them from the rest.

Repository size is not the concern (`.git` is a few MB). Reviewability and findability are.

## 2. Goals and non-goals

Goals:

1. A PR's Files-changed view shows code first; process material is collapsed.
2. Project memory is small, curated and easy for a person or agent to find.
3. New material follows a lifecycle rule, so the tree does not grow back.

Non-goals: changing product behavior or any contract; deleting history from git; rewriting
reports of live verification; changing how acceptance is performed.

## 3. Decisions already taken

- **Review view.** A collapsed Files-changed view is sufficient. No change to where files live
  is needed for this goal alone.
- **Plans and specs are not kept as a record.** The maintainer does not read them after merge.
  Only about two plans and two specs remain worth keeping verbatim, chosen by the maintainer
  during review. The rest is compacted into a small number of ADRs showing project evolution.
- **Live-verification reports are kept.** They are the material actually revisited.
- **Raw evidence.** Raw Openness call/response evidence stays, because it records exactly what
  was called. Whether a given file qualifies is verified file by file (a trace relayed through
  the MCP server is not automatically raw Openness evidence). Other evidence (MCP-level traces,
  preflights, restoration and status dumps, timing outputs) goes after the PR that needed it.
- **Evidence has a lifecycle.** It may sit in the PR that produced it, for measurement and
  reproduction, and is pruned after merge except for the raw Openness material.
- **AGENTS.md carries no history.** It keeps only current rules plus a pointer telling an agent
  where history lives and how to find it. Existing history narrative moves to ADRs/reports.
- **Timing harness.** Reusable measurement tooling becomes a shared script instead of a copy per
  PR.
- **Recoverability.** Anything removed from the tree stays reachable through git. A tag on the
  pre-removal commit gives ADRs and the history pointer a stable permalink.

## 4. Directions

Four workstreams. Order and sizing belong to the plan.

**A. Review view.** Mark process material so it collapses in PR diffs, using repository
attributes. Confirm the actual behavior on a test PR before relying on it.

**B. Lifecycle rule for new material.** Define where new plans, specs and evidence land, what
is expected of them at merge, and what is pruned after it. The rule must be enforced where work
actually happens (agent instructions, PR template), not only documented. It should decide the
default output location, which today is the superpowers plugin default.

**C. One-time compaction.**

- Review all plans and specs; pick the few to keep; compact the remainder into ADRs.
- Review each evidence file; keep raw Openness evidence, remove the rest.
- Keep reports; fix their links to whatever survives.
- Introduce the shared timing script and retire the per-PR copies.
- Tag the pre-removal state.

**D. Entry points.** Strip history out of `AGENTS.md` and leave a history pointer. Align the
`docs/` indexes and the "every doc is listed" rule with the new layout, including the roughly
two hundred links into the removed material.

## 5. Principles for the plan

- ADRs hold decisions and consequences, not execution steps; they must not become a fourth
  overlapping place next to `ARCHITECTURE.md`, `IMPROVEMENT_LOG.md` and reports.
- Compaction is reviewed by the maintainer. Drafting may be delegated; deciding relevance is not.
- Each step leaves the docs tree with no broken links.
- Make the cheap, reversible changes first, and the changes needing hands-on review last.

## 6. Open questions for the plan

- Layout and naming of the ADR set and of the kept plans/specs; fate of the superpowers index.
- Exact rule and mechanism for the evidence lifecycle, and where the post-merge prune is
  triggered or checked.
- Where the shared timing script lives and what it replaces.
- Whether the doctor/packaging/CI checks reference any of the moved material (a first scan
  found no references in tests, scripts or CI, to be confirmed).
- Which attribute reliably collapses diffs on the maintainer's GitHub setup.

## 7. Success criteria

- A representative PR shows its code first, with process material collapsed.
- The number of retained plans/specs is in single digits, ADRs cover the removed ones, and every
  ADR can point to the tagged original.
- `AGENTS.md` contains no history narrative, only a pointer.
- No broken links in the docs tree; the documentation index matches the new layout.
- Raw Openness evidence is intact; removed evidence is recoverable from the tag.
- New work follows the lifecycle rule without manual policing.
