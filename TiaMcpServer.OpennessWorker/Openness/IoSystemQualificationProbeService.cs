using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Temporary worker-only qualification. No public operation uses this service.</summary>
public static class IoSystemQualificationProbeService
{
    public static IoSystemQualificationResultInfo InspectOwner(TiaPortal portal, Project project, IoSystemQualificationProbeInfo request)
    {
        var diagnostic = new IoSystemQualificationOwnerDiagnosticInfo();
        try
        {
            using var exclusive = portal.ExclusiveAccess();
            var target = RequireExactIoSystem(project, request.Target!);
            var owner = RequireExactOwningDeviceItem(project, target, diagnostic);
            var result = NewResult(request, target, owner);
            result.OwnerDiagnostics = diagnostic;
            diagnostic.Stage = "pnSnapshot";
            diagnostic.Reason = "snapshot_unverified";
            result.BeforePnDeviceNames = ReadAffectedPnDeviceNames(project, target);
            result.PnDeviceNameEvidenceScope = GetPnDeviceNameEvidenceScope(target);
            if (result.BeforePnDeviceNames.Any(node => !node.Available))
                throw Failure("A linked PN device name is unavailable during inspection.");
            diagnostic.Stage = "attributeSnapshot";
            diagnostic.Reason = "attribute_snapshot_unverified";
            result.Before = ReadFiveAttributeSnapshot(target);
            diagnostic.Stage = "compileService";
            diagnostic.Reason = "compile_service_unverified";
            var compiler = ((IEngineeringServiceProvider)owner.Item).GetService<ICompilable>();
            result.HardwareCompileServiceAvailable = compiler is not null;
            if (compiler is null)
            {
                diagnostic.Stage = "compileService";
                diagnostic.Reason = "compile_service_unavailable";
                result.RestorationGuidance = "The exact owning hardware item has no compile service. Do not compile or mutate this target.";
            }
            else
            {
                diagnostic.Stage = "verification";
                diagnostic.Reason = "verified";
            }
            if (!IoSystemQualificationEvidence.FitsResultBudget(result))
            {
                diagnostic.Stage = "evidenceBudget";
                diagnostic.Reason = "evidence_oversized";
                throw Failure("Read-only qualification evidence exceeds the result limit.");
            }
            return result;
        }
        catch (Exception)
        {
            // Inspection failure is data, not owner proof. No identifiers or raw exceptions escape.
            return new IoSystemQualificationResultInfo
            {
                Mode = "inspectOwner", OwnerMatchCount = diagnostic.MatchCount,
                OwnerDiagnostics = diagnostic,
                RestorationGuidance = "Ownership or linked PN name evidence is unverified. Do not compile or mutate this target."
            };
        }
    }

    public static IoSystemQualificationResultInfo CompileBaseline(TiaPortal portal, Project project, IoSystemQualificationProbeInfo request)
    {
        using var exclusive = portal.ExclusiveAccess();
        var target = RequireExactIoSystem(project, request.Target!);
        var owner = RequireExactOwningDeviceItem(project, target);
        var result = NewResult(request, target, owner);
        result.Before = ReadFiveAttributeSnapshot(target);
        CompileHardware(owner.Item, result);
        return result;
    }

    public static IoSystemQualificationResultInfo SetAndCompile(TiaPortal portal, Project project, IoSystemQualificationProbeInfo request)
    {
        IoSystemQualificationResultInfo result;
        DeviceItem preEditOwnerItem;
        using (var exclusive = portal.ExclusiveAccess())
        {
            using (var transaction = exclusive.Transaction(project, "Qualify IO system"))
            {
                var target = RequireExactIoSystem(project, request.Target!);
                var owner = RequireExactOwningDeviceItem(project, target);
                RequireCompiler(owner.Item);
                result = NewResult(request, target, owner);
                preEditOwnerItem = owner.Item;
                result.Before = ReadFiveAttributeSnapshot(target);
                result.BeforePnDeviceNames = ReadAffectedPnDeviceNames(project, target);
                result.PnDeviceNameEvidenceScope = GetPnDeviceNameEvidenceScope(target);
                if (result.BeforePnDeviceNames.Any(node => !node.Available))
                    throw Failure("A linked PN device name is unavailable before the edit.");
                RequireExpectedValueAndWritableMetadata(target, result.Before, request);
                if (!IoSystemQualificationEvidence.FitsResultBudget(result))
                    throw Failure("Pre-edit qualification evidence exceeds the result limit.");
                ApplySingleField(target, request);
                transaction.CommitOnDispose();
            }
            result.MutationCommitted = true;
            result.AppliedTarget = null;
            result.RestorationGuidance = "Mutation committed. Inspect the applied selector and all observations before an explicitly authorized restore; never retry after transport loss.";
            try
            {
                var applied = ReadAppliedStateAndNewSelector(project, request);
                result.AppliedTarget = applied.Target;
                result.After = ReadFiveAttributeSnapshot(applied);
                result.AfterPnDeviceNames = ReadAffectedPnDeviceNames(project, applied);
                if (GetPnDeviceNameEvidenceScope(applied) != result.PnDeviceNameEvidenceScope)
                    throw Failure("The IO-system network type changed after the committed edit.");
                if (result.AfterPnDeviceNames.Any(node => !node.Available)
                    || !IoSystemQualificationEvidence.SamePnNodeIdentities(result.BeforePnDeviceNames, result.AfterPnDeviceNames))
                    throw Failure("Linked PN node evidence changed or became unavailable after the committed edit.");
                var owner = RequireExactOwningDeviceItem(project, applied);
                if (!object.Equals(preEditOwnerItem, owner.Item))
                    throw Failure("Hardware ownership changed after the committed edit.");
                var actual = result.After.Single(attribute => attribute.Name == request.AttributeName);
                if (!actual.Available || !IoSystemQualificationEvidence.Equal(actual.Value, request.DesiredValue))
                    throw Failure("The committed attribute did not match its requested value.");
                result.OwnerTarget = owner.Selector;
                if (!IoSystemQualificationEvidence.FitsResultBudget(result))
                    throw Failure("Post-edit qualification evidence exceeds the result limit.");
                CompileHardware(owner.Item, result);
            }
            catch (Exception)
            {
                result.CompileState = "postCommitFailure";
                IoSystemQualificationEvidence.AddMessage(result, "Post-commit verification or hardware compile failed; inspect current state before restoration.");
            }
        }
        return result;
    }

    private static void RequireExpectedValueAndWritableMetadata(ResolvedNetworkObject target,
        List<IoSystemQualificationAttributeInfo> before, IoSystemQualificationProbeInfo request)
    {
        if (request.AttributeName != "Name" && request.AttributeName != "Number"
            && !string.Equals(target.Evidence.NetworkType, "Ethernet", StringComparison.Ordinal))
            throw Failure("Dynamic IO-system qualification requires a PROFINET fixture.");
        var observation = before.Single(attribute => attribute.Name == request.AttributeName);
        var error = IoSystemQualificationEvidence.ValidateChange(observation, request);
        if (error is not null) throw Failure(error);
    }

    private static void ApplySingleField(ResolvedNetworkObject target, IoSystemQualificationProbeInfo request)
    {
        var desired = request.DesiredValue!;
        object value = desired.Kind switch
        {
            "string" => desired.StringValue!, "integer" => desired.IntegerValue!.Value,
            "boolean" => desired.BooleanValue!.Value, _ => throw Failure("Unsupported scalar kind.")
        };
        target.EngineeringObject.SetAttribute(request.AttributeName!, value);
    }

    private static ResolvedNetworkObject ReadAppliedStateAndNewSelector(Project project, IoSystemQualificationProbeInfo request)
    {
        var selector = new NetworkObjectSelectorInfo
        {
            Kind = NetworkObjectKinds.IoSystem, SubnetId = request.Target!.SubnetId,
            Number = request.AttributeName == "Number" ? request.DesiredValue!.IntegerValue : request.Target.Number
        };
        return RequireExactIoSystem(project, selector);
    }

    private static ICompilable RequireCompiler(DeviceItem owningDeviceItem)
    {
        var compiler = ((IEngineeringServiceProvider)owningDeviceItem).GetService<ICompilable>();
        if (compiler is null) throw Failure("The exact owning hardware item has no compile service.");
        return compiler;
    }
    private static ResolvedNetworkObject RequireExactIoSystem(Project project, NetworkObjectSelectorInfo selector)
    {
        var resolved = NetworkObjectSelectorResolver.Resolve(project, selector);
        if (!resolved.Success || resolved.Resolved?.Value is not IoSystem)
            throw Failure("The exact IO system could not be uniquely resolved.");
        return resolved.Resolved;
    }

    private static Owner RequireExactOwningDeviceItem(Project project, ResolvedNetworkObject target,
        IoSystemQualificationOwnerDiagnosticInfo? diagnostic = null)
    {
        Owner? owner = null;
        diagnostic = IoSystemQualificationEvidence.InspectOwner<OwnerCandidate>(
            matches =>
            {
                foreach (var located in ProjectDeviceEnumerator.EnumerateWithLocations(project))
                    FindOwners(located.Device.DeviceItems, located.Device.Name,
                        IoSystemQualificationEvidence.ClassifyDeviceLocation(located.StructuralLocator),
                        new List<DeviceItemPathSegmentInfo>(), (IoSystem)target.Value, matches);
            },
            candidate =>
            {
                if (diagnostic is not null)
                {
                    diagnostic.OwnerDeviceLocation = candidate.Location;
                    diagnostic.DirectDeviceNameMatchCount = TryCountDirectDeviceNameMatches(project, candidate.DeviceName);
                }
                return IoSystemQualificationEvidence.SummarizeOwnerPath(candidate.DeviceName, candidate.Path);
            },
            candidate =>
            {
                // The public selector factory rejects blank types. This temporary probe
                // carries its already validated, directly observed path to the resolver.
                var selector = new NetworkObjectSelectorInfo
                {
                    Kind = NetworkObjectKinds.DeviceItem,
                    DeviceName = candidate.DeviceName,
                    ItemPath = candidate.Path.ToList()
                };
                var verified = NetworkObjectSelectorResolver.ResolveQualificationDeviceItem(project, selector);
                if (diagnostic is not null)
                    IoSystemQualificationEvidence.RecordOwnerResolution(candidate.Item, verified, diagnostic);
                if (verified is null || !IoSystemQualificationEvidence.VerifyResolvedOwner(verified, candidate.Item, (IoSystem)target.Value, ReadControllerIoSystems)) return false;
                owner = new Owner(verified, selector);
                return true;
            }, diagnostic);
        if (diagnostic.Reason != "verified" || owner is null)
            throw Failure("The exact owning DeviceItem could not be verified.");
        return owner;
    }

    private static IEnumerable<IoSystem>? ReadControllerIoSystems(DeviceItem verified)
    {
        var networkInterface = ((IEngineeringServiceProvider)verified).GetService<NetworkInterface>();
        return networkInterface?.IoControllers.Select(controller => controller.IoSystem);
    }

    private static string GetPnDeviceNameEvidenceScope(ResolvedNetworkObject target)
        => target.Evidence.NetworkType switch
        {
            "Ethernet" => "profinet",
            "Profibus" => "notApplicable",
            _ => throw Failure("The IO-system network type is unavailable for PN name evidence.")
        };

    private static List<IoSystemQualificationPnDeviceNameInfo> ReadAffectedPnDeviceNames(
        Project project, ResolvedNetworkObject target)
    {
        var isProfinet = GetPnDeviceNameEvidenceScope(target) == "profinet";
        var nodes = new List<IoSystemQualificationPnDeviceNameInfo>();
        if (isProfinet)
            foreach (var located in ProjectDeviceEnumerator.EnumerateWithLocations(project))
                CollectAffectedPnDeviceNames(located.Device.DeviceItems, located.StructuralLocator,
                    located.Device.Name, new List<DeviceItemPathSegmentInfo>(), (IoSystem)target.Value, nodes);
        IoSystemQualificationEvidence.ValidatePnDeviceNameSnapshot(nodes, isProfinet);
        return nodes.OrderBy(node => node.DeviceLocator, StringComparer.Ordinal)
            .ThenBy(node => string.Join(".", node.ItemPath.Select(segment => segment.Index.ToString("D8"))), StringComparer.Ordinal)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal).ToList();
    }

    private static void CollectAffectedPnDeviceNames(DeviceItemComposition items, string locator,
        string deviceName, List<DeviceItemPathSegmentInfo> parentPath, IoSystem target,
        List<IoSystemQualificationPnDeviceNameInfo> nodes)
    {
        var index = 0;
        foreach (DeviceItem item in items)
        {
            var path = parentPath.Concat(new[] { new DeviceItemPathSegmentInfo
            {
                Index = index++, Name = item.Name, PositionNumber = item.PositionNumber,
                TypeIdentifier = item.TypeIdentifier
            }}).ToList();
            if (path.Count > 16)
                throw Failure("PN device-name item path exceeds the evidence limit.");
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
            {
                var association = IoSystemQualificationEvidence.ClassifyPnAssociation(
                    networkInterface.IoControllers.Select(controller => controller.IoSystem),
                    networkInterface.IoConnectors.Select(connector => connector.ConnectedToIoSystem), target);
                if (association is not null)
                {
                    var linkedNodeCount = 0;
                    foreach (Node node in networkInterface.Nodes)
                    {
                        linkedNodeCount++;
                        if (!string.Equals(node.NodeType.ToString(), "Ethernet", StringComparison.Ordinal))
                            throw Failure("A linked PN interface has an unexpected node type.");
                        var engineeringNode = (IEngineeringObject)node;
                        var infos = engineeringNode.GetAttributeInfos()
                            .Where(info => string.Equals(info.Name, "PnDeviceName", StringComparison.Ordinal)).ToList();
                        var observed = IoSystemQualificationEvidence.ObservePnDeviceName(infos.Count,
                            infos.Count == 1 && infos[0].SupportedTypes.Any(type => type == typeof(string)),
                            () => engineeringNode.GetAttribute("PnDeviceName"));
                        nodes.Add(new IoSystemQualificationPnDeviceNameInfo
                        {
                            DeviceLocator = locator, DeviceName = deviceName, ItemPath = path,
                            NodeId = node.NodeId, AssociationKind = association,
                            Available = observed.Available, Value = observed.Value
                        });
                        if (nodes.Count > 128)
                            throw Failure("PN device-name snapshot exceeds the evidence limit.");
                    }
                    IoSystemQualificationEvidence.RequireLinkedPnNodes(linkedNodeCount);
                }
            }
            CollectAffectedPnDeviceNames(item.DeviceItems, locator, deviceName, path, target, nodes);
        }
    }

    private static int? TryCountDirectDeviceNameMatches(Project project, string requestedName)
    {
        try
        {
            return IoSystemQualificationEvidence.CountDirectDeviceNameMatches(
                project.Devices.Cast<Device>().Select(device => device.Name), requestedName);
        }
        catch (Exception) { return null; }
    }

    private static void FindOwners(DeviceItemComposition items, string deviceName, string location,
        List<DeviceItemPathSegmentInfo> parentPath, IoSystem target, List<OwnerCandidate> matches)
    {
        var index = 0;
        foreach (DeviceItem item in items)
        {
            var path = parentPath.Concat(new[] { new DeviceItemPathSegmentInfo
            {
                Index = index++, Name = item.Name, PositionNumber = item.PositionNumber, TypeIdentifier = item.TypeIdentifier
            }}).ToList();
            var networkInterface = ((IEngineeringServiceProvider)item).GetService<NetworkInterface>();
            if (networkInterface is not null)
                foreach (IoController controller in networkInterface.IoControllers)
                    if (object.Equals(controller.IoSystem, target))
                        matches.Add(new OwnerCandidate(item, deviceName, location, path));
            // Unreadable compositions propagate: incomplete discovery cannot prove uniqueness.
            FindOwners(item.DeviceItems, deviceName, location, path, target, matches);
        }
    }

    private static IoSystemQualificationResultInfo NewResult(IoSystemQualificationProbeInfo request,
        ResolvedNetworkObject target, Owner owner)
        => new()
        {
            Mode = request.Mode, OriginalTarget = target.Target, AppliedTarget = target.Target,
            OwnerTarget = owner.Selector, OwnerMatchCount = 1, OwnerIdentityVerified = true,
            HardwareTargetKind = NetworkObjectKinds.DeviceItem, HardwareTargetAlias = "owning-controller-item",
            RestorationGuidance = "No mutation was requested."
        };

    private static List<IoSystemQualificationAttributeInfo> ReadFiveAttributeSnapshot(ResolvedNetworkObject target)
    {
        var attributes = target.EngineeringObject.GetAttributeInfos();
        var result = new List<IoSystemQualificationAttributeInfo>();
        foreach (var name in new[] { "Name", "Number", "MultipleUseIoSystem", "UseIoSystemNameAsDeviceNameExtension", "MaxNumberIWlanLinksPerSegment" })
        {
            var infos = attributes.Where(info => string.Equals(info.Name, name, StringComparison.Ordinal)).ToList();
            var observation = new IoSystemQualificationAttributeInfo { Name = name };
            if (infos.Count == 1)
            {
                observation.Writable = infos[0].AccessMode == EngineeringAttributeAccessMode.ReadWrite;
                observation.SupportedTypes = infos[0].SupportedTypes.Select(type => type.FullName ?? type.Name).ToList();
                try
                {
                    observation.Value = ToScalar(target.EngineeringObject.GetAttribute(name));
                    observation.Available = observation.Value is not null;
                }
                catch (EngineeringException) { observation.Available = false; }
            }
            result.Add(observation);
        }
        return result;
    }

    private static IoSystemQualificationScalarInfo? ToScalar(object? value)
        => value switch
        {
            string text => new() { Kind = "string", StringValue = text },
            int number => new() { Kind = "integer", IntegerValue = number },
            bool boolean => new() { Kind = "boolean", BooleanValue = boolean },
            _ => null
        };

    private static void CompileHardware(DeviceItem owningDeviceItem, IoSystemQualificationResultInfo result)
    {
        var compiler = ((IEngineeringServiceProvider)owningDeviceItem).GetService<ICompilable>();
        if (compiler is null)
            throw Failure("The exact owning hardware item has no compile service.");
        var compilerResult = compiler.Compile();
        result.CompileState = compilerResult.State.ToString();
        result.ErrorCount = compilerResult.ErrorCount;
        result.WarningCount = compilerResult.WarningCount;
        ReadCompilerMessages(compilerResult.Messages, result);
    }

    private static void ReadCompilerMessages(IEnumerable<CompilerResultMessage> messages, IoSystemQualificationResultInfo result)
    {
        foreach (var message in messages)
        {
            IoSystemQualificationEvidence.AddMessage(result, message.State + ": " + message.Description);
            ReadCompilerMessages(message.Messages, result);
        }
    }
    private static WorkerOperationException Failure(string message)
        => new(WorkerFailureCategories.WorkerOperationFailed, message);

    private sealed class OwnerCandidate
    {
        public OwnerCandidate(DeviceItem item, string deviceName, string location, List<DeviceItemPathSegmentInfo> path)
        { Item = item; DeviceName = deviceName; Location = location; Path = path; }
        public DeviceItem Item { get; }
        public string DeviceName { get; }
        public string Location { get; }
        public List<DeviceItemPathSegmentInfo> Path { get; }
    }

    private sealed class Owner
    {
        public Owner(DeviceItem item, NetworkObjectSelectorInfo selector) { Item = item; Selector = selector; }
        public DeviceItem Item { get; }
        public NetworkObjectSelectorInfo Selector { get; }
    }
}
