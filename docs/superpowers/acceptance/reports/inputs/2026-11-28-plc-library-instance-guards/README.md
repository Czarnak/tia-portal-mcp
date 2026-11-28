# PLC library-instance guard native inputs and selected evidence

Date: 2026-11-28. Product candidate code: `fe604c5788b0dd70808bf812c2effcfb21c40d9b`.
Report: [PLC library-instance guard verification](../../2026-11-28-plc-library-instance-guards-verification.md).

This directory retains the actual neutral UDT import document, exact source excerpts
from the executed native helper, and selected unchanged fields from four direct
Openness helper receipts. The JSON extracts are **not complete raw responses**.
They omit private absolute project/source paths and the ephemeral Portal process ID.
No MCP trace, preflight, restoration dump or timing output is retained here.

| File | Kind and verification | What it establishes |
| --- | --- | --- |
| [native-udt-neutral.xml](native-udt-neutral.xml) | Byte-exact input passed to `PlcTypeComposition.Import`; 1,576 bytes; SHA-256 in the provenance file. | The owned `_MCP_PR1_e706c4f1_Udt` fixture input. Culture metadata was removed before this document was executed; this archived document was not subsequently redacted or rewritten. |
| [prepare-fixture-xml.cs.txt](prepare-fixture-xml.cs.txt) | Byte-exact lines 124-151 of the executed helper source. | The constrained preparation of the neutral fixture: preserve structural members/start values, remove only approved comment/title culture metadata, and set the owned name. |
| [native-operations.cs.txt](native-operations.cs.txt) | Byte-exact lines 206-372 of the executed helper source. | Direct Openness target selection, exact PLC software-name matching, fixture transaction/import, linkage inspection, and guarded cleanup implementation. Source alone does not establish execution of every branch. |
| [native-create-udt-neutral.fields.json](native-create-udt-neutral.fields.json) | Selected fields from the successful direct-native `create_udt` receipt; retained values are unchanged. | The owned UDT existed as a plain object (`linked:false`) after the helper's transaction completed. This is fixture creation evidence, not a candidate `plc_write` guard result. |
| [native-fb-committed.fields.json](native-fb-committed.fields.json) | Selected fields from the direct-native `inspect` receipt; retained values are unchanged. | The owned FB was present and linked to the reported library version whose actual native state was `Committed`. |
| [native-fc-committed.fields.json](native-fc-committed.fields.json) | Selected fields from the direct-native `inspect` receipt; retained values are unchanged. | The owned nested FC was present and linked to the reported library version `0.0.1` whose actual native state was `Committed`. |
| [native-delete-udt.fields.json](native-delete-udt.fields.json) | Selected fields from the successful direct-native `delete_udt` receipt; retained values are unchanged. | Native deletion returned, its helper transaction completed, and the owned UDT was absent. This is native fixture cleanup, not a public `plc_write` type-delete operation. |
| [provenance.json](provenance.json) | Derived index of original hashes, exact source ranges, omitted fields and retained artifact hashes. | The extraction/byte-comparison boundary. It contains no private absolute paths. |

## Provenance and limits

The complete helper source used for these excerpts had SHA-256
`67e4050b6e1feee10e6665a2aa28109de14631ab98259c3a7d76ae7fb9792944`.
The excerpts preserve its original bytes and are not standalone compilable programs;
the omitted wrapper includes invocation parsing, environment paths and assembly setup.
No claim is made that the retained fragments hash to the complete executable.

The original neutral-creation receipt had SHA-256
`ce6dbd1825bf61babba0fc34784592abbc38ebd339a2ab2d5fc8a1d7409ba140`;
the original committed-FB receipt had SHA-256
`e610763920e7a1f1e19acb3c0c8cff29c34d96eda9a2813f03b62037680f7459`;
the original committed-FC receipt had SHA-256
`579c3737e230844b6f87b287214f1e928fe0de06dc15b981119e2fd816ccea47`;
the original UDT-deletion receipt had SHA-256
`7934c2f185fa085dca7fb62495e1a5cec848965f86f6f8c09eb576c07ebe5705`.
The selected-field files were parsed and reserialized as new documents. Their field
values are unchanged, but their bytes and scope differ from the originals.

`mutationAttempted`, `nativeReturned` and `transactionCommitted` are helper
instrumentation, not Siemens properties. In particular, `nativeReturned:false` on an
inspection means no native mutation call was made; the linkage service/version/state
getters were still read. The library version's `state:"Committed"` is distinct from
the helper's fixture-transaction marker.

The retained receipt fields cover neutral UDT creation, committed FB/FC inspection
and native UDT cleanup. The deletion receipt does not serialize its pre-delete library
version state; the report correlates it with the fixture's release workflow. Overall
fixture cleanup, baseline preservation, guarded candidate behavior and unexecuted
cases are assessed separately in the dated report. These selected receipts do not
constitute complete project or restoration snapshots.
