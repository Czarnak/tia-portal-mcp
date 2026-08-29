using System;
using System.Collections.Generic;
using System.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Pure coordinator for one internal hardware-candidate read. Siemens traversal and
/// materialization stay behind <see cref="HardwarePageCandidateSource"/> callbacks so the
/// ordering, continuation, and message-scoping rules remain directly testable.
/// </summary>
internal static class HardwarePageCandidateReader
{
    private const int DefaultPageSize = 50;
    private const int MaximumPageSize = 200;

    public static HardwarePageCandidateResultInfo Read(
        HardwarePageCandidateSource source,
        string? deviceName,
        string? plcName,
        bool includeIoDetails,
        bool includeTagMatches,
        int? requestedPageSize,
        HardwarePageContinuationInfo? continuation)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        if (includeTagMatches && !includeIoDetails)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "IncludeTagMatches requires IncludeIoDetails to also be true.");
        }

        var pageSize = ResolvePageSize(requestedPageSize, continuation);
        var queryHash = HardwarePageEvidence.CreateQueryHash(
            deviceName,
            plcName,
            includeIoDetails,
            includeTagMatches);

        // Enumeration deliberately precedes continuation validation. A continuation is valid
        // only against the complete current matching descriptor set, never a selected window.
        var inventory = source.Enumerate();
        var descriptors = inventory.Descriptors;
        ValidateContinuation(continuation, descriptors, queryHash);

        var startOffset = continuation?.Offset ?? 0;
        if (startOffset < 0 || startOffset > descriptors.Descriptors.Count)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CursorOutOfRange,
                "The hardware-page cursor offset is outside the current candidate set.");
        }

        var deviceCandidates = new List<HardwareDevicePageCandidateInfo>();
        var subnetCandidates = new List<HardwareSubnetPageCandidateInfo>();
        var window = descriptors.GetWindow(startOffset, pageSize);
        for (var windowIndex = 0; windowIndex < window.Count; windowIndex++)
        {
            var descriptor = window[windowIndex];
            var materialized = source.Materialize(descriptor);
            var offset = startOffset + windowIndex;

            if (descriptor.Kind == HardwarePageDescriptorKind.Device && materialized.Device is not null)
            {
                deviceCandidates.Add(new HardwareDevicePageCandidateInfo(
                    offset,
                    materialized.Device,
                    materialized.Messages));
                continue;
            }

            if (descriptor.Kind == HardwarePageDescriptorKind.Subnet && materialized.Subnet is not null)
            {
                subnetCandidates.Add(new HardwareSubnetPageCandidateInfo(
                    offset,
                    materialized.Subnet,
                    materialized.Messages));
                continue;
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.ProtocolError,
                "The internal hardware candidate materializer returned the wrong entity kind.");
        }

        return new HardwarePageCandidateResultInfo(
            descriptors.OrderingVersion,
            queryHash,
            descriptors.SnapshotHash,
            startOffset,
            descriptors.TotalDevices,
            descriptors.TotalSubnets,
            inventory.Messages,
            deviceCandidates,
            subnetCandidates);
    }

    private static int ResolvePageSize(
        int? requestedPageSize,
        HardwarePageContinuationInfo? continuation)
    {
        if (requestedPageSize is null)
        {
            if (continuation is not null)
            {
                return DefaultPageSize;
            }

            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "HardwarePageSize is required for the first hardware page.");
        }

        if (requestedPageSize < 1 || requestedPageSize > MaximumPageSize)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "HardwarePageSize must be between 1 and 200.");
        }

        return requestedPageSize.Value;
    }

    private static void ValidateContinuation(
        HardwarePageContinuationInfo? continuation,
        HardwarePageDescriptorSet descriptors,
        string queryHash)
    {
        if (continuation is null)
        {
            return;
        }

        if (!string.Equals(continuation.QueryHash, queryHash, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CursorFilterMismatch,
                "The hardware-page cursor does not match the requested filters and detail options.");
        }

        if (continuation.OrderingVersion != descriptors.OrderingVersion
            || !string.Equals(continuation.SnapshotHash, descriptors.SnapshotHash, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.CursorSnapshotMismatch,
                "The hardware candidate set or ordering changed after the cursor was issued.");
        }
    }
}

internal sealed class HardwarePageCandidateSource
{
    private readonly Func<HardwarePageCandidateInventory> _enumerate;
    private readonly Func<HardwarePageDescriptor, HardwarePageCandidateMaterialization> _materialize;

    public HardwarePageCandidateSource(
        Func<HardwarePageCandidateInventory> enumerate,
        Func<HardwarePageDescriptor, HardwarePageCandidateMaterialization> materialize)
    {
        _enumerate = enumerate ?? throw new ArgumentNullException(nameof(enumerate));
        _materialize = materialize ?? throw new ArgumentNullException(nameof(materialize));
    }

    public HardwarePageCandidateInventory Enumerate() => _enumerate();

    public HardwarePageCandidateMaterialization Materialize(HardwarePageDescriptor descriptor)
        => _materialize(descriptor ?? throw new ArgumentNullException(nameof(descriptor)));
}

internal sealed class HardwarePageCandidateInventory
{
    public HardwarePageCandidateInventory(
        HardwarePageDescriptorSet descriptors,
        IReadOnlyList<string> messages)
    {
        Descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));
        Messages = messages?.ToArray() ?? throw new ArgumentNullException(nameof(messages));
    }

    public HardwarePageDescriptorSet Descriptors { get; }

    public IReadOnlyList<string> Messages { get; }
}

internal sealed class HardwarePageCandidateMaterialization
{
    private HardwarePageCandidateMaterialization(
        DeviceInfo? device,
        SubnetInfo? subnet,
        IReadOnlyList<string> messages)
    {
        Device = device;
        Subnet = subnet;
        Messages = messages?.ToArray() ?? throw new ArgumentNullException(nameof(messages));
    }

    public DeviceInfo? Device { get; }

    public SubnetInfo? Subnet { get; }

    public IReadOnlyList<string> Messages { get; }

    public static HardwarePageCandidateMaterialization ForDevice(
        DeviceInfo device,
        IReadOnlyList<string> messages)
        => new(
            device ?? throw new ArgumentNullException(nameof(device)),
            subnet: null,
            messages);

    public static HardwarePageCandidateMaterialization ForSubnet(
        SubnetInfo subnet,
        IReadOnlyList<string> messages)
        => new(
            device: null,
            subnet ?? throw new ArgumentNullException(nameof(subnet)),
            messages);
}
