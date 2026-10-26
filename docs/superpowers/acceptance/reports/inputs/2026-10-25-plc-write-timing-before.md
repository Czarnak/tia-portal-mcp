# plc_write call timing, before the native-rejection refactor

Baseline for Task 6 of the plc_write native-rejections plan. Task 6 repeats this exact protocol on the refactored build.

## Build under test

| Item | Value |
| --- | --- |
| Source | `main` at `02ffe5a` (the branch base) |
| Installed tool version | `4.2.0-local.33.g02ffe5a` |
| Access mode | `full` (audit `accessMode:"full"` on every record) |
| Client | Claude Code (MCP Inspector not used; no Inspector wall-clock column, audit figures only) |
| Date | 2026-10-25, maintainer machine, TIA Portal V21 |
| Project | `<disposable-copy>` (bound with `bind_project`, never saved, never closed) |

## Project size

Counts are read from `browse_project_tree` and `plc_read list_tag_tables`; folder counts are direct children only.

| PLC | Station | Program-blocks folder direct children | Tag tables | Tags (user) | User constants |
| --- | --- | --- | --- | --- | --- |
| `PLC_1` (S7-1500, measured) | Station_1 | 1 (`Main` OB1) | 2 (`Default tag table`, `TableWith/Slash`) | 2 | 0 |
| `PLC_2` | Station_2 | 11 | 6 | not read | not read |
| `PLC_3` | Station_3 | 11 | 6 | not read | not read |
| `PLC_4/SlashTest` | Station_4 | 1 | 1 | not read | not read |

Totals: 4 PLC software objects, 111 top-level tree nodes (about 100 devices). PLC_1 has 1 block, 2 tables, 2 tags. Tag totals for the other three PLCs were not read (would flood the response budget); the measured PLC is the small one, so tag-table lookups are cheap and the fixed cost below is not a large-table effect.

## Protocol

- All calls target `PLC_1`. Fixed names, identical in every run: tag table `ZZ_T0_Tbl`, tags `ZZ_T0_Tag1..5`, constants `ZZ_T0_Const1..2`, block group `ZZ_T0_Grp`, block `ZZ_T0_FC1` (FC, LAD), T1 tag `ZZ_T0_T1Tag` in `Default tag table`.
- Per run, strictly sequential (never parallel): T5 -> T1 -> T2 -> `plc_read` of the T2 block (untimed, for T3's `expectedContentHash`) -> T3 -> T4 -> cleanup (untimed). Nothing exists when T5 runs, so main's planning-time collision block does not fire.
- One discarded warm-up run first (first call after the session started), then three measured runs. Medians are over the three measured runs.
- Metric source: `%LOCALAPPDATA%\TiaMcpServer\audit\writes-2026-10-25.jsonl`, audit v2. Root `durationMs` = whole guarded call; item sum = sum of `items[].durationMs`; remainder = root minus item sum (planning, verification, audit, binding lease). Both fields exist in every record. Dry runs have `items[].durationMs:null` (status `skipped`), so item sum is 0 and remainder = root.
- The audit `timestamp` is the call start (consecutive records line up as start + root duration), which confirms the calls ran serialized.
- The project is returned to its starting contents after every run (cleanup deletes the T1 tag and `ZZ_T0_Tbl`; T4 deletes the group and block).

## Median table (3 measured runs, milliseconds)

| Case | Call | Root | Item sum | Remainder (planning + verification) |
| --- | --- | --- | --- | --- |
| T1 | 1 `create_tag` | 17,826 | 5,201 | 12,673 |
| T2 | 10 items (table, 5 tags, 2 constants, group, block) | 137,669 | 124,185 | 13,484 |
| T3 | 1 `update_block_logic` | 54,933 | 37,318 | 18,356 |
| T4 | 1 `delete_block_group` (holds T2 block) | 18,814 | 5,325 | 13,718 |
| T5 | T2 with `dryRun:true` | 5,975 | 0 | 5,975 |

Each median is taken per column independently over the three measured runs, so root, item sum and remainder in a row need not come from the same run (T3 root 54,933 is run 2; its item-sum median 37,318 is run 1).

## Per-run figures (root / item sum / remainder, ms)

| Case | Warm-up (discarded) | Run 1 | Run 2 | Run 3 |
| --- | --- | --- | --- | --- |
| T1 | 26,083 / 8,550 / 17,533 | 17,398 / 4,725 / 12,673 | 17,826 / 5,201 / 12,625 | 21,310 / 6,643 / 14,667 |
| T2 | 188,099 / 171,982 / 16,117 | 137,669 / 124,185 / 13,484 | 128,999 / 116,133 / 12,866 | 146,154 / 131,443 / 14,711 |
| T3 | 54,541 / 37,969 / 16,572 | 54,698 / 37,318 / 17,380 | 54,933 / 36,577 / 18,356 | 60,113 / 40,905 / 19,208 |
| T4 | 17,180 / 5,031 / 12,149 | 18,814 / 5,096 / 13,718 | 17,901 / 5,325 / 12,576 | 21,154 / 6,300 / 14,854 |
| T5 | 9,147 / 0 / 9,147 | 5,972 / 0 / 5,972 | 5,975 / 0 / 5,975 | 9,235 / 0 / 9,235 |
| Cleanup (2 items, untimed for the protocol) | 23,068 / 10,800 / 12,268 | 23,056 / 10,844 / 12,212 | 24,740 / 10,984 / 13,756 | 25,033 / 11,997 / 13,036 |

Spread: run 3 is about 10-20 percent slower than runs 1-2 on every case and T5 jumped from about 6.0 s to 9.2 s, so the machine was noisier. The medians absorb it; Task 6 should compare medians and expect a similar spread.

## Replay payloads (verbatim)

`bind_project` with no arguments first. Tool paths: `PLC_1/Blocks/<Group>/<Block>`; the bare `PLC_1/<Group>/<Block>` form is rejected by validation.

### T5 (dryRun of the T2 payload) and T2

T5 is `plc_write` with `"dryRun": true` and the operations below; T2 is the same operations without `dryRun`.

```json
[
  {"operationId": "t2-01", "operation": "create_tag_table", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl"},
  {"operationId": "t2-02", "operation": "create_tag", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Tag1", "dataType": "Bool"},
  {"operationId": "t2-03", "operation": "create_tag", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Tag2", "dataType": "Int"},
  {"operationId": "t2-04", "operation": "create_tag", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Tag3", "dataType": "Word"},
  {"operationId": "t2-05", "operation": "create_tag", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Tag4", "dataType": "Real"},
  {"operationId": "t2-06", "operation": "create_tag", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Tag5", "dataType": "DInt"},
  {"operationId": "t2-07", "operation": "create_user_constant", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Const1", "dataType": "Int", "value": "1"},
  {"operationId": "t2-08", "operation": "create_user_constant", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl", "name": "ZZ_T0_Const2", "dataType": "Int", "value": "2"},
  {"operationId": "t2-09", "operation": "create_block_group", "blockPath": "PLC_1/Blocks/ZZ_T0_Grp"},
  {"operationId": "t2-10", "operation": "create_block", "blockPath": "PLC_1/Blocks/ZZ_T0_Grp/ZZ_T0_FC1", "blockType": "FC", "language": "LAD"}
]
```

### T1

```json
[{"operationId": "t1-01", "operation": "create_tag", "plcName": "PLC_1", "tableName": "Default tag table", "name": "ZZ_T0_T1Tag", "dataType": "Bool"}]
```

### T3 pre-read (untimed) and T3

Pre-read: `plc_read` with `[{"operationId":"r1","operation":"get_block_content","blockPath":"PLC_1/Blocks/ZZ_T0_Grp/ZZ_T0_FC1","format":"xml"}]`. On a freshly created `ZZ_T0_FC1` the returned `contentHash` was identical in all four runs: `xml:sha256:8ab52978f6dc961a97f09c1b25baefa87bae27dfb31ab15e85fc1a662456fe9d`. The content is a two-file bundle (`--- FILE: ZZ_T0_FC1.xml ---` then `--- FILE: ZZ_T0_FC1.s7dcl ---`). T3 sends that bundle back with one line changed: the first (`en-GB`) Title `<Text />` becomes `<Text>T0 baseline</Text>`. Always re-read first in case the hash differs. The content string below is JSON-escaped (`\r\n` inside the XML and the `.s7dcl` part, a bare `\n` between the two file parts), exactly as sent.

```json
[{"operationId": "t3-01", "operation": "update_block_logic", "blockPath": "PLC_1/Blocks/ZZ_T0_Grp/ZZ_T0_FC1", "format": "xml", "expectedContentHash": "xml:sha256:8ab52978f6dc961a97f09c1b25baefa87bae27dfb31ab15e85fc1a662456fe9d", "content": "--- FILE: ZZ_T0_FC1.xml ---\n<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<Document>\r\n  <Engineering version=\"V21\" />\r\n  <SW.Blocks.FC ID=\"0\">\r\n    <AttributeList>\r\n      <Interface>\r\n        <Sections xmlns=\"http://www.siemens.com/automation/Openness/SW/Interface/v5\">\r\n          <Section Name=\"Input\" />\r\n          <Section Name=\"Output\" />\r\n          <Section Name=\"InOut\" />\r\n          <Section Name=\"Temp\" />\r\n          <Section Name=\"Constant\" />\r\n          <Section Name=\"Return\">\r\n            <Member Name=\"Ret_Val\" Datatype=\"Void\" />\r\n          </Section>\r\n        </Sections>\r\n      </Interface>\r\n      <MemoryLayout>Optimized</MemoryLayout>\r\n      <Name>ZZ_T0_FC1</Name>\r\n      <Namespace />\r\n      <Number>1</Number>\r\n      <ProgrammingLanguage>LAD</ProgrammingLanguage>\r\n      <SetENOAutomatically>false</SetENOAutomatically>\r\n    </AttributeList>\r\n    <ObjectList>\r\n      <MultilingualText ID=\"1\" CompositionName=\"Comment\">\r\n        <ObjectList>\r\n          <MultilingualTextItem ID=\"2\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>en-GB</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n          <MultilingualTextItem ID=\"3\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>es-ES</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n          <MultilingualTextItem ID=\"4\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>fr-FR</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n          <MultilingualTextItem ID=\"5\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>pl-PL</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n        </ObjectList>\r\n      </MultilingualText>\r\n      <MultilingualText ID=\"6\" CompositionName=\"Title\">\r\n        <ObjectList>\r\n          <MultilingualTextItem ID=\"7\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>en-GB</Culture>\r\n              <Text>T0 baseline</Text>\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n          <MultilingualTextItem ID=\"8\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>es-ES</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n          <MultilingualTextItem ID=\"9\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>fr-FR</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n          <MultilingualTextItem ID=\"A\" CompositionName=\"Items\">\r\n            <AttributeList>\r\n              <Culture>pl-PL</Culture>\r\n              <Text />\r\n            </AttributeList>\r\n          </MultilingualTextItem>\r\n        </ObjectList>\r\n      </MultilingualText>\r\n    </ObjectList>\r\n  </SW.Blocks.FC>\r\n</Document>\n--- FILE: ZZ_T0_FC1.s7dcl ---\n{\r\n    S7_Optimized := \"TRUE\";\r\n    S7_PreferredLanguage := \"LAD\";\r\n    S7_Version := \"0.1\"\r\n}\r\nFUNCTION \"ZZ_T0_FC1\" : Void\r\nEND_FUNCTION\r\n"}]
```

T3 applies the change and runs a block-scope compile (`importOutcome.compileStage:"succeeded"`), which is why its item time is large.

### T4

```json
[{"operationId": "t4-01", "operation": "delete_block_group", "blockPath": "PLC_1/Blocks/ZZ_T0_Grp"}]
```

### Cleanup (untimed)

```json
[
  {"operationId": "c-01", "operation": "delete_tag", "plcName": "PLC_1", "tableName": "Default tag table", "name": "ZZ_T0_T1Tag"},
  {"operationId": "c-02", "operation": "delete_tag_table", "plcName": "PLC_1", "tableName": "ZZ_T0_Tbl"}
]
```

All 24 calls (warm-up plus three runs, six audited `plc_write` calls each) succeeded first time: T1/T2/T3/T4/cleanup `phase:"applied"`, `success:true`, verification true; T5 `phase:"preview"`. The `plc_read` calls are not audited.

## Trimmed raw audit records

One JSON object per `plc_write` call, in execution order. `call` is `<run>/<case>` (`W` = discarded warm-up run, `C` = cleanup). Kept: timestamp, tool, accessMode, phase, root `durationMs`, and per item operationId, operation, status and `durationMs`. Dropped: project path, binding, response text and hash, preconditions, targets.

```jsonl
{"call":"W/T5","timestamp":"2026-10-25T23:32:49.6018975+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":9147,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"W/T1","timestamp":"2026-10-25T23:33:05.8404061+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":26083,"items":[{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":8550}]}
{"call":"W/T2","timestamp":"2026-10-25T23:33:37.3964651+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":188099,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":6864},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":15489},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":16898},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":16304},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":15848},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":15847},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":17921},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":16768},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":7804},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":42239}]}
{"call":"W/T3","timestamp":"2026-10-25T23:37:03.7324158+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":54541,"items":[{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":37969}]}
{"call":"W/T4","timestamp":"2026-10-25T23:38:00.5484806+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17180,"items":[{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":5031}]}
{"call":"W/C","timestamp":"2026-10-25T23:38:21.0639444+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":23068,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5485},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5315}]}
{"call":"1/T5","timestamp":"2026-10-25T23:38:50.3699215+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":5972,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"1/T1","timestamp":"2026-10-25T23:38:58.7896064+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17398,"items":[{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":4725}]}
{"call":"1/T2","timestamp":"2026-10-25T23:39:21.8117976+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":137669,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5276},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":12689},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":14292},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":12940},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":12751},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":12897},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":12348},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":12789},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":5670},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":22533}]}
{"call":"1/T3","timestamp":"2026-10-25T23:41:56.6920878+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":54698,"items":[{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":37318}]}
{"call":"1/T4","timestamp":"2026-10-25T23:42:53.8786469+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":18814,"items":[{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":5096}]}
{"call":"1/C","timestamp":"2026-10-25T23:43:14.9837921+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":23056,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5146},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5698}]}
{"call":"2/T5","timestamp":"2026-10-25T23:43:44.9116777+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":5975,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"2/T1","timestamp":"2026-10-25T23:43:50.8994852+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17826,"items":[{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":5201}]}
{"call":"2/T2","timestamp":"2026-10-25T23:44:14.5757278+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":128999,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5147},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":11633},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":11913},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":11729},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":11343},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":12625},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":11793},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":12073},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":5111},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":22766}]}
{"call":"2/T3","timestamp":"2026-10-25T23:46:42.9498622+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":54933,"items":[{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":36577}]}
{"call":"2/T4","timestamp":"2026-10-25T23:47:40.2527153+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17901,"items":[{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":5325}]}
{"call":"2/C","timestamp":"2026-10-25T23:48:00.506558+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":24740,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5443},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5541}]}
{"call":"3/T5","timestamp":"2026-10-25T23:48:30.7493784+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":9235,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"3/T1","timestamp":"2026-10-25T23:48:42.3058034+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":21310,"items":[{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":6643}]}
{"call":"3/T2","timestamp":"2026-10-25T23:49:08.7285232+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":146154,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5061},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":13204},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":14069},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":13136},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":13156},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":13068},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":12755},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":14115},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":6987},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":25892}]}
{"call":"3/T3","timestamp":"2026-10-25T23:51:54.605821+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":60113,"items":[{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":40905}]}
{"call":"3/T4","timestamp":"2026-10-25T23:52:56.8057235+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":21154,"items":[{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":6300}]}
{"call":"3/C","timestamp":"2026-10-25T23:53:20.4158016+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":25033,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5787},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":6210}]}
```
