using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwareDeviceSelectionTests
{
    private static string FindRepositoryFile(params string[] pathSegments)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Path.GetDirectoryName(current);
        }

        throw new FileNotFoundException(
            $"Could not find repository file '{Path.Combine(pathSegments)}' from '{AppContext.BaseDirectory}'.");
    }

    private static string ReadRepositorySource(params string[] pathSegments)
    {
        return File.ReadAllText(FindRepositoryFile(pathSegments)).Replace("\r\n", "\n");
    }

    [Fact]
    public void HardwareConfigReader_SelectDevices_ReadsNameOnceIntoEvidence()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareConfigReader.cs");

        // Name evidence is read once in SelectDevices using ReadTypedIdentityString
        Assert.Contains("var nameEvidence = ReadTypedIdentityString(() => device.Name, \"Device name\");", source, StringComparison.Ordinal);
        Assert.Contains("candidates.Add((device, nameEvidence));", source, StringComparison.Ordinal);

        // ReadDevice accepts the evidence directly instead of re-reading device.Name
        Assert.Contains("ReadDevice(\n        Device device,\n        NetworkObjectDiscoveryEvidenceValue<string> deviceName,", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDevice(\n        Device device,\n        string deviceDescription,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareConfigReader_SelectDevices_UnfilteredReadTraversesAllDevicesEvenWhenNameIsUnreadable()
    {
        var candidates = new[]
        {
            NetworkObjectDiscoveryEvidence.ReadString("Device_A", "Device name"),
            NetworkObjectDiscoveryEvidence.UnreadableString("Device name"),
        };
        var selected = NetworkOpennessRead.FilterByDeviceName(candidates, null, candidate => candidate, out var failure);
        Assert.Same(candidates, selected);
        Assert.Null(failure);

        // ReadDevice preserves unreadable device with Name = null and emits degradation message
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareConfigReader.cs");
        Assert.Contains("Name = deviceName.IsUsable ? deviceName.Value : null,", source, StringComparison.Ordinal);
        Assert.Contains("NetworkObjectDiscoveryEvidence.AddReadMessage(messages, deviceName);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareConfigReader_SelectDevices_FilteredReadMatchesOrdinalIgnoreCaseOnUsableNames()
    {
        var candidates = new[]
        {
            NetworkObjectDiscoveryEvidence.UnreadableString("Device name"),
            NetworkObjectDiscoveryEvidence.ReadString("Device_A", "Device name"),
            NetworkObjectDiscoveryEvidence.ReadString("Device_B", "Device name"),
        };

        var selected = NetworkOpennessRead.FilterByDeviceName(candidates, "device_a", candidate => candidate, out var failure);

        Assert.Same(candidates[1], Assert.Single(selected));
        Assert.Null(failure);
    }

    [Theory]
    [InlineData("missing", "No device named 'missing' was found; no devices are reported.")]
    [InlineData("Device_A", "More than one device matches 'Device_A'; no devices are reported because the device filter is ambiguous.")]
    public void DeviceFilter_MissingOrAmbiguousNameReportsNoDevices(string name, string expectedFailure)
    {
        var candidates = new[]
        {
            NetworkObjectDiscoveryEvidence.ReadString("Device_A", "Device name"),
            NetworkObjectDiscoveryEvidence.ReadString("device_a", "Device name"),
            NetworkObjectDiscoveryEvidence.UnreadableString("Device name"),
        };

        var selected = NetworkOpennessRead.FilterByDeviceName(candidates, name, candidate => candidate, out var failure);

        Assert.Empty(selected);
        Assert.Equal(expectedFailure, failure);
    }

    [Fact]
    public void HardwareConfigReader_DelegatesIoMapAndTagIndexToFocusedComponents()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareConfigReader.cs");

        Assert.Contains("HardwareIoMapReader.Read(item, itemDescription, messages, tagIndex)", source, StringComparison.Ordinal);
        Assert.Contains("return HardwareTagIndexResolver.Resolve(project, plcName, messages);", source, StringComparison.Ordinal);

        // HardwareConfigReader does not contain oversized internal TagIndex or ReadIoDetails
        Assert.DoesNotContain("private static DeviceItemIoDetailsInfo ReadIoDetails", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class TagIndex", source, StringComparison.Ordinal);

    }

    [Fact]
    public void HardwareIoMapReader_And_HardwareTagIndexResolver_RemainFocusedUnderSizeThresholds()
    {
        var ioMapSource = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareIoMapReader.cs");
        var tagIndexSource = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareTagIndexResolver.cs");

        var ioMapLines = ioMapSource.Split('\n').Length;
        var tagIndexLines = tagIndexSource.Split('\n').Length;

        Assert.InRange(ioMapLines, 1, 800);
        Assert.InRange(tagIndexLines, 1, 800);
    }

    [Fact]
    public void HardwareConfigReader_TagIndexFailureIsNonFatalOptionalEnrichment()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareConfigReader.cs");

        Assert.Contains("private static IoTagIndex? ResolveTagIndex(", source, StringComparison.Ordinal);
        Assert.Contains("catch (EngineeringException exception)", source, StringComparison.Ordinal);
        Assert.Contains("no tag matches are reported", source, StringComparison.Ordinal);
        Assert.Contains("tagIndex = ResolveTagIndex(project, plcName, result.Messages);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareIoMapReader_EnumerationFailuresStayInsideOptionalIoDetails()
    {
        var source = ReadRepositorySource(
            "TiaMcpServer.OpennessWorker", "Openness", "Network", "HardwareIoMapReader.cs");

        Assert.Contains("try\n        {\n            foreach (Address address in item.Addresses)", source, StringComparison.Ordinal);
        Assert.Contains("Could not enumerate addresses while reading device item", source, StringComparison.Ordinal);
        Assert.Contains("try\n        {\n            foreach (Channel channel in item.Channels)", source, StringComparison.Ordinal);
        Assert.Contains("Could not enumerate channels while reading device item", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultRead_ProducesByteIdenticalJsonToLegacyPayloadWithoutIoDetails()
    {
        var config = new HardwareConfigInfo
        {
            Devices = new List<DeviceInfo>
            {
                new()
                {
                    Name = "PLC_1",
                    TypeIdentifier = "OrderNumber:CPU",
                    Items = new List<DeviceItemInfo>
                    {
                        new()
                        {
                            Name = "CPU",
                            TypeIdentifier = "OrderNumber:CPU",
                            PositionNumber = 1,
                            Selectable = false,
                            SelectorDiagnostics = new List<string> { "unavailable" },
                            IoDetails = null, // null when includeIoDetails was not requested
                        },
                    },
                },
            },
        };

        var json = CanonicalJson.Serialize(config);

        Assert.DoesNotContain("ioDetails", json, StringComparison.Ordinal);
    }

    [Fact]
    public void DegradedRead_PreservesDeviceWithNullNameAndDegradationMessage()
    {
        var config = new HardwareConfigInfo
        {
            Devices = new List<DeviceInfo>
            {
                new()
                {
                    Name = null, // unreadable name preserved as null
                    TypeIdentifier = "OrderNumber:UNKNOWN",
                    Items = new List<DeviceItemInfo>(),
                },
            },
            Messages = new List<string>
            {
                "Could not read device name: Device name was null; selector not available.",
            },
        };

        var json = CanonicalJson.Serialize(config);

        Assert.Contains("\"name\":null", json, StringComparison.Ordinal);
        Assert.Contains("Could not read device name", json, StringComparison.Ordinal);
    }
}
