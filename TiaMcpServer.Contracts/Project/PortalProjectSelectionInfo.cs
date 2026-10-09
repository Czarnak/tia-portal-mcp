namespace TiaMcpServer.Contracts.Project;

public sealed class PortalProjectSelectionInfo
{
    public int? PreviousProcessId { get; set; }

    public string? PreviousProjectPath { get; set; }

    public bool PreviousProjectWasWorkerOpened { get; set; }

    public bool? PreviousProjectIsModified { get; set; }

    public bool Reattached { get; set; }
}
