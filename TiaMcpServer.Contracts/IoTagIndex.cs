using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Deterministically selected PLC tag index, shared by every channel of the read.
/// </summary>
public sealed class IoTagIndex
{
    public IoTagIndex(string plcDeviceName, IReadOnlyList<IoTagCandidate> candidates)
    {
        PlcDeviceName = plcDeviceName;
        Candidates = candidates;
    }

    /// <summary>
    /// Owning device name of the selected PLC, compared against controller association evidence.
    /// </summary>
    public string PlcDeviceName { get; }

    public IReadOnlyList<IoTagCandidate> Candidates { get; }
}

/// <summary>
/// One flattened PLC tag used for channel matching.
/// </summary>
public sealed class IoTagCandidate
{
    public IoTagCandidate(string name, string dataType, string logicalAddress, string tableName, string folderPath)
    {
        Name = name;
        DataType = dataType;
        LogicalAddress = logicalAddress;
        TableName = tableName;
        FolderPath = folderPath;
    }

    public string Name { get; }

    public string DataType { get; }

    public string LogicalAddress { get; }

    public string TableName { get; }

    public string FolderPath { get; }
}
