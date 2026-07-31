# TIA Portal Multiuser Operations

## Scope

This area covers server projects, local sessions, update/check-in workflows, conflict handling, and multiuser commissioning.

## Exposed operations

No Multiuser-specific public MCP operation was found.

The project lifecycle tools operate on a local `.ap21` path and a single MCP session binding. They do not expose `MultiuserService`, `LocalSession`, `ServerProject`, local-session update/check-in, `AccessOptions`, or `MultiuserCommissioningService`.

## Not exposed

- Locating or connecting to a project server.
- Creating, opening, updating, or closing local sessions.
- `Update()` synchronization and `CheckIn()` operations.
- Conflict resolution through `AccessOptions`.
- Multiuser commissioning workflows and asynchronous commissioning states.
- VCI/version-control operations, which are also outside the current MCP surface.

## Static evidence

No multiuser operation appears in the batch operation catalog, public tool descriptions, worker request dispatch, or worker Openness helper tree. See [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md) for the local project lifecycle that is exposed.
