# TIA Portal Multiuser Operations

## Support status

The current MCP session model is based on a local `.ap21` project path and one active project binding. It does not provide a Multiuser service or local-session object model.

## Current limits

The current surface does not provide:

- Project-server discovery or connection.
- Creation, opening, updating, or closing of Multiuser local sessions.
- `Update()` synchronization or `CheckIn()` operations.
- Conflict resolution through `AccessOptions`.
- Multiuser commissioning workflows or asynchronous commissioning states.
- VCI and version-control operations.

For local project lifecycle operations, see [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md).
