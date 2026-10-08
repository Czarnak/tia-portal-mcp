# `hmi_read` — WinCC Unified read tool design

*2026-10-07. Source analysis: `priv/HMI_UNIFIED_OPERATIONS_ANALYSIS.md` (its §0 overrides its body).*

## 1. Intent

Give an agent a typed, read-only view of a WinCC Unified HMI's engineering model — devices, tags,
connections, alarms, logs, screens, runtime settings, list and module names, languages and
validation results — through one batched tool that follows the `plc_read` pattern.

**In scope:** the analysis §4 Phase 1 read list plus §0's cheap read additions (screen navigation
graph, extended alarm/tag properties), WinCC Unified only.

**Out of scope (decided):**

- SiVArc in every form (separate TIA module; not considered at this stage).
- Plant model / `PlantViewsProvider`.
- YAML/JS export-backed content: text/graphic list entries, script-module source, bulk tag export.
- Dynamizations, event handlers and property-event scripts.
- Generic `GetAttributeInfos` attribute dump.
- Unified compile (not a read; provider unknown) and download.
- Hardware-layer HMI connections — `network_read` already lists communication connections.
- Classic WinCC object reads (Classic devices are discovered and then rejected).
- Cross-references inside `hmi_read`. They ship as PR B: a WinCC Unified `HmiTag` owner kind in
  the existing `read_cross_references` (already verified live, spec 2026-10-05 Appendix B).

**Success:** all 20 operations pass offline gates and a live V21 acceptance run on a disposable
project with one Unified Comfort panel and one Unified PC station; every response is canonical
typed JSON within the existing budgets.

## 2. Public contract

### 2.1 Tool

`hmi_read(operations)` — `ReadOnly=true`, `Destructive=false`, structured output, contract version
`"1.0"`. 1–50 items, unique `operationId` (≤256 chars), items run independently (a failing item
does not stop later items). A batch that ran returns `isError:false` with per-item status; a
request-shape rejection returns a root `error` with `isError:true`. Available in every access
mode. Tool counts become 7 / 16 / 16 (read-only / read-write / full).

### 2.2 Common item fields

| Field | Applies to | Rule |
| --- | --- | --- |
| `operationId`, `operation` | all | required |
| `projectPath` | all | optional, same semantics as `plc_read` |
| `hmiName` | all except `list_hmi_devices`, `list_project_languages` | optional; case-insensitive match on software name or device name; may be omitted only when the project has exactly one Unified HMI, otherwise `target_ambiguous` |
| `language` | ops returning texts | optional culture name (e.g. `en-US`); narrows every text array to that culture |
| `offset`, `limit` | paged ops | `offset` ≥ 0 (default 0); `limit` 1–2000 (default 500, final default set by the spike) |

Undeclared fields for an operation are rejected, as in `PlcOperationCatalog`. `groupPath` is a
`/`-separated list of group names below the root collection (exact, case-sensitive). In
`validate`, `name` narrows to one object of the given `category` and is rejected with
`category:"all"`.

### 2.3 Operations

| # | Operation | Specific fields | Result |
| --- | --- | --- | --- |
| 1 | `list_hmi_devices` | — | devices: name, software name, `kind` (`unified`\|`classic`), type identifier |
| 2 | `list_tag_tables` | opt `groupPath` | group/table tree with tag counts |
| 3 | `list_tags` | opt `tableName`; paged | name, table, connection, PLC tag, data type, address, acquisition mode/cycle, access mode, persistent, linear-scaling enabled, comment |
| 4 | `get_tag` | `tagName` | #3 row + members (recursive, depth cap 8, `membersTruncated`), thresholds, logging tags, upper/lower range, substitute value, `InitialValue`/`HmiStartValue`/`HmiEndValue`/`PlcStartValue`/`PlcEndValue` as variants |
| 5 | `list_system_tags` | paged | name, data type |
| 6 | `list_connections` | — | name, communication driver, `initialAddress` (raw + parsed key/value map), partner, station, node, disabled-at-startup, comment, driver properties |
| 7 | `list_alarms` | opt `alarmKind` (`discrete`\|`analog`); paged | kind, name, class, priority, trigger tag + bit, condition/value, acknowledgment tags, trigger mode, `eventText`, `infoText` |
| 8 | `get_alarm` | `alarmName`, `alarmKind` | #7 row + `eventText1`…`eventText9`, alarm parameter tags (variants) |
| 9 | `list_alarm_classes` | — | `alarmClasses[]`, `auditClasses[]`, `opcUaAlarmTypes[]` |
| 10 | `list_logs` | — | data logs, alarm logs, audit trails with settings, segment and backup |
| 11 | `list_logging_tags` | opt `dataLogName`, `tagName`; paged | logging tag, owning tag, data log, logging mode, cycle, trigger mode |
| 12 | `list_screens` | opt `groupPath` | screen-group tree with screen scalars and item counts |
| 13 | `list_screen_items` | `screenName`; paged | name, CLR type name, left/top/width/height, visible, enabled |
| 14 | `list_faceplate_instances` | opt `screenName`; paged | screen, container name, `containedType`, interface bindings (property name + variant value) |
| 15 | `get_screen_navigation` | — | `startScreen`, edges `{fromScreen, viaItem, toScreen}` from screen windows |
| 16 | `get_runtime_settings` | — | runtime settings; device-dependent sub-objects are nullable |
| 17 | `list_script_modules` | — | module names |
| 18 | `list_text_and_graphic_lists` | — | text, graphic and system text list names |
| 19 | `list_project_languages` | — (project scope) | project languages, editing language, reference language |
| 20 | `validate` | `category` (`tags`\|`alarms`\|`screens`\|`connections`\|`logs`\|`alarmClasses`\|`all`), opt `name`; paged | scanned count, clean count, objects with errors/warnings (`propertyName`, `errors[]`, `warnings[]`), `notValidatable[]` object kinds |

### 2.4 Shared value shapes

- **Text:** `HmiText { culture, text }`; every multilingual property is `HmiText[]`. Without
  `language`, all project languages are returned. Whether formatted-text markup is returned raw or
  stripped is fixed by spike finding S5.
- **Variant:** `HmiVariant { type, value }` for `System.Object` properties — `type` is the CLR
  type name or null, `value` is a JSON scalar/string or null. The exact encoding table is fixed by
  spike finding S3.
- **Page:** `HmiPage { offset, limit, total, nextOffset }`; results ordered by name (ordinal,
  case-insensitive); `nextOffset` is null on the last page. An `offset` past `total` returns an
  empty page, not an error. Paging is stateless — a project change between pages shows as a
  changed `total`.

### 2.5 Errors

Request rejection (root `error`, `isError:true`): `validation_error` for unknown operation,
undeclared/missing field, duplicate or overlong `operationId`, more than 50 items, malformed
culture, `limit` outside 1–2000, negative `offset`, unknown `category`/`alarmKind`;
`access_denied` if a mode ever disallows an operation.

Item failure (other items continue): `target_not_found` (HMI, tag, alarm, screen, group, table),
`target_ambiguous`, `target_kind_unsupported` (Classic device), `worker_operation_failed`
(Openness exception, including a license exception, with its cause in the message),
`protocol_error` (payload does not decode as the operation's type; payload never echoed).

Oversized results use `StructuredOperationBatchPayloadBudget` (60,000 per value, 180,000 per
document) with per-operation retry guidance: lower `limit`, add a narrowing field, or split the
batch.

## 3. Architecture

### 3.1 Contracts (`TiaMcpServer.Contracts/Hmi/`)

`HmiOperationRequest` (wire item) and one result DTO per operation, plus `HmiText`, `HmiVariant`,
`HmiPage`. Payloads are serialized with `WorkerJson.SerializePayload`, write explicit nulls and
are decoded through `CanonicalJson.DeserializeWorkerPayload`. No `[LegacyNullOmission]`. Any
conditional member is registered in `ConditionalMemberRegisterTests` with its condition.

### 3.2 Policy

Operation policy names are global and flat (`OperationPolicyCatalog`), and PLC already owns
`list_tag_tables`, `create_tag`, `update_tag`, `delete_tag`. HMI worker methods are therefore
named `hmi_<operation>` and registered as `OperationCapability.Observe`. Public operation names
inside `hmi_read` stay unprefixed; `HmiOperationCatalog` holds the single public→worker map.

### 3.3 Host (`TiaMcpServer/Hmi/`)

Clone of `Plc/` read side: `HmiReadTools`, `HmiOperationCatalog` (specs, field/culture/paging/
enum validation, access mode), `HmiWorkerInvoker`, `HmiPayloadContract` (one CLR type per
operation), `HmiContractVersion`, `HmiToolResponses`. Execution reuses
`StructuredOperationBatchExecutionEngine.ExecuteReadsAsync`, `StructuredToolResult` and
`StructuredOperationBatchPayloadBudget` unchanged. New host files are added to
`TiaMcpServer.Tests.csproj`; the tool gets success and rejection probes in
`ToolOutputContractConformanceTests`.

### 3.4 Worker (`TiaMcpServer.OpennessWorker/Openness/Hmi/`)

- `HmiSoftwareLocator.FindUnique(project, hmiName)` — mirrors `PlcSoftwareLocator`: strict
  device walk via `ProjectDeviceEnumerator`, `SoftwareContainer.Software as HmiSoftware`.
  Classic software is recognised by runtime type name (`Siemens.Engineering.Hmi.HmiTarget`), so
  `Siemens.Engineering.WinCC.dll` is not referenced.
- Readers by area: `HmiTagReader` (2, 3, 4, 5, 11), `HmiConnectionReader` (6), `HmiAlarmReader`
  (7, 8, 9), `HmiLogReader` (10), `HmiScreenReader` (12–15), `HmiSoftwareInfoReader` (1, 16–19),
  `HmiValidationReader` (20).
- Shared helpers: `HmiTextMapper`, `HmiVariantMapper`, `HmiPager`, `HmiInitialAddressParser`.
- Reads **enumerate** compositions and never call `Find`, so they do not depend on how the
  license gate treats `Find`.
- Each operation is one `"hmi_…"` case in `Program.cs`, using the existing `WithProject` /
  `Execute`. Reads never bind, switch, open or save a project.

### 3.5 Build

New `reference-stubs/Siemens.Engineering.WinCCUnified/` with minimal declarations of only the
types the readers use, generated `ref/Siemens.Engineering.WinCCUnified.dll` (version `21.0.0.0`,
same public-only key), `verify-reference-stubs.ps1` extended from two to three DLLs, and a worker
`<Reference>` with updated `Exists` conditions and error text. The real assembly is present at
`…\PublicAPI\V21\net48\Siemens.Engineering.WinCCUnified.dll` on the dev machine.

## 4. Live spike (Task 0)

A throwaway probe (not committed) against a disposable project copy with one Unified Comfort panel
and one Unified PC station. Findings go into an acceptance report under
`docs/superpowers/acceptance/reports/`. Tasks that depend on a finding wait for the report;
plumbing, contracts and host work do not.

| ID | Question | Decides |
| --- | --- | --- |
| S1 | License state; do enumeration and `Validate()` work under it? | error text, feasibility |
| S2 | Cold cost of 500 tags (core vs full row) and one screen's items | default `limit`, row split |
| S3 | Real CLR types behind the five tag `Object` properties, `AlarmParameterTags`, `HmiFaceplateInterface.Value` | variant encoding |
| S4 | Runtime-setting sub-objects on Comfort: null or exception? | nullability handling |
| S5 | Do tag/alarm texts carry formatted-text markup? | raw vs stripped text |
| S6 | `Validate()` result on a known-bad object; which areas lack it | `validate` shape, `notValidatable` |
| S7 | Types of `HmiScreenWindow.Screen` and `StartScreen` | navigation edges |
| S8 | `InitialAddress` format | parser |
| S9 | UDT member nesting depth seen in practice | depth cap |
| S10 | Classic software runtime type name (if a Classic device is available) | Classic detection |

## 5. Verification

- **Host tests:** table-driven catalog validation per operation; public→worker map; all `hmi_*`
  are `Observe` and allowed read-only; payload decode accept/reject per operation; retry
  guidance; conformance probes.
- **Worker tests:** readers linked into `TiaMcpServer.Tests` against new
  `HmiUnifiedSiemensDoubles` — locator (none/one/many/Classic/unreadable item), pager, text
  filter, variant mapper, `InitialAddress` parser, member depth cap, nullable runtime
  sub-objects.
- **IPC:** one FakeWorker round trip of a mixed `hmi_read` batch with one failing item.
- **Build:** stub verifier (three DLLs), stub build (`UseTiaPortalReferenceStubs=true`),
  real-assembly build, `dotnet test`, coverage ≥ 80% on new code.
- **Live acceptance:** all 20 operations on the spike project plus unknown HMI, ambiguous HMI,
  Classic (if available), offset past end, language filter and a forced omission; run through the
  installed tool, not only the repo build. A stub build, FakeWorker run or contract test is not
  evidence of live behavior.

## 6. Delivery

- **PR A — `hmi_read`:** spike → stubs → contracts + policy → host → worker readers area by area →
  FakeWorker → live acceptance → documentation (last: `HMI_OPERATIONS_SUMMARY.md`, `README.md`
  tool list/counts with absolute links, `AGENTS.md` counts, `docs/ARCHITECTURE.md`,
  `docs/README.md`, `docs/development/building.md`).
- **PR B — cross-references:** WinCC Unified `HmiTag` owner kind in `read_cross_references`, with
  its own short addendum and live check.

## 7. Risks

- License gate may block reads entirely on machines without a Unified engineering license (S1).
- Remoting cost per property may make full tag rows slow on large projects (S2); mitigated by
  paging and the core/detail split.
- Device- and version-dependent runtime settings (Unified PC V20+ only objects) — nullable DTOs.
- Formatted-text markup in texts could bloat payloads (S5).

## Corrections after implementation (2026-10-08)

The sections above are the historical design. Where the implementation differs, the code and the
[HMI operations summary](../../SupportedOperations/HMI_OPERATIONS_SUMMARY.md) win:

- **D8 - completeness everywhere.** Every result carries `isComplete` and `messages`. An unreadable
  property becomes null plus a message and `isComplete:false`; an unreadable composition fails the item
  `worker_operation_failed`.
- **D9 - `validate` paging.** Pages over scanned objects (bounded cost per call); `total` is the category
  object count; clean objects are counted, not listed. Scope excludes member tags, logging tags and
  screen items; system tags are counted but listed as `notValidatable`.
- **D10 - `HmiVariant.value`** is a real JSON scalar, not a JSON-in-string, per the spike encoding table.
- **Non-project language (Review Focus 3).** A `language` filter that is not a project culture fails the
  item `target_not_found` and names the project languages.
- **Default limit** is 100 (the spike lowered it from 500); maximum 2000.
- **`list_connections` has no `language`**; connection comments are plain strings.
- **`list_alarm_classes` has no `language`** (final review I2): alarm classes, audit classes and OPC UA
  alarm types carry no texts, so the field is rejected. The live acceptance runs that passed `language`
  to it predate this change.
- **Password omission.** Connection driver properties whose name contains "password" are never returned.
- **Secret-name denylist** (final review I3, refines the password omission). One case-insensitive check
  (`password`, `passwd`, `passphrase`, `pwd`, `secret`, `token`, `credential`, `privatekey`) drops
  driver properties before their value is read and drops matching `InitialAddress` parsed keys;
  `initialAddress.raw` is null whenever the address names one.