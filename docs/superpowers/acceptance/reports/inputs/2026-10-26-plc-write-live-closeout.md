# plc_write live closeout: L7 re-run, hash probe, L1 regression, timing Set C (2026-10-26)

Task 6c evidence on build `4.2.0-local.48.g7445c5f` (commit `7445c5f`), `--access-mode full`, `contractVersion:"2.0"` confirmed on the first plc_write, PLC `PLC_1` of `<disposable-copy>` (bound with `bind_project`, never saved or closed). Portal UI language English. Four parts, in the order they ran: L7 re-run, hash probe, L1 regression, then the full timing protocol as Set C. Captures are consolidated verbatim from the session workspace; `engineeringError` and failure objects are untouched.

## Part 1: L7 re-run (colliding update_tag rename)

Setup: table `ZZ_T6C_Tbl`, `ZZ_T6C_TagA` Int, `ZZ_T6C_TagB` Bool. Verdict: PASS.

```json
{
 "_note": "Task 6c L7 re-run. Build 4.2.0-local.48.g7445c5f, full mode, contractVersion 2.0 confirmed on first plc_write. PLC_1 of <disposable-copy>. Setup: table ZZ_T6C_Tbl, ZZ_T6C_TagA Int, ZZ_T6C_TagB Bool (3 items applied, trusted).",
 "call_1_collision_rename_plus_datatype": {
  "request": [{"operationId":"l7-01","operation":"update_tag","plcName":"PLC_1","tableName":"ZZ_T6C_Tbl","name":"ZZ_T6C_TagA","newName":"ZZ_T6C_TagB","dataType":"Real"}],
  "response_trimmed": {
   "batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operations":[{"operationId":"l7-01","operation":"update_tag","status":"failed","result":null,"failure":{"category":"postcondition_failed","message":"update_tag: Portal did not rename tag 'ZZ_T6C_TagA' to 'ZZ_T6C_TagB'; the tag is still named 'ZZ_T6C_TagA'. Another object may already hold that name. No other property was changed."}}]},
   "contractVersion":"2.0",
   "effects":[{"operationId":"l7-01","effect":{"operation":"update_tag","changes":[{"field":"name","current":"ZZ_T6C_TagA","requested":"ZZ_T6C_TagB"},{"field":"dataType","current":"Int","requested":"Real"}]}}],
   "error":null,"guards":[],"phase":"applied","success":false,"tool":"plc_write",
   "verification":{"operations":[],"success":true},"warnings":[]},
  "isError_observed": false
 },
 "plc_read_after_call_1": {"tags":[{"name":"ZZ_T6C_TagA","dataType":"Int"},{"name":"ZZ_T6C_TagB","dataType":"Bool"}],"verdict":"A still exists with ORIGINAL type Int (not Real), B unchanged (Bool). PASS"},
 "positive_control": {
  "request": [{"operationId":"l7-ctl","operation":"update_tag","plcName":"PLC_1","tableName":"ZZ_T6C_Tbl","name":"ZZ_T6C_TagA","newName":"ZZ_T6C_TagC"}],
  "response_trimmed": {"status":"succeeded","phase":"applied","success":true,"error":null,"contractVersion":"2.0","result.tagName":"ZZ_T6C_TagC","verification":[{"check":"trusted","success":true}]},
  "plc_read_after": {"tags":[{"name":"ZZ_T6C_TagC","dataType":"Int"},{"name":"ZZ_T6C_TagB","dataType":"Bool"}],"note":"read taken after the L1 call (L1 changed nothing); rename applied and type preserved"}
 },
 "cleanup": "delete_tag_table ZZ_T6C_Tbl applied (removed 2 tags, info guard plc_deletes_table_contents)",
 "verdict": "PASS"
}
```

## Part 2: contentHash probe



Build 4.2.0-local.48.g7445c5f, full mode, PLC_1 of `<disposable-copy>`. Block `PLC_1/Blocks/ZZ_T6C_Grp/ZZ_T6C_FC1` (FC, LAD, freshly created, format xml). All calls sequential.

### Hashes

| # | Call | Hash |
| --- | --- | --- |
| H0 | plc_read r1 (pre-write) | xml:sha256:8603bd614d48fa9490edb12b6ba2700625b587a28a53a2e10bb11b024ec60de9 |
| V1 | plc_write h-03 update_block_logic, content = r1 content unchanged, expectedContentHash = H0: applied, verification.check `contentHash`, success true, `contentHash` and `actual` | xml:sha256:8603bd614d48fa9490edb12b6ba2700625b587a28a53a2e10bb11b024ec60de9 |
| R1 | plc_read r2 (post-write) | xml:sha256:8603bd614d48fa9490edb12b6ba2700625b587a28a53a2e10bb11b024ec60de9 |
| R2 | plc_read r3 (post-write) | xml:sha256:8603bd614d48fa9490edb12b6ba2700625b587a28a53a2e10bb11b024ec60de9 |
| V2 | plc_write h-04 update_block_logic, expectedContentHash = V1 (verification hash), Title en-GB text changed to `T6C hash probe`: applied, not state_changed; verification `contentHash` and `actual` | xml:sha256:60900034f35b35d43d962e06bd472cd5d0ff8dbfcc94d1b22515c4beb6722b7f |
| R3 | plc_read r4 (after h-04) | xml:sha256:60900034f35b35d43d962e06bd472cd5d0ff8dbfcc94d1b22515c4beb6722b7f |

Other values seen: h-04 `effects.contentDiff.requested.sha256` (hash of the request text, not the block) 2f38dc943335a9c525f3bbdb0983ec164e31335a44025c11d799652a555cb089; `contentDiff.current.sha256` of h-03/h-04 (the pre-write current) 8603bd61...0de9.

### Answers

- Are the two post-write reads equal? Yes (R1 == R2 == H0), for the unchanged write-back.
- Does the verification contentHash equal the post-write read hash? Yes, in both writes: V1 == R1/R2 and V2 == R3 (V2 is a real content change, which also adds `.s7res` and `S7_BlockTitle` to the exported bundle; the read still matches the verification hash exactly).
- Is the verification hash usable as the next expectedContentHash? Yes. h-04 used V1 as expectedContentHash and was applied (not state_changed). A second chain step from V2 was not exercised (the block was then deleted), but R3 == V2 shows V2 would be accepted.
- Observation: the unchanged write-back left the block hash unchanged (H0 == V1) and `contentDiff.rawTextEqual:true`, `requestedChangedLineCount:0`; `contentRelation:"unknown"` in importOutcome.

### Verdict

PASS. Post-write reads are stable and the verification contentHash equals the post-write read hash, so it is usable directly as the next expectedContentHash.

Cleanup: `delete_block_group` ZZ_T6C_Grp applied (removed the 1 block).

### Addendum from the timing block (T3 updates)

Four identical `update_block_logic` writes of the same content (T3, warm-up and runs 1-3, each on a freshly created `ZZ_T0_FC1`, same `expectedContentHash` xml:sha256:8ab52978...6fe9d) returned four different verification contentHashes: c18c1dee...5259, 518bc995...92d9, bd85dee9...f255, cd027407...be7e. The exported bundle gains a generated `S7_BlockTitle := "MLC_xxx"` id and a `.s7res` part when a Title is set (visible in the h-04 read), and the id differs per write. So the verification hash is a hash of the block as exported after that write (equal to the next read, shown for h-03 and h-04), not a function of the submitted content. Callers must take the hash from the verification or a fresh read, never precompute it. The T3 hashes were not re-read afterwards (blocks deleted by T4).

## Part 3: L1 regression (create_tag with an existing name)

Verdict: PASS, identical to the 6a capture in the live-cases input.

```json
{
 "_note": "Task 6c L1 regression. Run while ZZ_T6C_Tbl held ZZ_T6C_TagC (Int) and ZZ_T6C_TagB (Bool).",
 "request": [{"operationId":"l1-01","operation":"create_tag","plcName":"PLC_1","tableName":"ZZ_T6C_Tbl","name":"ZZ_T6C_TagB","dataType":"Bool"}],
 "response_trimmed": {
  "batch":{"counts":{"failed":1,"omitted":0,"skipped":0,"succeeded":0},"operations":[{"operationId":"l1-01","operation":"create_tag","status":"failed","result":null,"failure":{"category":"engineering_rejected","engineeringError":{"inner":[],"type":"EngineeringTargetInvocationException","message":"Error when calling method 'Create' of type 'Siemens.Engineering.SW.Tags.PlcTagComposition'.\r\n\r\nAn attempt was made to create an object that already exists."}}}]},
  "contractVersion":"2.0","error":null,"guards":[],"phase":"applied","success":false,"tool":"plc_write","verification":{"operations":[],"success":true},"warnings":[]},
 "plc_read_after": {"tags":[{"name":"ZZ_T6C_TagC","dataType":"Int"},{"name":"ZZ_T6C_TagB","dataType":"Bool"}],"note":"each name listed once, project unchanged"},
 "verdict": "PASS (identical to 6a L01.json)"
}
```

## Part 4: timing Set C



Build 4.2.0-local.48.g7445c5f, `--access-mode full`, PLC_1 of `<disposable-copy>`, 2026-10-26. Protocol of `docs/superpowers/acceptance/reports/inputs/2026-10-25-plc-write-timing-before.md` replayed verbatim (same ZZ_T0_ names, order, payloads, cleanup): one discarded warm-up, three measured runs, strictly sequential calls. Figures from `%LOCALAPPDATA%\TiaMcpServer\audit\writes-2026-10-26.jsonl` (audit v2): root = `durationMs`, item sum = sum of `items[].durationMs`, remainder = root minus item sum. All 24 timing calls: applied (or preview for T5), success true, first time.

Deviations from the protocol: the untimed T3 pre-read (`plc_read`) was done once, in the warm-up; the hash was identical to the baseline (`xml:sha256:8ab52978...6fe9d`), runs 1-3 reused it (a stale hash would have returned `state_changed`; none did). The L7/hash-probe/L1 calls ran immediately before the timing block on the same worker (the worker was therefore already warm, so the warm-up was not a cold start).

### Set C median table (ms, medians of runs 1-3, per column)

| Case | Root | Item sum | Remainder |
| --- | --- | --- | --- |
| T1 | 11 442 | 5 178 | 6 264 |
| T2 | 119 732 | 112 428 | 6 437 |
| T3 | 55 378 | 38 335 | 17 073 |
| T4 | 12 017 | 5 472 | 6 545 |
| T5 | 6 308 | 0 | 6 308 |

### Set C per-run figures (root / item sum / remainder, ms)

| Case | Warm-up (discarded) | Run 1 | Run 2 | Run 3 |
| --- | --- | --- | --- | --- |
| T1 | 13 126 / 6 068 / 7 058 | 11 241 / 5 155 / 6 086 | 11 442 / 5 178 / 6 264 | 11 574 / 5 221 / 6 353 |
| T2 | 133 445 / 126 162 / 7 283 | 119 732 / 111 901 / 7 831 | 118 685 / 112 428 / 6 257 | 130 967 / 124 530 / 6 437 |
| T3 | 58 980 / 42 061 / 16 919 | 55 041 / 37 968 / 17 073 | 55 378 / 38 335 / 17 043 | 64 544 / 44 731 / 19 813 |
| T4 | 11 472 / 5 228 / 6 244 | 12 017 / 5 472 / 6 545 | 11 996 / 5 465 / 6 531 | 14 459 / 7 054 / 7 405 |
| T5 | 7 115 / 0 / 7 115 | 6 122 / 0 / 6 122 | 6 308 / 0 / 6 308 | 6 327 / 0 / 6 327 |
| C | 16 935 / 10 944 / 5 991 | 17 425 / 11 094 / 6 331 | 17 380 / 11 148 / 6 232 | 17 291 / 11 105 / 6 186 |

### Comparison: before / Set A / Set B / Set C (medians, ms; percent is vs before)

Before = `4.2.0-local.33.g02ffe5a`; Set A and Set B = `4.2.0-local.44.g7c9cf92` (6a, Set A noisy machine, Set B quiet replicate); Set C = `4.2.0-local.48.g7445c5f`.

#### Root

| Case | Before | Set A | Set B | Set C | C vs before | C vs B |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | 17 826 | 21 219 | 13 212 | 11 442 | -36% | -13% |
| T2 | 137 669 | 185 284 | 128 087 | 119 732 | -13% | -7% |
| T3 | 54 933 | 73 383 | 63 814 | 55 378 | +1% | -13% |
| T4 | 18 814 | 19 336 | 13 516 | 12 017 | -36% | -11% |
| T5 | 5 975 | 11 490 | 6 151 | 6 308 | +6% | +3% |

#### Item sum

| Case | Before | Set A | Set B | Set C | C vs before | C vs B |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | 5 201 | 9 877 | 6 066 | 5 178 | 0% | -15% |
| T2 | 124 185 | 173 932 | 120 818 | 112 428 | -9% | -7% |
| T3 | 37 318 | 48 464 | 43 407 | 38 335 | +3% | -12% |
| T4 | 5 325 | 8 116 | 6 197 | 5 472 | +3% | -12% |
| T5 | 0 | 0 | 0 | 0 | n/a | n/a |

#### Remainder

| Case | Before | Set A | Set B | Set C | C vs before | C vs B |
| --- | --- | --- | --- | --- | --- | --- |
| T1 | 12 673 | 11 342 | 7 312 | 6 264 | -51% | -14% |
| T2 | 13 484 | 11 132 | 7 730 | 6 437 | -52% | -17% |
| T3 | 18 356 | 22 220 | 21 166 | 17 073 | -7% | -19% |
| T4 | 13 718 | 11 220 | 8 069 | 6 545 | -52% | -19% |
| T5 | 5 975 | 11 490 | 6 151 | 6 308 | +6% | +3% |

### Noise observations

- T5 dryRun (planning only, no Openness mutation) per run: W=7 115, 1=6 122, 2=6 308, 3=6 327 ms; before median 5,975 ms (runs 6.0 / 6.0 / 9.2 s), Set B 6,151 ms. Set C T5 is flat and in line with before and Set B; no sign of the Set A slowdown (11.5 s).
- Run spread (max/min of root) T1 1.03, T2 1.10, T3 1.17, T4 1.21. Run 3 is the slowest on T2/T3/T4 (T3 64.5 s vs 55.0 / 55.4 s).

### Trimmed audit records (one JSON object per plc_write call; project path, binding, response text and hashes dropped)

```jsonl
{"call":"W/T5","timestamp":"2026-10-26T18:38:30.5074946+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":7115,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"W/T1","timestamp":"2026-10-26T18:38:40.5112296+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":13126,"items":{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":6068}}
{"call":"W/T2","timestamp":"2026-10-26T18:38:59.0730807+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":133445,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5946},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":13400},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":13339},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":13390},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":13333},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":13386},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":13466},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":13438},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":6384},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":20080}]}
{"call":"W/T3","timestamp":"2026-10-26T18:41:32.6271677+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":58980,"items":{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":42061}}
{"call":"W/T4","timestamp":"2026-10-26T18:42:35.4363619+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":11472,"items":{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":5228}}
{"call":"W/C","timestamp":"2026-10-26T18:42:49.7759016+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":16935,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5288},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5656}]}
{"call":"1/T5","timestamp":"2026-10-26T18:43:15.0373411+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":6122,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"1/T1","timestamp":"2026-10-26T18:43:24.5201387+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":11241,"items":{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":5155}}
{"call":"1/T2","timestamp":"2026-10-26T18:43:40.9367920+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":119732,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5777},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":11801},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":11630},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":11679},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":12212},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":11653},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":11883},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":11755},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":5661},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":17850}]}
{"call":"1/T3","timestamp":"2026-10-26T18:45:52.5893823+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":55041,"items":{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":37968}}
{"call":"1/T4","timestamp":"2026-10-26T18:46:54.6512510+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":12017,"items":{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":5472}}
{"call":"1/C","timestamp":"2026-10-26T18:47:09.0907094+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17425,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5293},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5801}]}
{"call":"2/T5","timestamp":"2026-10-26T18:47:35.1576220+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":6308,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"2/T1","timestamp":"2026-10-26T18:47:45.6116309+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":11442,"items":{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":5178}}
{"call":"2/T2","timestamp":"2026-10-26T18:48:02.8101425+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":118685,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5252},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":11902},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":11897},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":11865},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":12202},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":11915},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":12133},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":12000},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":5584},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":17678}]}
{"call":"2/T3","timestamp":"2026-10-26T18:50:12.1546278+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":55378,"items":{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":38335}}
{"call":"2/T4","timestamp":"2026-10-26T18:51:10.3534348+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":11996,"items":{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":5465}}
{"call":"2/C","timestamp":"2026-10-26T18:51:25.0229789+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17380,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5402},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5746}]}
{"call":"3/T5","timestamp":"2026-10-26T18:51:49.9061474+01:00","tool":"plc_write","accessMode":"full","phase":"preview","durationMs":6327,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"skipped","durationMs":null},{"operationId":"t2-02","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-03","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-04","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-05","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-06","operation":"create_tag","status":"skipped","durationMs":null},{"operationId":"t2-07","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-08","operation":"create_user_constant","status":"skipped","durationMs":null},{"operationId":"t2-09","operation":"create_block_group","status":"skipped","durationMs":null},{"operationId":"t2-10","operation":"create_block","status":"skipped","durationMs":null}]}
{"call":"3/T1","timestamp":"2026-10-26T18:52:01.7822533+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":11574,"items":{"operationId":"t1-01","operation":"create_tag","status":"succeeded","durationMs":5221}}
{"call":"3/T2","timestamp":"2026-10-26T18:52:18.8069538+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":130967,"items":[{"operationId":"t2-01","operation":"create_tag_table","status":"succeeded","durationMs":5210},{"operationId":"t2-02","operation":"create_tag","status":"succeeded","durationMs":11972},{"operationId":"t2-03","operation":"create_tag","status":"succeeded","durationMs":11856},{"operationId":"t2-04","operation":"create_tag","status":"succeeded","durationMs":12110},{"operationId":"t2-05","operation":"create_tag","status":"succeeded","durationMs":14355},{"operationId":"t2-06","operation":"create_tag","status":"succeeded","durationMs":13808},{"operationId":"t2-07","operation":"create_user_constant","status":"succeeded","durationMs":13827},{"operationId":"t2-08","operation":"create_user_constant","status":"succeeded","durationMs":14196},{"operationId":"t2-09","operation":"create_block_group","status":"succeeded","durationMs":6426},{"operationId":"t2-10","operation":"create_block","status":"succeeded","durationMs":20770}]}
{"call":"3/T3","timestamp":"2026-10-26T18:54:41.7550533+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":64544,"items":{"operationId":"t3-01","operation":"update_block_logic","status":"succeeded","durationMs":44731}}
{"call":"3/T4","timestamp":"2026-10-26T18:55:49.4266459+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":14459,"items":{"operationId":"t4-01","operation":"delete_block_group","status":"succeeded","durationMs":7054}}
{"call":"3/C","timestamp":"2026-10-26T18:56:06.1760143+01:00","tool":"plc_write","accessMode":"full","phase":"applied","durationMs":17291,"items":[{"operationId":"c-01","operation":"delete_tag","status":"succeeded","durationMs":5443},{"operationId":"c-02","operation":"delete_tag_table","status":"succeeded","durationMs":5662}]}
```

