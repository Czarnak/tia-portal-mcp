# TIA Portal Teamcenter Operations

## Scope

This area covers Teamcenter storage access and managed-project service management.

## Exposed operations

No Teamcenter-specific public MCP operation was found.

The MCP can open, read, save, archive, and close ordinary project paths through its project lifecycle surface, but no operation exposes `TeamcenterService`, `TeamcenterStorage`, managed-project associations, or Teamcenter connection/permission handling.

## Not exposed

- Teamcenter service discovery and availability checks.
- Teamcenter storage enumeration or management.
- Managed project association and Teamcenter-backed project lifecycle.
- Teamcenter-specific connection, authentication, or exception workflows.

## Static evidence

No Teamcenter operation appears in `TiaMcpServer/Batch/BatchOperationCatalog.cs`, `TiaMcpServer.Contracts/WorkerRequest.cs`, or `TiaMcpServer.OpennessWorker/Program.cs`. The exposed project operations are listed in [PROJECT_OPERATIONS_SUMMARY.md](PROJECT_OPERATIONS_SUMMARY.md).
