using TiaMcpServer.Contracts;

namespace TiaMcpServer.Network;

/// <summary>Outcome of resolving one operation's canonical target evidence.</summary>
public sealed record NetworkIdentityResolution(
    bool Success,
    NetworkWriteTargetEvidence? Evidence,
    string? FailureCategory,
    string? Error)
{
    public static NetworkIdentityResolution Ok(NetworkWriteTargetEvidence evidence) => new(true, evidence, null, null);

    public static NetworkIdentityResolution Fail(string failureCategory, string error) => new(false, null, failureCategory, error);
}

/// <summary>
/// Pure, Siemens-free host-side resolution of exactly which device, node, subnet, and IO system a
/// <c>configure_network_device</c> operation names, walked out of an already-decoded
/// <see cref="HardwareConfigInfo"/>.
///
/// <para>
/// Every step fails closed: zero matches, more than one match, or a candidate whose own identity
/// could not be read (modelled as an empty/null identity field) are all treated the same way —
/// <see cref="WorkerFailureCategories.PostconditionFailed"/> — because none of them describe a
/// selector that names exactly one existing thing. There is no first-match or name-only fallback
/// anywhere in this type.
/// </para>
///
/// <para>
/// Nested device-item and network-interface collections are walked defensively: a null collection
/// or null element (which <see cref="NetworkPayloadContract"/>'s strict decode contract rejects as
/// <c>protocol_error</c> before this type ever runs) is treated as empty rather than dereferenced,
/// so this stays safe even if that guarantee is ever loosened.
/// </para>
/// </summary>
public static class NetworkIdentityResolver
{
    public static NetworkIdentityResolution Resolve(NetworkOperationRequest operation, HardwareConfigInfo? state)
        => operation.Operation switch
        {
            "add_network_device" => ResolveCreation(operation),
            "configure_network_device" => ResolveConfiguration(operation, state),
            _ => NetworkIdentityResolution.Fail(
                WorkerFailureCategories.ValidationError,
                $"'{operation.Operation}' is not a resolvable network write operation."),
        };

    /// <summary>
    /// Creation names something that does not exist yet, so it is evidenced from the request alone:
    /// no hardware state is consulted and every existing-object member stays null.
    /// </summary>
    private static NetworkIdentityResolution ResolveCreation(NetworkOperationRequest operation)
        => NetworkIdentityResolution.Ok(new NetworkWriteTargetEvidence(
            operation.OperationId,
            operation.Operation,
            operation.DeviceName ?? string.Empty,
            operation.TypeIdentifier,
            Array.Empty<string>(),
            NetworkInterfaceName: null,
            NodeName: null,
            NodeId: null,
            SubnetName: null,
            SubnetId: null,
            IoSystemName: null,
            IoSystemNumber: null));

    private static NetworkIdentityResolution ResolveConfiguration(NetworkOperationRequest operation, HardwareConfigInfo? state)
    {
        if (state is null)
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.PostconditionFailed,
                $"Operation '{operation.OperationId}': no hardware snapshot was available to resolve this target.");
        }

        var target = operation.Target;
        if (target is null)
        {
            // The catalog already requires a target before this runs; fail closed rather than
            // dereference a null one if it is ever reached anyway.
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.ValidationError,
                $"Operation '{operation.OperationId}': 'target' is required to resolve a configure target.");
        }

        // A blank target.deviceName/target.nodeId (the catalog rejects these, but this resolver is
        // also exercised directly in tests) is never treated as a special case here: NamesMatch and
        // IdentitiesMatch already require both sides to be non-blank, so a blank selector simply
        // matches nothing and falls through to the same zero-match, fail-closed path below.

        var prefix = $"Operation '{operation.OperationId}'";

        var deviceMatch = MatchExactlyOne(
            OrEmpty(state.Devices),
            device => NamesMatch(device.Name, target.DeviceName));
        if (!deviceMatch.IsResolved)
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.PostconditionFailed,
                deviceMatch.IsAmbiguous
                    ? $"{prefix}: multiple devices are named '{target.DeviceName}'; device names must be unique to select one exactly."
                    : $"{prefix}: no device named '{target.DeviceName}' was found.");
        }

        var device = deviceMatch.Match!;
        var nodeMatch = MatchExactlyOne(
            EnumerateNodes(device),
            candidate => IdentitiesMatch(candidate.Node.NodeId, target.NodeId));
        if (!nodeMatch.IsResolved)
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.PostconditionFailed,
                nodeMatch.IsAmbiguous
                    ? $"{prefix}: multiple nodes on device '{target.DeviceName}' report nodeId '{target.NodeId}'; nodeId must select exactly one node."
                    : $"{prefix}: no node with nodeId '{target.NodeId}' was found on device '{target.DeviceName}'.");
        }

        var matchedNode = nodeMatch.Match!;
        var resolvedNode = matchedNode.Node;
        var deviceItemPath = matchedNode.Path;
        var networkInterfaceName = matchedNode.InterfaceName;

        SubnetInfo? resolvedSubnet = null;
        var subnetIdToResolve = operation.Changes?.Subnet?.SubnetId ?? operation.Changes?.IoSystem?.SubnetId;
        if (subnetIdToResolve is not null)
        {
            var subnetMatch = MatchExactlyOne(
                OrEmpty(state.Subnets),
                subnet => IdentitiesMatch(subnet.SubnetId, subnetIdToResolve));
            if (!subnetMatch.IsResolved)
            {
                return NetworkIdentityResolution.Fail(
                    WorkerFailureCategories.PostconditionFailed,
                    subnetMatch.IsAmbiguous
                        ? $"{prefix}: multiple subnets report subnetId '{subnetIdToResolve}'; subnetId must select exactly one subnet."
                        : $"{prefix}: no subnet with subnetId '{subnetIdToResolve}' was found.");
            }

            resolvedSubnet = subnetMatch.Match;
        }

        IoSystemInfo? resolvedIoSystem = null;
        if (operation.Changes?.IoSystem is { Number: { } requestedNumber })
        {
            // The catalog guarantees changes.subnet.subnetId and changes.ioSystem.subnetId name the
            // same subnet when both are present, so resolvedSubnet is already the right owner here.
            // A null resolvedSubnet at this point means changes.ioSystem.subnetId itself was blank —
            // the catalog rejects that before this runs, but this stays defensive rather than crash.
            if (resolvedSubnet is null)
            {
                return NetworkIdentityResolution.Fail(
                    WorkerFailureCategories.ValidationError,
                    $"{prefix}: 'changes.ioSystem.subnetId' is required to resolve an IO-system target.");
            }

            var ioSystemMatch = MatchExactlyOne(
                OrEmpty(resolvedSubnet.IoSystems),
                ioSystem => ioSystem.Number.HasValue && ioSystem.Number.Value == requestedNumber);
            if (!ioSystemMatch.IsResolved)
            {
                return NetworkIdentityResolution.Fail(
                    WorkerFailureCategories.PostconditionFailed,
                    ioSystemMatch.IsAmbiguous
                        ? $"{prefix}: multiple IO systems on subnet '{subnetIdToResolve}' report number {requestedNumber}; number must select exactly one IO system."
                        : $"{prefix}: no IO system with number {requestedNumber} was found on subnet '{subnetIdToResolve}'.");
            }

            resolvedIoSystem = ioSystemMatch.Match;
        }

        return NetworkIdentityResolution.Ok(new NetworkWriteTargetEvidence(
            operation.OperationId,
            operation.Operation,
            device.Name ?? string.Empty,
            device.TypeIdentifier,
            deviceItemPath,
            networkInterfaceName,
            resolvedNode.Name,
            resolvedNode.NodeId,
            resolvedSubnet?.Name,
            resolvedSubnet?.SubnetId,
            resolvedIoSystem?.Name,
            resolvedIoSystem?.Number));
    }

    /// <summary>
    /// Walks every nested device item and every network interface under it, depth first, yielding
    /// every node together with the device-item path and interface it was found under. Null nested
    /// collections are treated as empty rather than dereferenced.
    /// </summary>
    private static IEnumerable<NodeCandidate> EnumerateNodes(DeviceInfo device)
    {
        foreach (var item in OrEmpty(device.Items))
        {
            foreach (var candidate in EnumerateItem(item, Array.Empty<string>()))
            {
                yield return candidate;
            }
        }
    }

    private static IEnumerable<NodeCandidate> EnumerateItem(DeviceItemInfo item, IReadOnlyList<string> parentPath)
    {
        var path = new List<string>(parentPath) { item.Name ?? string.Empty };

        foreach (var networkInterface in OrEmpty(item.NetworkInterfaces))
        {
            foreach (var node in OrEmpty(networkInterface.Nodes))
            {
                yield return new NodeCandidate(path, networkInterface.Name, node);
            }
        }

        foreach (var child in OrEmpty(item.Items))
        {
            foreach (var candidate in EnumerateItem(child, path))
            {
                yield return candidate;
            }
        }
    }

    /// <summary>Case-insensitive, matching the worker's existing device-name lookup.</summary>
    private static bool NamesMatch(string? candidateName, string? requestedName)
        => !string.IsNullOrWhiteSpace(candidateName)
            && !string.IsNullOrWhiteSpace(requestedName)
            && string.Equals(candidateName, requestedName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ordinal identity comparison for node/subnet ids. A blank candidate identity (unreadable) or a
    /// blank requested identity never matches anything — an unreadable identity must never satisfy a
    /// write selector.
    /// </summary>
    private static bool IdentitiesMatch(string? candidateId, string? requestedId)
        => !string.IsNullOrWhiteSpace(candidateId)
            && !string.IsNullOrWhiteSpace(requestedId)
            && string.Equals(candidateId, requestedId, StringComparison.Ordinal);

    /// <summary>
    /// Substitutes an empty sequence for a null collection. The Contracts DTOs declare these
    /// collections non-nullable, but nullable reference annotations are not runtime-enforced, and
    /// this resolver is exercised directly in tests with hand-built state as well as through the
    /// validated worker path. The parameter is declared nullable so the null-coalesce below is a
    /// normal (not a "never null") operation regardless of the caller's static type — a defensive
    /// read must not assume the annotation held.
    /// </summary>
    private static IEnumerable<T> OrEmpty<T>(List<T>? source) => source ?? Enumerable.Empty<T>();

    private static MatchOutcome<T> MatchExactlyOne<T>(IEnumerable<T> candidates, Func<T, bool> predicate)
    {
        T? match = default;
        var count = 0;
        foreach (var candidate in candidates)
        {
            if (!predicate(candidate))
            {
                continue;
            }

            count++;
            if (count == 1)
            {
                match = candidate;
            }
        }

        return new MatchOutcome<T>(count, match);
    }

    private sealed record NodeCandidate(IReadOnlyList<string> Path, string InterfaceName, NodeInfo Node);

    /// <summary>
    /// Wraps a match count so a single ambiguity/absence rule can be applied uniformly at every
    /// resolution step, whether the candidate type is a device, node, subnet, or IO system.
    /// </summary>
    private sealed class MatchOutcome<T>
    {
        private readonly int _count;

        public MatchOutcome(int count, T? match)
        {
            _count = count;
            Match = match;
        }

        public T? Match { get; }

        public bool IsResolved => _count == 1;

        public bool IsAmbiguous => _count > 1;
    }
}
