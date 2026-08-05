using Siemens.Engineering;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Reads only dynamic attribute metadata and values; it never invokes a write API.</summary>
public static class EngineeringAttributeInspector
{
    public static IReadOnlyList<NetworkAttributeObservation> Inspect(
        IEngineeringObject engineeringObject,
        IReadOnlyList<string>? attributeNames)
    {
        var selectedNames = attributeNames is null
            ? null
            : new HashSet<string>(attributeNames, StringComparer.Ordinal);
        var metadata = engineeringObject.GetAttributeInfos()
            .Where(info => !string.IsNullOrEmpty(info.Name))
            .Where(info => selectedNames is null || selectedNames.Contains(info.Name))
            .OrderBy(info => info.Name, StringComparer.Ordinal)
            .ToArray();
        var observations = new List<NetworkAttributeObservation>(metadata.Length);

        foreach (var info in metadata)
        {
            var (canRead, canWrite) = Access(info.AccessMode);
            Func<object?>? readValue = null;
            if (canRead == true)
            {
                try
                {
                    var value = engineeringObject.GetAttribute(info.Name);
                    readValue = () => value;
                }
                catch (Exception exception)
                {
                    var failure = exception;
                    readValue = () => throw failure;
                }
            }

            observations.Add(new NetworkAttributeObservation
            {
                Name = info.Name,
                ReadValue = readValue,
                CanRead = canRead,
                CanWrite = canWrite,
                SupportedTypes = info.SupportedTypes
                    .Select(type => type.FullName ?? type.Name)
                    .OrderBy(typeName => typeName, StringComparer.Ordinal)
                    .ToArray(),
                Availability = canRead == true ? "available" : "unreadable",
            });
        }

        return observations;
    }

    private static (bool? CanRead, bool? CanWrite) Access(EngineeringAttributeAccessMode accessMode)
        => accessMode switch
        {
            EngineeringAttributeAccessMode.None => (false, false),
            EngineeringAttributeAccessMode.Read => (true, false),
            EngineeringAttributeAccessMode.Write => (false, true),
            EngineeringAttributeAccessMode.ReadWrite => (true, true),
            _ => (null, null),
        };
}
