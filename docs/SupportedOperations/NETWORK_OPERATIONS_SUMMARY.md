# TIA Portal Network and Topology Operations

## Supported operations

The MCP provides a bounded device and network-identity surface:

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `execute_read_batch` | `read_hardware_config` | Reads devices and returned network DTOs, including interfaces, nodes, subnets, and IO systems where present. |
| `execute_read_batch` | `search_equipment_catalog` | Searches the hardware catalog for a device type before creation. |
| `preview_write_batch` → `apply_write_batch` | `add_network_device` | Creates a device from an exact catalog `typeIdentifier`; requires `deviceName` and accepts optional `deviceItemName`. |
| `preview_write_batch` → `apply_write_batch` | `configure_network_device` | Configures a named device with optional `ipAddress`, `subnetMask`, `pnDeviceName`, `subnetName`, and `ioSystemName`. |

`configure_network_device` is not a general network-editor proxy. Its writable contract is limited to the listed fields.

## Recommended workflow

1. Use `search_equipment_catalog` to obtain the exact catalog `typeIdentifier`.
2. Preview an ordered write batch containing `add_network_device` and, when required, `configure_network_device`.
3. Apply the unchanged batch with `confirm=true` and the returned safety token.
4. Read `read_hardware_config` after the write to inspect the resulting project configuration.

Network writes follow the common sequential batch semantics; completed operations are not rolled back when a later item fails.

## Current limits

The current surface does not provide:

- First-class subnet creation, deletion, or editing.
- Node attributes beyond the device-configuration fields listed above.
- PROFINET IO-system or DP master-system attribute editing.
- Transfer-area creation or deletion.
- Address objects, process-image settings, channels, or address-controller services.
- IO connector timing, watchdog, RT class, sync role, send-clock, or isochronous settings.
- S7, FDL, ISO, ISO-on-TCP, TCP, UDP, PTP, or HMI communication-connection management.
- Online connection path selection, accessible-device discovery, gateways, or `ApplyConfiguration`.
- Generic network interface, node, subnet, or connection attribute enumeration and writes.

Hardware configuration results and compile results are engineering data. They do not by themselves certify a live hardware configuration or commissioning outcome.
