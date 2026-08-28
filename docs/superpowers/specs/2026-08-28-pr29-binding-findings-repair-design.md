# PR #29 Binding Findings Repair Design

**Date:** 2026-08-28

**Status:** Approved for implementation planning
**Scope:** The three confirmed PR #29 review findings only

## Goal

Repair the deterministic project-binding implementation without weakening its safety model:

1. Permit ordinary unbound reads while the server runs in read-write mode.
2. Make FakeWorker-backed network write tests establish and enforce a worker-observed project identity.
3. Make same-path `forceRebind=true` create the fresh unverified binding revision promised by `ProjectSessionBinding.Bind`.

## Non-goals

- Redesigning the project-binding architecture or safety-token format.
- Changing MCP tool schemas or preview/apply workflows.
- Fixing unrelated post-rebase integration-test flakes.
- Performing live TIA Portal mutations.
- Replacing FakeWorker with a complete TIA session simulator.

## Confirmed root causes

### Unbound reads fail in read-write mode

`TiaMcpServer.OpennessWorker/Program.cs` currently permits a missing
`ExpectedSessionIdentity` for every operation only in read-only mode. In read-write mode it
permits only `get_project_status`, `open_project`, and `create_project`. This contradicts the
wire contract, which allows a null identity for unbound reads, and rejects ordinary reads such
as `read_hardware_config` before their deterministic project selection can run.

### Network tests manufacture identity

`NetworkVerifiedWriteFixture` probes the neutral `ok` scenario, copies its worker session id,
generation, and Portal PID, replaces its project path with the requested scenario path, and
calls `BindVerified`. FakeWorker does not validate the expected identity on later protected
requests. Tests can therefore pass even though the worker never reported the target scenario
path as its active project.

### Same-path forced rebind is ignored

`ProjectSessionBinding.Bind` returns early whenever the canonical path matches the current
configured or verified path. The early return does not consider `forceRebind`, so a same-path
forced rebind retains the verified identity, binding id, and revision despite the method's
documented behavior.

## Architecture decision

Keep the existing binding architecture and make `OperationPolicyCatalog` the single authority
for whether a worker operation may omit `ExpectedSessionIdentity`.

Add a fail-closed method named `RequiresExpectedSessionIdentity(string operation)` with these
semantics:

| Operation category | Identity requirement |
| --- | --- |
| `Observe` | Optional |
| `TemporaryExport` | Optional |
| `open_project` | Optional because it establishes binding |
| `create_project` | Optional because it establishes binding |
| Compile, lifecycle probes, mutations, and online control | Required |
| Unknown or unclassified operation | Required |

The `hello` protocol exchange remains outside this policy and continues to run before ordinary
worker dispatch. Access-mode authorization remains a separate gate; allowing an unbound read
does not grant any additional read-only permissions.

The real worker and FakeWorker must consume the same policy. This avoids maintaining a second
allowlist in test infrastructure and makes unknown operations fail closed in both processes.

## Component changes

### Shared contracts

Extend `TiaMcpServer.Contracts/OperationPolicyCatalog.cs` with
`RequiresExpectedSessionIdentity`. Do not introduce a parallel catalog or duplicate the
operation classifications.

Update `ProjectSessionBinding.Bind` so the same-path no-op applies only when
`forceRebind=false`. A same-path forced rebind clears verified identity and transitions to
`ConfiguredUnverified`, which rotates the binding id and revision through the existing state
transition mechanism.

### Real worker

Change `TiaMcpServer.OpennessWorker/Program.cs` so its missing-identity decision delegates to
`OperationPolicyCatalog.RequiresExpectedSessionIdentity`. Existing protocol-version checks,
authorization, deterministic target selection, post-resolution identity validation, and
immediate-before-mutation validation remain unchanged.

### FakeWorker

Before executing a protected scenario, FakeWorker must:

1. Apply the shared missing-identity policy.
2. Reject a required but absent identity with `binding_conflict`.
3. When an identity is supplied, compare worker session id, generation, Portal PID, and canonical
   project path against FakeWorker's tracked session state.
4. Reject a non-transition request whose project path differs from the verified expected path.
5. Return `binding_conflict` before scenario execution when any comparison fails.

An optional identity is not an ignored identity. If `open_project` or `create_project` carries an
expected identity during a deliberate transition, FakeWorker still validates the supplied
identity against the pre-transition session.

The first successful unbound read may establish FakeWorker's tracked project identity for later
protected calls. A manually constructed host binding that was never observed by that FakeWorker
process must fail.

### Network verified-write fixture

Replace the neutral `ok` probe with `ReadHardwareConfigAsync(projectPath)`, reusing the target-path
state read already required by network write previews.

Fixture setup must require:

- a successful worker response;
- a complete `SessionIdentity`;
- a canonical identity project path equal to the canonical requested project path.

It then passes that exact object to `BindVerified` without constructing or rewriting any field.
If the read or identity check fails, fixture setup fails and the binding remains unbound.

## Runtime data flow

### Ordinary unbound read

1. The host resolves the requested path while its binding snapshot is unbound.
2. It sends `ExpectedSessionIdentity=null`.
3. The shared policy permits the read.
4. The worker selects the target deterministically and performs the read.
5. The worker stamps the observed session identity on the response.
6. The host validates the response but does not bind an ordinary unbound read.

### Verified operation

1. The host sends the complete identity retained by its verified binding or safety token.
2. The worker validates the identity before operating on the selected project.
3. Any missing or mismatched field produces `binding_conflict` before a protected operation.
4. The real worker retains its existing post-resolution and immediate-before-mutation checks.
5. The host validates the response identity and existing binding-transition rules.

### Same-path forced rebind

1. `Bind(path, forceRebind:true)` validates and canonicalizes the requested path.
2. It clears the verified worker identity.
3. It transitions to `ConfiguredUnverified` with a new binding id and revision.
4. Safety tokens retaining the previous snapshot no longer match and are rejected.

Production `open_project` behavior remains worker-grounded: it uses `CanBind`, invokes the worker,
and calls `BindVerified` only after a successful response. The `Bind` correction does not make
`open_project` discard a valid binding before the worker succeeds.

## Error handling

- Missing required identity: `binding_conflict`.
- Mismatch in worker session id, generation, Portal PID, or canonical project path:
  `binding_conflict`.
- Protected request path inconsistent with expected identity: `binding_conflict`.
- Failed target-path fixture read: explicit fixture setup failure; no fallback probe.
- Missing or incomplete fixture identity: explicit fixture setup failure.
- Fixture identity path different from requested path: explicit fixture setup failure; no bind.
- Unknown operation without identity: fail closed with `binding_conflict` after existing protocol
  and access checks.

Errors must not echo rejected worker payloads or fabricate replacement identity fields.

## TDD strategy

Implement the repair as three independent RED-GREEN cycles.

### Cycle 1: missing-identity policy

Add tests that:

- cover every operation capability category;
- allow `Observe`, `TemporaryExport`, `open_project`, and `create_project` to omit identity;
- require identity for compile, internal lifecycle probes, mutations, and online control;
- require identity for unknown operations;
- prove ordinary unbound reads succeed in read-write mode; and
- prove two unbound reads against different project paths leave the host binding unbound.

The shared-policy test must fail before production policy is changed.

### Cycle 2: honest FakeWorker and network fixture

Add tests that:

- prime FakeWorker with a target-path unbound hardware read;
- vary each expected identity field independently and receive `binding_conflict`;
- reject a protected request path different from the expected path;
- prove fixture binding uses the exact returned target-path identity;
- simulate a different resolved path and prove fixture setup fails while binding stays unbound;
- preserve all existing network write and subnet-lifecycle behavior with the honest fixture.

The fixture test must first demonstrate that the current synthetic path replacement is accepted.
The FakeWorker tests must demonstrate the current absence of enforcement before adding it.

### Cycle 3: same-path forced rebind

Start from a verified binding, capture its snapshot, and prove that the current same-path
`forceRebind=true` call incorrectly preserves it. After the fix, assert:

- state is `ConfiguredUnverified`;
- verified identity is cleared;
- canonical path is retained;
- binding id and revision change;
- `TryGetVerified` fails; and
- a safety token retaining the old snapshot is rejected.

Retain coverage proving that same-path `forceRebind=false` preserves the exact verified snapshot.

## Verification

Run focused tests after each RED-GREEN cycle, followed by:

```powershell
dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true
dotnet test TiaMcpServer.Tests/TiaMcpServer.Tests.csproj -c Debug --no-restore -m:1 --disable-build-servers
git diff --check
git status --short
```

The two unrelated integration-test flakes observed immediately after rebasing are not repair
targets. If one recurs, report it separately, rerun that exact test, and do not modify unrelated
tests under this scope.

No live TIA evidence is required to complete the implementation. A later, separately authorized
read-only acceptance check may run an ordinary unbound read while the server is configured for
read-write access. No live project mutation is included.

## Acceptance criteria

- Ordinary unbound reads work in both access modes where the operation itself is authorized.
- Writes and other protected operations require an exact expected identity.
- Unknown operations fail closed.
- Real worker and FakeWorker share the same identity-requirement policy.
- Network write fixtures never construct or rewrite worker identity fields.
- FakeWorker rejects missing or mismatched protected-operation identities before scenario execution.
- Same-path `forceRebind=true` rotates to a fresh configured-unverified binding revision.
- Existing preview/apply, safety-token, deterministic selector, audit, and lifecycle safeguards remain intact.
- Targeted tests, stub build, and repository diff checks pass; full-suite results are reported exactly.
