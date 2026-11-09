# ADR-0019: Global-library metadata edits use native transactions and end-state checks

**Status:** Accepted
**Decided:** 2026-11-09   **Recorded:** 2026-11-09
**Area:** library
**Current home:** [Library metadata edits](../SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md#comment-and-language-edits)

## Context

Global comment and language edits can affect several related native values. A setter can fail or
silently leave an unexpected end-state; a second verification RPC cannot make a partially applied
item atomic. Native probes also found different Siemens refusal types for read-only comments and
language activation, so a single server-side read-only rule would conceal Portal's actual result.

## Decision

Each metadata item resolves targets and captures before-state under native exclusive access and a
library transaction, then checks requested changes and preservation of unrelated metadata before
requesting commit. A completed rollback discards that item's setters. Evidence describes only reads
made inside the transaction; failure never triggers a reread or an extra verification RPC.

Portal remains the support and refusal authority. Read-only comments were refused with
`EngineeringNotSupportedException`; activation with `EngineeringTargetInvocationException`. Preserve
the shared type-based classification, including Portal-loss precedence, instead of adding a guard.
Use last-writer-wins semantics rather than an optimistic text fingerprint. Keep explicit saves.

## Consequences

- An item is protected by its native transaction; the ordered batch has no rollback or replay.
- Exact end-state checks detect ignored or altered setters without replacing native refusal handling.
- Commit or scope-disposal failure leaves state unknown and must not publish a rollback guarantee or
  outcome. The caller inspects current metadata before deciding whether to retry.
- A read or preview does not reserve text against a later edit by a user or another session.

## Evidence

- [Native probe report and verified inputs](../superpowers/acceptance/reports/2026-11-09-library-metadata-pr-b-native-probes.md).
- [Built-MCP acceptance and provenance](../superpowers/acceptance/reports/2026-11-09-library-metadata-pr-b-live-verification.md).
- [PR A metadata-read/creation-author acceptance](../superpowers/acceptance/reports/2026-11-09-library-metadata-pr-a-live-verification.md),
  delivered in [PR #230](https://github.com/Czarnak/tia-portal-mcp/pull/230).
- [Issue #218](https://github.com/Czarnak/tia-portal-mcp/issues/218) motivated the delivered metadata scope;
  the contributor's broader bundle was not applied unchanged.
