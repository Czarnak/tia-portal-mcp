# Multiuser preparation and acceptance boundary

PR 2 prepares the worker's internal engineering context. It does not deliver public Multiuser
operations or complete Issue #65. Existing standalone `.ap21` selection and lifecycle remain
the supported public surface, with 5/15/15 tools in read-only/read-write/full.

## Internal context

`TiaPortalSession` stores an `ActiveProjectContext` whose `ProjectBase` root comes from its
typed owner. `StandaloneProjectOwner` retains a `Project`; `LocalSessionOwner` passively
retains a `LocalSession` and its `MultiuserProject`. A local/server owner exposes no save,
close, discard, or commit abstraction, and no production caller activates one in this PR.
Current content services use a standalone `Project` compatibility projection. Status,
lifecycle, and worker dispatch resolve the standalone owner explicitly; local/server owners
are refused with `target_kind_unsupported` at these boundaries.

Identity remains a projection of the authoritative session. Rewrapping the same root does
not advance generation; a replacement root or accepted path change does. Detach releases
ownership, and adoption never restores it. Shutdown releases Portal resources without
claiming project save, session discard, or revision commit. See
[architecture](../ARCHITECTURE.md#internal-active-project-context).

Capabilities are internal, read-only, and empty; remote identity and connection observation
are null. They are not an advertised compatibility matrix and grant no authorization.

## Public Multiuser work still pending

No Multiuser tool or worker operation, `.als21` bind/open or startup selection, local-session
content compatibility, local save, discard, commit, Project Server inventory/connectivity,
or server mutation is enabled. Generic standalone lifecycle cannot replace a synthetic
local/server source. Later PRs must individually establish capability, typed contracts,
guarded execution, and operation-specific live evidence before enabling their public paths.

## Acceptance boundary

Source-linked in-memory Siemens doubles exercise the actual context/session source offline;
FakeWorker tests establish host protocol, binding, confirmation, and audit behavior. These
checks cannot prove installed Openness runtime behavior. Generated references are compile-only;
installed-reference compilation and package exclusion are separate qualification checks.

Fresh standalone live selection/switching and lifecycle acceptance on a frozen PR 2 candidate,
including headless and adverse-state cases, remains pending. Historical reports do not qualify
this candidate. The [PR 2 implementation plan](../superpowers/plans/2026-10-05-multiuser-pr2-active-project-context.md)
defines the exact matrix and fixture authorization boundary.
The [qualification and fixture preparation record](../superpowers/acceptance/reports/2026-10-05-multiuser-pr2-active-project-context-live.md)
lists the exact project contents and proposed destinations; live acceptance remains pending.
