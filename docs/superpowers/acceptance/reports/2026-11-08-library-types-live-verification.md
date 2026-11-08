# Library types — live verification, 2026-11-08

**Verdict: scoped PASS after a delete-preview fix.** All eight type operations completed successfully and
each produced an exercised native refusal. Read-write content calls did not prompt; a full-mode
create/release batch also passed. The run found and fixed an invalid cross-container instance search
in global delete previews. The corrected build was verified against the same live fixtures.

The `library_state_unverifiable` path for an unexportable, uncompiled instance remains offline-only:
the ordinary and interface-changed FBs used here exported before compilation. This run does not
establish that every uncompiled instance is exportable.

## Provenance and scope

Codex used the installed stdio tool `4.3.0-local.107.g38bee830` (candidate `38bee830`) for the initial
full-mode case and read-write acceptance. A small Python stdio client reused the repository's timing
transport, advertised no elicitation capability, and checked canonical text against structured content.
The final preview cases used commit `d7b89c50`, built locally against installed V21 assemblies;
its default assembly version was `1.0.0.0`. The global tool installation was not changed.

| Build | Artifact | SHA-256 |
| --- | --- | --- |
| Initial installed candidate | Host DLL | `d25ec8e1d7f442c8af50f2275e05877ab3185b51f64918c17eafa58bb15df6ec` |
| Initial installed candidate | Worker EXE | `46ce1e6701ea7fb5dda6bdbff8e824925518397a51f9f8156e13fc6a09b2942c` |
| Corrected local candidate | Host DLL | `3774bad96d043c175d7ac26d166361e63e0e31c7d7d6cfd2b0339cac41b046db` |
| Corrected local candidate | Worker EXE | `de7a73ccdd9e5cc8b3bc54e31853add814f5081facfb48110b13642a5ca6e890` |

The maintainer authorized mutations in one already-open disposable standalone project (P1), with
two PLCs (`PLC_1`, `PLC_2`), and one disposable user global library (L1). Their identities and paths
are not published. Only `LT_*` test objects and their containing test folders were changed.
Native fixture preparation renamed one linked block and one type; exact names were read back and
restored. Native diagnostic calls were read-only. No project/library save, project close, download,
PLC control, hardware operation or installation was performed.

## Operation and selector cases

| Case | Observed result |
| --- | --- |
| Full mode | `create_type` then `release_type_version` in one call succeeded; no effects were planned and the earlier verification entry was superseded. |
| Create FB, FC, UDT | All converted to linked project-library types and released. |
| Create native refusal | An `LT_*` SCL source without an exportable compile unit was rejected by native `CreateFromDocuments`. A final project-tree read found the source with its original name and block number; its protected content was not compared. |
| Duplicate type name | A second PLC source with the same name was blocked by `library_type_name_exists`; a different requested folder did not bypass the library-wide check. |
| Linked source with different name | A native fixture rename preserved the link; `create_type` returned `guard_blocked` / `library_source_is_type_instance`. |
| Dependent preview/apply | Create followed by release previewed `createdByItem:1`; actual execution resolved the newly created type and returned `effects:[]`. |
| Name ambiguity | Two same-named types in different folders produced `target_ambiguous` for both read and write. Exact folder and GUID reads succeeded. |
| Release without number | Released the native prefilled `0.0.1`, and later `0.0.3`. |
| Release explicit own number | An in-work version prefilled `0.0.2` released successfully with explicit `versionNumber:"0.0.2"`. |
| Release checks | An already released/lower number returned `library_version_not_higher`; `0.0.0` was a host `validation_error`. |
| Release native refusal | A type containing forbidden global-data access/single-instance calls was rejected by native `Release`; dependencies were not automatically released. |
| Set default | Selected committed versions in both directions; receipts showed the actual before/after numbers. Missing versions returned `target_not_found`. |
| Set default native refusal | Native `SetAsDefault` rejected an in-work version. |
| Synchronize | Project → global and global → project succeeded. A target default `0.0.4` stayed unchanged when source default was `0.0.2`. With the same target's default reset to `0.0.1`, synchronization moved it to `0.0.2`, although its own `0.0.4` release still existed. |
| Synchronize native refusal | Updating a library from itself returned `engineering_rejected`. |
| Instantiate | Global → PLC_2 succeeded and introduced the version into the project library. Repeating placement of the same type/version into its existing location succeeded. A plain-name conflict produced a native suffixed name (`_1`), reported in `instancePaths`. |
| Instantiate native refusal | Native `CreateFrom` refused an in-work version. |
| Delete whole type | A global type with no instances deleted successfully; a project type with PLC instances and a global type held by a global master copy were rejected natively. |
| Delete version | An unused nondefault project version deleted successfully. Deleting a global type's only version also deleted its type (`typeDeleted:true`). |
| Delete version native refusals | PLC-held, project-master-copy-only and global-master-copy-only versions were rejected. A default version with other versions also received a native refusal. |
| Master-copy collision after progress | An earlier folder creation stayed applied, the colliding copy failed `library_master_copy_exists`, and the last item was skipped. The same ordered result was verified for a case-only collision. |

Every exercised Siemens mutation refusal above returned `engineering_rejected` with typed
`engineeringError`. These outcomes establish the stated cases, not a complete native-refusal matrix.

## Content-version and discard cases

`create_type_version` accepted the instance's XML bundle and `expectedContentHash` from `plc_read`.
The run exercised a new in-work version with `replaceInWork:true` when none existed
(`replacedInWork:false`), replacement of an existing in-work version (`true`), native refusal when
replacement was omitted, and a stale hash (`state_changed`, without import). Comment and interface
changes were subsequently compiled, released and read back.

With linked instances in both PLCs, documents and a hash read from PLC_2 caused Portal to choose
PLC_1 as the test instance. The receipt correctly reported PLC_1. After release and compilation,
PLC_1 contained the edited content and PLC_2 did not. The input PLC identifies the source/hash
check; callers must inspect the returned `instancePaths` for native test-instance selection.

Discarding a content-created in-work version with a prior release kept the test instance and
restored its last released content. Discarding the first, never-released version deleted the type
and its test instance; a subsequent compile lookup returned `target_not_found`.

Targeted successful compiles reported zero errors. One older PLC_2 instance produced a warning;
it remained readable. Export before compilation succeeded for both ordinary and interface-changed
FB fixtures. Requests using prechange hashes then returned `state_changed`. The compile-first
workflow remains recommended, but a categorical claim that all uncompiled instances are unreadable
is not supported by this run.

## Global preview defect and correction

The initial global delete previews returned `library_state_unverifiable` and omitted master-copy
evidence. Separate native calls established the precise cause: invoking a global version's
`FindInstances` with either project PLC threw `EngineeringTargetInvocationException`, explaining
that the search scope belonged to another project/global library. Reading and enumerating the same
version's `MasterCopiesContainingInstances` succeeded.

Commit `d7b89c50` confines PLC instance searches to project-library deletes. Global previews still
read their own master-copy associations and still fail closed if required evidence is unreadable.
The six new regression cases failed before the fix; all 50 focused and 696 library tests passed after it.

The corrected native-reference build then passed these live checks:

- Global whole-type and selected-version previews listed the holding master copy without an
  unverifiable-state guard or an invalid project PLC search.
- The project master-copy-only preview retained its existing evidence.
- An unused global version previewed without blockers and deleted its only-version type.
- An actual deletion of the global copy-held version still received Portal's native refusal.

The [retained native diagnostic and source](inputs/README.md) substantiate this correction. MCP
captures, test-client scripts and restoration dumps are not retained as published evidence.

## Verification limits and final state

Read-write library content audits inspected during the run recorded `none/not_requested`;
successful calls with a client lacking elicitation support corroborate no-prompt behavior.
Lifecycle prompting was not requalified; its previous [live report](2026-11-07-library-write-live-verification.md)
remains the scoped evidence for those operations.

The unexportable-instance guard, Portal loss, local sessions, non-user libraries, additional PLC
kinds/languages, nested dependency conflicts and extreme release-number ranges were not qualified
by this run. No claim extends to a running PLC or WinCC runtime.

Test additions and native library changes remain in the disposable Portal session and were not
saved by this run. Temporary fixture names were restored; no whole-project restoration is claimed.

Final offline gates passed on `d7b89c50`: the reference-stub solution build had zero errors,
the full non-Packaging suite passed 8,382/8,382 tests, and Packaging passed 39/39.
The installed-reference candidate build also had zero errors. Existing compiler warnings remain.
The documentation verifier reported zero errors before the planned transient-file pruning.
Independent source and documentation reviews left no unresolved important findings.
