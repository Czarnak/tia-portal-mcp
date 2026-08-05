using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Builds lightweight connection summaries and selectors from already-read typed evidence.
/// Missing required evidence keeps the connection visible but unselectable.
/// </summary>
public static class CommunicationConnectionSelectorFactory
{
    public static CommunicationConnectionInfo Create(
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo>? itemPath,
        int connectionIndex,
        string? connectionType,
        string? localConnectionName,
        string? localConnectionId,
        string? partnerName,
        bool isValid,
        IEnumerable<string>? identityDiagnostics = null)
    {
        var diagnostics = Validate(
            deviceName,
            itemPath,
            connectionIndex,
            connectionType,
            localConnectionName,
            identityDiagnostics);
        var effectiveLocalConnectionId = string.Equals(
            connectionType,
            "HmiConnection",
            StringComparison.Ordinal)
                ? null
                : localConnectionId;

        var summary = new CommunicationConnectionInfo
        {
            ConnectionType = connectionType ?? string.Empty,
            LocalConnectionName = localConnectionName ?? string.Empty,
            LocalConnectionId = effectiveLocalConnectionId,
            PartnerName = partnerName,
            IsValid = isValid,
            Selectable = diagnostics.Count == 0,
            SelectorDiagnostics = diagnostics,
        };

        if (summary.Selectable)
        {
            summary.Selector = new NetworkObjectSelectorInfo
            {
                Kind = NetworkObjectKinds.CommunicationConnection,
                DeviceName = deviceName,
                ItemPath = itemPath!.Select(Clone).ToList(),
                ConnectionIndex = connectionIndex,
                ConnectionType = connectionType,
                LocalConnectionName = localConnectionName,
                LocalConnectionId = effectiveLocalConnectionId,
            };
        }

        return summary;
    }

    private static List<string> Validate(
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo>? itemPath,
        int connectionIndex,
        string? connectionType,
        string? localConnectionName,
        IEnumerable<string>? identityDiagnostics)
    {
        var diagnostics = identityDiagnostics?
            .Where(diagnostic => !string.IsNullOrWhiteSpace(diagnostic))
            .Distinct(StringComparer.Ordinal)
            .ToList()
            ?? new List<string>();
        if (string.IsNullOrWhiteSpace(deviceName))
        {
            diagnostics.Add("Device name is missing; communicationConnection selector not available.");
        }

        if (itemPath is null || itemPath.Count == 0)
        {
            diagnostics.Add("Owner device item path is missing; communicationConnection selector not available.");
        }
        else
        {
            for (var index = 0; index < itemPath.Count; index++)
            {
                var segment = itemPath[index];
                if (segment.Index < 0
                    || string.IsNullOrWhiteSpace(segment.Name)
                    || segment.PositionNumber is null
                    || string.IsNullOrWhiteSpace(segment.TypeIdentifier))
                {
                    diagnostics.Add(
                        $"Owner device item path segment {index} is missing required evidence; "
                        + "communicationConnection selector not available.");
                }
            }
        }

        if (connectionIndex < 0)
        {
            diagnostics.Add("Connection composition index is negative; communicationConnection selector not available.");
        }

        if (string.IsNullOrWhiteSpace(connectionType))
        {
            diagnostics.Add("Connection type is missing; communicationConnection selector not available.");
        }
        else if (!ConnectionModeledAttributeCatalog.IsSupportedConnectionType(connectionType))
        {
            diagnostics.Add(
                $"Connection type '{connectionType}' is not an installed V21 connection type; "
                + "communicationConnection selector not available.");
        }

        if (string.IsNullOrWhiteSpace(localConnectionName))
        {
            diagnostics.Add("Local connection name is missing; communicationConnection selector not available.");
        }

        return diagnostics;
    }

    private static DeviceItemPathSegmentInfo Clone(DeviceItemPathSegmentInfo segment)
        => new DeviceItemPathSegmentInfo
        {
            Index = segment.Index,
            Name = segment.Name,
            PositionNumber = segment.PositionNumber,
            TypeIdentifier = segment.TypeIdentifier,
        };
}
