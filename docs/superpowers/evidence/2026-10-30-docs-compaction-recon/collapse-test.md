# Collapse test (Task 2, D9)

Probe PR #204 (draft, closed), 2026-10-31. Maintainer observed in GitHub "Files changed":

- Deleted plan `plans/2026-08-15-pr27-review-fixes.md`: collapsed
- Renamed spec `specs/2026-09-26-open-bug-parallel-pr-delivery-design-renamed.md`: collapsed
- Edited plan `plans/2026-10-30-docs-lifecycle-and-adr-compaction.md`: collapsed
- Edited report and `AGENTS.md` (negative controls): expanded

API totals: 5 files, +3/-302; collapsed files are still counted in the totals.
No fallback needed: deletions and renames collapse with the attribute on the base branch.
