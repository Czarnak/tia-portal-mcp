# TIA Portal Device Operations

## Scope

This file maps the TIA Portal device Openness area to the device functionality exposed by `tia-portal-mcp`.

## Exposed operations

| Public entry point | Operation | Inputs and behavior |
|---|---|---|
| `execute_read_batch` | `read_hardware_config` | Reads the project hardware configuration, including devices, device items, network interfaces, nodes, subnets, and IO-system information returned by the shared DTOs. |
| `execute_read_batch` | `search_equipment_catalog` | Required `query`; optional `maxResults` (default bounded by the worker). Searches the TIA hardware catalog and returns catalog type identifiers. |
| `preview_write_batch` → `apply_write_batch` | `add_network_device` | Required exact `typeIdentifier` and `deviceName`; optional `deviceItemName`. The type identifier must be a creatable catalog identifier. |
| `preview_write_batch` → `apply_write_batch` | `configure_network_device` | Required `deviceName`; optional `ipAddress`, `subnetMask`, `pnDeviceName`, `subnetName`, and `ioSystemName`. |
| `execute_read_batch` | `browse_project_tree` | Provides project-tree navigation for locating devices and other objects; optional `depth` and `startPath`. |

`add_network_device` and `configure_network_device` are data-write operations, so they must be previewed and applied as one ordered batch. The implementation validates the catalog identifier and device name before calling the worker.

## Device model exposed by the MCP

The hardware read path is an inspection surface. The write path is intentionally narrower:

- Device creation uses a catalog `typeIdentifier`, not an arbitrary Openness object constructor.
- Device configuration targets the named device and selected network identity fields.
- `deviceItemName` is supported during creation and defaults to `deviceName` when omitted.
- The returned hardware model can expose nested device items and network metadata, but the MCP does not expose generic attribute enumeration or arbitrary attribute writes.

## Not exposed

No public MCP operation was found for:

- Device groups, ungrouped-device management, or general device rename/delete.
- Plugging, moving, copying, deleting, or changing device-item/module types.
- Slot/subslot/module manipulation and hardware catalog creation beyond `add_network_device`.
- Generic device/device-item `GetAttributeInfos`, `GetAttributes`, or `SetAttributes` calls.
- Module-specific hardware parameters, IO addresses, diagnostic settings, channels, or hardware identifiers.
- Software-container access as a public device operation; PLC block/tag operations are exposed separately.

## Static evidence

- `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- `TiaMcpServer.OpennessWorker/Openness/EquipmentCatalogSearcher.cs`
- `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs`
- `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`
- `TiaMcpServer.OpennessWorker/Program.cs`
- `TiaMcpServer.Contracts/HardwareConfigInfo.cs`
