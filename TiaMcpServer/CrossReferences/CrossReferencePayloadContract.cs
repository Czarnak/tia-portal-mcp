using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tools;
using TiaMcpServer.Worker;

namespace TiaMcpServer.CrossReferences;

/// <summary>The only decoder of <c>read_cross_references</c> worker payloads; rejections are never echoed.</summary>
internal static class CrossReferencePayloadContract
{
    internal static StandaloneToolOutcome<CrossReferenceReport> Project(WorkerCallResult result)
    {
        if (!result.Success)
            return new(OperationBatchStatus.Failed, null, StandalonePayloadContract.Failure(result), null);
        try
        {
            var report = CrossReferenceBudget.Apply(Decode(result));
            return new(OperationBatchStatus.Succeeded, report, null, null);
        }
        catch (JsonException)
        {
            return StandalonePayloadContract.ProtocolFailure<CrossReferenceReport>();
        }
    }

    /// <exception cref="JsonException">The payload is not a valid cross-reference report.</exception>
    internal static CrossReferenceReport Decode(WorkerCallResult result)
    {
        var report = CanonicalJson.DeserializeWorkerPayload<CrossReferenceReport>(result.Payload);
        if (report.Target is null || report.Sources is null || report.Messages is null
            || !CrossReferenceFilterNames.Allowed.Contains(report.Filter)
            || report.OwnerQueryCount < 0 || report.SuccessfulOwnerQueryCount < 0
            || report.TotalSourceCount < 0 || report.TotalReferenceCount < 0
            || report.TotalLocationCount < 0 || report.OmittedSourceCount < 0)
            throw new JsonException();
        StandalonePayloadContract.RejectNullElements(report.Messages);
        ValidateSources(report.Sources);
        return report;
    }

    private static void ValidateSources(List<CrossReferenceSourceInfo> sources)
    {
        foreach (var source in sources)
        {
            if (source is null || source.References is null || source.Children is null) throw new JsonException();
            foreach (var reference in source.References)
            {
                if (reference is null || reference.Locations is null) throw new JsonException();
                foreach (var location in reference.Locations)
                    if (location is null
                        || !CrossReferenceAccessNames.All.Contains(location.Access)
                        || !CrossReferenceTypeNames.All.Contains(location.ReferenceType))
                        throw new JsonException();
            }
            ValidateSources(source.Children);
        }
    }
}
