using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
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
        if (report.Plcs.Sum(plc => plc.SuccessfulOwnerQueryCount) == 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "No cross-reference owner query succeeded for the selected PLC software.");

        report.IsComplete = report.Plcs.All(plc => plc.IsComplete);
        if (!report.IsComplete)
            Console.Error.WriteLine("Cross-reference coverage is incomplete; retained results are not a complete unused-object audit.");

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
            PlcName = plcSoftware.Name,
            DeviceName = deviceName,
            IsComplete = true
        };

        void Query(IEngineeringServiceProvider owner) => QueryOwner(owner, filter, maxSources, result);
        TryRead(() => ReadBlocks(plcSoftware.BlockGroup, Query, result), result);
        TryRead(() => ReadTags(plcSoftware.TagTableGroup, Query, result), result);
        TryRead(() => ReadTypes(plcSoftware.TypeGroup, Query, result), result);
        TryRead(() =>
        {
            var provider = plcSoftware.GetService<PlcUnitProvider>();
            if (provider is null) return; // Software units are optional, not a missing source-owner service.
            Visit(() => provider.UnitGroup.Units, unit =>
            {
                TryRead(() => ReadBlocks(unit.BlockGroup, Query, result), result);
                TryRead(() => ReadTags(unit.TagTableGroup, Query, result), result);
                TryRead(() => ReadTypes(unit.TypeGroup, Query, result), result);
            }, result);
        }, result);

        if (result.SuccessfulOwnerQueryCount == 0)
            MarkIncomplete(result);

        result.SourceCount = CountSources(result.Sources);
        result.ReferenceCount = CountReferences(result.Sources);
        result.LocationCount = CountLocations(result.Sources);

        return result;
    }

    private static void QueryOwner(IEngineeringServiceProvider owner, CrossReferenceFilter filter,
        int? maxSources, PlcCrossReferenceInfo result)
    {
        result.OwnerQueryCount++;
        TryRead(() =>
        {
            var service = owner.GetService<CrossReferenceService>();
            if (service is null)
            {
                MarkIncomplete(result);
                return;
            }
            var query = service.GetCrossReferences(filter);
            result.SuccessfulOwnerQueryCount++;
            // Continue querying owners at the cap: empty queries still prove successful coverage.
            // maxResults limits top-level roots, as before; totals include retained descendants.
            foreach (SourceObject source in query.Sources)
            {
                if (maxSources is not null && result.Sources.Count >= maxSources.Value)
                {
                    MarkIncomplete(result);
                    break;
                }
                TryRead(() => result.Sources.Add(ReadSource(source, result)), result);
            }
        }, result);
    }

    private static void ReadBlocks(PlcBlockGroup group, Action<IEngineeringServiceProvider> query, PlcCrossReferenceInfo result)
    {
        Visit(() => group.Blocks, block => { if (IsSupportedBlock(block)) query(block); }, result);
        Visit(() => group.Groups, child => ReadBlocks(child, query, result), result);
        if (group is PlcBlockSystemGroup system)
            Visit(() => system.SystemBlockGroups, child => ReadSystemBlocks(child, query, result), result);
    }

    private static void ReadSystemBlocks(PlcSystemBlockGroup group, Action<IEngineeringServiceProvider> query, PlcCrossReferenceInfo result)
    {
        Visit(() => group.Blocks, block => { if (IsSupportedBlock(block)) query(block); }, result);
        Visit(() => group.Groups, child => ReadSystemBlocks(child, query, result), result);
    }

    private static bool IsSupportedBlock(PlcBlock block) =>
        block is OB || block is FB || block is FC || block is GlobalDB || block is InstanceDB || block is ArrayDB;

    private static void ReadTags(PlcTagTableGroup group, Action<IEngineeringServiceProvider> query, PlcCrossReferenceInfo result)
    {
        Visit(() => group.TagTables, table =>
        {
            Visit(() => table.Tags, tag => query(tag), result);
            Visit(() => table.SystemConstants, constant => query(constant), result);
        }, result);
        Visit(() => group.Groups, child => ReadTags(child, query, result), result);
    }

    private static void ReadTypes(PlcTypeGroup group, Action<IEngineeringServiceProvider> query, PlcCrossReferenceInfo result)
    {
        Visit(() => group.Types, type => query(type), result);
        Visit(() => group.Groups, child => ReadTypes(child, query, result), result);
    }

    private static void Visit<T>(Func<IEnumerable<T>> items, Action<T> read, PlcCrossReferenceInfo result)
    {
        TryRead(() =>
        {
            foreach (var item in items())
                TryRead(() => read(item), result);
        }, result);
    }

    private static void TryRead(Action read, PlcCrossReferenceInfo result)
    {
        try { read(); }
        catch (Exception ex) when (ex is EngineeringException && ex is not NonRecoverableException)
        {
            MarkIncomplete(result);
        }
    }

    private static void MarkIncomplete(PlcCrossReferenceInfo result)
    {
        result.IsComplete = false;
        // Fixed, one-per-PLC diagnostic bounds output and never includes object names or exception detail.
        if (result.Messages.Count == 0)
            result.Messages.Add("Cross-reference coverage is incomplete: an owner or projection was unavailable, failed, or exceeded maxResults. Retained results are not a complete unused-object audit.");
    }

    private static CrossReferenceSourceInfo ReadSource(SourceObject source, PlcCrossReferenceInfo result)
    {
        var sourceInfo = new CrossReferenceSourceInfo
        {
            Name = SafeString(source.Name),
            TypeName = SafeString(source.TypeName),
            Path = SafeString(source.Path),
            Device = SafeString(source.Device),
            Address = SafeString(source.Address)
        };

        Visit(() => source.References, reference => sourceInfo.References.Add(ReadReference(reference, result)), result);
        Visit(() => source.Children, child => sourceInfo.Children.Add(ReadSource(child, result)), result);

        return sourceInfo;
    }

    private static CrossReferenceTargetInfo ReadReference(ReferenceObject reference, PlcCrossReferenceInfo result)
    {
        var referenceInfo = new CrossReferenceTargetInfo
        {
            Name = SafeString(reference.Name),
            TypeName = SafeString(reference.TypeName),
            Path = SafeString(reference.Path),
            Device = SafeString(reference.Device),
            Address = SafeString(reference.Address)
        };

        Visit(() => reference.Locations, location => referenceInfo.Locations.Add(ReadLocation(location)), result);

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
