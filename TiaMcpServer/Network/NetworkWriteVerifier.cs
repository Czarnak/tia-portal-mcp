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
        var checks = new List<NetworkFinalCheck>();
        var expected = new Dictionary<ExpectationKey, string?>(new ExpectationKeyComparer());
        // Equality identifies the node; optional constraints from any attempted observation
        // remain obligations even when a later write supersedes its scalar value.
        var constraints = new Dictionary<NetworkNodeIdentityInfo, List<NetworkNodeIdentityInfo>>(NetworkNodeIdentityComparer.Instance);
        // Preservation expectations whose pre-write value was unreadable can never pass.
        var unknownPrior = new HashSet<ExpectationKey>(new ExpectationKeyComparer());
        void ExpectNode(NetworkNodeIdentityInfo identity, string field, string? value, string? removedSubnet = null)
        {
            var nodeKey = NodeKey(identity, field) with { SubnetId = removedSubnet };
            expected[nodeKey] = value;
            unknownPrior.Remove(nodeKey);
            if (identity.InterfacePath is null || identity.InterfaceName is null && identity.InterfacePath.All(s => s.TypeIdentifier is null)) return;
            if (!constraints.TryGetValue(identity, out var observations))
                constraints.Add(NetworkWritePlanner.CloneIdentity(identity), observations = new());
            if (!observations.Any(previous => previous.InterfaceName == identity.InterfaceName
                && previous.InterfacePath!.Zip(identity.InterfacePath, (a, b) => a.TypeIdentifier == b.TypeIdentifier).All(equal => equal)))
                observations.Add(NetworkWritePlanner.CloneIdentity(identity));
        }
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
                ExpectNode(node, "exists", "true");
            if (!immediate.TryGetValue(item.OperationId, out var evidence))
            {
                if (item.Operation == "add_network_device") rootUncertain = true;
                operations.Add(new(item.OperationId, item.Operation, "unverified", null, null));
                checks.Add(Evaluate(NetworkFinalCheck.Operation(item.OperationId, "immediateEvidence"), "available", null, false));
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
                var name = evidence.Identity.DeviceName!;
                foreach (var check in evidence.Checks) expected[new("device", name, evidence.Identity.DeviceItemName, check.Name)] = check.Expected;
            }
            else if (item.Operation == "configure_network_device")
            {
                var identity = new NetworkNodeIdentityInfo { DeviceName = evidence.Identity.DeviceName!, NodeId = evidence.Identity.NodeId!,
                    InterfacePath = evidence.Identity.InterfacePath is { } path ? NetworkWritePlanner.ClonePath(path) : null,
                    InterfaceName = evidence.Identity.InterfaceName };
                ExpectNode(identity, "exists", "true");
                // Strict projection required exactly the applied keys. A skipped key must keep its
                // pre-write value, unless an earlier applied expectation for it already exists.
                foreach (var check in evidence.Checks) ExpectNode(identity, check.Name, check.Expected);
                var skipped = item.Result is { ValueKind: JsonValueKind.Object } result
                    && result.TryGetProperty("skippedSettings", out var map) && map.ValueKind == JsonValueKind.Object
                    ? map.EnumerateObject().Select(p => p.Name) : Enumerable.Empty<string>();
                foreach (var field in skipped.SelectMany(name => name == "IoSystem" ? new[] { "IoSystemSubnet", "IoSystemNumber" } : new[] { name }))
                {
                    if (expected.ContainsKey(NodeKey(identity, field))) continue;
                    var prior = initial?.CurrentSettings.GetValueOrDefault(field);
                    var known = prior is { Availability: "available", Value: not null };
                    ExpectNode(identity, field, known ? prior!.Value!.Value switch
                    {
                        string text => text, JsonElement => AttributeText(prior), _ => null
                    } : null);
                    if (!known) unknownPrior.Add(NodeKey(identity, field));
                }
            }
            else
            {
                var subnet = evidence.Identity.SubnetId!;
                if (effect?.RootDeviceCount is null) rootUncertain = true;
                if (item.Operation == "delete_subnet")
                {
                    foreach (var key in expected.Keys.Where(k => k.Kind == "subnet" && k.Identity == subnet).ToArray()) expected.Remove(key);
                    expected[new("subnet", subnet, null, "absent")] = "true";
                    if (effect?.ConnectionsComplete != true || initial is null)
                        checks.Add(Evaluate(NetworkFinalCheck.Operation(item.OperationId, "affectedInventory"), "complete", null, false));
                    else
                    {
                        foreach (var node in initial.AffectedNodes.Concat(effect.AffectedNodes).Distinct(NetworkNodeIdentityComparer.Instance))
                        {
                            ExpectNode(node, "removedSubnet", "true", subnet);
                            // Replace only expectations referring to the removed relationship.
                            var subnetKey = NodeKey(node, "Subnet");
                            if (expected.GetValueOrDefault(subnetKey) == subnet) expected.Remove(subnetKey);
                            var ioKey = subnetKey with { Field = "IoSystemSubnet" };
                            if (expected.GetValueOrDefault(ioKey) == subnet)
                            {
                                expected.Remove(ioKey);
                                expected.Remove(subnetKey with { Field = "IoSystemNumber" });
                            }
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
        // Reuse preparation's ordinary-project structural gate after mutation as well.
        var readable = snapshot.Success && NetworkWritePlanner.DiscoveryComplete(snapshot.State!);
        var state = readable ? snapshot.State : null;
        if (!readable) checks.Add(Evaluate(NetworkFinalCheck.Write("finalHardwareState"), "readable complete inventory", null, false));
        if (verifyRootCount) checks.Add(Evaluate(NetworkFinalCheck.Write("networkDeviceCountUnchanged"), Format(rootCount), Format(state?.RootDeviceCount), !rootUncertain && state?.RootDeviceCount.HasValue == true));
        foreach (var entry in expected)
        {
            var key = entry.Key;
            var subject = key.Kind switch
            {
                "device" => NetworkFinalCheck.Device(key.Identity, key.NodeId, key.Field),
                "subnet" => NetworkFinalCheck.Subnet(key.Identity, key.Field),
                _ => NetworkFinalCheck.Node(key.NodeIdentity!, key.Field, key.SubnetId),
            };
            var observation = Observe(state, key, key.NodeIdentity is null ? null : constraints.GetValueOrDefault(key.NodeIdentity));
            if (key.Kind == "subnet" && key.Field is "HighestAddress" or "TransmissionSpeed" && state is not null
                && state.Subnets.All(s => !string.IsNullOrWhiteSpace(s.SubnetId))
                && state.Subnets.Count(s => s.SubnetId == key.Identity) == 1)
            {
                var attributes = await NetworkWritePlanner.ReadAttributesAsync(_client, projectPath,
                    new() { Kind = NetworkObjectKinds.Subnet, SubnetId = key.Identity }, new[] { key.Field }).ConfigureAwait(false);
                var value = attributes.TryGetValue(key.Field, out var attribute) ? AttributeText(attribute) : null;
                observation = (value, value is not null);
            }
            // The hardware snapshot does not carry this flag; inspect the exact final node instead.
            if (key.Kind == "node" && key.Field == "PnDeviceNameAutoGeneration" && state is not null
                && state.Devices.All(d => !string.IsNullOrWhiteSpace(d.Name))
                && NetworkWritePlanner.TryResolveAffected(state, key.NodeIdentity!, out var qualified, out _)
                && constraints.GetValueOrDefault(key.NodeIdentity!)?.All(identity => NetworkWritePlanner.TryResolveAffected(state, identity, out _, out _)) != false)
            {
                var attributes = await NetworkWritePlanner.ReadAttributesAsync(_client, projectPath,
                    NetworkWritePlanner.NodeTarget(qualified!.DeviceName, qualified.NodeId, qualified.InterfacePath!), new[] { key.Field }).ConfigureAwait(false);
                var value = attributes.TryGetValue(key.Field, out var attribute) ? AttributeText(attribute) : null;
                observation = (value, value is not null);
            }
            checks.Add(Evaluate(subject, entry.Value, observation.Value, observation.Readable && !unknownPrior.Contains(key)));
        }
        return new(operations.All(o => o.Status is "passed" or "not_required") && checks.All(c => c.Status == "passed"), operations, checks, null);
    }

    private static (string? Value, bool Readable) Observe(HardwareConfigInfo? state, ExpectationKey key, IReadOnlyList<NetworkNodeIdentityInfo>? constraints)
    {
        if (state is null) return (null, false);
        if (key.Kind == "subnet")
        {
            // Structural completeness cannot prove absence if any candidate ID is unreadable.
            if (state.Subnets.Any(s => string.IsNullOrWhiteSpace(s.SubnetId))) return (null, false);
            var subnets = state.Subnets.Where(s => s.SubnetId == key.Identity).ToArray();
            if (key.Field == "absent") return ((subnets.Length == 0).ToString().ToLowerInvariant(), true);
            if (key.Field == "exists") return ((subnets.Length == 1).ToString().ToLowerInvariant(), true);
            if (subnets.Length != 1) return (null, false);
            var subnet = subnets[0];
            var value = key.Field switch { "Name" => subnet.Name, "TypeIdentifier" => subnet.TypeIdentifier, _ => null };
            return (value, value is not null);
        }
        if (state.Devices.Any(d => string.IsNullOrWhiteSpace(d.Name))) return (null, false);
        var devices = state.Devices.Where(d => NetworkWritePlanner.NamesEqual(d.Name, key.Identity)).ToArray();
        if (key.Kind == "device")
        {
            if (devices.Length != 1) return (null, false);
            var device = devices[0];
            // Same rule as the worker's creator: unique exact name among top-level items only.
            // An ET200SP head module's sub-item repeats the head's name, so children are never searched.
            var matchingItems = device.Items.Where(i => string.Equals(i.Name, key.NodeId, StringComparison.Ordinal)).ToArray();
            var item = matchingItems.Length == 1 ? matchingItems[0] : null;
            var value = key.Field switch
            {
                "deviceName" => device.Name, "typeIdentifier" => item?.TypeIdentifier,
                "deviceItemName" => item?.Name, _ => null
            };
            return (value, value is not null);
        }
        if (key.Field == "exists" && devices.Length != 1) return ("false", true);
        if (key.NodeIdentity is null || !NetworkWritePlanner.TryResolveAffected(state, key.NodeIdentity, out _, out var selectedNode)) return (null, false);
        if (constraints?.Any(identity => !NetworkWritePlanner.TryResolveAffected(state, identity, out _, out _)) == true) return (null, false);
        if (key.Field == "exists") return ("true", true);
        var node = selectedNode!;
        if (key.Field == "removedSubnet")
        {
            var removed = key.SubnetId;
            return node.ConnectionEvidence?.Complete == true
                ? ((node.ConnectionEvidence.SubnetId != removed && node.ConnectionEvidence.IoSystemSubnetId != removed).ToString().ToLowerInvariant(), true)
                : (null, false);
        }
        if (key.Field == "Subnet" && node.ConnectionEvidence?.SubnetId is { } connectedSubnet
            && (state.Subnets.Any(s => string.IsNullOrWhiteSpace(s.SubnetId))
                || state.Subnets.Count(s => s.SubnetId == connectedSubnet) != 1)) return (null, false);
        if (key.Field is "IoSystemSubnet" or "IoSystemNumber" && node.ConnectionEvidence?.IoSystemSubnetId is { } ioSubnet)
        {
            if (state.Subnets.Any(s => string.IsNullOrWhiteSpace(s.SubnetId))) return (null, false);
            var subnets = state.Subnets.Where(s => s.SubnetId == ioSubnet).ToArray();
            if (subnets.Length != 1 || subnets[0].IoSystems.Any(i => i.Number is null || i.Number < 0)
                || subnets[0].IoSystems.Count(i => i.Number == node.ConnectionEvidence.IoSystemNumber) != 1) return (null, false);
        }
        var nodeValue = NetworkWritePlanner.NodeValue(node, key.Field);
        return (nodeValue, NetworkWritePlanner.IsConnectionKey(key.Field) ? node.ConnectionEvidence?.Complete == true : nodeValue is not null);
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
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }
    private static string? Format(int? value) => value?.ToString(CultureInfo.InvariantCulture);
    private static NetworkFinalCheck Evaluate(NetworkFinalCheck check, string? expected, string? observed, bool readable) => check with
    {
        Expected = expected, Observed = observed,
        Status = !readable ? "unverified" : expected == observed ? "passed" : "failed",
        Message = !readable ? "Evidence is unavailable. Inspect current state with network_read before retrying; do not replay automatically."
            : expected == observed ? null : check.Kind == "node" && check.Field is "IoSystemSubnet" or "IoSystemNumber"
                ? "The explicit IO relationship differs. Inspect current state and possible side effects of later subnet changes with network_read."
                : "The final state differs from the effective attempted changes. Inspect with network_read."
    };
    private static ExpectationKey NodeKey(NetworkNodeIdentityInfo identity, string field) => new("node", identity.DeviceName, identity.NodeId, field,
        NetworkWritePlanner.CloneIdentity(identity));
    // SubnetId names the removed subnet of a node's removedSubnet expectation.
    private sealed record ExpectationKey(string Kind, string Identity, string? NodeId, string Field, NetworkNodeIdentityInfo? NodeIdentity = null,
        string? SubnetId = null);
    private sealed class ExpectationKeyComparer : IEqualityComparer<ExpectationKey>
    {
        // Only device names share the selector's case-insensitive identity semantics.
        // Opaque subnet/node IDs, fields and observed values keep their exact spelling.
        private static StringComparer IdentityComparer(ExpectationKey key)
            => key.Kind is "node" or "device" ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public bool Equals(ExpectationKey? left, ExpectationKey? right)
            => ReferenceEquals(left, right) || left is not null && right is not null
                && left.Kind == right.Kind && IdentityComparer(left).Equals(left.Identity, right.Identity)
                && left.NodeId == right.NodeId && left.Field == right.Field && left.SubnetId == right.SubnetId
                && (left.Kind != "node" || NetworkNodeIdentityComparer.Instance.Equals(left.NodeIdentity, right.NodeIdentity));

        public int GetHashCode(ExpectationKey key)
            => HashCode.Combine(key.Kind, IdentityComparer(key).GetHashCode(key.Identity), key.NodeId, key.Field, key.SubnetId,
                key.NodeIdentity is null ? 0 : NetworkNodeIdentityComparer.Instance.GetHashCode(key.NodeIdentity));
    }
}
