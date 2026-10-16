using TiaMcpServer.Contracts.Vci;

namespace TiaMcpServer.Vci;

public sealed record VciBreadcrumbLocation(string Path)
{
    public VciBreadcrumbLocation() : this(System.IO.Path.ChangeExtension(VciBreadcrumbFile.DefaultPath, $"{Guid.NewGuid():N}.json")) { }
}
