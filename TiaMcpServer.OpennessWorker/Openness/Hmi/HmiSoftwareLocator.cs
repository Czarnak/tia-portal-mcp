using Siemens.Engineering;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Project;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

using Project = Siemens.Engineering.Project;

public sealed class DiscoveredHmiSoftware
{
    public DiscoveredHmiSoftware(string deviceName, HmiSoftware software)
    {
        DeviceName = deviceName;
        Software = software;
    }

    public string DeviceName { get; }

    public HmiSoftware Software { get; }
}

public sealed class DiscoveredHmiEntry
{
    public DiscoveredHmiEntry(string deviceName, string softwareName, string kind, string? typeIdentifier, HmiSoftware? unified)
    {
        DeviceName = deviceName;
        SoftwareName = softwareName;
        Kind = kind;
        TypeIdentifier = typeIdentifier;
        Unified = unified;
    }

    public string DeviceName { get; }

    public string SoftwareName { get; }

    /// <summary><c>unified</c> or <c>classic</c>.</summary>
    public string Kind { get; }

    public string? TypeIdentifier { get; }

    /// <summary>The software for a Unified entry; null for Classic, which the worker never touches.</summary>
    public HmiSoftware? Unified { get; }
}

public static class HmiSoftwareLocator
{
    public const string KindUnified = "unified";
    public const string KindClassic = "classic";

    // The Classic runtime type is recognised by name so the worker never references Siemens.Engineering.WinCC.dll.
    private const string ClassicSoftwareTypeName = "Siemens.Engineering.Hmi.HmiTarget";

    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, Found> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static ProjectBase? cachedRoot;

    internal static void ResetCache()
    {
        lock (CacheGate)
        {
            Cache.Clear();
            cachedRoot = null;
        }
    }

    /// <summary>
    /// The read-path resolver: exactly one Unified HMI whose software or device name equals
    /// <paramref name="hmiName"/> case-insensitively (any Unified HMI when null). Zero matches throw
    /// <c>target_not_found</c>, several <c>target_ambiguous</c>, a Classic match <c>target_kind_unsupported</c>.
    /// Unreadable device items fail the call; original Openness exceptions reach the worker boundary for Portal-loss classification.
    /// </summary>
    public static DiscoveredHmiSoftware FindUnique(ProjectBase project, string? hmiName)
    {
        lock (CacheGate)
        {
            // Evict on a different handle without invoking native equality on a possibly disposed cached root.
            if (!ReferenceEquals(cachedRoot, project))
            {
                Cache.Clear();
                cachedRoot = project;
            }

            if (hmiName is not null && Cache.TryGetValue(hmiName, out var cached))
            {
                var live = false;
                try
                {
                    var softwareName = ((HmiSoftware)cached.Software).Name;
                    var deviceName = cached.Device.Name;
                    live = string.Equals(softwareName, hmiName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(deviceName, hmiName, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception exception) when (OpennessExceptions.IsRecoverable(exception))
                {
                    // A stale Openness proxy must be relocated through the strict discovery path.
                }

                // Liveness cannot prove uniqueness after another device or software is added or renamed.
                if (live) return Locate(project, hmiName);
                Cache.Remove(hmiName);
            }

            return Locate(project, hmiName);
        }
    }

    private static DiscoveredHmiSoftware Locate(ProjectBase project, string? hmiName)
    {
        Found? match = null;
        Found? classicSeen = null;
        foreach (var found in Walk(project, messages: null))
        {
            if (hmiName is null
                ? found.Kind != KindUnified
                : !string.Equals(found.SoftwareName, hmiName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(found.DeviceName, hmiName, StringComparison.OrdinalIgnoreCase))
            {
                classicSeen ??= found.Kind == KindClassic ? found : null;
                continue;
            }

            if (match is not null)
            {
                throw new WorkerOperationException(
                    WorkerFailureCategories.TargetAmbiguous,
                    $"Several HMI software instances match{Describe(hmiName)}. Specify an unambiguous hmiName (see list_hmi_devices).");
            }

            match = found;
        }

        if (match is null && classicSeen is not null && hmiName is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetKindUnsupported,
                $"The project's HMI '{classicSeen.SoftwareName}' on device '{classicSeen.DeviceName}' is a Classic (WinCC) HMI; only WinCC Unified HMIs are supported.");
        }

        if (match is null)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"No Unified HMI software{Describe(hmiName)} was found in the project. Use list_hmi_devices to see the HMI devices.");
        }

        if (match.Software is not HmiSoftware unified)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetKindUnsupported,
                $"HMI '{match.SoftwareName}' on device '{match.DeviceName}' is a Classic (WinCC) HMI; only WinCC Unified HMIs are supported.");
        }

        if (hmiName is not null)
        {
            Cache[hmiName] = match;
        }

        return new DiscoveredHmiSoftware(match.DeviceName, unified);
    }

    /// <summary>
    /// Every Unified and Classic HMI in the project (root, grouped and ungrouped devices). With
    /// <paramref name="messages"/> an unreadable device item is reported there and skipped; without it the call fails.
    /// </summary>
    public static IEnumerable<DiscoveredHmiEntry> EnumerateAll(ProjectBase project, ICollection<string>? messages = null)
    {
        foreach (var found in Walk(project, messages))
        {
            yield return new DiscoveredHmiEntry(
                found.DeviceName,
                found.SoftwareName,
                found.Kind,
                ReadTypeIdentifier(found, messages),
                found.Software as HmiSoftware);
        }
    }

    private static string Describe(string? hmiName) => hmiName is null ? string.Empty : $" named '{hmiName}'";

    private sealed class Found
    {
        public Found(Device device, string deviceName, DeviceItem host, object software, string softwareName, string kind)
        {
            Device = device;
            DeviceName = deviceName;
            Host = host;
            Software = software;
            SoftwareName = softwareName;
            Kind = kind;
        }

        public Device Device { get; }

        public string DeviceName { get; }

        public DeviceItem Host { get; }

        public object Software { get; }

        public string SoftwareName { get; }

        public string Kind { get; }
    }

    private static IEnumerable<Found> Walk(ProjectBase project, ICollection<string>? messages)
    {
        foreach (var device in ProjectDeviceEnumerator.Enumerate(project))
        {
            string deviceName;
            try
            {
                deviceName = device.Name;
            }
            catch (EngineeringException ex) when (messages is not null)
            {
                messages.Add($"A device name could not be read while locating HMI software: {ex.Message}");
                continue;
            }

            foreach (var found in WalkItems(device, deviceName, device.DeviceItems, messages))
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<Found> WalkItems(
        Device device, string deviceName, DeviceItemComposition items, ICollection<string>? messages)
    {
        using var enumerator = items.GetEnumerator();
        while (true)
        {
            DeviceItem? item = null;
            try
            {
                if (enumerator.MoveNext())
                {
                    item = enumerator.Current;
                }
            }
            catch (EngineeringException ex) when (messages is not null)
            {
                messages.Add($"A device item could not be read while locating HMI software: {ex.Message}");
            }

            if (item is null)
            {
                yield break;
            }

            Found? found = null;
            try
            {
                var software = item.GetService<SoftwareContainer>()?.Software;
                var kind = KindOf(software);
                if (kind is not null)
                {
                    found = new Found(device, deviceName, item, software!, ((Software)software!).Name, kind);
                }
            }
            catch (EngineeringException ex) when (messages is not null)
            {
                messages.Add($"A device item could not be read while locating HMI software: {ex.Message}");
            }

            if (found is not null)
            {
                yield return found;
            }

            foreach (var child in WalkItems(device, deviceName, item.DeviceItems, messages))
            {
                yield return child;
            }
        }
    }

    private static string? KindOf(object? software)
    {
        if (software is HmiSoftware)
        {
            return KindUnified;
        }

        return software is not null && software.GetType().FullName == ClassicSoftwareTypeName ? KindClassic : null;
    }

    /// <summary>
    /// Comfort panels carry the type identifier (order number) on the head device item (named like the device),
    /// PC stations on the software item; <c>Device.TypeIdentifier</c> is null on Comfort and only <c>System:Device.PC</c>
    /// on a PC station, so it is the last resort.
    /// </summary>
    private static string? ReadTypeIdentifier(Found found, ICollection<string>? messages)
    {
        string? Read(Func<string?> get, string what)
        {
            try
            {
                return get();
            }
            catch (EngineeringException ex) when (HmiReadLog.IsRecoverable(ex))
            {
                messages?.Add($"The type identifier of {what} could not be read: {ex.Message}");
                return null;
            }
        }

        var id = Read(() => found.Host.TypeIdentifier, $"the software item of '{found.SoftwareName}'");
        if (id is not null)
        {
            return id;
        }

        foreach (DeviceItem item in found.Device.DeviceItems)
        {
            if (Read(() => item.Name, "a device item") == found.DeviceName)
            {
                id = Read(() => item.TypeIdentifier, $"the head item of '{found.DeviceName}'");
                break;
            }
        }

        return id ?? Read(() => found.Device.TypeIdentifier, $"device '{found.DeviceName}'");
    }
}
