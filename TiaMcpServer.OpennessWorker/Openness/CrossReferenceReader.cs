using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class CrossReferenceReader
{
    public static CrossReferenceReport Read(Project project, string? plcName, string filterName, int? maxResults = null)
    {
        var filter = ToOpennessFilter(filterName);
        var report = new CrossReferenceReport
        {
            Filter = filterName
        };

        var remaining = maxResults;
        foreach (var plc in PlcSoftwareLocator.FindAll(project, plcName))
        {
            var plcInfo = ReadPlc(plc.DeviceName, plc.Software, filter, remaining);
            report.Plcs.Add(plcInfo);

            if (remaining is not null)
            {
                remaining = Math.Max(0, remaining.Value - plcInfo.Sources.Count);
            }
        }

        if (report.Plcs.Count == 0)
        {
            var detail = plcName is null ? string.Empty : $" named '{plcName}'";
            throw new InvalidOperationException($"No PLC software{detail} was found in the project.");
        }

        report.TotalSourceCount = report.Plcs.Sum(plc => plc.SourceCount);
        report.TotalReferenceCount = report.Plcs.Sum(plc => plc.ReferenceCount);
        report.TotalLocationCount = report.Plcs.Sum(plc => plc.LocationCount);

        return report;
    }

    private static PlcCrossReferenceInfo ReadPlc(
        string deviceName,
        PlcSoftware plcSoftware,
        CrossReferenceFilter filter,
        int? maxSources)
    {
        var result = new PlcCrossReferenceInfo
        {
            PlcName = deviceName
        };

        CrossReferenceService? service = null;
        try
        {
            service = plcSoftware.GetService<CrossReferenceService>();
        }
        catch (EngineeringException ex)
        {
            result.Messages.Add($"Could not get cross-reference service for PLC '{deviceName}': {ex.Message}");
            return result;
        }

        if (service is null)
        {
            result.Messages.Add($"PLC '{deviceName}' does not expose the cross-reference service.");
            return result;
        }

        CrossReferenceResult crossReferenceResult;
        try
        {
            crossReferenceResult = service.GetCrossReferences(filter);
        }
        catch (EngineeringException ex)
        {
            result.Messages.Add($"Could not read cross references for PLC '{deviceName}': {ex.Message}");
            return result;
        }

        foreach (SourceObject source in crossReferenceResult.Sources)
        {
            if (maxSources is not null && result.Sources.Count >= maxSources.Value)
            {
                result.Messages.Add(
                    $"Truncated: maxResults limit reached while reading sources for PLC '{deviceName}'. "
                    + "Narrow with plcName or filter, or raise maxResults.");
                break;
            }

            try
            {
                result.Sources.Add(ReadSource(source, result.Messages));
            }
            catch (EngineeringException ex)
            {
                result.Messages.Add($"Skipped a cross-reference source in PLC '{deviceName}': {ex.Message}");
            }
        }

        result.SourceCount = CountSources(result.Sources);
        result.ReferenceCount = CountReferences(result.Sources);
        result.LocationCount = CountLocations(result.Sources);

        return result;
    }

    private static CrossReferenceSourceInfo ReadSource(SourceObject source, List<string> messages)
    {
        var sourceInfo = new CrossReferenceSourceInfo
        {
            Name = SafeString(source.Name),
            TypeName = SafeString(source.TypeName),
            Path = SafeString(source.Path),
            Device = SafeString(source.Device),
            Address = SafeString(source.Address)
        };

        foreach (ReferenceObject reference in source.References)
        {
            try
            {
                sourceInfo.References.Add(ReadReference(reference));
            }
            catch (EngineeringException ex)
            {
                messages.Add($"Skipped a cross-reference target for source '{sourceInfo.Name}': {ex.Message}");
            }
        }

        foreach (SourceObject child in source.Children)
        {
            try
            {
                sourceInfo.Children.Add(ReadSource(child, messages));
            }
            catch (EngineeringException ex)
            {
                messages.Add($"Skipped a child cross-reference source for source '{sourceInfo.Name}': {ex.Message}");
            }
        }

        return sourceInfo;
    }

    private static CrossReferenceTargetInfo ReadReference(ReferenceObject reference)
    {
        var referenceInfo = new CrossReferenceTargetInfo
        {
            Name = SafeString(reference.Name),
            TypeName = SafeString(reference.TypeName),
            Path = SafeString(reference.Path),
            Device = SafeString(reference.Device),
            Address = SafeString(reference.Address)
        };

        foreach (Location location in reference.Locations)
        {
            referenceInfo.Locations.Add(ReadLocation(location));
        }

        return referenceInfo;
    }

    private static CrossReferenceLocationInfo ReadLocation(Location location)
    {
        return new CrossReferenceLocationInfo
        {
            Name = SafeString(location.Name),
            TypeName = SafeString(location.TypeName),
            Address = SafeString(location.Address),
            Access = location.Access.ToString(),
            ReferenceType = location.ReferenceType.ToString(),
            ReferenceLocation = SafeString(location.ReferenceLocation),
            ReferencedAs = SafeString(location.ReferencedAs),
            ReferencedAsName = SafeString(location.ReferencedAsName)
        };
    }

    private static CrossReferenceFilter ToOpennessFilter(string filterName)
    {
        return filterName switch
        {
            CrossReferenceFilterNames.AllObjects => CrossReferenceFilter.AllObjects,
            CrossReferenceFilterNames.ObjectsWithReferences => CrossReferenceFilter.ObjectsWithReferences,
            CrossReferenceFilterNames.ObjectsWithoutReferences => CrossReferenceFilter.ObjectsWithoutReferences,
            CrossReferenceFilterNames.UnusedObjects => CrossReferenceFilter.UnusedObjects,
            _ => throw new InvalidOperationException($"Unsupported normalized cross-reference filter '{filterName}'.")
        };
    }

    private static int CountSources(IEnumerable<CrossReferenceSourceInfo> sources)
    {
        return sources.Sum(source => 1 + CountSources(source.Children));
    }

    private static int CountReferences(IEnumerable<CrossReferenceSourceInfo> sources)
    {
        return sources.Sum(source => source.References.Count + CountReferences(source.Children));
    }

    private static int CountLocations(IEnumerable<CrossReferenceSourceInfo> sources)
    {
        return sources.Sum(source =>
            source.References.Sum(reference => reference.Locations.Count) + CountLocations(source.Children));
    }

    private static string SafeString(object? value)
    {
        return value?.ToString() ?? string.Empty;
    }
}
