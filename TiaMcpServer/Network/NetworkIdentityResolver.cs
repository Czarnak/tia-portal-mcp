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
/// Zero matches return <see cref="WorkerFailureCategories.TargetNotFound"/> and duplicate matches
/// <see cref="WorkerFailureCategories.TargetAmbiguous"/>; a resolved subnet with a missing or
/// unsupported network type returns <see cref="WorkerFailureCategories.TargetKindUnsupported"/>, and a
/// missing hardware snapshot returns <see cref="WorkerFailureCategories.WorkerOperationFailed"/>.
/// <see cref="WorkerFailureCategories.PostconditionFailed"/> is reserved for late worker-side matches
/// and postchecks, never for this pre-mutation resolution.
/// A readable match with unreadable competing identities returns
/// <see cref="WorkerFailureCategories.WorkerOperationFailed"/> because uniqueness is unknown.
/// There is no first-match or name-only fallback anywhere in this type.
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
            "add_network_device" => ResolveCreation(operation, state),
            "configure_network_device" => ResolveConfiguration(operation, state),
            "create_subnet" => ResolveSubnetCreation(operation, state),
            "update_subnet" => ResolveExistingSubnet(operation, state, validateChanges: true),
            "delete_subnet" => ResolveExistingSubnet(operation, state, validateChanges: false),
            _ => NetworkIdentityResolution.Fail(
                WorkerFailureCategories.ValidationError,
                $"'{operation.Operation}' is not a resolvable network write operation."),
        };

    /// <summary>
    /// Creation names something that does not exist yet, so it is evidenced from the request alone:
    /// existing names must be readable when an inventory is supplied. Existing-object members stay null.
    /// </summary>
    private static NetworkIdentityResolution ResolveCreation(NetworkOperationRequest operation, HardwareConfigInfo? state)
    {
        if (state is not null && OrEmpty(state.Devices).Any(device => string.IsNullOrWhiteSpace(device.Name)))
            return UnreadableIdentity(operation.OperationId, "project device-name");
        return NetworkIdentityResolution.Ok(new NetworkWriteTargetEvidence(
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
    }

    /// <summary>
    /// A new subnet's <c>subnetId</c> is assigned by Openness at creation time, so, like
    /// <see cref="ResolveCreation"/>, its new identity is evidenced from the request. Existing names
    /// must be readable when an inventory is supplied;
    /// <see cref="NetworkWriteTargetEvidence.SubnetId"/> stays null, and every
    /// device-identity member also stays null because a subnet target never has a device identity.
    /// </summary>
    private static NetworkIdentityResolution ResolveSubnetCreation(NetworkOperationRequest operation, HardwareConfigInfo? state)
    {
        if (state is not null && OrEmpty(state.Subnets).Any(subnet => string.IsNullOrWhiteSpace(subnet.Name)))
            return UnreadableIdentity(operation.OperationId, "project subnet-name");
        return NetworkIdentityResolution.Ok(new NetworkWriteTargetEvidence(
            operation.OperationId,
            operation.Operation,
            DeviceName: null,
            DeviceTypeIdentifier: null,
            DeviceItemPath: Array.Empty<string>(),
            NetworkInterfaceName: null,
            NodeName: null,
            NodeId: null,
            SubnetName: operation.Subnet!.Name,
            SubnetId: null,
            IoSystemName: null,
            IoSystemNumber: null));
    }

    /// <summary>
    /// Resolves <c>update_subnet</c>/<c>delete_subnet</c> targets against
    /// <see cref="HardwareConfigInfo.Subnets"/>: exactly one element whose nonblank
    /// <c>SubnetId</c> equals the requested value by <see cref="StringComparison.Ordinal"/>. There
    /// is no name fallback, no case-insensitive match, and no first-match/index fallback — an
    /// unreadable or absent identity never satisfies a write selector, and zero or duplicate
    /// matches fail exactly the same way any other resolver step in this type does.
    ///
    /// <para>
    /// Subnet IDs must be readable throughout the selected project namespace. The selected subnet's
    /// <see cref="SubnetInfo.NetworkType"/> is checked. <see cref="SubnetInfo.ConnectedNodeNames"/> and <see cref="SubnetInfo.IoSystems"/>
    /// are never read here: connected subnets resolve and remain deletable exactly like
    /// disconnected ones, because this resolver builds no dependency inventory.
    /// </para>
    /// </summary>
    private static NetworkIdentityResolution ResolveExistingSubnet(
        NetworkOperationRequest operation,
        HardwareConfigInfo? state,
        bool validateChanges)
    {
        if (state is null)
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.WorkerOperationFailed,
                $"Operation '{operation.OperationId}': no hardware snapshot was available to resolve this target.");
        }

        var target = operation.Target;
        if (target is null)
        {
            // The catalog already requires a target before this runs; fail closed rather than
            // dereference a null one if it is ever reached anyway.
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.ValidationError,
                $"Operation '{operation.OperationId}': 'target' is required to resolve a subnet target.");
        }

        var prefix = $"Operation '{operation.OperationId}'";
        var requestedId = target.SubnetId;

        var subnetMatch = MatchExactlyOne(
            OrEmpty(state.Subnets),
            subnet => IdentitiesMatch(subnet.SubnetId, requestedId));
        if (!subnetMatch.IsResolved)
        {
            return NetworkIdentityResolution.Fail(
                subnetMatch.IsAmbiguous ? WorkerFailureCategories.TargetAmbiguous : WorkerFailureCategories.TargetNotFound,
                subnetMatch.IsAmbiguous
                    ? $"{prefix}: multiple subnets report subnetId '{requestedId}'; subnetId must select exactly one subnet."
                    : $"{prefix}: no subnet with subnetId '{requestedId}' was found.");
        }

        if (OrEmpty(state.Subnets).Any(candidate => string.IsNullOrWhiteSpace(candidate.SubnetId)))
            return UnreadableIdentity(operation.OperationId, "project subnet-ID");
        var subnet = subnetMatch.Match!;
        if (!SubnetLifecycleContract.IsSupportedNetworkType(subnet.NetworkType))
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.TargetKindUnsupported,
                $"{prefix}: subnet '{requestedId}' reports a missing or unsupported network type and cannot be "
                    + "resolved as a write target.");
        }

        if (validateChanges
            && string.Equals(subnet.NetworkType, SubnetLifecycleContract.Ethernet, StringComparison.Ordinal)
            && operation.SubnetChanges is { } changes
            && (changes.HighestAddress is not null || changes.TransmissionSpeed is not null))
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.ValidationError,
                $"{prefix}: 'subnetChanges.highestAddress' and 'subnetChanges.transmissionSpeed' are not "
                    + $"applicable to subnet '{requestedId}', which is network type '{SubnetLifecycleContract.Ethernet}'.");
        }

        return NetworkIdentityResolution.Ok(new NetworkWriteTargetEvidence(
            operation.OperationId,
            operation.Operation,
            DeviceName: null,
            DeviceTypeIdentifier: null,
            DeviceItemPath: Array.Empty<string>(),
            NetworkInterfaceName: null,
            NodeName: null,
            NodeId: null,
            SubnetName: subnet.Name,
            SubnetId: subnet.SubnetId,
            IoSystemName: null,
            IoSystemNumber: null));
    }

    private static NetworkIdentityResolution ResolveConfiguration(NetworkOperationRequest operation, HardwareConfigInfo? state)
    {
        if (state is null)
        {
            return NetworkIdentityResolution.Fail(
                WorkerFailureCategories.WorkerOperationFailed,
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
                deviceMatch.IsAmbiguous ? WorkerFailureCategories.TargetAmbiguous : WorkerFailureCategories.TargetNotFound,
                deviceMatch.IsAmbiguous
                    ? $"{prefix}: multiple devices are named '{target.DeviceName}'; device names must be unique to select one exactly."
                    : $"{prefix}: no device named '{target.DeviceName}' was found.");
        }

        if (OrEmpty(state.Devices).Any(candidate => string.IsNullOrWhiteSpace(candidate.Name)))
            return UnreadableIdentity(operation.OperationId, "project device-name");
        var device = deviceMatch.Match!;
        var selection = SelectNode(device, target);
        if (selection.Error is not null) return NetworkIdentityResolution.Fail(selection.Category!, $"{prefix}: {selection.Error}");
        var matchedNode = selection.Candidate!;
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
                    subnetMatch.IsAmbiguous ? WorkerFailureCategories.TargetAmbiguous : WorkerFailureCategories.TargetNotFound,
                    subnetMatch.IsAmbiguous
                        ? $"{prefix}: multiple subnets report subnetId '{subnetIdToResolve}'; subnetId must select exactly one subnet."
                        : $"{prefix}: no subnet with subnetId '{subnetIdToResolve}' was found.");
            }

            if (OrEmpty(state.Subnets).Any(candidate => string.IsNullOrWhiteSpace(candidate.SubnetId)))
                return UnreadableIdentity(operation.OperationId, "project subnet-ID");
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
                    ioSystemMatch.IsAmbiguous ? WorkerFailureCategories.TargetAmbiguous : WorkerFailureCategories.TargetNotFound,
                    ioSystemMatch.IsAmbiguous
                        ? $"{prefix}: multiple IO systems on subnet '{subnetIdToResolve}' report number {requestedNumber}; number must select exactly one IO system."
                        : $"{prefix}: no IO system with number {requestedNumber} was found on subnet '{subnetIdToResolve}'.");
            }

            if (OrEmpty(resolvedSubnet.IoSystems).Any(candidate => !candidate.Number.HasValue || candidate.Number < 0))
                return UnreadableIdentity(operation.OperationId, $"subnet '{resolvedSubnet.SubnetId}' IO-number");
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
            resolvedIoSystem?.Number,
            matchedNode.OwnerPath));
    }

    private static NetworkIdentityResolution UnreadableIdentity(string operationId, string identityNamespace)
        => NetworkIdentityResolution.Fail(WorkerFailureCategories.WorkerOperationFailed,
            $"Operation '{operationId}': required {identityNamespace} identities are unreadable; exact selection cannot be proved. Inspect the hardware configuration before retrying.");

    /// <summary>Freezes the request and fills only an underspecified bare owner's path.</summary>
    public static NetworkOperationRequest BindPreparedTarget(NetworkOperationRequest item, NetworkWriteTargetEvidence target)
    {
        var copy = TiaMcpServer.Json.CanonicalJson.Deserialize<NetworkOperationRequest>(TiaMcpServer.Json.CanonicalJson.Serialize(item));
        if (copy.Operation == "configure_network_device" && copy.Target!.InterfacePath is null && copy.Target.ItemPath is null)
            copy.Target.InterfacePath = target.InterfacePath?.Select(segment => new NetworkInterfacePathSegment
            { Name = segment.Name, PositionNumber = segment.PositionNumber, TypeIdentifier = segment.TypeIdentifier }).ToArray()
                ?? throw new InvalidOperationException("Prepared configuration target has no qualified owner identity.");
        return copy;
    }

    internal static NodeInfo PreparedNode(HardwareConfigInfo state, NetworkWriteTargetEvidence target)
    {
        var device = state.Devices.Single(d => NamesMatch(d.Name, target.DeviceName));
        var owner = NetworkInterfacePathMatcher.Match(device.Items, target.InterfacePath!, i => i.Items,
            i => i.Name, i => i.PositionNumber, i => i.TypeIdentifier);
        if (!owner.Success) throw new InvalidOperationException("Resolved owner evidence changed within the decoded snapshot.");
        return owner.Item!.NetworkInterfaces.Single().Nodes.Single(n => IdentitiesMatch(n.NodeId, target.NodeId));
    }

    private static (NodeCandidate? Candidate, string? Category, string? Error) SelectNode(DeviceInfo device, NetworkObjectTarget target)
    {
        IReadOnlyList<NetworkInterfacePathSegmentInfo>? path = target.InterfacePath?.Select(x => new NetworkInterfacePathSegmentInfo
        { Name = x.Name, PositionNumber = x.PositionNumber ?? -1, TypeIdentifier = x.TypeIdentifier }).ToArray();
        if (target.InterfacePath is not null && target.ItemPath is not null)
            return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Both owner path forms were supplied.");
        if (target.ItemPath is not null)
        {
            if (target.NodeIndex is null) return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Legacy itemPath requires nodeIndex.");
            var siblings = device.Items;
            foreach (var segment in target.ItemPath)
            {
                if (segment.Index is not int index || index < 0 || index >= siblings.Count)
                    return (null, WorkerFailureCategories.TargetNotFound, "Legacy itemPath index was not found.");
                var item = siblings[index];
                if (item.Name != segment.Name || item.PositionNumber != segment.PositionNumber || item.TypeIdentifier != segment.TypeIdentifier
                    || string.IsNullOrWhiteSpace(segment.TypeIdentifier))
                    return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Legacy itemPath evidence does not match.");
                siblings = item.Items;
            }
            path = target.ItemPath.Select(x => new NetworkInterfacePathSegmentInfo
            { Name = x.Name, PositionNumber = x.PositionNumber ?? -1, TypeIdentifier = x.TypeIdentifier }).ToArray();
        }
        if (path is not null)
        {
            var owner = NetworkInterfacePathMatcher.Match(device.Items, path, i => i.Items, i => i.Name, i => i.PositionNumber, i => i.TypeIdentifier);
            if (!owner.Success) return (null, owner.FailureCategory, owner.Error);
            if (owner.Item!.NetworkInterfaces.Count != 1)
                return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Selected owner does not expose exactly one interface.");
            var networkInterface = owner.Item.NetworkInterfaces[0];
            if (target.InterfaceName is not null && target.InterfaceName != networkInterface.Name)
                return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Selected interface name constraint does not match.");
            if (networkInterface.Nodes.Any(n => string.IsNullOrWhiteSpace(n.NodeId)))
                return (null, WorkerFailureCategories.WorkerOperationFailed, "Selected interface node identities are unreadable.");
            var matches = networkInterface.Nodes.Where(n => IdentitiesMatch(n.NodeId, target.NodeId)).ToArray();
            if (matches.Length != 1) return (null, matches.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
                "Node identity must be unique within the selected interface.");
            if (target.NodeIndex is int index && (index < 0 || index >= networkInterface.Nodes.Count || !ReferenceEquals(networkInterface.Nodes[index], matches[0])))
                return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Node index constraint does not match.");
            return (new(path.Select(x => x.Name).ToArray(), path, networkInterface.Name, matches[0]), null, null);
        }
        if (target.NodeIndex is not null || target.InterfaceName is not null)
            return (null, WorkerFailureCategories.TargetEvidenceMismatch, "Node constraints require an owner path.");
        if (string.IsNullOrWhiteSpace(target.NodeId))
            return (null, WorkerFailureCategories.TargetNotFound, "No node with nodeId was found.");
        var candidates = EnumerateNodes(device).ToArray();
        if (candidates.Any(x => string.IsNullOrWhiteSpace(x.Node.NodeId)))
            return (null, WorkerFailureCategories.WorkerOperationFailed, "Device node identities are unreadable.");
        var matched = candidates.Where(x => IdentitiesMatch(x.Node.NodeId, target.NodeId)).ToArray();
        if (matched.Length != 1) return (null, matched.Length == 0 ? WorkerFailureCategories.TargetNotFound : WorkerFailureCategories.TargetAmbiguous,
            matched.Length == 0 ? "No node with nodeId was found." : "Multiple nodes match this bare nodeId. Use the interfacePath selector returned by read_hardware_config or list_network_objects.");
        var proof = NetworkInterfacePathMatcher.Match(device.Items, matched[0].OwnerPath, i => i.Items, i => i.Name, i => i.PositionNumber, i => i.TypeIdentifier);
        if (!proof.Success) return (null, proof.FailureCategory, proof.Error);
        return SelectNode(device, new NetworkObjectTarget { DeviceName = target.DeviceName, NodeId = target.NodeId,
            InterfacePath = matched[0].OwnerPath.Select(x => new NetworkInterfacePathSegment
            { Name = x.Name, PositionNumber = x.PositionNumber }).ToArray() });
    }

    private static IEnumerable<NodeCandidate> EnumerateNodes(DeviceInfo device) => OrEmpty(device.Items).SelectMany(item => EnumerateItem(item, Array.Empty<NetworkInterfacePathSegmentInfo>()));
    private static IEnumerable<NodeCandidate> EnumerateItem(DeviceItemInfo item, IReadOnlyList<NetworkInterfacePathSegmentInfo> parent)
    {
        var path = parent.Concat(new[] { new NetworkInterfacePathSegmentInfo { Name = item.Name ?? string.Empty, PositionNumber = item.PositionNumber ?? -1 } }).ToArray();
        foreach (var networkInterface in OrEmpty(item.NetworkInterfaces))
            foreach (var node in OrEmpty(networkInterface.Nodes))
                yield return new(path.Select(x => x.Name).ToArray(), path, networkInterface.Name, node);
        foreach (var child in OrEmpty(item.Items))
            foreach (var candidate in EnumerateItem(child, path)) yield return candidate;
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

    private sealed record NodeCandidate(IReadOnlyList<string> Path, IReadOnlyList<NetworkInterfacePathSegmentInfo> OwnerPath, string InterfaceName, NodeInfo Node);

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
