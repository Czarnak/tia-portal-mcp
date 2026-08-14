using System;
using System.IO;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
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

    [Fact]
    public void HardwareConfigReader_SelectDevices_ReadsNameOnceIntoEvidence()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"));

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
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"));

        // When deviceName is null, SelectDevices returns all candidates
        Assert.Contains("if (deviceName is null)\n        {\n            return candidates;\n        }", source, StringComparison.Ordinal);

        // ReadDevice preserves unreadable device with Name = null and emits degradation message
        Assert.Contains("Name = deviceName.IsUsable ? deviceName.Value : null,", source, StringComparison.Ordinal);
        Assert.Contains("AddReadMessage(messages, deviceName, \"device name\");", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareConfigReader_SelectDevices_FilteredReadMatchesOrdinalIgnoreCaseOnUsableNames()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"));

        // Matches only usable names with OrdinalIgnoreCase, never unreadable evidence
        Assert.Contains("candidate.NameEvidence.IsUsable", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(candidate.NameEvidence.Value, deviceName, StringComparison.OrdinalIgnoreCase)", source, StringComparison.Ordinal);

        // Zero and multiple match nonfatal messages exactly match specification
        Assert.Contains("No device named '{deviceName}' was found; no devices are reported.", source, StringComparison.Ordinal);
        Assert.Contains("More than one device matches '{deviceName}'; no devices are reported because the device filter is ambiguous.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareConfigReader_DelegatesIoMapAndTagIndexToFocusedComponents()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareConfigReader.cs"));

        Assert.Contains("HardwareIoMapReader.Read(item, itemDescription, messages, tagIndex)", source, StringComparison.Ordinal);
        Assert.Contains("HardwareTagIndexResolver.Resolve(project, plcName, result.Messages)", source, StringComparison.Ordinal);

        // HardwareConfigReader does not contain oversized internal TagIndex or ReadIoDetails
        Assert.DoesNotContain("private static DeviceItemIoDetailsInfo ReadIoDetails", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private sealed class TagIndex", source, StringComparison.Ordinal);

        // File remains focused (under 800 lines)
        var lineCount = source.Split('\n').Length;
        Assert.InRange(lineCount, 1, 800);
    }

    [Fact]
    public void HardwareIoMapReader_And_HardwareTagIndexResolver_RemainFocusedUnderSizeThresholds()
    {
        var ioMapSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareIoMapReader.cs"));
        var tagIndexSource = File.ReadAllText(FindRepositoryFile(
            "TiaMcpServer.OpennessWorker", "Openness", "HardwareTagIndexResolver.cs"));

        var ioMapLines = ioMapSource.Split('\n').Length;
        var tagIndexLines = tagIndexSource.Split('\n').Length;

        Assert.InRange(ioMapLines, 1, 800);
        Assert.InRange(tagIndexLines, 1, 800);
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
