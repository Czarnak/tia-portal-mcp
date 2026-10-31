# Architecture decision records

An ADR records one lasting decision: why it was made, what was rejected, and what it still forces on the code. ADRs 0001-0013 were compacted on 2026-10-30 from the plans and specs written during development; each cites its originals at the archive tag.

To add one, copy [TEMPLATE.md](TEMPLATE.md) to `NNNN-<slug>.md` with the next free number, add a row below, and land it in the same PR as the change.

## Content boundaries

| Place | Holds |
| --- | --- |
| `docs/ARCHITECTURE.md` | How the system works now |
| ADR | Why, what was rejected, what the choice still forces |
| `docs/IMPROVEMENT_LOG.md` | Open follow-ups and the latest completed entry |
| Live-verification reports | What was verified, with scope and limits |

- Each fact has one home: `ARCHITECTURE.md` = how the system works now; ADR = why, what was rejected, what the choice still forces; `IMPROVEMENT_LOG.md` = open follow-ups and the latest completed entry; reports = what was verified.
- An ADR states no field lists, schemas, limits, thresholds, test counts, timings, case matrices, candidate hashes, follow-ups or task lists; it links instead.
- An ADR is immutable once Accepted except Status/Superseded-by lines and link fixes; a reversal is a new ADR.
- Admission test for a new ADR: a decision with a considered alternative or lasting constraint that a future maintainer could plausibly reverse by mistake. Bug fixes, refactors and plain feature additions do not get one.
- Length cap: one screen per ADR (~60 lines).

## Decisions

| No | Decision | Status | Area | Decided | Record |
| --- | --- | --- | --- | --- | --- |
| [0001](0001-plc-content-exchange-and-creation.md) | PLC content is exchanged as strict single-object external source and created by server-owned rules | Accepted | plc | 2026-07-26 to 2026-10-09 |  |
| [0002](0002-bounded-project-and-hardware-reads.md) | Large project and hardware reads are bounded, complete and cursor-paged | Accepted | json-contract | 2026-07-31 to 2026-09-06 |  |
| [0003](0003-network-domain-identity-and-subnet-lifecycle.md) | Network is an independent domain with exact, fail-closed identity and gated phase delivery | Accepted | network | 2026-08-01 to 2026-10-23 |  |
| [0004](0004-guarded-single-call-write-pipeline.md) | Writes run as one guarded call; safety tokens are retired | Accepted | write-safety | 2026-09-01 to 2026-10-21 |  |
| [0005](0005-canonical-json-contract.md) | Worker payloads carry a declared null policy and are decoded by one reader that enforces required members | Accepted | json-contract | 2026-09-28 to 2026-10-25 |  |
| [0006](0006-multiuser-owner-model.md) | Multiuser local sessions run through a typed common root with fail-closed ownership | Accepted | multiuser | 2026-09-28 to 2026-10-09 | [multiuser engineering design](records/2026-09-28-multiuser-engineering-design.md) |
| [0007](0007-access-tiers-lifecycle-confirmation-and-binding.md) | Access modes are capability presets, lifecycle confirmation follows the mode, and content acts only on an explicitly bound project | Accepted | write-safety | 2026-08-28 to 2026-10-22 |  |
| [0008](0008-tool-surface-consistency.md) | Tool surface rules are shared and derived once, not repeated per tool | Accepted | process | 2026-10-21 to 2026-10-24 |  |
| [0009](0009-hmi-read-contract.md) | hmi_read is a batched read-only WinCC Unified contract with opt-in snapshot cursors | Accepted | hmi | 2026-10-07 to 2026-10-12 |  |
| [0010](0010-vci-workspace-primitives.md) | VCI is exposed as standalone-only workspace primitives over leaf-expanded scopes | Accepted | vci | 2026-10-14 to 2026-10-16 |  |
| [0011](0011-fail-closed-surface-and-portal-loss.md) | The tool surface fails closed, and Portal loss is classified once at the worker boundary | Accepted | process | 2026-10-17 to 2026-10-20 | [simplification review](records/2026-10-17-codebase-simplification-review.md), [findings](records/2026-10-17-codebase-simplification-review-findings.md) |
| [0012](0012-portal-as-rejection-authority.md) | TIA Portal is the authority on its own rejections; the server guards only what Portal does not report | Accepted | write-safety | 2026-08-06 to 2026-10-30 |  |
| [0013](0013-toolchain-and-test-infrastructure.md) | The toolchain moves asymmetrically, and its build and test infrastructure is source-owned and policy-bound | Accepted | process | 2026-09-12 to 2026-10-24 |  |

`Record` links an original plan or spec kept verbatim under `records/` because it holds material no ADR carries.

## History

The plans, specs and non-Openness evidence these ADRs replace were removed from `main` after tag `docs-archive-2026-10-30`. Read one with `git show docs-archive-2026-10-30:<path>`, list them with `git ls-tree -r --name-only docs-archive-2026-10-30 -- docs/superpowers`, or browse https://github.com/Czarnak/tia-portal-mcp/tree/docs-archive-2026-10-30/docs/superpowers. Live-verification reports stay in [docs/superpowers](../superpowers/README.md).
