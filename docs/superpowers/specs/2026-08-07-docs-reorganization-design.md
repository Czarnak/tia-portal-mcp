# Documentation Reorganization — Design (2026-08-07)

## Problem

The repository has 41 tracked markdown files totalling roughly 15,000 lines. The volume is
not the problem; the lack of differentiation is. Four audiences — users, AI agents,
contributors, and the maintainer of record — share one undifferentiated namespace with no
index anywhere.

Concretely:

1. **`README.md` is 654 lines and does six jobs.** About 300 of those lines are contributor
   and maintainer procedure sitting in the project's front door. The single largest block,
   `## Local MCP Sandbox Testing` (L338–520), is 183 lines of a developer test loop.
2. **`docs/` is a flat pile with no index.** All five top-level files are orphans — nothing
   in the repository links to `ARCHITECTURE.md`, `IMPROVEMENT_PLAN.md`, `LOCAL_TOOL_INSTALL.md`,
   or `EXPORT_IMPORT_FORMAT_ROADMAP.md`. They are findable only by browsing the folder.
3. **Four documents are all "the roadmap":** root `ROADMAP.md` (17 lines, strategic, orphan),
   `docs/NETWORK_OPERATIONS_ROADMAP.md` (266 lines, tactical),
   `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` (225 lines, tactical), and
   `docs/IMPROVEMENT_PLAN.md` (332 lines) — which is really a changelog: half its headings
   end in `— DONE 2026-07-2x`.
4. **`CLAUDE.md` and `AGENTS.md` are byte-identical duplicates** (6,291 bytes each), which
   means agent instructions have two sources of truth that can silently diverge.
5. **`docs/superpowers/` is 12,000+ lines** — 80% of all tracked documentation text — of
   specs, plans, and acceptance reports, of which 11 of 16 files are orphans. It is
   development-process history occupying the same tree a user browses for help.

`docs/SupportedOperations/` is the exception: it has a README index and dense working
cross-links. It is the model to copy, not a problem to fix.

## Goals

- A first-time visitor can decide "is this for me" and get running without scrolling past
  maintainer procedure.
- Every tracked document under `docs/` is reachable from an index.
- Documents are grouped by who reads them, not by what type of file they are.
- The structure does not silently re-rot after this pass.

## Non-goals

- **No content rewriting.** Every existing section is *moved*, not reworded. The only new
  prose is three index files and the `CLAUDE.md` pointer.
- **No renaming of `docs/SupportedOperations/`.** It is the healthiest tree in the repo and
  carries dense inbound links from 13 files. Converting it to `docs/operations/` with
  kebab-case filenames is a cosmetic win against real breakage risk. Possible later pass.
- **No merging of the three roadmaps.** `ROADMAP.md` is strategic and the other two are
  per-feature-area tactical plans. That split is correct; they only need to link to each other.
- **No relocation of `docs/superpowers/`.** Decision taken: it stays in place, gains an index
  and an explicit "historical process artifacts" label.
- **`priv/`** (gitignored) and **`.superpowers/sdd/`** (untracked) are out of scope.

## Approach

Organize by audience. Two alternatives were considered and rejected:

- **Flat `docs/` with a strong index** — least churn, but README extraction adds seven files,
  putting the flat tree at ~12 top-level documents. That is the state it is already in, and it
  re-rots for the same reason.
- **Diátaxis** (tutorial / how-to / reference / explanation) — principled, but the repository
  has no tutorials, and the framework's vocabulary would fight the established
  `SupportedOperations` and `ARCHITECTURE` names.

## Target structure

```
README.md                        654 -> ~175 lines. Landing page only.
CONTRIBUTING.md                  keeps its short setup section; links to development/building.md
ROADMAP.md                       unchanged content; gains links to the two tactical roadmaps
SECURITY.md                      unchanged
AGENTS.md                        canonical agent instructions (single source of truth)
CLAUDE.md                        -> 2-line pointer to AGENTS.md

docs/
  README.md                      NEW - the index, routes by audience
  ARCHITECTURE.md                unchanged (490 lines, already good; cited by scripts/)
  IMPROVEMENT_LOG.md             renamed from IMPROVEMENT_PLAN.md; open items above completed

  guides/                        for people USING the server
    installation.md
    mcp-client-configuration.md
    troubleshooting.md

  development/                   for people BUILDING the server
    building.md
    local-mcp-testing.md
    packaging.md

  roadmap/
    network-operations.md        from NETWORK_OPERATIONS_ROADMAP.md
    export-import-format.md      from EXPORT_IMPORT_FORMAT_ROADMAP.md

  SupportedOperations/           unchanged
  superpowers/
    README.md                    NEW - "historical process artifacts" label + index
    specs/  plans/  acceptance/  unchanged
```

## Move map

### README.md extractions

Line ranges refer to the current 654-line `README.md`.

| Current section | Lines | Destination |
| --- | --- | --- |
| `## Requirements` | 82–121 | `docs/guides/installation.md` |
| `## Install` (incl. Version flag, Doctor command, Register with an MCP client) | 122–218 | `docs/guides/installation.md` |
| `### Access modes` | 219–277 | `docs/guides/installation.md` |
| `## Build From Source` + `### Coverage` | 278–305 | `docs/development/building.md` |
| `## Run Locally` | 306–337 | `docs/development/building.md` |
| `## Local MCP Sandbox Testing` | 338–520 | `docs/development/local-mcp-testing.md` |
| `## Local Package Build` | 521–560 | `docs/development/packaging.md` |
| `## Block Paths` | 561–573 | `docs/guides/mcp-client-configuration.md` |
| `## MCP Client Configuration` | 574–613 | `docs/guides/mcp-client-configuration.md` |
| `## Troubleshooting` | 614–622 | `docs/guides/troubleshooting.md` |
| `## Verified TIA Portal V21 behavior` | 623–641 | `docs/guides/troubleshooting.md` |

Note: lines 202, 205, 208, and 211 are shell comments inside a fenced PowerShell block, not
headings. They travel with the `Install` section.

### Whole-file moves and renames

| From | To | Note |
| --- | --- | --- |
| `docs/LOCAL_TOOL_INSTALL.md` | `docs/development/packaging.md` | merged with README `Local Package Build`; both cover installing a local build as the `tia-mcp` tool |
| `docs/NETWORK_OPERATIONS_ROADMAP.md` | `docs/roadmap/network-operations.md` | 4 inbound links to fix |
| `docs/EXPORT_IMPORT_FORMAT_ROADMAP.md` | `docs/roadmap/export-import-format.md` | orphan; no inbound links |
| `docs/IMPROVEMENT_PLAN.md` | `docs/IMPROVEMENT_LOG.md` | rename + reorder only |

All moves use `git mv` so history follows the file.

## README target

The trimmed `README.md` keeps only what a first-time visitor needs to decide "is this for me,
and how do I start":

1. Badges and pitch (existing L1–16)
2. `## Tools` — the 14/4 tool tables (existing L17–58)
3. `## Write safety` — the preview-then-apply model (existing L59–70)
4. `## Architecture` — two-process summary, linking to `docs/ARCHITECTURE.md` (existing L71–81)
5. `## Quick start` — NEW, ~25 lines. Leads with `dotnet tool install` as the primary path for
   normal users: prerequisites condensed to one table, the install command, one client
   registration command, then a link to `docs/guides/installation.md` for everything else.
   Building from source is named as the contributor path with a link to
   `docs/development/building.md`, not shown inline.
6. `## Documentation` — NEW, the map: guides / architecture / supported operations /
   development / roadmap
7. Contributing, Security, Check other tools (existing L642–654)

Target length 160–180 lines.

### NuGet constraint (important)

`TiaMcpServer/TiaMcpServer.csproj` sets `<PackageReadmeFile>README.md</PackageReadmeFile>` and
packs `../README.md` to the package root. The README is therefore also the nuget.org listing
page, where relative links such as `docs/guides/installation.md` do not resolve.

**Every cross-document link in `README.md` must be an absolute
`https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URL.** This rule applies only to
`README.md`. All other documents use relative links.

This constraint already applies to the existing README and is not introduced by this change,
but the reorganization greatly increases the number of outbound links, so it must be explicit.

## New index files

### `docs/README.md`

The audience router. One section per audience, each a short list of links with a one-line
description of what the reader will find:

- **Using the server** — installation, MCP client configuration, troubleshooting, supported
  operations
- **Understanding the design** — architecture
- **Building and contributing** — building, local MCP testing, packaging, CONTRIBUTING
- **Direction** — root roadmap, the two tactical roadmaps, improvement log
- **Project history** — pointer to `docs/superpowers/`, labelled as historical process
  artifacts

### `docs/superpowers/README.md`

Opens with an explicit statement that this tree is development-process history — design specs,
implementation plans, and live acceptance reports produced while building features — and is
**not** current user or contributor documentation, may describe superseded designs, and is kept
for auditability. Then three grouped lists (specs, plans, acceptance reports) in reverse
chronological order, each with its date and subject.

### `CLAUDE.md`

Replaced by a two-line pointer to `AGENTS.md`. A file (not a symlink) because symlinks are
unreliable across Windows checkouts and git configurations. This removes the byte-identical
duplication while keeping the filename that Claude Code looks for.

## Guardrails

Restructuring alone will not hold — this tree was organized once already. Two mechanisms:

1. **`docs/README.md` is the contract.** Add a rule to `AGENTS.md` and `CONTRIBUTING.md`: a new
   document under `docs/` is not complete until it has an entry in `docs/README.md`. This is
   exactly what would have caught all five current orphans.
2. **Markdown link check in CI.** A step that fails on broken relative links, so a moved file
   breaks the build instead of silently orphaning. This matters most *during* the migration:
   `docs/SupportedOperations/*` has dense internal cross-links, and
   `NETWORK_OPERATIONS_ROADMAP.md` is cited from four places.

## Migration order

Each step leaves the repository in a consistent state.

1. Create `docs/guides/`, `docs/development/`, `docs/roadmap/`.
2. `git mv` the four whole-file moves and renames.
3. Extract the eleven README sections into their destination files, each with a short
   orienting sentence at the top.
4. Rewrite `README.md` to the target outline, with absolute GitHub URLs.
5. Write `docs/README.md` and `docs/superpowers/README.md`.
6. Fix inbound links: 4 references to `NETWORK_OPERATIONS_ROADMAP.md`, plus
   `CONTRIBUTING.md` -> `docs/development/building.md`.
7. Reorder `docs/IMPROVEMENT_LOG.md` — open and deferred items above completed entries.
8. Replace `CLAUDE.md` with the pointer.
9. Add the CI link check and the index rule to `AGENTS.md` / `CONTRIBUTING.md`.

## Verification

- Link check passes with zero broken relative links across all tracked markdown.
- Orphan check: re-run the inbound-link analysis; every tracked document under `docs/` except
  `docs/README.md` itself has at least one inbound link.
- `README.md` is 160–180 lines and contains no relative markdown links.
- `dotnet pack` succeeds and the packed README renders with working links (absolute URLs).
- `scripts/live-test-network-phase2.ps1` still resolves — it cites `docs/ARCHITECTURE.md`,
  a path this design does not move.
- `git log --follow` works on each moved file, confirming history was preserved.

## Risks

| Risk | Mitigation |
| --- | --- |
| External deep links to README anchors break | Unavoidable for a split of this size; the new `## Documentation` section gives arrivals a route to the moved content |
| nuget.org listing shows broken links | Absolute-URL rule, verified by the `dotnet pack` check |
| `SupportedOperations` cross-links broken during roadmap move | CI link check added before step 6 |
| `IMPROVEMENT_LOG.md` reorder loses context | Reorder only — no entry is edited or deleted |
