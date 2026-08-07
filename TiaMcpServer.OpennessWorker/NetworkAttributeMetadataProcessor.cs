using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaMcpServer.OpennessWorker;

/// <summary>
/// Siemens-free, fault-injectable description of one dynamic engineering attribute.
/// Every metadata property remains behind a delegate so acquisition, enumeration, property access,
/// supported-type processing, and value reads can be isolated independently.
/// </summary>
public sealed class NetworkAttributeMetadataEntry
{
    public Func<string?>? ReadName { get; set; }
    public Func<NetworkAttributeAccessMetadata>? ReadAccess { get; set; }
    public Func<IEnumerable<NetworkAttributeSupportedTypeMetadata>?>? ReadSupportedTypes { get; set; }
    public Func<object?>? ReadValue { get; set; }
}

public sealed class NetworkAttributeAccessMetadata
{
    public bool? CanRead { get; set; }
    public bool? CanWrite { get; set; }
}

public sealed class NetworkAttributeSupportedTypeMetadata
{
    public Func<string?>? ReadName { get; set; }
}

public sealed class NetworkAttributeMetadataProcessingResult
{
    public List<NetworkAttributeObservation> Observations { get; set; } = new();
    public List<string> Diagnostics { get; set; } = new();
}

/// <summary>
/// Converts a dynamic metadata source into observations without allowing one failure to suppress
/// later attributes. Collection-level failure is represented for every explicitly requested name.
/// </summary>
public static class NetworkAttributeMetadataProcessor
{
    private const int MaxConsecutiveEnumerationFailures = 3;

    public static NetworkAttributeMetadataProcessingResult Process(
        Func<IEnumerable<NetworkAttributeMetadataEntry>?> acquireMetadata,
        IReadOnlyList<string>? attributeNames)
    {
        var result = new NetworkAttributeMetadataProcessingResult();
        var selectedNames = attributeNames is null
            ? null
            : new HashSet<string>(attributeNames, StringComparer.Ordinal);

        IEnumerable<NetworkAttributeMetadataEntry>? metadata;
        try
        {
            metadata = acquireMetadata();
        }
        catch (Exception)
        {
            AddDiagnostic(result.Diagnostics, "Dynamic attribute metadata collection could not be acquired.");
            AddRequestedFailures(result.Observations, attributeNames);
            return result;
        }

        if (metadata is null)
        {
            AddDiagnostic(result.Diagnostics, "Dynamic attribute metadata collection was null.");
            AddRequestedFailures(result.Observations, attributeNames);
            return result;
        }

        ForEachFaultIsolated(
            metadata,
            entry => ProcessEntry(entry, selectedNames, result),
            result.Diagnostics,
            "Dynamic attribute metadata");
        result.Observations = result.Observations
            .OrderBy(observation => observation.Name, StringComparer.Ordinal)
            .ToList();
        return result;
    }

    private static void ProcessEntry(
        NetworkAttributeMetadataEntry? entry,
        ISet<string>? selectedNames,
        NetworkAttributeMetadataProcessingResult result)
    {
        if (entry is null)
        {
            AddDiagnostic(result.Diagnostics, "Dynamic attribute metadata current entry was null.");
            return;
        }

        string? name;
        try
        {
            name = entry.ReadName is null ? null : entry.ReadName();
        }
        catch (Exception)
        {
            AddDiagnostic(result.Diagnostics, "Dynamic attribute metadata name could not be read.");
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            AddDiagnostic(result.Diagnostics, "Dynamic attribute metadata name was blank or unavailable.");
            return;
        }

        if (selectedNames is not null && !selectedNames.Contains(name!))
        {
            return;
        }

        NetworkAttributeAccessMetadata? access = null;
        try
        {
            access = entry.ReadAccess is null ? null : entry.ReadAccess();
        }
        catch (Exception)
        {
            AddDiagnostic(
                result.Diagnostics,
                $"Dynamic attribute '{name}' access metadata could not be read.");
        }

        if (access is null)
        {
            AddDiagnostic(
                result.Diagnostics,
                $"Dynamic attribute '{name}' access metadata was unavailable.");
        }

        var supportedTypes = ReadSupportedTypes(entry, name!, result.Diagnostics);
        Func<object?>? readValue = null;
        var availability = "unreadable";
        if (access?.CanRead == true)
        {
            if (entry.ReadValue is null)
            {
                AddDiagnostic(
                    result.Diagnostics,
                    $"Dynamic attribute '{name}' value reader was unavailable.");
            }
            else
            {
                availability = "available";
                try
                {
                    var value = entry.ReadValue();
                    readValue = () => value;
                }
                catch (Exception)
                {
                    AddDiagnostic(
                        result.Diagnostics,
                        $"Dynamic attribute '{name}' value could not be read.");
                    readValue = () => throw new InvalidOperationException(
                        $"Dynamic attribute '{name}' value could not be read.");
                }
            }
        }

        result.Observations.Add(new NetworkAttributeObservation
        {
            Name = name!,
            ReadValue = readValue,
            CanRead = access?.CanRead,
            CanWrite = access?.CanWrite,
            SupportedTypes = supportedTypes,
            Availability = availability,
        });
    }

    private static IReadOnlyList<string> ReadSupportedTypes(
        NetworkAttributeMetadataEntry entry,
        string attributeName,
        List<string> diagnostics)
    {
        IEnumerable<NetworkAttributeSupportedTypeMetadata>? types;
        try
        {
            types = entry.ReadSupportedTypes is null ? null : entry.ReadSupportedTypes();
        }
        catch (Exception)
        {
            AddDiagnostic(
                diagnostics,
                $"Dynamic attribute '{attributeName}' supported types could not be read.");
            return Array.Empty<string>();
        }

        if (types is null)
        {
            AddDiagnostic(
                diagnostics,
                $"Dynamic attribute '{attributeName}' supported types were unavailable.");
            return Array.Empty<string>();
        }

        var names = new List<string>();
        ForEachFaultIsolated(
            types,
            type =>
            {
                if (type is null)
                {
                    AddDiagnostic(
                        diagnostics,
                        $"Dynamic attribute '{attributeName}' supported type entry was null.");
                    return;
                }

                string? typeName;
                try
                {
                    typeName = type.ReadName is null ? null : type.ReadName();
                }
                catch (Exception)
                {
                    AddDiagnostic(
                        diagnostics,
                        $"Dynamic attribute '{attributeName}' supported type name could not be read.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(typeName))
                {
                    AddDiagnostic(
                        diagnostics,
                        $"Dynamic attribute '{attributeName}' supported type name was blank or unavailable.");
                    return;
                }

                names.Add(typeName!);
            },
            diagnostics,
            $"Dynamic attribute '{attributeName}' supported-type metadata");
        return names
            .Distinct(StringComparer.Ordinal)
            .OrderBy(typeName => typeName, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ForEachFaultIsolated<T>(
        IEnumerable<T> source,
        Action<T> process,
        List<string> diagnostics,
        string scope)
    {
        IEnumerator<T> enumerator;
        try
        {
            enumerator = source.GetEnumerator();
        }
        catch (Exception)
        {
            AddDiagnostic(diagnostics, $"{scope} enumeration could not be started.");
            return;
        }

        try
        {
            var consecutiveFailures = 0;
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = enumerator.MoveNext();
                    consecutiveFailures = 0;
                }
                catch (Exception)
                {
                    consecutiveFailures++;
                    AddDiagnostic(diagnostics, $"{scope} enumeration could not advance.");
                    if (consecutiveFailures >= MaxConsecutiveEnumerationFailures)
                    {
                        AddDiagnostic(
                            diagnostics,
                            $"{scope} enumeration stopped after repeated failures.");
                        break;
                    }

                    continue;
                }

                if (!hasNext)
                {
                    break;
                }

                T current;
                try
                {
                    current = enumerator.Current;
                }
                catch (Exception)
                {
                    AddDiagnostic(diagnostics, $"{scope} current entry could not be read.");
                    continue;
                }

                try
                {
                    process(current);
                }
                catch (Exception)
                {
                    AddDiagnostic(diagnostics, $"{scope} current entry could not be processed.");
                }
            }
        }
        finally
        {
            try
            {
                enumerator.Dispose();
            }
            catch (Exception)
            {
                AddDiagnostic(diagnostics, $"{scope} enumerator could not be disposed.");
            }
        }
    }

    private static void AddRequestedFailures(
        List<NetworkAttributeObservation> observations,
        IReadOnlyList<string>? attributeNames)
    {
        if (attributeNames is null)
        {
            return;
        }

        foreach (var name in attributeNames.Distinct(StringComparer.Ordinal))
        {
            observations.Add(new NetworkAttributeObservation
            {
                Name = name,
                CanRead = true,
                CanWrite = null,
                Availability = "available",
                ReadValue = () => throw new InvalidOperationException(
                    "Dynamic attribute metadata could not be read."),
            });
        }
    }

    private static void AddDiagnostic(List<string> diagnostics, string message)
    {
        if (!diagnostics.Contains(message, StringComparer.Ordinal))
        {
            diagnostics.Add(message);
        }
    }
}
