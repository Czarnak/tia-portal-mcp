using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Adapts the read-only Siemens metadata surface to the fault-isolated pure processor.</summary>
public static class EngineeringAttributeInspector
{
    public static NetworkAttributeMetadataProcessingResult Inspect(
        IEngineeringObject engineeringObject,
        IReadOnlyList<string>? attributeNames)
        => NetworkAttributeMetadataProcessor.Process(
            () => engineeringObject.GetAttributeInfos().Select(info =>
                new NetworkAttributeMetadataEntry
                {
                    ReadName = () => info.Name,
                    ReadAccess = () => Access(info.AccessMode),
                    ReadSupportedTypes = () => info.SupportedTypes.Select(type =>
                        new NetworkAttributeSupportedTypeMetadata
                        {
                            ReadName = () => type.FullName ?? type.Name,
                        }),
                    ReadValue = () => engineeringObject.GetAttribute(info.Name),
                }),
            attributeNames);

    private static NetworkAttributeAccessMetadata Access(
        EngineeringAttributeAccessMode accessMode)
    {
        var access = accessMode switch
        {
            EngineeringAttributeAccessMode.None => (CanRead: (bool?)false, CanWrite: (bool?)false),
            EngineeringAttributeAccessMode.Read => (CanRead: (bool?)true, CanWrite: (bool?)false),
            EngineeringAttributeAccessMode.Write => (CanRead: (bool?)false, CanWrite: (bool?)true),
            EngineeringAttributeAccessMode.ReadWrite => (CanRead: (bool?)true, CanWrite: (bool?)true),
            _ => (CanRead: (bool?)null, CanWrite: (bool?)null),
        };
        return new NetworkAttributeAccessMetadata
        {
            CanRead = access.CanRead,
            CanWrite = access.CanWrite,
        };
    }
}
