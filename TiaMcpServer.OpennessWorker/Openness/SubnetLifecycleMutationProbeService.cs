using System;
using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Live-only evidence probe for subnet lifecycle behavior. This is deliberately not registered in
/// any public operation catalog; only the separately authorized PowerShell harness calls it.
/// </summary>
internal static class SubnetLifecycleMutationProbeService
{
    private const string EthernetTypeIdentifier = "System:Subnet.Ethernet";
    private const string ProfibusTypeIdentifier = "System:Subnet.Profibus";

    public static SubnetLifecycleMutationProbeResult Run(
        TiaPortal tiaPortal,
        Project project,
        string runId,
        string connectedEthernetSubnetId,
        string connectedProfibusSubnetId,
        int profibusHighestAddress,
        string profibusTransmissionSpeed)
    {
        var result = new SubnetLifecycleMutationProbeResult
        {
            Operation = "probe_subnet_lifecycle_mutations",
            ProjectPath = project.Path.FullName,
            RunId = runId,
            ConnectedEthernetSubnetId = connectedEthernetSubnetId,
            ConnectedProfibusSubnetId = connectedProfibusSubnetId,
            ProfibusHighestAddress = profibusHighestAddress,
            ProfibusTransmissionSpeed = profibusTransmissionSpeed,
            Before = HardwareConfigReader.Read(project),
        };

        var prefix = "P4_" + runId;
        var ethernetBefore = FindSubnetInfo(result.Before, connectedEthernetSubnetId);
        var profibusBefore = FindSubnetInfo(result.Before, connectedProfibusSubnetId);
        ValidateConnectedSubnet(result.PreflightErrors, ethernetBefore, connectedEthernetSubnetId, "Ethernet");
        ValidateConnectedSubnet(result.PreflightErrors, profibusBefore, connectedProfibusSubnetId, "Profibus");
        if (string.Equals(connectedEthernetSubnetId, connectedProfibusSubnetId, StringComparison.Ordinal))
        {
            result.PreflightErrors.Add("The Ethernet and PROFIBUS connected-subnet IDs must differ.");
        }

        var collisions = project.Subnets
            .Where(subnet => subnet.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(subnet => subnet.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (collisions.Count > 0)
        {
            result.PreflightErrors.Add(
                $"Probe subnet names already exist for run ID '{runId}': {string.Join(", ", collisions)}.");
        }

        result.SelectedConnectedSubnetsBefore = new List<SubnetInfo>();
        if (ethernetBefore is not null)
        {
            result.SelectedConnectedSubnetsBefore.Add(ethernetBefore);
        }
        if (profibusBefore is not null)
        {
            result.SelectedConnectedSubnetsBefore.Add(profibusBefore);
        }

        if (result.PreflightErrors.Count > 0)
        {
            result.After = result.Before;
            result.DeviceSurvival = CompareDevices(result.Before, result.After);
            result.AllDevicesSurvived = result.DeviceSurvival.All(item => item.Survived);
            return result;
        }

        result.MutationStarted = true;
        var rollbackName = prefix + "_RB";
        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            "transaction_rollback_create_ethernet",
            "transaction",
            "Ethernet",
            commit: false,
            action: () => project.Subnets.Create(EthernetTypeIdentifier, rollbackName),
            persisted: () => FindByName(project, rollbackName) is not null,
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["typeIdentifier"] = EthernetTypeIdentifier,
                ["name"] = rollbackName,
            }));

        var invalidTypeName = prefix + "_BADTYPE";
        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            "validation_invalid_type_identifier",
            "validation",
            "Unknown",
            commit: false,
            action: () => project.Subnets.Create("System:Subnet.Phase4Invalid", invalidTypeName),
            persisted: () => FindByName(project, invalidTypeName) is not null,
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["typeIdentifier"] = "System:Subnet.Phase4Invalid",
                ["name"] = invalidTypeName,
            }));

        string? invalidEmptyNameSubnetId = null;
        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            "validation_empty_name",
            "validation",
            "Ethernet",
            commit: false,
            action: () =>
            {
                var subnet = project.Subnets.Create(EthernetTypeIdentifier, string.Empty);
                invalidEmptyNameSubnetId = ReadSubnetId(subnet);
            },
            persisted: () => string.IsNullOrWhiteSpace(invalidEmptyNameSubnetId)
                ? FindByName(project, string.Empty) is not null
                : FindById(project, invalidEmptyNameSubnetId!) is not null,
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["typeIdentifier"] = EthernetTypeIdentifier,
                ["name"] = string.Empty,
            }));

        var invalidProfibusName = prefix + "_PBINV";
        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            "validation_profibus_highest_address_minus_one",
            "validation",
            "Profibus",
            commit: false,
            action: () =>
            {
                var subnet = project.Subnets.Create(ProfibusTypeIdentifier, invalidProfibusName);
                ((IEngineeringObject)subnet).SetAttribute("HighestAddress", -1);
            },
            persisted: () => FindByName(project, invalidProfibusName) is not null,
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["attribute"] = "HighestAddress",
                ["value"] = "-1",
            }));

        var ethernetCreatedName = prefix + "_E_A";
        var ethernetEditedName = prefix + "_E_B";
        string? ethernetCreatedId = null;
        var createEthernet = RunTransactionStep(
            tiaPortal,
            project,
            "create_isolated_ethernet",
            "create",
            "Ethernet",
            commit: true,
            action: () =>
            {
                var subnet = project.Subnets.Create(EthernetTypeIdentifier, ethernetCreatedName);
                ethernetCreatedId = ReadSubnetId(subnet);
            },
            persisted: () => FindByName(project, ethernetCreatedName) is not null,
            expectedPersisted: true,
            new Dictionary<string, string>
            {
                ["typeIdentifier"] = EthernetTypeIdentifier,
                ["name"] = ethernetCreatedName,
            });
        createEthernet.TargetSubnetId = ethernetCreatedId;
        result.Steps.Add(createEthernet);

        if (string.IsNullOrWhiteSpace(ethernetCreatedId))
        {
            result.Steps.Add(Skipped("edit_isolated_ethernet", "edit", "Ethernet", "The create step did not return a subnet ID."));
        }
        else
        {
            result.Steps.Add(RunTransactionStep(
                tiaPortal,
                project,
                "edit_isolated_ethernet",
                "edit",
                "Ethernet",
                commit: true,
                action: () => FindRequiredById(project, ethernetCreatedId!).Name = ethernetEditedName,
                persisted: () => FindByName(project, ethernetEditedName) is not null,
                expectedPersisted: true,
                new Dictionary<string, string>
                {
                    ["subnetId"] = ethernetCreatedId!,
                    ["name"] = ethernetEditedName,
                }));
        }

        var profibusCreatedName = prefix + "_P_A";
        var profibusEditedName = prefix + "_P_B";
        string? profibusCreatedId = null;
        var createProfibus = RunTransactionStep(
            tiaPortal,
            project,
            "create_isolated_profibus",
            "create",
            "Profibus",
            commit: true,
            action: () =>
            {
                var subnet = project.Subnets.Create(ProfibusTypeIdentifier, profibusCreatedName);
                profibusCreatedId = ReadSubnetId(subnet);
            },
            persisted: () => FindByName(project, profibusCreatedName) is not null,
            expectedPersisted: true,
            new Dictionary<string, string>
            {
                ["typeIdentifier"] = ProfibusTypeIdentifier,
                ["name"] = profibusCreatedName,
            });
        createProfibus.TargetSubnetId = profibusCreatedId;
        result.Steps.Add(createProfibus);

        if (string.IsNullOrWhiteSpace(profibusCreatedId))
        {
            result.Steps.Add(Skipped("edit_isolated_profibus", "edit", "Profibus", "The create step did not return a subnet ID."));
        }
        else
        {
            result.Steps.Add(RunTransactionStep(
                tiaPortal,
                project,
                "edit_isolated_profibus",
                "edit",
                "Profibus",
                commit: true,
                action: () =>
                {
                    var subnet = FindRequiredById(project, profibusCreatedId!);
                    subnet.Name = profibusEditedName;
                    var engineeringObject = (IEngineeringObject)subnet;
                    engineeringObject.SetAttribute("HighestAddress", profibusHighestAddress);
                    var currentSpeed = engineeringObject.GetAttribute("TransmissionSpeed")
                        ?? throw new InvalidOperationException("TransmissionSpeed returned null.");
                    var requestedSpeed = Enum.Parse(
                        currentSpeed.GetType(),
                        profibusTransmissionSpeed,
                        ignoreCase: false);
                    engineeringObject.SetAttribute("TransmissionSpeed", requestedSpeed);
                },
                persisted: () => IsEditedProfibusPersisted(
                    project,
                    profibusCreatedId!,
                    profibusEditedName,
                    profibusHighestAddress,
                    profibusTransmissionSpeed),
                expectedPersisted: true,
                new Dictionary<string, string>
                {
                    ["subnetId"] = profibusCreatedId!,
                    ["name"] = profibusEditedName,
                    ["HighestAddress"] = profibusHighestAddress.ToString(),
                    ["TransmissionSpeed"] = profibusTransmissionSpeed,
                }));
        }

        DeleteCreatedEmptySubnet(result, tiaPortal, project, ethernetCreatedId, "Ethernet");
        DeleteCreatedEmptySubnet(result, tiaPortal, project, profibusCreatedId, "Profibus");
        DeleteConnectedSubnet(
            result,
            tiaPortal,
            project,
            connectedEthernetSubnetId,
            ethernetBefore!,
            "Ethernet");
        DeleteConnectedSubnet(
            result,
            tiaPortal,
            project,
            connectedProfibusSubnetId,
            profibusBefore!,
            "Profibus");

        CleanupProbeSubnets(result, tiaPortal, project, prefix);
        SaveProject(result, project);

        result.After = HardwareConfigReader.Read(project);
        result.DeviceSurvival = CompareDevices(result.Before, result.After);
        result.AllDevicesSurvived = result.DeviceSurvival.All(item => item.Survived);
        result.DeviceInventoryEqual = result.AllDevicesSurvived
            && result.Before.Devices.Count == result.After.Devices.Count;
        result.ConnectedEthernetSubnetDeleted = FindSubnetInfo(result.After, connectedEthernetSubnetId) is null;
        result.ConnectedProfibusSubnetDeleted = FindSubnetInfo(result.After, connectedProfibusSubnetId) is null;
        result.RemainingProbeSubnetNames = result.After.Subnets
            .Where(subnet => subnet.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(subnet => subnet.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        result.RollbackObserved = result.Steps.Any(step =>
            step.Name == "transaction_rollback_create_ethernet"
            && step.Outcome == "rolledBack");
        result.TransactionCommitObserved = result.Steps.Any(step =>
            step.Category == "create"
            && step.Outcome == "committed");
        result.PartialApplication = result.Steps.Any(step =>
                step.Category != "validation"
                && (step.Outcome is "rejected" or "postconditionFailed"))
            || result.Steps.Any(step =>
                step.Outcome == "skipped"
                && step.Category != "cleanup")
            || result.RemainingProbeSubnetNames.Count > 0
            || !result.Saved;

        return result;
    }

    private static void DeleteCreatedEmptySubnet(
        SubnetLifecycleMutationProbeResult result,
        TiaPortal tiaPortal,
        Project project,
        string? subnetId,
        string networkType)
    {
        var stepName = "delete_empty_" + networkType.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(subnetId))
        {
            result.Steps.Add(Skipped(stepName, "deleteEmpty", networkType, "The create step did not return a subnet ID."));
            return;
        }

        var snapshot = FindSubnetInfo(HardwareConfigReader.Read(project), subnetId!);
        if (snapshot is null)
        {
            result.Steps.Add(Skipped(stepName, "deleteEmpty", networkType, "The created subnet no longer exists."));
            return;
        }
        if (snapshot.ConnectedNodeNames.Count > 0 || snapshot.IoSystems.Count > 0)
        {
            result.Steps.Add(Skipped(stepName, "deleteEmpty", networkType, "The created subnet is not empty."));
            return;
        }

        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            stepName,
            "deleteEmpty",
            networkType,
            commit: true,
            action: () => FindRequiredById(project, subnetId!).Delete(),
            persisted: () => FindById(project, subnetId!) is not null,
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["subnetId"] = subnetId!,
                ["connectedNodeCount"] = "0",
                ["ioSystemCount"] = "0",
            }));
    }

    private static void DeleteConnectedSubnet(
        SubnetLifecycleMutationProbeResult result,
        TiaPortal tiaPortal,
        Project project,
        string subnetId,
        SubnetInfo before,
        string networkType)
    {
        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            "delete_connected_" + networkType.ToLowerInvariant(),
            "deleteConnected",
            networkType,
            commit: true,
            action: () => FindRequiredById(project, subnetId).Delete(),
            persisted: () => FindById(project, subnetId) is not null,
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["subnetId"] = subnetId,
                ["subnetName"] = before.Name,
                ["connectedNodeCount"] = before.ConnectedNodeNames.Count.ToString(),
                ["ioSystemCount"] = before.IoSystems.Count.ToString(),
            }));
    }

    private static void CleanupProbeSubnets(
        SubnetLifecycleMutationProbeResult result,
        TiaPortal tiaPortal,
        Project project,
        string prefix)
    {
        var leftovers = project.Subnets
            .Where(subnet => subnet.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(ReadSubnetId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .ToList();
        if (leftovers.Count == 0)
        {
            result.Steps.Add(Skipped("cleanup_probe_subnets", "cleanup", "Mixed", "No probe-created subnets remain."));
            return;
        }

        result.Steps.Add(RunTransactionStep(
            tiaPortal,
            project,
            "cleanup_probe_subnets",
            "cleanup",
            "Mixed",
            commit: true,
            action: () =>
            {
                foreach (var subnetId in leftovers)
                {
                    FindRequiredById(project, subnetId).Delete();
                }
            },
            persisted: () => project.Subnets.Any(subnet => subnet.Name.StartsWith(prefix, StringComparison.Ordinal)),
            expectedPersisted: false,
            new Dictionary<string, string>
            {
                ["subnetIds"] = string.Join(",", leftovers),
            }));
    }

    private static void SaveProject(SubnetLifecycleMutationProbeResult result, Project project)
    {
        var step = new SubnetLifecycleMutationProbeStep
        {
            Name = "save_project",
            Category = "save",
            NetworkType = "Mixed",
            TransactionRequested = false,
        };
        try
        {
            project.Save();
            step.OpennessCallAccepted = true;
            step.Outcome = "saved";
            result.Saved = true;
        }
        catch (NonRecoverableException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EngineeringException
            or InvalidOperationException
            or ArgumentException)
        {
            RecordException(step, exception);
            step.Outcome = "rejected";
        }
        result.Steps.Add(step);
    }

    private static SubnetLifecycleMutationProbeStep RunTransactionStep(
        TiaPortal tiaPortal,
        Project project,
        string name,
        string category,
        string networkType,
        bool commit,
        Action action,
        Func<bool> persisted,
        bool expectedPersisted,
        Dictionary<string, string> inputs)
    {
        var step = new SubnetLifecycleMutationProbeStep
        {
            Name = name,
            Category = category,
            NetworkType = networkType,
            TransactionRequested = true,
            Inputs = inputs,
        };

        try
        {
            using (var exclusiveAccess = tiaPortal.ExclusiveAccess("Network Phase 4 mutation probe: " + name))
            using (var transaction = exclusiveAccess.Transaction(project, name))
            {
                step.TransactionStarted = true;
                action();
                step.OpennessCallAccepted = true;
                if (commit)
                {
                    transaction.CommitOnDispose();
                    step.CommitRequested = true;
                }
            }
            step.TransactionDisposed = true;
        }
        catch (NonRecoverableException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EngineeringException
            or InvalidOperationException
            or ArgumentException)
        {
            RecordException(step, exception);
        }

        try
        {
            step.PersistedAfterTransaction = persisted();
        }
        catch (NonRecoverableException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is EngineeringException
            or InvalidOperationException
            or ArgumentException)
        {
            if (step.Error is null)
            {
                RecordException(step, exception);
            }
        }

        if (step.Error is not null)
        {
            step.Outcome = "rejected";
        }
        else if (step.PersistedAfterTransaction != expectedPersisted)
        {
            step.Outcome = "postconditionFailed";
        }
        else
        {
            step.Outcome = commit ? "committed" : "rolledBack";
        }

        return step;
    }

    private static SubnetLifecycleMutationProbeStep Skipped(
        string name,
        string category,
        string networkType,
        string reason)
        => new()
        {
            Name = name,
            Category = category,
            NetworkType = networkType,
            Outcome = "skipped",
            Error = reason,
        };

    private static void RecordException(SubnetLifecycleMutationProbeStep step, Exception exception)
    {
        step.ExceptionType = exception.GetType().FullName;
        step.Error = exception.Message;
    }

    private static void ValidateConnectedSubnet(
        List<string> errors,
        SubnetInfo? subnet,
        string subnetId,
        string expectedNetworkType)
    {
        if (subnet is null)
        {
            errors.Add($"Subnet '{subnetId}' was not found.");
            return;
        }
        if (!string.Equals(subnet.NetworkType, expectedNetworkType, StringComparison.Ordinal))
        {
            errors.Add(
                $"Subnet '{subnetId}' has network type '{subnet.NetworkType ?? "(unknown)"}', expected '{expectedNetworkType}'.");
        }
        if (subnet.ConnectedNodeNames.Count == 0 && subnet.IoSystems.Count == 0)
        {
            errors.Add($"Subnet '{subnetId}' is not connected; the connected-deletion probe requires dependencies.");
        }
    }

    private static bool IsEditedProfibusPersisted(
        Project project,
        string subnetId,
        string expectedName,
        int expectedHighestAddress,
        string expectedTransmissionSpeed)
    {
        var subnet = FindById(project, subnetId);
        if (subnet is null || !string.Equals(subnet.Name, expectedName, StringComparison.Ordinal))
        {
            return false;
        }

        var engineeringObject = (IEngineeringObject)subnet;
        var highestAddress = engineeringObject.GetAttribute("HighestAddress");
        var transmissionSpeed = engineeringObject.GetAttribute("TransmissionSpeed");
        return Convert.ToInt32(highestAddress) == expectedHighestAddress
            && string.Equals(transmissionSpeed?.ToString(), expectedTransmissionSpeed, StringComparison.Ordinal);
    }

    private static Subnet FindRequiredById(Project project, string subnetId)
        => FindById(project, subnetId)
            ?? throw new InvalidOperationException($"Subnet '{subnetId}' was not found at mutation time.");

    private static Subnet? FindById(Project project, string subnetId)
        => project.Subnets.FirstOrDefault(subnet =>
            string.Equals(ReadSubnetId(subnet), subnetId, StringComparison.Ordinal));

    private static Subnet? FindByName(Project project, string name)
        => project.Subnets.FirstOrDefault(subnet =>
            string.Equals(subnet.Name, name, StringComparison.Ordinal));

    private static string? ReadSubnetId(Subnet subnet)
        => OpennessReflection.ReadPropertyOrAttribute(subnet, "SubnetId");

    private static SubnetInfo? FindSubnetInfo(HardwareConfigInfo hardware, string subnetId)
        => hardware.Subnets.FirstOrDefault(subnet =>
            string.Equals(subnet.SubnetId, subnetId, StringComparison.Ordinal));

    private static List<SubnetLifecycleDeviceSurvival> CompareDevices(
        HardwareConfigInfo before,
        HardwareConfigInfo after)
    {
        var afterCounts = after.Devices
            .GroupBy(DeviceKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return before.Devices
            .GroupBy(DeviceKey, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                afterCounts.TryGetValue(group.Key, out var afterCount);
                var sample = group.First();
                return new SubnetLifecycleDeviceSurvival
                {
                    Name = sample.Name,
                    TypeIdentifier = sample.TypeIdentifier,
                    BeforeCount = group.Count(),
                    AfterCount = afterCount,
                    Survived = afterCount >= group.Count(),
                };
            })
            .ToList();
    }

    private static string DeviceKey(DeviceInfo device)
        => (device.Name ?? string.Empty) + "\u001f" + (device.TypeIdentifier ?? string.Empty);
}

internal sealed class SubnetLifecycleMutationProbeResult
{
    public string Operation { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ConnectedEthernetSubnetId { get; set; } = string.Empty;
    public string ConnectedProfibusSubnetId { get; set; } = string.Empty;
    public int ProfibusHighestAddress { get; set; }
    public string ProfibusTransmissionSpeed { get; set; } = string.Empty;
    public bool MutationStarted { get; set; }
    public bool Saved { get; set; }
    public bool PartialApplication { get; set; }
    public bool RollbackObserved { get; set; }
    public bool TransactionCommitObserved { get; set; }
    public bool ConnectedEthernetSubnetDeleted { get; set; }
    public bool ConnectedProfibusSubnetDeleted { get; set; }
    public bool AllDevicesSurvived { get; set; }
    public bool DeviceInventoryEqual { get; set; }
    public List<string> PreflightErrors { get; set; } = new();
    public List<string> RemainingProbeSubnetNames { get; set; } = new();
    public List<SubnetInfo> SelectedConnectedSubnetsBefore { get; set; } = new();
    public HardwareConfigInfo Before { get; set; } = new();
    public HardwareConfigInfo After { get; set; } = new();
    public List<SubnetLifecycleMutationProbeStep> Steps { get; set; } = new();
    public List<SubnetLifecycleDeviceSurvival> DeviceSurvival { get; set; } = new();
}

internal sealed class SubnetLifecycleMutationProbeStep
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string NetworkType { get; set; } = string.Empty;
    public string? TargetSubnetId { get; set; }
    public bool TransactionRequested { get; set; }
    public bool TransactionStarted { get; set; }
    public bool CommitRequested { get; set; }
    public bool TransactionDisposed { get; set; }
    public bool OpennessCallAccepted { get; set; }
    public bool? PersistedAfterTransaction { get; set; }
    public string Outcome { get; set; } = string.Empty;
    public string? ExceptionType { get; set; }
    public string? Error { get; set; }
    public Dictionary<string, string> Inputs { get; set; } = new();
}

internal sealed class SubnetLifecycleDeviceSurvival
{
    public string? Name { get; set; }
    public string? TypeIdentifier { get; set; }
    public int BeforeCount { get; set; }
    public int AfterCount { get; set; }
    public bool Survived { get; set; }
}
