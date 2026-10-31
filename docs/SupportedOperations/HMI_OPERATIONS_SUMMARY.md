# TIA Portal HMI Operations

`hmi_read` is the read-only WinCC Unified HMI tool. It is registered in every access mode (counts
8/19/19), never writes, never compiles, and never opens, saves or closes a project. It runs up to
50 items per call on the structured JSON contract (`contractVersion:"1.1"`); items run
independently, so a failing item does not stop later ones. Design background lives in the
[hmi_read design spec](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-07-hmi-read-design.md), the
[spike report](../superpowers/acceptance/reports/2026-10-07-hmi-read-spike.md), the
[live acceptance report](../superpowers/acceptance/reports/2026-10-08-hmi-read-live-acceptance.md),
the [completion spec](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/specs/2026-10-11-hmi-read-completion-design.md) and the
[completion live acceptance report](../superpowers/acceptance/reports/2026-10-12-hmi-read-completion-live-acceptance.md).

`hmi_write` separately provides nine tag-table, tag and alarm writes in writable modes on standalone
projects and bound local sessions. The [write acceptance report](../superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md)
qualifies the exact production source/package, launched side by side without global installation.

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
`all`. The name-scoped categories `memberTags`, `loggingTags` and `screenItems` require `name` (see
[validate](#operation-notes)) and are not part of `all`. `language` is a culture name such as `en-US`; `list_connections` and `list_alarm_classes`
have no `language` because connection comments are plain strings and alarm classes carry no texts.

## Result conventions

- **Tag and alarm rows.** `list_tags`, `get_tag` and recursive member rows include `displayName`
  text arrays and `plcName`. `list_alarms` and `get_alarm` include `triggerBitAddress` for discrete
  alarms and `triggerAddress` for analog alarms. Unreadable/inapplicable members are explicit nulls;
  these additions keep the read contract at `1.1`.
- **Paging.** Paged operations take `offset` (0 or greater, default 0) and `limit` (1-2000, default
  100) and return `page { offset, limit, total, nextOffset, nextCursor }`. Order is case-insensitive
  ordinal with a case-sensitive tie-break. A call without `cursor` re-scans the project; see
  [Cursor paging](#cursor-paging) for continuing one scan.
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
  `list_hmi_devices` but every other operation fails the item `target_kind_unsupported` ("... is a
  Classic (WinCC) HMI; only WinCC Unified HMIs are supported."). This also applies when `hmiName` is
  omitted and the project has no Unified HMI but at least one Classic HMI. Selector misses are
  `target_not_found` or `target_ambiguous`.

## Cursor paging

The seven paged operations (`list_tags`, `list_system_tags`, `list_alarms`, `list_logging_tags`,
`list_screen_items`, `list_faceplate_instances`, `validate`) return `page.nextCursor` whenever
`nextOffset` is set, and null on the last page. Pass it back as `cursor`, instead of `offset`, with the
same operation and filters to continue the same scan. `limit` may differ between pages. Offset paging
stays fully supported; a call without `cursor` behaves as before and re-scans.

- **Cost.** The worker holds the name-ordered objects of the first scan, so a continuation reads only
  its own rows. Live: 1,286 faceplate instances on a Unified PC took 21 s for the first page and
  0.5-0.8 s per continuation; an offset-only page took 21 s.
- **Staleness.** The object list is frozen when the first page is read; every row is read live. An
  object deleted since then fails its own row with a message and `isComplete:false`; the other rows
  are unaffected. Objects added since then are not included; a call without `cursor` sees them.
- **Lifetime.** The worker keeps at most 4 snapshots, least recently used evicted first, each dropped
  after 10 minutes without a continuation. Every scan with more rows than its page takes a slot,
  including offset-only calls, so a batch with more than 4 multi-page items, or other paged calls in
  between, can evict a cursor that is still in use.
- **Failures.** `cursor` together with `offset`, or `cursor` on an unpaged operation, is a
  `validation_error` that rejects the whole call, like any other field error. Every other cursor
  failure fails only its item. Checks before any worker call:

  | Condition | Category |
  |---|---|
  | blank, malformed or tampered cursor, or one issued by an earlier server process | `invalid_cursor` |
  | different operation or any filter (`hmiName`, `tableName`, `language`, `alarmKind`, `dataLogName`, `tagName`, `screenName`, `category`, `name`) changed, including only its case | `cursor_filter_mismatch` |
  | project binding changed since the cursor was issued | `cursor_binding_mismatch` |

  In the worker, an expired or evicted snapshot is `snapshot_unavailable`, and a snapshot taken from
  another project or operation is `cursor_snapshot_mismatch`. Restart without a cursor in each case.
- **Worker restart.** A worker restart loses every snapshot, but it also invalidates the project
  binding, so the next continuation fails `binding_conflict`, not `snapshot_unavailable`. After
  `bind_project` the old cursor fails `cursor_binding_mismatch`; a call without a cursor recovers.
- **Local sessions.** Cursor paging works on a cold-adopted `.als21` local session (live, 2026-10-12).

## Operation notes

- **`get_tag` members.** Members are read recursively to depth 8 and at most 100 member rows in
  all (every level combined). Hitting either cap sets `membersTruncated:true`; the row cap also adds
  one message. Neither cap makes the result incomplete.
- **`validate`.** Pages over *scanned* objects so the cost of one call is bounded; `total` is the
  number of objects in the category. Clean objects are counted, not listed; each finding carries
  `objectKind` (the Openness type name, such as `HmiIOField`), `name` and `results[]` of
  `{propertyName, errors[], warnings[]}`. Objects without `Validate()` are listed once per kind in
  `notValidatable`. System tags are not scanned or counted (`tags` and `all`). An unreadable
  validation result is never counted clean. Three name-scoped categories reach objects the others
  skip; `name` is required, and they are not part of `all`:

  | Category | `name` | Validates | Finding `name` |
  |---|---|---|---|
  | `memberTags` | top-level tag | every member, recursively to depth 8 | `Motor_1.Speed` |
  | `loggingTags` | top-level tag | the tag's logging tags | `Temp_1/Log_1` |
  | `screenItems` | screen | every item on that screen | `Main/Button_3` |

  Live example: an IO field bound to a deleted tag is reported under `screenItems` as
  `objectKind:"HmiIOField"`, `propertyName:"Dynamization"`, with the error "The tag '...' for
  dynamization of the property 'Process value' does not exist."
- **`list_logging_tags` with `dataLogName`.** A logging tag whose `DataLog` cannot be read is left
  out of the scan with a message and `isComplete:false`. The message comes from the scanning call;
  cursor continuations do not scan again and do not repeat it.
- **`get_runtime_settings`.** Setting values are strings. The OpcUaServer section carries a subset
  of its settings. `GMPEnabled`, `GeneralESIGCommentsStrategy` and `TagOptimizationActive` are on an
  allow-list: when Openness reports one as unsupported on the device, the value is null with a note
  and `isComplete` stays true. Any other unreadable setting still sets `isComplete:false`. Whether
  the GMP and ESIG settings read depends on the device; some Comfort panels never return them.
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
  `GmpRelevant` / `MandatoryCommenting`. These stay permanently unread; there is no probe.

## Writes (`hmi_write`)

`hmi_write(operations, dryRun=false)` executes by default against one verified, already-open project.
Set `dryRun:true` for preview. Each of 1–50 caller-ordered items carries a unique `operationId`
(at most 256 characters), `operation`, and an optional `hmiName`. HMI selection follows
the read rules; all items target the bound project. Unknown fields (including a retired item `projectPath`) fail
validation before worker activity. Read-only mode neither registers the tool nor permits its nine
worker methods.

| Operation | Required | Optional |
| --- | --- | --- |
| `create_tag_table` | `tableName` | `groupPath` |
| `update_tag_table` | `tableName`, `newName` | – |
| `delete_tag_table` | `tableName` | – |
| `create_tag` | `tableName`, `tagName`, and either `plcTag` or `dataType` | `connection` (with `plcTag`), `comment`, `displayName`, `acquisitionCycle` |
| `update_tag` | `tagName` | `newName`, `plcTag`, `connection` (with `plcTag`), `dataType`, `comment`, `displayName`, `acquisitionCycle` |
| `delete_tag` | `tagName` | – |
| `create_alarm` | `alarmKind`, `alarmName`, `triggerTag`; analog also `condition`, `conditionValue` | `alarmClass` (default `Alarm`), `priority`, `eventText`, `infoText`; discrete only `bitNumber` (default 0), `triggerMode` |
| `update_alarm` | `alarmKind`, `alarmName` | `newName` and create fields valid for that kind |
| `delete_alarm` | `alarmKind`, `alarmName` | – |

### Fields and dependencies

- **Binding.** Request shape requires `plcTag` when `connection` is supplied; a create needs
  `plcTag` or `dataType`. The host does not preflight connection existence, require a bound tag
  for `plcTag`, or refuse `dataType` on a bound tag. Siemens decides those semantic cases during
  mutation; binding does not create connection objects. Quoted (`"DB".Member`) and unquoted PLC
  paths normalize to Siemens' unquoted dotted form. A preview cannot prove native acceptance or
  PLC-source existence.
- **Names.** Tags are HMI-wide, tables span all groups, alarms are scoped by kind. `tableName` places
  a new tag; existing tags use `tagName` alone. New names cannot be blank, padded, or contain controls,
  `.` or `"`. Existing selectors match case-insensitively. Create/rename collisions go to Siemens;
  use a public read to inspect the resulting spelling and object identity.
- **Values.** `acquisitionCycle` is `T100ms`, `T250ms`, `T500ms`, `T1s`, `T2s`, `T5s` or `T10s`.
  Priority is 0–255. Discrete `bitNumber` is at least zero, with its native type-specific upper bound;
  `triggerMode` is `OnRisingEdge` or `OnFallingEdge`. Analog `condition` is `LowerLimit`, `UpperLimit`,
  `Equal`, `NotEqual`, `LowerLimitOrEqual` or `UpperLimitOrEqual`. Finite JSON `conditionValue` uses Int32
  setter input for integral values in Int32 range (including `50.0`), otherwise Double.
- **Members and references.** `triggerTag` accepts a tag or recursive `Tag.Member` path. The host
  does not prove member, trigger-tag or alarm-class existence before mutation. There is no
  rebind/read/second-call restriction for a newly introduced member. Siemens can accept an invalid
  reference; read the affected objects, run public `validate`, and compile the HMI device in the
  TIA Portal UI. These checks are separate from write verification.
- **Texts.** `comment`, `displayName`, `eventText` and `infoText` are nonempty arrays of `{culture,text}`;
  only supplied cultures change. Duplicate cultures are `validation_error`; an unconfigured language
  is `target_not_found`. Send plain text: input starting with `<body` is rejected; EventText line
  breaks are rejected. The server escapes XML text ampersands/angle brackets, preserves literal
  quotes and wraps event text as `<body><p>…</p></body>`. Literal entities remain literal text.
  Other text fields are written as given. Effects use scalar `field[culture]` values; raw readback
  markup is not write input.

### Guards and outcomes

| Guard | Level | Fires when |
| --- | --- | --- |
| `hmi_state_unverifiable` | block | Required evidence is incomplete or initial delete planning needs over 200 cross-reference queries. |
| `hmi_deletes_table_contents` | info | Deleting a table also deletes its inventoried tags. |
| `hmi_deletes_referenced_tag` | info | Deleted tags have inventoried alarm/screen/script users; references can remain dangling. |

Planning gathers one initial public-read evidence set per resolved HMI for update/delete targets
and delete consequences through sequential, non-atomic reads, then simulates effects in caller
order. The 200-query cap covers its cross-reference
reads for pre-existing top-level tags
removed by the call, including table contents. There is no per-item re-planning or worker
post-write read-back. Delete effects use initial evidence plus caller-order simulation; no
native consequence reads refresh them after mutation. Split deletes across calls if the initial
evidence would exceed the cap.

Binding-gate failures preserve their original category, including `worker_timeout`, `worker_crashed`,
`portal_lost` and `protocol_error`, in the top-level error and audit. A gate denial stops before planning
or mutation; a missing, mismatched or changed binding is `binding_conflict`.

The shared guarded pipeline pins the binding through planning, mutation, verification and audit.
There is no elicitation, confirmation argument, acknowledgment input or safety token. Execution stops
at the first failing setter/item; there is no batch rollback or automatic replay. Typed results
retain ordered `appliedProperties`, `failedProperty` and create `cleanup` (`notNeeded`, `deleted`,
`failed`, with diagnostics). `appliedProperties` lists completed stages, not an atomic change set:
a failing multi-culture `Comment` stage can already have changed an earlier culture even though
`Comment` is not listed. A failed create attempts identity-checked deletion of the new object;
cleanup can fail and leave it behind. A failed update keeps earlier changes. Inspect actual state
with public reads before retrying.

An `EngineeringTargetInvocationException` during a mutation method is `engineering_rejected`;
other Siemens `EngineeringException` failures are `worker_operation_failed`, and `portal_lost`
takes precedence. The conditional item-failure `engineeringError` contains `type`, verbatim
`message` and `inner[]`, capped at 4,000 message characters and eight inner exceptions. HMI wraps
preserve this evidence except when `portal_lost` takes precedence. A partial outcome is present
only after writer allocation; HMI resolution can fail before that point.
Worker refusals keep their category, such as `target_not_found` for a missing culture; that
category alone does not establish that nothing changed. For example, a duplicate-tag create's
captured message is:

```json
"Error when calling method 'Create' of type 'Siemens.Engineering.HmiUnified.HmiTags.HmiTagComposition'.\r\n\r\n-The value must be unique.\r\n"
```

Shared verification records completed items as `trusted` or `superseded`, without a worker
read-back or Validate call. Failed/skipped items have no verification entry. A later attempted
item can supersede an earlier success even when it fails after possible mutation, including
an outcome-bearing `target_not_found`; its verification message identifies the uncertainty.
Thus `verification.success:true` can coexist with root `success:false`. Mutations invalidate
that HMI's read snapshots; initial planning reads can also consume or evict snapshot slots.

Write responses use `contractVersion:"2.0"` (reads remain `1.1`), phases `preview`, `applied`, `blocked`, `error`, typed
effects/results/verification, explicit nulls and default 180,000-document/60,000-value character
budgets, raisable at startup.
Rejections have a top-level error; attempted mutation/verification failure has `success:false`,
`error:null`, MCP `isError:false` and can retain changes. Whole-value omissions may fail delivery
while preserving true execution summaries. Each entered call, including preview/denial, produces
one audit v2 record with exact delivered canonical text/hash. Actual confirmation is `none` in
read-write and `policy` in full.

Standalone and local-session writes use the active engineering root; they do not implicitly save,
compile, refresh from or check in to Project Server. Use `save_project` explicitly; a local save
calls `LocalSession.Save()` only.

### Write response data

The root is `{tool, contractVersion, phase, success, error, warnings, guards, effects, batch,
verification, omission}`. Text and structured content are the same canonical document.

| Value | Fields |
| --- | --- |
| `effects[]` | `operationId`, `effect`, `omission` |
| `effect` | `operation`, `target`, `changes`, `removes`, nullable `dependsOn` (presentation metadata, not a re-plan request) |
| `target` | `kind`, `hmiName`, `name`, `alarmKind`, `tableName`, `groupPath` |
| `changes[]` | `field`, `current`, `requested`, `derived` |
| `removes` | `tags[]`, `references[]`; each reference has `tag`, `name`, `typeName`, `path` |
| Item `result` | `objectName`, ordered `appliedProperties[]`, `failedProperty`, `cleanup`, `cleanupMessage` |
| `verification` | `success`, `operations[]`, `omission`; each entry has `operationId`, `success`, `check`, `expected`, `actual`, `message` |

An attempted failed item can retain its typed `result` alongside `failure`; it is not a success
result or proof of unchanged state. Preview has no mutation outcome or read-back guarantee.

### Current qualification

The [native-rejection qualification record](../superpowers/acceptance/reports/2026-10-30-hmi-write-native-rejections-live-verification.md)
records completed qualification within the recorded R1/R2/R3 scope. The fresh 35-case matrix ran on installed
`4.2.0-local.112.g96a28b7`: 31 P variants produced 21 O1 native rejections, three O2 accepted
invalid references, five accepted controls and two worker refusals, plus L1–L4. O1 error types
and messages match the native checkpoint. O2 references remained visible in public reads and
were reported by public Validate; write success does not certify their validity.

| Captured native case | Tool result / observed state |
| --- | --- |
| Duplicate table/tag/alarm or colliding rename; tag case-only rename; default-table delete | O1 `engineering_rejected`; original objects remained |
| Bound-tag `dataType`, `plcTag` without connection, or missing connection followed by `PlcTag` | O1 setter rejection with completed stages retained; failed new tags were cleaned up |
| Create tag into missing table | O1 native `Create` rejection; no fallback placement or automatic table creation |
| Missing alarm class, trigger tag or struct member | O2 accepted reference; public Validate reports the missing object; earlier checkpoint UI compile also diagnosed these cases |
| Cross-kind alarm-name reuse, table/alarm case-only rename, delete-then-create case variant | Accepted controls; intended objects/read state and public Validate matched |
| Missing table group or text culture | Worker `target_not_found`; group refusal precedes Create, while the culture refusal can follow mutation and cleanup |

The [31-variant table and canonical inputs](https://github.com/Czarnak/tia-portal-mcp/blob/docs-archive-2026-10-30/docs/superpowers/acceptance/reports/inputs/2026-10-30-hmi-write-native-rejections-live-cases.md)
retain exact outcomes, independent reads, public validation and restoration. The checkpoint UI
observations are not fresh compiles of every candidate variant.

The R1 fix's focused live check passed on installed `.113` / `31d1a44` and restored the saved
baseline. It demonstrated conservative supersession after a later partial failure. The full
offline suite passed 7,644 tests with 95.05% scoped coverage; this is separate from native/UI
qualification. Manual/local-session evidence on `.113` is accepted and complete: the maintainer
reported clean compiles for P10, P12-discrete, L1, L2 and L5; L4 produced the expected two
missing-tag errors and zero warnings. Six R3 waivers reuse the earlier checkpoint with fresh
candidate reads/Validate, rather than fresh candidate compiles.

L5's two cleanup items succeeded and its full inventory matched the local baseline: seven
tables, 72 tags and 78 alarms. `isModified` changed from false to true despite restored inventory;
the probe did not save or check in. The maintainer then reported manual cleanup and opening the
standalone project. Timing-after on `.113` is complete: root medians from three measured samples
are H1 12,117 ms, H2 128,725 ms, H3 33,934 ms, H4 34,542 ms and H5 17,824 ms. The comparison
uses installed `75c0568` as the baseline, a recorded deviation from planned `0b2640a`.
No full `.113` matrix rerun, global O3-absence proof, or induced race/fault qualification is claimed.

## Known limits

- Whole-project scans take roughly 21-23 s per call on a large PC station regardless of `limit`;
  narrow with `screenName` or `tableName` and continue with the cursor.
- Every locator call, including retained-cache hits, freshly traverses for current project
  membership and selector uniqueness. Retained entries clear on the next locator use after an
  engineering-owner change; an omitted selector does not refill a keyed entry. The maintainer kept
  the cache and left O1 open: the planned traversal saving is not delivered. The earlier roughly
  6 s per `validate screenItems` item is historical baseline evidence.
- Direct case-only tag rename can fail at the Siemens setter while preserving the original tag.
  Table/alarm case-only success does not qualify tags.
  Two explicit caller-owned renames through a unique intermediate name worked on an earlier
  candidate; there is no hidden server rename. Inspect partial state before retrying.
- Default tag-table deletion can fail natively; no public property reliably identifies it, so no
  name-based guard is used. Plain-struct binding can apply but fail validation, requiring explicit
  cleanup of the created tag.
- `get_tag` with a dotted UDT member name fails `target_not_found` without the member-path hint that
  `read_cross_references` gives.
- Floating-point values are written explicitly (`G15`, or `G17` when 15 digits do not round-trip;
  `G7`/`G9` for float), so net10 and net48 produce the same text. A tag's non-integral initial value
  arrives from Openness as a string (`System.String`), so non-integral `Double` encoding is still not
  observed live.
- Live evidence gaps: non-default connection drivers (no OPC UA connection was available), live
  validation warnings, non-default Object values, and native connection-only assignment without
  a subsequent `PlcTag` setter. Public request shape still requires `plcTag` with `connection`;
  the missing-connection probe's later `PlcTag` rejection does not qualify that isolated setter path.

See the [improvement log](../IMPROVEMENT_LOG.md#open-hmi_read-follow-ups-2026-10-12) for follow-ups.
The [earlier write acceptance report](../superpowers/acceptance/reports/2026-10-14-hmi-write-live-acceptance.md)
qualifies its historical both-mode functional verification on source `48f2edd`. Editor rendering remains one
earlier-candidate maintainer check. Performance uses an archival baseline with no write tool:
matched baseline-write comparisons and the three-repetition owner-transition gate remain
unqualified; the single owner-transition observation failed the proposed 10% memory-return condition.

## Cross-references of HMI tags

`read_cross_references` takes a provisional `{ hmi: { hmiName, tagName } }` target for one top-level
Unified HMI tag; see
[Cross-reference coverage](PLC_OPERATIONS_SUMMARY.md#cross-reference-coverage-read_cross_references).

## Out of scope

SiVArc, plant views, YAML or JavaScript export content, dynamizations and events, a generic attribute
dump, HMI compile, hardware-layer connections, and reads/writes of Classic WinCC objects.
Writes also exclude connection create/edit, tag-table group changes, tag moves/unbinding,
`accessMode`, `acquisitionMode`, acknowledgment tags, `EventText1`–`EventText9`, alarm-text parameter
fields, logging and screen changes. `compile_check` keeps its PLC-only contract.

Related: [README.md](README.md) for the operation index and
[DEVICES_OPERATIONS_SUMMARY.md](DEVICES_OPERATIONS_SUMMARY.md) for hardware information.
