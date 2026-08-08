using System;
using System.Collections.Generic;
using System.Globalization;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.Settings;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.Types;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Bounded, read-only discovery of engineering objects the VCI Workspace Phase 1 read-only probe
/// may resolve a selector against. Enumerates exactly six candidate families — project, device,
/// nested device item, PLC block, PLC tag table, PLC type — reusing <see cref="PlcSoftwareLocator"/>
/// and the traversal shape already established by <see cref="ProjectTreeWalker"/> and
/// <see cref="NetworkObjectIndexReader"/> rather than inventing a new one.
///
/// <para>
/// Never invokes a VCI write member. Every composition this type reads (<c>Project.Devices</c>,
/// <c>DeviceItem.DeviceItems</c>, <c>PlcBlockGroup.Blocks</c>/<c>Groups</c>,
/// <c>PlcTagTableGroup.TagTables</c>/<c>Groups</c>, <c>PlcTypeGroup.Types</c>/<c>Groups</c>) is one
/// of the harmless identity/property reads the Phase 1 plan's global constraints allow.
/// </para>
///
/// <para>
/// <see cref="Enumerate"/> returns a fresh <see cref="VciProbeEngineeringObjectCatalogResult"/> on
/// every call and never retains a discovered Siemens object proxy in a static field — no engineering
/// object survives past the caller's use of the returned candidates for one worker request.
/// </para>
/// </summary>
public static class VciProbeEngineeringObjectCatalog
{
    /// <summary>
    /// Bound on <c>DeviceItem</c> nesting depth. Not carried on <see cref="VciProbeRequestInfo"/> —
    /// Task 1 locked that contract without a device-item-depth field — kept as a local constant so a
    /// later task can promote it onto the wire contract without changing this file's traversal
    /// shape. Generous enough for any realistic rack/slot/subslot/submodule nesting.
    /// </summary>
    internal const int MaxDeviceItemDepth = 12;

    /// <summary>Bound on PLC block/tag-table/type folder nesting depth, for the same reason as <see cref="MaxDeviceItemDepth"/>.</summary>
    internal const int MaxSoftwareGroupDepth = 12;

    public static VciProbeEngineeringObjectCatalogResult Enumerate(Project project, VciProbeRequestInfo request)
    {
        if (project is null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var context = new EnumerationContext(project, request);

        // 1. Project root candidate — the resolvable root every other structural path descends from.
        context.AddCandidate(
            project,
            VciProbeEngineeringObjectFamilies.Project,
            objectTypeLabel: "Project",
            name: SafeRead(() => project.Name),
            parentPath: Array.Empty<PathStep>(),
            siblingIndex: 0,
            sameNameOrdinal: 0,
            identity: new[] { ("Name", SafeRead(() => project.Name)) });

        // 2. Devices, their nested device items, and any hosted PLC software.
        var deviceCounter = new SiblingNameCounter();
        var deviceIndex = 0;
        foreach (Device device in project.Devices)
        {
            if (deviceIndex >= request.MaxCollectionItems)
            {
                context.RecordOmission(
                    "project.Devices enumeration stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    request.MaxCollectionItems,
                    deviceIndex);
                break;
            }

            var deviceName = SafeRead(() => device.Name);
            var deviceOrdinal = deviceCounter.NextOrdinal(deviceName);
            var deviceStep = new PathStep("Device", deviceName, deviceIndex, deviceOrdinal);

            context.AddCandidate(
                device,
                VciProbeEngineeringObjectFamilies.Device,
                objectTypeLabel: "Device",
                name: deviceName,
                parentPath: Array.Empty<PathStep>(),
                siblingIndex: deviceIndex,
                sameNameOrdinal: deviceOrdinal,
                identity: new[] { ("Name", deviceName) });

            WalkDeviceItems(context, device.DeviceItems, new List<PathStep> { deviceStep }, depth: 1);

            var softwareCounter = new SiblingNameCounter();
            var softwareIndex = 0;
            foreach (var plcSoftware in PlcSoftwareLocator.FindInDevice(device))
            {
                if (softwareIndex >= request.MaxCollectionItems)
                {
                    context.RecordOmission(
                        $"PLC software enumeration for device '{deviceName}' stopped to respect the configured per-composition budget.",
                        nameof(VciProbeRequestInfo.MaxCollectionItems),
                        request.MaxCollectionItems,
                        softwareIndex);
                    break;
                }

                var softwareName = SafeRead(() => plcSoftware.Name);
                var softwareOrdinal = softwareCounter.NextOrdinal(softwareName);
                var softwarePath = new List<PathStep>
                {
                    deviceStep,
                    new("PlcSoftware", softwareName, softwareIndex, softwareOrdinal),
                };

                WalkBlockGroup(context, plcSoftware.BlockGroup, softwarePath, depth: 0);
                WalkTagTableGroup(context, plcSoftware.TagTableGroup, softwarePath, depth: 0);
                WalkTypeGroup(context, plcSoftware.TypeGroup, softwarePath, depth: 0);

                softwareIndex++;
            }

            deviceIndex++;
        }

        var selected = SelectWithinBudget(context.Candidates, request.MaxEngineeringObjects, context.Omissions);
        return new VciProbeEngineeringObjectCatalogResult(selected, context.Omissions);
    }

    private static void WalkDeviceItems(
        EnumerationContext context,
        DeviceItemComposition items,
        List<PathStep> parentPath,
        int depth)
    {
        if (depth > MaxDeviceItemDepth)
        {
            context.RecordOmission(
                $"Device item traversal stopped at depth {depth} to respect the maximum device-item nesting depth.",
                nameof(MaxDeviceItemDepth),
                MaxDeviceItemDepth,
                depth - 1);
            return;
        }

        var counter = new SiblingNameCounter();
        var index = 0;
        foreach (DeviceItem item in items)
        {
            if (index >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    "Device item composition enumeration stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    index);
                break;
            }

            var name = SafeRead(() => item.Name);
            var positionNumber = SafeReadInt(() => item.PositionNumber);
            var typeIdentifier = SafeRead(() => item.TypeIdentifier);
            var ordinal = counter.NextOrdinal(name);
            var step = new PathStep("DeviceItem", name, index, ordinal);
            var itemPath = new List<PathStep>(parentPath) { step };

            context.AddCandidate(
                item,
                VciProbeEngineeringObjectFamilies.DeviceItem,
                objectTypeLabel: "DeviceItem",
                name: name,
                parentPath: parentPath,
                siblingIndex: index,
                sameNameOrdinal: ordinal,
                identity: new[]
                {
                    ("Name", name),
                    ("PositionNumber", positionNumber),
                    ("TypeIdentifier", typeIdentifier),
                });

            try
            {
                WalkDeviceItems(context, item.DeviceItems, itemPath, depth + 1);
            }
            catch (EngineeringException)
            {
                // A failed child composition must not make sibling items undiscoverable — same
                // resilience rule NetworkObjectIndexReader.ReadDeviceItems already applies.
            }

            index++;
        }
    }

    private static void WalkBlockGroup(EnumerationContext context, PlcBlockGroup group, List<PathStep> parentPath, int depth)
    {
        if (depth > MaxSoftwareGroupDepth)
        {
            context.RecordOmission(
                $"Block group traversal stopped at depth {depth} to respect the maximum software-group nesting depth.",
                nameof(MaxSoftwareGroupDepth),
                MaxSoftwareGroupDepth,
                depth - 1);
            return;
        }

        var groupName = SafeRead(() => group.Name);
        var groupStep = new PathStep("BlockFolder", groupName, 0, 0);
        var groupPath = new List<PathStep>(parentPath) { groupStep };

        var counter = new SiblingNameCounter();
        var index = 0;
        foreach (PlcBlock block in group.Blocks)
        {
            if (index >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    $"Block composition enumeration for group '{groupName}' stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    index);
                break;
            }

            try
            {
                var name = SafeRead(() => block.Name);
                var number = SafeReadInt(() => block.Number);
                var kind = BlockKindLabel(block);
                var ordinal = counter.NextOrdinal(name);

                context.AddCandidate(
                    block,
                    VciProbeEngineeringObjectFamilies.PlcBlock,
                    objectTypeLabel: kind,
                    name: name,
                    parentPath: groupPath,
                    siblingIndex: index,
                    sameNameOrdinal: ordinal,
                    identity: new[] { ("Name", name), ("Number", number) });
            }
            catch (EngineeringException)
            {
                // Skip an unreadable block; siblings remain discoverable.
            }

            index++;
        }

        var childIndex = 0;
        foreach (PlcBlockGroup childGroup in group.Groups)
        {
            if (childIndex >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    $"Nested block group enumeration under '{groupName}' stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    childIndex);
                break;
            }

            try
            {
                WalkBlockGroup(context, childGroup, groupPath, depth + 1);
            }
            catch (EngineeringException)
            {
                // Skip an unreadable nested group; siblings remain discoverable.
            }

            childIndex++;
        }
    }

    private static void WalkTagTableGroup(EnumerationContext context, PlcTagTableGroup group, List<PathStep> parentPath, int depth)
    {
        if (depth > MaxSoftwareGroupDepth)
        {
            context.RecordOmission(
                $"Tag table group traversal stopped at depth {depth} to respect the maximum software-group nesting depth.",
                nameof(MaxSoftwareGroupDepth),
                MaxSoftwareGroupDepth,
                depth - 1);
            return;
        }

        var groupName = SafeRead(() => group.Name);
        var groupStep = new PathStep("TagTableFolder", groupName, 0, 0);
        var groupPath = new List<PathStep>(parentPath) { groupStep };

        var counter = new SiblingNameCounter();
        var index = 0;
        foreach (PlcTagTable table in group.TagTables)
        {
            if (index >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    $"Tag table composition enumeration for group '{groupName}' stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    index);
                break;
            }

            try
            {
                var name = SafeRead(() => table.Name);
                var ordinal = counter.NextOrdinal(name);

                context.AddCandidate(
                    table,
                    VciProbeEngineeringObjectFamilies.PlcTagTable,
                    objectTypeLabel: "TagTable",
                    name: name,
                    parentPath: groupPath,
                    siblingIndex: index,
                    sameNameOrdinal: ordinal,
                    identity: new[] { ("Name", name) });
            }
            catch (EngineeringException)
            {
                // Skip an unreadable tag table; siblings remain discoverable.
            }

            index++;
        }

        var childIndex = 0;
        foreach (PlcTagTableGroup childGroup in group.Groups)
        {
            if (childIndex >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    $"Nested tag table group enumeration under '{groupName}' stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    childIndex);
                break;
            }

            try
            {
                WalkTagTableGroup(context, childGroup, groupPath, depth + 1);
            }
            catch (EngineeringException)
            {
                // Skip an unreadable nested group; siblings remain discoverable.
            }

            childIndex++;
        }
    }

    private static void WalkTypeGroup(EnumerationContext context, PlcTypeGroup group, List<PathStep> parentPath, int depth)
    {
        if (depth > MaxSoftwareGroupDepth)
        {
            context.RecordOmission(
                $"Type group traversal stopped at depth {depth} to respect the maximum software-group nesting depth.",
                nameof(MaxSoftwareGroupDepth),
                MaxSoftwareGroupDepth,
                depth - 1);
            return;
        }

        var groupName = SafeRead(() => group.Name);
        var groupStep = new PathStep("TypeFolder", groupName, 0, 0);
        var groupPath = new List<PathStep>(parentPath) { groupStep };

        var counter = new SiblingNameCounter();
        var index = 0;
        foreach (PlcType type in group.Types)
        {
            if (index >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    $"Type composition enumeration for group '{groupName}' stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    index);
                break;
            }

            try
            {
                var name = SafeRead(() => type.Name);
                var ordinal = counter.NextOrdinal(name);

                context.AddCandidate(
                    type,
                    VciProbeEngineeringObjectFamilies.PlcType,
                    objectTypeLabel: "Type",
                    name: name,
                    parentPath: groupPath,
                    siblingIndex: index,
                    sameNameOrdinal: ordinal,
                    identity: new[] { ("Name", name) });
            }
            catch (EngineeringException)
            {
                // Skip an unreadable type; siblings remain discoverable.
            }

            index++;
        }

        var childIndex = 0;
        foreach (PlcTypeGroup childGroup in group.Groups)
        {
            if (childIndex >= context.Request.MaxCollectionItems)
            {
                context.RecordOmission(
                    $"Nested type group enumeration under '{groupName}' stopped to respect the configured per-composition budget.",
                    nameof(VciProbeRequestInfo.MaxCollectionItems),
                    context.Request.MaxCollectionItems,
                    childIndex);
                break;
            }

            try
            {
                WalkTypeGroup(context, childGroup, groupPath, depth + 1);
            }
            catch (EngineeringException)
            {
                // Skip an unreadable nested group; siblings remain discoverable.
            }

            childIndex++;
        }
    }

    private static string BlockKindLabel(PlcBlock block) => block switch
    {
        OB => "OB",
        FB => "FB",
        FC => "FC",
        GlobalDB => "GlobalDB",
        InstanceDB => "InstanceDB",
        ArrayDB => "ArrayDB",
        _ => "Block",
    };

    /// <summary>
    /// Selects candidates within <paramref name="maxEngineeringObjects"/>: one representative per
    /// distinct <see cref="VciProbeEngineeringObjectCandidate.RuntimeTypeName"/> first (in original
    /// enumeration order), then fills any remaining budget with leftover candidates, also in
    /// original enumeration order. Guarantees the first large device tree cannot, by itself, consume
    /// the entire candidate budget before a PLC block, tag table, or type is ever selected.
    /// </summary>
    private static List<VciProbeEngineeringObjectCandidate> SelectWithinBudget(
        List<VciProbeEngineeringObjectCandidate> raw,
        int maxEngineeringObjects,
        List<VciProbeOmissionInfo> omissions)
    {
        if (raw.Count <= maxEngineeringObjects)
        {
            return raw;
        }

        var selected = new List<VciProbeEngineeringObjectCandidate>();
        var selectedSet = new HashSet<VciProbeEngineeringObjectCandidate>();
        var seenTypes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidate in raw)
        {
            if (selected.Count >= maxEngineeringObjects)
            {
                break;
            }

            if (seenTypes.Add(candidate.RuntimeTypeName))
            {
                selected.Add(candidate);
                selectedSet.Add(candidate);
            }
        }

        if (selected.Count < maxEngineeringObjects)
        {
            foreach (var candidate in raw)
            {
                if (selected.Count >= maxEngineeringObjects)
                {
                    break;
                }

                if (selectedSet.Add(candidate))
                {
                    selected.Add(candidate);
                }
            }
        }

        selected.Sort((a, b) => a.EnumerationIndex.CompareTo(b.EnumerationIndex));

        omissions.Add(new VciProbeOmissionInfo
        {
            Reason = string.Format(
                CultureInfo.InvariantCulture,
                "Engineering-object discovery selected {0} of {1} discovered candidate(s) — one representative per distinct runtime type first — to respect the configured budget.",
                selected.Count,
                raw.Count),
            BudgetName = nameof(VciProbeRequestInfo.MaxEngineeringObjects),
            BudgetValue = maxEngineeringObjects,
            ObservedCount = selected.Count,
        });

        return selected;
    }

    private static string SafeRead(Func<string?> read)
    {
        try
        {
            return read() ?? string.Empty;
        }
        catch (EngineeringException)
        {
            return string.Empty;
        }
    }

    private static string SafeReadInt(Func<int> read)
    {
        try
        {
            return read().ToString(CultureInfo.InvariantCulture);
        }
        catch (EngineeringException)
        {
            return string.Empty;
        }
    }

    /// <summary>One step of an in-progress structural path, before it is projected onto the wire and fingerprint shapes.</summary>
    private readonly struct PathStep
    {
        public PathStep(string objectType, string name, int index, int sameNameOrdinal)
        {
            ObjectType = objectType;
            Name = name;
            Index = index;
            SameNameOrdinal = sameNameOrdinal;
        }

        public string ObjectType { get; }
        public string Name { get; }
        public int Index { get; }
        public int SameNameOrdinal { get; }
    }

    /// <summary>Tracks, within one composition's enumeration, how many prior siblings share each name.</summary>
    private sealed class SiblingNameCounter
    {
        private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

        public int NextOrdinal(string name)
        {
            _counts.TryGetValue(name, out var count);
            _counts[name] = count + 1;
            return count;
        }
    }

    /// <summary>Mutable state shared across one <see cref="Enumerate"/> call.</summary>
    private sealed class EnumerationContext
    {
        private readonly ObjectIdentifierProvider? _idProvider;
        private int _enumerationIndex;

        public EnumerationContext(Project project, VciProbeRequestInfo request)
        {
            Request = request;

            try
            {
                _idProvider = project.GetService<ObjectIdentifierProvider>();
            }
            catch (EngineeringException)
            {
                _idProvider = null;
            }
        }

        public VciProbeRequestInfo Request { get; }
        public List<VciProbeEngineeringObjectCandidate> Candidates { get; } = new();
        public List<VciProbeOmissionInfo> Omissions { get; } = new();

        public void AddCandidate(
            object engineeringObject,
            string family,
            string objectTypeLabel,
            string name,
            IReadOnlyList<PathStep> parentPath,
            int siblingIndex,
            int sameNameOrdinal,
            IReadOnlyList<(string Key, string Value)> identity)
        {
            var runtimeTypeName = engineeringObject.GetType().FullName ?? engineeringObject.GetType().Name;

            var wirePath = new List<VciEngineeringObjectPathSegmentInfo>();
            var fingerprintPath = new List<VciSelectorFingerprintPathSegment>();
            foreach (var step in parentPath)
            {
                wirePath.Add(new VciEngineeringObjectPathSegmentInfo
                {
                    Index = step.Index,
                    Name = step.Name,
                    ObjectType = step.ObjectType,
                });
                fingerprintPath.Add(new VciSelectorFingerprintPathSegment
                {
                    Kind = step.ObjectType,
                    Name = step.Name,
                    SameNameOrdinal = step.SameNameOrdinal,
                });
            }

            wirePath.Add(new VciEngineeringObjectPathSegmentInfo
            {
                Index = siblingIndex,
                Name = name,
                ObjectType = objectTypeLabel,
            });
            fingerprintPath.Add(new VciSelectorFingerprintPathSegment
            {
                Kind = objectTypeLabel,
                Name = name,
                SameNameOrdinal = sameNameOrdinal,
            });

            var identityFields = new List<VciSelectorFingerprintIdentityField>();
            foreach (var (key, value) in identity)
            {
                identityFields.Add(new VciSelectorFingerprintIdentityField { Key = key, Value = value });
            }

            var fingerprint = VciProbeSelectorFingerprint.Compute(new VciSelectorFingerprintInput
            {
                SchemaVersion = VciReadProbeContract.SchemaVersion,
                RuntimeTypeName = runtimeTypeName,
                StructuralPath = fingerprintPath,
                IdentityFields = identityFields,
            });

            // Ask ObjectIdentifierProvider.GetIdentifier(candidate) as a member-level observation:
            // an unsupported or disposed candidate must never fail discovery of the rest of the
            // tree — it simply keeps StableIdentifier null, leaving the structural path/fingerprint
            // as the only selector evidence for that one candidate.
            string? stableIdentifier = null;
            if (_idProvider is not null && engineeringObject is IEngineeringObject identifiable)
            {
                try
                {
                    var identifier = _idProvider.GetIdentifier(identifiable);
                    stableIdentifier = string.IsNullOrWhiteSpace(identifier) ? null : identifier;
                }
                catch (EngineeringException)
                {
                    stableIdentifier = null;
                }
            }

            var selector = new VciEngineeringObjectSelectorInfo
            {
                StableIdentifier = stableIdentifier,
                StructuralPath = wirePath,
                Fingerprint = fingerprint,
            };

            Candidates.Add(new VciProbeEngineeringObjectCandidate(
                engineeringObject,
                runtimeTypeName,
                family,
                selector,
                fingerprint,
                _enumerationIndex));

            _enumerationIndex++;
        }

        public void RecordOmission(string reason, string budgetName, int budgetValue, int observedCount)
        {
            Omissions.Add(new VciProbeOmissionInfo
            {
                Reason = reason,
                BudgetName = budgetName,
                BudgetValue = budgetValue,
                ObservedCount = observedCount,
            });
        }
    }
}

/// <summary>The closed set of engineering-object candidate families the Task 4 catalog discovers.</summary>
public static class VciProbeEngineeringObjectFamilies
{
    public const string Project = "project";
    public const string Device = "device";
    public const string DeviceItem = "device_item";
    public const string PlcBlock = "plc_block";
    public const string PlcTagTable = "plc_tag_table";
    public const string PlcType = "plc_type";
}

/// <summary>
/// One bounded-discovery candidate: the live Siemens engineering object (valid only for the
/// duration of the worker request that produced it), its evidence-ready selector, and the data the
/// resolver needs to re-verify a selector without invoking Openness a second time within the same
/// call.
/// </summary>
public sealed class VciProbeEngineeringObjectCandidate
{
    public VciProbeEngineeringObjectCandidate(
        object engineeringObject,
        string runtimeTypeName,
        string family,
        VciEngineeringObjectSelectorInfo selector,
        string fingerprint,
        int enumerationIndex)
    {
        EngineeringObject = engineeringObject;
        RuntimeTypeName = runtimeTypeName;
        Family = family;
        Selector = selector;
        Fingerprint = fingerprint;
        EnumerationIndex = enumerationIndex;
    }

    /// <summary>The live Siemens object. Never retained past the worker request that discovered it.</summary>
    public object EngineeringObject { get; }

    /// <summary>CLR runtime type name of <see cref="EngineeringObject"/>, as observed by the worker.</summary>
    public string RuntimeTypeName { get; }

    /// <summary>One of <see cref="VciProbeEngineeringObjectFamilies"/>.</summary>
    public string Family { get; }

    /// <summary>The wire-ready selector for this candidate.</summary>
    public VciEngineeringObjectSelectorInfo Selector { get; }

    /// <summary>Same value as <see cref="VciEngineeringObjectSelectorInfo.Fingerprint"/>, kept alongside for fast resolver re-verification.</summary>
    public string Fingerprint { get; }

    /// <summary>Zero-based position in the order this catalog run discovered the candidate.</summary>
    public int EnumerationIndex { get; }
}

/// <summary>Result of one <see cref="VciProbeEngineeringObjectCatalog.Enumerate"/> call.</summary>
public sealed class VciProbeEngineeringObjectCatalogResult
{
    public VciProbeEngineeringObjectCatalogResult(
        List<VciProbeEngineeringObjectCandidate> candidates,
        List<VciProbeOmissionInfo> omissions)
    {
        Candidates = candidates;
        Omissions = omissions;
    }

    public List<VciProbeEngineeringObjectCandidate> Candidates { get; }
    public List<VciProbeOmissionInfo> Omissions { get; }
}
