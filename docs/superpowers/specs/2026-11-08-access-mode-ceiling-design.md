# Admin-owned access-mode ceiling, hardening guide and README trim (PR 2 of 2)

**Date:** 2026-11-08
**Size:** project (full design)
**Branch:** `claude/access-mode-ceiling`
**Depends on:** PR 1 (`claude/read-only-default-mode`, spec
`2026-11-08-read-only-default-design.md`). PR 2 reuses PR 1's `AccessModeSource` in its messages,
so rebase onto `main` after PR 1 merges.

## Intent

The access mode is a startup argument chosen by whoever starts tia-mcp. Any process running as the
same Windows user (for example an agent with shell access) can start `tia-mcp --access-mode full`,
or start the Openness worker directly, and get every capability, whatever the user configured in
their MCP client.

The TIA Portal Openness firewall does not close this gap. It identifies the **binary** that loads
`Siemens.Engineering` (path, modification date, SHA-256), not the process that started it. Once the
tia-mcp worker is allow-listed, any caller can start it with any flags.

PR 2 lets an administrator set a **maximum access mode** that tia-mcp cannot exceed, whoever starts
it and with whatever flags. The firewall decides *which* binary may connect; the ceiling decides
*what* that trusted binary may do. A modified or rebuilt binary changes its hash and triggers the
Portal firewall prompt again.

PR 2 also adds a user-facing hardening guide and trims the README to a landing page.

## Goals

1. An administrator can cap the access mode machine-wide with a registry value only an
   administrator can change.
2. The host and the worker each enforce the ceiling independently. Starting either one above the
   ceiling fails before any TIA Portal access.
3. Users always see whether a ceiling is configured: at startup, in `doctor`, and in the docs.
4. tia-mcp only **reads** the ceiling. It has no code that writes, creates or deletes registry keys.
5. A hardening guide explains the threat, the Openness firewall, installation location, the
   ceiling and its limits.
6. The README becomes a short landing page with a table of contents, without losing information.

## Non-goals

- Configuring or restricting agents or MCP clients. That is the user's responsibility; the guide
  says so in one line.
- A separate Windows account or a non-stdio transport (named pipe, HTTP).
- Changing what each mode permits, the tool surface per mode, or the precedence of requested
  modes (CLI > environment > default).
- Making TIA Portal's own firewall stricter or managing its allow list.
- A per-user or per-project ceiling.

## Anti-goals

- **No registry write path in tia-mcp.** A bug in tia-mcp must not be able to raise the ceiling.
- **Never read the ceiling from a user-writable place:** not HKCU, not an environment variable,
  not a file in the user profile. A parent process can rewrite all of those.
- **A ceiling problem must never grant more access.** An unreadable or malformed ceiling counts as
  `read-only`.
- **No silent downgrade.** A request above the ceiling fails with a clear error; it is never quietly
  lowered.
- **No README facts lost.** Every fact removed from the README must already exist in, or be moved
  to, the reference docs.

## Constraints

- Windows only. The host is `net10.0`, the worker `net48`, shared code `netstandard2.0`
  (`TiaMcpServer.Contracts`, which must stay free of Siemens and Windows dependencies).
- `HKLM\SOFTWARE` is writable only by administrators by default. The ceiling's protection comes
  from that ACL, not from tia-mcp.
- The host speaks MCP over stdout, so every startup message goes to stderr.
- The host passes `--access-mode` to the worker (`OpennessWorkerClient.Invocation.cs`); the worker
  can also be started directly.
- The existing `IRegistryService.GetStringValue` returns `null` for a missing key, a missing value
  **and** a value of the wrong type, and lets access errors escape. The ceiling must tell these apart.
- `README.md` is also the NuGet package readme: every link in it must be an absolute
  `https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URL.
- CI runs build and tests on `windows-latest`.

## Approaches considered

**A. Shared rules in Contracts, one small registry reader per process (chosen).** The decision
logic lives in `TiaMcpServer.Contracts`: map a registry reading to a ceiling, compare a requested
mode against it, and produce the messages. The host and the worker each read the registry with
their own reader of about ten lines. One copy of the rules, testable without a real registry, and
no new dependency in Contracts.

**B. The host reads the ceiling and passes it to the worker as an argument.** Rejected: a worker
started directly would trust whatever argument it is given, which is the bypass being closed.

**C. One shared registry reader inside Contracts.** Rejected: it needs the
`Microsoft.Win32.Registry` package in a `netstandard2.0` project meant to stay platform-neutral, to
save about ten lines.

Also rejected during discussion:

- **Environment variable as the ceiling:** a parent process can set or remove any variable for its
  children, so tia-mcp cannot tell an administrator's value from an agent's.
- **tia-mcp writing the ceiling** (e.g. `tia-mcp policy set`): adds a write path to the attack
  surface for no gain; administrators already have `reg`, Group Policy and Intune.
- **A missing key meaning `read-only`:** the user chose opt-in (no key = no ceiling), with the
  missing ceiling stated clearly.

The user picked approach A.

## Design

### 1. Registry location and values

| Item | Value |
| --- | --- |
| Hive and view | `HKEY_LOCAL_MACHINE`, always `RegistryView.Registry64` |
| Key | `SOFTWARE\Policies\TiaMcpServer` |
| Value name | `MaxAccessMode` |
| Value type | `REG_SZ` |
| Valid data | `read-only`, `read-write`, `full` (parsed with `McpAccessModeNames.TryParse`) |

`SOFTWARE\Policies` is the standard place for administrator policy and is what Group Policy and
Intune write to. The 64-bit view stops 32-bit redirection to `WOW6432Node` from making a process
read a different place.

### 2. Reading result (Contracts)

The readers report what they saw as plain data; Contracts decides what it means.

```text
AccessModeCeilingReading
  Kind:      NotConfigured | Value | WrongType | Unreadable
  RawValue:  string?   (Kind = Value)
  Detail:    string?   (WrongType: the registry value kind; Unreadable: the exception type name)
```

- `NotConfigured`: the key or the value does not exist.
- `Value`: a `REG_SZ` value exists (it may still be invalid text, including empty).
- `WrongType`: the value exists but is not `REG_SZ` (e.g. `REG_DWORD`, `REG_EXPAND_SZ`).
- `Unreadable`: opening the key or reading the value threw (`SecurityException`,
  `UnauthorizedAccessException`, `IOException`). Readers catch these three and nothing broader.

### 3. Resolution and check (Contracts, pure)

`AccessModeCeiling.Resolve(reading)` returns:

| Reading | `Ceiling` | `Status` |
| --- | --- | --- |
| `NotConfigured` | `null` (no ceiling) | `notConfigured` |
| `Value`, valid name | that mode | `configured` |
| `Value`, invalid or empty text | `ReadOnly` | `invalid` |
| `WrongType` | `ReadOnly` | `invalid` |
| `Unreadable` | `ReadOnly` | `unreadable` |

`AccessModeCeiling.Check(requested, requestedSource, resolution)` returns `null` when allowed, or
an error message when the requested mode ranks above the ceiling. Ranking is explicit
(`read-only` < `read-write` < `full`), not the enum's numeric order.

Error message (one line, stderr, prefixed `Error: ` by the host like other startup errors):

```text
Requested access mode 'full' (option) exceeds the administrator ceiling 'read-write' set in HKLM\SOFTWARE\Policies\TiaMcpServer\MaxAccessMode. Request 'read-write' or lower, or ask an administrator to change the ceiling.
```

For `invalid`/`unreadable` the message names the reason instead of a configured value, e.g.
"exceeds the administrator ceiling 'read-only' (the configured value 'fulll' is not a valid access
mode)" or "(the ceiling could not be read: UnauthorizedAccessException)". Raw values are echoed
truncated to 64 characters.

The ceiling is read **once at process start** and never re-read, like the access mode itself.

### 4. Host enforcement

In `TiaMcpServer/Program.cs`, directly after `AccessModeParser.Parse` succeeds and before anything
else starts:

1. Read the ceiling through a new `IRegistryService` method that returns
   `AccessModeCeilingReading` (the existing `GetStringValue` cannot tell the four kinds apart).
2. `Resolve`, then `Check`. On failure write the error and `return 1`, the same exit code as other
   startup errors.
3. On success print one ceiling line after the existing access-mode line:

```text
TIA MCP access mode: READ-WRITE (option)
TIA MCP access-mode ceiling: none (not configured; see the hardening guide)
```

```text
TIA MCP access-mode ceiling: READ-WRITE (HKLM\SOFTWARE\Policies\TiaMcpServer)
```

```text
TIA MCP access-mode ceiling: READ-ONLY (configured value 'fulll' is invalid; treated as read-only)
```

The guide reference in the first form is plain text, not a URL.

### 5. Worker enforcement

In `TiaMcpServer.OpennessWorker/Program.cs`, at the start of `Main`, after `_accessMode` is parsed:

1. Read the ceiling with a worker-side reader (new file under `TiaMcpServer.OpennessWorker/Worker/`)
   using `Microsoft.Win32.RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)`.
2. `Resolve`, then `Check` (source reported as `option`; the worker only takes the mode from its
   arguments).
3. On failure write `TIA Openness worker: <message>` to stderr and exit with a non-zero code before
   reading stdin or touching TIA Portal. `Main` becomes `int Main()` or sets `Environment.ExitCode`.
4. On success add the ceiling to the existing `TIA Openness worker access mode:` stderr line.

The reader takes the registry access through a small delegate or interface so the decision path is
unit-testable in `TiaMcpServer.Tests` (the worker file is linked into the test project like
`WorkerOperationAuthorization`).

**Known effect:** if an administrator lowers the ceiling below a running server's mode, the running
host keeps its mode until restart, but a worker restart (after a crash or timeout) refuses to start.
The host then reports its existing worker-start failure. The guide states that ceiling changes take
effect at the next server start, and that a running server should be restarted after a change.

`TiaMcpServer.FakeWorker` does not model the ceiling; it is a test stand-in and never reads the
real registry.

### 6. `tia-mcp install`

Before writing any client configuration, `InstallCommand` reads and checks the ceiling against the
requested `--access-mode` (default `read-only`). Above the ceiling it writes nothing and fails with
the same message, through the installer's existing invalid-option path (text and `--json` output,
existing invalid-option exit code). It never touches the ceiling.

### 7. `doctor`

- **Header:** after `Access mode: …` print `Access-mode ceiling: …` with the same wording as the
  startup line.
- **JSON:** add `accessModeCeiling` (`"read-only" | "read-write" | "full" | null`) and
  `accessModeCeilingStatus` (`"configured" | "notConfigured" | "invalid" | "unreadable"`), written
  whenever `accessMode` is written.
- **New check `AccessModeCeilingCheck`** in the existing check list (`DoctorCommand`):

| Situation | Status | Message (gist) |
| --- | --- | --- |
| No ceiling | Passed | "No access-mode ceiling configured. See the hardening guide to set one." |
| Ceiling configured, requested mode within it, key ACL safe | Passed | "Ceiling READ-WRITE; requested mode allowed." |
| Requested mode above the ceiling | Failed | Same text as the startup error |
| Invalid or unreadable | Warning, or Failed when the requested mode is above `read-only` | Names the reason |
| Key writable by non-administrators | Warning | "The ceiling key can be changed by non-administrators; the ceiling does not protect against them." |

`doctor` keeps running every check when this one fails; its exit code follows the existing rule
(1 when any check failed).

**ACL rule:** read the key's security descriptor (`RegistryKey.GetAccessControl`, requesting only
`ReadPermissions`). Warn when any **Allow** rule grants a write-type right (`SetValue`,
`CreateSubKey`, `Delete`, `WriteKey`, `ChangePermissions`, `TakeOwnership`, `FullControl`) to a SID
other than Administrators (`S-1-5-32-544`), `SYSTEM` (`S-1-5-18`), `TrustedInstaller`
(`S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464`) or `CREATOR OWNER` (`S-1-3-0`).
If the descriptor cannot be read, the check reports a Warning saying the ACL could not be verified.
The ACL is checked only when the key exists. Put the rule itself (rules in, verdict out) in a pure
function so it is testable without a real key.

**Builder to verify:** on `net10.0`, reading a registry key's ACL may need the
`Microsoft.Win32.Registry.AccessControl` package (`RegistryAclExtensions.GetAccessControl`). If a
package is needed, that touches a product `.csproj`, so also run the packaging tests
(`dotnet test TiaMcpServer.Tests --filter "Category=Packaging"`).

### 8. Documentation

**New: `docs/guides/hardening.md`**, linked from `docs/README.md` ("Using the server"), the
installation guide, the README's access-mode section and `SECURITY.md`. Contents:

1. **What access modes protect, and what they do not.** The mode is chosen by whoever starts
   tia-mcp; any process running as the same user can choose a higher one. Agent and client
   configuration is the user's responsibility.
2. **TIA Portal Openness firewall.** It identifies the binary (path, date, hash), not the caller.
   Never answer "Yes to all" for general-purpose programs (`python.exe`, `dotnet.exe`,
   `powershell.exe`, `pwsh.exe`, `cmd.exe`, `node.exe`). Review
   `HKLM\SOFTWARE\Siemens\Automation\Openness\AllowList`. A modified or rebuilt binary prompts again.
3. **Installation location.** The default global tool lives in the user-writable
   `%USERPROFILE%\.dotnet\tools`. For a hardened machine, install to an administrator-only location
   (`dotnet tool install TiaMcpServer --tool-path "C:\Program Files\TiaMcpServer"` from an elevated
   prompt, or the self-contained `win-x64` archive extracted there) and allow-list that worker.
4. **Setting the ceiling.** Elevated commands, for example:

   ```powershell
   reg add "HKLM\SOFTWARE\Policies\TiaMcpServer" /v MaxAccessMode /t REG_SZ /d read-write /f /reg:64
   ```

   ```powershell
   reg delete "HKLM\SOFTWARE\Policies\TiaMcpServer" /v MaxAccessMode /f /reg:64
   ```

   Group Policy and Intune can deploy the same value. Changes take effect at the next server start;
   restart running servers.
5. **Verifying.** `tia-mcp doctor` output with and without a ceiling, and the ACL warning.
6. **Limits.** Not a hard security boundary: a process running as the same user can still debug or
   modify a running tia-mcp process; an administrator can change anything; a person can approve a
   firewall prompt without reading it. The only hard boundary is a separate Windows account, which
   tia-mcp does not support today.

**`README.md` trim** to roughly 100-120 lines:

```text
# TIA Portal MCP server            badges + 2-line pitch
Contents                           table of contents (absolute GitHub URLs with anchors)
## Tools                           one table: tool | what it does | modes (21 rows, one line each,
                                   linking to the operation reference)
## Access modes                    3-row table, read-only default, ceiling in one sentence,
                                   link to the hardening guide
## Write safety                    about 6 bullets: dryRun preview, one guarded call, stop at first
                                   failure with no rollback, lifecycle prompt in read-write, audit,
                                   client consent
## Architecture                    3 lines + link
## Quick start                     install / doctor / install client + prerequisites table
## Documentation · Contributing · Security · Other tools   (as today)
```

Rules for the trim:

- Before deleting a README fact, confirm it exists in `docs/SupportedOperations/`,
  `docs/ARCHITECTURE.md` or `docs/guides/`; move it there if it does not. List any moved facts in
  the PR Summary.
- Drop the outdated "staged for the redesign's final major release" paragraph and the
  custom .NET SDK / `browse_project_tree` v3 migration notes from the README (their reference pages
  keep them).
- Table-of-contents entries use absolute `https://github.com/Czarnak/tia-portal-mcp/blob/main/README.md#<anchor>`
  URLs so they work on GitHub and on nuget.org.

**Other updates:**

- `SECURITY.md`: replace "Access control is inherited from the TIA Portal installation's OS-level
  permissions rather than implemented separately in this tool" with the access-mode model, the
  ceiling and a link to the hardening guide. Refresh the stale "Write safety" paragraph there
  (it names only `plc_write` and `network_write`).
- `docs/guides/installation.md`: the ceiling in the access-mode section and in configuration
  precedence; `install` refusing above the ceiling.
- `docs/ARCHITECTURE.md` §2 (host startup and access modes) and §8 (write safety): the ceiling,
  host and worker enforcement, the read-only stance.
- `AGENTS.md`: one bullet in "Write safety model" on the ceiling and the no-write rule.
- `docs/adr/`: ADR-0018, "An administrator-owned access-mode ceiling, read but never written by
  tia-mcp", recording the rejected alternatives above. (PR 1 adds ADR-0017.)

### 9. Tests

All in `TiaMcpServer.Tests`, using hand-written fakes (no real registry):

- **Contracts:** `Resolve` for every row of the table in §3; `Check` for every requested/ceiling
  pair (3 x 3) plus invalid and unreadable; message content including the source label and the
  64-character truncation.
- **Host startup:** above the ceiling exits 1 with the message; within it prints the ceiling line;
  each of the three ceiling-line forms.
- **Worker:** the decision path through an injected reader: refuses above the ceiling, allows
  within it, treats unreadable as `read-only`.
- **Installer:** refuses above the ceiling and writes no configuration; text and `--json` forms.
- **Doctor:** every row of the check table, the ACL rule (allowed and disallowed SIDs, unreadable
  descriptor), the header line and the two JSON members.
- **Readers:** both readers keep their native calls minimal and pass what they observed (key
  present, value present, `RegistryValueKind`, value, caught exception) to one pure mapping
  function in Contracts that produces `AccessModeCeilingReading`. Tests cover that function for a
  missing key, a missing value, `REG_SZ`, `REG_DWORD` and each caught exception type; the thin
  native wrappers are covered by the live check.
- **No write path:** a test that fails if any product source file calls a registry write API
  (`SetValue`, `CreateSubKey`, `DeleteValue`, `DeleteSubKey`, `DeleteSubKeyTree`, `SetAccessControl`)
  on `RegistryKey`. This pins anti-goal 1.

## Calls made by Claude (please check)

- The key is `HKLM\SOFTWARE\Policies\TiaMcpServer`, value `MaxAccessMode`, `REG_SZ` only.
- `WrongType` (e.g. `REG_DWORD`) counts as invalid, i.e. a `read-only` ceiling.
- A missing ceiling is a **Passed** `doctor` check with a clear message, not a Warning, so users who
  deliberately skip it are not nagged.
- An invalid or unreadable ceiling is a Warning in `doctor` unless the requested mode is above
  `read-only`, in which case it is Failed (startup would fail).
- The ceiling is read once per process; changes need a server restart.
- A "no registry writes" source test pins the read-only stance.
- `SECURITY.md` and `AGENTS.md` are updated in this PR.
- Table-of-contents links use absolute GitHub URLs because the README is also the NuGet readme.

## Left to the builder

- Names and file placement of the Contracts types and the worker reader.
- The exact non-zero worker exit code and the installer's existing invalid-option exit code.
- How the "no registry writes" test finds product sources (e.g. scan `TiaMcpServer/` and
  `TiaMcpServer.OpennessWorker/` `.cs` files, excluding tests).
- Exact README wording and which one-line description each tool gets.

## Implementation checklist

1. Rebase onto `main` after PR 1 merges.
2. Contracts: reading type, `Resolve`, `Check`, messages, with tests.
3. Host: `IRegistryService` reading method and reader tests; startup enforcement and the ceiling
   line, with tests.
4. Worker: reader, enforcement in `Main`, linked into tests, with tests.
5. Installer: ceiling check before writing configuration, with tests.
6. Doctor: header, JSON members, `AccessModeCeilingCheck` with the ACL rule, with tests.
7. "No registry writes" source test.
8. Docs: hardening guide; README trim with table of contents; `SECURITY.md`, installation guide,
   `ARCHITECTURE.md`, `AGENTS.md`; ADR-0018 and the ADR index; `docs/README.md` link.
9. Run `verify-docs.py`, the stub build and the tests.
10. Live check on a real Windows machine with TIA Portal V21: no ceiling; `read-write` ceiling with
    `--access-mode full` (host fails, direct worker launch fails, `install` refuses); within the
    ceiling the server starts and attaches; ACL warning after granting Users write access to the
    key; `doctor` output for each.
11. Prune before merge: delete this spec in a final `docs: prune working files` commit.

## Verification

- `dotnet build TiaMcpServer.slnx -m:1 /p:UseTiaPortalReferenceStubs=true`
- `dotnet test TiaMcpServer.Tests --filter "Category!=Packaging"`
- `python3 -I scripts/verify-docs.py --archive-tag docs-archive-2026-10-30`
- The worker changes, so the PR template's live TIA Portal check applies (checklist step 10), or
  the maintainer verifies before merge.
