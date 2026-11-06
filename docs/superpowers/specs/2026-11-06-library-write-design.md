# library_write (issue #218, PR 2) — design

Status: approved for writing up 2026-11-06. Input: the native-rejection probe report on this branch
(`docs/superpowers/acceptance/reports/2026-11-06-library-write-native-probes.md`, "Consequences"
section), the `library_write` carry list in `docs/IMPROVEMENT_LOG.md`, and the contributor package
attached to issue #218 (`OPERATIONS.md`, patch against v4.2.0).

## Intent

Issue #218's contributor proposed library support: global-library lifecycle, folders, master
copies, versioned STEP7 types and PLC instance updates (25 write operations). PR 1 (`library_read`,
#220) shipped the reads. This PR ships the first slice of writes. It covers the operations where
Portal itself refuses most bad requests and the hazards the tool must own are simple. The riskier
type/version and instance-update work follows in later PRs (see "PR 3 notes").

Success: an agent can create or open a global library, create folders, put PLC objects into a
library as master copies, copy master copies between libraries, instantiate them into a PLC, delete
folders and master copies, then save, archive and close the library — through the same guarded
pipeline as `plc_write`, `hmi_write` and `vci_write`, with no silent data loss.

## Goals

- One new tool, `library_write`, registered in read-write and full mode only, with 12 operations:
  - Global lifecycle: `create_global`, `open_global`, `retrieve_global`, `save_global`,
    `close_global`, `archive_global`.
  - Folders: `create_folder`, `delete_folder`.
  - Master copies: `create_master_copy`, `copy_master_copy`, `instantiate_master_copy`,
    `delete_master_copy`.
- Folder and master-copy operations work on the project library and on open global libraries
  (`scope: project|global`, as in `library_read`).
- Every tool-owned hazard from the probe report that applies to these operations is closed (see
  "Hazards the tool owns").

## Non-goals (this PR)

- Versioned types, type versions, `UpdateLibrary`, type instantiation (PR 3).
- `update_type_instances` (`UpdateProject`), `replace_master_copy`, `move_master_copy`,
  `move_master_copy_folder` (deferred until a transaction-plus-inventory design exists).
- Implicit saves: no project save, no global-library save outside `save_global`, `close_global` with
  `closeAction:"save"`, or `archive_global`'s documented behavior.
- Library upgrade on open/retrieve, library metadata writes (the contributor's later "metadata"
  bundle), Software Units, constants, HMI objects as master-copy sources or destinations.
- Project-library writes on local sessions, and any Project Server interaction.
- Unbound (Portal-only) global operations.

## Anti-goals

These would make the PR a failure even if every test passed:

- Any path that sends a `0.0.0`-style or otherwise unvalidated value straight to a native call known
  to kill Portal.
- A close that discards unsaved library changes without the caller saying `discard`.
- A master copy or PLC object created under a different name from the one requested, without the
  caller being told (Portal's silent `_1` rename).
- A second write pipeline, a token, a public confirmation argument, or an acknowledge guard.
- Pre-checks that duplicate a refusal Portal already makes (ADR-0012).

## Constraints

- Builds on `Safety/Pipeline` (`IWriteDomain`, `WriteExecution`/`WriteRun`) and the structured JSON
  contract rules in `AGENTS.md`. Worker success payloads are typed; conditional members are
  registered in `ConditionalMemberRegisterTests`.
- Two-process split: every Openness call is in the net48 worker.
- Worker method names live in `TiaMcpServer.Contracts/Library` (new `LibraryWriteMethods`) and map
  to capabilities through `ProjectCapabilityCatalog.OperationFor` only, as `LibraryReadMethods` do.
  Mutation methods are classified `ProjectMutation` in `OperationPolicyCatalog`, so native refusals
  surface as `engineering_rejected` with `engineeringError`.
- `TiaPortalProcess` is never disposed (it closes every Portal).
- Live acceptance runs on disposable fixtures only; no project or library names go into the repo.
- Documentation (README, AGENTS, ARCHITECTURE, SupportedOperations) is written in the last task,
  after live acceptance. The live report is the only documentation written earlier.

## Approaches considered

1. **All 25 contributed operations in one PR.** Matches the contributor's package. Rejected:
   the riskiest operations (`UpdateProject`, cross-library moves) would hold up everything else and
   the PR would be very large.
2. **Staged by risk (chosen by the maintainer).** PR 2: lifecycle, folders, master copies. PR 3:
   versioned types. Instance updates, replace and moves later.
3. **Types first.** Ship the type/version workflow first. Rejected: it carries the hardest hazards
   (version validation, dependency closure) without the simpler lifecycle surface it depends on.

## Decided behavior

### Tool shape

- `library_write(operations, dryRun=false)`: 1–50 items, unique `operationId` (256-character limit),
  caller order, stop at the first failure, no rollback, no replay. Write contract `2.0`;
  `library_read` stays `1.0`. Phases `preview`, `applied`, `blocked`, `error`, as in the other write
  tools. Argument-shape errors return the canonical `validation_error` document with no audit
  record.
- Items are flat objects, like `library_read`. Members are closed per operation: an unknown member
  or a member that does not belong to the operation is `validation_error`.
- No item depends on an earlier item in the same call (builder-check call). Every item must target
  libraries, folders and master copies that exist when the call is planned; a target created,
  opened or retrieved earlier in the same call fails `target_not_found` at plan time, before any
  mutation. Callers split such work into two calls (for example `open_global`, then a second call
  with `create_folder`). Reason: the actual path of a created or retrieved library is not
  predictable (probe G6), and dependent items would have null plan effects, so the confirmation
  would not show them. Intra-call dependency inference (`ItemPlan.DependsOnItem`, as PLC uses) can
  be added later if callers need it.
- Verification is the shared `{success, operations, omission}` with `trusted`/`superseded` entries
  and no native read-back, except the postconditions listed under "Hazards". There is no `verify`
  RPC and no `LibraryWriteVerification` root (contributor code to drop).
- One audit v2 record per entered call, dry runs and blocked calls included.

### Confirmation (maintainer decision)

- In read-write mode, a call that contains at least one global lifecycle operation asks for one
  form elicitation (`accept` + `confirm:true`). Missing capability, decline, cancel, timeout or
  transport failure denies with `access_denied`. Full mode never prompts and records `policy`.
- Calls with only folder and master-copy operations never prompt, in any mode (audit confirmation
  `none` in read-write, `policy` in full).
- Pipeline seam: add an item-aware member to `IWriteDomain` (receives the call's items) whose
  default returns the existing `ConfirmsEveryCall`; `WriteRun` (the only reader, in ask-user mode)
  calls the new member. The five existing domains are unchanged. The library tool passes a
  `WriteConfirmationContext` built with `UserConfirmation.For(server, …)` as
  `ProjectWriteTools` does today. `DescribeForConfirmation` names every lifecycle operation and
  its target. Data domains keep `WriteGuardCatalog.NonInteractive`; no acknowledge guard is added.
- After acceptance the pipeline re-resolves state (existing behavior), so an accepted consequence
  cannot silently change.

### Binding and capability

- Every call needs a verified binding, global-only calls included (consistent with ADR-0015 and
  `library_read`). Unbound calls fail `binding_conflict`.
- Global operations act on the bound Portal. No new identity check is needed:
  `ProjectBindingSnapshot` already carries `PortalProcessId`, and the lease, the confirmation
  rechecks and the worker's per-request check already compare it. (The carry-list item claiming
  otherwise is stale; close it in the docs task.)
- Library ownership (maintainer decision): `save_global`, `close_global` and `archive_global` act on
  any global library open in the bound Portal, whether the tool or the user opened it. There is no
  worker-owned/borrowed distinction for libraries. The read-write lifecycle prompt names the
  library, and `close_global` still requires an explicit `closeAction`.
- The `AGENTS.md` rule "create, save-as, archive and close stay standalone-only" is about project
  lifecycle. Global-library lifecycle is Portal-scope and follows the global capability below; the
  docs task says so explicitly.
- Capability is decided per item, not per tool:
  - Items that touch the project library or a PLC (any `scope:"project"` item, and every
    `create_master_copy` and `instantiate_master_copy`) are standalone-only.
  - Items that touch only global libraries follow `library_read`'s global capability (allowed on
    bound local sessions; `notYetDelivered` on server projects).
  - The host gate in `WriteRun` checks `ProjectCapabilityCatalog.Supports(container, ToolName)`.
    Keep it, but take the names to check from a new domain hook that defaults to `[ToolName]`; the
    library domain returns each item's worker method. `Supports` already maps through
    `OperationFor`, and the per-request host gate and worker gate stay as they are. A refused item
    fails the call before any worker mutation with `unsupported_capability`.
  - New capability names: a global write capability next to `library_read` in the project-content
    set, and a standalone-only project-library write capability next to `library_project_read`.
    Library write worker methods map to them; no write method maps to a read capability.

### Selectors

- Library selectors reuse `library_read`'s rules: `scope`, `libraryPath`/`libraryName` (exact
  ordinal name, normalized full path, both must match when both are given), `folderKind`
  (`masterCopies` or `types`), segmented `folderPath`. Global selectors resolve only libraries open
  in the bound Portal; two open libraries with the same name fail `target_ambiguous`, so the caller
  must use the path.
- `copy_master_copy` carries a second selector with a `source` prefix (`sourceScope`,
  `sourceLibraryPath`, `sourceLibraryName`, `sourceFolderPath`) plus the master copy `name`.
- PLC selectors: exact `plcName`, `kind`, segmented `folderPath`, and `name` for a source object.
  PR 2 supports sources `block`, `plcType` (PLC UDT) and `tagTable`, and destinations
  `blockGroup`, `typeGroup` and `tagTableGroup`. Portal remains the authority on which object/CPU
  combinations it accepts (probe M7).

### Operations

| Operation | Members besides `operationId`/`operation` | Native call |
| --- | --- | --- |
| `create_global` | absolute parent `directory`, library `name` | `GlobalLibraries.Create` |
| `open_global` | absolute `.al21` `filePath`, `openMode`: `readOnly`/`readWrite` | `GlobalLibraries.Open` |
| `retrieve_global` | absolute `.zal21` `filePath`, absolute target `directory`, `openMode` | `GlobalLibraries.Retrieve` |
| `save_global` | global library selector | `Save` |
| `close_global` | global library selector, required `closeAction`: `save`/`discard` | `Save` (when `save`), then `Close` |
| `archive_global` | global library selector, absolute `directory`, archive `name` | `Archive` |
| `create_folder` | library selector + parent `folderKind`/`folderPath`, new folder `name` | `Folders.Create` |
| `delete_folder` | library selector + parent folder, child folder `name` | `Delete` |
| `create_master_copy` | destination library master-copy folder, PLC source selector | `MasterCopies.Create` |
| `copy_master_copy` | destination folder, source folder, master copy `name` | `MasterCopies.CreateFrom` |
| `instantiate_master_copy` | source library folder + master copy `name`, PLC destination selector | PLC `CreateFrom(mc, ThrowIfExists)` |
| `delete_master_copy` | library master-copy folder, master copy `name` | `Delete` |

Portal is the refusal authority (ADR-0012), with no tool pre-check, for: create into a non-empty
folder or with an invalid name; open in the other mode; save of a read-only library; archive of a
dirty library, over an existing file or into the library's own folder; folder name collisions
(case-insensitive); any write to a read-only library; deletes blocked by dependents; master-copy
instantiation over an existing object or into an unsupported composition.

Observed native behavior the tool reports but does not block: `open_global` of an already-open file
in the same mode returns the existing library (the item result says it was already open);
`retrieve_global` into a folder that already holds files creates a `<name>` subfolder (the item
result reports the actual library path).

### Hazards the tool owns

| Probe finding | Tool behavior |
| --- | --- |
| G9: `Close` silently discards changes | `closeAction` is required on every `close_global`; `discard` fires the info guard `library_discards_changes` in the preview. |
| G13: `Archive` does not append `.zal21` | `archive_global` always uses `LibraryArchivationMode.Compressed` (no mode argument in PR 2) and appends `.zal21` when missing, case-insensitively, through a helper parameterized from `ArchiveModeNames.EnsureArchiveExtension` (which hardcodes `.zap21`); the result reports the final path. |
| M1–M3: master-copy create/copy auto-renames | Plan-time check for an existing master copy with the same name (case-insensitive) in the destination folder fails the item with the block guard `library_master_copy_exists`. After the native call, the worker reads back the created name; a mismatch fails `postcondition_failed` and reports the actual name. |
| M6: PLC `CreateFrom` without a mode auto-renames | Always use the `ThrowIfExists` overload (Portal refuses, M5). |
| F5: deleting a non-empty master-copy folder deletes its contents | Info guard `library_deletes_folder_contents` lists the bounded contents in the preview. |
| 9: two open libraries can share a name | `target_ambiguous` on a name-only selector (see Selectors). |

Unreadable plan evidence (for example, a folder's contents cannot be listed for the delete preview)
fires the block guard `library_state_unverifiable`, as in the other domains.

### Results and failures

- Each item result is a typed object: the resolved library identity (`scope`, `libraryPath`,
  `libraryName`), the affected folder or master copy identity in `library_read`'s identity shape,
  and operation-specific facts (`alreadyOpen`, `archivePath`, `actualName`, `saved`).
- A native `EngineeringTargetInvocationException` in a mutation method is `engineering_rejected`
  with `engineeringError`. Other Siemens failures are `worker_operation_failed`. `portal_lost`
  takes precedence and invalidates the binding.
- `close_global` with `save` is two native steps. If `Save` succeeds and `Close` fails, the item
  fails with a typed partial outcome (`saved:true`). `PersistentWorkerTransport` validates this
  outcome before the permissive envelope reader, as it does for Network and VCI outcomes; a
  malformed outcome is `protocol_error`.
- Library read snapshots are not dropped after a mutation (builder-check call; supersedes the
  carry-list item). `library_read` continuations already page a frozen row order with live values
  and mark deleted rows; rows created after the snapshot appear in a fresh scan. Dropping would
  change `library_read`'s documented cursor behavior and would miss the Portal-owned
  `list_global_libraries` snapshots anyway.

## Left to the builder

- Class and file layout under `TiaMcpServer/Library` and the worker, following the HMI/VCI write
  domains.
- Exact payload member names beyond the ones above, guard message text, preview effect shapes.
- The per-item bound for the delete-folder content preview, which must stay within the response
  budget (read `StructuredOperationBatchPayloadBudget.MaxItemChars`, never a literal).
- Which contributor native code to port (`LibraryWriter` and helpers) versus rewrite. Any ported
  code must drop the `verify` RPC, position-based `Take(6)` operation sets, and the skipped
  admission accounting (`WriteValidation.Compose(MeasureProtectedCore…)`) noted in the carry list.
- Test layout, as long as tests are added to `TiaMcpServer.Tests.csproj` where needed and the
  FakeWorker covers the IPC paths.
- The live-acceptance case list, which must cover each operation once, each hazard row above, the
  lifecycle prompt and its absence on content-only calls, a duplicate-name selector, and a
  local-session global-only call.

## PR 3 notes (not in this PR)

- Operations: `create_type`, `create_type_version`, `edit_type_version`, `release_type_version`,
  `set_default_type_version`, `synchronize_type`, `instantiate_type_version`, `delete_type`,
  `delete_type_version`.
- Hazards from the probe report that PR 3 must own:
  - Version-number validation before `Release`: reject `0.0.0` (T7Z killed Portal), and reject a
    number equal to or lower than an existing version (T7, T8 are accepted natively).
  - Never use `CreateOptions.Override` (T4 replaced an in-work version and deleted a test
    instance); `CreateFromDocuments` creates duplicate-name types (T1), so check for collisions.
  - `UpdateLibrary` and type-version instantiation pull in dependencies (T17, T20); the preview must
    show the dependency closure.
  - Deleting the only version removes the whole type (T11).
  - `release_type_version` and `synchronize_type` change the default in several steps with no
    transaction; decide on a receipt.
  - Stage approved XML into an operation-owned directory before import (sidecar race).
- PR 3 reuses what this PR adds: the item-aware confirmation, the per-item capability hook, the
  write capabilities and the typed outcome validation.
- Still deferred after PR 3: `update_type_instances` (`UpdateProject` removed 56 unused instances
  despite `DoNotDelete`; needs a transaction with an inventory check), `replace_master_copy`,
  `move_master_copy`, `move_master_copy_folder`.
