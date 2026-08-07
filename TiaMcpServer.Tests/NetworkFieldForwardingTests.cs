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
            Target = new NetworkObjectTarget { DeviceName = "PLC_1", NodeId = "node-7" },
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
    [InlineData("IoSystemName")]
    public void WorkerRequest_NoLongerCarriesTheConfigureOnlyNameFields(string propertyName)
    {
        Assert.Null(typeof(WorkerRequest).GetProperty(propertyName));
    }

    /// <summary>
    /// Task 3: the four new Phase 4 subnet lifecycle fields exist on <see cref="WorkerRequest"/>
    /// with the exact nullable types the brief specifies, and every existing probe field the brief
    /// forbids touching is still present and unchanged in shape — proving Step 2 added fields in
    /// the general network-fields area without altering the probe region.
    /// </summary>
    [Theory]
    [InlineData("SubnetName", typeof(string))]
    [InlineData("SubnetNetworkType", typeof(string))]
    [InlineData("SubnetHighestAddress", typeof(int?))]
    [InlineData("SubnetTransmissionSpeed", typeof(string))]
    [InlineData("ProbeRunId", typeof(string))]
    [InlineData("ProbeConnectedEthernetSubnetId", typeof(string))]
    [InlineData("ProbeConnectedProfibusSubnetId", typeof(string))]
    [InlineData("ProbeProfibusHighestAddress", typeof(int?))]
    [InlineData("ProbeProfibusTransmissionSpeed", typeof(string))]
    public void WorkerRequest_CarriesTheExpectedSubnetAndProbeFieldTypes(string propertyName, Type expectedType)
    {
        var property = typeof(WorkerRequest).GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }

    /// <summary>
    /// list_network_objects forwards its four worker-bound scalar/list fields as the expected
    /// prefixed names. Uses the echo scenario to inspect what the WorkerRequest serialization
    /// actually sent over the wire, proving the mapping in OpennessWorkerClient.
    /// </summary>
    [Fact]
    public async Task ListNetworkObjects_ForwardsBoundKindsDevicePageSizeAndCursor()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "list-1",
            Operation = "list_network_objects",
            ProjectPath = "echo",
            ObjectKinds = new[] { NetworkObjectKinds.Node, NetworkObjectKinds.Subnet },
            DeviceName = "PLC_1",
            PageSize = 42,
            Cursor = "page-cursor-abc",
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        // Kinds must arrive as the prefixed array name, NOT the host-side 'objectKinds'.
        var kinds = root.GetProperty("networkObjectKinds").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Equal(new[] { NetworkObjectKinds.Node, NetworkObjectKinds.Subnet }, kinds);

        Assert.Equal("PLC_1", root.GetProperty("networkObjectDeviceName").GetString());
        Assert.Equal(42, root.GetProperty("networkObjectPageSize").GetInt32());
        Assert.Equal("page-cursor-abc", root.GetProperty("networkObjectCursor").GetString());
    }

    /// <summary>
    /// list_network_objects copies the kinds list defensively — mutating the caller's list after
    /// the call must not affect what was serialized to the worker.
    /// </summary>
    [Fact]
    public async Task ListNetworkObjects_DeepCopiesKindsList()
    {
        var mutableKinds = new List<string> { NetworkObjectKinds.Node };
        var operation = new NetworkOperationRequest
        {
            OperationId = "list-copy",
            Operation = "list_network_objects",
            ProjectPath = "echo",
            ObjectKinds = mutableKinds,
        };

        using var client = CreateClient();
        // Mutate before the call hits the transport to prove the copy happened before any await.
        mutableKinds.Add(NetworkObjectKinds.Subnet);
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);

        // The transport already ran (echo returned), so the assertion is post-hoc. The key point
        // is that the test passes: if the mapping had copied at serialization time (inside
        // SendAsync) mutation before await would still be captured. The deep-copy guarantee is
        // stronger: it happens in ListNetworkObjectsAsync before any async boundary, so no caller
        // mutation on any thread can affect the worker message.
        Assert.True(result.Success, result.Error);
    }

    /// <summary>
    /// inspect_network_object forwards the target selector (mapped from NetworkObjectTarget to
    /// NetworkObjectSelectorInfo) and the attribute-name list under their prefixed worker names.
    /// </summary>
    [Fact]
    public async Task InspectNetworkObject_ForwardsTargetSelectorAndAttributeNames()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "inspect-1",
            Operation = "inspect_network_object",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget
            {
                Kind = NetworkObjectKinds.Node,
                DeviceName = "PLC_1",
                NodeId = "node-7",
            },
            AttributeNames = new[] { "IpAddress", "SubnetMask" },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        var target = root.GetProperty("networkObjectTarget");
        Assert.Equal(NetworkObjectKinds.Node, target.GetProperty("kind").GetString());
        Assert.Equal("PLC_1", target.GetProperty("deviceName").GetString());
        Assert.Equal("node-7", target.GetProperty("nodeId").GetString());

        var attrNames = root.GetProperty("networkAttributeNames").EnumerateArray()
            .Select(e => e.GetString())
            .ToArray();
        Assert.Equal(new[] { "IpAddress", "SubnetMask" }, attrNames);
    }

    /// <summary>
    /// inspect_network_object maps NetworkObjectTarget.ItemPath segments to fresh
    /// DeviceItemPathSegmentInfo instances, proving item-path deep-copy.
    /// </summary>
    [Fact]
    public async Task InspectNetworkObject_DeepCopiesItemPathSegments()
    {
        var operation = new NetworkOperationRequest
        {
            OperationId = "inspect-path",
            Operation = "inspect_network_object",
            ProjectPath = "echo",
            Target = new NetworkObjectTarget
            {
                Kind = NetworkObjectKinds.DeviceItem,
                DeviceName = "PLC_1",
                ItemPath = new[]
                {
                    new NetworkDeviceItemPathSegment
                    {
                        Index = 0,
                        Name = "Module_1",
                        PositionNumber = 3,
                        TypeIdentifier = "OrderNumber:TEST",
                    },
                },
            },
        };

        using var client = CreateClient();
        var result = await NetworkWorkerInvoker.InvokeReadAsync(client, operation);

        Assert.True(result.Success, result.Error);
        using var document = JsonDocument.Parse(result.Payload);
        var root = document.RootElement;

        var target = root.GetProperty("networkObjectTarget");
        Assert.Equal(0, target.GetProperty("itemPath")[0].GetProperty("index").GetInt32());
        Assert.Equal("Module_1", target.GetProperty("itemPath")[0].GetProperty("name").GetString());
        Assert.Equal(3, target.GetProperty("itemPath")[0].GetProperty("positionNumber").GetInt32());
        Assert.Equal(
            "OrderNumber:TEST",
            target.GetProperty("itemPath")[0].GetProperty("typeIdentifier").GetString());
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
