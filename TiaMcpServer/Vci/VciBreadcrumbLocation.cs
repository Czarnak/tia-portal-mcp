using TiaMcpServer.Contracts.Vci;

namespace TiaMcpServer.Vci;

public sealed record VciBreadcrumbLocation(string Path)
{
    public VciBreadcrumbLocation() : this(VciBreadcrumbFile.DefaultPath) { }
}
