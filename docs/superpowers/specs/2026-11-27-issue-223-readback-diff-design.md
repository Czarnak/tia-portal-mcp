# Issue #223: submitted versus observed PLC content

Status: implemented and qualified offline; [scoped live qualification](../acceptance/reports/2026-11-27-issue-223-readback-diff-live-verification.md) completed on 2026-11-27

Issue: <https://github.com/Czarnak/tia-portal-mcp/issues/223>

Branch: `feat/issue-223-readback-diff`; inspected baseline: `e19e46599681`.

Release baseline: the latest published release is [v4.4.0](https://github.com/Czarnak/tia-portal-mcp/releases/tag/v4.4.0), whose PLC write contract is `2.0`. The branch already has unreleased PLC write contract `2.1` from earlier PRs. Issue #223 joins that unreleased contract without another bump; PLC reads stay at `1.0`.

## Problem

The caller can submit a small logical change, receive a successful write, and remain unaware that the subsequent export differs elsewhere. `PlcVerificationPolicy.ReadBackAsync` already holds the execution request's content and a fresh export, but publishes only the observed export hash. The issue reporter supplied a neutral-block v4.3.0 reproduction in which an FBD constant's `LReal` text became `Real`; this is reported evidence, not verification performed for this plan, and its functional effect has not been assessed.

## Users

MCP callers need compact evidence of the text that currently exists after a content write. Maintainers need the evidence to preserve existing mutation, verification, concurrency-hash, canonical serialization, and delivery-budget semantics.

## In scope

- [S-1] Add optional, typed `verification.operations[].readBack` evidence for `update_block_logic`, resolved `patch_block_logic`, and `update_type_content` when a nonempty re-export is available.
- [S-2] Compare like-for-like selected texts with explicit `xmlDocument` or `wholeText` scope; expose exact and newline-normalized equality plus bounded submitted/observed excerpts.
- [S-3] Retain existing execution success, verification success, actual full-export hash, supersession, failed-attempt observation, and shared-compilation ordering.
- [S-4] Share the existing excerpt allowance across `TiaMcpServer/Plc/PlcWritePlanner.cs` initial/dependent resolution and `TiaMcpServer/Plc/PlcVerificationPolicy.cs` read-back; preserve informative summaries when excerpts cannot be retained.
- [S-5] Budget comparison diagnostics before existing verification checks, with explicit omission evidence and truthful delivery success.
- [S-6] Include the additive evidence in existing unreleased PLC write contract `2.1`, update schema discovery and `docs/SupportedOperations/PLC_OPERATIONS_SUMMARY.md`, and verify the change with meaningful offline tests.

## Out of scope

This work does not diagnose or change Siemens import normalization, infer functional equivalence, automatically restore submitted content, introduce another engineering call, change worker payloads, change `importOutcome.contentRelation`, or change PLC read contract `1.0`. Live TIA mutations and remote GitHub writes require later authorization and are not part of preparing or executing this offline implementation plan.

## Constraints

- Host and worker remain separate processes; this change adds no worker method, worker capability, or Siemens API call.
- Reuse existing unreleased PLC write contract `2.1`; issue #223 adds no further contract-version bump. Keep PLC read contract `1.0` and existing version assertions unchanged.
- Compare the resolved execution content, including resolved patches, with the existing nonempty read-back export under the existing binding lease.
- A mismatch alone never changes mutation status, verification success, `expected`, `actual`, or `contentHash`.
- Both equality flags describe the selected comparison scope; do not add a second whole-export equality flag.
- `normalizedLinesEqual` normalizes CRLF and lone CR to LF only; it makes no XML, whitespace, or semantic-equivalence claim.
- Use one call-owned `PlcContentDiffBudget`: 40 excerpt lines and 8,192 raw characters per side, at most 512 characters per line, and 320 excerpt lines and 32,768 raw excerpt characters across the call.
- Use effective item/document limits owned by `TiaMcpServer/OperationBatches/StructuredOperationBatchPayloadBudget.cs` for canonical encoded delivery size; defaults remain 60,000 and 180,000 characters.
- Keep shared `WriteOperationVerification`, `WriteVerification`, and `TrustedWriteVerifier` contracts unchanged.
- Text and `structuredContent` continue to derive from one canonical serialization; audit retains the exact delivered document and execution truth.
- The implementation is Windows/.NET 10 host code. Use existing Siemens-free FakeWorker tests; serialize solution builds with `-m:1`.

## Interfaces

`PlcOperationVerification` gains an init-only nullable `ReadBack` property, omitted when null and described in the conditional-member register. Its existing positional constructor remains unchanged. `PlcReadBackEvidence` contains the following required members, including explicit nullable `omission`:

| JSON member | Type and meaning |
| --- | --- |
| `comparisonScope` | `xmlDocument` when both selected texts are their XML documents; otherwise `wholeText`. |
| `rawTextEqual` | Exact ordinal equality of the selected texts. |
| `normalizedLinesEqual` | Equality after the documented newline conversion. |
| `lineEndingOnly` | Raw unequal and newline-normalized equal. |
| `submitted`, `observed` | Existing `PlcContentDiffSide` shape: diagnostic SHA-256, character count, line count, and bounded excerpt. |
| `submittedChangedSpanLineCount`, `observedChangedSpanLineCount` | Line counts between the first and last difference after common prefix/suffix removal; these include unchanged intervening lines. |
| `budgetExhausted` | Whether the common generation allowance was exhausted when evidence was built; inspect excerpt omission counts to learn what was omitted. Equal text can report true after earlier exhaustion while omitting zero lines/characters. |
| `omission` | Explicit null normally; `StructuredOperationOmission` when outer response budgeting removes retained evidence. |

Side `sha256` values describe selected text and are diagnostic only. They are not format-tagged concurrency hashes and must never replace `actual`/`contentHash`, which continue to cover the whole served export.

## Data

Comparison evidence is owned by the per-call PLC policy in an ordinal operation-ID dictionary and joined into the PLC response projection after shared verification. A per-call domain-owned excerpt budget is passed to initial planning, late replanning, and verification. Nothing new is persisted separately; the existing canonical response and audit flow carry delivered evidence.

## Edge cases

- Raw XML and single-XML-document submissions compare with the XML member of an observed bundle, without separator/companion noise. Multi-document submissions, source, and parse/companion-only fallbacks compare whole text.
- Failed content attempts can produce evidence about currently observed content; this does not prove import success or the cause of a difference.
- Successful items marked `superseded` have no comparison. Non-content items, previews, skipped/denied items, and empty/failed exports have no comparison.
- Existing shared compilation completes before content verification; observed differences can therefore include changes established after compilation.
- Earlier planning or replanning can exhaust the common excerpt allowance before read-back. Comparisons still carry flags, counts, and side hashes; spent allowance is not refunded when evidence is later replaced or omitted.
- Bounded excerpts can omit a particular difference. Changed-span counts are not an edit script or an exact count of unequal lines.

## Failure modes

Re-export/transport failure follows existing verification and binding behavior and yields no fabricated comparison. Generation truncation uses the existing excerpt omission counts and `budgetExhausted`; it does not itself fail execution or verification. Outer delivery-budget removal records explicit omission and follows the existing convention that root delivery `success` may become false while item status and verification success remain true. Comparison excerpts yield before comparison summaries, and comparison summaries yield before the existing aggregate verification checks. Copies used for budgeting must not mutate the original execution response.

## Acceptance criteria

- [x] [S-1] A successful unequal import retains all success values and provides oriented excerpts; proved by `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~ReadBackMismatchDoesNotFailSuccessfulImport`; fails if mismatch fails the write or either side is reversed. Expected exit code: 0.
- [x] [S-1] [S-3] Patches and both type formats compare execution content and retain the fresh reusable hash; proved by `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~PatchExecutesExistingImportAndVerifiesResolvedFormat|FullyQualifiedName~TypeReadBackUsesSubmittedExecutionContent"`; fails if public patch edits or a partial-document hash replace the execution text/full-export hash. Expected exit code: 0.
- [x] [S-2] XML companion packaging, actual constant differences, and newline-only changes remain distinguishable; proved by `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~PlcReadBackComparisonTests`; fails if identical selected XML is reported unequal or normalization conceals the `LReal`/`Real` difference. Expected exit code: 0.
- [x] [S-2] Whole-text fallbacks and changed-span counts retain their specified meanings; proved by `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~UsesWholeText|FullyQualifiedName~DisjointDifferencesCountTheWholeChangedSpan"`; fails if a fallback claims XML-only comparison or disjoint differences count only unequal lines. Expected exit code: 0.
- [x] [S-3] Failed attempts keep their failure and can expose observations; superseded/unavailable/preview/non-content entries omit comparison; proved by `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~PlcGuardedWriteDomainTests`; fails if a comparison changes an existing outcome or appears without a nonempty re-export. Expected exit code: 0.
- [x] [S-3] Comparison observes deliberate compile-time transformation; proved by `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~ReadBackObservesSharedCompileTransformation`; fails if comparison is taken before shared compilation. Expected exit code: 0.
- [x] [S-4] Planning, late replanning, and verification share one allowance while summaries survive exhaustion; proved by `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ReadBackSharesPlanningAndReplanningExcerptAllowance|FullyQualifiedName~GenerationExhaustion"`; fails if any phase silently resets the common allowance or loses hashes/counts. Expected exit code: 0.
- [x] [S-5] Encoded limits discard diagnostics before existing hashes and keep execution truth; proved by `dotnet test TiaMcpServer.Tests --filter FullyQualifiedName~PlcWriteBudgetTests`; fails if new diagnostics cause deliverable hashes to be discarded first or omission does not affect delivery success. Expected exit code: 0.
- [x] [S-5] Size limits, omission accounting and immutability hold; proved by `dotnet test TiaMcpServer.Tests --filter "FullyQualifiedName~ReadBackUsesEncodedSizesAndEffectiveLimits|FullyQualifiedName~ReadBackOmissionAccountsForRetainedAndAlreadyOmittedText|FullyQualifiedName~ReadBackBudgetingDoesNotMutateExecutionResponse"`; fails if canonical size exceeds limits, counts double-count removed characters, or the original response changes. Expected exit code: 0.
- [x] [S-6] Protocol schemas, canonical/audit delivery and offline qualification agree; proved by `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"` and `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`; fails if either schema path omits the contract addition, canonical representations diverge, or a required check fails. Expected exit code: 0.

## Open questions

<!-- All decisions are recorded above; no questions remain. -->
