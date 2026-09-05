# TIA Portal PLC Import and Export

## Operation surface

The MCP provides a focused PLC round-trip interface through four batch operations:

| Operation | Direction | Default format | Purpose |
|---|---|---|---|
| `get_block_content` | Read | `xml` | Reads one existing PLC block. |
| `update_block_logic` | Write | `xml` | Updates one existing PLC block from supplied document content. |
| `get_type_content` | Read | `source` | Reads one existing PLC data type. |
| `update_type_content` | Write | `source` | Updates one existing PLC data type from supplied document content. |

The `format` field accepts `xml` and `source`, case-insensitively. Block and type writes use the normal `preview_write_batch` → `apply_write_batch` workflow.

## Supported formats

### PLC blocks

- `format="xml"` is the default. Reads return a controlled bundle containing sanitized SimaticML and SIMATIC SD `.s7dcl`/`.s7res` documents.
- `format="source"` returns Siemens external-source text for global DBs (`.db`).
- Instance DBs, array DBs, FBs, FCs, OBs, LAD, FBD, GRAPH, and STL use the XML route; they are not eligible for the external-source route.

### PLC data types

- `format="source"` is the default and returns `.udt` text.
- `format="xml"` returns SimaticML.

## Update contract

Updates are strict updates to an existing object:

- The target path must resolve to an existing block or data type.
- The declaration in the submitted content must match the addressed object name.
- Import does not create, rename, delete, or upsert objects.
- XML block imports use `ImportOptions.Override`.
- External-source generation uses `GenerateBlockOption.None`.
- Root groups, user groups, and software-unit-owned external-source groups are resolved according to the addressed path.

For block updates, the worker validates the document, performs the import, compiles the affected scope, and checks the postcondition by re-exporting the block. External-source global DB updates compile the PLC because changing a DB declaration can affect dependent blocks.

### Preview evidence

`preview_write_batch` may include a response-only structured `diff` object for
`update_block_logic` and `update_type_content`. It compares already-bound exact-format current
text with the submitted replacement text; it does not predict Siemens post-write state and is
outside safety-token issuance and validation.

- Each eligible operation has at most 40 excerpt lines and 8,192 excerpt characters per side.
- When a changed span exceeds 40 lines, each excerpt contains the first 20 plus last 20 lines.
- Each displayed line is limited to 512 characters.
- The complete batch is limited to 320 excerpt lines and 32,768 excerpt characters in request
  order.
- The response reports raw SHA-256, raw character count, raw line count, `rawTextEqual`,
  `normalizedLinesEqual`, and `lineEndingOnly`; a line-ending-only difference has unequal raw text
  but equal normalized lines.
- Every other operation has `diff: null`.

## Format matrix

| Capability | Status |
|---|---|
| PLC block SimaticML/XML read and update | Supported |
| PLC block SIMATIC SD `.s7dcl`/`.s7res` bundle handling | Partial: supported as controlled bundle context |
| PLC block external-source `.db` exchange | Partial: global DBs |
| FB/FC/OB, LAD/FBD/GRAPH/STL external-source exchange | Not supported |
| PLC data type SimaticML/XML exchange | Supported |
| PLC data type `.udt` exchange | Supported |
| Existing-object update | Supported |
| Create, rename, delete, or upsert through import | Not supported |
| Root, user-group, and software-unit target routing | Supported |
| Caller-selected Openness `ExportOptions` | Partial: XML export uses `None` |
| Caller-selected Openness `ImportOptions` | Partial: imports use `Override` |
| `SWImportOptions` for structural changes or missing references | Not supported |
| Know-how-protected block exchange | Partial: generic export may expose only the public interface or fail; protected import is unavailable |
| Dedicated failsafe block exchange | Not supported; compatible consistent F-blocks may use the generic XML route |
| System-block exchange | Not supported |
| Tag tables, tags, and constants through import/export | Not supported; use PLC tag operations |
| Technology objects, watch tables, and force tables | Not supported |
| PLC alarm, ProDiag, hardware CAx, project-text, and graphics exchange | Not supported |
| HMI import/export | Not supported; see [HMI_OPERATIONS_SUMMARY.md](HMI_OPERATIONS_SUMMARY.md) |
| Generic arbitrary-object XML import/export | Not supported |

## Relationship to the Openness API

TIA Portal Openness supports additional PLC, HMI, project, hardware, alarm, and text exchange APIs. This MCP surface intentionally exposes only the PLC block and PLC data-type routes described above. It does not provide a generic proxy for arbitrary `Import` and `Export` calls.
