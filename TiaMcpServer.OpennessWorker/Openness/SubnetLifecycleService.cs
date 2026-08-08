using System;
using System.Collections.Generic;
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

        var deviceCountAfter = project.Devices.Count;
        var deviceCountUnchanged = deviceCountAfter == deviceCountBefore;

        var postReadMatches = string.IsNullOrWhiteSpace(createdSubnetId)
            ? new List<Subnet>()
            : FindMatches(project, createdSubnetId!);
        if (postReadMatches.Count != 1
            || !string.Equals(postReadMatches[0].Name, name, StringComparison.Ordinal)
            || !MatchesTypeIdentifier(postReadMatches[0], typeIdentifier)
            || !MatchesRequestedProfibusFields(postReadMatches[0], highestAddress, transmissionSpeed)
            || !deviceCountUnchanged)
        {
            throw PostconditionFailed(
                "create_subnet",
                "Expected exactly one post-read subnet with the returned nonblank SubnetId whose name, "
                + "type, and requested PROFIBUS attributes matched the request, and an unchanged device "
                + "count, after the transaction committed. Inspect the project before retrying.");
        }

        var created = postReadMatches[0];
        return new SubnetLifecycleResultInfo
        {
            SubnetId = createdSubnetId!,
            Name = created.Name,
            NetworkDeviceCount = deviceCountAfter,
            NetworkDeviceCountUnchanged = deviceCountUnchanged,
        };
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
            ResolveExactSubnetOrThrow(project, subnetId),
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

        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: update_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "update_subnet"))
        {
            var subnet = ResolveExactSubnetOrThrow(project, subnetId);
            if (name is not null)
            {
                subnet.Name = name;
            }

            ApplyProfibusAttributes(subnet, highestAddress, transmissionSpeed);
            transaction.CommitOnDispose();
        }

        var deviceCountAfter = project.Devices.Count;
        var deviceCountUnchanged = deviceCountAfter == deviceCountBefore;

        var postReadMatches = FindMatches(project, subnetId);
        if (postReadMatches.Count != 1
            || (name is not null && !string.Equals(postReadMatches[0].Name, name, StringComparison.Ordinal))
            || !MatchesRequestedProfibusFields(postReadMatches[0], highestAddress, transmissionSpeed)
            || !deviceCountUnchanged)
        {
            throw PostconditionFailed(
                "update_subnet",
                $"Expected exactly one subnet with SubnetId '{subnetId}' whose requested fields matched, "
                + "and an unchanged device count, after the transaction committed. Inspect the project "
                + "before retrying.");
        }

        var updated = postReadMatches[0];
        return new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = updated.Name,
            NetworkDeviceCount = deviceCountAfter,
            NetworkDeviceCountUnchanged = deviceCountUnchanged,
        };
    }

    public static SubnetLifecycleResultInfo Delete(
        TiaPortal tiaPortal,
        Project project,
        string subnetId)
    {
        // Deliberately does not enumerate the target's connected nodes or IO systems: a
        // connected-subnet deletion must not inspect or block on any of that.
        var existing = ResolveExactSubnetOrThrow(project, subnetId);

        // Captured before the transaction because the Openness object is gone once deleted — this
        // is NOT a pre-commit guard: an unreadable name here never blocks the delete from
        // proceeding. It only means the eventual result cannot report the deleted subnet's own
        // identity, which is checked — and fails closed — only after the transaction has already
        // committed, alongside every other postcondition below.
        string? capturedName;
        try
        {
            capturedName = existing.Name;
        }
        catch (EngineeringException)
        {
            capturedName = null;
        }

        var deviceCountBefore = project.Devices.Count;

        using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 subnet lifecycle: delete_subnet"))
        using (var transaction = exclusiveAccess.Transaction(project, "delete_subnet"))
        {
            var subnet = ResolveExactSubnetOrThrow(project, subnetId);
            subnet.Delete();
            transaction.CommitOnDispose();
        }

        var deviceCountAfter = project.Devices.Count;
        var deviceCountUnchanged = deviceCountAfter == deviceCountBefore;

        // Fail-closed, not fail-open: a surviving subnet whose SubnetId happens to be transiently
        // unreadable must NOT read as "successfully deleted". Every other postcondition in this
        // service already fails closed on an unreadable identity because absence-of-match is a
        // FAILURE condition there; delete_subnet is the one operation where absence-of-match is the
        // SUCCESS condition, so it needs its own explicit guard against that asymmetry. An
        // unreadable capturedName joins the same guard: the delete has already committed by this
        // point, so a name that cannot be re-confirmed is reported as a postcondition failure,
        // never as a fabricated blank name.
        var postReadMatches = FindMatches(project, subnetId, out var unreadableSubnetIdCount);
        if (postReadMatches.Count != 0 || unreadableSubnetIdCount > 0 || !deviceCountUnchanged || capturedName is null)
        {
            throw PostconditionFailed(
                "delete_subnet",
                $"Expected no subnet with SubnetId '{subnetId}', no subnet with an unreadable "
                + "SubnetId, an unchanged device count, and a readable Name captured for the deleted "
                + "subnet, after the transaction committed. The delete already committed; inspect the "
                + "project before retrying.");
        }

        return new SubnetLifecycleResultInfo
        {
            SubnetId = subnetId,
            Name = capturedName,
            NetworkDeviceCount = deviceCountAfter,
            NetworkDeviceCountUnchanged = deviceCountUnchanged,
        };
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

    private static bool MatchesTypeIdentifier(Subnet subnet, string expectedTypeIdentifier)
    {
        try
        {
            return string.Equals(subnet.TypeIdentifier, expectedTypeIdentifier, StringComparison.Ordinal);
        }
        catch (EngineeringException)
        {
            return false;
        }
    }

    private static bool MatchesRequestedProfibusFields(Subnet subnet, int? highestAddress, string? transmissionSpeed)
    {
        var engineeringObject = (IEngineeringObject)subnet;

        if (highestAddress is not null)
        {
            object? currentHighestAddress;
            try
            {
                currentHighestAddress = engineeringObject.GetAttribute("HighestAddress");
            }
            catch (EngineeringException)
            {
                return false;
            }

            if (currentHighestAddress is null || Convert.ToInt32(currentHighestAddress) != highestAddress.Value)
            {
                return false;
            }
        }

        if (transmissionSpeed is not null)
        {
            object? currentTransmissionSpeed;
            try
            {
                currentTransmissionSpeed = engineeringObject.GetAttribute("TransmissionSpeed");
            }
            catch (EngineeringException)
            {
                return false;
            }

            if (currentTransmissionSpeed is null
                || !string.Equals(currentTransmissionSpeed.ToString(), transmissionSpeed, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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

    /// <summary>
    /// Ordinal, exact-one <c>SubnetId</c> lookup. Never falls back to <c>Name</c>, collection index,
    /// a connected device, or the first match — zero or more than one match is a resolution failure.
    /// </summary>
    private static Subnet ResolveExactSubnetOrThrow(Project project, string subnetId)
    {
        var matches = FindMatches(project, subnetId);

        if (matches.Count == 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"No subnet with SubnetId '{subnetId}' was found.");
        }

        if (matches.Count > 1)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous,
                $"Multiple subnets report SubnetId '{subnetId}'.");
        }

        return matches[0];
    }

    private static List<Subnet> FindMatches(Project project, string subnetId)
        => FindMatches(project, subnetId, out _);

    /// <summary>
    /// Same ordinal exact-match scan as <see cref="FindMatches(Project, string)"/>, additionally
    /// reporting how many candidates' <c>SubnetId</c> could not be read at all — distinct from "read
    /// successfully but didn't match" — so a caller that treats zero matches as a meaningful outcome
    /// (delete_subnet's postcondition) can tell the two apart instead of silently conflating them.
    /// </summary>
    private static List<Subnet> FindMatches(Project project, string subnetId, out int unreadableCount)
    {
        var matches = new List<Subnet>();
        var unreadable = 0;
        foreach (Subnet candidate in project.Subnets)
        {
            var candidateId = ReadSubnetId(candidate);
            if (candidateId is null)
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
