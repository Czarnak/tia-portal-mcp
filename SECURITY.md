# Security Policy

MCP server for Siemens TIA Portal V21, exposing Openness API operations through an MCP interface.

## Supported Versions

This project is maintained as a single rolling-release NuGet global tool. Only the latest published version receives security updates. No version support matrix is maintained; users should keep the tool updated via `dotnet tool update -g TiaMcpServer`.

## Reporting a Vulnerability

Do not file security vulnerabilities as public GitHub Issues. Instead, use GitHub's private vulnerability reporting:

1. Navigate to the [Security Advisories](https://github.com/Czarnak/tia-portal-mcp/security/advisories) tab on the repository.
2. Click "Report a vulnerability".
3. Provide:
   - Description of the vulnerability
   - Steps to reproduce (if applicable)
   - Potential impact
   - Affected version

Reports will be acknowledged and triaged as soon as possible. This project does not currently maintain a fixed SLA; response times depend on severity and availability.

## Security Considerations

### Local stdio transport

The MCP server communicates over local stdio only. It does not listen on a network port and cannot be accessed remotely. Access is controlled by the invoking process and the Windows user running the tool.

### Operating system integration

The tool requires:
- The invoking user to be a member of the Windows `Siemens TIA Openness` user group.
- TIA Portal V21 to be running with a project open.

Access control is inherited from the TIA Portal installation's OS-level permissions rather than implemented separately in this tool. Authorization failures at the OS level will block the tool.

### Write safety

All write operations use a preview-then-apply workflow with single-use safety tokens bound to the exact tool name, normalized project path, requested input, and current project state. Tokens expire after 10 minutes and are rejected if reused, expired, mismatched, or if project state changes. See the README's "Write safety" section for full details.

Successful write attempts append audit JSONL records under `%LOCALAPPDATA%\TiaMcpServer\audit` for forensic review.

### Supply chain

Siemens Openness DLLs (`Siemens.Engineering*.dll`) are never committed to this repository or included in the NuGet package. They are loaded from the local TIA Portal V21 installation at runtime. The NuGet package contains only the MCP host, the net48 worker executable, and open-source dependencies.

### Trust boundary

The MCP server exposes read and write operations on live TIA Portal projects. Because the tool can inspect and modify automation code and hardware configuration:

- **Do not point MCP clients or AI agents at production projects** without explicit review. Use disposable or backed-up copies of `.ap21` projects for development and testing.
- Treat MCP client configuration and any AI agent driving the client as part of your security trust boundary.
- Review project-writing AI prompts before execution against shared or critical projects.
