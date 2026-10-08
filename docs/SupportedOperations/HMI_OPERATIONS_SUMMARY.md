# TIA Portal HMI Operations

`hmi_read` is the read-only WinCC Unified HMI tool. It is registered in every access mode (counts
7/16/16), never writes, never compiles, and never opens, saves or closes a project. It runs up to
50 items per call on the structured JSON contract (`contractVersion:"1.0"`); items run
independently, so a failing item does not stop later ones. Design background lives in the
[hmi_read design spec](../superpowers/specs/2026-10-07-hmi-read-design.md), the
[spike report](../superpowers/acceptance/reports/2026-10-07-hmi-read-spike.md) and the
[live acceptance report](../superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md).

## Operations

Every operation except `list_hmi_devices` and `list_project_languages` takes an optional `hmiName`
(software or device name); omit it only when the project has exactly one Unified HMI. Each item
carries a unique `operationId` (at most 256 characters) and an `operation`. Fields outside an
operation's list are rejected before any worker call.

| Operation | Required | Optional | Paged | Returns |
|---|---|---|---|---|
| `list_hmi_devices` | - | - | no | Unified and Classic HMI devices in the project |
| `list_tag_tables` | - | `hmiName`, `groupPath` | no | Tag-table and group tree |
| `list_tags` | - | `hmiName`, `tableName`, `language` | yes | Tag rows (core fields) |
| `get_tag` | `tagName` | `hmiName`, `language` | no | One tag with detail: members, thresholds, ranges, substitute values |
| `list_system_tags` | - | `hmiName` | yes | System tags |
| `list_connections` | - | `hmiName` | no | Connections, driver properties, initial addresses |
| `list_alarms` | - | `hmiName`, `alarmKind`, `language` | yes | Discrete or analog alarm rows |
| `get_alarm` | `alarmName`, `alarmKind` | `hmiName`, `language` | no | One alarm with detail |
| `list_alarm_classes` | - | `hmiName` | no | Alarm classes, audit classes, OPC UA alarm types |
| `list_logs` | - | `hmiName` | no | Data logs and alarm logs with settings, segments, backup |
| `list_logging_tags` | - | `hmiName`, `dataLogName`, `tagName` | yes | Logging tags |
| `list_screens` | - | `hmiName`, `groupPath`, `language` | no | Screen group tree with screens |
| `list_screen_items` | `screenName` | `hmiName` | yes | Items on one screen |
| `list_faceplate_instances` | - | `hmiName`, `screenName` | yes | Faceplate instances and their bindings |
| `get_screen_navigation` | - | `hmiName` | no | Navigation edges between screens |
| `get_runtime_settings` | - | `hmiName` | no | Runtime settings sections |
| `list_script_modules` | - | `hmiName` | no | JavaScript script modules |
| `list_text_and_graphic_lists` | - | `hmiName` | no | Text lists and graphic lists |
| `list_project_languages` | - | - | no | Project languages |
| `validate` | `category` | `hmiName`, `name` | yes | Openness validation findings |

`alarmKind` is `discrete` or `analog`. `validate` takes `category` `tags`, `alarms`, `screens`,
`connections`, `logs`, `alarmClasses` or `all`; `name` narrows to one object and is not valid with
`all`. `language` is a culture name such as `en-US`; `list_connections` and `list_alarm_classes`
have no `language` because connection comments are plain strings and alarm classes carry no texts.

## Result conventions

- **Paging.** Paged operations take `offset` (0 or greater, default 0) and `limit` (1-2000, default
  100) and return `page { offset, limit, total, nextOffset }`. Order is case-insensitive ordinal with
  a case-sensitive tie-break. Paging is stateless: each page re-scans the project.
- **Completeness.** Every result carries `isComplete` and `messages`. A property Openness cannot read
  becomes `null` with a message and `isComplete:false`. An unreadable composition fails the item with
  `worker_operation_failed`. Per-property recovery applies only to
  `EngineeringTargetInvocationException` and `EngineeringNotSupportedException`; a dead portal fails
  the item.
- **Values.** `HmiVariant.value` is a real JSON scalar, never JSON inside a string. Texts are
  `HmiText` arrays of `{culture, text}` and are returned raw: alarm event texts keep their
  `<body><p>...</p></body>` markup. A `language` filter naming a culture that is not a project
  language fails the item `target_not_found` and names the project languages.
- **Budgets.** A complete response is capped at 180,000 characters and one value at 60,000; larger
  values are omitted whole with narrowing guidance (lower `limit`, add a narrowing field, or re-run
  the `operationId` alone).
- **Errors.** Classic WinCC (`Siemens.Engineering.Hmi.HmiTarget`) devices are listed by
  `list_hmi_devices` but every other operation fails the item `target_kind_unsupported`. Selector
  misses are `target_not_found` or `target_ambiguous`.

## Operation notes

- **`get_tag` members.** Members are read recursively to depth 8 and at most 100 member rows in
  all (every level combined). Hitting either cap sets `membersTruncated:true`; the row cap also adds
  one message. Neither cap makes the result incomplete.
- **`validate`.** Pages over *scanned* objects so the cost of one call is bounded; `total` is the
  number of objects in the category. Clean objects are counted, not listed. Scope excludes member
  tags, logging tags and screen items. System tags are counted but listed as `notValidatable`. An
  unreadable validation result is never counted clean.
- **`get_runtime_settings`.** Setting values are strings. The OpcUaServer section carries a subset
  of its settings. Comfort panels return `isComplete:false` because `GMPEnabled` and
  `GeneralESIGCommentsStrategy` are not supported on the device (PC stations: `TagOptimizationActive`).
  This is expected, not a defect.
- **Screen items.** Geometry comes from `IHmiBoxFeature`; items without box geometry (for example
  elliptical arcs) report null geometry with no message. A screen window without a target screen
  yields no navigation edge and a message (`isComplete` stays true).
- **Connections.** Secrets never leave the worker. A driver property whose name contains a
  credential-like word (`password`, `passwd`, `passphrase`, `pwd`, `secret`, `token`, `credential`,
  `privatekey`, any case) is dropped before its value is read, and a property whose name cannot be
  read has its value left unread. The same check drops matching `initialAddress.parsed` keys and sets
  `initialAddress.raw` to null when the address names one. Duplicate (name, value) rows reported by
  Openness are collapsed.
- **Never read.** `HmiTag.ConfirmationType` (it crashed TIA Portal in the spike), and
  `GmpRelevant` / `MandatoryCommenting` (unknown hazard).

## Known limits

- Whole-project scans take roughly 21-23 s per call on a large PC station regardless of `limit`.
- Non-integral doubles were not exercised live on net48 (offline test pins the net10 formatting).
- Live evidence gaps: analog alarms, non-default connection drivers, tag comments, live validation
  errors and warnings, non-default Object values.

See the [improvement log](../IMPROVEMENT_LOG.md#open-hmi_read-follow-ups-2026-10-08) for follow-ups.

## Out of scope

SiVArc, plant views, YAML or JavaScript export content, dynamizations and events, a generic attribute
dump, HMI compile, hardware-layer connections, and reads of Classic WinCC objects. There are no HMI
writes. `compile_check` keeps its PLC-only contract.

Related: [README.md](README.md) for the operation index and
[DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) for hardware information.
