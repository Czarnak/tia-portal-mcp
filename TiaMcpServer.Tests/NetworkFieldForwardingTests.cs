using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Network;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class NetworkFieldForwardingTests
{
    private static OpennessWorkerClient CreateClient()
        => new(new ProjectSessionBinding(null), logger: null, workerExecutablePath: FakeWorkerLocator.Locate());

    /// <summary>
    /// Operations whose declared fields are all flat scalars, so one reflective sentinel sweep can
    /// prove each is forwarded. configure_network_device declares nested selectors instead and is
    /// covered explicitly by <see cref="ConfigureNetworkDevice_ForwardsEveryExactIdentityAndChange"/>.
    /// </summary>
    public static TheoryData<string> FlatFieldOperations()
    {
        var data = new TheoryData<string>();
        foreach (var spec in NetworkOperationCatalog.All)
        {
            var isFlat = spec.RequiredFields.Concat(spec.OptionalFields).All(field =>
            {
                var property = typeof(NetworkOperationRequest).GetProperty(ToPropertyName(field));
                var type = property is null ? null : Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                return type == typeof(string) || type == typeof(int);
            });

            if (isFlat)
            {
                data.Add(spec.Name);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FlatFieldOperations))]
    public async Task EveryDeclaredField_IsForwardedExactlyOnceWithItsSentinelValue(string operationName)
    {
        Assert.True(NetworkOperationCatalog.TryGetSpec(operationName, out var spec));
        var operation = new NetworkOperationRequest
        {
            OperationId = "item-1",
            Operation = operationName,
            ProjectPath = "echo",
        };
        var expected = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var fieldName in spec!.RequiredFields.Concat(spec.OptionalFields))
        {
            var property = typeof(NetworkOperationRequest).GetProperty(ToPropertyName(fieldName))
                ?? throw new InvalidOperationException($"Missing request property for '{fieldName}'.");
            var sentinel = SentinelFor(property, fieldName);
            property.SetValue(operation, sentinel);
            expected[fieldName] = sentinel;
        }

        using var client = CreateClient();
        var result = spec.Category == NetworkOperationCategory.Read
            ? await NetworkWorkerInvoker.InvokeReadAsync(client, operation)
            : await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        foreach (var (fieldName, sentinel) in expected)
        {
            var properties = document.RootElement.EnumerateObject()
                .Where(property => property.NameEquals(fieldName))
                .ToArray();
            var property = Assert.Single(properties);
            AssertSentinel(property.Value, sentinel);
        }
    }

    [Fact]
    public async Task AddNetworkDevice_OmittedDeviceItemNameForwardsDeviceName()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "add-1",
            Operation = "add_network_device",
            ProjectPath = "echo",
            TypeIdentifier = "OrderNumber:6ES7",
            DeviceName = "PLC_1",
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        Assert.Equal("PLC_1", document.RootElement.GetProperty("deviceItemName").GetString());
    }

    [Fact]
    public async Task ConfigureNetworkDevice_ForwardsEveryExactIdentityAndChange()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "cfg-1",
            Operation = "configure_network_device",
            ProjectPath = "echo",
            Target = new NetworkDeviceTarget { DeviceName = "PLC_1", NodeId = "node-7" },
            Changes = new NetworkDeviceChanges
            {
                IpAddress = "192.168.0.10",
                SubnetMask = "255.255.255.0",
                PnDeviceName = "plc-1",
                Subnet = new NetworkSubnetTarget { SubnetId = "subnet-a" },
                IoSystem = new NetworkIoSystemTarget { SubnetId = "subnet-a", Number = 100 },
            },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeWriteAsync(client, operation, commonProjectPath: "echo");

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;
        Assert.Equal("PLC_1", root.GetProperty("deviceName").GetString());
        Assert.Equal("node-7", root.GetProperty("nodeId").GetString());
        Assert.Equal("192.168.0.10", root.GetProperty("ipAddress").GetString());
        Assert.Equal("255.255.255.0", root.GetProperty("subnetMask").GetString());
        Assert.Equal("plc-1", root.GetProperty("pnDeviceName").GetString());
        Assert.Equal("subnet-a", root.GetProperty("subnetId").GetString());
        Assert.Equal("subnet-a", root.GetProperty("ioSystemSubnetId").GetString());
        Assert.Equal(100, root.GetProperty("ioSystemNumber").GetInt32());
    }

    [Theory]
    [InlineData("SubnetName")]
    [InlineData("IoSystemName")]
    public void WorkerRequest_NoLongerCarriesTheConfigureOnlyNameFields(string propertyName)
    {
        Assert.Null(typeof(WorkerRequest).GetProperty(propertyName));
    }

    private static string ToPropertyName(string fieldName)
        => char.ToUpperInvariant(fieldName[0]) + fieldName.Substring(1);

    private static object SentinelFor(PropertyInfo property, string fieldName)
        => (Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType) == typeof(int)
            ? 4242
            : $"__sentinel_{fieldName}__";

    private static void AssertSentinel(JsonElement actual, object expected)
    {
        if (expected is int intValue)
        {
            Assert.Equal(intValue, actual.GetInt32());
            return;
        }

        Assert.Equal(expected, actual.GetString());
    }
}
