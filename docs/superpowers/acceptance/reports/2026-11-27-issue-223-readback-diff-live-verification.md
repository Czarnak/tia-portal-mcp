# Issue #223: submitted versus observed content live verification

2026-11-27. **Verdict: scoped PASS.** Normalization reporting, XML/source patches, the equal-source
control, shared compilation, audit delivery and fixture cleanup passed. The submitted `LReal` was
re-exported as `Real`, and the
candidate exposed that difference without changing successful execution or verification.
The project remains open, modified and unsaved; the original `Main` export is unchanged.

## Candidate and target

- Code under test: `feat/issue-223-readback-diff`, commit
  `3ec5fa1eb71a8fe81be363b5b9cf01b7d3cf172e`.
- Repository apphost: `TiaMcpServer/bin/Debug/net10.0/TiaMcpServer.exe`, launched with
  `--access-mode full`. This is a development build reporting `TiaMcpServer 1.0.0.0`.
- Serialized solution build used `UseTiaPortalReferenceStubs=false` and the installed
  `Portal V21/PublicAPI/V21/net48` references; it completed with zero errors.
- Observed Portal executable product/file version: `2100.0.121.1`.
- Authorized standalone project: `<DisposableProjects>/MixOfEverything/MixOfEverything.ap21`;
  verified Portal PID `18676`. Initial public status reported open and `isModified:false`.
- Owned fixture group: `PLC_1/Blocks/Codex_I223_20261127`, containing FCs `I223_NeutralFbd`,
  `I223_SclA` and `I223_SclB`. Software `PLC_1` belongs to device
  `S7-1500/ET200MP station_1` (CPU 1516-3 PN/DP in the UI).
- MCP initialization negotiated protocol `2025-11-25`. Discovery returned 21 full-mode tools;
  `plc_write` exposed the conditional `readBack` schema. PLC write contract remained `2.1` and
  PLC read contract `1.0`.

Candidate artifact SHA-256 values:

| Artifact | SHA-256 |
| --- | --- |
| Apphost | `e20508538db9b0340ae25b82f1e6d60fdbe145ea4c6a50454640d8ecbc86760e` |
| Host DLL | `4ab0574e017533097a8b421e5006cb150c6d73dc18e59d26536d5604c429ad50` |
| Contracts DLL | `350d5bb44fd0db16c6947f3da439fcca2a1072b343f6333f95f2f0d51338fda2` |
| Copied net48 worker | `1b2bdb83794acf0767d0df74ff2d6f374434efb6a3d5ae2c6425c5356e790c4b` |

## Authorization and verification method

The user authorized starting V21 with this disposable project, live testing and accepting the
Openness dialog. The dialog identified the repository's copied worker and PID `18676`; the
one-time **Yes** action granted access. Engineering operations then used the candidate's registered
MCP tools through one verified binding. No PLC online connection, download or PLC-control action
was requested by the test client.

Before mutation, a public tree and the complete served XML bundle for `PLC_1/Main` were captured.
Each tested content write was followed by a separate same-format `plc_read`. Independent checks
recomputed the UTF-8 SHA-256 of the complete fresh export, compared it with verification
`actual`/`contentHash`, selected the authoritative XML document for XML comparisons, and checked
exact/newline equality, UTF-16 counts, side SHA-256, changed spans and excerpt text. Canonical text,
parsed structured content and exact audit response text/hash were checked independently. Patches
used the audit's exact `resolvedOperation.content` as the submitted execution text.

## Completed live cases

| Case | Observed evidence | Status |
| --- | --- | --- |
| Setup preview and non-content writes | One owned group and three FCs resolved to `PLC_1`; all four creates succeeded. Their verification checks were `trusted` and omitted `readBack`; preview verification was null. | Passed |
| Valid FBD baseline | A neutral FBD FC with Temp `val:Real`, `res:Bool`, `Ne` with `SrcType=Real`, literal `0.0` and a Coil on `res` imported and compiled with zero errors/warnings. The installed V21 XSDs validated its FlgNet/v5 and Interface/v5 sections before import. Native formatting differences were reported truthfully and matched a fresh export. | Passed |
| Full XML replacement | Changing only `ConstantType` from `Real` to `LReal` imported successfully and compiled with zero errors/warnings. Re-export changed it back to `Real`; one-line submitted/observed evidence retained successful root, item and verification outcomes. | Passed |
| Reusable full-export hash | The full replacement used the prior baseline write's verification hash; its fresh read matched. The following XML patch preview and actual call used the full replacement's verification hash successfully. | Passed |
| XML patch preview | Accepted the current whole-export hash, reported the planned one-line edit, and returned `verification:null` without comparison evidence or attempted resolved input. | Passed |
| Resolved XML patch | Public operation remained `patch_block_logic`; audit retained the original edit and exact XML-only `update_block_logic` execution input without edits. Read-back compared that resolved input, reported the same one-line reversal and matched a fresh export/hash. Import and compile succeeded with zero errors/warnings. | Passed |
| Resolved source patch | Public patch and original LF replacement were retained; the exact resolved source used surrounding CRLF terminators. The later export changed four leading spaces to a tab plus three spaces on the inserted comment line. `wholeText` evidence reported that exact one-line difference without changing successful execution/verification. | Passed |
| Equal source control | The other FC's unchanged 104-character source round-tripped exactly: raw and newline-normalized equality true, changed spans zero and empty excerpts. Fresh hash matched the original and verification. | Passed |
| Shared compilation | The two source operations returned `compilationMode:"shared-per-plc"`, per-item `compileStage:"deferred"` with null per-item compile reports, and one succeeded `PLC_1` shared report with zero errors/warnings. Both final observations matched independent fresh reads. | Passed |
| Cleanup and preservation | Preview named exactly the three owned FCs and zero child groups; deletion succeeded. All three probes were absent afterward, `Main` bytes/hash were unchanged, and the final 12-node `PLC_1` subtree matched the baseline. The project remained open, modified and unsaved. | Passed |

## Exact normalization observation

For the full replacement and resolved XML patch, selected submitted XML contained:

```xml
<ConstantType>LReal</ConstantType>
```

The independently read selected XML contained:

```xml
<ConstantType>Real</ConstantType>
```

Replacing that single submitted token produced the observed selected XML byte-for-byte. Both
responses reported `comparisonScope:"xmlDocument"`, `rawTextEqual:false`,
`normalizedLinesEqual:false`, `lineEndingOnly:false`, changed-span counts `1`/`1`, line-69
excerpts in the correct direction, `budgetExhausted:false` and `omission:null`. Submitted/observed
UTF-16 lengths were `7347`/`7346`, with 195 lines on both sides. Root success, succeeded item status
and verification success remained true; verification `expected` remained null.

Diagnostic selected-text SHA-256 values were
`419550fd333eb7d3cd4c459ec965c3d7acf0a9aa0c743fbe3823b5e9d9d37eeb` (submitted) and
`9e36cea9a275cb1fc6e46f3c9d311fd48793d38363c2e3c9c424428a8541520d` (observed).
These are not concurrency hashes. Each complete fresh export independently hashed to its returned
verification `actual` and `contentHash`:

| Write | Whole-export hash |
| --- | --- |
| Real baseline | `xml:sha256:8fbd4944724e1287ea683f63f2316854e798c3302c3ae0929d87f675f4a46330` |
| LReal full replacement | `xml:sha256:5d80efc783e48b811022a32184fedd90dcd5e0f434d56c9ad90aecd7648c0625` |
| LReal resolved XML patch | `xml:sha256:1ccd628778cf7bb4ea4f11c3211fd6d9d6da07d5b851c5a78f5249cdbfc1192c` |

The selected observed XML was identical between the Real baseline and LReal replacement, while
the `.s7dcl` and `.s7res` companions changed their generated multilingual identifiers. Thus the
different whole-export hashes coexist with identical observed XML; the comparison correctly
excluded those companion changes while the reusable hash continued to cover them.

## Source and shared-compilation observations

The source batch combined a patch of `I223_SclA` with an unchanged full-source update of
`I223_SclB`. For the patch, the resolved attempted source and observed source were both 135 UTF-16
characters and 9 lines. Line 6 changed from `"    // I223 live source patch"` to
`"\t   // I223 live source patch"`. Raw and newline-normalized equality were false,
`lineEndingOnly` was false, and both changed-span counts were one. This correctly retained a
whitespace difference; newline normalization does not remove indentation differences. The fresh
whole-export hash was
`source:sha256:2061ceac85354f7f4f3f9d5602b8cb8741e9c3ac7207c623ed007812db110425`, matching
verification `actual` and `contentHash`.

The unchanged source control retained its exact 104 characters and 8 lines, with empty excerpts
and hash `source:sha256:5668706c709b2d0ec76705cbf4af2d1086219f0873056482e69f953604bcd820`.
The two per-item deferred outcomes and one PLC-scoped shared report are the compilation evidence;
both independent reads observed the final content after the call completed. These observations do
not establish which import, compile or export stage introduced the source indentation difference.

## Cleanup, final state and evidence retention

The deletion preview listed only `I223_NeutralFbd`, `I223_SclA` and `I223_SclB`, with no contained
groups and one informational `plc_deletes_group_contents` guard. Actual group deletion succeeded
with trusted verification. A final four-item read returned the original `Main` successfully and
`target_not_found` for each removed probe. Its root `success:false` is the expected aggregation of
those three absence probes, not a preservation failure. The original served Main bundle remained
byte-for-byte identical, including hash
`xml:sha256:f94beec10c771b559e64f79abd7b12edf69a23658339e38343fde14d9f85ef8a`.

The final complete bounded `PLC_1` tree matched all 12 initial node paths/types/details, with no
continuation cursor or owned fixture. Public status reported the same open project with
`isModified:true`; the persisted last-modified timestamp was unchanged. No save or close was
called. Deleting test objects does not establish a clean in-memory project. The test client was
detached after these checks, and the GUI project remained open.

Independent checks covered 24 canonical tool receipts and nine exact matching audit documents and
response hashes. All nine audits retained the same verified binding, audit v2 and write contract
`2.1`; confirmation `by` was `policy` for actual calls and `none` for previews, with
`outcome:"not_requested"`. Independent review found no unresolved evidence discrepancy. Raw MCP
receipts, selected audit rows, temporary helpers and exported probe data are private temporary
evidence outside the repository at `%TEMP%/tia-mcp-issue223-live-20261127-3ec5fa1e`; this report
retains verified summaries and artifact/content provenance only.

## Offline qualification

The implementation previously passed 9,086 non-packaging tests. An additional domain-level budget
regression passed separately and rejected three deliberate allowance-reset mutations; after
restoration all 57 read-back/patch/audit regressions passed. The stub solution build and this
installed-V21-reference solution build passed. Those offline checks are separate from the live
observations above.

## Limits

This is a round-trip observation through the candidate's live import/compile/export pipeline. It
does not identify whether import or compilation established the change, or establish functional
equivalence. The neutral fixture reproduces the missing read-back report described in
[issue #223](https://github.com/Czarnak/tia-portal-mcp/issues/223#issuecomment-6687535253); it does not
recreate the original history in which a block's own export already contained `LReal` and a preview
failed to reveal an additional change.

Other Portal versions, CPU families, local sessions, type writes, native fault interleavings,
delivery-budget exhaustion, concurrent edits, saving/persistence and hardware acceptance are not
established by these live cases. Budget/protocol/failure-path coverage remains offline evidence.
