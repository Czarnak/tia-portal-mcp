using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.CommunicationConnections;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Resolves snapshot-scoped selectors without name-search fallback after path selection.</summary>
public static class NetworkObjectSelectorResolver
{
    public static NetworkObjectSelectionResult Resolve(Project project, NetworkObjectSelectorInfo target)
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

    private static NetworkObjectSelectionResult ResolveDeviceItem(Project project, NetworkObjectSelectorInfo target)
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
            Address = ReadOptionalStringAttribute((IEngineeringObject)item, "Address", messages),
            DeviceItemPath = itemMatch.VerifiedPath!.Select(segment => segment.Name).ToList(),
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.DeviceItem,
            item,
            NetworkSelectorFactory.DeviceItem(itemMatch.DeviceName!, itemMatch.VerifiedPath!),
            evidence,
            messages));
    }

    private static NetworkObjectSelectionResult ResolveNetworkInterface(Project project, NetworkObjectSelectorInfo target)
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

    private static NetworkObjectSelectionResult ResolveNode(Project project, NetworkObjectSelectorInfo target)
    {
        if (target.ItemPath is not null && target.NodeIndex is not null)
        {
            return ResolveIndexedNode(project, target);
        }

        var deviceMatch = MatchDevice(project, target.DeviceName);
        if (!deviceMatch.Success)
        {
            return deviceMatch.Failure!;
        }

        var candidates = new List<(NodeCandidate Candidate, string NodeId)>();
        foreach (var candidate in EnumerateNodes(deviceMatch.Value!))
        {
            try
            {
                var nodeId = candidate.Node.NodeId;
                if (string.Equals(nodeId, target.NodeId, StringComparison.Ordinal))
                {
                    candidates.Add((candidate, nodeId));
                }
            }
            catch (EngineeringException)
            {
                // An unreadable identity can never satisfy the selector.
            }
        }

        if (candidates.Count == 0)
        {
            return NotFound($"Device '{deviceMatch.Name}' has no node with nodeId '{target.NodeId}'.");
        }

        if (candidates.Count > 1)
        {
            return Ambiguous($"Device '{deviceMatch.Name}' has multiple nodes with nodeId '{target.NodeId}'.");
        }

        var match = candidates[0];
        var matchedCandidate = match.Candidate;
        var messages = new List<string>();
        var nodeName = ReadOptionalString(() => matchedCandidate.Node.Name, "node name", messages);
        var nodeType = ReadOptionalEnumName(() => matchedCandidate.Node.NodeType, "node type", messages);
        var interfaceName = ReadOptionalStringAttribute(
            (IEngineeringObject)matchedCandidate.NetworkInterface, "Name", messages);
        var interfaceType = ReadOptionalEnumName(
            () => matchedCandidate.NetworkInterface.InterfaceType, "network interface type", messages);
        var interfaceMode = ReadOptionalEnumName(
            () => matchedCandidate.NetworkInterface.InterfaceOperatingMode,
            "network interface operating mode",
            messages);
        var evidence = new NetworkObjectEvidenceInfo
        {
            DeviceItemPath = matchedCandidate.ItemPath,
            InterfaceName = interfaceName,
            InterfaceType = interfaceType,
            InterfaceOperatingMode = interfaceMode,
            NodeName = nodeName,
            NodeType = nodeType,
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.Node,
            matchedCandidate.Node,
            NetworkSelectorFactory.Node(deviceMatch.Name!, match.NodeId),
            evidence,
            messages));
    }

    private static NetworkObjectSelectionResult ResolveIndexedNode(
        Project project,
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

        var node = NodeAt(networkInterface, nodeIndex);
        if (node is null)
        {
            return NotFound($"The selected network interface has no node at index {target.NodeIndex}.");
        }

        string nodeId;
        try
        {
            nodeId = node.NodeId;
        }
        catch (EngineeringException exception)
        {
            return EvidenceMismatch($"Could not verify the selected node identity: {exception.Message}");
        }

        if (!string.Equals(nodeId, target.NodeId, StringComparison.Ordinal))
        {
            return EvidenceMismatch("The resolved node identity does not match the selector evidence.");
        }

        var messages = new List<string>();
        var nodeName = ReadOptionalString(() => node.Name, "node name", messages);
        var nodeType = ReadOptionalEnumName(() => node.NodeType, "node type", messages);
        var interfaceName = ReadOptionalStringAttribute((IEngineeringObject)networkInterface, "Name", messages);
        var interfaceType = ReadOptionalEnumName(
            () => networkInterface.InterfaceType, "network interface type", messages);
        var interfaceMode = ReadOptionalEnumName(
            () => networkInterface.InterfaceOperatingMode, "network interface operating mode", messages);
        var evidence = new NetworkObjectEvidenceInfo
        {
            DeviceItemPath = itemMatch.VerifiedPath!.Select(segment => segment.Name).ToList(),
            InterfaceName = interfaceName,
            InterfaceType = interfaceType,
            InterfaceOperatingMode = interfaceMode,
            NodeName = nodeName,
            NodeType = nodeType,
        };

        return NetworkObjectSelectionResult.Ok(new ResolvedNetworkObject(
            NetworkObjectKinds.Node,
            node,
            NetworkSelectorFactory.Node(
                itemMatch.DeviceName!,
                nodeId,
                itemMatch.VerifiedPath!,
                nodeIndex),
            evidence,
            messages));
    }

    private static NetworkObjectSelectionResult ResolveSubnet(Project project, NetworkObjectSelectorInfo target)
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

    private static NetworkObjectSelectionResult ResolveIoSystem(Project project, NetworkObjectSelectorInfo target)
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
        Project project,
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

    private static DeviceItemMatch MatchDeviceItem(Project project, NetworkObjectSelectorInfo target)
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

    private static Match<Device> MatchDevice(Project project, string? requestedName)
    {
        var matches = new List<(Device Device, string Name)>();
        foreach (Device candidate in project.Devices)
        {
            try
            {
                var candidateName = candidate.Name;
                if (string.Equals(candidateName, requestedName, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add((candidate, candidateName));
                }
            }
            catch (EngineeringException)
            {
                // An unreadable name cannot satisfy the selector.
            }
        }

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

    private static Match<Subnet> MatchSubnet(Project project, string? subnetId)
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
        EnumerateNodes(device.DeviceItems, new List<string>(), result);
        return result;
    }

    private static void EnumerateNodes(
        DeviceItemComposition items,
        IReadOnlyList<string> parentPath,
        List<NodeCandidate> result)
    {
        foreach (DeviceItem item in items)
        {
            var path = parentPath.ToList();
            try
            {
                path.Add(item.Name);
            }
            catch (EngineeringException)
            {
                path.Add(string.Empty);
            }

            try
            {
                var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
                if (networkInterface is not null)
                {
                    foreach (Node node in networkInterface.Nodes)
                    {
                        result.Add(new NodeCandidate(networkInterface, node, path));
                    }
                }
            }
            catch (EngineeringException)
            {
                // A failed interface does not suppress later device items.
            }

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
        public NodeCandidate(NetworkInterface networkInterface, Node node, IReadOnlyList<string> itemPath)
        {
            NetworkInterface = networkInterface;
            Node = node;
            ItemPath = itemPath.ToList();
        }

        public NetworkInterface NetworkInterface { get; }
        public Node Node { get; }
        public List<string> ItemPath { get; }
    }
}
