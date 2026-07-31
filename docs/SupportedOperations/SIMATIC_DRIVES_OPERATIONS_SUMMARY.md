# SIMATIC Drives and Startdrive Operations

## Scope

This area covers Startdrive, SINAMICS, SIMATIC Drive Controller, PROFIdrive properties, telegrams, commissioning, and drive-specific download/upload configuration.

## Exposed operations

No drive-specific public MCP operation was found.

Generic hardware browsing, hardware configuration reads, equipment-catalog search, network-device creation/configuration, and generic compile checks may encounter a project containing a drive, but they do not expose `DriveObject`, drive parameters, telegrams, Drive Function Interface services, commissioning, or drive-specific transfer configuration.

## Not exposed

- Drive-object navigation and activation/type handling.
- SINAMICS parameter reads/writes.
- Telegram find, insert, erase, and size operations.
- Drive Function Interface commissioning, motor/encoder configuration, or DFI operations.
- Drive safety/security objects.
- Startdrive-specific download and upload configuration.
- PROFIdrive-specific engineering beyond the generic network fields listed in [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md).

## Static evidence

No drive operation appears in `TiaMcpServer/Batch/BatchOperationCatalog.cs` or the worker dispatch in `TiaMcpServer.OpennessWorker/Program.cs`. The public MCP tools and their limits are summarized in [README.md](README.md).
