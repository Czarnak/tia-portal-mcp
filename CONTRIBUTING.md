# Contributing

Contributions are welcome. For background, see [AGENTS.md](AGENTS.md) for build and convention
reference, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design, and the
[documentation index](docs/README.md) for everything else.

## Before you start

This project is Windows-only and requires a stable .NET 10 SDK, `10.0.400` or newer within the
.NET 10 feature bands. `global.json` pins that version and excludes prerelease SDKs; see
[building from source](docs/development/building.md) for details.

### Stub vs. real TIA Portal

Most code in this project (the MCP host, contracts, tests, docs) can be developed and tested with compile-time reference stubs, without TIA Portal installed. The build system falls back to `ref/` stubs automatically when a local TIA Portal V21 installation is not found.

**If you are modifying or testing:**
- Host code (`TiaMcpServer/`), contracts (`TiaMcpServer.Contracts/`), tests (`TiaMcpServer.Tests/`), or docs → you can use the stub build; no TIA Portal installation needed.
- Openness worker code (`TiaMcpServer.OpennessWorker/`) → you need a real TIA Portal V21 installation with Openness enabled to verify end-to-end behavior.

External contributors without a TIA Portal license can develop and test host/contract/test changes using the stub build. The maintainer will verify any OpennessWorker changes with a real installation before merging.

## Development setup

### 1. Restore and build

```powershell
dotnet restore TiaMcpServer.sln
dotnet build TiaMcpServer.sln -m:1
```

**Important:** The `-m:1` flag serializes solution builds. The host project builds and copies the net48 Openness worker via MSBuild targets; parallel builds cause duplicate copy conflicts. Always use `-m:1`.

### 2. Run tests

```powershell
dotnet test TiaMcpServer.sln
```

Tests use xUnit and do not require TIA Portal installed (they run against the stub build). No mocking libraries are used; fakes are hand-written implementations of service interfaces.

For coverage reporting, stub vs. real TIA reference selection, and running the server locally from
a source checkout, see [docs/development/building.md](docs/development/building.md). For the MCP
Inspector test loop, see [docs/development/local-mcp-testing.md](docs/development/local-mcp-testing.md).

## Making changes

### Branch and focus

- Branch from `main`.
- Keep changes focused on a single feature or bug fix.
- Follow existing conventions in the codebase:
  - Organize code by feature/domain; many small files over a few large files.
  - Keep functions small (<50 lines) and files focused (<800 lines).
  - Test files follow the naming pattern `{ClassUnderTest}Tests.cs` in namespace `TiaMcpServer.Tests`.
  - No mocking libraries; write fakes by hand.

### Documentation

Documents under `docs/` are grouped by audience: `guides/` for people using the server,
`development/` for people building it, `roadmap/` for direction. `docs/superpowers/` is historical
process material and is not current documentation.

**A new document under `docs/` is not complete until it is listed in
[docs/README.md](docs/README.md).** That index is the only thing keeping the tree navigable — an
unlisted document is one nobody will find.

`README.md` is also the NuGet package readme, where relative links do not resolve. Every
cross-document link in `README.md` must be an absolute
`https://github.com/Czarnak/tia-portal-mcp/blob/main/...` URL. All other documents use relative links.

## Commit messages

Use conventional commit style with the following types:

- `feat:` – new feature
- `fix:` – bug fix
- `refactor:` – code restructuring without behavior change
- `test:` – test-only changes
- `docs:` – documentation changes
- `chore:` – maintenance tasks (dependency updates, CI config, etc.)
- `perf:` – performance improvements
- `ci:` – CI/CD workflow changes
- `build:` – .NET SDK, project file, or packaging changes

Format: `<type>(<scope>): <description>`. The scope is optional and names the part of the
codebase the change is about. Prefer the area names used for issue and PR labels:

| Scope | Covers |
| --- | --- |
| `plc-blocks` | PLC blocks and types, source/XML formats, cross-references, `compile_check`, PLC start/stop |
| `plc-tags` | Tag tables, tags, user constants |
| `network` | `network_read`/`network_write`, hardware configuration, subnets, I/O maps |
| `project` | Project tree, project status, lifecycle, binding |
| `write-safety` | Preview-then-apply, safety tokens, audit, read-only mode |
| `mcp-protocol` | Tool schemas, canonical JSON, batch engine, cursors |
| `worker` | Openness worker process, host-worker IPC, contracts |
| `cli` | `doctor`, `install`, `--version` |

A narrower scope is fine when it is clearer, for example `install` or `cursors`.

Examples:

- `feat(network): add subnet rename`
- `fix(plc-blocks): keep S7-300/400 block reads working when document export is rejected`
- `build: migrate host tooling to .NET 10`

## Testing

Before opening a pull request, run tests locally:

```powershell
dotnet test TiaMcpServer.sln
```

CI (`.github/workflows/ci.yml`) runs on every push to `main` and every pull request against it. It
builds the solution against the reference stubs, runs the test suite with scoped coverage, and fails
when line coverage drops below 80%. CI has no TIA Portal installation, so it never exercises the
Openness worker against the real API.

For changes to `TiaMcpServer.OpennessWorker`, ideally test against a real TIA Portal V21
installation. The pull request template asks whether you did, or whether the change was tested on
the stub build only.

## Opening a pull request

1. Fork the repository and push your branch.
2. Open a pull request against `main`, with a title in the commit message format above.
3. Fill in the pull request template: what changed and why, how it was verified (including
   whether it was tested against a real TIA Portal V21 installation or the stub build only), and
   its impact on tool contracts, write safety, and documentation.

Area labels are added automatically from the paths the pull request changes. A pull request
labeled `area: worker` touches code that runs in the Openness worker and is verified on a real
TIA Portal V21 installation before merge.

## Reporting bugs or requesting features

Open an issue with one of the [issue forms](https://github.com/Czarnak/tia-portal-mcp/issues/new/choose):
bug report, feature request, or documentation issue. Questions and ideas that are not yet a
concrete proposal belong in [Discussions](https://github.com/Czarnak/tia-portal-mcp/discussions).

## Reporting security vulnerabilities

Do not file security issues as public GitHub Issues. See [SECURITY.md](SECURITY.md) for the private vulnerability reporting process.
