# TIA Portal HMI Operations

## Scope

This summary records which Classic WinCC and WinCC Unified Openness capabilities are exposed by `tia-portal-mcp`.

## Exposed operations

No HMI-specific public MCP operation was found.

The generic project tree and hardware read operations can expose enough project structure to locate an HMI device, but they do not provide an HMI object-model handle or HMI-specific read/write operation. `compile_check` is available as a generic compile operation, but the MCP contract does not expose a separate HMI target selector or HMI composition API.

## Not exposed

The following HMI areas are not represented in the MCP batch catalog or worker dispatch:

- Classic WinCC `HmiTarget` initialization and compile-specific operations.
- WinCC screens, screen templates, popups, slide-ins, permanent areas, faceplates, and screen items.
- HMI tags and tag tables.
- Alarms, alarm classes, recipes, reports, data logs, and logging tags.
- VB scripts, cycles, connections, text lists, graphic lists, and themes.
- WinCC Unified `HmiSoftware`, screens, groups, widgets, parts, dynamization, UI events, features, logging, runtime settings, plant model, JavaScript modules, and system services.
- HMI import/export workflows.

## Static evidence

The public batch operation registry contains no HMI operation names, and the worker dispatch in `TiaMcpServer.OpennessWorker/Program.cs` contains no HMI handler. The available generic operations are documented in [README.md](README.md) and the project/device summaries.
