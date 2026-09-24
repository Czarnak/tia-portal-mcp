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
        var target = RequireExactIoSystem(project, request.Target!);
        var owner = RequireExactOwningDeviceItem(project, target);
        return NewResult(request, target, owner);
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

    private static ResolvedNetworkObject RequireExactIoSystem(Project project, NetworkObjectSelectorInfo selector)
    {
        var resolved = NetworkObjectSelectorResolver.Resolve(project, selector);
        if (!resolved.Success || resolved.Resolved?.Value is not IoSystem)
            throw Failure("The exact IO system could not be uniquely resolved.");
        return resolved.Resolved;
    }

    private static Owner RequireExactOwningDeviceItem(Project project, ResolvedNetworkObject target)
    {
        var matches = new List<Owner>();
        foreach (var device in ProjectDeviceEnumerator.Enumerate(project))
            FindOwners(device.DeviceItems, device.Name, new List<DeviceItemPathSegmentInfo>(), (IoSystem)target.Value, matches);
        if (matches.Count != 1)
            throw Failure("The IO system must have exactly one owning controller DeviceItem.");
        var owner = matches[0];
        var verified = NetworkObjectSelectorResolver.Resolve(project, owner.Selector);
        if (!verified.Success || !object.Equals(verified.Resolved!.Value, owner.Item))
            throw Failure("The owning DeviceItem selector could not be verified.");
        return owner;
    }

    private static void FindOwners(DeviceItemComposition items, string deviceName,
        List<DeviceItemPathSegmentInfo> parentPath, IoSystem target, List<Owner> matches)
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
                        matches.Add(new Owner(item, NetworkSelectorFactory.DeviceItem(deviceName, path)));
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
    }

    private static WorkerOperationException Failure(string message)
        => new(WorkerFailureCategories.WorkerOperationFailed, message);

    private sealed class Owner
    {
        public Owner(DeviceItem item, NetworkObjectSelectorInfo selector) { Item = item; Selector = selector; }
        public DeviceItem Item { get; }
        public NetworkObjectSelectorInfo Selector { get; }
    }
}
