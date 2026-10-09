using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

internal sealed record HardwarePagePayloadContractResult(
    HardwarePageCandidateResultInfo? Payload,
    StructuredOperationItem? Item)
{
    internal bool IsSuccess => Payload is not null && Item is null;
}

internal static class HardwarePagePayloadContract
{
    private static readonly Regex LowercaseSha256 = new(
        "^[0-9a-f]{64}$",
        RegexOptions.CultureInvariant);

    internal static HardwarePagePayloadContractResult Decode(
        NetworkOperationRequest operation,
        WorkerCallResult workerResult,
        HardwarePageContinuationInfo? continuation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(workerResult);
        var warnings = workerResult.Warnings ?? Array.Empty<string>();

        if (!workerResult.Success)
        {
            return new HardwarePagePayloadContractResult(
                Payload: null,
                Failed(
                    operation,
                    workerResult.FailureCategory ?? WorkerFailureCategories.WorkerOperationFailed,
                    workerResult.Error ?? "The hardware-page worker operation failed.",
                    warnings));
        }

        try
        {
            var payload = CanonicalJson.DeserializeWorkerPayload<HardwarePageCandidateResultInfo>(workerResult.Payload);
            Validate(operation, payload, continuation);
            return new HardwarePagePayloadContractResult(payload, Item: null);
        }
        catch (JsonException)
        {
            return new HardwarePagePayloadContractResult(
                Payload: null,
                Failed(
                    operation,
                    WorkerFailureCategories.ProtocolError,
                    "The hardware-page worker payload did not match its declared result contract and was rejected.",
                    warnings));
        }
    }

    private static void Validate(
        NetworkOperationRequest operation,
        HardwarePageCandidateResultInfo payload,
        HardwarePageContinuationInfo? continuation)
    {
        if (payload.Messages is null
            || payload.DeviceCandidates is null
            || payload.SubnetCandidates is null
            || payload.Messages.Any(message => message is null)
            || payload.DeviceCandidates.Any(candidate =>
                candidate is null || candidate.Device is null || candidate.Messages is null
                || candidate.Messages.Any(message => message is null))
            || payload.SubnetCandidates.Any(candidate =>
                candidate is null || candidate.Subnet is null || candidate.Messages is null
                || candidate.Messages.Any(message => message is null)))
        {
            throw new JsonException("Hardware page collections and candidates must be non-null.");
        }

        var expectedQueryHash = HardwarePageEvidence.CreateQueryHash(
            operation.DeviceName,
            operation.PlcName,
            operation.IncludeIoDetails,
            operation.IncludeTagMatches);
        var expectedStartOffset = continuation?.Offset ?? 0;
        if (payload.OrderingVersion <= 0
            || !string.Equals(payload.QueryHash, expectedQueryHash, StringComparison.Ordinal)
            || !LowercaseSha256.IsMatch(payload.SnapshotHash ?? string.Empty)
            || payload.StartOffset != expectedStartOffset
            || payload.TotalDevices < 0
            || payload.TotalSubnets < 0)
        {
            throw new JsonException("Hardware page evidence or counts are invalid.");
        }

        if (continuation is not null
            && (payload.OrderingVersion != continuation.OrderingVersion
                || !string.Equals(payload.QueryHash, continuation.QueryHash, StringComparison.Ordinal)
                || !string.Equals(payload.SnapshotHash, continuation.SnapshotHash, StringComparison.Ordinal)))
        {
            throw new JsonException("Hardware page continuation evidence is inconsistent.");
        }

        var total = (long)payload.TotalDevices + payload.TotalSubnets;
        var returned = payload.DeviceCandidates.Count + payload.SubnetCandidates.Count;
        var effectivePageSize = operation.PageSize ?? 50;
        if (total > int.MaxValue
            || payload.StartOffset < 0
            || payload.StartOffset > total
            || payload.DeviceCandidates.Count > payload.TotalDevices
            || payload.SubnetCandidates.Count > payload.TotalSubnets
            || returned > effectivePageSize
            || payload.StartOffset + (long)returned > total
            || returned != Math.Min((long)effectivePageSize, total - payload.StartOffset))
        {
            throw new JsonException("Hardware page totals and returned counts are inconsistent.");
        }

        var expectedOffset = payload.StartOffset;
        foreach (var candidate in payload.DeviceCandidates)
        {
            if (candidate.Offset != expectedOffset || candidate.Offset >= payload.TotalDevices)
            {
                throw new JsonException("Hardware device candidate offsets are invalid.");
            }

            expectedOffset++;
        }

        foreach (var candidate in payload.SubnetCandidates)
        {
            if (candidate.Offset != expectedOffset
                || candidate.Offset < payload.TotalDevices
                || candidate.Offset >= total)
            {
                throw new JsonException("Hardware subnet candidate offsets are invalid.");
            }

            expectedOffset++;
        }

        var publicPayload = new HardwareConfigInfo
        {
            Devices = payload.DeviceCandidates.Select(candidate => candidate.Device).ToList(),
            Subnets = payload.SubnetCandidates.Select(candidate => candidate.Subnet).ToList(),
            Messages = payload.Messages
                .Concat(payload.DeviceCandidates.SelectMany(candidate => candidate.Messages))
                .Concat(payload.SubnetCandidates.SelectMany(candidate => candidate.Messages))
                .ToList(),
        };
        var publicProjection = NetworkPayloadContract.Project(
            operation,
            WorkerCallResult.Ok(CanonicalJson.Serialize(publicPayload)),
            _ => { });
        if (!string.Equals(publicProjection.Status, OperationBatchStatus.Succeeded, StringComparison.Ordinal))
        {
            throw new JsonException("Hardware candidates do not match the public hardware contract.");
        }
    }

    internal static StructuredOperationItem Failed(
        NetworkOperationRequest operation,
        string category,
        string message,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Failed,
            Result: null,
            new StructuredOperationFailure(category, message),
            Omission: null,
            SkipReason: null,
            warnings);
}
