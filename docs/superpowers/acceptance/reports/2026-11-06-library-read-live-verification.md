# library_read — live verification, 2026-11-05/06

**Verdict:** scoped PASS for the global-library and project-library read paths listed below, on one bound standalone project and one opened user global library. The read-only proof is clean for the final candidate. An earlier run saw a one-time storage rewrite inside the library folder that this report cannot attribute; see "Read-only proof". Local session, rebind cursors, duplicate libraries, and changes between pages (library closed, library changed, Portal lost) were not exercised and remain unverified.

## Provenance and scope

The runs used the Release build output directly, built against the installed TIA Portal V21 reference assemblies (`UseTiaPortalReferenceStubs=false`). They did not use an installed package. Each run used a stdio MCP client that launched the host with `--access-mode read-only` or `read-write` and `--worker-timeout-minutes 15`. Default value budgets applied. TIA Portal V21 executable build `2100.0.121.1`, English UI.

| Run | Candidate | Host SHA-256 | Worker SHA-256 |
| --- | --- | --- | --- |
| R1–R4 (2026-11-05) | `d2e67fdc8dc2e60f050a04cd18c0ae326e0ab369` | `E54A481F…647A4F` | `797D6CED…C04E` |
| R5 (2026-11-06) | `3e138997b40900b8e2aa4fdbcac02a0d8d61249d` | `7D07C211DCF111B5B79563D839B6B89B242605DD0DE5DA503C4F41B86BCEFC6E` | `B561235F5BC7797D48FD710E431DC175378C329F85CBEEB8323FABC58699A4F5` |

A package packed from `3e138997` (SHA-256 `E0B074986D10A2C93AC10D035C064613E56FD7B775007FE1C4519BEF7E38E08F`) passed `verify-doctor-package.ps1` and contains no Siemens entries. Between R4 and R5, `list_global_libraries` changed to enumerate `GetGlobalLibraryInfos()`; R5 re-verified that operation and the not-open selector path on the final code.

Fixtures:
- **P1**: one standalone project, already open in the Portal, with unsaved changes. Its project library has 69 types.
- **L1**: one user global library, opened read-only in the same Portal. It has 129 types in nested type folders up to depth 4, and no master copies.

Identities and paths are not published here. The raw MCP traces were kept outside the repository and are not retained.

## Verdicts

| ID | Observed result | Verdict |
| --- | --- | --- |
| B1 | Unbound call → whole-call `binding_conflict`. Read-only mode exposes 9 tools; read-write exposes 20. | PASS |
| B2 | `bind_project` on P1 succeeded. Every later call used the bound Portal. | PASS |
| G1 | R5 `list_global_libraries`: 21 rows (6 system, 14 corporate, 1 user), ordered by name. Only L1 had `isOpen:true`, and only L1 carried `isModified`/`isWriteProtected`/`nativeType` (`UserGlobalLibrary`). The other 20 rows had `isOpen:false`, `isReadOnly:true` from the info, null flags and the not-open diagnostic. System libraries reported `.as21` paths under the Portal installation's `Lib\Sys`; corporate libraries reported `.al21` paths. No info had a null path. | PASS |
| G2 | R5 paging `list_global_libraries` with `limit:2` took 11 pages and matched the full listing row-for-row, including `isOpen`. | PASS |
| G3 | R5 global `inspect_library` on L1 → `libraryType:"user"`, `isOpen:true`, no diagnostics. | PASS |
| G4 | R5 unopened library by name, by path, and `list_children` by path → `target_not_found`, "available but not open in the bound Portal; open it in TIA Portal first. No library was opened or changed." An unknown name → `target_not_found`, "found none". | PASS |
| S1 | Selectors (R3): exact ordinal name matched; a wrong-case name missed; path matched case-insensitively; name+path both had to match (a mismatch → `target_not_found`); a relative path and a malformed path → `validation_error`. | PASS |
| T1 | `list_children` walked the global and project type trees, folders before entries; the master-copy tree was empty. | PASS |
| V1 | `list_versions`: a three-version type with default `0.0.3`. Every version state observed was `Committed`, and every type status was `Consistent`. | PASS |
| D1 | `list_dependencies`, both directions, both scopes. P1's project library produced 59 non-empty dependency lists. No item failed. | PASS |
| C1 | Cursor paging of children, versions and dependencies (`limit` 1–2) matched the full listings. A continuation that restated the default `direction` explicitly was accepted. | PASS |
| C2 | Cursor with `offset` → `validation_error`; tampered cursor → `invalid_cursor`; cursor with other selectors → `cursor_filter_mismatch`. | PASS |
| C3 | A page-1 cursor still worked after the last page had been read: the snapshot lives until idle expiry and is not dropped at the end. | Observed |
| P1 | Project `inspect_library` → `isModified:true` (P1's own flag, with the diagnostic saying so), null read-only/write-protection with a diagnostic, native type `ProjectLibrary`. | PASS |
| RO | Read-only proof for R5: see below. | PASS |

## Read-only proof

Before and after each session, every file under P1's folder and L1's folder was hashed, with size and modification time recorded. L1 reported `isModified:false` throughout.

- **R5** (final candidate, 18 tool calls): both folders unchanged, 0 differences.
- **R1–R4**: P1's folder was unchanged. In L1's folder, the library's internal storage files under `System\` (the `PEData` data file, its index, and a `~PEData` temp file) were rewritten once. The data file shrank from 9.3 MB to 4.4 MB. This happened 8 seconds after R1 finished, and R1 made no library reads; its only calls were a status read, a portal inventory and one host-rejected call. Nothing changed during R2–R4, which made roughly 400 library reads. Our working explanation is a one-time Portal-side compaction or save of the opened library, but this run cannot prove that.

## Not verified

- **Local session.** Global reads succeeding and project-library reads failing with `unsupported_capability` are covered offline only.
- **Rebind cursor (`cursor_binding_mismatch`).** `forceRebind` was avoided because P1 had unsaved changes.
- **Changes between pages:** a library closed in the UI, a library changed (frozen order with live values), and Portal loss. Per-row liveness and `isOpen` re-evaluation are covered offline only.
- **Duplicate global library names (`target_ambiguous`).**
- **Read-write mode:** exercised for reads only (R4), which is all this tool does.
