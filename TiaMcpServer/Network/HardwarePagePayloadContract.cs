using System.Text.Json;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

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

    private static readonly string[] RequiredRootMembers =
    {
        "orderingVersion",
        "queryHash",
        "snapshotHash",
        "startOffset",
        "totalDevices",
        "totalSubnets",
        "messages",
        "deviceCandidates",
        "subnetCandidates",
    };

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
            ValidateRequiredJsonShape(workerResult.Payload);
            var payload = CanonicalJson.Deserialize<HardwarePageCandidateResultInfo>(workerResult.Payload);
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

    private static void ValidateRequiredJsonShape(string payload)
    {
        using var document = JsonDocument.Parse(payload, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        });
        var root = document.RootElement;
        RequireObject(root, "hardware page payload");
        RequireMembers(root, "hardware page payload", RequiredRootMembers);
        RequireStringArray(root.GetProperty("messages"), "messages");

        var deviceCandidates = root.GetProperty("deviceCandidates");
        RequireArray(deviceCandidates, "deviceCandidates");
        foreach (var candidate in deviceCandidates.EnumerateArray())
        {
            RequireObject(candidate, "deviceCandidates[]");
            RequireMembers(candidate, "deviceCandidates[]", "offset", "device", "messages");
            RequireObject(candidate.GetProperty("device"), "deviceCandidates[].device");
            RequireStringArray(candidate.GetProperty("messages"), "deviceCandidates[].messages");
        }

        var subnetCandidates = root.GetProperty("subnetCandidates");
        RequireArray(subnetCandidates, "subnetCandidates");
        foreach (var candidate in subnetCandidates.EnumerateArray())
        {
            RequireObject(candidate, "subnetCandidates[]");
            RequireMembers(candidate, "subnetCandidates[]", "offset", "subnet", "messages");
            RequireObject(candidate.GetProperty("subnet"), "subnetCandidates[].subnet");
            RequireStringArray(candidate.GetProperty("messages"), "subnetCandidates[].messages");
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

    private static void RequireMembers(JsonElement value, string path, params string[] members)
    {
        foreach (var member in members)
        {
            if (!value.TryGetProperty(member, out _))
            {
                throw new JsonException($"'{path}.{member}' is required.");
            }
        }
    }

    private static void RequireObject(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"'{path}' must be an object.");
        }
    }

    private static void RequireArray(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException($"'{path}' must be an array.");
        }
    }

    private static void RequireStringArray(JsonElement value, string path)
    {
        RequireArray(value, path);
        if (value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            throw new JsonException($"'{path}' must contain only strings.");
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
