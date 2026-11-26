# Issue #224: cheaper reads and writes of large PLC blocks (design)

Date: 2026-11-26. Status: approved by the owner on 2026-11-26, revised after one adversarial review.
Scope: three PRs; a per-block working copy is deferred (section 6). Input: the issue #224 discussion
summary (2026-11-12) and issue #224 itself.

## 1. Intent

A one-line change in a 72,141-character block costs about 72,000 output tokens and 9 minutes of
model time, because the agent retypes the whole document twice (`dryRun`, then apply). TIA Portal
took 54.5 s of the total. Retyping is also an error source (the LF-only submission that showed
1,789 changed lines).

Goal: the agent's cost of a content write follows the size of the change, not the size of the
block, without weakening any existing guarantee (`expectedContentHash`, `contentDiff`, guards,
worker re-check, verification, audit). Secondary: a cheaper read and less TIA time per write.

## 2. Goals, non-goals, anti-goals

Goals

- G1. Any client sends only the changed text of a block write (PR 3).
- G2. Delivered documents cost fewer tokens for the same content (PR 2).
- G3. One fewer full export per XML write whose submission carries the companions (PR 1).

Non-goals (decided by the owner on 2026-11-26)

- No translation layer, compact LAD/FBD form, SIMATIC SD as agent-facing form, encoder or
  network splice. Prior art kept for later: the
  [simaticml-decoder](https://pypi.org/project/simaticml-decoder/) Python package.
- No live probes of round-trip fidelity, no STL source route.
- No persistence across server sessions and no project index.
- No working copy in this round (section 6).
- Option C of the issue (reuse previewed content by reference) is not built: the patch makes the
  resend small.
- No change to the import path or the Openness calls used.

Anti-goals (a failure even if it works)

- The server reads or writes any path a caller supplies.
- A patch bypasses the plan-time hash check, the diff, the worker re-check, verification or the
  audit.
- The audit can no longer show exactly what was applied.

## 3. Constraints

- Two processes: the host (net10.0) owns tools and planning; the worker (net48) owns Openness.
  Patching is a host concern; the worker request is unchanged.
- Content writes run on `Safety/Pipeline` (validate, binding, plan, guards, `dryRun`, mutate,
  verify, audit). One immutable item list feeds planning, mutation, verification and the audit
  (`WriteRun.cs`: `PlanAsync(Items)`, `MutateAsync(item)`, `RequestedOperations = ToElement(Items)`).
  No tokens (ADR-0004).
- Structured JSON contract rules in `AGENTS.md`: typed payloads, conditional members registered in
  `ConditionalMemberRegisterTests`, text and structured content from one `CanonicalJson.Serialize`.
- The server runs over stdio as the same Windows user as the client.
- Exports use CRLF. An XML bundle (`BlockBundleFormat.Compose`) adds LF-terminated `--- FILE:`
  separator lines and appends a bare LF to a non-last document that does not end in a newline, so the
  `.xml` part can end in LF.

## 4. Approaches considered

| Question | Options | Decision |
|---|---|---|
| Delivery | With a translation layer, or on today's formats | **Today's formats**; translator dropped for now |
| Patch base | The plan-time fresh export, or a server working copy | **Fresh export**: works for clients without file tools, adds no state |
| Patch anchor | Unique `oldText` → `newText`, or the issue's `startLine` + `expectedText` | **Unique `oldText`**: the pattern agents' own edit tools use; no line counting inside an escaped JSON string |
| Working copy | In this round, or after PR 3 is measured | **Deferred** (section 6) |
| Audit of a patch | The applied document, or the edits and hashes only | **The applied document**, as `update_block_logic` records today |

## 5. Design

PR order: PR 1 → PR 3 → PR 2. PR 3 needs PR 1's parser move. PR 2 is independent.

### PR 1: write-path trims and a shared bundle parser (no public contract change)

1. **Move the bundle parser to `TiaMcpServer.Contracts`.** `BlockBundleFormat`,
   `BlockImportBundleParser`, `ParsedBlockImportBundle`, `BlockImportDocument` and the `.xml`
   lookup of `BlockImportRouting` (Siemens-free) move so host and worker share one parser
   (`BlockBundleFormat` stays the sole owner of the format). These types throw
   `WorkerOperationException`, which lives in the worker, and `BlockImportCoordinator.CreatePreTargetFailure`
   maps any other exception type to `worker_operation_failed`. The move therefore introduces a
   Contracts-side failure type carrying the failure category and converts it at every worker site, so
   each refusal keeps its current category and message. Tests pin the categories (existing
   `BlockBundleFormatTests`/`BlockImportBundleParserTests` plus a coordinator test), and the test
   project's file links follow the move.
2. **One worker export instead of two.** For an XML write whose bundle carries companions,
   `BlockImporter.Import` exports once for `RequireContentHash` (`BlockImporter.cs:46`) and again for
   `EnsureOnlyAuthoritativeDocumentChanged` (`BlockImporter.cs:87`), in the same worker request.
   Reuse the first export string for the companion check. The check stays where it is (inside the
   coordinator's outcome tracking), so `BlockImportOutcome` keeps its shape. An XML-only submission
   never ran the companion export, so it gains nothing here.
3. **Host diff for an XML-only submission.** When the submitted content is a single SimaticML
   document (no separators, or a bundle holding only the `.xml`), `PlcWritePlanner` builds
   `contentDiff` against the `.xml` document of the current bundle; for a one-document bundle, the
   separator line is stripped from the requested side. The hash check is unchanged (it is over the
   whole served bundle). In that case `contentDiff.current` (`sha256`, characters, lines) describes the
   `.xml` part, not the served document; the PLC reference says so.
4. Tests: worker-side test that the companion check uses the precondition export (callback seam as in
   `BlockExporterSafetyTests`); host tests that an XML-only submission and a one-document bundle with a
   one-line change each yield a one-line diff.

### PR 3: `patch_block_logic`

A `plc_write` operation `{ operationId, operation: "patch_block_logic", blockPath, format?,
expectedContentHash, edits: [{ oldText, newText }] }`, one or more edits, blocks only (types are small
enough to resend).

**Base and checks.**

- Base: the plan-time export the host already makes for content writes, guarded by the
  whole-document `expectedContentHash` (from any `plc_read`). A mismatch is `state_changed`.
- Uniqueness scope: the document that is applied. For `format: xml` that is the `.xml` part of the
  bundle (found with the PR 1 parser); a bundle with no `.xml` (companion-only fallback) is refused
  with the existing "no Simatic ML document" guidance. For `format: source` it is the whole text.
- Each `oldText` is nonempty, not whitespace only, and must occur exactly once in the scope,
  counting overlapping occurrences as separate matches. Edits must not overlap, judged on the original
  offsets. Edits apply to the base, not to each other's results. A `newText` containing a bundle
  delimiter line (`ContainsDelimiterLine`) is refused.
- Line endings: matching normalizes terminators (`\r\n` and `\n` are equal); positions map back to the
  original text. `newText` is written with the matched region's terminator; when the region holds none
  or a mix, the terminator of the line containing the match start is used, else CRLF.
- Violations are `validation_error` naming the edit index and whether it matched zero or several
  times. They are found in planning, so, like every planner failure, they fail the whole call
  (`PlcWritePlanner.PlanAsync`), not only the item.

**Pipeline integration** (the review's blocker: the item list is immutable and the worker call reads
`op.Content`).

- The operation is normalized to `update_block_logic` at one point, right after the patch is resolved,
  so the existing name-keyed sites (`PlcPayloadContract.DecodeWrite`, `PlcWorkerInvoker`,
  `PlcVerificationPolicy`, `PlcWritePlanner`, `PlcWorkingState.Resolve`, `PlcFormatNames`, the
  `OperationPolicyCatalog` access check) see a known operation. The public catalog
  (`PlcOperationCatalog`) adds `patch_block_logic` with its own field set; the request-shape and access
  checks run on the public name before normalization. A test per name-keyed site proves a patch item
  reaches it as `update_block_logic`.
- The PLC write domain keeps the resolved document per `operationId` for the duration of the call:
  computed in `PlanAsync`, recomputed in `ReplanAsync` (edits re-applied to the fresh export for a
  dependent item), consumed by `MutateAsync` (passed to the worker as `content`, with the caller's
  `expectedContentHash`). The format is resolved with it, so mutation and read-back use the same format
  as planning.
- `contentDiff` compares the base with the resolved document, as for `update_block_logic`; it is the
  caller's check that the patch did what it meant.
- Audit: the requested operation (with its edits) is recorded as today, and the resolved applied
  document is recorded beside it for patch items, so the audit shows exactly what was applied. The
  builder picks the field and states it in the audit description.
- Contract: `plc_write` 2.0 → 2.1 (new operation, additive). New conditional members, if any, are
  registered in `ConditionalMemberRegisterTests`.
- Docs: `docs/SupportedOperations/` PLC reference, tool description, `AGENTS.md` PLC bullet,
  `docs/guides/mcp-client-configuration.md` (a content write no longer has to send the whole
  document).

### PR 2: relaxed JSON encoder for delivered documents

1. The escaping that reaches the model is done by the `Utf8JsonWriter` in `CanonicalJson.Serialize`
   (`JsonWriterOptions`); `WriteCanonical` re-emits strings through it, so changing only the serializer
   options changes nothing. Set `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` on that writer
   (and on the serializer options for consistency).
2. Effect: `<`, `>`, `&`, `'`, `+` and BMP non-ASCII text are delivered literally; `"` becomes `\"`
   instead of `"`. Non-BMP characters, U+2028/U+2029, `\` and control characters stay escaped.
3. The gain is in the text content block. `structuredContent` is re-serialized by the MCP SDK or the
   client. Before closing the `IMPROVEMENT_LOG.md` "escaping inflation" item, measure the gain on a real
   client (owner offered to test).
4. Same JSON values, different bytes: delivered-document hashes in the audit change for identical
   content. Contract versions stay unchanged. Noted in `docs/ARCHITECTURE.md` and the changelog. The
   documents are JSON tool results; a client that renders them as HTML loses escaping as a defense in
   depth, which is noted, not mitigated.
5. Budgets measure the serialized text, so XML and SCL items shrink against the 60,000/180,000
   budgets; no budget value changes.
6. Tests: a canonical document containing `<Network>`, `"` and `ä` serializes them literally (or as
   `\"`); update the test that pins an escaped form (`HmiGuardedWriteMcpTests.cs:44`). Worker IPC
   (`WorkerJson`) is out of scope.

## 6. Deferred: per-block working copy

A server-written file per block (default under `%LOCALAPPDATA%\TiaMcpServer\working-copies`,
overridable at startup), read with `output: "file"` and written by reference. It would remove the
remaining full-document read for clients with file tools and allow blocks above `--max-item-chars`.
Deferred by the owner until PR 3 has been measured live. The adversarial review found these must be
solved if it is resumed:

- Content resolved inside the binding lease and held per `operationId` (shared with PR 3's mechanism);
  `IWriteDomain.PlanAsync` gets the pinned snapshot.
- File names injective under Windows case-insensitivity and name normalization, or a flat unique
  layout; a host check that the XML declares the target block's name; no reparse-point traversal.
- A binding-transition observer (none exists; cursors check lazily), and read pinning
  (`RequireVerifiedBindingAsync` returns no snapshot). Same-project re-verification does not bump the
  binding revision.
- Instance directories: create under a temp name, lock, then rename; strict name pattern; an absolute
  root; deletable lock; files outlive a killed stdio server until the next start.
- Strict UTF-8 decoding (BOM stripped, anything else refused); hash over the decoded text; a hash name
  distinct from `contentHash`.
- Per-item, race-safe refresh after apply (compare-and-swap index, atomic file writes), and a rule for
  a re-read over local edits.
- Owner decisions then needed: leftover files after a crash; re-read over local edits; `plc_read`
  (annotated read-only) writing files.

## 7. Left to the builder

- Exact names of new internal types and files.
- The Contracts-side failure type of PR 1 and how worker sites convert it, within the stated rule.
- The audit field for the resolved patch document.
- Test granularity beyond the cases named above.

## 8. Calls made by Claude, approved by the owner

- PR 1 moves the bundle parser into `TiaMcpServer.Contracts`.
- PR 2 changes no contract version but changes audit hashes.
- `patch_block_logic` is normalized to `update_block_logic` at one point.
