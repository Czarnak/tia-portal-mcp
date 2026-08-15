using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Deterministically selected PLC tag index, shared by every channel of the read.
/// </summary>
public sealed class IoTagIndex
{
    private static readonly IReadOnlyList<IoTagCandidate> EmptyMatches = Array.Empty<IoTagCandidate>();

    private readonly Dictionary<IoAbsoluteIoAddress, IReadOnlyList<IoTagCandidate>> _candidatesByAddress;

    public IoTagIndex(string plcDeviceName, IReadOnlyList<IoTagCandidate> candidates)
    {
        PlcDeviceName = plcDeviceName;

        var frozenCandidates = candidates.ToArray();
        Candidates = Array.AsReadOnly(frozenCandidates);

        var groupedCandidates = new Dictionary<IoAbsoluteIoAddress, List<IoTagCandidate>>();
        foreach (var candidate in frozenCandidates)
        {
            if (!IoLogicalAddressFormatter.TryParse(candidate.LogicalAddress, out var address)
                || address is null)
            {
                continue;
            }

            if (!groupedCandidates.TryGetValue(address.Value, out var matches))
            {
                matches = new List<IoTagCandidate>();
                groupedCandidates.Add(address.Value, matches);
            }

            matches.Add(candidate);
        }

        _candidatesByAddress = groupedCandidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<IoTagCandidate>)Array.AsReadOnly(
                pair.Value
                    .OrderBy(candidate => candidate.TableName, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.FolderPath, StringComparer.Ordinal)
                    .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                    .ToArray()));
    }

    /// <summary>
    /// Owning device name of the selected PLC, compared against controller association evidence.
    /// </summary>
    public string PlcDeviceName { get; }

    public IReadOnlyList<IoTagCandidate> Candidates { get; }

    /// <summary>
    /// Returns every tag whose normalized area, start bit, and width exactly equal
    /// <paramref name="address"/>. The result is immutable and deterministically ordered.
    /// </summary>
    public IReadOnlyList<IoTagCandidate> FindMatches(IoAbsoluteIoAddress address)
        => _candidatesByAddress.TryGetValue(address, out var matches) ? matches : EmptyMatches;
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
