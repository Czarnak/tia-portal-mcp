using System.Globalization;
using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;

namespace TiaMcpServer.Network;

/// <summary>Retains immediate evidence and reads the effective attempted prefix, without replay.</summary>
public sealed class NetworkWriteVerifier
{
    private readonly OpennessWorkerClient _client;
    private readonly IReadOnlyDictionary<string, NetworkWriteEffect> _initial;
    private readonly IReadOnlyDictionary<string, NetworkWriteEffect> _current;
    public NetworkWriteVerifier(OpennessWorkerClient client) : this(client, new Dictionary<string, NetworkWriteEffect>(), new Dictionary<string, NetworkWriteEffect>()) { }
    internal NetworkWriteVerifier(OpennessWorkerClient client, IReadOnlyDictionary<string, NetworkWriteEffect> initial,
        IReadOnlyDictionary<string, NetworkWriteEffect> current)
    { _client = client; _initial = initial; _current = current; }

    public async Task<NetworkWriteVerification> VerifyAsync(string? projectPath, StructuredOperationBatch batch,
        IReadOnlyDictionary<string, NetworkMutationVerificationInfo> immediate)
    {
        var operations = new List<NetworkOperationVerification>();
        var checks = new List<NetworkVerificationCheckInfo>();
        var expected = new Dictionary<ExpectationKey, string?>();
        // The first immutable observation is the baseline. Later re-plans are observations,
        // not permission to absorb external root-device loss into our expected final state.
        int? rootCount = batch.Operations.Select(item => _initial.GetValueOrDefault(item.OperationId)?.RootDeviceCount).FirstOrDefault();
        var rootUncertain = !rootCount.HasValue;
        var verifyRootCount = false;
        foreach (var item in batch.Operations.Where(i => i.Status != OperationBatchStatus.Skipped))
        {
            verifyRootCount |= item.Operation is "create_subnet" or "update_subnet" or "delete_subnet";
            _current.TryGetValue(item.OperationId, out var effect);
            _initial.TryGetValue(item.OperationId, out var initial);
            // Preservation is an invariant of an attempted write, even when its outcome is
            // unknown. Known identities do not prove that the requested mutation was applied.
            foreach (var node in (initial?.AffectedNodes ?? Array.Empty<NetworkNodeIdentityInfo>())
                .Concat(effect?.AffectedNodes ?? Array.Empty<NetworkNodeIdentityInfo>()))
                expected[new("node", node.DeviceName, node.NodeId, "exists")] = "true";
            if (!immediate.TryGetValue(item.OperationId, out var evidence))
            {
                if (item.Operation == "add_network_device") rootUncertain = true;
                operations.Add(new(item.OperationId, item.Operation, "unverified", null, null));
                checks.Add(Check(item.OperationId + "/immediateEvidence", "available", null, false));
                continue;
            }
            operations.Add(new(item.OperationId, item.Operation, evidence.Status, evidence, null));
            if (item.Operation == "add_network_device")
            {
                if (item.Status == OperationBatchStatus.Succeeded && evidence.Status == "passed")
                {
                    if (rootCount.HasValue) rootCount++;
                }
                else rootUncertain = true;
                var name = evidence.Identity["deviceName"];
                foreach (var check in evidence.Checks) expected[new("device", name, evidence.Identity["deviceItemName"], check.Name)] = check.Expected;
            }
            else if (item.Operation == "configure_network_device")
            {
                var name = evidence.Identity["deviceName"];
                var node = evidence.Identity["nodeId"];
                expected[new("node", name, node, "exists")] = "true";
                // Strict projection required exactly the applied keys. Skips are execution failures,
                // but make no final setting claim and cannot erase an earlier applied expectation.
                foreach (var check in evidence.Checks) expected[new("node", name, node, check.Name)] = check.Expected;
            }
            else
            {
                var subnet = evidence.Identity["subnetId"];
                if (effect?.RootDeviceCount is null) rootUncertain = true;
                if (item.Operation == "delete_subnet")
                {
                    foreach (var key in expected.Keys.Where(k => k.Kind == "subnet" && k.Identity == subnet).ToArray()) expected.Remove(key);
                    expected[new("subnet", subnet, null, "absent")] = "true";
                    if (effect?.ConnectionsComplete != true || initial is null)
                        checks.Add(Check(item.OperationId + "/affectedInventory", "complete", null, false));
                    else
                    {
                        foreach (var node in effect.AffectedNodes)
                        {
                            expected[new("node", node.DeviceName, node.NodeId, "removedSubnet:" + subnet)] = "true";
                            // Replace only expectations referring to the removed relationship.
                            var subnetKey = new ExpectationKey("node", node.DeviceName, node.NodeId, "Subnet");
                            if (expected.GetValueOrDefault(subnetKey) == subnet) expected.Remove(subnetKey);
                            var ioKey = subnetKey with { Field = "IoSystem" };
                            if (expected.TryGetValue(ioKey, out var io) && IoSubnet(io) == subnet) expected.Remove(ioKey);
                        }
                    }
                }
                else
                {
                    expected[new("subnet", subnet, null, "exists")] = "true";
                    foreach (var check in evidence.Checks.Where(c => c.Name is not "subnetIdentity" and not "networkDeviceCountUnchanged"))
                        expected[new("subnet", subnet, null, check.Name)] = check.Expected;
                }
            }
        }
        if (operations.Count == 0) return new(true, operations, checks, null);
        var snapshot = await NetworkWritePlanner.ReadCurrentStateAsync(_client, projectPath).ConfigureAwait(false);
        var readable = snapshot.Success && NetworkWritePlanner.DiscoveryComplete(snapshot.State!);
        var state = readable ? snapshot.State : null;
        if (!readable) checks.Add(Check("finalHardwareState", "readable complete inventory", null, false));
        if (verifyRootCount) checks.Add(Check("networkDeviceCountUnchanged", Format(rootCount), Format(state?.RootDeviceCount), !rootUncertain && state?.RootDeviceCount.HasValue == true));
        foreach (var entry in expected)
        {
            var key = entry.Key;
            var name = $"{key.Kind}/{key.Identity}/{key.NodeId}/{key.Field}";
            var observation = Observe(state, key);
            if (key.Kind == "subnet" && key.Field is "HighestAddress" or "TransmissionSpeed" && state is not null)
            {
                var attributes = await NetworkWritePlanner.ReadAttributesAsync(_client, projectPath, key.Identity, new[] { key.Field }).ConfigureAwait(false);
                var value = attributes.TryGetValue(key.Field, out var attribute) && attribute.Availability == "available"
                    ? AttributeText(attribute) : null;
                observation = (value, value is not null);
            }
            checks.Add(Check(name, entry.Value, observation.Value, observation.Readable));
        }
        return new(operations.All(o => o.Status is "passed" or "not_required") && checks.All(c => c.Status == "passed"), operations, checks, null);
    }

    private static (string? Value, bool Readable) Observe(HardwareConfigInfo? state, ExpectationKey key)
    {
        if (state is null) return (null, false);
        if (key.Kind == "subnet")
        {
            var subnets = state.Subnets.Where(s => s.SubnetId == key.Identity).ToArray();
            if (key.Field == "absent") return ((subnets.Length == 0).ToString().ToLowerInvariant(), true);
            if (key.Field == "exists") return ((subnets.Length == 1).ToString().ToLowerInvariant(), true);
            if (subnets.Length != 1) return (null, false);
            var subnet = subnets[0];
            var value = key.Field switch { "Name" => subnet.Name, "TypeIdentifier" => subnet.TypeIdentifier, _ => null };
            return (value, value is not null);
        }
        var devices = state.Devices.Where(d => NetworkWritePlanner.NamesEqual(d.Name, key.Identity)).ToArray();
        if (key.Kind == "device")
        {
            if (devices.Length != 1) return (null, false);
            var device = devices[0];
            if (key.Field != "deviceName" && DeviceItems(device.Items).Any(i => string.IsNullOrWhiteSpace(i.Name))) return (null, false);
            var matchingItems = DeviceItems(device.Items).Where(i => i.Name == key.NodeId).ToArray();
            var item = matchingItems.Length == 1 ? matchingItems[0] : null;
            var value = key.Field switch
            {
                "deviceName" => device.Name, "typeIdentifier" => item?.TypeIdentifier,
                "deviceItemName" => item?.Name, _ => null
            };
            return (value, value is not null);
        }
        var nodes = NetworkWritePlanner.Nodes(state).Where(p => NetworkWritePlanner.NamesEqual(p.DeviceName, key.Identity) && p.Node.NodeId == key.NodeId).ToArray();
        if (key.Field == "exists") return ((devices.Length == 1 && nodes.Length == 1).ToString().ToLowerInvariant(), true);
        if (devices.Length != 1 || nodes.Length != 1) return (null, false);
        var node = nodes[0].Node;
        if (key.Field.StartsWith("removedSubnet:", StringComparison.Ordinal))
        {
            var removed = key.Field["removedSubnet:".Length..];
            return node.ConnectionEvidence?.Complete == true
                ? ((node.ConnectionEvidence.SubnetId != removed && node.ConnectionEvidence.IoSystemSubnetId != removed).ToString().ToLowerInvariant(), true)
                : (null, false);
        }
        var nodeValue = NetworkWritePlanner.NodeValue(node, key.Field);
        return (nodeValue, key.Field is "Subnet" or "IoSystem" ? node.ConnectionEvidence?.Complete == true : nodeValue is not null);
    }
    private static string? AttributeText(NetworkAttributeInfo attribute)
    {
        if (attribute.Value?.Value is not JsonElement element) return null;
        if (attribute.Value.Kind == "enum")
        {
            // The ordinary inspection contract has already strictly decoded this enum DTO.
            // Match the exact API symbol used by the immediate worker check; never guess by digits.
            var symbol = CanonicalJson.DeserializeWorkerPayload<NetworkEnumValueInfo>(element.GetRawText()).Symbol;
            return string.IsNullOrWhiteSpace(symbol) ? null : symbol;
        }
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };
    }
    private static IEnumerable<DeviceItemInfo> DeviceItems(IEnumerable<DeviceItemInfo> items) => items.SelectMany(i => new[] { i }.Concat(DeviceItems(i.Items)));
    private static string? IoSubnet(string? value)
    {
        if (value is null) return null;
        using var document = JsonDocument.Parse(value);
        return document.RootElement[0].GetString();
    }
    private static string? Format(int? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static NetworkVerificationCheckInfo Check(string name, string? expected, string? observed, bool readable) => new()
    {
        Name = name, Expected = expected, Observed = observed,
        Status = !readable ? "unverified" : expected == observed ? "passed" : "failed",
        Message = !readable ? "Evidence is unavailable. Inspect current state with network_read before retrying; do not replay automatically."
            : expected == observed ? null : name.EndsWith("/IoSystem", StringComparison.Ordinal)
                ? "The explicit IO relationship differs. Inspect current state and possible side effects of later subnet changes with network_read."
                : "The final state differs from the effective attempted changes. Inspect with network_read."
    };
    private sealed record ExpectationKey(string Kind, string Identity, string? NodeId, string Field);
}
