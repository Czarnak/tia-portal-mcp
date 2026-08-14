using System.Text.Json;
using System.Text.Json.Serialization;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Contract tests for the structured I/O map DTOs
/// (<see cref="DeviceItemIoDetailsInfo"/>, <see cref="IoAddressInfo"/>,
/// <see cref="IoChannelInfo"/>, <see cref="IoTagMatchInfo"/>): non-null collections, null
/// unreadable scalars, JSON round trips, and the <see cref="JsonIgnoreCondition.WhenWritingNull"/>
/// guarantee that a default read serializes byte-identically to the pre-I/O-map shape.
/// </summary>
public class DeviceItemIoDetailsContractTests
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void IoDetails_IsNullByDefaultOnADeviceItem()
    {
        Assert.Null(new DeviceItemInfo().IoDetails);
    }

    [Fact]
    public void IoDetails_CollectionsAreNeverNullWhenTheContainerIsPresent()
    {
        var details = new DeviceItemIoDetailsInfo();

        Assert.NotNull(details.Addresses);
        Assert.NotNull(details.Channels);
        Assert.Empty(details.Addresses);
        Assert.Empty(details.Channels);

        Assert.NotNull(new IoAddressInfo().ControllerNames);
        Assert.Empty(new IoAddressInfo().ControllerNames);
        Assert.NotNull(new IoChannelInfo().TagMatches);
        Assert.Empty(new IoChannelInfo().TagMatches);
    }

    [Fact]
    public void UnreadScalars_StayNull_NotZeroOrEmptyDefaults()
    {
        var address = new IoAddressInfo();
        var channel = new IoChannelInfo();

        Assert.Null(address.IoType);
        Assert.Null(address.StartAddress);
        Assert.Null(address.Length);
        Assert.Null(address.Context);
        Assert.Null(channel.Number);
        Assert.Null(channel.IoType);
        Assert.Null(channel.Type);
        Assert.Null(channel.ChannelAddressBits);
        Assert.Null(channel.ChannelWidthBits);
        Assert.Null(channel.LogicalAddress);
    }

    [Fact]
    public void IoTagMatchInfo_DeclaresNonNullStringMembers()
    {
        var match = new IoTagMatchInfo();

        Assert.Equal(string.Empty, match.Name);
        Assert.Equal(string.Empty, match.DataType);
        Assert.Equal(string.Empty, match.LogicalAddress);
        Assert.Equal(string.Empty, match.TableName);
        Assert.Equal(string.Empty, match.FolderPath);
    }

    [Fact]
    public void DeviceItemInfo_Address_IsUntouchedAndRemainsNullable()
    {
        var item = new DeviceItemInfo { Address = null };
        Assert.Null(item.Address);

        var withAddress = new DeviceItemInfo { Address = "0..1" };
        Assert.Equal("0..1", withAddress.Address);

        var json = JsonSerializer.Serialize(withAddress, WebOptions);
        Assert.Contains("\"address\":\"0..1\"", json);
    }

    [Fact]
    public void DefaultRead_SerializesWithoutTheIoDetailsMember()
    {
        var item = new DeviceItemInfo
        {
            Name = "DI_16",
            TypeIdentifier = "OrderNumber:TEST",
            PositionNumber = 1,
            Address = "0..1",
        };

        var json = JsonSerializer.Serialize(item, WebOptions);
        var deserialized = JsonSerializer.Deserialize<DeviceItemInfo>(json, WebOptions)!;

        Assert.DoesNotContain("ioDetails", json);
        Assert.Null(deserialized.IoDetails);
        Assert.Equal("0..1", deserialized.Address);
    }

    [Fact]
    public void IoDetails_Null_IsOmittedByTheCanonicalSerializerToo()
    {
        // The canonical serializer uses DefaultIgnoreCondition.Never, so the per-property
        // JsonIgnore(WhenWritingNull) is what keeps a default read's canonical document (and
        // therefore a safety-token state hash) byte-identical to the legacy shape.
        var config = new HardwareConfigInfo
        {
            Devices =
            {
                new DeviceInfo
                {
                    Name = "PLC_1",
                    Items = { new DeviceItemInfo { Name = "DI_16" } },
                },
            },
        };

        var canonical = CanonicalJson.Serialize(config);

        Assert.DoesNotContain("ioDetails", canonical);
        Assert.Contains("\"name\":\"DI_16\"", canonical);
    }

    [Fact]
    public void IoDetails_Populated_RoundTripsEveryMember()
    {
        var item = new DeviceItemInfo
        {
            Name = "AI_8",
            TypeIdentifier = "OrderNumber:TEST",
            Address = "64..71",
            IoDetails = new DeviceItemIoDetailsInfo
            {
                Addresses =
                {
                    new IoAddressInfo
                    {
                        IoType = "Input",
                        StartAddress = 64,
                        Length = 8,
                        Context = "Device",
                        ControllerNames = { "PLC_1" },
                    },
                },
                Channels =
                {
                    new IoChannelInfo
                    {
                        Number = 0,
                        IoType = "Input",
                        Type = "Analog",
                        ChannelAddressBits = 512,
                        ChannelWidthBits = 16,
                        LogicalAddress = "%IW64",
                        TagMatches =
                        {
                            new IoTagMatchInfo
                            {
                                Name = "AnalogIn",
                                DataType = "Int",
                                LogicalAddress = "%IW64",
                                TableName = "Tag table_1",
                                FolderPath = "/",
                            },
                        },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(item, WebOptions);
        var roundTripped = JsonSerializer.Deserialize<DeviceItemInfo>(json, WebOptions)!;

        Assert.Equal("64..71", roundTripped.Address); // untouched legacy nullable string survives
        Assert.NotNull(roundTripped.IoDetails);
        var address = Assert.Single(roundTripped.IoDetails!.Addresses);
        Assert.Equal("Input", address.IoType);
        Assert.Equal(64, address.StartAddress);
        Assert.Equal(8, address.Length);
        Assert.Equal("Device", address.Context);
        Assert.Equal(new[] { "PLC_1" }, address.ControllerNames);

        var channel = Assert.Single(roundTripped.IoDetails.Channels);
        Assert.Equal(0, channel.Number);
        Assert.Equal("Input", channel.IoType);
        Assert.Equal("Analog", channel.Type);
        Assert.Equal(512, channel.ChannelAddressBits);
        Assert.Equal(16u, channel.ChannelWidthBits);
        Assert.Equal("%IW64", channel.LogicalAddress);

        var match = Assert.Single(channel.TagMatches);
        Assert.Equal("AnalogIn", match.Name);
        Assert.Equal("Int", match.DataType);
        Assert.Equal("%IW64", match.LogicalAddress);
        Assert.Equal("Tag table_1", match.TableName);
        Assert.Equal("/", match.FolderPath);
    }

    [Fact]
    public void IoDetails_UsesDeclaredCamelCaseJsonNames()
    {
        var item = new DeviceItemInfo
        {
            IoDetails = new DeviceItemIoDetailsInfo
            {
                Addresses =
                {
                    new IoAddressInfo { IoType = "Input", StartAddress = 4, Length = 2 },
                },
                Channels =
                {
                    new IoChannelInfo
                    {
                        ChannelAddressBits = 32,
                        ChannelWidthBits = 1,
                        LogicalAddress = "%I4.0",
                        TagMatches = { new IoTagMatchInfo { Name = "StartButton" } },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(item, WebOptions);

        Assert.Contains("\"ioDetails\"", json);
        Assert.Contains("\"startAddress\":4", json);
        Assert.Contains("\"channelAddressBits\":32", json);
        Assert.Contains("\"channelWidthBits\":1", json);
        Assert.Contains("\"logicalAddress\":\"%I4.0\"", json);
        Assert.Contains("\"tagMatches\"", json);
        Assert.Contains("\"controllerNames\"", json);
    }

    [Fact]
    public void RawChannelEvidence_PreservedWhenNoLogicalAddressCanBeFormatted()
    {
        // A complex channel has no I/Q area: no formatted address, but the raw bit evidence
        // survives the round trip untouched.
        var channel = new IoChannelInfo
        {
            Number = 2,
            IoType = "Complex",
            Type = "Technology",
            ChannelAddressBits = 96,
            ChannelWidthBits = 32,
            LogicalAddress = null,
        };

        var json = JsonSerializer.Serialize(channel, WebOptions);
        var roundTripped = JsonSerializer.Deserialize<IoChannelInfo>(json, WebOptions)!;

        Assert.Null(roundTripped.LogicalAddress);
        Assert.Equal(96, roundTripped.ChannelAddressBits);
        Assert.Equal(32u, roundTripped.ChannelWidthBits);
    }
}
