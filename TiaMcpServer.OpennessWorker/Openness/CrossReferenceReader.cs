using Siemens.Engineering;
using Siemens.Engineering.CrossReference;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class CrossReferenceReader
{
    public static CrossReferenceReport Read(Project project, CrossReferenceSelectorInfo selector, string filterName, int? maxResults = null)
    {
        var filter = ToOpennessFilter(filterName);
        var target = CrossReferenceTargetResolver.Resolve(project, selector);
        var report = new CrossReferenceReport
        {
            Target = target.Canonical,
            Filter = filterName,
            IsComplete = true
        };

        if (target.LeafService is not null)
            QueryService(target.LeafService, filter, maxResults, report);
        else
            FanOut(target.Container!, owner => QueryOwner(owner, filter, maxResults, report), report);

        // An empty container is a complete success; owners that all failed are not.
        if (report.OwnerQueryCount > 0 && report.SuccessfulOwnerQueryCount == 0)
            throw new WorkerOperationException(WorkerFailureCategories.WorkerOperationFailed,
                "No cross-reference owner query succeeded for the selected target.");

        report.TotalSourceCount = CountSources(report.Sources);
        report.TotalReferenceCount = CountReferences(report.Sources);
        report.TotalLocationCount = CountLocations(report.Sources);
        if (!report.IsComplete)
            Console.Error.WriteLine("Cross-reference coverage is incomplete; retained results are not a complete unused-object audit.");

        return report;
    }

    // Containers are never owners themselves (spec Appendix B); only the owners beneath them are queried.
    private static void FanOut(object container, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        switch (container)
        {
            case Device device:
                Visit(() => PlcSoftwareLocator.FindInDevice(device), software => ReadSoftware(software, query, report), report);
                break;
            case PlcSoftware software:
                ReadSoftware(software, query, report);
                break;
            case PlcUnit unit:
                ReadUnit(unit, query, report);
                break;
            case PlcSystemBlockGroup group:
                ReadSystemBlocks(group, query, report);
                break;
            case PlcBlockGroup group:
                ReadBlocks(group, query, report);
                break;
            case PlcTagTableGroup group:
                ReadTags(group, query, report);
                break;
            case PlcTagTable table:
                ReadTable(table, query, report);
                break;
            case PlcTypeGroup group:
                ReadTypes(group, query, report);
                break;
            default:
                throw new WorkerOperationException(WorkerFailureCategories.TargetKindUnsupported,
                    "The selected target does not provide cross-references.");
        }
    }

    private static void ReadSoftware(PlcSoftware software, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        TryRead(() => ReadBlocks(software.BlockGroup, query, report), report);
        TryRead(() => ReadTags(software.TagTableGroup, query, report), report);
        TryRead(() => ReadTypes(software.TypeGroup, query, report), report);
        TryRead(() =>
        {
            var provider = software.GetService<PlcUnitProvider>();
            if (provider is null) return; // Software units are optional, not a missing source-owner service.
            Visit(() => provider.UnitGroup.Units, unit => ReadUnit(unit, query, report), report);
        }, report);
    }

    private static void ReadUnit(PlcUnit unit, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        TryRead(() => ReadBlocks(unit.BlockGroup, query, report), report);
        TryRead(() => ReadTags(unit.TagTableGroup, query, report), report);
        TryRead(() => ReadTypes(unit.TypeGroup, query, report), report);
    }

    private static void QueryOwner(IEngineeringServiceProvider owner, CrossReferenceFilter filter,
        int? maxSources, CrossReferenceReport report)
    {
        report.OwnerQueryCount++;
        TryRead(() =>
        {
            var service = owner.GetService<CrossReferenceService>();
            if (service is null)
            {
                MarkIncomplete(report);
                return;
            }
            RunQuery(service, filter, maxSources, report);
        }, report);
    }

    private static void QueryService(CrossReferenceService service, CrossReferenceFilter filter,
        int? maxSources, CrossReferenceReport report)
    {
        report.OwnerQueryCount++;
        RunQuery(service, filter, maxSources, report);
    }

    private static void RunQuery(CrossReferenceService service, CrossReferenceFilter filter,
        int? maxSources, CrossReferenceReport report)
    {
        TryRead(() =>
        {
            var query = service.GetCrossReferences(filter);
            report.SuccessfulOwnerQueryCount++;
            // Continue querying owners at the cap: empty queries still prove successful coverage.
            // maxResults limits top-level roots; totals include retained descendants.
            foreach (SourceObject source in query.Sources)
            {
                if (maxSources is not null && report.Sources.Count >= maxSources.Value)
                {
                    MarkIncomplete(report);
                    break;
                }
                TryRead(() => report.Sources.Add(ReadSource(source, report)), report);
            }
        }, report);
    }

    private static void ReadBlocks(PlcBlockGroup group, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        Visit(() => group.Blocks, block => { if (IsSupportedBlock(block)) query(block); }, report);
        Visit(() => group.Groups, child => ReadBlocks(child, query, report), report);
        if (group is PlcBlockSystemGroup system)
            Visit(() => system.SystemBlockGroups, child => ReadSystemBlocks(child, query, report), report);
    }

    private static void ReadSystemBlocks(PlcSystemBlockGroup group, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        Visit(() => group.Blocks, block => { if (IsSupportedBlock(block)) query(block); }, report);
        Visit(() => group.Groups, child => ReadSystemBlocks(child, query, report), report);
    }

    private static bool IsSupportedBlock(PlcBlock block) =>
        block is OB || block is FB || block is FC || block is GlobalDB || block is InstanceDB || block is ArrayDB;

    private static void ReadTags(PlcTagTableGroup group, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        Visit(() => group.TagTables, table => ReadTable(table, query, report), report);
        Visit(() => group.Groups, child => ReadTags(child, query, report), report);
    }

    private static void ReadTable(PlcTagTable table, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        Visit(() => table.Tags, tag => query(tag), report);
        Visit(() => table.SystemConstants, constant => query(constant), report);
        Visit(() => table.UserConstants, constant => query(constant), report);
    }

    private static void ReadTypes(PlcTypeGroup group, Action<IEngineeringServiceProvider> query, CrossReferenceReport report)
    {
        Visit(() => group.Types, type => query(type), report);
        Visit(() => group.Groups, child => ReadTypes(child, query, report), report);
    }

    private static void Visit<T>(Func<IEnumerable<T>> items, Action<T> read, CrossReferenceReport report)
    {
        TryRead(() =>
        {
            foreach (var item in items())
                TryRead(() => read(item), report);
        }, report);
    }

    private static void TryRead(Action read, CrossReferenceReport report)
    {
        try { read(); }
        catch (Exception ex) when (ex is not NonRecoverableException &&
            (ex is EngineeringException || ex.GetType() == typeof(InvalidOperationException)))
        {
            MarkIncomplete(report);
        }
    }

    private static void MarkIncomplete(CrossReferenceReport report)
    {
        report.IsComplete = false;
        // Fixed, one-per-report diagnostic bounds output and never includes object names or exception detail.
        if (report.Messages.Count == 0)
            report.Messages.Add("Cross-reference coverage is incomplete: an owner or projection was unavailable, failed, or exceeded maxResults. Retained results are not a complete unused-object audit.");
    }

    private static CrossReferenceSourceInfo ReadSource(SourceObject source, CrossReferenceReport report)
    {
        var sourceInfo = new CrossReferenceSourceInfo
        {
            Name = SafeString(source.Name),
            TypeName = SafeString(source.TypeName),
            Path = SafeString(source.Path),
            Device = SafeString(source.Device),
            Address = SafeString(source.Address)
        };

        Visit(() => source.References, reference => sourceInfo.References.Add(ReadReference(reference, report)), report);
        Visit(() => source.Children, child => sourceInfo.Children.Add(ReadSource(child, report)), report);

        return sourceInfo;
    }

    private static CrossReferenceTargetInfo ReadReference(ReferenceObject reference, CrossReferenceReport report)
    {
        var referenceInfo = new CrossReferenceTargetInfo
        {
            Name = SafeString(reference.Name),
            TypeName = SafeString(reference.TypeName),
            Path = SafeString(reference.Path),
            Device = SafeString(reference.Device),
            Address = SafeString(reference.Address)
        };

        Visit(() => reference.Locations, location => referenceInfo.Locations.Add(ReadLocation(location, report)), report);

        return referenceInfo;
    }

    private static CrossReferenceLocationInfo ReadLocation(Location location, CrossReferenceReport report)
    {
        var info = new CrossReferenceLocationInfo
        {
            Name = SafeString(location.Name),
            TypeName = SafeString(location.TypeName),
            Address = SafeString(location.Address),
            Access = ClosedName(location.Access.ToString(), CrossReferenceAccessNames.All, CrossReferenceAccessNames.Unknown),
            ReferenceType = ClosedName(location.ReferenceType.ToString(), CrossReferenceTypeNames.All, CrossReferenceTypeNames.Unknown),
            ReferenceLocation = SafeString(location.ReferenceLocation),
            ReferencedAsName = SafeString(location.ReferencedAsName)
        };
        // An unreadable referenced object keeps the location, as null plus an incomplete report.
        TryRead(() => info.ReferencedAs = ReadObjectRef(location.ReferencedAs), report);
        return info;
    }

    private static CrossReferenceObjectRefInfo? ReadObjectRef(IEngineeringObject? referenced)
    {
        if (referenced is null) return null;
        return new CrossReferenceObjectRefInfo
        {
            Name = SafeString(referenced.GetAttribute("Name")),
            TypeName = referenced.GetType().Name
        };
    }

    // ponytail: a value outside the V21 enum (a later TIA version) maps to "Unknown"; widen the
    // closed lists when the metadata gains members.
    private static string ClosedName(string name, IReadOnlyList<string> closed, string unknown)
        => closed.Contains(name) ? name : unknown;

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
