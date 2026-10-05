using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Production implementation of the Phase 4 subnet lifecycle operations (<c>create_subnet</c>,
/// <c>update_subnet</c>, <c>delete_subnet</c>). Each entry point opens exactly one Openness
/// transaction, performs every requested mutation inside it, and verifies operation-specific
/// postconditions — including that the root device count never changes — after the transaction
/// commits and is disposed. This file is a distinct production implementation; it does not call,
/// alias, or share code with <see cref="SubnetLifecycleMutationProbeService"/>, which remains an
/// internal-only evidence probe.
/// </summary>
internal static class SubnetLifecycleService
{
    private const string EthernetTypeIdentifier = "System:Subnet.Ethernet";
    private const string ProfibusTypeIdentifier = "System:Subnet.Profibus";

    public static SubnetLifecycleResultInfo Create(
        TiaPortal tiaPortal,
        Project project,
        string name,
        string networkType,
        int? highestAddress,
        string? transmissionSpeed)
    {
        var typeIdentifier = ResolveTypeIdentifier(networkType);
        var deviceCountBefore = project.Devices.Count;

        string? createdSubnetId;
        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: create_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "create_subnet"))
        {
            var subnet = project.Subnets.Create(typeIdentifier, name);
            ApplyProfibusAttributes(subnet, highestAddress, transmissionSpeed);
            createdSubnetId = ReadSubnetId(subnet);
            transaction.CommitOnDispose();
        }

        if (string.IsNullOrWhiteSpace(createdSubnetId))
            throw PostconditionFailed("create_subnet", "The create committed but its identity was unreadable. Inspect the project before retrying.");
        var result = new SubnetLifecycleResultInfo
        {
            SubnetId = createdSubnetId!,
            Name = name,
            NetworkDeviceCount = deviceCountBefore,
        };
        result.Verification = NetworkMutationVerifier.VerifySubnet(project, new WorkerRequest
        {
            Method = "create_subnet", SubnetName = name, SubnetNetworkType = networkType,
            SubnetHighestAddress = highestAddress, SubnetTransmissionSpeed = transmissionSpeed,
        }, result, deviceCountBefore, Array.Empty<NetworkNodeIdentityInfo>());
        return result;
    }

    public static SubnetLifecycleResultInfo Update(
        TiaPortal tiaPortal,
        Project project,
        string subnetId,
        string? name,
        int? highestAddress,
        string? transmissionSpeed)
    {
        // Current-type applicability requires an Openness read of the exact target, so an
        // inapplicable PROFIBUS-only field is rejected here, before any transaction is opened.
        var currentTypeIdentifier = ResolveCurrentTypeIdentifierOrThrow(
            ResolveExactSubnetOrThrow(project, subnetId, "update_subnet"),
            subnetId);
        if ((highestAddress is not null || transmissionSpeed is not null)
            && !string.Equals(currentTypeIdentifier, ProfibusTypeIdentifier, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"Subnet '{subnetId}' is not a PROFIBUS subnet. HighestAddress and TransmissionSpeed "
                + "are not applicable and were rejected before any mutation was attempted.");
        }

        var deviceCountBefore = project.Devices.Count;
        var capturedName = ReadRequiredSubnetNameOrThrow(ResolveExactSubnetOrThrow(project, subnetId, "update_subnet"), subnetId);

        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: update_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "update_subnet"))
        {
            var subnet = ResolveExactSubnetOrThrow(project, subnetId, "update_subnet");
            if (name is not null)
            {
                subnet.Name = name;
            }

            ApplyProfibusAttributes(subnet, highestAddress, transmissionSpeed);
            transaction.CommitOnDispose();
        }

        var result = new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = name ?? capturedName,
            NetworkDeviceCount = deviceCountBefore,
        };
        result.Verification = NetworkMutationVerifier.VerifySubnet(project, new WorkerRequest
        {
            Method = "update_subnet", SubnetId = subnetId, SubnetName = name,
            SubnetHighestAddress = highestAddress, SubnetTransmissionSpeed = transmissionSpeed,
        }, result, deviceCountBefore, Array.Empty<NetworkNodeIdentityInfo>());
        return result;
    }

    public static SubnetLifecycleResultInfo Delete(
        TiaPortal tiaPortal,
        Project project,
        string subnetId)
    {
        int deviceCountBefore;
        string capturedName;
        IReadOnlyList<NetworkNodeIdentityInfo> affectedNodes;

        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: delete_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "delete_subnet"))
        {
            var subnet = ResolveExactSubnetOrThrow(project, subnetId, "delete_subnet");
            _ = ResolveCurrentTypeIdentifierOrThrow(subnet, subnetId);
            capturedName = ReadRequiredSubnetNameOrThrow(subnet, subnetId);
            // Capture reliable exact identities immediately before mutation. Unknown inventory
            // is not an empty inventory and cannot establish preservation after deletion.
            affectedNodes = NetworkMutationVerifier.CaptureAffectedNodes(subnet);
            deviceCountBefore = project.Devices.Count;
            subnet.Delete();
            transaction.CommitOnDispose();
        }

        var result = new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = capturedName,
            NetworkDeviceCount = deviceCountBefore,
        };
        result.Verification = NetworkMutationVerifier.VerifySubnet(project,
            new WorkerRequest { Method = "delete_subnet", SubnetId = subnetId }, result, deviceCountBefore, affectedNodes);
        return result;
    }

    /// <summary>
    /// Sets HighestAddress through <see cref="IEngineeringObject.SetAttribute"/>. TransmissionSpeed
    /// is never bound to a guessed Siemens enum type: the current attribute value is read first, and
    /// the requested symbol is parsed case-sensitively against that value's own CLR type.
    /// </summary>
    private static void ApplyProfibusAttributes(Subnet subnet, int? highestAddress, string? transmissionSpeed)
    {
        var engineeringObject = (IEngineeringObject)subnet;

        if (highestAddress is not null)
        {
            engineeringObject.SetAttribute("HighestAddress", highestAddress.Value);
        }

        if (transmissionSpeed is not null)
        {
            var currentValue = engineeringObject.GetAttribute("TransmissionSpeed")
                ?? throw new WorkerOperationException(
                    WorkerFailureCategories.WorkerOperationFailed,
                    "TransmissionSpeed returned null; its enum type could not be determined.");
            var requestedValue = Enum.Parse(currentValue.GetType(), transmissionSpeed, ignoreCase: false);
            engineeringObject.SetAttribute("TransmissionSpeed", requestedValue);
        }
    }

    private static string ResolveTypeIdentifier(string networkType)
    {
        if (string.Equals(networkType, SubnetLifecycleContract.Ethernet, StringComparison.Ordinal))
        {
            return EthernetTypeIdentifier;
        }

        if (string.Equals(networkType, SubnetLifecycleContract.Profibus, StringComparison.Ordinal))
        {
            return ProfibusTypeIdentifier;
        }

        throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            $"NetworkType '{networkType}' is not supported. Valid values: "
            + $"{SubnetLifecycleContract.Ethernet}, {SubnetLifecycleContract.Profibus}.");
    }

    /// <summary>
    /// Reads the target's current type identifier and fails closed — never falling back to a
    /// guess — when it is unreadable or outside the two supported types.
    /// </summary>
    private static string ResolveCurrentTypeIdentifierOrThrow(Subnet subnet, string subnetId)
    {
        string? typeIdentifier;
        try
        {
            typeIdentifier = subnet.TypeIdentifier;
        }
        catch (EngineeringException)
        {
            typeIdentifier = null;
        }

        if (typeIdentifier is null
            || (!string.Equals(typeIdentifier, EthernetTypeIdentifier, StringComparison.Ordinal)
                && !string.Equals(typeIdentifier, ProfibusTypeIdentifier, StringComparison.Ordinal)))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetKindUnsupported,
                $"Subnet '{subnetId}' has an unavailable or unsupported network type. Only Ethernet "
                + "and PROFIBUS subnets are supported.");
        }

        return typeIdentifier;
    }

    private static string ReadRequiredSubnetNameOrThrow(Subnet subnet, string subnetId)
    {
        string? name;
        try
        {
            name = subnet.Name;
        }
        catch (EngineeringException)
        {
            name = null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw PostconditionFailed(
                "delete_subnet",
                $"Subnet '{subnetId}' did not expose a nonblank Name immediately before deletion. No delete was committed.");
        }

        return name!;
    }

    /// <summary>
    /// Ordinal, exact-one <c>SubnetId</c> lookup. Never falls back to <c>Name</c>, collection index,
    /// a connected device, or the first match. A target resolved by the host that no longer has
    /// exactly one worker-side match is reported as postcondition drift.
    /// </summary>
    private static Subnet ResolveExactSubnetOrThrow(
        Project project,
        string subnetId,
        string operationName)
    {
        var matches = FindMatches(project, subnetId, out var unreadableCount);
        var failure = NetworkPostconditionChecks.ClassifySelection(matches.Count, unreadableCount == 0);
        if (failure == WorkerFailureCategories.WorkerOperationFailed)
            throw new WorkerOperationException(failure,
                "Subnet identity discovery was unreadable. No subnet mutation was attempted.");

        if (matches.Count == 0)
        {
            throw PostconditionFailed(
                operationName,
                $"The previously resolved subnet '{subnetId}' no longer exists. Inspect the project before retrying.");
        }

        if (matches.Count > 1)
        {
            throw PostconditionFailed(
                operationName,
                $"The previously unique SubnetId '{subnetId}' now matches multiple subnets. Inspect the project before retrying.");
        }

        return matches[0];
    }

    /// <summary>
    /// Reports unreadable candidates separately from known nonmatches so partial discovery
    /// cannot authorize a mutation on an apparently unique visible match.
    /// </summary>
    private static List<Subnet> FindMatches(Project project, string subnetId, out int unreadableCount)
    {
        var matches = new List<Subnet>();
        var unreadable = 0;
        foreach (Subnet candidate in project.Subnets)
        {
            var candidateId = ReadSubnetId(candidate);
            if (string.IsNullOrWhiteSpace(candidateId))
            {
                unreadable++;
                continue;
            }

            if (string.Equals(candidateId, subnetId, StringComparison.Ordinal))
            {
                matches.Add(candidate);
            }
        }

        unreadableCount = unreadable;
        return matches;
    }

    private static string? ReadSubnetId(Subnet subnet)
    {
        try
        {
            return ((IEngineeringObject)subnet).GetAttribute("SubnetId")?.ToString();
        }
        catch (EngineeringException)
        {
            return null;
        }
    }

    private static WorkerOperationException PostconditionFailed(string operationName, string message)
        => new(WorkerFailureCategories.PostconditionFailed, $"{operationName}: {message}");
}
