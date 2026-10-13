# `hmi_write` live write spike (W0–W9) — 2026-10-13

Result: **every question in the [hmi_read completion spec, Appendix A](../../specs/2026-10-11-hmi-read-completion-design.md#appendix-a-handoff-to-hmi_write)
was answered from live evidence, plus the connection question added on 2026-10-13.** Openness does **not**
create an HMI connection when a PLC tag is bound, so connection creation is deferred and v1 binds through an
existing connection. No probe crashed TIA Portal. One harness defect closed every running Portal
instance after the read-only W0 phase (see [Incident](#incident)); nothing in the disposable copy was saved or lost.

| Parameter | Value |
| --- | --- |
| Binding recipe | `Create(name, table)`, then `Connection`, then `PlcTag`. `PlcTag` without a connection fails. |
| Connection auto-creation | **No.** `PlcTag` on an HMI without a connection fails "The controller tag X was not found"; no connection, no network change |
| `PlcName` | Derived from `Connection`; read-only (no CLR setter; `SetAttribute` → `EngineeringNotSupportedException`) |
| `PlcTag` spelling | Quoted (`"DB".Member`) and unquoted (`DB.Member`) both accepted; stored unquoted dotted |
| `DataType` on a bound tag | Derived from the PLC tag (`Bool`, `Real`, UDT name); setting it fails "Set is not allowed for disabled fields" |
| `DataType` on an internal tag | Settable (`Int`, `Real`, `Bool`, `WString`, `DInt`); unknown type rejected; `HmiDataType` never settable |
| UDT binding | Works like a normal tag; `DataType` = UDT name, members created automatically (10 for `UDT_DRIVE_PARA`) |
| Plain struct binding | Setter accepts it; `Validate()` reports 2 errors (`DataType`, `PlcTag`) |
| Tag name scope | Unique across the whole HMI, **case-insensitive**; `HmiTagComposition.Find` is case-insensitive |
| Table name scope | Unique across groups, case-insensitive |
| Move tag between tables | Not possible (`TagTableName` read-only, `SetAttribute` not supported) |
| Delete table / group | Cascades: contained tables and tags are deleted |
| Default tag table | Cannot be deleted ("Hmi tag table could not be deleted"); **can be renamed**; tableless `Create` lands in it |
| `EventText` | **Must** be formatted (`<body><p>…</p></body>`); plain text throws "invalid format" |
| `InfoText`, tag `Comment` | **Must** be plain: Openness stores formatted text raw and the editor then shows the `<body><p>` markup |
| `DisplayName` | Openness accepts plain and formatted; editor rendering not reported |
| Dangling `Connection`, `AlarmClass`, `RaisedStateTag` | Setter accepts; only `Validate()` reports "The object X does not exist" |
| Missing PLC tag | Rejected by the `PlcTag` setter |
| `ConditionValue` | `Int32` and `Double` only; `Single` and `String` rejected; no limit-tag form |
| `SetAttributes` | All-or-nothing on a bad name or a bad value type; the `AttributeDelegate` was never invoked |
| Cost per tag (hmiB) | create 46 ms, bind 90 ms, `Validate` 18 ms, **delete 452 ms**; one HMI locate walk ≈ 2 s |

## Method

- **Authorization.** The maintainer authorized any operation against a disposable project copy
  (name withheld). Fixture: one PLC (ET 200SP, CPU 1512SP F-1 PN),
  hmiB (Unified Comfort, software `HMI_RT_1`, connection `HMI_Connection_1`, 348 tags,
  155 discrete and 4 analog alarms) and `EmptyHMI` (hmiA, software `HMI_RT_2`, no connection, not on a
  subnet). Project languages: `en-US`, `pl-PL`.
- **Harness.** A throwaway net48 x64 console outside the repository, never committed. It is reflection-only
  (references only `Siemens.Engineering.Base`), attaches only to the Portal whose open project equals the
  configured full path, writes a flushed trace line before every Openness call, keeps the `hmi_read` crash
  denylist (`ConfirmationType`, `GmpRelevant`, `MandatoryCommenting`), stops on non-recoverable, disposed
  or remoting failures, and never saves, closes or compiles. Created objects carry a `ZW<HHmmss>_<phase>_`
  prefix. An adversarial review (3 lenses, 13 verifications) ran before the live run; it found the
  `PlcName`/`TagTableName` setter gap from DLL metadata, which the harness then probed via `SetAttribute`.
- **Runs.** W0 (read-only baseline) at 16:50; W1+W2 at 16:53; W3 at 16:53; W4–W6+W7A at 16:54; the
  installed `read_cross_references` between W7A and W7B; W7B+W8+W9+W1D at about 17:00. Every phase ended
  with Portal alive.

## Incident

A review suggestion added `Dispose()` on every `TiaPortalProcess` returned by `TiaPortal.GetProcesses()`.
Per the Openness documentation, `TiaPortalProcess.Dispose` "performs a close of the process". At the end
of W0 the harness therefore closed **all three running TIA Portal instances**, including two unrelated to
the spike. W0 was read-only and the next run made no change, so the disposable copy lost nothing; the
maintainer reopened it. The call was removed (only `TiaPortal.Dispose()`, which detaches, remains). The
repository worker does not dispose `TiaPortalProcess` (`TiaPortalProcessInventory` only reads).

## Findings by question

### Q1 — Formatted text

`EventText` rejects plain text ("The argument 'text' (…) has an invalid format") and accepts
`<body><p>…</p></body>`. A new alarm's `EventText` defaults to `<body><p/></body>` in every language.
`InfoText`, tag `Comment` and tag `DisplayName` accept both forms and read back exactly what was written.
`Validate()` is clean for both forms. All items of a `MultilingualText` are writable per language.

The maintainer inspected the kept objects in the editor:

| Object | Text written | Editor shows |
| --- | --- | --- |
| hmiB discrete alarm `ZW165438_W5_d1` | plain `EventText` (rejected), plain `InfoText` | no alarm text; info text correct |
| hmiB discrete alarm `ZW165438_W5_d1f` | formatted `EventText`, formatted `InfoText` | alarm text correct; info text with raw `<body><p>` markup |
| hmiA tag `ZW165305_W2_txtPlain` | plain `Comment` | comment correct |
| hmiA tag `ZW165305_W2_txtFmt` | formatted `Comment` | comment with raw markup |

So the accepted form is per property: `EventText` is formatted text only, while `InfoText` and tag `Comment`
are plain text, and writing formatted text there silently stores markup the user then sees. `DisplayName`
rendering was not reported. The written texts also carry a harness artifact, `[String]"en-US"` in place of
the bare culture, which does not affect the result.

### Q2 — `ConfirmationType` crash path

No crash. `Create`, every setter used, `SetAttribute` and `SetAttributes` ran on the Unified Comfort hmiB and
on hmiA without touching `ConfirmationType`. `GetAttributeInfos` lists `ConfirmationType` (ReadWrite) without
reading it. The denylist stays.

### Q3 — Discrete alarm trigger

`RaisedStateTag` takes an **HMI tag name**. `TriggerBitAddress` is derived and read-only: `AI_Temperature_T1.x0`,
then `.x3` after `RaisedStateTagBitNumber = 3`; for an internal trigger tag it is the bare bit number (`1`).
Bit 99 is rejected at set ("The trigger bit number is invalid"). A Real trigger is accepted by the setter;
`Validate()` reports "The data type of the tag is not permitted for this type of alarm". `TriggerMode`
(`OnRisingEdge`, `OnFallingEdge`), `AlarmClass` and `Priority` (Byte) set and read back. Defaults: class
`Alarm`, trigger `<No tag>`, area `<software>::Alarming`.

### Q4 — Tag tables

Create at root and in a group work. Names are unique across groups and case-insensitive; a rename into an
existing name fails "The name … is not unique". Deleting a table deletes its tags; deleting a group deletes its
tables and their tags. The default table cannot be deleted but can be renamed, and tableless `Create` lands in
it. **No Openness member identifies the default table**; the planner can only know it by name or by probing.

### Q5 — Tag names

`Name` is writable; renaming into an existing name fails "The tag name already exists". Names are unique
across all tables of the HMI and case-insensitive (`zw…_b` collides with `ZW…_B`). `Find` matches
case-insensitively, so exact-name verification must compare the returned name. Openness accepts space, dot,
slash, double quote and non-ASCII in tag names.

### Q6 — Analog alarms

`Condition` accepts all six values (`LowerLimit`, `UpperLimit`, `Equal`, `NotEqual`, `LowerLimitOrEqual`,
`UpperLimitOrEqual`). `ConditionValue` accepts `Int32` (read back `50`) and `Double` (`50.5`); `Single`, numeric
strings and a tag name are rejected ("The type of the argument 'ConditionValue' (System.String) is invalid").
`TriggerAddress` is derived from the trigger tag's PLC tag. A new analog alarm without a trigger reports
"The trigger value is invalid" for `ConditionValue`. Discrete and analog alarms have separate name scopes
(an analog alarm may share a discrete alarm's name); within one kind names are unique.

### Q7 — Dangling references

`Connection`, `AlarmClass` and `RaisedStateTag` accept a missing name and `Validate()` reports
"The object \"X\" does not exist. Please update or delete the reference." A missing PLC tag is rejected by the
setter. Clearing `PlcTag` then `Connection` (`""`) turns a bound tag back into an internal tag
(`<Internal tag>`), `Validate()` clean. `AccessMode` and `AcquisitionMode` reject `None`.

### Q8 — Connection auto-creation and binding (added 2026-10-13)

Sixteen spellings on hmiA (global Bool and Int tags, DB Real member, UDT instance, DB Bool member, plain
struct; quoted and unquoted) all failed "The controller tag … was not found"; no connection appeared, the
network snapshot (subnets, interface nodes, hardware connections) and the PLC tag's `External*` flags were
unchanged. On hmiB, `Connection` + `PlcTag` succeeded for every spelling; without `Connection`, `PlcTag`
failed the same way. **Connection creation is deferred; v1 requires an existing connection.**

### Q9 — Deleting a referenced tag

The installed `read_cross_references {hmi}` reported `ZW7_al` (discrete) and `ZW7_an` (analog) as `UsedBy`
via "Property.Trigger tag" for their trigger tags. Deleting the tag, or the table holding it, succeeds and
leaves the alarm's `RaisedStateTag` pointing at the missing name; `Validate()` then reports "The object … does
not exist". Deletes do not cascade to alarms.

### Q10 — `SetAttributes`

The `AttributeDelegate` signature is `void (AttributeConfiguration)`. A bad value type (`AccessMode` as a
string) failed the whole call before any attribute was applied, with or without a handler, and the handler was
never called; a bad name failed with `EngineeringNotSupportedException`, also with nothing applied. Semantic
failures (for example a missing PLC tag) inside `SetAttributes` were not probed. Typed setters in a fixed
order give per-property errors; `SetAttributes` gives none.

### Q11 — Cost

On hmiB, 50 tags: create 2.3 s, bind (`Connection` + `PlcTag`) 4.5 s, `Validate` 0.9 s, delete 22.6 s, no
errors. One device walk to locate the HMIs took 1.9–2.3 s on this 4-device project.

## Evidence gaps

- Editor rendering of `DisplayName`, and of `EventText1`–`EventText9` (not written).
- `SetAttributes` with a semantically invalid value; `Validate()` warnings (none seen).
- hmiA's device type was not recorded; hmiB is the Unified Comfort panel. No Unified PC station.
- The connection layers (`HmiConnectionComposition.Create`, hardware `ConnectionComposition.Create<HmiConnection>`)
  were not probed, by decision.
