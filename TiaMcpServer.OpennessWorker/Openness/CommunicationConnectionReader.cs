using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.CommunicationConnections;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Enumerates one device item's communication-connection composition exactly once and captures
/// only the lightweight typed evidence needed for summaries and later exact resolution.
/// </summary>
public static class CommunicationConnectionReader
{
    public static IReadOnlyList<CommunicationConnectionReadResult> Read(
        DeviceItem owner,
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        List<string>? messages = null)
    {
        CommunicationManagement? communicationManagement;
        try
        {
            communicationManagement =
                ((IEngineeringServiceProvider)owner).GetService<CommunicationManagement>();
        }
        catch (EngineeringException exception)
        {
            messages?.Add($"Could not read communication connections: {exception.Message}");
            return Array.Empty<CommunicationConnectionReadResult>();
        }

        if (communicationManagement is null)
        {
            return Array.Empty<CommunicationConnectionReadResult>();
        }

        var results = new List<CommunicationConnectionReadResult>();
        var connectionIndex = 0;
        try
        {
            foreach (Connection connection in communicationManagement.Connections)
            {
                results.Add(ReadConnection(
                    connection,
                    deviceName,
                    itemPath,
                    connectionIndex,
                    messages));
                connectionIndex++;
            }
        }
        catch (EngineeringException exception)
        {
            messages?.Add($"Could not continue reading communication connections: {exception.Message}");
        }

        return results;
    }

    internal static bool TryReadConnectionType(
        Connection connection,
        out string? connectionType,
        out string? diagnostic)
    {
        try
        {
            var value = connection.ConnectionType;
            connectionType = Enum.Format(
                typeof(Siemens.Engineering.HW.CommunicationConnections.ConnectionType),
                value,
                "G");
            diagnostic = null;
            return true;
        }
        catch (Exception exception)
        {
            connectionType = null;
            diagnostic = $"Could not read connection type: {exception.Message}";
            return false;
        }
    }

    internal static bool TryReadIdentityString(
        Connection connection,
        string connectionType,
        string attributeName,
        out string? value,
        out string? diagnostic)
    {
        var descriptor = ConnectionModeledAttributeCatalog
            .ForConnectionType(connectionType)
            .FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                attributeName,
                StringComparison.Ordinal));
        if (descriptor is null
            || !ConnectionModeledAttributeAdapters.TryCreateReader(
                connection,
                descriptor.AdapterKey,
                out var reader))
        {
            value = null;
            diagnostic = $"No typed V21 reader is available for {connectionType}.{attributeName}.";
            return false;
        }

        try
        {
            var raw = reader!();
            if (raw is null || raw is string)
            {
                value = (string?)raw;
                diagnostic = null;
                return true;
            }

            value = null;
            diagnostic =
                $"Typed V21 reader for {connectionType}.{attributeName} returned "
                + "a non-string value.";
            return false;
        }
        catch (Exception exception)
        {
            value = null;
            diagnostic = $"Could not read {connectionType}.{attributeName}: {exception.Message}";
            return false;
        }
    }

    private static CommunicationConnectionReadResult ReadConnection(
        Connection connection,
        string? deviceName,
        IReadOnlyList<DeviceItemPathSegmentInfo> itemPath,
        int connectionIndex,
        List<string>? messages)
    {
        var identityDiagnostics = new List<string>();
        TryReadConnectionType(connection, out var connectionType, out var typeDiagnostic);
        AddDiagnostic(identityDiagnostics, typeDiagnostic);

        string? localConnectionName = null;
        string? localConnectionId = null;
        if (connectionType is not null)
        {
            TryReadIdentityString(
                connection,
                connectionType,
                "LocalConnectionName",
                out localConnectionName,
                out var nameDiagnostic);
            AddDiagnostic(identityDiagnostics, nameDiagnostic);

            if (ConnectionModeledAttributeCatalog
                .ForConnectionType(connectionType)
                .Any(descriptor => string.Equals(
                    descriptor.Name,
                    "LocalConnectionId",
                    StringComparison.Ordinal)))
            {
                TryReadIdentityString(
                    connection,
                    connectionType,
                    "LocalConnectionId",
                    out localConnectionId,
                    out var idDiagnostic);
                AddMessage(messages, idDiagnostic);
            }
        }

        var partnerName = ReadOptionalString(
            () => connection.PartnerTarget?.Name,
            "connection partner target name",
            messages);
        var isValid = ReadIsValid(connection, messages);
        var summary = CommunicationConnectionSelectorFactory.Create(
            deviceName,
            itemPath,
            connectionIndex,
            connectionType,
            localConnectionName,
            localConnectionId,
            partnerName,
            isValid,
            identityDiagnostics);

        return new CommunicationConnectionReadResult(connectionIndex, summary);
    }

    private static string? ReadOptionalString(
        Func<string?> reader,
        string description,
        List<string>? messages)
    {
        try
        {
            return reader();
        }
        catch (Exception exception)
        {
            messages?.Add($"Could not read {description}: {exception.Message}");
            return null;
        }
    }

    private static bool ReadIsValid(Connection connection, List<string>? messages)
    {
        try
        {
            return connection.IsValid;
        }
        catch (Exception exception)
        {
            messages?.Add($"Could not read connection validity: {exception.Message}");
            return false;
        }
    }

    private static void AddDiagnostic(List<string> diagnostics, string? diagnostic)
    {
        if (!string.IsNullOrWhiteSpace(diagnostic))
        {
            diagnostics.Add(diagnostic!);
        }
    }

    private static void AddMessage(List<string>? messages, string? message)
    {
        if (messages is not null && !string.IsNullOrWhiteSpace(message))
        {
            messages.Add(message!);
        }
    }
}

public sealed class CommunicationConnectionReadResult
{
    public CommunicationConnectionReadResult(int connectionIndex, CommunicationConnectionInfo summary)
    {
        ConnectionIndex = connectionIndex;
        Summary = summary;
    }

    public int ConnectionIndex { get; }

    public CommunicationConnectionInfo Summary { get; }
}
