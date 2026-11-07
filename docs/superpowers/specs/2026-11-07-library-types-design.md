# Library types (`library_write` PR 3) — design

Date: 2026-11-07. Status: approved 2026-11-07. Issue: Refs #218, PR 3 of N.

**This PR does not close #218.** The PR body says "Refs #218 (PR 3 of N: versioned types; …)" and lists
what remains: `update_type_instances`, `replace_master_copy`, `move_master_copy`,
`move_master_copy_folder`, library upgrade on open/retrieve, library metadata writes, project-library
writes on local sessions, Portal-only (unbound) global operations, HMI types and Software Units.

Calls marked *(my call)* were settled while writing and are open to correction.

## 1. Intent

Users work with reusable PLC code as versioned library types. A typical request to an agent is: "I like
this block, make it a type, then let's see how it works and maybe improve it." The agent should be able
to do that by naming things the way the user does (a block in a PLC, a type by name, a version by
number), with one item per step and with the tool doing every native step itself. The agent never moves
block content around and never re-reads what the tool already knows.

PR 3 adds eight versioned STEP 7 type operations to `library_write`. It also changes how the whole tool
applies writes: no planning pass unless the call is a dry run or prompts. The other write tools will
move to the same model in later PRs.

## 2. Goals, non-goals, anti-goals

Goals:

- Eight operations: `create_type`, `edit_type_version`, `release_type_version`,
  `set_default_type_version`, `synchronize_type`, `instantiate_type_version`, `delete_type`,
  `delete_type_version`.
- Name-first selectors for types and versions, in `library_write` and `library_read`.
- Apply without a planning pass for all of `library_write`, PR 2 operations included.
- A Task 0 native probe that settles the unproven native behaviour before any product code.

Non-goals:

- `create_type_version` (new version from another block's documents). Edit, then `plc_write`, then
  release covers new versions.
- An optional release inside `create_type`. A new type stays in work; an instant released type is two
  items in one call.
- Updating PLC uses (`UpdateProject`), master-copy replace/move, metadata writes, local-session
  project-library writes, HMI types, Software Units, unbound global operations.
- Parallel or batched execution. The worker channel takes one request at a time and every Openness call
  goes to one Portal.
- Moving `plc_write`, `network_write`, `hmi_write`, `vci_write` to the no-planning model (later PRs).

Anti-goals (a failure even if it works):

- The tool performing a write the agent did not ask for. In particular, no hidden `SetAsDefault` after
  release or synchronize.
- Pre-checks that duplicate what Portal already refuses (ADR-0012).
- A planning pass on apply that searches the project for callers or instances.
- A call that can kill Portal through a request the tool could have rejected (T7Z).

## 3. Constraints

- ADR-0012: Portal is the refusal authority. A native `EngineeringTargetInvocationException` in a
  mutation worker method is `engineering_rejected`; the tool owns only cases Portal accepts silently,
  gets wrong or crashes on.
- ADR-0015: every call needs a verified binding; global libraries use the bound Portal.
- ADR-0016: per-item capability and confirmation hooks, typed outcome validation, Portal process id in the
  binding snapshot, "no intra-call dependencies". The shared write pipeline (AGENTS.md: validate,
  verified binding, resolve targets, guards, `dryRun`, mutate, verify, audit) resolves targets and
  evaluates guards for every item before any mutation. This PR changes both for `library_write` (new
  ADR, section 9).
- Project-library authoring is project-library only in Openness: `CreateFromDocuments`, `Edit` and
  release of in-work versions do not work on global libraries (docs). Project-library methods map to
  `library_project_write` (standalone only); global-only methods map to `library_write`.
- Contract versions stay: write `2.0`, read `1.0`. The response schema is unchanged and new members are
  additive; the behaviour changes for existing operations (section 6) are stated in the ADR and docs
  *(my call: no schema change, so no version bump)*.
- Items run one at a time, in caller order, stop at the first failure, no rollback across items.
- Native facts come from the PR 2 probe (`2026-11-06-library-write-native-probes.md`: G, F, M, T and U
  cases) and the V21 API reference. Everything else is unproven until Task 0. In PR 2 only a UDT went
  through `PlcType.Export` → `Types.CreateFromDocuments`; the FB route and the export → delete → create
  order are unproven (P1).

## 4. Approaches considered

**`create_type` source.** (a) Inline `content` + `format`: the agent carries the code, and the tool
writes it into a folder it owns. (b) Caller-supplied file path (the contributor's choice): the server
accepts arbitrary paths and must defend against files changing or gaining sidecars. (c) A PLC block or
UDT as the source: the tool exports it, deletes the original and imports it as a type inside one project
transaction, so the original is replaced by a linked instance. **Picked (c)**: it matches the user's
request ("make this block a type"), and no content passes through the agent. New code still enters the
project through `plc_write`, then becomes a type through (c). If Task 0 shows (c) unsafe, implementation
stops and this spec is revised for (a) (section 5 gate); converting existing blocks then becomes a
follow-up. Openness offers no other way to create a type: `CreateFromDocuments` is the only
type-creating member in the 16 V21 API reference files, and an Add-In, UI automation or reflection were
rejected.

**Applying a call.** (a) Today's pipeline: plan every item first (one worker request each), evaluate
guards, then mutate. (b) Plan only for a dry run or a call that will prompt; on apply, each item resolves
its target when it runs, and the few tool-owned checks run inside the mutation worker call just before
the native call. **Picked (b)**. It removes the up-front round trips, and items can refer to objects that
earlier items create. The cost: a refused item 5 no longer stops items 1–4. That is already how native
refusals behave, and an agent that wants all-or-nothing does a dry run first.

**Items that depend on earlier items.** (a) `plc_write`-style working state plus `ReplanAsync`.
(b) Nothing extra on apply: an item finds its target when it runs. In planned calls (dry run, prompting),
the planner marks an item whose target an earlier item in the call creates as "created by item N"
instead of failing. **Picked (b)**.

**Default version after release and synchronize.** (a) Restore the previous default afterwards
(contribution): a second write nobody asked for, with no transaction. (b) Read the default just before
and just after the native call, report both, and change nothing. **Picked (b)**. Refusing equal or lower
release numbers removes the case that made restoring attractive (T7). To keep an old default, the agent
queues `set_default_type_version` in the same call. The docs must state this behaviour (section 10).

**Selectors.** (a) GUID only (library_read today). (b) Names first: PLC block, type name, version
number, with GUIDs as the exact fallback. **Picked (b)**. Users name things, and Openness exposes names,
numbers and the instance-to-version link (`LibraryTypeInstanceInfo`). Duplicates are handled as PR 2
handles duplicate library names. The worker enumerates to find matches; the native `Find(name)` and
`Find(Version)` return one object and cannot reveal duplicates.

## 5. Task 0 — native probe

A standalone net48 probe against V21 on disposable copies, the same way as the PR 2 probe: no worker
pre-checks, independent before/after snapshots, raw traces kept under
`docs/superpowers/acceptance/reports/inputs/`. It runs before any product code and its results update
this spec.

| ID | Question | Decides |
|---|---|---|
| P1 | Export → delete → `CreateFromDocuments` in `ExclusiveAccess.Transaction(project)`: commit works; a forced failure after delete rolls back fully. UDT and FB. | `create_type` route |
| P2 | After conversion: callers and instance DBs resolve; compile result. | `create_type` route |
| P3 | FB using a UDT that is not a type. | `create_type` refusal |
| P4 | Source block already a type instance. | `create_type` refusal |
| P5 | Version GUID before and after `Release` (T7/T8 already show a release becomes the default). | release result |
| P6 | `SetAsDefault` on an older released version. | `set_default_type_version` |
| P7 | Delete a type or version with PLC instances and no dependents; master copies holding instances. | docs wording on what a delete takes with it |
| P8 | Instantiate a global version the project library lacks. | instantiate dry-run evidence |
| P9 | Target default after `UpdateLibrary(..., SetOnlyHigherUpdatedVersionAsDefault)`, with a higher and with a lower source default. | synchronize result and docs wording |
| P10 | Know-how protected block as `create_type` source. | refusal before delete |
| P11 | `Discard()` on an in-work version: what happens to its test instance (the API reference says uses reset to the last released version). | `delete_type_version` on in-work |
| P12 | `plc_write update_block_logic` on an in-work test instance. | edit → change → release flow |
| P13 | Version number an in-work version shows; `Release` with a null number. | release validation |
| P14 | Release as `1.0.0`, `0.1.0`, `0.0.1`, run last and alone because Portal may die (`CheckVersionValuesAreWithinRange`, "Parameter name: build"). Look for the documented range first. | host version rule |

Never repeat T7Z (`0.0.0`). Never use `CreateOptions.Override` (T4).

**Gate:** if P1 or P2 fails, stop after Task 0 and revise this spec for the inline-content fallback
before any product task starts.

## 6. Apply without a planning pass

### Pipeline hook

One opt-in member on `IWriteDomain`: `PlansBeforeApply(items)`, default `true`, so every other domain is
unchanged. `WriteRun` plans and evaluates guards when the call is a dry run, or when
`PlansBeforeApply` is true, or when the call will prompt (read-write and `ConfirmsCall(items)`).
`LibraryWriteDomain.PlansBeforeApply` returns `false`. So a library call plans only for a dry run or a
read-write call containing a global-library lifecycle item. A full-mode call never prompts and never
plans. The hook is safe only for domains with no acknowledge guards; library guards are all
non-interactive. Document that on the hook.

Pipeline and domain changes this needs (found in review):

- `WriteRun.RunItemAsync` reads `_plans[index].DependsOn`; it must tolerate a missing plan.
- `LibraryWriteDomain` sets its item list only in `PlanAsync`, and `VerifyAsync` uses it; set it at the
  start of the run.
- `LibraryVerificationPolicy.ObjectKeys` reads effects; it must read resolved identities instead (below).
- `WriteRun.MarkPartialWrite` treats every attempted item as possibly mutated. A failed item whose
  category is a precondition refusal (`WorkerFailureCategories.IsPreconditionRefusal`, which includes
  `guard_blocked` and `target_not_found`) and that carries no outcome counts as not mutated.

### Apply without planning

- The host validates shape (closed member sets, version number rule) as today.
- Each item's worker mutation method resolves its own target. A miss is `target_not_found`, several
  matches are `target_ambiguous`.
- Tool-owned checks run in the same worker call, right before the native call. A check that fails before
  any native change fails the item with `guard_blocked` and the guard id in the message. That category
  is a precondition refusal, so it neither supersedes earlier items nor counts as a partial write
  *(my call)*.
- Effects are absent from the apply response; the item `result` carries what happened.
- Audit records stay one per call with actual item statuses. Effects and audit already handle a missing
  plan.

### Planned calls (dry run, read-write prompting)

- Block guards evaluated at plan time stop the whole call before any change, as today. The same checks
  run again next to the native call on apply.
- `LibraryWritePlanner` stops failing the whole call at the first unresolved item. When an item's target
  is a type, folder or master copy that an earlier item in the call creates (matched on operation and
  name), it returns a plan whose effect says "created by item N". Otherwise an unresolved target still
  fails the call. The prompt re-check after acceptance compares these entries like any other effect.
- A global library created, opened or retrieved by an earlier item is still used in a later call when
  the call prompts. ADR-0016's reason holds: retrieve can create a subfolder, so the path is not
  predictable at plan time *(my call)*. In full mode and content-only calls nothing plans, so the
  dependency works.

### Verification

`LibraryWriteDomain` keeps the resolved identity of each item: from its result on success, from its
outcome on failure when present. `ObjectKeys` builds keys from that: library root path, folder kind and
path, master-copy name, type GUID, version GUID. A failed item that was attempted and has no outcome
falls back to a library-level key from its selector and conservatively supersedes earlier items on that
library *(my call: keeps the shared `IWriteVerificationPolicy` signature unchanged)*.

### Behaviour changes for PR 2 operations

- `library_master_copy_exists` moves into the create/copy master-copy worker calls. On apply it fails
  that item (`guard_blocked`) after earlier items have run, where it used to block the whole call.
- Target-not-found on apply fails the item, not the whole call.
- Apply responses carry no effects.
- `library_deletes_folder_contents` becomes dry-run information only.
- `library_discards_changes` is unchanged (`close_global` is a lifecycle item; read-write calls with it
  plan).
- `library_state_unverifiable` is raised when a check's own evidence cannot be read.

## 7. Selectors and operations

### Selectors

Library: unchanged (`scope` + `libraryPath`/`libraryName`; source side `sourceScope` +
`sourceLibraryPath`/`sourceLibraryName`). New item members: `typeName`, `typeGuid`, `versionNumber`,
`versionGuid`, `inWork`, `author`, `comment`. PLC members are PR 2's (`plcName`, `plcKind`,
`plcFolderPath`, `plcObjectName`).

- **Type**: exactly one of
  - PLC block (`plcName` + `plcKind` `block`/`plcType` + `plcFolderPath` + `plcObjectName`), resolved
    through `LibraryTypeInstanceInfo.LibraryTypeVersion`. This also fixes the version and the instance.
    Valid for `edit_type_version` (and as the source of `create_type`).
  - `typeName`, optional `folderPath` to narrow. The search covers the whole type tree, subfolders
    included, ordinal name match, by enumeration.
  - `typeGuid`.
- **Version**: at most one of `versionNumber`, `versionGuid`, `inWork: true`. Omitted means the type's
  default version, except `release_type_version`, which always takes the in-work version.
- **Duplicates**: several types with the name (T1) or versions with the number (T8) fail
  `target_ambiguous`. The message lists each candidate's folder path and GUID.
- `library_read` gets the same `typeName`/`folderPath` and `versionNumber` selectors on `list_versions`
  and `list_dependencies`, as alternatives to `typeGuid`/`versionGuid`.
- `LibraryWriteMethods.TouchesProject` adds the always-project operations by name (`create_type`,
  `edit_type_version`, `release_type_version`, `instantiate_type_version`); the others follow
  `scope`/`sourceScope`. `Validate` gets per-operation rules for `plcKind` (block route: source kinds;
  `instantiate_type_version`: no `plcKind`, the group kind follows the version's type).

### Operations

All are content operations: they never prompt, in any mode. "Dry run shows" is the plan evidence.

| Operation | Members | Native route on apply | Result | Dry run shows |
|---|---|---|---|---|
| `create_type` | PLC block (`plcKind` `block`/`plcType`), project type `folderPath` | resolve block and folder; check name (T1); export to an operation-owned temp folder; transaction: delete original, `Types.CreateFromDocuments(dir, name, group, LibraryImportOptions.None)`, link check, commit; delete temp folder | type name, GUID, folder; version GUID, state `InWork`; instance path | block to be replaced by a linked instance; target folder |
| `edit_type_version` | PLC block, or type + optional version + optional `plcName` | from block: its version and instance. From name: the version, then `FindInstances(PlcSoftware)` over every PLC, or only `plcName`; several → `target_ambiguous` naming `plcName` and the block route, none → `target_not_found`. Then `version.Edit(instanceInfo)` | in-work version GUID, based-on number, test instance path | version and instance used |
| `release_type_version` | type, `versionNumber` (required), `author`, `comment` | resolve the in-work version; number check; read default; `Release(DoNotAutomaticallyCreateOrReleaseDependencies, v, author, comment)`; read GUID and default | version GUID and number; `defaultBefore`, `defaultAfter` | in-work version; expected default move |
| `set_default_type_version` | library, type, version | read default; `SetAsDefault()` | `defaultBefore`, `defaultAfter` | current default |
| `synchronize_type` | source library + type; target library | read target default; `type.UpdateLibrary(target, DoNotDelete, CancelIfStructureConflicts, SetOnlyHigherUpdatedVersionAsDefault)`; read target default | target type GUID; `targetDefaultBefore`, `targetDefaultAfter` | dependency closure to be copied; target default |
| `instantiate_type_version` | source library + type + optional version; `plcName`, `plcFolderPath` | `Blocks.CreateFrom(CodeBlockLibraryTypeVersion, ThrowIfPathsConflict)` or `Types.CreateFrom(PlcTypeLibraryTypeVersion, ThrowIfPathsConflict)` | root instance path | dependencies that come along; whether a global version also lands in the project library |
| `delete_type` | library, type | resolve; `LibraryType.Delete()` | deleted type name, deleted version numbers | versions to go; PLC instances and master copies that would go with them |
| `delete_type_version` | library, type, version or `inWork: true` | released: `Delete()`; in work: `Discard()` | deleted number or `discarded: true`; `typeDeleted` | whether the last version takes the type (T11); PLC instances and master copies that would go |

Capability: `create_type`, `edit_type_version`, `release_type_version`, `instantiate_type_version` are
project methods. `set_default_type_version`, `delete_type`, `delete_type_version` follow the library
scope. `synchronize_type` is a project method when either side is the project library.

Supported type kinds: FB, FC, PLC UDT (`CodeBlockLibraryTypeVersion`, `PlcTypeLibraryTypeVersion`).
Other kinds fail `target_kind_unsupported`.

Results extend `LibraryWriteResultInfo`. The failure outcome (`LibraryWriteOutcomeInfo`) is extended
only where a failed item can leave evidence. Example: `create_type` whose link check failed after the
rollback reports what was attempted. New members follow the worker-payload rules: required unless
declared conditional and registered in `ConditionalMemberRegisterTests`.

## 8. Tool-owned checks

| Check | Where | Operation | Failure | Why |
|---|---|---|---|---|
| Version number: three numeric parts, not `0.0.0`, each part in the documented range (P14) | host validation | release | `validation_error` | `0.0.0` killed Portal (T7Z) |
| Version number strictly higher than every existing version of the type | worker, before `Release` | release | `guard_blocked` | equal and lower numbers are accepted; the release becomes the default (T7, T8) |
| No type with the name in the project library | worker, before export | create_type | `guard_blocked` | Portal creates duplicate names (T1) |
| New instance linked to the created type | worker, inside the transaction | create_type | `postcondition_failed` after rollback, with outcome | a conversion must leave a linked instance or roll back |
| Master copy name exists (PR 2) | worker, before create/copy | master copies | `guard_blocked` | Portal renames silently (M1–M3) |

**Deletes have no instance check (owner decision).** `delete_type` and `delete_type_version` call the
native delete directly; the apply route never scans PLCs. The docs say a delete may also remove PLC
instances and master copies that contain them (confirmed or corrected by P7). Library work stays
separate from PLC work: why a type is deleted is the caller's business, controlled through its harness
and prompts. A dry run is the place to see what would go, and only a dry run scans for instances.

Info shown only in dry run and prompts: block replaced by an instance, dependencies copied, default
moving, last version removing the type, folder contents deleted, unsaved changes discarded.

Dropped from the contribution: `library_collision` (narrowed to the type-name check);
`library_unsaved_changes`, which the contribution used for in-work and release states (Portal refuses:
T3, T5, T9); `library_read_only` (Portal refuses: T18, T24, M4).

## 9. Decision record

One new ADR, "Library writes apply without a planning pass", updating ADR-0016:

- `library_write` plans only for a dry run or a read-write prompting call. Tool-owned checks sit next to
  the native call. Items resolve targets when they run, so an item may use what an earlier item created.
  Planned calls show such items as "created by item N". A global library created by a lifecycle item
  in a prompting call is still used in a later call.
- The trade-off and the behaviour changes for PR 2 operations (section 6).
- `create_type` uses `ExclusiveAccess` and one project transaction. This replaces AGENTS.md's "there is
  no `ExclusiveAccess` or transaction" for library writes (precedent: VCI import, ADR-0010).
- Release and synchronize report the default before and after and never restore it.
- The other write tools are expected to adopt the same hook in later PRs.

## 10. Documentation

After live acceptance (docs task last):

- `docs/SupportedOperations/LIBRARY_OPERATIONS_SUMMARY.md`: operations, selectors, checks, apply vs dry
  run, behaviour changes, the "Not supported" list, live-verified scope. It must state plainly how
  defaults move, finalized after P9:
  - **Releasing a version makes it the type's default version (Portal does this).**
  - **Synchronizing a type moves the target library's default only when the source default has a higher
    number than the target default.**
  - **The tool reports the default before and after and never restores the previous default. To keep
    it, add `set_default_type_version` to the same call.**
  - **Deleting a type or version may also remove its PLC instances and master copies that contain them
    (wording finalized after P7); the tool does not check for them. Use a dry run to see what would go.**
- `docs/ARCHITECTURE.md` (library writes, pipeline hook), `AGENTS.md` (library bullets, including the
  transaction), `README.md` (absolute links), `docs/IMPROVEMENT_LOG.md` (rewrite the PR 3 block, keep
  "still deferred", add: other write tools adopt the hook, batched planning if timing shows a need,
  optional `keepDefault`, and consider making a dry run mandatory before any delete — not in this PR), `docs/roadmap/json-contract.md`, `docs/adr/README.md`, the live report under
  `docs/superpowers/acceptance/reports/`.

## 11. Implementation notes for the builder

- Stubs and doubles to add: `TypeCreateTransferResults.CreatedType` and
  `LibraryTypeComposition.CreateFromDocuments` (with environment), `Edit(LibraryTypeInstanceInfo)`,
  `Release`, `SetAsDefault`, `Discard`, `Delete` (type and version), `UpdateLibrary` (4-argument),
  `LibraryTypeInstanceInfo`, `CodeBlockLibraryTypeVersion`, `PlcTypeLibraryTypeVersion`,
  `PlcBlockComposition`/`PlcTypeComposition.CreateFrom(version, UpdatePathsMode)`, `UpdatePathsMode`,
  `FindInstances`, `PlcSoftware : IInstanceSearchScope`, `PlcType.Delete`, the enums used. Already
  present: `ExclusiveAccess.Transaction`, `ProjectBase : ITransactionSupport`,
  `PlcBlock.Export`/`Delete`, `PlcType.Export`. Touching `reference-stubs/` means running the
  packaging tests.
- Hard-coded lists to extend: `LibraryWriteMethods` (operations, `TouchesProject`, `Validate`), the
  worker plan and writer switches, `LibraryWritePlanner` (created-by tolerance), `LibraryVerificationPolicy`,
  the budget guard count, tool and item descriptions, count assertions in tests, the FakeWorker operation
  filter.
- The operation-owned temp folder lives under the worker's temp root, is unique per item and is deleted
  in a `finally`.
- Left to the builder: exact result member names, test names, how `ExclusiveAccess` and the transaction
  are scoped in code, FakeWorker scripts.

## 12. Live acceptance

As PR 2: installed local build, Claude Code client, read-write plus one full-mode case, disposable
standalone project with two PLCs, one disposable user global library, objects named `LT_*`. Cases cover
every operation's success, at least one native refusal each where one exists, both tool-owned version
checks, the duplicate-name check, `target_ambiguous` by name, dry run with a "created by item N" item,
create + release in one call, a refusal after earlier items applied, the default before/after report,
and one PR 2 master-copy collision under the new apply behaviour. No raw MCP traces kept; the report
lists limits.
