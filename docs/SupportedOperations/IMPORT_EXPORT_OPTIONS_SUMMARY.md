# TIA Portal MCP Import/Export Options

## Scope

`tia-portal-mcp` exposes a focused PLC round-trip surface rather than a generic proxy for every TIA Portal Openness `Import` and `Export` API.

Import/export behavior is available through four batch operations:

- `get_block_content`
- `update_block_logic`
- `get_type_content`
- `update_type_content`

Writes use the normal `preview_write_batch` → `apply_write_batch` safety flow. This summary describes implemented code paths. It is not a fresh live TIA Portal V21 certification.

## Implemented formats

### PLC blocks

- `format="xml"` is the default.
- XML reads return a bundle containing sanitized SimaticML and SIMATIC SD `.s7dcl`/`.s7res` documents.
- `format="source"` returns raw Siemens external-source text:
  - `.db` for global data blocks.
  - `.scl` for SCL-language FB, FC, and OB blocks.
- Instance DBs, array DBs, LAD, FBD, GRAPH, and STL are not supported through the external-source route; they must use XML where applicable.

### PLC data types

- `format="source"` is the default and returns `.udt` text.
- `format="xml"` returns SimaticML.

### Dependencies and updates

- `withDependencies=true` is supported on source-format reads.
- Dependency-inclusive source may declare several objects and is read-only context; it cannot be submitted as an update.
- Updates require an existing target and an exact match between the addressed object and the declaration in the submitted content.
- Imports do not create, rename, delete, or upsert objects.
- XML imports use `ImportOptions.Override`.
- External-source generation uses `GenerateBlockOption.None`.
- Root groups, user groups, and software-unit-owned external-source groups are resolved separately.

## Comparison with TIA Portal Openness

TIA Portal Openness supports a substantially broader import/export surface:

- XML/SimaticML for many PLC, HMI, and project objects.
- AML/CAx hardware exchange.
- XLSX workflows for project texts, PLC alarm text lists, and ProDiag data.
- PLC tag tables, tags, constants, technology objects, watch tables, and force tables.
- HMI screens, templates, pop-ups, slide-ins, permanent areas, tags, connections, cycles, scripts, and lists.
- Caller-selectable `ExportOptions`, `ImportOptions`, and PLC-specific `SWImportOptions`.

`tia-portal-mcp` currently implements only the PLC block and PLC data-type subset of that surface.

## Support matrix

| Option | Supported |
|---|---|
| PLC block SimaticML/XML export | yes |
| PLC block SimaticML/XML update/import | yes |
| PLC block SIMATIC SD `.s7dcl`/`.s7res` round-trip | partially |
| PLC block external-source `.db`/`.scl` exchange | partially |
| LAD/FBD/GRAPH/STL external-source exchange | no |
| PLC data type SimaticML/XML exchange | yes |
| PLC data type `.udt` external-source exchange | yes |
| Source export with dependency closure | yes |
| Import of dependency-inclusive multi-object source | no |
| Update an existing block or data type | yes |
| Create, rename, delete, or upsert through import | no |
| Root, user-group, and software-unit target routing | yes |
| Selectable Openness `ExportOptions` | partially |
| Selectable Openness `ImportOptions` | partially |
| `SWImportOptions` for structural changes or missing references | no |
| Know-how-protected block exchange | partially |
| Failsafe block exchange | partially |
| System-block exchange | no |
| PLC tag tables, tags, and constants via import/export | no |
| PLC technology objects | no |
| PLC watch and force tables | no |
| PLC alarm classes and alarm-text XLSX | no |
| ProDiag supervision XLSX | no |
| Hardware CAx/AutomationML exchange | no |
| Project-text XLSX and project graphics | no |
| HMI screens, templates, pop-ups, slide-ins, and permanent areas | no |
| HMI tags, connections, cycles, scripts, and text/graphic lists | no |
| Generic arbitrary-object XML import/export proxy | no |

## Partial-support explanations

- **SIMATIC SD documents:** Included as readable context and accepted through the controlled bundle importer, but not exposed as an unrestricted document-package API. SimaticML remains authoritative when present.
- **PLC block external source:** Limited to global DBs and SCL-language FB/FC/OB blocks.
- **`ExportOptions`:** XML export is fixed to `ExportOptions.None`; callers cannot request `WithDefaults` or `WithReadOnly`.
- **`ImportOptions`:** Imports are fixed to `Override`; callers cannot select fail-if-existing or culture activation/skipping behavior.
- **Know-how-protected blocks:** No `PlcBlockProtectionProvider` unlock/relock workflow is exposed. Generic export may return only the public interface or fail, and protected import is unavailable.
- **Failsafe blocks:** Compatible consistent F-blocks may pass through the generic XML route, but there is no dedicated F-block handling, F-system blocks are excluded, and the route is not separately certified.

## Main implementation files

- `TiaMcpServer.OpennessWorker/Openness/BlockExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/BlockImporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/PlcTypeExporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/PlcTypeImporter.cs`
- `TiaMcpServer.OpennessWorker/Openness/SourceFormatEligibility.cs`
