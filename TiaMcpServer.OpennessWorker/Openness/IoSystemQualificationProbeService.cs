using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>Temporary worker-only qualification. No public operation uses this service.</summary>
public static class IoSystemQualificationProbeService
{
    public static IoSystemQualificationResultInfo InspectOwner(Project project, IoSystemQualificationProbeInfo request)
    {
        var diagnostic = new IoSystemQualificationOwnerDiagnosticInfo();
        try
        {
            var target = RequireExactIoSystem(project, request.Target!);
            var owner = RequireExactOwningDeviceItem(project, target, diagnostic);
            var result = NewResult(request, target, owner);
            result.OwnerDiagnostics = diagnostic;
            return result;
        }
        catch (Exception)
        {
            // Inspection failure is data, not owner proof. No identifiers or raw exceptions escape.
            return new IoSystemQualificationResultInfo
            {
                Mode = "inspectOwner", OwnerMatchCount = diagnostic.MatchCount,
                OwnerDiagnostics = diagnostic,
                RestorationGuidance = "Ownership is unverified. Do not compile or mutate this target."
            };
        }
    }

    public static IoSystemQualificationResultInfo CompileBaseline(Project project, IoSystemQualificationProbeInfo request)
    {
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
        NetworkObjectSelectorInfo originalOwner;
        using (var exclusive = portal.ExclusiveAccess())
        {
            using (var transaction = exclusive.Transaction(project, "Qualify IO system"))
            {
                var target = RequireExactIoSystem(project, request.Target!);
                var owner = RequireExactOwningDeviceItem(project, target);
                RequireCompiler(owner.Item);
                result = NewResult(request, target, owner);
                originalOwner = owner.Selector;
                result.Before = ReadFiveAttributeSnapshot(target);
                RequireExpectedValueAndWritableMetadata(target, result.Before, request);
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
                var owner = RequireExactOwningDeviceItem(project, applied);
                var original = NetworkObjectSelectorResolver.Resolve(project, originalOwner);
                if (!original.Success || !object.Equals(original.Resolved!.Value, owner.Item))
                    throw Failure("Hardware ownership changed after the committed edit.");
                var actual = result.After.Single(attribute => attribute.Name == request.AttributeName);
                if (!actual.Available || !IoSystemQualificationEvidence.Equal(actual.Value, request.DesiredValue))
                    throw Failure("The committed attribute did not match its requested value.");
                result.OwnerTarget = owner.Selector;
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
                foreach (var device in ProjectDeviceEnumerator.Enumerate(project))
                    FindOwners(device.DeviceItems, device.Name, new List<DeviceItemPathSegmentInfo>(), (IoSystem)target.Value, matches);
            },
            candidate => IoSystemQualificationEvidence.SummarizeOwnerPath(candidate.DeviceName, candidate.Path),
            candidate =>
            {
                var selector = NetworkSelectorFactory.DeviceItem(candidate.DeviceName, candidate.Path);
                var verified = NetworkObjectSelectorResolver.Resolve(project, selector);
                if (!verified.Success || !object.Equals(verified.Resolved!.Value, candidate.Item)) return false;
                owner = new Owner(candidate.Item, selector);
                return true;
            }, diagnostic);
        if (diagnostic.Reason != "verified" || owner is null)
            throw Failure("The exact owning DeviceItem could not be verified.");
        return owner;
    }

    private static void FindOwners(DeviceItemComposition items, string deviceName,
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
                        matches.Add(new OwnerCandidate(item, deviceName, path));
            // Unreadable compositions propagate: incomplete discovery cannot prove uniqueness.
            FindOwners(item.DeviceItems, deviceName, path, target, matches);
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
        public OwnerCandidate(DeviceItem item, string deviceName, List<DeviceItemPathSegmentInfo> path)
        { Item = item; DeviceName = deviceName; Path = path; }
        public DeviceItem Item { get; }
        public string DeviceName { get; }
        public List<DeviceItemPathSegmentInfo> Path { get; }
    }

    private sealed class Owner
    {
        public Owner(DeviceItem item, NetworkObjectSelectorInfo selector) { Item = item; Selector = selector; }
        public DeviceItem Item { get; }
        public NetworkObjectSelectorInfo Selector { get; }
    }
}
