using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

/// <summary>
/// Proves that canonical serialization of an enriched hardware-config result is stable:
/// two calls with the same object graph must produce byte-identical output.
///
/// This guards the safety-token binding contract: a network write token is bound to the
/// canonical JSON of the current hardware state, and a second serialisation of the same graph
/// must always reproduce the same binding key.
/// </summary>
public class NetworkPhase3SafetySnapshotTests
{
    /// <summary>
    /// Serializes a fully-populated, selector-enriched <see cref="HardwareConfigInfo"/> graph twice
    /// and verifies that both serializations hash to exactly the same value. Any non-determinism
    /// in the serialized form (property ordering, floating-point rendering, list ordering) would
    /// break the safety-token assumption.
    /// </summary>
    [Fact]
    public void EnrichedHardwareConfig_SerializesIdentically_WhenCalledTwice()
    {
        var config = BuildEnrichedConfig();

        var json1 = CanonicalJson.Serialize(config);
        var json2 = CanonicalJson.Serialize(config);

        Assert.Equal(json1, json2);

        var hash1 = Sha256Hex(json1);
        var hash2 = Sha256Hex(json2);

        Assert.Equal(hash1, hash2);
    }

    /// <summary>
    /// A selector-unselectable object (Selectable = false, Selector = null, non-empty Diagnostics)
    /// must also serialize deterministically so degraded results remain stable.
    /// </summary>
    [Fact]
    public void UnselectableHardwareItems_SerializeIdentically_WhenCalledTwice()
    {
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "BadDevice",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = null,
                            Selectable = false,
                            Selector = null,
                            SelectorDiagnostics = { "Device name could not be read; selector not available." },
                        }
                    }
                }
            }
        };

        var json1 = CanonicalJson.Serialize(config);
        var json2 = CanonicalJson.Serialize(config);

        Assert.Equal(json1, json2);
    }

    /// <summary>
    /// Verifies that the new selector fields survive a JSON round-trip (serialize → deserialize →
    /// serialize again) without drift, so the safety token computed from the first render matches
    /// one computed after a second round-trip.
    /// </summary>
    [Fact]
    public void EnrichedHardwareConfig_RoundTripProducesStableHash()
    {
        var config = BuildEnrichedConfig();

        var json1 = CanonicalJson.Serialize(config);

        // Round-trip: strict deserialize then re-serialize.
        var deserialized = CanonicalJson.Deserialize<HardwareConfigInfo>(json1);
        var json2 = CanonicalJson.Serialize(deserialized);

        Assert.Equal(json1, json2);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HardwareConfigInfo BuildEnrichedConfig()
    {
        var itemPath = new List<DeviceItemPathSegmentInfo>
        {
            new() { Index = 0, Name = "PROFINET interface_1", PositionNumber = 0, TypeIdentifier = "OrderNumber:IF" },
        };

        return new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PLC_1",
                    TypeIdentifier = "OrderNumber:CPU",
                    Items =
                    {
                        new DeviceItemInfo
                        {
                            Name = "PROFINET interface_1",
                            TypeIdentifier = "OrderNumber:IF",
                            PositionNumber = 0,
                            Selectable = true,
                            Selector = NetworkSelectorFactory.DeviceItem("PLC_1", itemPath),
                            NetworkInterfaces =
                            {
                                new NetworkInterfaceInfo
                                {
                                    Name = "PROFINET interface_1",
                                    Selectable = true,
                                    Selector = NetworkSelectorFactory.NetworkInterface(
                                        "PLC_1", itemPath, "PROFINET interface_1", "PROFINET", null),
                                    Nodes =
                                    {
                                        new NodeInfo
                                        {
                                            Name = "X1",
                                            NodeId = "node-1",
                                            NodeType = "Ethernet",
                                            IpAddress = "192.168.0.10",
                                            Selectable = true,
                                            Selector = NetworkSelectorFactory.Node("PLC_1", "node-1"),
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            Subnets =
            {
                new SubnetInfo
                {
                    Name = "PN/IE_1",
                    SubnetId = "subnet-abc",
                    NetworkType = "Ethernet",
                    Selectable = true,
                    Selector = NetworkSelectorFactory.Subnet("subnet-abc"),
                    IoSystems =
                    {
                        new IoSystemInfo
                        {
                            Name = "IO system_1",
                            Number = 100,
                            Selectable = true,
                            Selector = NetworkSelectorFactory.IoSystem("subnet-abc", 100),
                        }
                    },
                    ConnectedNodeNames = { "PLC_1.X1" }
                }
            }
        };
    }

    private static string Sha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
