# ADR-0017: Library writes apply without a planning pass

**Status:** Accepted
**Decided:** 2026-11-07   **Recorded:** 2026-11-08
**Area:** library
**Supersedes:** [ADR-0016](0016-library-write-staging-and-lifecycle-prompt.md)'s blanket prohibition on intra-call dependencies; its lifecycle confirmation, capability and ownership decisions remain in force.
**Current home:** [ARCHITECTURE: Library writes](../ARCHITECTURE.md#library-writes-library_write); [Library operations summary](../SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md#writes-library_write)

## Context

Library-type work needs ordered calls that create a type, create or release a version, then use the result. Planning every item against the initial library prevents those dependencies and makes a later selector miss or collision deny earlier independent work. Portal already refuses most invalid mutations; the server must keep its own checks beside the native call instead of treating a prior read as authority.

## Decision

- `library_write` plans only for a dry run or a read-write call that prompts for global-library lifecycle. The opt-in `PlansBeforeApply` hook leaves other write domains unchanged and is safe only for domains without acknowledge guards.
- On unplanned apply, each item resolves its target and performs tool-owned checks immediately before the native call. Later items can use earlier creations. Planned calls describe dependent objects as "created by item N"; a library created, opened or retrieved by a lifecycle item in a prompting call still needs a later call.
- `create_type` converts a plain PLC object under `ExclusiveAccess` and one project transaction. This protects the delete-and-import conversion without promising batch rollback.
- Type content changes through `create_type_version`, using `Versions.CreateFromDocuments` without a test-environment argument. The rejected edit-then-`plc_write` route can block behind a modal Portal dialog that does not raise the Openness `Confirmation` event.
- Release and synchronize report the default before and after and never restore it. A caller that needs the old default adds an explicit `set_default_type_version` item.
- Verification uses resolved identities and typed outcomes. A completed item is trusted or superseded; this remains separate from the native read-back evidence carried by type-operation results.

## Consequences

- Unplanned apply returns no planned effects. Target misses and master-copy collisions fail the current item after earlier items may have succeeded; delete-folder information is available in previews and other planned calls. Callers inspect actual results before retrying.
- Planned block guards still deny the whole call before mutation. The same tool-owned checks run again beside the native mutation.
- There is no batch rollback, implicit default restoration, automatic dependency release or apply-time delete-instance scan. Portal remains the refusal authority; previews expose known deletion blockers.
- Other domains retain their current planning behavior until they explicitly adopt the hook. Library read snapshots retain their existing behavior.

## Evidence

- [Library-write native-rejection probes](../superpowers/acceptance/reports/2026-11-06-library-write-native-probes.md)
- [Library-type native probes](../superpowers/acceptance/reports/2026-11-07-library-types-native-probes.md)
- [Library-type live verification](../superpowers/acceptance/reports/2026-11-08-library-types-live-verification.md) (scoped cases and limits).
