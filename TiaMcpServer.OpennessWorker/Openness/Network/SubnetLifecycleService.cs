using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Network;

/// <summary>
/// Production implementation of the Phase 4 subnet lifecycle operations (<c>create_subnet</c>,
/// <c>update_subnet</c>, <c>delete_subnet</c>). Each entry point opens exactly one Openness
/// transaction and performs every requested mutation inside it. This file is a distinct production
/// implementation; it does not call,
/// alias, or share code with <see cref="SubnetLifecycleMutationProbeService"/>, which remains an
/// internal-only evidence probe.
/// </summary>
internal static class SubnetLifecycleService
{
    public static SubnetLifecycleResultInfo Create(
        TiaPortal tiaPortal,
        ProjectBase project,
        string name,
        string networkType,
        int? highestAddress,
        string? transmissionSpeed)
    {
        var typeIdentifier = ResolveTypeIdentifier(networkType);

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
        };
        return result;
    }

    public static SubnetLifecycleResultInfo Update(
        TiaPortal tiaPortal,
        ProjectBase project,
        string subnetId,
        string? name,
        int? highestAddress,
        string? transmissionSpeed)
    {
        string capturedName;

        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: update_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "update_subnet"))
        {
            var subnet = ResolveExactSubnetOrThrow(project, subnetId, "update_subnet");
            if (name is not null)
            {
                subnet.Name = name;
            }

            ApplyProfibusAttributes(subnet, highestAddress, transmissionSpeed);
            capturedName = ReadRequiredSubnetNameOrThrow(subnet, subnetId);
            transaction.CommitOnDispose();
        }

        var result = new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = capturedName,
        };
        return result;
    }

    public static SubnetLifecycleResultInfo Delete(
        TiaPortal tiaPortal,
        ProjectBase project,
        string subnetId)
    {
        string capturedName;

        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: delete_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "delete_subnet"))
        {
            var subnet = ResolveExactSubnetOrThrow(project, subnetId, "delete_subnet");
            capturedName = ReadRequiredSubnetNameOrThrow(subnet, subnetId);
            subnet.Delete();
            transaction.CommitOnDispose();
        }

        var result = new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = capturedName,
        };
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
        return SubnetLifecycleContract.TypeIdentifierFor(networkType) ?? throw new WorkerOperationException(
            WorkerFailureCategories.ValidationError,
            $"NetworkType '{networkType}' is not supported. Valid values: "
            + $"{SubnetLifecycleContract.Ethernet}, {SubnetLifecycleContract.Profibus}.");
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
                "subnet lifecycle",
                $"Subnet '{subnetId}' did not expose a nonblank Name. No transaction was committed.");
        }

        return name!;
    }

    /// <summary>
    /// Ordinal, exact-one <c>SubnetId</c> lookup. Never falls back to <c>Name</c>, collection index,
    /// a connected device, or the first match. A target resolved by the host that no longer has
    /// exactly one worker-side match is reported as postcondition drift.
    /// </summary>
    private static Subnet ResolveExactSubnetOrThrow(
        ProjectBase project,
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
    private static List<Subnet> FindMatches(ProjectBase project, string subnetId, out int unreadableCount)
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
