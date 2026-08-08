namespace TiaMcpServer.Contracts;

public class ProjectStatusInfo
{
    public bool IsOpen { get; set; }

    public string? Name { get; set; }

    public string? Path { get; set; }

    public string? Version { get; set; }

    public string? Author { get; set; }

    public bool? IsModified { get; set; }

    public DateTime? CreationTime { get; set; }

    public DateTime? LastModified { get; set; }

    public string? LastModifiedBy { get; set; }

    public long? Size { get; set; }

    /// <summary>
    /// Extended read-only project metadata (copyright, family, multilingual comment, language
    /// settings, history, used products, block-compilation settings), populated only by the
    /// direct <c>get_project_status</c> read. Null when the project is not open so the JSON
    /// contract remains additive and backward compatible.
    /// </summary>
    public ProjectMetadataInfo? Metadata { get; set; }
}
