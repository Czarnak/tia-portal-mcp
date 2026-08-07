# Contributing

Contributions are welcome. For background, see [AGENTS.md](AGENTS.md) for build and convention
reference, [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design, and the
[documentation index](docs/README.md) for everything else.

## Before you start

This project is Windows-only and requires .NET SDK 8.0.4xx or newer.

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

Format: `<type>: <description>`

Example: `feat: add support for user-defined data types in block imports`

## Testing

Before opening a pull request, run tests locally:

```powershell
dotnet test TiaMcpServer.sln
```

**Note:** This project does not currently run automated tests on pull requests in CI. The test workflow (`.github/workflows/publish.yml`) only triggers on release tags or manual dispatch. Running tests locally is the contributor's responsibility.

For changes to `TiaMcpServer.OpennessWorker`, ideally test against a real TIA Portal V21 installation. Document in your PR description whether you tested against a real installation or stub-only.

## Opening a pull request

1. Fork the repository and push your branch.
2. Open a pull request against `main`.
3. Describe what changed and why.
4. Note whether your changes were tested against a real TIA Portal V21 installation or the stub build only (especially important for OpennessWorker changes).

## Reporting bugs or requesting features

Use [GitHub Issues](https://github.com/Czarnak/tia-portal-mcp/issues).

## Reporting security vulnerabilities

Do not file security issues as public GitHub Issues. See [SECURITY.md](SECURITY.md) for the private vulnerability reporting process.
