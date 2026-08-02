# SIMATIC Drives and Startdrive Operations

## Support status

The current MCP contract does not include a drive-specific operation. Generic hardware browsing, hardware reads, equipment-catalog search, network-device provisioning, and compile checks can operate on a project containing a drive, but they do not expose the drive object model.

## Current limits

The following Startdrive, SINAMICS, SIMATIC Drive Controller, and PROFIdrive areas are outside the current surface:

- Drive-object navigation and activation/type handling.
- SINAMICS parameter reads and writes.
- Telegram find, insert, erase, and size operations.
- Drive Function Interface commissioning, motor/encoder configuration, and DFI operations.
- Drive safety and security objects.
- Startdrive-specific download and upload configuration.
- PROFIdrive-specific engineering beyond the generic network fields in [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md).

For the generic device and network operations available around a drive, see [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) and [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md).
