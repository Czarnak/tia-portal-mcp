# PLC read and standalone cross-references (PR A) validation — 2026-10-05

Result: **PASS with recorded limits.** Every `plc_read` operation and every `read_cross_references` target class that the project contains behaved as specified, in read-only mode against a large real project. The `target_kind_unsupported` path did not occur live, because every target this project offers exposes the cross-reference service. Offline tests cover that path.

Frozen candidate: `0d9a8b3` (branch `feature/plc-read-and-cross-references`), installed locally as `tia-mcp` `3.0.1-local.287.g0d9a8b3`. The package was built against the installed V21 assemblies and contains no Siemens DLLs.

## Offline qualification

- **At `b899614`, before the final-review fixes:**
  - The stub build had 0 warnings and 0 errors.
  - 5,247 tests passed, run serially.
  - Line coverage was 93.05%.
  - The reference stubs were current.
  - The real-assembly build was clean.
- **At `0d9a8b3`:** see the section "Offline re-run at the frozen candidate" below.
- **Reviews:**
  - Each task was reviewed for spec compliance and quality.
  - An independent whole-branch review found 3 Important and 6 Minor findings.
  - All of them were fixed in `5a4ff69..0d9a8b3`, and a scoped re-review addressed every one.

## Authorization and target

The maintainer authorized a read-only run against a copy of a large, real customer project that was already open in TIA Portal V21. The project's name and location are intentionally not recorded here.

- Binding used `bind_project` with the exact open path, giving a verified binding to one Portal process.
- Nothing was opened, saved, compiled or modified. The project's modified flag was `false` at binding.
- The server ran with the read-only tool set: `bind_project`, `browse_project_tree`, `get_project_status`, `network_read`, `plc_read` and `read_cross_references`. `execute_read_batch` was absent.

The project contains:

- two PLCs: a PC-based software controller with several hundred tags, safety (F) system blocks and many folders; and an S7-1500 with an almost empty program;
- about 85 hardware devices;
- a WinCC Runtime HMI that references PLC data.

## Results

| Check | Observed result |
| --- | --- |
| `list_tag_tables` with no PLC filter (every PLC) | 63,769 characters exceeds the 60,000 limit. The item is omitted whole, and the guidance names `plcName`, `folderPath` and `tableName`. |
| `list_tag_tables` for the large PLC | Still too large at 63,605 characters, so it is omitted. One PLC can exceed the limit, which shows the narrowing is needed. |
| `list_tag_tables` for the small PLC | Returns `isComplete:true` and one default table with `isDefault:true`. |
| `list_tag_tables` narrowed by `folderPath` | Returns both tables in that folder, each tag carrying `externalAccessible`, `externalVisible` and `externalWritable`. `isComplete` is true. |
| `list_tag_tables` with an unknown `tableName` | That item fails with `target_not_found`, "No tag table matched tableName …". |
| Mismatched per-item `projectPath` | Only that item fails, with `binding_conflict`. The other item in the same call still runs. |
| `get_block_content`, global DB, `source` | Content returned with `contentHash` `source:sha256:<hex>`. |
| `get_block_content`, same DB, `withDependencies` | The same text is returned with `contentHash: null`. |
| `get_block_content`, main OB, `xml` | 211,562 characters, so the result is omitted whole: no hash, no partial XML, and narrowing guidance. |
| `get_type_content`, PLC data type, default and `xml` | The default is `source` with `source:sha256:<hex>`. `xml` returns `xml:sha256:<hex>`. |
| `get_block_content` with a block path that leaves out `Blocks/` for a folder block | `worker_operation_failed` with the path-syntax message. This is legacy path syntax and behaviour, unchanged. |
| Cross-references, leaf global DB used by the PLC and the HMI | The source has 119 sources, 573 references and 814 locations, so it is trimmed inside, not emptied. One child subtree is kept, and a message reports "omitted 107 child sources, 522 references and 648 locations". `isComplete` is false and the narrowing guidance is given. |
| Cross-references, `Tag` member | Exactly one owner is queried. The result is complete and lists two SCL FB users with line and column locations. |
| Cross-references, system safety FB leaf | Complete: 32 locations, including `Call` and `Multiinstance` access names. |
| Cross-references, `SystemBlockFolder` sweep | 102 owners queried, all of them succeeded, so every system block exposes the service. `maxResults:2` cut the result, so `isComplete:false` with a message. |
| Cross-references, `PlcSoftware` sweep, `UnusedObjects` | 55 owners, all succeeded. When cut, the root warnings include "An incomplete UnusedObjects result is not proof that objects can be deleted." |
| Cross-references, `TagTable` container in mixed case | Resolved case-insensitively. The canonical spelling is echoed in `target`, and the sweep fans out over 2 tags. |
| Missing leaf | Typed failure `target_not_found`, with `isError` false and `success` false. |
| `member` on a path that does not end at a `TagTable` | Rejected with `invalid_selector`, `isError:true`. |
| Unknown `filter` | Rejected with `validation_error`, `isError:true`, listing the allowed values. |
| `referencedAs` | Returned as `{ name, typeName }`, for example HMI tag references give `typeName: "Tag"`. It is `null` where Openness reports no referenced object. `access` and `referenceType` values were all within the closed V21 name sets. |

## Deviations and observations

- **`target_kind_unsupported` was not exercised live.** No leaf in this project lacks the service. It is covered by `TargetKindUnsupportedIsTypedFailureNotEmptySuccess` and the resolver tests.
- **The legacy field `referencedAsName` is still emitted next to `referencedAs`.** It carries the call-site text when `referencedAs` is null, for example a library block version. It was kept unchanged.
- **The `maxResults` cut message is shared.** It also mentions "unused-object audit" for the other filters, and `omittedSourceCount` counts only budget omissions, not sources cut by `maxResults`. Both are pre-existing reader wording and accounting, recorded as follow-ups.
- **A single PLC's tag inventory can exceed 60,000 characters.** `folderPath`/`tableName` narrowing is the supported route, as recorded in spec §3.2 and §7.

## Offline re-run at the frozen candidate

- **Stub build:** 0 errors.
- **Full suite:** 5,273 passed, 0 failed, 0 skipped, run serially with coverage.
- **Coverage:** line rate 93.11%, against an 80% gate.
- **Reference stubs:** both artifacts are current.
- **Real-assembly build:** the Release build against the installed V21 assemblies ran inside `scripts/install-local-tool.ps1` and succeeded.
