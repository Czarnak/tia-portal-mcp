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
        foreach (var descriptor in NetworkModeledAttributeCatalog.ForKind(resolved.Kind))
        {
            if (selectedNames is not null && !selectedNames.Contains(descriptor.Name))
            {
                continue;
            }

            if (NetworkModeledAttributeAdapters.TryCreateReader(resolved, descriptor.AdapterKey, out var reader))
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
        foreach (var attribute in attributes.Where(attribute =>
            string.Equals(attribute.Availability, "unknownAttribute", StringComparison.Ordinal)
            && attribute.Diagnostic is null))
        {
            attribute.Diagnostic = new NetworkAttributeDiagnosticInfo
            {
                Category = "unknown_attribute",
                Message = $"Attribute '{attribute.Name}' was not recognized by the modeled or dynamic metadata surface.",
            };
        }

        return new NetworkObjectInspectionInfo
        {
            Target = resolved.Target,
            Evidence = resolved.Evidence,
            Attributes = attributes,
            Messages = resolved.Messages.ToList(),
        };
    }
}
