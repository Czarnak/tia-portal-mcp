using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.CrossReferences;

/// <summary>
/// Drops whole tail sources until the report fits the value and document budgets, so the generic
/// standalone renderer never has to omit the entire report. When not even one whole source fits,
/// the first source is kept and trimmed inside instead of returning no sources: tail child sources
/// go first, then tail references, then tail locations of the one remaining reference.
/// </summary>
internal static class CrossReferenceBudget
{
    internal const string NarrowingMessage =
        "Narrow with maxResults, filter, a narrower target path, or a member; "
        + "a single heavily used object may exceed the 60,000-character limit.";

    // Room for root warnings that the composed document carries beside the report.
    private const int DocumentReserveChars = 1_000;

    internal static CrossReferenceReport Apply(
        CrossReferenceReport report,
        int maxValueChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
    {
        bool Fits(CrossReferenceReport candidate) => FitsBudget(candidate, maxValueChars, maxDocumentChars);

        if (Fits(report))
            return report;

        if (report.Sources.Count == 0)
            return Keep(report, 0);

        // Fit is monotonic in the number of kept sources, so binary search the largest fitting head.
        var kept = LargestFitting(report.Sources.Count - 1, count => Fits(Keep(report, count)));
        return kept >= 1 ? Keep(report, kept) : TrimFirstSource(report, Fits);
    }

    private static CrossReferenceReport TrimFirstSource(
        CrossReferenceReport report,
        Func<CrossReferenceReport, bool> fits)
    {
        var source = report.Sources[0];
        CrossReferenceReport With(CrossReferenceSourceInfo trimmed) => Trimmed(report, source, trimmed);

        var children = LargestFitting(source.Children.Count,
            count => fits(With(Copy(source, source.Children.Take(count), source.References))));
        if (children >= 0)
            return With(Copy(source, source.Children.Take(children), source.References));

        var references = LargestFitting(source.References.Count,
            count => fits(With(Copy(source, [], source.References.Take(count)))));
        if (references != 0 || source.References.Count == 0)
        {
            // -1: even the bare source does not fit; it is still the one source kept.
            return With(Copy(source, [], source.References.Take(Math.Max(references, 0))));
        }

        var first = source.References[0];
        var locations = LargestFitting(first.Locations.Count,
            count => fits(With(Copy(source, [], [Copy(first, first.Locations.Take(count))]))));
        return locations >= 0
            ? With(Copy(source, [], [Copy(first, first.Locations.Take(locations))]))
            : With(Copy(source, [], []));
    }

    // Largest count in [0, max] for which fits holds (fits is monotonic), or -1 when none does.
    private static int LargestFitting(int max, Func<int, bool> fits)
    {
        if (!fits(0))
            return -1;

        var low = 0;
        var high = max;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (fits(mid)) low = mid;
            else high = mid - 1;
        }

        return low;
    }

    private static bool FitsBudget(CrossReferenceReport report, int maxValueChars, int maxDocumentChars)
        => CanonicalJson.Serialize(report).Length <= maxValueChars
            && CanonicalJson.Serialize(new ReadCrossReferencesResponse(
                StructuredStandaloneResult.Version, true, null, Array.Empty<string>(),
                new StandaloneToolOutcome<CrossReferenceReport>(OperationBatchStatus.Succeeded, report, null, null))).Length
                <= maxDocumentChars - DocumentReserveChars;

    private static CrossReferenceReport Keep(CrossReferenceReport report, int count)
        => Bounded(report, report.Sources.Take(count).ToList(), new[] { NarrowingMessage });

    private static CrossReferenceReport Trimmed(
        CrossReferenceReport report,
        CrossReferenceSourceInfo original,
        CrossReferenceSourceInfo trimmed)
    {
        var (sources, references, locations) = Count(original);
        var (keptSources, keptReferences, keptLocations) = Count(trimmed);
        var detail = $"Source '{original.Name}' was trimmed to fit: omitted {sources - keptSources} child sources, "
            + $"{references - keptReferences} references and {locations - keptLocations} locations.";
        return Bounded(report, new List<CrossReferenceSourceInfo> { trimmed }, new[] { NarrowingMessage, detail });
    }

    private static CrossReferenceReport Bounded(
        CrossReferenceReport report,
        List<CrossReferenceSourceInfo> sources,
        IEnumerable<string> messages) => new()
    {
        Target = report.Target,
        Filter = report.Filter,
        IsComplete = false,
        OwnerQueryCount = report.OwnerQueryCount,
        SuccessfulOwnerQueryCount = report.SuccessfulOwnerQueryCount,
        Messages = report.Messages.Concat(messages).ToList(),
        Sources = sources,
        TotalSourceCount = report.TotalSourceCount,
        TotalReferenceCount = report.TotalReferenceCount,
        TotalLocationCount = report.TotalLocationCount,
        OmittedSourceCount = report.OmittedSourceCount + (report.Sources.Count - sources.Count),
    };

    // Descendant child sources (excluding the source itself), references and locations, recursively.
    private static (int Sources, int References, int Locations) Count(CrossReferenceSourceInfo source)
    {
        var references = source.References.Count;
        var locations = source.References.Sum(reference => reference.Locations.Count);
        var sources = 0;
        foreach (var child in source.Children)
        {
            var (s, r, l) = Count(child);
            sources += 1 + s;
            references += r;
            locations += l;
        }

        return (sources, references, locations);
    }

    private static CrossReferenceSourceInfo Copy(
        CrossReferenceSourceInfo source,
        IEnumerable<CrossReferenceSourceInfo> children,
        IEnumerable<CrossReferenceTargetInfo> references) => new()
    {
        Name = source.Name,
        TypeName = source.TypeName,
        Path = source.Path,
        Device = source.Device,
        Address = source.Address,
        References = references.ToList(),
        Children = children.ToList(),
    };

    private static CrossReferenceTargetInfo Copy(
        CrossReferenceTargetInfo reference,
        IEnumerable<CrossReferenceLocationInfo> locations) => new()
    {
        Name = reference.Name,
        TypeName = reference.TypeName,
        Path = reference.Path,
        Device = reference.Device,
        Address = reference.Address,
        Locations = locations.ToList(),
    };
}
