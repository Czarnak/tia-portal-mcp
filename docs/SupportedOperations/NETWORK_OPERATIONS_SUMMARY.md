# TIA Portal Network and Topology Operations

## Supported operations

The MCP provides a bounded device and network-identity surface:

| Entry point | Operation | Inputs and behavior |
|---|---|---|
| `network_read` | `read_hardware_config` | Reads devices and returned network DTOs, including interfaces, nodes, subnets, and IO systems where present. |
| `network_read` | `search_equipment_catalog` | Searches the hardware catalog for a device type before creation. |
| `network_write` | `add_network_device` | Creates a device from an exact catalog `typeIdentifier`; requires `deviceName` and accepts optional `deviceItemName`. |
| `network_write` | `configure_network_device` | Configures a named device with optional `ipAddress`, `subnetMask`, `pnDeviceName`, `subnetName`, and `ioSystemName`. |

`configure_network_device` is not a general network-editor proxy. Its writable contract is limited to the listed fields.

## Recommended workflow

1. Use `network_read` with `search_equipment_catalog` to obtain the exact catalog `typeIdentifier`.
2. Call `network_write` with the ordered `add_network_device` and, when required, `configure_network_device` operations and `confirm:false` (or omit `confirm`) to receive a preview and safety token.
3. Call the same `network_write` tool with `confirm:true`, the unchanged ordered operation list, and the returned token.
4. Use `network_read` with `read_hardware_config` after the write to inspect the resulting project configuration.

Network writes are sequential. Completed operations are not rolled back when a later item fails; later operations are skipped.

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

## Future roadmap

The approved high-level direction for dedicated network tools, agent-facing JSON contracts,
and expanded topology operations is documented in
[NETWORK_OPERATIONS_ROADMAP.md](../NETWORK_OPERATIONS_ROADMAP.md).
