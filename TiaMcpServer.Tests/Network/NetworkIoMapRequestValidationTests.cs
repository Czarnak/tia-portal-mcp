using System.Text.Json;
using TiaMcpServer.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Request-level validation for the I/O-map options of <c>read_hardware_config</c>
/// (<c>deviceName</c>, <c>plcName</c>, <c>includeIoDetails</c>, <c>includeTagMatches</c>):
/// acceptance rules, the tag-matching dependency, blank-string rejection, inapplicable-field
/// rejection, and the preserved strict unknown-field gate.
/// </summary>
public class NetworkIoMapRequestValidationTests
{
    private static NetworkOperationRequest ReadHardware(
        string id = "read",
        Action<NetworkOperationRequest>? configure = null)
    {
        var request = new NetworkOperationRequest { OperationId = id, Operation = "read_hardware_config" };
        configure?.Invoke(request);
        return request;
    }

    [Fact]
    public void Accepts_IncludeIoDetailsTrue()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ReadHardware(configure: operation => operation.IncludeIoDetails = true),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void Accepts_IncludeTagMatchesWithIncludeIoDetails()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ReadHardware(configure: operation =>
            {
                operation.IncludeIoDetails = true;
                operation.IncludeTagMatches = true;
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Accepts_EveryIncludeFlagCombinationExceptTagMatchesWithoutIoDetails(bool includeIoDetails, bool includeTagMatches)
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ReadHardware(configure: operation =>
            {
                operation.IncludeIoDetails = includeIoDetails;
                operation.IncludeTagMatches = includeTagMatches;
            }),
        });

        if (includeTagMatches && !includeIoDetails)
        {
            Assert.False(result.IsValid);
            Assert.Contains("includeTagMatches", result.Error);
            Assert.Contains("includeIoDetails", result.Error);
        }
        else
        {
            Assert.True(result.IsValid, result.Error);
        }
    }

    [Fact]
    public void Rejects_IncludeTagMatchesWithoutIncludeIoDetails()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ReadHardware(configure: operation => operation.IncludeTagMatches = true),
        });

        Assert.False(result.IsValid);
        Assert.Contains("requires 'includeIoDetails'", result.Error);
    }

    [Fact]
    public void Accepts_DeviceNameAndPlcName()
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ReadHardware(configure: operation =>
            {
                operation.DeviceName = "ET 200SP station_1";
                operation.PlcName = "PLC_1";
            }),
        });

        Assert.True(result.IsValid, result.Error);
    }

    [Theory]
    [InlineData("deviceName", "   ")]
    [InlineData("plcName", "   ")]
    [InlineData("deviceName", "")]
    [InlineData("plcName", "")]
    public void Rejects_BlankDeviceNameAndPlcName(string field, string value)
    {
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            ReadHardware(configure: operation =>
            {
                if (field == "deviceName")
                {
                    operation.DeviceName = value;
                }
                else
                {
                    operation.PlcName = value;
                }
            }),
        });

        Assert.False(result.IsValid);
        Assert.Contains(field, result.Error);
        Assert.Contains("must not be blank", result.Error);
    }

    [Fact]
    public void Rejects_DeviceNameAndPlcNameStillRequireTheReadToBeValid()
    {
        // Fields are optional, so a bare read_hardware_config with none of them stays valid.
        Assert.True(NetworkOperationCatalog.ValidateRead(new[] { ReadHardware() }).IsValid);
    }

    [Theory]
    [InlineData("includeIoDetails")]
    [InlineData("includeTagMatches")]
    [InlineData("plcName")]
    public void Rejects_IoMapFieldsOnOperationsWhereInapplicable(string field)
    {
        var request = new NetworkOperationRequest
        {
            OperationId = "search",
            Operation = "search_equipment_catalog",
            Query = "CPU",
        };
        var property = typeof(NetworkOperationRequest).GetProperty(
            char.ToUpperInvariant(field[0]) + field.Substring(1));
        if (property!.PropertyType == typeof(bool?))
        {
            property.SetValue(request, true);
        }
        else
        {
            property.SetValue(request, "PLC_1");
        }

        var result = NetworkOperationCatalog.ValidateRead(new[] { request });

        Assert.False(result.IsValid);
        Assert.Contains($"'{field}' is not valid", result.Error);
    }

    [Fact]
    public void Rejects_DeviceNameOnSearchCatalogStillUsesTheSharedDeviceField()
    {
        // deviceName is shared with add_network_device/list_network_objects, so it is declared on
        // the request; it must still be rejected on search_equipment_catalog.
        var result = NetworkOperationCatalog.ValidateRead(new[]
        {
            new NetworkOperationRequest
            {
                OperationId = "search",
                Operation = "search_equipment_catalog",
                Query = "CPU",
                DeviceName = "PLC_1",
            },
        });

        Assert.False(result.IsValid);
        Assert.Contains("'deviceName' is not valid", result.Error);
    }

    [Fact]
    public void StrictUnknownFieldRejection_IsPreservedOnReadHardwareConfig()
    {
        var json = """
            {"operationId":"read","operation":"read_hardware_config","unknownField":true}
            """;

        var exception = Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<NetworkOperationRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        Assert.Contains("unknownField", exception.Message);
    }
}
