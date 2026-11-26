# Issue #224: cheaper reads and writes of large PLC blocks (design)

Date: 2026-11-26. Status: draft for owner review. Scope: four PRs. Input: the issue #224 discussion
summary (2026-11-12) and issue #224 itself.

## 1. Intent

A one-line change in a 72,141-character block costs about 72,000 output tokens and 9 minutes of
model time, because the agent retypes the whole document twice (`dryRun`, then apply). TIA Portal
took 54.5 s of the total. Retyping is also an error source (the LF-only submission that showed
1,789 changed lines).

Goal: the agent's cost of a content write follows the size of the change, not the size of the
block, without weakening any existing guarantee (`expectedContentHash`, `contentDiff`, guards,
worker re-check, verification, audit). Secondary: a cheaper XML read and less TIA time per write.

## 2. Goals, non-goals, anti-goals

Goals

- G1. A client with file tools edits a server-written file and writes by reference: neither
  `dryRun` nor apply carries the document.
- G2. A client without file tools sends only the changed text.
- G3. An XML read costs fewer tokens for the same content.
- G4. One fewer full export per XML write.

Non-goals (decided by the owner on 2026-11-26)

- No translation layer, compact LAD/FBD form, SIMATIC SD as agent-facing form, encoder or
  network splice. Prior art kept for later: the
  [simaticml-decoder](https://pypi.org/project/simaticml-decoder/) Python package.
- No live probes of round-trip fidelity, no STL source route.
- No persistence across server sessions and no project index.
- Option C of the issue (reuse previewed content by reference) is not built separately: the
  working copy (PR 3) gives the same by-reference write and the same "applied equals previewed"
  guarantee.
- No change to the import path or the Openness calls used.

Anti-goals (a failure even if it works)

- The server reads or writes any path a caller supplies.
- A write applies content the caller did not preview, when the caller previewed.
- A working copy outlives the binding it was read under, or is usable after the binding changed.
- The working-copy route bypasses the plan-time hash check, the diff, the worker re-check or the
  audit.

## 3. Constraints

- Two processes: the host (net10.0) owns tools, planning and files; the worker (net48) owns
  Openness. The working copy is entirely a host concern; the worker request is unchanged.
- Content writes run on `Safety/Pipeline` (validate, binding, plan, guards, `dryRun`, mutate,
  verify, audit). No tokens (ADR-0004).
- Structured JSON contract rules in `AGENTS.md`: typed payloads, conditional members registered in
  `ConditionalMemberRegisterTests`, text and structured content from one `CanonicalJson.Serialize`.
- Read-only is the default access mode (ADR-0018); `plc_read` is available in every mode.
- The server runs over stdio as the same Windows user as the client.
- Exports use CRLF, with LF only on the `--- FILE:` separator lines of an XML bundle.

## 4. Approaches considered

Working-copy location (the main fork, decided by the owner):

| Approach | Trade-off | Decision |
|---|---|---|
| A. Server-owned directory under `%LOCALAPPDATA%\TiaMcpServer\working-copies` | No configuration, never lands in a repo; clients that confine file tools to a workspace need a one-time allow | **Chosen as default** |
| B. Startup-configured root (`--working-copy-dir` / `TIA_MCP_WORKING_COPY_DIR`) | Point it into the client workspace and file tools just work; user configures and gitignores it | **Chosen as override** |
| C. Directory per call | Reintroduces caller-supplied paths | Rejected |

Delivery order (decided by the owner): the working copy ships on today's formats, decoupled from
any translation; the translator track is dropped for now.

Partial write for clients without file tools: see PR 4, approach pending owner decision.

## 5. Design

### PR 1: write-path trims (no contract change)

1. **One worker export instead of two.** For an XML write whose bundle has companions,
   `BlockImporter.Import` exports once for `RequireContentHash` (`BlockImporter.cs:46`) and again for
   `EnsureOnlyAuthoritativeDocumentChanged` (`BlockImporter.cs:87`), in the same worker request.
   Reuse the first export for the companion check. Both checks keep their current failure
   categories and messages.
2. **Host diff for an XML-only submission.** When the submitted content is a single SimaticML
   document (no bundle separators, or a bundle holding only the `.xml`), `PlcWritePlanner` builds
   `contentDiff` against the `.xml` document of the current bundle instead of the whole bundle.
   The hash check is unchanged (it is over the whole served bundle). This needs the bundle parser on
   the host: move `BlockBundleFormat`/`BlockImportBundleParser`/`ParsedBlockImportBundle` (Siemens-free)
   to `TiaMcpServer.Contracts` so host and worker share one parser.
3. Tests: worker-side test that the companion check uses the precondition export (callback seam as in
   `BlockExporterSafetyTests`); host test that an XML-only submission with a one-line change yields a
   one-line diff.

### PR 2: relaxed JSON encoder for delivered documents

1. `CanonicalJson`'s write options use `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, so `<`, `>`,
   `&`, `'`, `+` and non-ASCII text are delivered literally instead of as `\uXXXX`. `"`, `\` and
   control characters stay escaped. The documents are JSON tool results, never HTML.
2. Same JSON values, different bytes: delivered-document hashes in the audit change for identical
   content. Contract versions stay unchanged (the value contract is unchanged); the change is noted in
   `docs/ARCHITECTURE.md` and the changelog, and `IMPROVEMENT_LOG.md`'s "escaping inflation" item closes.
3. Budgets measure the serialized text, so XML and SCL items shrink against the 60,000/180,000
   budgets; no budget value changes.
4. Tests: a canonical document containing `<Network>` and `ä` serializes them literally; update tests
   that pin escaped forms; worker IPC (`WorkerJson`) is out of scope.

### PR 3: per-block working copy

**Read.** `get_block_content` and `get_type_content` accept `output`: `inline` (default, today's
behavior) or `file`. With `file`:

- The host writes the content to the working copy and returns
  `{ format, output: "file", path, contentHash, workingCopyHash, chars, lines, warnings }` instead of
  `content`. `contentHash` is the usual hash of the served document (the write's base);
  `workingCopyHash` is `<format>:sha256:<hex>` of the file text as written.
- The file holds what a write applies:
  - `source`: the exported text as-is (`.scl`, `.db`, `.udt`).
  - `xml`: only the primary SimaticML document (`<name>.xml`), without the bundle separators or the
    `.s7dcl`/`.s7res` companions, which a write never applies (`BlockImportRouting`). A bundle with no
    `.xml` document (companion-only fallback) fails the item with the existing "no Simatic ML
    document" guidance.
- The file is UTF-8 without BOM, byte-faithful to the served text (CRLF kept).
- The value budget does not apply to the file, only to the small result; a block over
  `--max-item-chars` can be read this way without raising budgets.
- `output: "file"` with `withDependencies: true` is a `validation_error` (that read is context only).
- Allowed in every access mode: it reads the project and writes only the server's own directory. The
  tool description says so.
- A new read replaces that object's working copy and its base.

**Location and lifetime.**

- Root: `--working-copy-dir` / `TIA_MCP_WORKING_COPY_DIR` (CLI > environment), parsed once at
  startup like the budget options, else `%LOCALAPPDATA%\TiaMcpServer\working-copies`. `doctor`
  reports the effective root and its source.
- Each server process uses its own instance directory under the root, holding an exclusively opened
  lock file for its lifetime. At startup the server deletes sibling instance directories whose lock
  can be acquired (dead instances) and never touches a live one. On normal exit it deletes its own.
- Layout inside the instance directory is readable but not an identity:
  `<plc>/<blocks|types>/<group path>/<name>.<ext>`, with characters invalid in Windows file names
  encoded. Identity is the in-memory index entry, never the path.
- Index (host memory only): key = (binding ID, binding revision, object kind, PLC name, object path,
  format); value = file path, base `contentHash`. An entry from another binding ID or revision is
  unusable; on a binding change the host deletes the instance directory's contents (best effort) and
  clears the index. The binding ID and revision change on every binding transition, re-verification of
  the same project included (`ProjectSessionBinding.TransitionTo`), so a rebind always asks for a
  re-read, as project-tree cursors already do.

**Write.** `update_block_logic` and `update_type_content` accept `workingCopy: true` in place of
`content` and `expectedContentHash` (mutually exclusive; `validation_error` otherwise), plus optional
`workingCopyHash`. `format` is optional and must match the working copy's format when given.

- Admission: no usable index entry for that object and format is a `validation_error` telling the
  caller to read with `output: "file"`.
- Resolution: the host reads the file and substitutes it as `content`, and the base `contentHash` from
  the index as `expectedContentHash`, before planning. Everything downstream is today's pipeline:
  plan-time export and hash check, `contentDiff` (against the `.xml` part, using PR 1), guards, worker
  re-check, import, verification, audit.
- `workingCopyHash`, when given, must equal the hash of the file as read; otherwise the item fails
  `state_changed` with a message naming the working copy (it changed after the preview). The preview
  effect carries a conditional `workingCopyHash` member for working-copy items, so the caller passes
  the preview's value to apply. *Whether apply requires it is pending owner decision (Q3).*
- The file is read once per item per call; the content-size budget on `content` does not apply, but
  the existing diff and response budgets do.
- At most one working-copy item per object per call (`validation_error` otherwise): a later item would
  otherwise read a file the earlier item's success already rewrote.
- After a successful apply, the host rewrites the working copy from the verification re-export it
  already performs (TIA may normalize the block on import; `PlcVerificationPolicy.ReadBackAsync` has the
  exported payload but returns only its hash today, so the payload has to be handed on) and updates
  the base hash, so the next edit
  starts from current state. If that read-back is unavailable, the index entry is dropped and the next
  write asks for a re-read. A failed or `dryRun` call never changes the file or the index.
- Audit: the requested operation records `workingCopy: true` and the resolved hashes, never the file
  path or content (the response already carries `contentDiff.requested.sha256`).

**Contracts.** `plc_read` 1.0 → 1.1 and `plc_write` 2.0 → 2.1 (additive: new optional request members
and conditional result members). New conditional members are registered in
`ConditionalMemberRegisterTests`.

**Docs.** ADR (working copy: server-owned files addressed by identity, session lifetime, privacy
note); `docs/SupportedOperations/` PLC reference; `docs/guides/mcp-client-configuration.md` (allowing
the directory in clients, the override, gitignore); `AGENTS.md` PLC bullet.

### PR 4: partial write for clients without file tools

*Pending owner decisions Q1 and Q2.* Recommended shape: a `plc_write` operation `patch_block_logic`
`{ blockPath, format?, expectedContentHash, edits: [{ oldText, newText }] }`, independent of the
working copy:

- Base: the plan-time export the host already makes, guarded by the whole-document
  `expectedContentHash` (from any `plc_read`, inline or file).
- Each `oldText` must occur exactly once in the base, and edits must not overlap; otherwise
  `validation_error` naming the edit. Edits apply to the base, not to each other's results.
- Line endings: `oldText` is matched with line terminators normalized; `newText` is inserted with the
  terminator of the matched region (CRLF for current exports). An edit spanning a `--- FILE:`
  separator, or outside the `.xml` document of an XML bundle, is refused.
- The reassembled document then follows the `update_block_logic` path unchanged, including
  `contentDiff`, which is the caller's check that the patch did what it meant.
- Blocks only; types are small enough to resend.

## 6. Left to the builder

- Exact names of new internal types and files; where the working-copy service is registered.
- The encoding scheme for file-name characters, and the lock-file mechanism details.
- Whether PR 1's parser move keeps type-forwarding shims for tests or updates the links.
- Test granularity beyond the cases named above.

## 7. Open questions for the owner

- Q1. PR 4 base: fresh export (independent of PR 3, as above) or the working copy.
- Q2. PR 4 anchor: unique `oldText` (as above) or the issue's `startLine` + `expectedText`.
- Q3. Working-copy apply: `workingCopyHash` optional (apply without preview allowed) or required.

## 8. PR order

PR 1 → PR 3 (uses PR 1's XML-part diff). PR 2 and PR 4 are independent and can land in any order.
