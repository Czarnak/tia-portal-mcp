using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Rule coverage past the worker-payload reader. The reader rejects a payload that is missing any
/// member, so a typed-validator rule is exercised only by a payload that is complete in every other
/// respect. Each case starts from a complete base fixture (every base is pinned as accepted by
/// <see cref="Project_AcceptsEachRuleBase"/>), applies exactly one edit, and asserts that the
/// bounded protocol diagnostic names the validator that owns the rule.
/// </summary>
public partial class NetworkPayloadContractTests
{
    private const string CompleteSubnetLifecycleResult = """{"subnetId":"subnet-1","name":"Ethernet","networkDeviceCount":0,"networkDeviceCountUnchanged":true}""";

    private const string ItemPathSegment = """{"index":0,"name":"CPU","positionNumber":1,"typeIdentifier":"OrderNumber:CPU"}""";

    private const string Blank = "\" \"";
    private const string NullElement = "[null]";
    private const string EmptyArray = "[]";

    private static string Str(string value) => JsonSerializer.Serialize(value);

    public static TheoryData<string> RuleBases() => new()
    {
        "hardware",
        "hardware-selectable",
        "catalog",
        "subnet-lifecycle",
        "list",
        "list-selectable",
        "inspection",
        "target:deviceItem",
        "target:networkInterface",
        "target:node",
        "target:node-with-path",
        "target:subnet",
        "target:ioSystem",
        "target:communicationConnection",
    };

    /// <summary>
    /// A complete fixture carrying the leak canary in a free-text member. The hardware bases are
    /// read with <c>includeIoDetails: true</c>; a <c>target:</c> base is the inspection fixture with
    /// a valid selector of that kind as its target.
    /// </summary>
    private static string RuleBase(string name) => name switch
    {
        "hardware" => Edit(CompleteHardwareConfig, ("messages", $"[{Str(LeakToken)}]")),
        "hardware-selectable" => Edit(
            RuleBase("hardware"),
            ("devices[0].items[0].selectable", "true"),
            ("devices[0].items[0].selector", SelectorOfKind("deviceItem")),
            ("devices[0].items[0].selectorDiagnostics", EmptyArray)),
        "catalog" => Edit(CompleteCatalog, ("[0].description", Str(LeakToken))),
        "subnet-lifecycle" => Edit(CompleteSubnetLifecycleResult, ("name", Str(LeakToken))),
        "list" => Edit(CompleteObjectList, ("items[0].evidence.name", Str(LeakToken))),
        "list-selectable" => Edit(
            RuleBase("list"),
            ("items[0].selectable", "true"),
            ("items[0].selector", CompleteNodeSelector)),
        "inspection" => Edit(CompleteInspection, ("evidence.name", Str(LeakToken))),
        _ when name.StartsWith("target:", StringComparison.Ordinal)
            => Edit(RuleBase("inspection"), ("target", SelectorOfKind(name["target:".Length..]))),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static string OperationOf(string baseName) => baseName switch
    {
        "hardware" or "hardware-selectable" => "read_hardware_config",
        "catalog" => "search_equipment_catalog",
        "subnet-lifecycle" => "create_subnet",
        "list" or "list-selectable" => "list_network_objects",
        _ => "inspect_network_object",
    };

    /// <summary>A valid selector of <paramref name="kind"/>, derived from the complete node selector.</summary>
    private static string SelectorOfKind(string kind) => kind switch
    {
        "node" => CompleteNodeSelector,
        "node-with-path" => Edit(
            CompleteNodeSelector,
            ("itemPath", $"[{ItemPathSegment}]"),
            ("nodeIndex", "0")),
        "deviceItem" => Edit(
            CompleteNodeSelector,
            ("kind", Str("deviceItem")),
            ("nodeId", "null"),
            ("itemPath", $"[{ItemPathSegment}]")),
        "networkInterface" => Edit(
            CompleteNodeSelector,
            ("kind", Str("networkInterface")),
            ("nodeId", "null"),
            ("itemPath", $"[{ItemPathSegment}]")),
        "subnet" => Edit(
            CompleteNodeSelector,
            ("kind", Str("subnet")),
            ("deviceName", "null"),
            ("nodeId", "null"),
            ("subnetId", Str("subnet-1"))),
        "ioSystem" => Edit(
            CompleteNodeSelector,
            ("kind", Str("ioSystem")),
            ("deviceName", "null"),
            ("nodeId", "null"),
            ("subnetId", Str("subnet-1")),
            ("number", "100")),
        "communicationConnection" => Edit(
            CompleteNodeSelector,
            ("kind", Str("communicationConnection")),
            ("nodeId", "null"),
            ("itemPath", $"[{ItemPathSegment}]"),
            ("connectionIndex", "0"),
            ("connectionType", Str("S7Connection")),
            ("localConnectionName", Str("S7_Connection_1")),
            ("localConnectionId", Str("16#1001"))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// Returns <paramref name="json"/> with each edit applied in order. An edit sets the member or
    /// array element at its path to the given JSON value, or removes the member when the value is
    /// null. The target must already exist, so a mistyped path cannot pass as an unmapped member.
    /// </summary>
    private static string Edit(string json, params (string Path, string? Value)[] edits)
    {
        var root = JsonNode.Parse(json)!;
        foreach (var (path, value) in edits)
        {
            var steps = Regex.Matches(path, @"\[\d+\]|[^.\[\]]+").Select(match => match.Value).ToArray();
            var parent = root;
            foreach (var step in steps[..^1])
            {
                parent = IsIndexStep(step) ? parent.AsArray()[StepIndex(step)]! : parent.AsObject()[step]!;
            }

            var last = steps[^1];
            if (IsIndexStep(last))
            {
                var array = parent.AsArray();
                Assert.True(StepIndex(last) < array.Count, $"'{path}' is not an element of the fixture.");
                Assert.NotNull(value);
                array[StepIndex(last)] = JsonNode.Parse(value!);
                continue;
            }

            var container = parent.AsObject();
            Assert.True(container.ContainsKey(last), $"'{path}' is not a member of the fixture.");
            if (value is null)
            {
                container.Remove(last);
            }
            else
            {
                container[last] = JsonNode.Parse(value);
            }
        }

        return root.ToJsonString();
    }

    private static bool IsIndexStep(string step) => step.StartsWith('[');

    private static int StepIndex(string step) => int.Parse(step[1..^1], CultureInfo.InvariantCulture);

    private static (StructuredOperationItem Item, List<string> Diagnostics) ProjectRuleCase(
        string operation,
        string payload,
        bool? includeIoDetails = null)
    {
        var diagnostics = new List<string>();
        var item = NetworkPayloadContract.Project(
            new NetworkOperationRequest
            {
                OperationId = "op-1",
                Operation = operation,
                IncludeIoDetails = includeIoDetails ?? (operation == "read_hardware_config" ? true : (bool?)null),
            },
            WorkerCallResult.Ok(payload),
            diagnostics.Add);
        return (item, diagnostics);
    }

    /// <summary>The validator chain the bounded protocol diagnostic reports, innermost first.</summary>
    private static string[] ValidatorChain(string diagnostic)
    {
        const string marker = "validators=";
        var start = diagnostic.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "The protocol diagnostic does not report a validator chain.");
        return diagnostic[(start + marker.Length)..].TrimEnd('.').Split('>');
    }

    private static string[] AssertRejectedWithoutLeak(
        string operation,
        string payload,
        bool? includeIoDetails = null)
    {
        var (item, diagnostics) = ProjectRuleCase(operation, payload, includeIoDetails);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain(LeakToken, item.Failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(LeakToken, CanonicalJson.Serialize(item), StringComparison.Ordinal);
        var diagnostic = Assert.Single(diagnostics);
        Assert.DoesNotContain(LeakToken, diagnostic, StringComparison.Ordinal);
        Assert.InRange(diagnostic.Length, 1, 512);
        return ValidatorChain(diagnostic);
    }

    [Theory]
    [MemberData(nameof(RuleBases))]
    public void Project_AcceptsEachRuleBase(string baseName)
    {
        var (item, diagnostics) = ProjectRuleCase(OperationOf(baseName), RuleBase(baseName));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// One case per rule the typed validators enforce: base fixture, edited path, the JSON value
    /// written there (null removes the member), and the validator that must report the rejection.
    /// </summary>
    public static TheoryData<string, string, string?, string> RuleViolations() => new()
    {
        // ValidateHardwareConfig: null elements of the top-level collections.
        { "hardware", "devices", NullElement, "ValidateHardwareConfig" },
        { "hardware", "subnets", NullElement, "ValidateHardwareConfig" },
        { "hardware", "subnets[0].ioSystems", NullElement, "ValidateHardwareConfig" },

        // ValidateDeviceItem: null elements, and ioDetails required when the read requested it.
        { "hardware", "devices[0].items", NullElement, "ValidateDeviceItem" },
        { "hardware", "devices[0].items[0].ioDetails", null, "ValidateDeviceItem" },
        { "hardware", "devices[0].items[0].communicationConnections", NullElement, "ValidateDeviceItem" },
        { "hardware", "devices[0].items[0].networkInterfaces", NullElement, "ValidateDeviceItem" },
        { "hardware", "devices[0].items[0].networkInterfaces[0].nodes", NullElement, "ValidateDeviceItem" },

        // ValidateIoDetails: null elements and negative numeric evidence.
        { "hardware", "devices[0].items[0].ioDetails.addresses", NullElement, "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.addresses[0].controllerNames", """["PLC_1",null]""", "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.addresses[0].startAddress", "-1", "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.addresses[0].length", "-1", "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.channels", NullElement, "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.channels[0].number", "-1", "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.channels[0].channelAddressBits", "-1", "ValidateIoDetails" },
        { "hardware", "devices[0].items[0].ioDetails.channels[0].tagMatches", NullElement, "ValidateIoDetails" },

        // ValidateHardwareSelector: diagnostics content and selectability agreement.
        { "hardware", "devices[0].items[0].selectorDiagnostics", $"[{Blank}]", "ValidateHardwareSelector" },
        { "hardware", "devices[0].items[0].selectorDiagnostics", NullElement, "ValidateHardwareSelector" },
        { "hardware", "devices[0].items[0].selectable", "true", "ValidateHardwareSelector" },
        { "hardware-selectable", "devices[0].items[0].selectorDiagnostics", $"[{Str("unexpected")}]", "ValidateHardwareSelector" },
        { "hardware", "devices[0].items[0].selectorDiagnostics", EmptyArray, "ValidateHardwareSelector" },

        // ValidateHardwareSelector: reached from each of its other five owners.
        { "hardware", "devices[0].items[0].communicationConnections[0].selectorDiagnostics", EmptyArray, "ValidateHardwareSelector" },
        { "hardware", "devices[0].items[0].networkInterfaces[0].selectorDiagnostics", EmptyArray, "ValidateHardwareSelector" },
        { "hardware", "devices[0].items[0].networkInterfaces[0].nodes[0].selectorDiagnostics", EmptyArray, "ValidateHardwareSelector" },
        { "hardware", "subnets[0].selectorDiagnostics", EmptyArray, "ValidateHardwareSelector" },
        { "hardware", "subnets[0].ioSystems[0].selectorDiagnostics", EmptyArray, "ValidateHardwareSelector" },

        // ValidateSelector from the hardware tree: the selector's kind must be the owner's kind.
        { "hardware-selectable", "devices[0].items[0].selector.kind", Str("networkInterface"), "ValidateSelector" },

        // ValidateCatalogEntries: null element of the array root.
        { "catalog", "[0]", "null", "ValidateCatalogEntries" },

        // ValidateSubnetLifecycleResult.
        { "subnet-lifecycle", "subnetId", Blank, "ValidateSubnetLifecycleResult" },
        { "subnet-lifecycle", "name", Blank, "ValidateSubnetLifecycleResult" },
        { "subnet-lifecycle", "networkDeviceCount", "-1", "ValidateSubnetLifecycleResult" },
        { "subnet-lifecycle", "networkDeviceCountUnchanged", "false", "ValidateSubnetLifecycleResult" },

        // ValidateObjectList: counts and null items.
        { "list", "totalCount", "-1", "ValidateObjectList" },
        { "list", "returnedCount", "-1", "ValidateObjectList" },
        { "list", "returnedCount", "0", "ValidateObjectList" },
        { "list", "totalCount", "0", "ValidateObjectList" },
        { "list", "items", NullElement, "ValidateObjectList" },

        // ValidateObjectSummary.
        { "list", "items[0].kind", Str(LeakToken), "ValidateObjectSummary" },
        { "list", "items[0].evidence.deviceItemPath", NullElement, "ValidateObjectSummary" },
        { "list", "items[0].diagnostics", NullElement, "ValidateObjectSummary" },
        { "list", "items[0].selectable", "true", "ValidateObjectSummary" },
        { "list", "items[0].diagnostics", $"[{Blank}]", "ValidateObjectSummary" },

        // ValidateSelector from a list summary: the selector's kind must be the summary's kind.
        { "list-selectable", "items[0].kind", Str("subnet"), "ValidateSelector" },

        // ValidateObjectInspection: null elements and duplicate attribute names.
        { "inspection", "evidence.deviceItemPath", NullElement, "ValidateObjectInspection" },
        { "inspection", "attributes", NullElement, "ValidateObjectInspection" },
        { "inspection", "attributes[1].name", Str("IpAddress"), "ValidateObjectInspection" },

        // ValidateAttribute: attributes[0] is available, attributes[2] is an unknown attribute.
        { "inspection", "attributes[0].supportedTypes", NullElement, "ValidateAttribute" },
        { "inspection", "attributes[0].access", Str(LeakToken), "ValidateAttribute" },
        { "inspection", "attributes[0].availability", Str(LeakToken), "ValidateAttribute" },
        { "inspection", "attributes[2].source", Str("modeled"), "ValidateAttribute" },
        { "inspection", "attributes[2].access", Str("readOnly"), "ValidateAttribute" },
        { "inspection", "attributes[2].supportedTypes", $"[{Str("string")}]", "ValidateAttribute" },
        { "inspection", "attributes[2].value", """{"kind":"null","value":null,"typeName":null}""", "ValidateAttribute" },
        { "inspection", "attributes[2].diagnostic", "null", "ValidateAttribute" },
        { "inspection", "attributes[2].diagnostic.category", Str("read_error"), "ValidateAttribute" },
        { "inspection", "attributes[0].source", "null", "ValidateAttribute" },
        { "inspection", "attributes[0].source", Str(LeakToken), "ValidateAttribute" },
        { "inspection", "attributes[0].value", "null", "ValidateAttribute" },
        { "inspection", "attributes[0].value.kind", Str(LeakToken), "ValidateAttribute" },
        { "inspection", "attributes[0].availability", Str("readFailed"), "ValidateAttribute" },

        // ValidateAttributeValue: the value must match its discriminator.
        { "inspection", "attributes[0].value.kind", Str("null"), "ValidateAttributeValue" },
        { "inspection", "attributes[0].value.value", "null", "ValidateAttributeValue" },
        { "inspection", "attributes[0].value.value", "5", "ValidateAttributeValue" },
        { "inspection", "attributes[0].value.kind", Str("boolean"), "ValidateAttributeValue" },
        { "inspection", "attributes[0].value.kind", Str("integer"), "ValidateAttributeValue" },
        { "inspection", "attributes[0].value", """{"kind":"integer","value":1.5,"typeName":null}""", "ValidateAttributeValue" },
        { "inspection", "attributes[0].value.kind", Str("number"), "ValidateAttributeValue" },
        { "inspection", "attributes[0].value.kind", Str("enum"), "ValidateAttributeValue" },

        // ValidateEnumValue: the nested enum value goes through the worker-payload reader, so each
        // member is required and typed even though the containing member is object-typed.
        { "inspection", "attributes[1].value.value.typeName", null, "ValidateEnumValue" },
        { "inspection", "attributes[1].value.value.symbol", null, "ValidateEnumValue" },
        { "inspection", "attributes[1].value.value.numericValue", null, "ValidateEnumValue" },
        { "inspection", "attributes[1].value.value.numericValue", Str("1"), "ValidateEnumValue" },

        // ValidateSelector: kind vocabulary.
        { "target:node", "target.kind", Str(LeakToken), "ValidateSelector" },
        { "target:node", "target.kind", "null", "ValidateSelector" },

        // ValidateSelector, deviceItem: required text, the item path and its segments, forbidden fields.
        { "target:deviceItem", "target.deviceName", "null", "ValidateSelector" },
        { "target:deviceItem", "target.itemPath", EmptyArray, "ValidateSelector" },
        { "target:deviceItem", "target.itemPath", "null", "ValidateSelector" },
        { "target:deviceItem", "target.itemPath[0]", "null", "ValidateSelector" },
        { "target:deviceItem", "target.itemPath[0].index", "-1", "ValidateSelector" },
        { "target:deviceItem", "target.itemPath[0].name", Blank, "ValidateSelector" },
        { "target:deviceItem", "target.itemPath[0].positionNumber", "-1", "ValidateSelector" },
        { "target:deviceItem", "target.itemPath[0].typeIdentifier", Blank, "ValidateSelector" },
        { "target:deviceItem", "target.nodeId", Str("n1"), "ValidateSelector" },
        { "target:deviceItem", "target.connectionIndex", "0", "ValidateSelector" },

        // ValidateSelector, networkInterface.
        { "target:networkInterface", "target.deviceName", "null", "ValidateSelector" },
        { "target:networkInterface", "target.itemPath", "null", "ValidateSelector" },
        { "target:networkInterface", "target.interfaceName", Blank, "ValidateSelector" },
        { "target:networkInterface", "target.interfaceType", Blank, "ValidateSelector" },
        { "target:networkInterface", "target.interfaceOperatingMode", Blank, "ValidateSelector" },
        { "target:networkInterface", "target.subnetId", Str("subnet-1"), "ValidateSelector" },

        // ValidateSelector, node.
        { "target:node", "target.deviceName", "null", "ValidateSelector" },
        { "target:node", "target.nodeId", Blank, "ValidateSelector" },
        { "target:node", "target.nodeIndex", "0", "ValidateSelector" },
        { "target:node-with-path", "target.itemPath", EmptyArray, "ValidateSelector" },
        { "target:node-with-path", "target.nodeIndex", "-1", "ValidateSelector" },
        { "target:node", "target.interfaceType", Str("PROFINET"), "ValidateSelector" },
        { "target:node", "target.connectionType", Str("S7Connection"), "ValidateSelector" },

        // ValidateSelector, subnet.
        { "target:subnet", "target.subnetId", Blank, "ValidateSelector" },
        { "target:subnet", "target.deviceName", Str("PLC_1"), "ValidateSelector" },
        { "target:subnet", "target.itemPath", $"[{ItemPathSegment}]", "ValidateSelector" },
        { "target:subnet", "target.localConnectionName", Str("S7_Connection_1"), "ValidateSelector" },

        // ValidateSelector, ioSystem.
        { "target:ioSystem", "target.subnetId", "null", "ValidateSelector" },
        { "target:ioSystem", "target.number", "null", "ValidateSelector" },
        { "target:ioSystem", "target.number", "-1", "ValidateSelector" },
        { "target:ioSystem", "target.ioSystemIndex", "-1", "ValidateSelector" },
        { "target:ioSystem", "target.ioSystemName", Blank, "ValidateSelector" },
        { "target:ioSystem", "target.interfaceOperatingMode", Str("IoController"), "ValidateSelector" },
        { "target:ioSystem", "target.nodeIndex", "0", "ValidateSelector" },
        { "target:ioSystem", "target.localConnectionId", Str("16#1001"), "ValidateSelector" },

        // ValidateSelector, communicationConnection.
        { "target:communicationConnection", "target.deviceName", "null", "ValidateSelector" },
        { "target:communicationConnection", "target.itemPath", EmptyArray, "ValidateSelector" },
        { "target:communicationConnection", "target.connectionIndex", "null", "ValidateSelector" },
        { "target:communicationConnection", "target.connectionIndex", "-1", "ValidateSelector" },
        { "target:communicationConnection", "target.connectionType", Blank, "ValidateSelector" },
        { "target:communicationConnection", "target.connectionType", Str(LeakToken), "ValidateSelector" },
        { "target:communicationConnection", "target.localConnectionName", Blank, "ValidateSelector" },
        { "target:communicationConnection", "target.connectionType", Str("HmiConnection"), "ValidateSelector" },
        { "target:communicationConnection", "target.localConnectionId", "null", "ValidateSelector" },
        { "target:communicationConnection", "target.interfaceName", Str("PROFINET interface_1"), "ValidateSelector" },
        { "target:communicationConnection", "target.number", "1", "ValidateSelector" },
        { "target:communicationConnection", "target.ioSystemIndex", "0", "ValidateSelector" },
        { "target:communicationConnection", "target.ioSystemName", Str("IO system_1"), "ValidateSelector" },
    };

    [Theory]
    [MemberData(nameof(RuleViolations))]
    public void Project_ReachesEachTypedRuleFromACompleteFixture(
        string baseName,
        string path,
        string? value,
        string validator)
    {
        var payload = Edit(RuleBase(baseName), (path, value));

        var chain = AssertRejectedWithoutLeak(OperationOf(baseName), payload);

        Assert.Contains(validator, chain);
    }

    /// <summary>
    /// The one request-dependent rule a complete read_hardware_config fixture cannot reach by
    /// editing the payload alone: I/O details present on a read that did not request them.
    /// </summary>
    [Fact]
    public void Project_ReachesTheUnrequestedIoDetailsRuleFromACompleteFixture()
    {
        var withoutIoDetails = Edit(RuleBase("hardware"), ("devices[0].items[0].ioDetails", null));
        var (accepted, _) = ProjectRuleCase("read_hardware_config", withoutIoDetails, includeIoDetails: false);
        Assert.Equal(OperationBatchStatus.Succeeded, accepted.Status);

        var chain = AssertRejectedWithoutLeak("read_hardware_config", RuleBase("hardware"), includeIoDetails: false);

        Assert.Contains("ValidateDeviceItem", chain);
    }

    [Fact]
    public void Project_ReachesTheUndeclaredOperationRuleInDecode()
    {
        var chain = AssertRejectedWithoutLeak("read_something_undeclared", CompleteAddDeviceResult);

        Assert.Equal(new[] { "Decode" }, chain);
    }

    /// <summary>
    /// A complete list item with exactly one required member absent. Each is rejected by the reader
    /// alone, before any validator runs. <c>selectable</c> is a value type, so its absence could
    /// otherwise read as <c>false</c>; this pins the reader's missing-member rule on a real
    /// Contracts value-type member.
    /// </summary>
    [Theory]
    [InlineData("items[0].kind")]
    [InlineData("items[0].selectable")]
    [InlineData("items[0].diagnostics")]
    public void Project_RejectsAListItemMissingOneMemberInTheReader(string path)
    {
        var payload = Edit(RuleBase("list"), (path, null));

        var chain = AssertRejectedWithoutLeak("list_network_objects", payload);

        Assert.Equal(new[] { "Decode" }, chain);
    }
}
