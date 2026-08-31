using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Builds the Siemens-backed source used by the pure hardware-page candidate coordinator.
/// Descriptor traversal stays complete and lightweight; deep public materialization remains
/// delegated to the existing hardware reader only for the selected descriptor window.
/// </summary>
internal static class HardwarePageCandidateSourceFactory
{
    public static HardwarePageCandidateSource Create(
        Project project,
        string? deviceName,
        string? plcName,
        bool includeIoDetails,
        bool includeTagMatches)
    {
        var devicesByLocator = new Dictionary<
            string,
            (Device Device, NetworkObjectDiscoveryEvidenceValue<string> NameEvidence)>(StringComparer.Ordinal);
        var subnetsByLocator = new Dictionary<
            string,
            (Subnet Subnet, NetworkObjectDiscoveryEvidenceValue<string> SubnetId)>(StringComparer.Ordinal);
        IoTagIndex? tagIndex = null;
        var enumerated = false;

        return new HardwarePageCandidateSource(
            enumerate: () =>
            {
                if (enumerated)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.ProtocolError,
                        "The internal hardware candidate source was enumerated more than once.");
                }

                enumerated = true;
                var pageMessages = new List<string>();
                if (includeTagMatches)
                {
                    tagIndex = HardwareConfigReader.ResolvePageTagIndex(project, plcName, pageMessages);
                }

                var descriptors = new List<HardwarePageDescriptor>();
                var deviceCandidates = ProjectDeviceEnumerator
                    .EnumerateWithLocations(project)
                    .Select(locatedDevice =>
                    {
                        var nameEvidence = HardwareConfigReader.ReadTypedIdentityString(
                            () => locatedDevice.Device.Name,
                            "Device name");
                        return (LocatedDevice: locatedDevice, NameEvidence: nameEvidence);
                    })
                    .ToList();

                IReadOnlyList<(
                    LocatedProjectDevice LocatedDevice,
                    NetworkObjectDiscoveryEvidenceValue<string> NameEvidence)> selectedDevices;
                if (deviceName is null)
                {
                    selectedDevices = deviceCandidates;
                }
                else
                {
                    var matches = deviceCandidates
                        .Where(candidate => candidate.NameEvidence.IsUsable
                            && string.Equals(
                                candidate.NameEvidence.Value,
                                deviceName,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (matches.Count == 1)
                    {
                        selectedDevices = matches;
                    }
                    else
                    {
                        pageMessages.Add(matches.Count == 0
                            ? $"No device named '{deviceName}' was found; no devices are reported."
                            : $"More than one device matches '{deviceName}'; no devices are reported because the device filter is ambiguous.");
                        selectedDevices = Array.Empty<(
                            LocatedProjectDevice,
                            NetworkObjectDiscoveryEvidenceValue<string>)>();
                    }
                }

                foreach (var candidate in selectedDevices)
                {
                    var publicIdentity = candidate.NameEvidence.IsUsable
                        ? candidate.NameEvidence.Value
                        : string.Empty;
                    descriptors.Add(new HardwarePageDescriptor(
                        HardwarePageDescriptorKind.Device,
                        publicIdentity,
                        candidate.LocatedDevice.StructuralLocator,
                        candidate.LocatedDevice.SourceOrder));
                    devicesByLocator.Add(
                        candidate.LocatedDevice.StructuralLocator,
                        (candidate.LocatedDevice.Device, candidate.NameEvidence));
                }

                var subnetIndex = 0;
                foreach (Subnet subnet in project.Subnets)
                {
                    var structuralLocator = $"subnets/{subnetIndex}";
                    var subnetId = HardwareConfigReader.ReadExactStringIdentityAttribute(
                        (IEngineeringObject)subnet,
                        "SubnetId",
                        "Subnet identity");
                    descriptors.Add(new HardwarePageDescriptor(
                        HardwarePageDescriptorKind.Subnet,
                        subnetId.IsUsable ? subnetId.Value : string.Empty,
                        structuralLocator,
                        subnetIndex));
                    subnetsByLocator.Add(structuralLocator, (subnet, subnetId));
                    subnetIndex++;
                }

                return new HardwarePageCandidateInventory(
                    new HardwarePageDescriptorSet(descriptors),
                    pageMessages);
            },
            materialize: descriptor =>
            {
                if (!enumerated)
                {
                    throw new WorkerOperationException(
                        WorkerFailureCategories.ProtocolError,
                        "The internal hardware candidate source was materialized before enumeration.");
                }

                if (descriptor.Kind == HardwarePageDescriptorKind.Device
                    && devicesByLocator.TryGetValue(descriptor.StructuralLocator, out var device))
                {
                    return HardwareConfigReader.ReadDevicePageCandidate(
                        device.Device,
                        device.NameEvidence,
                        includeIoDetails,
                        tagIndex);
                }

                if (descriptor.Kind == HardwarePageDescriptorKind.Subnet
                    && subnetsByLocator.TryGetValue(descriptor.StructuralLocator, out var subnet))
                {
                    return HardwareConfigReader.ReadSubnetPageCandidate(subnet.Subnet, subnet.SubnetId);
                }

                throw new WorkerOperationException(
                    WorkerFailureCategories.ProtocolError,
                    "A selected hardware descriptor could not be resolved by its internal locator.");
            });
    }
}
