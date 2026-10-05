using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;

namespace TiaMcpServer.CrossReferences;

/// <summary>
/// Drops whole tail sources until the report fits the value and document budgets, so the generic
/// standalone renderer never has to omit the entire report.
/// </summary>
internal static class CrossReferenceBudget
{
    internal const string NarrowingMessage = "Narrow with maxResults, a narrower target path, or a member.";

    // Room for root warnings that the composed document carries beside the report.
    private const int DocumentReserveChars = 1_000;

    internal static CrossReferenceReport Apply(
        CrossReferenceReport report,
        int maxValueChars = StructuredOperationBatchPayloadBudget.MaxItemChars,
        int maxDocumentChars = StructuredOperationBatchPayloadBudget.MaxDocumentChars)
    {
        if (Fits(report, maxValueChars, maxDocumentChars))
            return report;

        // Fit is monotonic in the number of kept sources, so binary search the largest fitting head.
        var low = 0;
        var high = report.Sources.Count - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (Fits(Keep(report, mid), maxValueChars, maxDocumentChars)) low = mid;
            else high = mid - 1;
        }

        return Keep(report, low);
    }

    private static bool Fits(CrossReferenceReport report, int maxValueChars, int maxDocumentChars)
        => CanonicalJson.Serialize(report).Length <= maxValueChars
            && CanonicalJson.Serialize(new ReadCrossReferencesResponse(
                StructuredStandaloneResult.Version, true, null, Array.Empty<string>(),
                new StandaloneToolOutcome<CrossReferenceReport>(OperationBatchStatus.Succeeded, report, null, null))).Length
                <= maxDocumentChars - DocumentReserveChars;

    private static CrossReferenceReport Keep(CrossReferenceReport report, int count) => new()
    {
        Target = report.Target,
        Filter = report.Filter,
        IsComplete = false,
        OwnerQueryCount = report.OwnerQueryCount,
        SuccessfulOwnerQueryCount = report.SuccessfulOwnerQueryCount,
        Messages = report.Messages.Append(NarrowingMessage).ToList(),
        Sources = report.Sources.Take(count).ToList(),
        TotalSourceCount = report.TotalSourceCount,
        TotalReferenceCount = report.TotalReferenceCount,
        TotalLocationCount = report.TotalLocationCount,
        OmittedSourceCount = report.OmittedSourceCount + (report.Sources.Count - count),
    };
}
