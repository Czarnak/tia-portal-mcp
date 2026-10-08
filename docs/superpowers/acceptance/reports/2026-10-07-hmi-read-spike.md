# `hmi_read` live spike (Task 0, S1–S10) — 2026-10-07

Result: **all ten questions were answered from live evidence, with recorded gaps.** One read crashed TIA Portal. Reading
`HmiTag.ConfirmationType` on a Unified Comfort panel killed the Portal process, so `hmi_read` must never read that
property (see "Crash finding" below). The parameter values that the later tasks depend on are:

| Parameter | Value |
| --- | --- |
| Default `limit` for paged operations | **100** (maximum stays 2000) |
| Member depth cap | **8** (the deepest nesting observed was 4) |
| Classic software runtime type | **`Siemens.Engineering.Hmi.HmiTarget`** (assembly `Siemens.Engineering.WinCC`) |
| Unified software runtime type | `Siemens.Engineering.HmiUnified.HmiSoftware` (assembly `Siemens.Engineering.WinCCUnified`) |
| Runtime-settings sub-objects that are null on Unified Comfort | `HmiReportingSettings`, `HmiUpssRuntimeSettings`, `OpcUaServerRuntimeSettings`, `ProcessDiagnosticsRuntimeSettings`, `TelemetryRuntimeSettings`; also `HmiExclusiveOperationSettings` on the panel at firmware 21.0.0.0 and on the PC station |
| Kinds without `Validate()` | `HmiSoftware`, `HmiTagTable`, `HmiTagTableGroup`, `HmiSystemTag`, `HmiThreshold`, `HmiScreenGroup`, `HmiScriptModule`, `HmiTextList`, `HmiGraphicList`, `HmiSystemTextList` |
| Multilingual text | returned **raw**, exactly as Openness returns it (S5) |
| License state | no `LicenseNotFoundException`; enumeration, `Find` and `Validate()` all worked |
| Properties `hmi_read` must never read | `HmiTag.ConfirmationType` (crashed TIA Portal); `GmpRelevant` and `MandatoryCommenting` were not read |

## Method

- **Authorization.** The maintainer authorized read-only use of a disposable V21 project copy that was already open in
  TIA Portal. The project's name and location are not recorded here. Device, tag and screen names are left out, and
  IP addresses are masked.
- **Harness.** A throwaway net48 x64 console was built and run outside the repository and never committed. It
  references the installed `PublicAPI\V21\net48` assemblies with `Private=false` and resolves `Siemens.Engineering.*`
  at runtime, the way the worker does. It attaches to the running Portal, checks that the open project is the
  authorized copy, prints one finding per question, and detaches with `TiaPortal.Dispose()`.
- **Read-only.** Nothing was saved, closed, compiled, created or modified. Apart from one deliberate S1 probe, `Find`
  was never called. Every property read goes through reflection inside try/catch, so one failure is recorded and the
  run continues.
- **Run 1, 17:34.** This run read *every* public property of each tag by reflection. TIA Portal crashed (see below).
- **Run 2, 17:40–18:02, after the maintainer restarted TIA Portal and reopened the same copy.** The hardened harness:
  - adds a crash denylist;
  - writes every property name to a trace file, flushed, before reading it;
  - phase A reads only explicit, allow-listed properties for all five Unified HMIs;
  - phase B, run last, uses generic reflection dumps (runtime settings first, then tag, alarm, log and connection
    dumps).

  Run 2 completed with no crash and no TIA crash report. About 4 s after the harness detached, the Portal process
  (the instance the maintainer had restarted) was no longer running. No crash report or Application Error event was
  written, and the cause (a user close, or an exit on detach) was not determined.

## Environment

- **Installed software:**
  - TIA Portal V21 Update 2 Hotfix 1; STEP 7 Professional V21 Update 2 Hotfix 1.
  - WinCC Basic/Comfort/Advanced V21 Update 2 Hotfix 1; WinCC Unified V21 Update 2.
  - `Siemens.Engineering.Base` 21.0.0.0, file version 2100.0.121.1.
- **Project:**
  - Products used by the project include WinCC Unified V21 Update 1 and WinCC Comfort V21.
  - Languages are en-US and pl-PL; the editing and reference language is en-US.
- **Device inventory:** the walk covers root devices, user device groups recursively and ungrouped devices, and
  enumerates DeviceItems recursively, so the inventory is complete. The brief expected one Comfort panel and one PC
  station; the copy has more.

| Kind | Count | Where the software was found | TypeIdentifier evidence |
| --- | --- | --- | --- |
| Unified Comfort panel | 4 | Root devices. `HmiSoftware` is on a depth-1 device item. | `Device.TypeIdentifier` is **null**. The software device item's TypeIdentifier is null. The head item (named like the device) has `OrderNumber:6AV2 123-3KB32-0AW0/21.0.1.0` (three panels) or `/21.0.0.0` (one panel). |
| Unified PC station (WinCC Unified PC RT) | 1 | Root device. `HmiSoftware` is on the depth-1 item next to the `SIMATIC PC station` rack. | `Device.TypeIdentifier` = `System:Device.PC`, the rack item has `System:Rack.PC`, and the **software item** has `OrderNumber:6AV2 155-xxxxx-xxxx/20.0.0.2`. |
| Classic (WinCC Comfort) panel | 1 | Inside a user device group. Software on a depth-1 item. | `Device.TypeIdentifier` is null. |
| PLCs (`PlcSoftware`) | 2 | ET 200SP stations | — |
| Other | 47 | 44 GSD devices, 2 switches, 1 device proxy (55 devices in total) | — |

`GetAttribute("OrderNumber")` and `GetAttribute("FirmwareVersion")` on HMI device items throw
`EngineeringNotSupportedException` ("'OrderNumber' is not supported by type '…DeviceItemImpl'"). The only exception is
the PC station's generic IE item. `list_hmi_devices` therefore cannot take its type identifier from one fixed place:

- Comfort panels have it only on the head device item.
- The PC station has it on the software device item.
- `Device.TypeIdentifier` is null on Comfort panels.

Per-HMI inventory, from enumeration in run 2:

| HMI | Tags (tables) | System tags | Discrete / analog alarms | Alarm classes | Connections | Screens (groups) | Data / alarm logs | Scripts | Text / graphic / system text lists |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Comfort A | 72 (7) | 29 | 78 / 0 | 19 | 1 | 21 (3) | 0 / 0 | 2 | 6 / 7 / 24 |
| Comfort B | 71 (7) | 29 | 78 / 0 | 19 | 1 | 21 (3) | 0 / 0 | 1 | 4 / 7 / 24 |
| Comfort C (fw 21.0.0.0) | 75 (7) | 29 | 81 / 0 | 19 | 1 | 22 (3) | 0 / 0 | 1 | 4 / 7 / 24 |
| PC station | 1142 (23, 1 group) | 32 | 1523 / 0 | 26 | 3 | 180 (3) | 6 / 1 | 1 | 9 / 13 / 21 |
| Comfort D | 65 (6) | 31 | 57 / 0 | 19 | 2 | 11 (3) | 0 / 0 | 1 | 5 / 7 / 24 |

Other counts were zero on every HMI:

- audit trails;
- `HmiAlarmAuditClass`;
- `OpcUaAlarmTypes`;
- `PlantObjectTags`;
- analog alarms.

`HmiSoftware.Tags` returned every tag across all tables and groups: its count equalled the sum over tag tables
including groups on every HMI.

## Crash finding (run 1)

The harness's "full row" pass read each tag property in alphabetical order. TIA Portal (V21 Update 2 HF1) crashed,
and its error report was filed with `Reason="Openness"`. The faulting frame was:

```
Siemens.Simatic.Hmi.Utah.Tag.Openness.Tag.TagProperties.AuditConfirmationTypeAdapter.DoGetPropertyValue()
  Type: Siemens.Simatic.Hmi.Utah.Common.Base.HmiBLException
  Message: Property not found : ConfirmationType
  ... Siemens.Automation.Openness.ServerAdapter.Object`1.IObjectContract.GetAttribute(String name)
```

The harness then saw `EngineeringObjectDisposedException` ("TIA Portal has either been disposed or stopped running").
**`HmiTag.ConfirmationType` must not be read** on a device without GMP/audit configured, which covers at least the
Unified Comfort panels. Its siblings `GmpRelevant` and `MandatoryCommenting` were denylisted in run 2 as unproven risk
and never read, so their behaviour is unknown. None of the three is in the `hmi_read` contract.

Other GMP/audit-type properties fail **recoverably**. They throw `EngineeringTargetInvocationException`, and TIA
Portal stays up:

| Property | Device | Message |
| --- | --- | --- |
| `HmiDiscreteAlarm.AuditClass` | Comfort | `-PropertyDoesNotExists` (on the PC station it returns `""`) |
| `HmiRuntimeSetting.GMPEnabled` | Comfort | "The GMPEnabled property is not supported on the current device version." |
| `HmiRuntimeSetting.GeneralESIGCommentsStrategy` | Comfort | "The property is not supported for the current device." |
| `HmiUnifiedTagSettings.TagOptimizationActive` | PC station | "The TagOptimizationActive property is not supported in the current device version." |

Readers therefore read properties from explicit lists, never from a reflection sweep. They catch
`EngineeringTargetInvocationException` per property and map it to null. A process crash cannot be caught.

## Findings

### S1 — License state

- No `LicenseNotFoundException` occurred in either run. The final exception histogram for run 2 contains only:
  - `MissingMemberException`, raised locally by the harness when it probed members that do not exist, such as
    `Condition` on discrete alarms;
  - `EngineeringTargetInvocationException`, all from the recoverable reads above;
  - `EngineeringNotSupportedException` from `GetAttribute`;
  - the harness's own denylist skips.
- Enumeration worked for every `HmiSoftware` composition on all five HMIs.
- `Tags.Find(existingName)` returned the `HmiTag`. `Tags.Find("__spike_nonexistent__")` returned `null` and did not
  throw.
- `Validate()` returned a result list on every validatable object, 3,614 objects in total.
- This engineering station therefore behaves as licensed. An unlicensed station was not available, so the
  `LicenseNotFoundException` path is unobserved. The design's rule to enumerate rather than call `Find` stays as
  defence in depth.

### S2 — Cold cost and default `limit`

These are "core row" timings for `list_tags`: name, table, connection, PLC tag, data type, HMI data type, address,
acquisition mode/cycle, access mode, persistent, `LinearScaling`, and `Comment` read as all items. Each run attaches
afresh.

| HMI | Tags read | Core row, cold | Core row, warm repeat |
| --- | --- | --- | --- |
| Comfort A, run 1 (Portal had been up for some time) | 72 | 1,417 ms (19.7 ms/tag) | 570 ms |
| Comfort A, run 2 (Portal just restarted) | 72 | 3,868 ms (53.7 ms/tag) | 697 ms |
| Comfort B | 71 | 986 ms (13.9 ms/tag) | 638 ms |
| Comfort C | 75 | 881 ms (11.8 ms/tag) | 1,100 ms |
| PC station | **500** | **6,571 ms (13.1 ms/tag)** | 7,690 ms |

Full-row extra for `get_tag` covers:

- the five `Object` values;
- `InitialMaxValue`/`InitialMinValue` (`Value`, `ValueType`);
- `SubstituteValue` (`SubstituteValueUsage`, `Value`);
- each threshold (`Name`, `Mode`, `Value`, `ValueType`);
- each logging tag (18 properties);
- members, recursively with core and extra.

It costs **407–725 ms per tag**, dominated by member recursion:

- Comfort panels: 72 tags in 45.4 s, 71 in 40.1 s, 75 in 54.3 s.
- PC station: 500 tags in 203.6 s.

A full row is therefore only feasible for a single tag (`get_tag`), as the design already splits it.

Screen items are read with name, CLR type, left/top/width/height, visible and enabled. The cold cost was 3.7–6.4 ms
per item, for example 534 items in 2,339 ms and 120 items in 610 ms. The item types seen were:

- `Shapes.HmiGraphicView`, `Shapes.HmiRectangle` and `Shapes.HmiText`;
- `Widgets.HmiButton`, `Widgets.HmiIOField` and `Widgets.HmiSymbolicIOField`.

None of them failed a geometry read. Counting the items of all 180 PC screens took 15.1 s.

`Validate()` took about 10–17 ms per object on the PC station: 1,142 tags in 20.0 s and 1,523 alarms in 23.9 s.

**The default `limit` is 100, and the value budget sets it, not time.** The core row cost 434–448 characters per tag
in the harness's debug format. Two languages carry a short CLR type prefix per field. A canonical JSON row is
estimated at about 350–450 characters, which is inference, not measured. The 60,000-character value budget therefore
holds about 130–170 tag rows, while 500 rows (≈ 200 kB) would always be omitted. 100 rows fit with headroom and cost at
most about 5.4 s cold at the slowest observed rate. Smaller rows, such as system tags, logging tags or screen items,
can raise `limit` up to the unchanged maximum of 2000; the budget guidance covers any overflow.

### S3 — Runtime types behind `System.Object` properties

Reflection over `Siemens.Engineering.WinCCUnified.dll` lists 24 `System.Object`-typed properties:

- 19 outside dynamizations and events;
- 5 inside `UI.Dynamization`/`UI.Events`, which are out of scope.

The encoding table below covers every in-scope one. Counts are property *reads* across all five HMIs, including
member tags and both the S2 and S3 passes; they are not distinct objects. Each property showed exactly one runtime type
over all its reads, apart from the faceplate interface value. Only five sample values were kept per type, and all
samples were defaults: `""`, `0`, `10`, `100`, `False`. Non-default values were not captured.

### S4 — Runtime settings: Unified PC versus Unified Comfort

`HmiSoftware.RuntimeSettings` is `Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSetting`. It is non-null on
every HMI and implements `Validate()`. "null" below means the property returned null; "throws" means a recoverable
`EngineeringTargetInvocationException`.

| Member | Comfort, fw 21.0.1.0 (3 panels) | Comfort, fw 21.0.0.0 | PC station |
| --- | --- | --- | --- |
| `HmiExclusiveOperationSettings` | object (`ExclusiveOperationControlTag`/`StatusTag` = null) | **null** | **null** |
| `HmiReportingSettings` | **null** | **null** | object |
| `HmiUnifiedTagSettings` | object | object | object (`TagOptimizationActive` **throws**) |
| `HmiUpssRuntimeSettings` | **null** | **null** | object |
| `LanguageAndFonts` (`HmiLanguageAndFontAssociation`) | 2 entries | 2 entries | 2 entries |
| `MaxLoginRuntimeSettings` | object | object | object |
| `OpcUaServerRuntimeSettings` | **null** | **null** | object (22 scalar settings) |
| `ProcessDiagnosticsRuntimeSettings` | **null** | **null** | object |
| `RuntimeResourceSettings` | object | object | object |
| `TelemetryRuntimeSettings` | **null** | **null** | object |
| `GMPEnabled` (scalar) | **throws** | **throws** | `False` |
| `GeneralESIGCommentsStrategy` (scalar) | **throws** | **throws** | `BothComments` |
| `AutoLogOffURL` (string) | null | null | null |
| `StartScreen` | `System.String` | `System.String` | `System.String` |
| `ScreenResolution` (enum) | `SR_1280X800` | `SR_1280X800` | `SR_1920X1080` |

No sub-object read threw. Device-dependent absence shows up as null at the sub-object level, and as a recoverable
throw at the scalar level. Every sub-object and the three scalars listed must therefore be nullable, and the reader
must catch the throw per property.

`LanguageAndFonts` entries are `HmiLanguageAndFont` with these members (declared, by reflection):

- `Language`, a string;
- `Order`, an Int16;
- `Enable` and `EnableForLogging`, booleans;
- `DefaultFont` and `FixedFont1`…`FixedFont4`, strings.

The analysis's F14 note that telemetry storage "can be set only on Comfort Panel V20+" does **not** match this
project: `TelemetryRuntimeSettings` is null on all four Comfort panels and present on the PC station.

### S5 — Formatted-text markup

- **Alarm `EventText`.** All 1,817 alarms carry markup in every language, for example
  `<body><p>Safety - Button (…) - ESTOP was pressed</p></body>`. Unused `EventText1`…`EventText9` are
  `<body><p/></body>`, not empty. `InfoText`, when unused, is `""` without markup. Non-empty `InfoText` was not
  present in the project.
- **Tag `Comment` and `DisplayName`.** Every item in the project is `""`. No tag has a comment, so markup on non-empty
  tag comments is **unobserved**.
- **`HmiConnection.Comment`** is a plain `System.String`, not a `MultilingualText`, and was null.
- **Faceplate interface values bound to text properties** hold a complete formatted-text document, for example
  `<?xml version="1.0" encoding="utf-8"?><ProjectText xmlns="http://www.siemens.com/Industry/2009/10/01/Automation/FormattedText"><body><p>…</p></body></ProjectText>`.

**Rule: return text raw.** `HmiText.text` is the exact `MultilingualTextItem.Text` string, markup included. Stripping
would lose parameter fields and formatting, and would make reads differ from what a later `hmi_write` must send. The
markup is uniform, and an agent can read it directly. Empty texts are returned as given: `""` or `<body><p/></body>`.

### S6 — `Validate()` shape and kinds without it

- **Results.** 3,614 objects were validated: tags, discrete alarms, screens, connections, alarm classes, data logs and
  alarm logs. **All were clean.** Each returned `Siemens.Engineering.HmiUnified.Common.Internal.HmiValidationResultList`
  with count 0, and none threw. **The project contains no object with validation errors or warnings**, so the
  populated result shape is unobserved.
- **Declared shape** (reflection on the DLL): `HmiValidationResult { PropertyName : String; Errors :
  IEnumerable<String>; Warnings : IEnumerable<String> }`. The design's `propertyName`, `errors[]` and `warnings[]`
  match it.
- **Kinds with `Validate()`:** checked by type for every `HmiSoftware` composition element and the tag sub-collections.
  - `HmiTag`, including member tags, and `HmiLoggingTag`;
  - `HmiDiscreteAlarm`, `HmiAnalogAlarm` and `HmiAlarmClass`;
  - `HmiAlarmAuditClass`, `HmiOpcUaAlarmType` and `HmiConnection`;
  - `HmiDataLog`, `HmiAlarmLog` and `HmiAuditTrail`;
  - `HmiScreen` and screen items, for example `HmiGraphicView`;
  - `HmiRuntimeSetting` and `Cpm.PlantObjectInterface`.
- **Kinds without `Validate()`:** these are the `notValidatable[]` kinds.
  - `HmiSoftware`, `HmiTagTable`, `HmiTagTableGroup` and `HmiSystemTag`;
  - `HmiThreshold`, `HmiScreenGroup` and `HmiScriptModule`;
  - `HmiTextList`, `HmiGraphicList` and `HmiSystemTextList`.

### S7 — Navigation types

| Property | Runtime type | Values |
| --- | --- | --- |
| `HmiScreenWindow.Screen` | `System.String` | The target screen's name. It equals `HmiScreenWindow.ScreenName` in all 19 windows observed. `ScreenNumber` is a `UInt16` and was 0 in every window. |
| `HmiRuntimeSetting.StartScreen` | `System.String` | The screen's name, with the same case as the screen in every HMI. |

Navigation edges therefore come straight from name strings, with no object resolution. The screen windows referenced
only screens of the same HMI, in groups and at the root.

`HmiFaceplateContainer.ContainedType` is a `System.String` of the form `V<version>\<faceplate type>`, for example
`V0.0.3\FP_Estop`. There were 1,393 faceplate containers.

### S8 — `HmiConnection.InitialAddress` format

All 8 connections use the driver `SIMATIC S7 1200/1500`, and every address has this form:

```
Version=16.0.0.0;CommunicationInterface=Industrial Ethernet;HostAccessPoint=S7ONLINE;HostAddress=192.168.x.y;PlcAddress=192.168.x.z;
```

`InitialAddress` is a list of `key=value` pairs separated by `;`, with a trailing `;`, and no quoting or escaping was
observed. The parser splits on `;`, drops empty segments, splits each segment at its first `=`, and keeps the raw
string as well. Other drivers were not present, so their keys are unobserved.

The other connection members were these:

- `Partner`, `Station` and `Node` are display strings.
- `DisabledAtStartup` is a `Boolean`.
- `DriverProperties` is a `DriverPropertyComposition` with 6 entries per connection. Each entry is a `DriverProperty`
  with the strings `PropertyName`, `Value` and `Info` (declared types). Entry values were not dumped.

### S9 — Member nesting depth

`HmiSoftware.Tags` holds 1,425 tags. Their `Members` depth histogram, where 0 means no members, was:

| HMI | Depth 0 | Depth 1 | Depth 2 | Depth 4 |
| --- | --- | --- | --- | --- |
| Comfort A | 47 | 16 | 9 | — |
| Comfort B | 48 | 14 | 9 | — |
| Comfort C | 49 | 17 | 9 | — |
| PC station | 873 | 196 | 72 | 1 |
| Comfort D | 58 | 5 | 2 | — |

The maximum observed depth is **4**, on one PC-station UDT tag, so **a cap of 8** leaves twice the observed headroom.
The member count per tag was not measured. A tag with many members can make one `get_tag` value large, so
`membersTruncated` should also trigger on the value budget, not only on depth.

### S10 — Classic detection

- The Classic (WinCC Comfort) panel's software runtime type is **`Siemens.Engineering.Hmi.HmiTarget`**:
  - assembly `Siemens.Engineering.WinCC`;
  - base chain `HmiTarget ← Siemens.Engineering.HW.Software ← Object`.
- Unified software is `Siemens.Engineering.HmiUnified.HmiSoftware`:
  - assembly `Siemens.Engineering.WinCCUnified`;
  - base chain `HmiSoftware ← Siemens.Engineering.HW.Software ← Object`.
- Matching the runtime type name works without a compile-time reference to `Siemens.Engineering.WinCC.dll`. Openness
  still loads that assembly into the client process when it materializes the object, and the worker's existing
  `Siemens.Engineering.*` resolver covers this.

## Encoding table

`HmiVariant { type, value }` is defined as follows:

- `type` is the runtime type's `FullName`, or null when the value is null.
- `value` is the JSON form of the value:
  - a string becomes a JSON string;
  - a `Boolean` becomes `true`/`false`;
  - integral types and `Single`/`Double`/`Decimal` become a JSON number, with `Double` written round-trip and
    NaN/±Infinity as a JSON string;
  - an enum becomes its name as a string;
  - any other type becomes `ToString()` with the invariant culture, as a string.

| Property | Observed runtime types (reads) | Encoding | Example |
| --- | --- | --- | --- |
| `HmiTag.InitialValue` | `System.String` (27,629) | variant | `{"type":"System.String","value":""}` |
| `HmiTag.HmiStartValue` | `System.Double` (27,629) | variant | `{"type":"System.Double","value":0}` |
| `HmiTag.HmiEndValue` | `System.Double` (27,629) | variant | `{"type":"System.Double","value":100}` |
| `HmiTag.PlcStartValue` | `System.Double` (27,629) | variant | `{"type":"System.Double","value":0}` |
| `HmiTag.PlcEndValue` | `System.Double` (27,629) | variant | `{"type":"System.Double","value":10}` |
| `UpperRange.Value` (via `InitialMaxValue`) | `System.String` (27,629) | variant, plus `valueType` (`HmiLimitValueType`, observed `None`) | `{"type":"System.String","value":""}` |
| `LowerRange.Value` (via `InitialMinValue`) | `System.String` (27,629) | variant, plus `valueType` | `{"type":"System.String","value":""}` |
| `HmiSubstituteValue.Value` | `System.String` (27,629) | variant, plus `usage` (`HmiSubstituteValueUsage`, observed `None`) | `{"type":"System.String","value":""}` |
| `HmiThreshold.Value` | `System.String` (18,312) | variant | `{"type":"System.String","value":""}` |
| `HmiLoggingTag.HighLimit` / `LowLimit` | `System.String` (39 each) | variant | `{"type":"System.String","value":""}` |
| `AlarmBase.AlarmParameterTags` | `System.Collections.Generic.List<System.String>`, always 10 elements, all `System.String` (1,817) | **not a scalar.** Typed `string[]` of the 10 parameter-tag names in order, `""` when unset. Any other runtime shape becomes `null` plus a warning. | `["","",…]` |
| `HmiFaceplateInterface.Value` | `System.String` (3,328), `System.Boolean` (2) | variant. Strings are tag names or full formatted-text XML, returned raw. | `{"type":"System.String","value":"<tag name>"}`, `{"type":"System.Boolean","value":false}` |
| `HmiAnalogAlarm.ConditionValue` | **unobserved** (no analog alarms) | variant, by the general rule | — |
| `HmiCustomControlInterface.Value` | **unobserved** (no custom controls; not in the contract) | — | — |
| `Cpm.*` (5 properties) | not read (plant model out of scope) | — | — |

The rule for text, from S5: `HmiText.text` is the raw `MultilingualTextItem.Text`, unstripped.

## Other observations for the readers

- **`HmiTag` members (by reflection):**
  - `LinearScaling` is a **`Boolean`**, not a sub-object.
  - The upper and lower ranges are `InitialMaxValue : UpperRange` and `InitialMinValue : LowerRange`. Neither has
    a `Value` of its own beyond `Range.Value`/`ValueType`. `HmiTag` has no `UpperRange` or `LowerRange` property.
  - `Comment` and `DisplayName` are `MultilingualText`.
  - `Scope` is `HmiTagScope`, observed as `System`; `TagType` is `HmiTagType`, observed as `Simple` or `UDT`.
  - `PlcName` names the partner PLC.
- **Alarms:**
  - `AcknowledgmentControlTag` and `AcknowledgmentStateTag` hold the literal string `"<No tag>"` when unset, and
    their bit numbers are `Int32`.
  - `RaisedStateTagBitNumber` is `UInt32` and `Priority` is `Byte`.
  - `TriggerMode` is `HmiDiscreteAlarmTriggerMode`, for example `OnRisingEdge`.
  - `Origin` and `Area` are strings.
- **`HmiDataLog` and `HmiAlarmLog`** expose `Settings : LogSettings`, `Segment : LogSegment` and `Backup : LogBackup`.

## Evidence gaps

The project did not contain these cases, so they are not inferred:

- a validation error or warning;
- non-empty tag comments;
- analog alarms;
- custom controls;
- connection drivers other than S7-1200/1500;
- `InfoText` content;
- an unlicensed engineering station;
- non-default values behind the `Object` properties.

The behaviour of `GmpRelevant` and `MandatoryCommenting` is unknown, because they were deliberately not read.
