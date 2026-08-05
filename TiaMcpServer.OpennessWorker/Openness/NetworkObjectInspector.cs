using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class NetworkObjectInspector
{
    public static NetworkObjectInspectionInfo Inspect(
        ResolvedNetworkObject resolved,
        IReadOnlyList<string>? attributeNames)
    {
        var selectedNames = attributeNames is null
            ? null
            : new HashSet<string>(attributeNames, StringComparer.Ordinal);
        var modeled = new List<NetworkAttributeObservation>();
        var descriptors = string.Equals(
            resolved.Kind,
            NetworkObjectKinds.CommunicationConnection,
            StringComparison.Ordinal)
                ? ConnectionModeledAttributeCatalog.ForConnectionType(
                    resolved.Target.ConnectionType ?? string.Empty)
                : NetworkModeledAttributeCatalog.ForKind(resolved.Kind);
        foreach (var descriptor in descriptors)
        {
            if (selectedNames is not null && !selectedNames.Contains(descriptor.Name))
            {
                continue;
            }

            Func<object?>? reader;
            var hasReader = string.Equals(
                resolved.Kind,
                NetworkObjectKinds.CommunicationConnection,
                StringComparison.Ordinal)
                    ? ConnectionModeledAttributeAdapters.TryCreateReader(
                        resolved,
                        descriptor.AdapterKey,
                        out reader)
                    : NetworkModeledAttributeAdapters.TryCreateReader(
                        resolved,
                        descriptor.AdapterKey,
                        out reader);
            if (hasReader)
            {
                modeled.Add(new NetworkAttributeObservation
                {
                    Name = descriptor.Name,
                    ReadValue = reader,
                    CanRead = true,
                    CanWrite = false,
                    SupportedTypes = new[] { descriptor.ExpectedClrTypeName },
                });
            }
            else
            {
                modeled.Add(new NetworkAttributeObservation
                {
                    Name = descriptor.Name,
                    CanRead = false,
                    CanWrite = false,
                    SupportedTypes = new[] { descriptor.ExpectedClrTypeName },
                    Availability = "unsupported",
                });
            }
        }

        var dynamic = EngineeringAttributeInspector.Inspect(resolved.EngineeringObject, attributeNames);
        var attributes = NetworkAttributeResultBuilder.Build(modeled, dynamic, attributeNames).ToList();

        return new NetworkObjectInspectionInfo
        {
            Target = resolved.Target,
            Evidence = resolved.Evidence,
            Attributes = attributes,
            Messages = resolved.Messages.ToList(),
        };
    }
}
