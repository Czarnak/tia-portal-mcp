# Packaging and local tool install

How to build the NuGet package from a source checkout and install that local build as the
`tia-mcp` global tool, plus how to revert to the published version.

## Local Package Build

The package is already published on NuGet. Use this section only when testing package changes locally before publishing a new version.

Create a local tool package:

```powershell
dotnet pack TiaMcpServer\TiaMcpServer.csproj -c Release
```

Install from the generated package source:

```powershell
dotnet tool install -g TiaMcpServer --add-source .\TiaMcpServer\bin\Release
```

Run the installed server:

```powershell
tia-mcp
```

To bind an MCP server process to a specific project, pass `--project` or set `TIA_MCP_PROJECT_PATH`:

```powershell
tia-mcp --project C:\Projects\Line.ap21
$env:TIA_MCP_PROJECT_PATH = 'C:\Projects\Line.ap21'
tia-mcp
```

`--project` creates a configured-unverified assertion. Ordinary reads never bind, switch, or open
projects. Use `bind_project` to adopt an already-open project, and `forceRebind:true` to select a
different configured or previously bound path. Use `open_project` only when opening is intended.
The current writable-mode mismatch refusal is: `TIA Portal currently has project 'A' open, but
this request targets 'B'. This operation does not switch projects implicitly. Call bind_project with
the intended projectPath and forceRebind=true to select an already-open project. You can call
open_project to switch.` Read-only instead ends with `Open the intended project in TIA Portal
before retrying.`
`get_project_status(projectPath)` is read-only and non-binding: do not use it to switch projects.
Reattachment may show TIA's human-answerable Openness access dialog; worker ownership does not
survive detach. Project-tree cursors reject any binding change, including switching away and back.


## Installing a local branch build as the `tia-mcp` global tool

### Prerequisites

- TIA Portal V21 installed with Openness enabled, so real `Siemens.Engineering*.dll`
  compile references exist at `TiaPortalV21Dir` (defaults to
  `C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48`,
  see `Directory.Build.props`).
- Run all `dotnet` commands in **PowerShell, not Git Bash**. MSYS/Git Bash rewrites
  leading `/p:...` MSBuild switches as path-like tokens and drops the `/`, which
  breaks `dotnet pack`'s version overrides silently (MSB1008 or wrong version).

### Scripted install

`scripts/install-local-tool.ps1` runs steps 2-7 below (version, restore, pack, package
verification, tool swap, version check). It refuses to run on a dirty tree or while `tia-mcp`
is running unless you pass `-AllowDirty` or `-StopRunningServer`:

```powershell
./scripts/install-local-tool.ps1 -StopRunningServer
```

### Steps

1. **Check out the branch/commit you want to test** and make sure the working tree
   is clean (`git status`).

2. **Compute a local package version.** Reuse the repo's existing convention —
   `<latest tag>-local.<commits ahead>.g<short sha>` — so the version is traceable
   and never collides with a real release:

   ```powershell
   git describe --tags --long
   # v2.3.2-39-ge65dc64  ->  version = 2.3.2-local.39.ge65dc64
   ```

3. **Restore and pack** just the host project (this also builds and embeds the
   net48 Openness worker via the `BuildOpennessWorker`/`CopyOpennessWorker` MSBuild
   targets in `TiaMcpServer.csproj`):

   ```powershell
   cd C:\Users\LCZ\Desktop\RnD\TIA-Portal\tia-portal-mcp
   dotnet restore TiaMcpServer.slnx

   $version = "2.3.2-local.39.ge65dc64"   # from step 2
   dotnet pack TiaMcpServer/TiaMcpServer.csproj -c Release --no-restore `
     -o ./artifacts/phase5-install `
     /p:Version=$version /p:PackageVersion=$version /p:InformationalVersion=$version `
     /p:IncludeSourceRevisionInInformationalVersion=false
   ```

   Confirm the build log shows `UseTiaPortalReferenceStubs=false` — that means it
   linked against the real local TIA Portal assemblies, not the CI-only stubs in `ref/`.

4. **Verify the package layout** (same check CI runs before publishing):

   ```powershell
   ./scripts/verify-doctor-package.ps1 -PackagePath ./artifacts/phase5-install/TiaMcpServer.$version.nupkg
   ```

5. **Stop the running `tia-mcp.exe`.** If an MCP client (Claude Code, etc.) currently
   has the `tia-portal` server open, its worker process holds a file lock on the
   installed exe and blocks uninstall/reinstall. This also drops any active TIA
   Portal project binding for that session.

   ```powershell
   Get-Process -Name tia-mcp -ErrorAction SilentlyContinue | Format-Table Id,Path
   Stop-Process -Name tia-mcp -Force
   ```

6. **Swap the global tool.** `dotnet tool install` ignores prerelease versions
   unless you pin one explicitly — omitting `--version` here will silently fetch
   the latest *stable* release from nuget.org instead of your local build:

   ```powershell
   dotnet tool uninstall -g TiaMcpServer
   dotnet tool install -g --add-source ./artifacts/phase5-install TiaMcpServer --version $version
   ```

7. **Verify:**

   ```powershell
   dotnet tool list -g
   tia-mcp --version
   ```

   Both should show your local version string, not a published release.

8. **Reconnect the MCP client.** Killing the old process in step 5 disconnects any
   live MCP session using it. Reconnect/restart the `tia-portal` server in your
   client (e.g. Claude Code's `/mcp` reconnect, or restart the client) to pick up
   the new binary, then use `bind_project` for an already-open project — the previous binding is gone.

### Reverting to the published version

```powershell
dotnet tool uninstall -g TiaMcpServer
dotnet tool install -g TiaMcpServer
```

This reinstalls the latest stable release from nuget.org.
