namespace TiaMcpServer.Contracts;

public class WorkerRequest
{
    public string Method { get; set; } = string.Empty;

    public string? ProjectPath { get; set; }

    public string? BlockPath { get; set; }

    public string? YamlContent { get; set; }

    public string? PlcName { get; set; }

    public string? CrossReferenceFilter { get; set; }

    public bool AllowTiaConfirmations { get; set; }

    public string? Query { get; set; }

    public int? Depth { get; set; }

    public string? StartPath { get; set; }

    public int? MaxResults { get; set; }

    public string? TypeIdentifier { get; set; }

    public string? DeviceName { get; set; }

    public string? DeviceItemName { get; set; }

    public string? IpAddress { get; set; }

    public string? SubnetMask { get; set; }

    public string? PnDeviceName { get; set; }

    public string? SubnetName { get; set; }

    public string? IoSystemName { get; set; }

    public bool Confirm { get; set; }

    public string? TableName { get; set; }

    public string? FolderPath { get; set; }

    public string? Name { get; set; }

    public string? NewName { get; set; }

    public string? DataType { get; set; }

    public string? LogicalAddress { get; set; }

    public bool? ExternalAccessible { get; set; }

    public bool? ExternalVisible { get; set; }

    public bool? ExternalWritable { get; set; }

    public bool? IsSafety { get; set; }

    public string? Value { get; set; }

    public string? ProjectDirectory { get; set; }

    public string? ProjectName { get; set; }

    public string? Author { get; set; }

    public string? Comment { get; set; }

    public string? TargetDirectory { get; set; }

    public string? TargetName { get; set; }

    public bool ForceRebind { get; set; }

    public bool Rebind { get; set; } = true;

    public string? ArchiveDirectory { get; set; }

    public string? ArchiveName { get; set; }

    public string? ArchiveMode { get; set; }

    public bool SaveBeforeArchive { get; set; } = true;

    public bool SaveBeforeClose { get; set; } = true;

    public string? BlockType { get; set; }

    public string? Language { get; set; }

    public string? OBEventClass { get; set; }
}
