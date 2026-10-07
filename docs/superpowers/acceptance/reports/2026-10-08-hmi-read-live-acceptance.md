# `hmi_read` installed-tool live acceptance (Task 9) — 2026-10-08

Result: **PASS with recorded limits.** All 20 operations ran live at least once and returned typed results. Every
error and edge path in the brief returned the category from spec §2.5. A whole-value omission behaved as specified.
TIA Portal and the worker stayed up for the whole run, and the project was still unmodified at the end
(`isModified:false`, `lastModified` unchanged).

The first run found one Medium security-hardening finding (D1) and two Low findings (D2, D3), listed under "Defects
and observations". D1 and D2 were fixed in `e565fe3` and verified live on that build ("Re-check on e565fe3"). D3
remains accepted by design. One live question, the encoding of a non-integral `Double` on the net48 worker, cannot
be settled read-only because the project holds no such value. `validate` passed on the clean path only: no live
object had findings.

## Scope and authorization

- **Authorization.** The maintainer opened a disposable copy of a real V21 project in TIA Portal and installed the
  branch build as the local tool. Reads only. The project's name and location are not recorded here. HMI, device,
  screen, tag and type names are replaced with neutral labels, and IP addresses are masked.
- **Preconditions.** These were checked before any HMI call:
  - `get_project_status` showed exactly one open project, and it was the authorized disposable copy;
  - the tool list contained `hmi_read`, so the installed build was the branch build.
- **Read-only.** The only calls made were `hmi_read` and `get_project_status`. No write, lifecycle, compile or
  `bind_project` call was made; the session was already bound. The project was not changed, and no second HMI was
  added. The brief's step "otherwise add a second Unified HMI" was not needed, because the copy already has five.
- **Conducted by** one agent, with calls run one after another (some calls carried several items).

## Environment

| Item | Value |
| --- | --- |
| Tool build, main run | `tiamcpserver 3.0.1-local.393.gac78706`, which is `ac78706` on `feature/hmi-read` (installed as a global tool) |
| Tool build, re-check | `tiamcpserver 3.0.1-local.396.ge565fe3`, which is `e565fe3` (the D1/D2 fix), reinstalled by the maintainer with the MCP server restarted |
| Installed access mode | `full`. The server and worker command lines at the re-check read `--access-mode full`. The main run's mode was not captured separately; the tool list exposed write tools there too. No write, lifecycle or compile tool was called in either run. |
| TIA Portal | V21 (Update 2 Hotfix 1, as in the spike), the same workstation as the spike |
| Project languages | en-US and pl-PL; the editing and reference language is en-US |

Device inventory, as `list_hmi_devices` returns it:

| Label | Kind | `typeIdentifier` (order number, masked to the family) |
| --- | --- | --- |
| Unified PC | `unified` (PC station, WinCC Unified PC RT) | `OrderNumber:6AV2 155-xxxxx-xxxx/20.0.0.2` |
| Comfort A, B, D | `unified` (Unified Comfort panel, fw 21.0.1.0) | `OrderNumber:6AV2 123-…/21.0.1.0` |
| Comfort C | `unified` (Unified Comfort panel, fw 21.0.0.0) | `OrderNumber:6AV2 123-…/21.0.0.0` |
| Classic panel 1 | `classic` (WinCC Comfort) | `OrderNumber:6AV2 124-…/17.0.0.0` |

The Unified PC is the largest HMI. It has 1,142 tags, 1,523 alarms, about 180 screens and 1,286 faceplate instances.

## Results — operations

Inputs are summarized and outputs trimmed. All outputs had `isComplete:true` and `messages:[]` unless stated
otherwise.

| # | Operation | Input | Expected | Observed | Result |
| --- | --- | --- | --- | --- | --- |
| 1 | `list_hmi_devices` | — | Unified and Classic devices with kind and type | 6 devices: 5 `unified`, 1 `classic`, each with device name, software name and type identifier | PASS |
| 2 | `list_tag_tables` | Comfort A root; Unified PC root; Unified PC `groupPath` (one named group) | Group/table tree with tag counts | Comfort A: 7 tables, no groups. Unified PC: 20 tables plus 1 group with 3 tables. The `groupPath` call returned only that group's 3 tables. | PASS |
| 3 | `list_tags` | Unified PC, largest table (278 tags), `limit:100`; Comfort B with no table, `offset:70, limit:1` | Paged core rows, ordered by name | `page {offset:0, limit:100, total:278, nextOffset:100}` with 100 rows of 14 fields. The rows are in name order (case-insensitive ordinal). With names neutralized to their prefix, the page runs `B…`, then `DB_…_1…`, `DB_…_2…`, `DB_…_3…`, then later `DI_…` and `HMI_…`. Comfort B: `total:71`, `nextOffset:null`, 1 row with an absolute address (`%DB…`). | PASS |
| 4 | `get_tag` | Unified PC: a `Real` tag; a logged tag; a UDT tag | Core row plus variants, ranges, substitute value, logging tags and members | All five variants are present (see live question 1). The logged tag lists its one logging tag. The UDT tag has 3 members, two of which have 3 members each (depth 2), with `membersTruncated:false`. | PASS |
| 5 | `list_system_tags` | Unified PC, `limit:3` | Paged name and data type | `total:32`, `nextOffset:3`, rows such as `{name:"@Configured…", dataType:"UDInt"}` | PASS |
| 6 | `list_connections` | Comfort A; Unified PC | Driver, raw and parsed `initialAddress`, partner, station, node, flags, driver properties | Comfort A has 1 connection and the Unified PC has 3, all `SIMATIC S7 1200/1500`. `initialAddress.raw` and `parsed` agree (keys `Version`, `CommunicationInterface`, `HostAccessPoint`, `HostAddress`, `PlcAddress`; addresses `192.168.x.x`). On `ac78706` this produced D1 and D2; see K1 for `e565fe3`. | PASS |
| 7 | `list_alarms` | Unified PC, `limit:2`, `language:"en-US"`; `alarmKind:"analog"` | Paged rows; the kind filter applies | `total:1523`, with discrete rows carrying `eventText` markup (`<body><p>…</p></body>`) in en-US only. The analog filter returned `total:0` and an empty page. | PASS |
| 8 | `get_alarm` | Unified PC, a discrete alarm, `language:"pl-PL"` | Row plus `eventText1…9` and parameter tags | The row has pl-PL text only. `eventText1…9` are each `[{pl-PL, "<body><p/></body>"}]`, and `alarmParameterTags` is 10 `""` entries. | PASS |
| 9 | `list_alarm_classes` | Comfort A, `language:"en-US"` | `alarmClasses[]`, `auditClasses[]`, `opcUaAlarmTypes[]` | 19 alarm classes (18 `isSystem:true`, 1 user class); `auditClasses:[]`; `opcUaAlarmTypes:[]` | PASS |
| 10 | `list_logs` | Unified PC | Data logs, alarm logs and audit trails with settings, segment and backup | 6 data logs, 1 alarm log and 0 audit trails, each with `settings`, `segment` (including `segmentTimePeriod {days,hours,minutes,seconds}`) and `backup` | PASS |
| 11 | `list_logging_tags` | Unified PC with no filter, `limit:1`; filtered by `dataLogName` on a log with no logging tags; filtered by `dataLogName` on a log that owns logging tags (K2); filtered by `tagName` | Paged rows; the filters narrow | No filter: `total:16`. The empty log returned `total:0`. The owning log returned `total:9` (< 16), and every row's `dataLog` was that log. The `tagName` filter returned `total:1`. Rows carry `highLimit`/`lowLimit` as variants. | PASS |
| 12 | `list_screens` | Unified PC root, `language:"en-US"`; a 3-level `groupPath`, `language:"pl-PL"` | Screen-group tree with scalars and item counts | Root: 2 root screens and 3 top-level groups nested up to 3 deep, each screen with `displayName`, `width`, `height`, `screenNumber` and `itemCount`. The `groupPath` call returned exactly 1 screen, with pl-PL text only. | PASS |
| 13 | `list_screen_items` | Unified PC root screen (19 items); a layout screen, `limit:170` | Paged name, type, geometry, visible and enabled | 19 rows of `HmiButton`, `HmiGraphicView`, `HmiIOField`, `HmiScreenWindow`, `HmiRectangle`, `HmiSymbolicIOField` and `HmiText`, all with integer geometry. The order is ordinal: `Button_1`, `Button_10`, `Button_2`, … (TIA default names). Layout screen: `total:336`, `nextOffset:170`. See live question 2. | PASS |
| 14 | `list_faceplate_instances` | Unified PC with no `screenName`, `limit:2`; the Unified PC layout screen (K3); Comfort A start screen | Screen, container, `containedType`, bindings; `screenName` narrows | No filter: `total:1286`. The layout screen returned `total:208` (< 1,286), and every row's `screen` was that screen. A row is `{screen, container, containedType:"V0.0.x\\<faceplate type>", bindings:[{propertyName, value:{type:"System.String", value:"<tag>"}}]}`. Comfort A's start screen has no faceplates: `total:0`. | PASS |
| 15 | `get_screen_navigation` | Unified PC | `startScreen` plus screen-window edges | `startScreen` is set and there are 2 edges `{fromScreen, viaItem, toScreen}` from the root screen's two screen windows | PASS |
| 16 | `get_runtime_settings` | Comfort A (fw 21.0.1.0), Comfort C (fw 21.0.0.0), Unified PC | Settings with nullable device-dependent sub-objects | Matches spike S4 (see live question 5). The Unified PC has all sub-objects except `exclusiveOperation`. | PASS |
| 17 | `list_script_modules` | Unified PC | Module names | 1 module | PASS |
| 18 | `list_text_and_graphic_lists` | Comfort A | Three name lists | 6 text lists, 7 graphic lists and 24 system text lists. The order is case-insensitive: a graphic list named `Al…` precedes one named `AU…`. | PASS |
| 19 | `list_project_languages` | — | Languages, editing language, reference language | `languages:["en-US","pl-PL"]`, `editingLanguage:"en-US"`, `referenceLanguage:"en-US"` | PASS |
| 20 | `validate` | Every category; `name` narrowing; `all` over every Unified HMI | Scanned, clean, findings, `notValidatable` | Clean path only: 3,614 objects were clean, `findings:[]` everywhere, and system tags were listed in `notValidatable`. No live object had errors or warnings, so the shape of a finding was **not** observed live. Offline coverage of that shape is `HmiValidationReaderTests` (Task 8 coverage). See live question 4. | PASS (clean path) |

## Results — error and edge paths

| # | Case | Input | Expected | Observed | Result |
| --- | --- | --- | --- | --- | --- |
| E1 | Unknown `hmiName` | `list_tag_tables`, `hmiName:"NoSuchHmi"` | `target_not_found` | `target_not_found`: "No Unified HMI software named 'NoSuchHmi' was found in the project. Use list_hmi_devices …" | PASS |
| E2 | `hmiName` omitted while five Unified HMIs exist | `list_tag_tables` | `target_ambiguous` | `target_ambiguous`: "Several HMI software instances match. Specify an unambiguous hmiName …" | PASS |
| E3 | Classic device | `list_tag_tables` by software name; `list_script_modules` by the lower-cased device name | `target_kind_unsupported` | Both returned `target_kind_unsupported`: "HMI '…' on device '…' is a Classic (WinCC) HMI; hmi_read supports WinCC Unified only." This also confirms case-insensitive device-name matching. | PASS |
| E4 | Offset past the end | `list_tags`, largest table, `offset:5000, limit:5` | An empty page with `nextOffset:null`, not an error | `page {offset:5000, limit:5, total:278, nextOffset:null}`, `tags:[]`, `succeeded` | PASS |
| E5a | `language`, valid culture | en-US and pl-PL across `list_tags`, `list_alarms`, `get_alarm`, `list_screens` and `get_tag` | Every text array narrowed to that culture | Every `HmiText[]` held only the requested culture. Without `language`, both cultures were returned. | PASS |
| E5b | `language`, non-project culture | `list_alarm_classes`, `language:"fr-FR"` | `target_not_found` naming the project languages | `target_not_found`: "'fr-FR' is not a project language. Project languages: en-US, pl-PL." | PASS |
| E6 | Forced omission | `list_tags`, largest table (278 rows), `limit:2000`, alone in the call | A whole-value omission with limit guidance, not truncation | `status:"omitted"`, `result:null`, and `omission {reason:"resultExceededItemCharLimit", limitChars:60000, originalChars:110313, retryTool:"hmi_read", guidance:"Lower limit or add a narrowing field, or split the batch: re-run this operationId in its own hmi_read call."}`. Root `truncation.truncated:true`, `success:false`, no `isError`. That is about 397 characters per row, consistent with spike S2. | PASS |
| E7 | `validate category:"all"` across pages | Unified PC, `limit:100`, at offsets 0, 100 and 2900 | `nextOffset` continuity and `scanned` ≤ `limit` | `total:2913` on every page. `nextOffset` went 100 → 200, and was `null` at offset 2900. `scanned` was 100, 100 and 13. | PASS |
| E8 | Unknown `dataLogName` | `list_logging_tags`, `dataLogName:"NoSuchLog"` | `target_not_found` | `target_not_found`: "Data log 'NoSuchLog' was not found. Use list_logs …" | PASS |
| E9 | Other unknown targets | `get_tag`, unknown tag; `list_screen_items`, unknown screen; `list_tags`, unknown table; `validate category:"screens"`, unknown name | `target_not_found` | `target_not_found` each time, with a message naming the matching list operation | PASS |
| E10 | Wrong alarm kind | `get_alarm` of a discrete alarm with `alarmKind:"analog"` | `target_not_found` | `target_not_found`: "The analog alarm '…' was not found." | PASS |
| E11 | `groupPath` is case-sensitive | `list_tag_tables`, an existing group name in the wrong case | `target_not_found` | `target_not_found`: "Tag table group '…' was not found." | PASS |
| E12 | `projectPath` differs from the bound project | `list_project_languages` with another `.ap21` path | Item failure, no switch (`plc_read` semantics) | `binding_conflict`, and the item fails while the other items in the call succeed. No switch or open happened. | PASS |
| E13 | Item independence | Batches mixing failing and succeeding items (up to 8 items) | Later items still run | Every batch ran all of its items, and the counts matched (for example `failed:6, succeeded:1`). Root `success:false` and `isError:false`. | PASS |

## Results — request rejections

Each rejection was sent as its own call, because a rejection rejects the whole request.

| # | Input | Expected | Observed | Result |
| --- | --- | --- | --- | --- |
| R1 | `validate category:"all", name:…` | `validation_error`, `isError:true` | "'name' is not valid with category all." | PASS |
| R2 | `list_tags limit:0` | `validation_error` | "'limit' must be between 1 and 2000." | PASS |
| R3 | `list_tags tagName:…` (undeclared field) | `validation_error` | "'tagName' is not valid for list_tags. Valid optional fields: hmiName, tableName, language, offset, limit." | PASS |
| R4 | `list_alarms language:"not a culture!"` | `validation_error` | "'language' must be a culture name such as en-US." | PASS |
| R5 | `list_hmi_devices hmiName:…` | `validation_error` | "'hmiName' is not valid for list_hmi_devices. Valid optional fields: (none)." | PASS |

## Re-check on e565fe3

The maintainer reinstalled the tool from `e565fe3` and restarted the MCP server. Before any HMI call,
`get_project_status` showed the same single disposable copy with `isModified:false`. In the fix, a driver property
whose name contains "password" is dropped before its value is read, a property whose name cannot be read never has
its value read, and duplicate (name, value) rows are collapsed. One `hmi_read` call carried the six items below.

| # | Case | Input | Expected | Observed | Result |
| --- | --- | --- | --- | --- | --- |
| K1 | D1/D2 fix | `list_connections` on Comfort A (1 connection) and the Unified PC (3 connections), as in the main run | No driver property whose name contains "password"; each property listed once | Every connection went from 6 driver-property rows on `ac78706` (`Physic.LocAddress` ×1, `Physic.LocDeviceName` ×1, `Protocol.Password` ×2, `Protocol.RemStAddress` ×2) to 3 rows on `e565fe3` (`Physic.LocAddress`, `Physic.LocDeviceName`, `Protocol.RemStAddress`, each once). There were 0 password rows on all 4 connections. `initialAddress` and the other connection fields were unchanged, and `isComplete:true`. | PASS |
| K2 | `dataLogName` narrows (review I1) | `list_logging_tags`, Unified PC: no filter, `limit:1`; then `dataLogName` = a data log that owns logging tags, `limit:3` | Filtered total below the unfiltered total; rows belong to that log | Unfiltered `total:16`; filtered `total:9`, `nextOffset:3`, and all 3 rows had `dataLog` = that log | PASS |
| K3 | `screenName` narrows (review I2) | `list_faceplate_instances`, Unified PC: no filter, `limit:1`; then `screenName` = the layout screen, `limit:2` | A positive, narrowed total; rows belong to that screen | Unfiltered `total:1286`; filtered `total:208`, and both rows had `screen` = the layout screen | PASS |

**Totals: 42 rows (20 operations, 14 edge/error rows E1–E13 with E5 split into E5a and E5b, 5 rejections and 3
re-check rows). 42 passed and 0 failed. Row 20 passed on the clean path only.**

## Live questions

1. **`Double` encoding on the net48 worker: unsettled for non-integral values.** Every `System.Double` the project
   holds is integral. `hmiStartValue` serialized as `{"type":"System.Double","value":0}`, `hmiEndValue` as `100` and
   `plcEndValue` as `10`. These are plain JSON numbers with no `.0` and no exponent, on a `Real` tag, a logged tag and
   a UDT tag. Ranges, the substitute value, thresholds and logging-tag limits are all `System.String` `""`, and there
   are no analog alarms, so no value such as `0.1` exists to read. Settling `0.1` against `0.10000000000000001` live
   would need a project change, which is out of scope for read-only work. The only coverage is
   `HmiVariantMapperTests.DoubleRowsAreJsonNumbersWrittenRoundTrip` (`0.1 → "0.1"`). Its source is linked into the
   net10.0 test project, so it does not exercise net48 formatting.
2. **Screen-item geometry via `IHmiBoxFeature`: confirmed.** Items with a box (`HmiButton`, `HmiRectangle`,
   `HmiScreenWindow`, `HmiFaceplateContainer`, `HmiIOField`, `HmiText`, `HmiGraphicView`) report integer
   `left/top/width/height`.

   - **Cross-check against `list_screens`.** On the Unified PC root screen (1920×1080), the content screen window
     is 1898×925 at (11,150). That equals the 1898×925 size `list_screens` reports for every content screen it
     hosts. The navigation screen window is 1920×74 at (0,79), which equals the 1920×74 of the navigation screens.
   - **Other sizes.** Buttons are 80×75.
   - **Items beyond the canvas.** Some buttons sit beyond the 1920-px canvas (left 2010–2220). That was recorded
     as returned and not checked further.

   The 52 `HmiEllipticalArc` items on the layout screen report
   `left/top/width/height:null`. The page still has `isComplete:true` and `messages:[]`, and the items keep
   `visible`/`enabled`. No plain circle or ellipse exists in the pages read.
3. **Cost (wall clock per call, Unified PC).** The client session transcript measures from tool call to tool
   result. That includes MCP transport and client hooks; rejected requests, which never reach the worker, took
   0.4–2.9 s.

   | Call | Time |
   | --- | --- |
   | `list_screens` at the root (about 180 screens) | 23.5 s |
   | `list_faceplate_instances` with no `screenName` (`limit:2` of 1,286) | 22.2 s |
   | `get_screen_navigation` | 21.1 s |
   | `validate category:"all"`, one page of 100 | 12.3 s |
   | `list_tags`, one page of 100 | 4.7 s |
   | `list_tags`, 278 rows, omitted | 7.9 s |
   | `validate category:"all"`, all 2,913 objects in 2 items | 55.3 s |
   | `validate category:"all"` on four Comfort panels (851 objects) in one call | 33.2 s |

   The whole-project scans cost the same for any `limit` (see D3).
4. **Validation output: none present.** `validate category:"all"` covered every object of every Unified HMI. That is
   3,764 objects: the Unified PC has 2,913, and the Comfort panels have 220, 219, 227 and 185. **No errors or
   warnings were found:** 3,614 objects were clean, `findings:[]` everywhere and `isComplete:true`. The remaining
   150 objects are system tags, which appear once per page in `notValidatable:["HmiSystemTag"]`, so `scanned`
   minus `clean` equals the system tags on the page. The shape is
   `{page, scanned, clean, findings[], notValidatable[], isComplete, messages}`. A finding's shape
   (`{objectKind, name, results[{propertyName, errors[], warnings[]}]}`) was not observed live. It is covered offline
   by `HmiValidationReaderTests` (Task 8 coverage).
5. **Runtime settings on Comfort panels: yes, `isComplete:false`.** On Comfort A (fw 21.0.1.0) and Comfort C
   (fw 21.0.0.0), `general.GMPEnabled` and `general.GeneralESIGCommentsStrategy` are `null`, `isComplete` is `false`,
   and the messages are:
   - "Runtime setting 'GMPEnabled' could not be read: Error when calling method 'get_GMPEnabled' of type
     '…HmiRuntimeSetting'. -The GMPEnabled property is not supported on the current device version."
   - "Runtime setting 'GeneralESIGCommentsStrategy' could not be read: … -The property is not supported for the
     current device."

   The device-dependent null sub-objects each add an informational message, "X is not available on this device.":
   - 5 on fw 21.0.1.0;
   - 6 on fw 21.0.0.0, which adds `HmiExclusiveOperationSettings`, null as spike S4 predicted.

   The Unified PC reads `GMPEnabled:"false"` and `GeneralESIGCommentsStrategy:"BothComments"`. It is also
   `isComplete:false`, because `unifiedTags.TagOptimizationActive` throws ("not supported in the current device
   version"). That matches spike S4 and is expected under plan D8. The messages keep Siemens' raw `\r\n` line
   breaks.
6. **Other `isComplete:false` or messages: none.** The runtime-settings results above are the only incomplete
   results in 64 operation items that reached the worker. Every other success had `isComplete:true` and `messages:[]`, and no item
   carried `warnings`. Root `success:false` appeared exactly when a batch held a failed or omitted item, and
   `isError` stayed false. That is expected.

## Defects and observations

| ID | Severity | Finding |
| --- | --- | --- |
| D1 | Medium (security hardening; spec gap). **Fixed in `e565fe3`, verified live (K1).** | On `ac78706`, `list_connections` returned every driver property verbatim, including `Protocol.Password` ("PLC protection level"). Each connection had 2 such rows. The values observed were `""` on 3 connections and a 5-space string on 1, which is probably Siemens' placeholder, so no secret was seen. Nothing redacted the value. On `e565fe3`, all 4 connections have 0 password-named rows. |
| D2 | Low. **Fixed in `e565fe3`, verified live (K1).** | On `ac78706`, each of the 4 connections had 6 driver-property rows. `Protocol.Password` and `Protocol.RemStAddress` each appeared twice with identical `propertyName`/`info`/`value`, while `Physic.LocAddress` and `Physic.LocDeviceName` appeared once. (The first version of this report said "every row appears twice"; that was wrong.) On `e565fe3`, each connection has 3 rows, and each name appears once. **Cause, by inference:** the worker enumerates `DriverProperties` once with a single `ToList()`. The (name, value) collapse removed exactly the identical pairs and left the single rows untouched. That is consistent with Openness returning the duplicated `Protocol.*` entries, but Openness was not inspected directly. |
| D3 | Low (performance) | Whole-project scans cost about 21–23 s per call on the Unified PC for any `limit`. These are `list_faceplate_instances` without `screenName`, `get_screen_navigation` and `list_screens` at the root. Paging all 1,286 faceplate instances at the default 100 takes 13 calls of about 22 s, because paging is stateless. Accepted by design (spec §2.4). Guidance to page with a high `limit`, or to filter by `screenName`, would help callers. |
| O1 | Info | `get_runtime_settings.opcUaServer` returns 6 allow-listed scalars, whereas the spike saw 22 settings. This is intentional (the reader's allow-list), not a defect. |
| O2 | Info | `get_screen_navigation` reports only static screen-window edges (2 on the Unified PC), as specified. Button or script navigation is out of scope and is not reported. |

## Evidence limitations

These spike gaps remain, because the project does not contain the cases:

- a non-integral `Double` (live question 1);
- any validation error or warning, so the shape of a finding was not observed;
- analog alarms (`condition`/`conditionValue` were always `null` on discrete alarms);
- connection drivers other than SIMATIC S7 1200/1500;
- non-empty tag comments and `infoText`;
- audit trails;
- custom controls;
- an unlicensed engineering station.

Also:

- `membersTruncated:true` (the depth cap of 8) was not reached; the deepest member nesting read was 2.
- A plain circle or ellipse item was not in the pages read. The no-box case is covered by `HmiEllipticalArc`.
- Unit coverage pins these offline. Task 8 covers `HmiReadFakeWorkerTests` and the budget, and the reader tests
  cover each reader.
- Timings come from the client session transcript and include its overhead. No worker-side timer was available.

## Read-only confirmation

The final `get_project_status` showed the same single open copy with `isModified:false`. `lastModified` was
unchanged from the first check. TIA Portal and the worker did not crash during the run.

The re-check on `e565fe3` was also read-only: one `get_project_status` call and one `hmi_read` call. Before the
re-check, the project was still the single disposable copy with `isModified:false`, the same Portal process, and
`lastModified` unchanged. The only restart was the maintainer's MCP server restart, before the re-check.
