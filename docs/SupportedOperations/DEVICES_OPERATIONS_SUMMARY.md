# TIA Portal Device Operations

## Supported operations

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `execute_read_batch` | `read_hardware_config` | Reads devices, device items, network interfaces, nodes, subnets, and IO-system information represented by the hardware DTOs. Optional `deviceName` filter and opt-in structured I/O extraction (`includeIoDetails`, `includeTagMatches`) — see [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md). |
| `execute_read_batch` | `search_equipment_catalog` | Requires `query`; accepts bounded `maxResults`; returns catalog type identifiers for candidate devices. |
| `preview_write_batch` → `apply_write_batch` | `add_network_device` | Requires an exact catalog `typeIdentifier` and `deviceName`; accepts optional `deviceItemName`. |
| `preview_write_batch` → `apply_write_batch` | `configure_network_device` | Requires `deviceName`; accepts `ipAddress`, `subnetMask`, `pnDeviceName`, `subnetName`, and `ioSystemName`. |
| `browse_project_tree` | `browse_project_tree` | Locates devices and other project objects; accepts optional `projectPath`, `depth`, and `startPath`. |

`add_network_device` and `configure_network_device` are data writes. Preview the complete ordered sequence and apply it with the returned token. The catalog identifier and device name are validated before the worker performs the change.

## Device data model

The hardware read path is an inspection surface. The write path is limited to catalog-based device creation and selected device/network identity fields:

- Device creation uses a catalog `typeIdentifier`.
- `deviceItemName` applies to creation and defaults to `deviceName` when omitted.
- Hardware results can contain nested device items and network metadata.
- Generic attribute enumeration and arbitrary device or device-item attribute writes are not part of the MCP contract.

## Current limits

The current surface does not provide:

- Device groups, ungrouped-device management, or general device rename/delete.
- Plug, move, copy, delete, or device-item/module-type changes.
- Slot, subslot, and module manipulation beyond `add_network_device`.
- Generic `GetAttributeInfos`, `GetAttributes`, or `SetAttributes` calls.
- I/O-address, channel, or address-controller **writes**. Read-only I/O extraction (`ioDetails`
  addresses/channels and optional PLC tag matches) is available on `read_hardware_config` — see
  [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) — but module-specific hardware
  parameters, diagnostics data, and hardware identifiers remain unexposed.
- A device-level software-container API; PLC blocks, tags, and constants use the PLC operations described in [PLC_OPERATIONS_SUMMARY.md](PLC_OPERATIONS_SUMMARY.md).

## Related operations

Use [NETWORK_OPERATIONS_SUMMARY.md](NETWORK_OPERATIONS_SUMMARY.md) for network identity configuration and [README.md](README.md) for batch safety and response warnings.
