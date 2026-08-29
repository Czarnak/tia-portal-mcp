using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker;

internal enum HardwarePageDescriptorKind
{
    Device,
    Subnet,
}

/// <summary>
/// Siemens-free evidence identifying one top-level entity in a stable hardware-page sequence.
/// Structural locators remain worker-internal and are only used for deterministic snapshot evidence.
/// </summary>
internal sealed class HardwarePageDescriptor
{
    public HardwarePageDescriptor(
        HardwarePageDescriptorKind kind,
        string publicIdentity,
        string structuralLocator,
        int sourceOrder)
    {
        if (publicIdentity == null)
        {
            throw new ArgumentNullException(nameof(publicIdentity));
        }

        if (structuralLocator == null)
        {
            throw new ArgumentNullException(nameof(structuralLocator));
        }

        if (sourceOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOrder));
        }

        Kind = kind;
        PublicIdentity = publicIdentity;
        StructuralLocator = structuralLocator;
        SourceOrder = sourceOrder;
    }

    public HardwarePageDescriptorKind Kind { get; }

    public string PublicIdentity { get; }

    public string StructuralLocator { get; }

    public int SourceOrder { get; }
}

/// <summary>
/// Produces deterministic worker-local hardware-page evidence without referencing Siemens objects.
/// </summary>
internal sealed class HardwarePageDescriptorSet
{
    public const int CurrentOrderingVersion = 1;

    private readonly IReadOnlyList<HardwarePageDescriptor> _descriptors;

    public HardwarePageDescriptorSet(IEnumerable<HardwarePageDescriptor> descriptors)
    {
        if (descriptors == null)
        {
            throw new ArgumentNullException(nameof(descriptors));
        }

        _descriptors = descriptors
            .OrderBy(descriptor => descriptor.Kind)
            .ThenBy(descriptor => descriptor.PublicIdentity, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.SourceOrder)
            .ToArray();

        SnapshotHash = HardwarePageEvidence.CreateSnapshotHash(CreateCanonicalSnapshot(_descriptors));
    }

    public int OrderingVersion => CurrentOrderingVersion;

    public IReadOnlyList<HardwarePageDescriptor> Descriptors => _descriptors;

    public int TotalDevices => _descriptors.Count(descriptor => descriptor.Kind == HardwarePageDescriptorKind.Device);

    public int TotalSubnets => _descriptors.Count(descriptor => descriptor.Kind == HardwarePageDescriptorKind.Subnet);

    public string SnapshotHash { get; }

    public HardwarePageDescriptorSet Filter(Func<HardwarePageDescriptor, bool> predicate)
    {
        if (predicate == null)
        {
            throw new ArgumentNullException(nameof(predicate));
        }

        return new HardwarePageDescriptorSet(_descriptors.Where(predicate));
    }

    public IReadOnlyList<HardwarePageDescriptor> GetWindow(int startOffset, int pageSize)
    {
        if (startOffset < 0 || startOffset > _descriptors.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(startOffset));
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        return _descriptors.Skip(startOffset).Take(pageSize).ToArray();
    }

    private static string CreateCanonicalSnapshot(IEnumerable<HardwarePageDescriptor> descriptors)
    {
        var canonical = new StringBuilder();
        AppendField(canonical, CurrentOrderingVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var descriptor in descriptors)
        {
            AppendField(canonical, ((int)descriptor.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture));
            AppendField(canonical, descriptor.PublicIdentity);
            AppendField(canonical, descriptor.StructuralLocator);
            AppendField(canonical, descriptor.SourceOrder.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return canonical.ToString();
    }

    private static void AppendField(StringBuilder destination, string value)
    {
        destination.Append(value.Length);
        destination.Append(':');
        destination.Append(value);
    }
}
