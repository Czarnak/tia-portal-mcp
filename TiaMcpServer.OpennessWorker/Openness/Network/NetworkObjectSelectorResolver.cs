using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.CommunicationConnections;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

using Project = Siemens.Engineering.Project;

/// <summary>Resolves snapshot-scoped selectors without name-search fallback after path selection.</summary>
public static class NetworkObjectSelectorResolver
{
    // The temporary qualification probe needs the same fresh indexed-path comparison
    // without rebuilding a public selector, whose factory excludes empty type identifiers.
    internal static DeviceItem? ResolveQualificationDeviceItem(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        if (target.Kind != NetworkObjectKinds.DeviceItem) return null;
        var match = MatchDeviceItem(project, target);
        return match.Item;
    }

    public static NetworkObjectSelectionResult Resolve(ProjectBase project, NetworkObjectSelectorInfo target)
        => target.Kind switch
        {
            NetworkObjectKinds.DeviceItem => ResolveDeviceItem(project, target),
            NetworkObjectKinds.NetworkInterface => ResolveNetworkInterface(project, target),
            NetworkObjectKinds.Node => ResolveNode(project, target),
            NetworkObjectKinds.Subnet => ResolveSubnet(project, target),
            NetworkObjectKinds.IoSystem => ResolveIoSystem(project, target),
            NetworkObjectKinds.CommunicationConnection => ResolveCommunicationConnection(project, target),
            _ => NetworkObjectSelectionResult.Fail(
                WorkerFailureCategories.TargetKindUnsupported,
                $"Network object kind '{target.Kind ?? "(null)"}' is not supported by this inspector."),
        };

    private static NetworkObjectSelectionResult ResolveDeviceItem(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        var itemMatch = MatchDeviceItem(project, target);
        if (!itemMatch.Success)
        {
            return itemMatch.Failure!;
        }

        var item = itemMatch.Item!;
        var messages = new List<string>();
        var leafEvidence = itemMatch.VerifiedPath![itemMatch.VerifiedPath.Count - 1];
        var evidence = new NetworkObjectEvidenceInfo
        {
            Name = leafEvidence.Name,
            TypeIdentifier = leafEvidence.TypeIdentifier,
            PositionNumber = leafEvidence.PositionNumber,
            // DeviceItem has no 'Address' attribute; I/O addresses are in ioDetails.addresses.
            Address = null,
            DeviceItemPath = itemMatch.VerifiedPath!.Select(segment => segment.Name).ToList(),
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.DeviceItem,
            item,
            NetworkSelectorFactory.DeviceItem(itemMatch.DeviceName!, itemMatch.VerifiedPath!),
            evidence,
            messages));
    }

    private static NetworkObjectSelectionResult ResolveNetworkInterface(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        var itemMatch = MatchDeviceItem(project, target);
        if (!itemMatch.Success)
        {
            return itemMatch.Failure!;
        }

        NetworkInterface? networkInterface;
        try
        {
            networkInterface = ((IEngineeringServiceProvider)itemMatch.Item!).GetService<NetworkInterface>();
        }
        catch (EngineeringException exception)
        {
            return EvidenceMismatch($"Could not resolve the selected item's network interface: {exception.Message}");
        }

        if (networkInterface is null)
        {
            return NotFound("The selected device item does not expose a network interface.");
        }

        var messages = new List<string>();
        var nameRead = TryReadStringAttribute((IEngineeringObject)networkInterface, "Name", out var interfaceName, out var nameError);
        var typeRead = TryReadEnumName(() => networkInterface.InterfaceType, out var interfaceType, out var typeError);
        var modeRead = TryReadEnumName(() => networkInterface.InterfaceOperatingMode, out var interfaceMode, out var modeError);

        if (target.InterfaceName is not null
            && (!nameRead || !string.Equals(interfaceName, target.InterfaceName, StringComparison.Ordinal)))
        {
            return EvidenceMismatch("The resolved network interface name does not match the selector evidence.");
        }

        if (target.InterfaceType is not null
            && (!typeRead || !string.Equals(interfaceType, target.InterfaceType, StringComparison.Ordinal)))
        {
            return EvidenceMismatch("The resolved network interface type does not match the selector evidence.");
        }

        if (target.InterfaceOperatingMode is not null
            && (!modeRead || !string.Equals(interfaceMode, target.InterfaceOperatingMode, StringComparison.Ordinal)))
        {
            return EvidenceMismatch("The resolved network interface operating mode does not match the selector evidence.");
        }

        AddReadMessage(messages, "network interface name", nameError);
        AddReadMessage(messages, "network interface type", typeError);
        AddReadMessage(messages, "network interface operating mode", modeError);

        var verifiedTarget = NetworkSelectorFactory.NetworkInterface(
            itemMatch.DeviceName!,
            itemMatch.VerifiedPath!,
            interfaceName,
            interfaceType,
            interfaceMode);
        var evidence = new NetworkObjectEvidenceInfo
        {
            DeviceItemPath = itemMatch.VerifiedPath!.Select(segment => segment.Name).ToList(),
            InterfaceName = interfaceName,
            InterfaceType = interfaceType,
            InterfaceOperatingMode = interfaceMode,
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.NetworkInterface,
            networkInterface,
            verifiedTarget,
            evidence,
            messages));
    }

    public static NetworkObjectSelectionResult ResolveNode(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        if (target.InterfacePath is not null && target.ItemPath is not null)
            return EvidenceMismatch("A node selector cannot contain both owner path forms.");
        if (target.InterfacePath is null && target.ItemPath is null && (target.NodeIndex is not null || target.InterfaceName is not null))
            return EvidenceMismatch("Node index and interface name constraints require an owner path.");
        if (target.ItemPath is not null && target.NodeIndex is null)
            return EvidenceMismatch("A legacy item path requires a node index.");
        if (target.InterfacePath is not null)
            return ResolveQualifiedNode(project, target);
        if (target.ItemPath is not null && target.NodeIndex is not null)
        {
            return ResolveIndexedNode(project, target);
        }

        var deviceMatch = MatchDevice(project, target.DeviceName);
        if (!deviceMatch.Success)
        {
            return deviceMatch.Failure!;
        }

        NetworkInterfacePathMatch<NodeCandidate> match;
        try
        {
            match = NetworkNodeReadSelectorBuilder.MatchNode(EnumerateNodes(deviceMatch.Value!), target.NodeId,
                null, candidate => candidate.Node.NodeId);
        }
        catch (Exception)
        {
            return EvidenceMismatch("The device's full node namespace could not be read completely.");
        }
        if (!match.Success) return NetworkObjectSelectionResult.Fail(match.FailureCategory!,
            match.FailureCategory == WorkerFailureCategories.TargetAmbiguous
                ? "Multiple nodes match this bare nodeId. Use the interfacePath selector returned by read_hardware_config or list_network_objects."
                : match.Error!);
        var ownerProof = NetworkInterfacePathMatcher.Match(deviceMatch.Value!.DeviceItems.Cast<DeviceItem>(), match.Item!.OwnerPath,
            item => item.DeviceItems.Cast<DeviceItem>(), item => item.Name, item => item.PositionNumber, item => item.TypeIdentifier);
        if (!ownerProof.Success) return NetworkObjectSelectionResult.Fail(ownerProof.FailureCategory!, ownerProof.Error!);
        return ResolveQualifiedNode(project, NetworkSelectorFactory.QualifiedNode(deviceMatch.Name!, target.NodeId!,
            match.Item!.OwnerPath));
    }

    private static NetworkObjectSelectionResult ResolveQualifiedNode(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        var deviceMatch = MatchDevice(project, target.DeviceName);
        if (!deviceMatch.Success) return deviceMatch.Failure!;
        NetworkInterfacePathMatch<DeviceItem> owner;
        try
        {
            owner = NetworkInterfacePathMatcher.Match(deviceMatch.Value!.DeviceItems.Cast<DeviceItem>(), target.InterfacePath!,
                item => item.DeviceItems.Cast<DeviceItem>(), item => item.Name, item => item.PositionNumber, item => item.TypeIdentifier);
        }
        catch (Exception) { return EvidenceMismatch("The interface owner root collection could not be read."); }
        if (!owner.Success) return NetworkObjectSelectionResult.Fail(owner.FailureCategory!, owner.Error!);
        NetworkInterface? networkInterface;
        try { networkInterface = ((IEngineeringServiceProvider)owner.Item!).GetService<NetworkInterface>(); }
        catch (Exception) { return EvidenceMismatch("The selected owner's network interface could not be read."); }
        if (networkInterface is null) return NotFound("The selected owner has no network interface service.");
        var messages = new List<string>();
        var interfaceName = ReadOptionalStringAttribute((IEngineeringObject)networkInterface, "Name", messages);
        if (target.InterfaceName is not null && !string.Equals(interfaceName, target.InterfaceName, StringComparison.Ordinal))
            return EvidenceMismatch("The selected interface name does not match the supplied constraint.");
        NetworkInterfacePathMatch<Node> node;
        try { node = NetworkNodeReadSelectorBuilder.MatchNode(networkInterface.Nodes.Cast<Node>(), target.NodeId, target.NodeIndex, x => x.NodeId); }
        catch (Exception) { return EvidenceMismatch("The selected interface node collection could not be read."); }
        if (!node.Success) return NetworkObjectSelectionResult.Fail(node.FailureCategory!, node.Error!);
        var evidence = new NetworkObjectEvidenceInfo
        {
            DeviceItemPath = target.InterfacePath!.Select(x => x.Name).ToList(),
            InterfaceName = interfaceName,
            InterfaceType = ReadOptionalEnumName(() => networkInterface.InterfaceType, "network interface type", messages),
            InterfaceOperatingMode = ReadOptionalEnumName(() => networkInterface.InterfaceOperatingMode, "network interface operating mode", messages),
            NodeName = ReadOptionalString(() => node.Item!.Name, "node name", messages),
            NodeType = ReadOptionalEnumName(() => node.Item!.NodeType, "node type", messages),
        };
        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(NetworkObjectKinds.Node, node.Item!,
            NetworkSelectorFactory.QualifiedNode(deviceMatch.Name!, target.NodeId!, target.InterfacePath!,
                string.IsNullOrWhiteSpace(interfaceName) ? null : interfaceName, target.NodeIndex),
            evidence, messages, networkInterface));
    }

    private static NetworkObjectSelectionResult ResolveIndexedNode(
        ProjectBase project,
        NetworkObjectSelectorInfo target)
    {
        if (target.NodeIndex is not int nodeIndex)
        {
            return NotFound("The node selector has no node index.");
        }

        var itemMatch = MatchDeviceItem(project, target);
        if (!itemMatch.Success)
        {
            return itemMatch.Failure!;
        }

        // The legacy index/name/position/type path is verified first. The node index remains
        // a consistency constraint after unique identity selection in that owner's namespace.
        return ResolveQualifiedNode(project, NetworkSelectorFactory.QualifiedNode(itemMatch.DeviceName!, target.NodeId!,
            itemMatch.VerifiedPath!.Select(x => new NetworkInterfacePathSegmentInfo
            { Name = x.Name, PositionNumber = x.PositionNumber, TypeIdentifier = x.TypeIdentifier }).ToList(), target.InterfaceName, nodeIndex));
    }

    private static NetworkObjectSelectionResult ResolveSubnet(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        var subnetMatch = MatchSubnet(project, target.SubnetId);
        if (!subnetMatch.Success)
        {
            return subnetMatch.Failure!;
        }

        var subnet = subnetMatch.Value!;
        var messages = new List<string>();
        var evidence = new NetworkObjectEvidenceInfo
        {
            SubnetName = ReadOptionalString(() => subnet.Name, "subnet name", messages),
            NetworkType = ReadOptionalEnumName(() => subnet.NetType, "subnet network type", messages),
            TypeIdentifier = ReadOptionalString(() => subnet.TypeIdentifier, "subnet type identifier", messages),
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.Subnet,
            subnet,
            NetworkSelectorFactory.Subnet(subnetMatch.Identity!),
            evidence,
            messages));
    }

    private static NetworkObjectSelectionResult ResolveIoSystem(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        var subnetMatch = MatchSubnet(project, target.SubnetId);
        if (!subnetMatch.Success)
        {
            return subnetMatch.Failure!;
        }

        var candidates = new List<(IoSystem IoSystem, int Number, int Index)>();
        var candidateIndex = 0;
        foreach (IoSystem candidate in subnetMatch.Value!.IoSystems)
        {
            var currentIndex = candidateIndex++;
            try
            {
                if (target.IoSystemIndex is not null && currentIndex != target.IoSystemIndex)
                {
                    continue;
                }

                var number = candidate.Number;
                if (number != target.Number)
                {
                    continue;
                }

                if (target.IoSystemName is not null
                    && !string.Equals(candidate.Name, target.IoSystemName, StringComparison.Ordinal))
                {
                    continue;
                }

                candidates.Add((candidate, number, currentIndex));
            }
            catch (EngineeringException)
            {
                // An unreadable identity can never satisfy the selector.
            }
        }

        if (candidates.Count == 0)
        {
            return NotFound($"No IO system with number {target.Number} exists on subnet '{target.SubnetId}'.");
        }

        if (candidates.Count > 1)
        {
            return Ambiguous($"Multiple IO systems report number {target.Number} on subnet '{target.SubnetId}'.");
        }

        var ioSystem = candidates[0].IoSystem;
        var ioSystemNumber = candidates[0].Number;
        var ioSystemIndex = candidates[0].Index;
        var messages = new List<string>();
        var evidence = new NetworkObjectEvidenceInfo
        {
            SubnetName = ReadOptionalString(() => subnetMatch.Value!.Name, "subnet name", messages),
            NetworkType = ReadOptionalEnumName(() => subnetMatch.Value!.NetType, "subnet network type", messages),
            IoSystemName = ReadOptionalString(() => ioSystem.Name, "IO system name", messages),
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.IoSystem,
            ioSystem,
            NetworkSelectorFactory.IoSystem(
                subnetMatch.Identity!,
                ioSystemNumber,
                ioSystemIndex,
                target.IoSystemName ?? evidence.IoSystemName),
            evidence,
            messages));
    }

    private static NetworkObjectSelectionResult ResolveCommunicationConnection(
        ProjectBase project,
        NetworkObjectSelectorInfo target)
    {
        var itemMatch = MatchDeviceItem(project, target);
        if (!itemMatch.Success)
        {
            return itemMatch.Failure!;
        }

        CommunicationManagement? communicationManagement;
        try
        {
            communicationManagement = ((IEngineeringServiceProvider)itemMatch.Item!)
                .GetService<CommunicationManagement>();
        }
        catch (EngineeringException exception)
        {
            return EvidenceMismatch(
                $"Could not resolve communication connections on the selected device item: {exception.Message}");
        }

        if (communicationManagement is null)
        {
            return NotFound("The selected device item does not expose communication connections.");
        }

        Connection? connection;
        try
        {
            connection = ConnectionAt(
                communicationManagement.Connections,
                target.ConnectionIndex ?? -1);
        }
        catch (EngineeringException exception)
        {
            return EvidenceMismatch(
                $"Could not resolve the recorded communication connection index: {exception.Message}");
        }
        if (connection is null)
        {
            return NotFound(
                $"The selected device item has no communication connection at index {target.ConnectionIndex}.");
        }

        if (!CommunicationConnectionReader.TryReadConnectionType(
                connection,
                out var connectionType,
                out var typeDiagnostic)
            || !string.Equals(connectionType, target.ConnectionType, StringComparison.Ordinal))
        {
            return EvidenceMismatch(
                typeDiagnostic
                ?? "The communication connection at the recorded index no longer matches its type evidence.");
        }

        if (!CommunicationConnectionReader.TryReadIdentityString(
                connection,
                connectionType!,
                "LocalConnectionName",
                out var localConnectionName,
                out var nameDiagnostic)
            || !string.Equals(
                localConnectionName,
                target.LocalConnectionName,
                StringComparison.Ordinal))
        {
            return EvidenceMismatch(
                nameDiagnostic
                ?? "The communication connection at the recorded index no longer matches its local-name evidence.");
        }

        var messages = new List<string>();
        string? localConnectionId = null;
        var requiresLocalConnectionId =
            ConnectionModeledAttributeCatalog.RequiresLocalConnectionId(connectionType!);
        if (requiresLocalConnectionId)
        {
            if (string.IsNullOrWhiteSpace(target.LocalConnectionId))
            {
                return EvidenceMismatch(
                    "The selected communication connection type requires local-ID evidence.");
            }

            var idRead = CommunicationConnectionReader.TryReadIdentityString(
                connection,
                connectionType!,
                "LocalConnectionId",
                out localConnectionId,
                out var idDiagnostic);
            if (!idRead
                || !string.Equals(
                    localConnectionId,
                    target.LocalConnectionId,
                    StringComparison.Ordinal))
            {
                return EvidenceMismatch(
                    idDiagnostic
                    ?? "The communication connection at the recorded index no longer matches its local-ID evidence.");
            }

        }
        else if (target.LocalConnectionId is not null)
        {
            return EvidenceMismatch(
                "The resolved communication connection type does not expose local-ID evidence.");
        }

        var isValid = ReadOptionalBool(() => connection.IsValid, "connection validity", messages);
        var partnerName = ReadOptionalString(
            () => connection.PartnerTarget?.Name,
            "connection partner target name",
            messages);
        var summary = CommunicationConnectionSelectorFactory.Create(
            itemMatch.DeviceName,
            itemMatch.VerifiedPath,
            target.ConnectionIndex!.Value,
            connectionType,
            localConnectionName,
            localConnectionId,
            partnerName,
            isValid ?? false);
        if (!summary.Selectable || summary.Selector is null)
        {
            return EvidenceMismatch(
                "The resolved communication connection no longer provides complete selector evidence.");
        }

        var evidence = new NetworkObjectEvidenceInfo
        {
            DeviceItemPath = itemMatch.VerifiedPath!.Select(segment => segment.Name).ToList(),
            ConnectionIsValid = isValid,
            LocalEndpointName = ReadOptionalString(
                () => connection.LocalInterface?.Name,
                "connection local endpoint name",
                messages),
            PartnerEndpointName = ReadOptionalString(
                () => connection.PartnerInterface?.Name,
                "connection partner endpoint name",
                messages),
            LocalSubnetName = ReadOptionalString(
                () => connection.LocalSubnetName,
                "connection local subnet name",
                messages),
            PartnerSubnetName = ReadOptionalString(
                () => connection.PartnerSubnetName,
                "connection partner subnet name",
                messages),
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.CommunicationConnection,
            connection,
            summary.Selector,
            evidence,
            messages));
    }

    private static DeviceItemMatch MatchDeviceItem(ProjectBase project, NetworkObjectSelectorInfo target)
    {
        var deviceMatch = MatchDevice(project, target.DeviceName);
        if (!deviceMatch.Success)
        {
            return DeviceItemMatch.Fail(deviceMatch.Failure!);
        }

        if (target.ItemPath is null || target.ItemPath.Count == 0)
        {
            return DeviceItemMatch.Fail(NotFound("The device item selector has no item path."));
        }

        var verifiedPath = new List<DeviceItemPathSegmentInfo>();
        DeviceItemComposition siblings = deviceMatch.Value!.DeviceItems;
        DeviceItem? selected = null;
        for (var depth = 0; depth < target.ItemPath.Count; depth++)
        {
            var requestedSegment = target.ItemPath[depth];
            selected = ItemAt(siblings, requestedSegment.Index);
            if (selected is null)
            {
                return DeviceItemMatch.Fail(NotFound(
                    $"Device item path segment {depth} has no sibling at index {requestedSegment.Index}."));
            }

            string name;
            string typeIdentifier;
            int positionNumber;
            try
            {
                name = selected.Name;
                typeIdentifier = selected.TypeIdentifier;
                positionNumber = selected.PositionNumber;
            }
            catch (EngineeringException exception)
            {
                return DeviceItemMatch.Fail(EvidenceMismatch(
                    $"Could not verify device item path segment {depth}: {exception.Message}"));
            }

            if (!string.Equals(name, requestedSegment.Name, StringComparison.Ordinal)
                || !string.Equals(typeIdentifier, requestedSegment.TypeIdentifier, StringComparison.Ordinal)
                || requestedSegment.PositionNumber != positionNumber)
            {
                return DeviceItemMatch.Fail(EvidenceMismatch(
                    $"Device item path segment {depth} no longer matches its name, type identifier, or position evidence."));
            }

            verifiedPath.Add(new DeviceItemPathSegmentInfo
            {
                Index = requestedSegment.Index,
                Name = name,
                TypeIdentifier = typeIdentifier,
                PositionNumber = positionNumber,
            });
            siblings = selected.DeviceItems;
        }

        return DeviceItemMatch.Ok(deviceMatch.Name!, selected!, verifiedPath);
    }

    private static Match<Device> MatchDevice(ProjectBase project, string? requestedName)
    {
        try
        {
            foreach (var candidate in ProjectDeviceEnumerator.Enumerate(project))
                if (string.IsNullOrWhiteSpace(candidate.Name))
                    return Match<Device>.Fail(EvidenceMismatch("The device-name namespace has unreadable required identity evidence."));
        }
        catch (Exception)
        {
            return Match<Device>.Fail(EvidenceMismatch("The device-name namespace could not be read completely."));
        }
        var matches = ProjectDeviceNameMatcher.FindMatches(project, requestedName);

        if (matches.Count == 0)
        {
            return Match<Device>.Fail(NotFound($"No device named '{requestedName}' was found."));
        }

        if (matches.Count > 1)
        {
            return Match<Device>.Fail(Ambiguous($"Multiple devices are named '{requestedName}'."));
        }

        return Match<Device>.Ok(matches[0].Device, matches[0].Name, null);
    }

    private static Match<Subnet> MatchSubnet(ProjectBase project, string? subnetId)
    {
        var matches = new List<(Subnet Subnet, string Identity)>();
        foreach (Subnet candidate in project.Subnets)
        {
            if (TryReadStringAttribute((IEngineeringObject)candidate, "SubnetId", out var identity, out _)
                && string.Equals(identity, subnetId, StringComparison.Ordinal))
            {
                matches.Add((candidate, identity!));
            }
        }

        if (matches.Count == 0)
        {
            return Match<Subnet>.Fail(NotFound($"No subnet with subnetId '{subnetId}' was found."));
        }

        if (matches.Count > 1)
        {
            return Match<Subnet>.Fail(Ambiguous($"Multiple subnets report subnetId '{subnetId}'."));
        }

        return Match<Subnet>.Ok(matches[0].Subnet, null, matches[0].Identity);
    }

    private static DeviceItem? ItemAt(DeviceItemComposition items, int index)
    {
        if (index < 0)
        {
            return null;
        }

        var current = 0;
        foreach (DeviceItem item in items)
        {
            if (current == index)
            {
                return item;
            }

            current++;
        }

        return null;
    }

    private static Node? NodeAt(NetworkInterface networkInterface, int index)
    {
        if (index < 0)
        {
            return null;
        }

        var current = 0;
        foreach (Node node in networkInterface.Nodes)
        {
            if (current == index)
            {
                return node;
            }

            current++;
        }

        return null;
    }

    private static Connection? ConnectionAt(ConnectionComposition connections, int index)
    {
        if (index < 0)
        {
            return null;
        }

        var current = 0;
        foreach (Connection connection in connections)
        {
            if (current == index)
            {
                return connection;
            }

            current++;
        }

        return null;
    }

    private static IEnumerable<NodeCandidate> EnumerateNodes(Device device)
    {
        var result = new List<NodeCandidate>();
        EnumerateNodes(device.DeviceItems, new List<NetworkInterfacePathSegmentInfo>(), result);
        return result;
    }

    private static void EnumerateNodes(DeviceItemComposition items,
        IReadOnlyList<NetworkInterfacePathSegmentInfo> parentPath, List<NodeCandidate> result)
    {
        foreach (DeviceItem item in items)
        {
            // Optional type evidence is independent of required name/position and enumeration.
            string? type = null;
            try { type = item.TypeIdentifier; } catch (EngineeringException) { }
            var path = parentPath.Concat(new[] { new NetworkInterfacePathSegmentInfo
            { Name = item.Name, PositionNumber = item.PositionNumber,
                TypeIdentifier = string.IsNullOrWhiteSpace(type) ? null : type } }).ToList();
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
                foreach (Node node in networkInterface.Nodes)
                    result.Add(new NodeCandidate(networkInterface, node, path));
            EnumerateNodes(item.DeviceItems, path, result);
        }
    }
    private static string? ReadOptionalStringAttribute(
        IEngineeringObject value,
        string name,
        List<string> messages)
    {
        var success = TryReadStringAttribute(value, name, out var result, out var error);
        AddReadMessage(messages, name, error);
        return success ? result : null;
    }

    private static bool TryReadStringAttribute(
        IEngineeringObject value,
        string name,
        out string? result,
        out string? error)
    {
        try
        {
            var raw = value.GetAttribute(name);
            result = raw as string;
            error = raw is null || raw is string
                ? null
                : $"returned unsupported CLR type '{raw.GetType().FullName}'.";
            return error is null;
        }
        catch (Exception exception)
        {
            result = null;
            error = exception.Message;
            return false;
        }
    }

    private static string? ReadOptionalString(
        Func<string?> reader,
        string description,
        List<string> messages)
    {
        try
        {
            return reader();
        }
        catch (Exception exception)
        {
            AddReadMessage(messages, description, exception.Message);
            return null;
        }
    }

    private static bool? ReadOptionalBool(
        Func<bool> reader,
        string description,
        List<string> messages)
    {
        try
        {
            return reader();
        }
        catch (Exception exception)
        {
            AddReadMessage(messages, description, exception.Message);
            return null;
        }
    }

    private static string? ReadOptionalEnumName<TEnum>(
        Func<TEnum> reader,
        string description,
        List<string> messages)
        where TEnum : struct, Enum
    {
        if (TryReadEnumName(reader, out var result, out var error))
        {
            return result;
        }

        AddReadMessage(messages, description, error);
        return null;
    }

    private static bool TryReadEnumName<TEnum>(
        Func<TEnum> reader,
        out string? result,
        out string? error)
        where TEnum : struct, Enum
    {
        try
        {
            var value = reader();
            result = Enum.Format(typeof(TEnum), value, "G");
            error = null;
            return true;
        }
        catch (Exception exception)
        {
            result = null;
            error = exception.Message;
            return false;
        }
    }

    private static void AddReadMessage(List<string> messages, string description, string? error)
    {
        if (error is not null)
        {
            messages.Add($"Could not read {description}: {error}");
        }
    }

    private static NetworkObjectSelectionResult NotFound(string error)
        => NetworkObjectSelectionResult.Fail(WorkerFailureCategories.TargetNotFound, error);

    private static NetworkObjectSelectionResult Ambiguous(string error)
        => NetworkObjectSelectionResult.Fail(WorkerFailureCategories.TargetAmbiguous, error);

    private static NetworkObjectSelectionResult EvidenceMismatch(string error)
        => NetworkObjectSelectionResult.Fail(WorkerFailureCategories.TargetEvidenceMismatch, error);

    private sealed class DeviceItemMatch
    {
        private DeviceItemMatch(
            DeviceItem? item,
            string? deviceName,
            IReadOnlyList<DeviceItemPathSegmentInfo>? verifiedPath,
            NetworkObjectSelectionResult? failure)
        {
            Item = item;
            DeviceName = deviceName;
            VerifiedPath = verifiedPath;
            Failure = failure;
        }

        public bool Success => Item is not null;
        public DeviceItem? Item { get; }
        public string? DeviceName { get; }
        public IReadOnlyList<DeviceItemPathSegmentInfo>? VerifiedPath { get; }
        public NetworkObjectSelectionResult? Failure { get; }

        public static DeviceItemMatch Ok(
            string deviceName,
            DeviceItem item,
            IReadOnlyList<DeviceItemPathSegmentInfo> verifiedPath)
            => new(item, deviceName, verifiedPath, null);

        public static DeviceItemMatch Fail(NetworkObjectSelectionResult failure)
            => new(null, null, null, failure);
    }

    private sealed class Match<T> where T : class
    {
        private Match(T? value, string? name, string? identity, NetworkObjectSelectionResult? failure)
        {
            Value = value;
            Name = name;
            Identity = identity;
            Failure = failure;
        }

        public bool Success => Value is not null;
        public T? Value { get; }
        public string? Name { get; }
        public string? Identity { get; }
        public NetworkObjectSelectionResult? Failure { get; }

        public static Match<T> Ok(T value, string? name, string? identity)
            => new(value, name, identity, null);

        public static Match<T> Fail(NetworkObjectSelectionResult failure)
            => new(null, null, null, failure);
    }

    private sealed class NodeCandidate
    {
        public NodeCandidate(NetworkInterface networkInterface, Node node, IReadOnlyList<NetworkInterfacePathSegmentInfo> ownerPath)
        {
            NetworkInterface = networkInterface;
            Node = node;
            OwnerPath = ownerPath.ToList();
        }

        public NetworkInterface NetworkInterface { get; }
        public Node Node { get; }
        public List<NetworkInterfacePathSegmentInfo> OwnerPath { get; }
    }
}
