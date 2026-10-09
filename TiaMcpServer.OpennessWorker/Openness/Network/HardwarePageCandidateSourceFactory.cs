using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

using Project = Siemens.Engineering.Project;

/// <summary>
/// Builds the Siemens-backed source used by the pure hardware-page candidate coordinator.
/// Descriptor traversal stays complete and lightweight; deep public materialization remains
/// delegated to the existing hardware reader only for the selected descriptor window.
/// </summary>
internal static class HardwarePageCandidateSourceFactory
{
    public static HardwarePageCandidateSource Create(
        ProjectBase project,
        string? deviceName,
        string? plcName,
        bool includeIoDetails,
        bool includeTagMatches)
    {
        var devicesByLocator = new Dictionary<
            string,
            (Device Device, NetworkObjectDiscoveryEvidenceValue<string> NameEvidence, bool NameNamespaceVerified)>(StringComparer.Ordinal);
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

                // Keep the complete lightweight name set before filtering/windowing. It proves
                // only device-name uniqueness, never ordinary hardware traversal completeness.
                var allDeviceNames = deviceCandidates.Select(candidate => candidate.NameEvidence.IsUsable
                    ? candidate.NameEvidence.Value : null).ToList();

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
                        (candidate.LocatedDevice.Device, candidate.NameEvidence,
                            NetworkNodeReadSelectorBuilder.DeviceNameIsUnique(allDeviceNames, publicIdentity)));
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
                        tagIndex,
                        device.NameNamespaceVerified);
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
