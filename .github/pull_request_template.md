<!--
PR title: use a conventional commit, for example `feat(network): add subnet rename`.
Types and scopes are listed in CONTRIBUTING.md.

Tick every box that is true. In "Contract and write-safety impact", delete the items
that do not apply instead of leaving them unticked.
-->

## Summary

<!-- What changed and why. Link the design or plan document if there is one. -->

## Related issue

<!-- `Closes #123` closes the issue when this PR merges; `Refs #123` only links it. -->

## Type of change

- [ ] `feat`: new feature
- [ ] `fix`: bug fix
- [ ] `refactor`: restructuring without behavior change
- [ ] `perf`: performance improvement
- [ ] `test`: test-only change
- [ ] `docs`: documentation only
- [ ] `build`: .NET SDK, project files, or packaging
- [ ] `ci`: GitHub Actions workflows
- [ ] `chore`: other maintenance
- [ ] **Breaking change**: changes a tool's input or output contract or the CLI behavior (migration notes are in the Summary)

## Components touched

- [ ] Host (`TiaMcpServer/`)
- [ ] Contracts (`TiaMcpServer.Contracts/`)
- [ ] Openness worker (`TiaMcpServer.OpennessWorker/`)
- [ ] Tests or FakeWorker (`TiaMcpServer.Tests/`, `TiaMcpServer.FakeWorker/`)
- [ ] Documentation (`docs/`, root `*.md`)
- [ ] CI, packaging, or scripts (`.github/`, `scripts/`, project files)

## Verification

- [ ] `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true` succeeds
- [ ] `dotnet test TiaMcpServer.Tests` passes locally
- [ ] New or changed behavior is covered by tests (xUnit, hand-written fakes, `{ClassUnderTest}Tests.cs`)

Live TIA Portal check (tick one; the first is required when the Openness worker is touched, unless the maintainer verifies instead):

- [ ] Tested against a real TIA Portal V21 installation. Update level: <!-- e.g. V21 Update 1 -->. Acceptance report: <!-- path or n/a -->
- [ ] Stub build only; the maintainer verifies on a real installation before merge
- [ ] Not applicable; no runtime behavior change (docs, CI, or tests only)

## Contract and write-safety impact

<!-- Delete the items that do not apply. -->

- [ ] No change to any MCP tool's input schema, output schema, or response shape
- [ ] Tool contract changed: migration notes are in the Summary and the matching `docs/SupportedOperations/` page is updated
- [ ] Write path touched: preview-then-apply is preserved, and the safety token still binds the tool name, requested input, current project state, and host binding revision
- [ ] Write path touched: a successful apply still appends an audit record
- [ ] Read-only mode still exposes only the observation tools; no new write capability leaks into it
- [ ] New worker method: added to the dispatch in `TiaMcpServer.OpennessWorker/Program.cs` and registered in its domain catalog and invoker
- [ ] Structured JSON tool: built on `StructuredToolResult` / `CanonicalWriteSafety`; `content` and `structuredContent` come from one `CanonicalJson.Serialize` call; worker payloads are typed; no nested JSON strings
- [ ] No `Siemens.Engineering*.dll` added to the repository or the package

## Documentation

- [ ] User-visible changes are documented (`docs/guides/`, `docs/SupportedOperations/`)
- [ ] Every new file under `docs/` is listed in `docs/README.md`
- [ ] Links added to `README.md` are absolute `https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URLs
- [ ] `docs/ARCHITECTURE.md` is updated if the design changed, and open follow-ups are recorded in `docs/IMPROVEMENT_LOG.md`

## Notes for the reviewer

<!-- Risky parts, anything you are unsure about, and follow-ups deliberately left out of this PR. -->
