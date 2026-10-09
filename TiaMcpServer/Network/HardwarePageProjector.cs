using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;

namespace TiaMcpServer.Network;

internal sealed class HardwarePageProjector
{
    private const int CursorVersion = 1;
    private const string RetryTool = "network_read";

    internal const string DiagnosticsLimitReason = "hardwarePageDiagnosticsExceededItemCharLimit";
    internal const string EntityLimitReason = "hardwarePageEntityExceededItemCharLimit";
    internal const string CursorLimitReason = "hardwarePageCursorExceededCharLimit";
    internal const string CursorLimitGuidance =
        "The hardware-page continuation cursor exceeds its character limit. Shorten the project path and start a new sequence, or retry without pagination if the result fits the item limit.";
    internal const string RetryGuidance =
        "Retry the unchanged request at the same cursor, or start a new sequence with narrower filters or fewer detail options.";

    private readonly HardwarePageCursorCodec _cursorCodec;

    internal HardwarePageProjector(HardwarePageCursorCodec cursorCodec)
    {
        _cursorCodec = cursorCodec ?? throw new ArgumentNullException(nameof(cursorCodec));
    }

    internal StructuredOperationItem Project(
        NetworkOperationRequest operation,
        HardwarePageCandidateResultInfo payload,
        string resolvedProjectPath,
        WorkerSessionIdentity sessionIdentity,
        ProjectBindingSnapshot hostBinding,
        IReadOnlyList<string> warnings,
        int maxItemChars = 60_000)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(resolvedProjectPath);
        ArgumentNullException.ThrowIfNull(sessionIdentity);
        ArgumentNullException.ThrowIfNull(hostBinding);
        ArgumentNullException.ThrowIfNull(warnings);
        if (maxItemChars <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItemChars));
        }

        var candidates = payload.DeviceCandidates
            .Select(candidate => PageCandidate.ForDevice(candidate))
            .Concat(payload.SubnetCandidates.Select(candidate => PageCandidate.ForSubnet(candidate)))
            .ToArray();

        StructuredOperationItem BuildItem(int returnedCount)
        {
            try
            {
                return BuildSucceededItem(
                    operation, payload, candidates, returnedCount,
                    resolvedProjectPath, sessionIdentity, hostBinding, warnings);
            }
            catch (AuthenticatedCursorSizeException exception)
            {
                return BoundedOmission(
                    operation, CursorLimitReason, exception.CursorChars, exception.LimitChars,
                    subject: null, warnings, maxItemChars);
            }
        }

        // A terminal page may still succeed without a cursor even if this baseline cannot encode one.
        var pageOnly = BuildItem(returnedCount: 0);
        var pageOnlyChars = ItemChars(pageOnly);
        if (pageOnlyChars > maxItemChars)
        {
            return BoundedOmission(
                operation,
                DiagnosticsLimitReason,
                pageOnlyChars,
                maxItemChars,
                subject: null,
                warnings);
        }

        for (var returnedCount = candidates.Length; returnedCount > 0; returnedCount--)
        {
            var prospective = BuildItem(returnedCount);
            var prospectiveChars = ItemChars(prospective);
            if (prospectiveChars <= maxItemChars)
            {
                return prospective;
            }

            if (returnedCount == 1)
            {
                return BoundedOmission(
                    operation,
                    EntityLimitReason,
                    prospectiveChars,
                    maxItemChars,
                    candidates[0].Subject,
                    warnings);
            }
        }

        return pageOnly;
    }

    private StructuredOperationItem BuildSucceededItem(
        NetworkOperationRequest operation,
        HardwarePageCandidateResultInfo payload,
        IReadOnlyList<PageCandidate> candidates,
        int returnedCount,
        string resolvedProjectPath,
        WorkerSessionIdentity sessionIdentity,
        ProjectBindingSnapshot hostBinding,
        IReadOnlyList<string> warnings)
    {
        var returned = candidates.Take(returnedCount).ToArray();
        var nextOffset = payload.StartOffset + returnedCount;
        var total = payload.TotalDevices + payload.TotalSubnets;
        var nextCursor = nextOffset < total
            ? _cursorCodec.Encode(new HardwarePageCursorState(
                CursorVersion,
                resolvedProjectPath,
                sessionIdentity,
                ProjectBindingCursorState.FromSnapshot(hostBinding),
                payload.QueryHash,
                payload.OrderingVersion,
                payload.SnapshotHash,
                nextOffset))
            : null;
        var config = new HardwareConfigInfo
        {
            Devices = returned
                .Where(candidate => candidate.Device is not null)
                .Select(candidate => candidate.Device!)
                .ToList(),
            Subnets = returned
                .Where(candidate => candidate.Subnet is not null)
                .Select(candidate => candidate.Subnet!)
                .ToList(),
            Messages = payload.Messages
                .Concat(returned.SelectMany(candidate => candidate.Messages))
                .ToList(),
            Pagination = new HardwarePaginationInfo(
                payload.TotalDevices,
                payload.TotalSubnets,
                returned.Count(candidate => candidate.Device is not null),
                returned.Count(candidate => candidate.Subnet is not null),
                nextCursor),
        };

        return new StructuredOperationItem(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Succeeded,
            CanonicalJson.ToElement(config),
            Failure: null,
            Omission: null,
            SkipReason: null,
            warnings);
    }

    private static int ItemChars(StructuredOperationItem item) => CanonicalJson.Serialize(item).Length;

    private static StructuredOperationItem BoundedOmission(
        NetworkOperationRequest operation,
        string reason,
        int originalChars,
        int limitChars,
        StructuredOperationOmissionSubject? subject,
        IReadOnlyList<string> warnings,
        int? maxItemChars = null)
    {
        var itemLimit = maxItemChars ?? limitChars;
        var omission = Omitted(
            operation,
            reason,
            originalChars,
            limitChars,
            subject,
            warnings);
        if (ItemChars(omission) <= itemLimit)
        {
            return omission;
        }

        if (subject is not null)
        {
            omission = Omitted(
                operation,
                reason,
                originalChars,
                limitChars,
                subject: null,
                warnings);
            if (ItemChars(omission) <= itemLimit)
            {
                return omission;
            }
        }

        var minimal = warnings.Count == 0
            ? omission
            : Omitted(
                operation,
                reason,
                originalChars,
                limitChars,
                subject: null,
                warnings: Array.Empty<string>());
        if (ItemChars(minimal) > itemLimit)
        {
            // Public catalog validation leaves ample room for this fixed envelope. Keep the
            // internal seam fail-closed as well: a future bypass must not silently publish an
            // operation item that violates the projector's promised limit.
            throw new InvalidOperationException(
                $"The minimal hardware-page omission cannot fit the {itemLimit}-character item limit.");
        }

        return minimal;
    }

    private static StructuredOperationItem Omitted(
        NetworkOperationRequest operation,
        string reason,
        int originalChars,
        int limitChars,
        StructuredOperationOmissionSubject? subject,
        IReadOnlyList<string> warnings)
        => new(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Omitted,
            Result: null,
            Failure: null,
            new StructuredOperationOmission(
                reason,
                limitChars,
                originalChars,
                RetryTool,
                reason == CursorLimitReason ? CursorLimitGuidance : RetryGuidance,
                subject),
            SkipReason: null,
            warnings);

    private sealed record PageCandidate(
        DeviceInfo? Device,
        SubnetInfo? Subnet,
        IReadOnlyList<string> Messages,
        StructuredOperationOmissionSubject Subject)
    {
        internal static PageCandidate ForDevice(HardwareDevicePageCandidateInfo candidate)
            => new(
                candidate.Device,
                Subnet: null,
                candidate.Messages,
                new StructuredOperationOmissionSubject(
                    "device",
                    candidate.Device.Name ?? string.Empty,
                    Identifier: null));

        internal static PageCandidate ForSubnet(HardwareSubnetPageCandidateInfo candidate)
            => new(
                Device: null,
                candidate.Subnet,
                candidate.Messages,
                new StructuredOperationOmissionSubject(
                    "subnet",
                    candidate.Subnet.Name,
                    candidate.Subnet.SubnetId));
    }
}
