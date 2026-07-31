# TIA Portal Network and Topology Operations

## Scope

This area covers low-level subnet, node, IO-system, address, timing, communication-connection, and online-connection configuration. The MCP exposes only a bounded device/network identity subset.

## Exposed operations

| Public entry point | Operation | Inputs and behavior |
|---|---|---|
| `execute_read_batch` | `read_hardware_config` | Reads devices and the network-related DTOs returned by the hardware reader, including interfaces, nodes, subnets, and IO systems where present. |
| `execute_read_batch` | `search_equipment_catalog` | Searches the hardware catalog for a device type before creation. |
| `preview_write_batch` → `apply_write_batch` | `add_network_device` | Creates a device from an exact catalog `typeIdentifier`; requires `deviceName` and optionally accepts `deviceItemName`. |
| `preview_write_batch` → `apply_write_batch` | `configure_network_device` | Configures a named device with optional `ipAddress`, `subnetMask`, `pnDeviceName`, `subnetName`, and `ioSystemName`. |

The name `configure_network_device` should not be read as a generic network-editor proxy. Its contract is limited to the fields above.

## Not exposed

No public MCP operation was found for:

- Creating, deleting, or editing subnets as first-class objects.
- Node attributes beyond the bounded device-configuration inputs.
- PROFINET IO-system and DP master-system attribute editing.
- Transfer-area creation/deletion.
- Address objects, process-image settings, channels, or address-controller services.
- IO connector timing, watchdog, RT class, sync role, send-clock, or isochronous settings.
- S7, FDL, ISO, ISO-on-TCP, TCP, UDP, PTP, or HMI communication-connection management.
- Online connection path selection, accessible-device discovery, gateways, or `ApplyConfiguration`.
- Generic network interface, node, subnet, or connection attribute enumeration/write.

## Safety and verification

Network writes are data writes and require preview/apply confirmation. The repository guidance expects the exact catalog type identifier to be read first, then ordered add/configure operations to be previewed together. Hardware configuration and compile results should be read after applying a change; no live hardware acceptance is implied by static implementation evidence.

## Static evidence

- `TiaMcpServer.OpennessWorker/Openness/HardwareConfigReader.cs`
- `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceCreator.cs`
- `TiaMcpServer.OpennessWorker/Openness/NetworkDeviceConfigurator.cs`
- `TiaMcpServer.Contracts/HardwareConfigInfo.cs`
- `TiaMcpServer.Contracts/NetworkInterfaceInfo.cs`
- `TiaMcpServer.Contracts/NodeInfo.cs`
- `TiaMcpServer.Contracts/SubnetInfo.cs`
- `TiaMcpServer.Contracts/IoSystemInfo.cs`
