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

    public static TheoryData<string> AllOperations()
    {
        var data = new TheoryData<string>();
        foreach (var spec in NetworkOperationCatalog.All)
        {
            data.Add(spec.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllOperations))]
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
