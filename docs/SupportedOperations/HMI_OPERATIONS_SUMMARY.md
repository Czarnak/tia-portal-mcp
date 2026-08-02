# TIA Portal HMI Operations

## Supported capabilities

The current MCP contract does not include an HMI-specific operation. Generic project-tree browsing and hardware reads can help locate an HMI device, but they do not return an HMI object-model handle or provide HMI object editing.

`compile_check` remains available for its generic PLC compile contract. It does not select an HMI target or compile HMI composition data.

## Current limits

The following Classic WinCC and WinCC Unified areas are outside the current MCP surface:

- Classic WinCC `HmiTarget` initialization and HMI-specific compile operations.
- Screens, screen templates, popups, slide-ins, permanent areas, faceplates, and screen items.
- HMI tags and tag tables.
- Alarms, alarm classes, recipes, reports, data logs, and logging tags.
- VB scripts, cycles, connections, text lists, graphic lists, and themes.
- WinCC Unified `HmiSoftware`, screens, groups, widgets, parts, dynamization, UI events, features, logging, runtime settings, plant model, JavaScript modules, and system services.
- HMI import and export workflows.

## Related generic operations

Use [README.md](README.md) for batch behavior and [DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) for the hardware information available when an HMI is part of the project configuration.
