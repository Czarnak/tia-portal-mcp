using System.Text.Json;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;
using static TiaMcpServer.FakeWorker.ScenarioSupport;

namespace TiaMcpServer.FakeWorker.Network;

internal sealed partial class NetworkScenarios
{
    private static HardwareConfigInfo RoundTripHardwareConfig() => new()
    {
        Devices = new List<DeviceInfo>
        {
            new()
            {
                Name = "PLC_1",
                TypeIdentifier = "OrderNumber:TEST",
                Items = new List<DeviceItemInfo>
                {
                    SelectableDeviceItem(
                        "PLC_1", 0, "PROFINET interface_1", "OrderNumber:TEST", 1,
                        "PROFINET interface_1",
                        SelectableNode(
                            "PLC_1", "X1", "node-1", "Ethernet", "192.168.0.10",
                            "255.255.255.0", "plc-1", "PN/IE_1", "IO system_1")),
                },
            },
            new()
            {
                Name = "PC_System_1",
                TypeIdentifier = "OrderNumber:PC-System",
                Items = new List<DeviceItemInfo>
                {
                    SelectableDeviceItem(
                        "PC_System_1", 0, "IE general_1", "OrderNumber:IE-General", 1,
                        "PROFINET interface_1",
                        SelectableNode(
                            "PC_System_1", "E1", "0", "Ethernet", "192.168.0.20",
                            "255.255.255.0", null, "PN/IE_1", null)),
                    SelectableDeviceItem(
                        "PC_System_1", 1, "IE general_2", "OrderNumber:IE-General", 2,
                        "PROFINET interface_2",
                        SelectableNode(
                            "PC_System_1", "E2", "1", "Ethernet", "10.0.0.20",
                            "255.255.255.0", null, "PN/IE_2", null)),
                },
            },
        },
        Subnets = new List<SubnetInfo>
        {
            SelectableSubnet(
                "PN/IE_1", "subnet-1", "Ethernet", "Ethernet",
                new[] { SelectableIoSystem("subnet-1", "IO system_1", 100, "PLC_1") },
                new[] { "PLC_1.X1", "PC_System_1.E1" }),
            SelectableSubnet(
                "PN/IE_2", "subnet-2", "Ethernet", "Ethernet",
                Array.Empty<IoSystemInfo>(),
                new[] { "PC_System_1.E2" }),
        },
    };

    private static HardwareConfigInfo ProjectCompletenessHardware() => new()
    {
        Devices = new List<DeviceInfo>
        {
            new() { Name = "Direct PLC", TypeIdentifier = "OrderNumber:CPU" },
            new() { Name = "Grouped ET200", TypeIdentifier = "OrderNumber:ET200" },
        },
    };

    private static HardwareConfigInfo HardwarePaginationUnpaged(string? projectScenario) => new()
    {
        Devices = HardwarePaginationDevices(projectScenario).Select(candidate => candidate.Device).ToList(),
        Subnets = HardwarePaginationSubnets().Select(candidate => candidate.Subnet).ToList(),
        Messages = new List<string> { "Fixture page diagnostic." },
    };

    string HardwarePaginationResponse(string requestLine, string? projectScenario)
    {
        var payload = HardwarePaginationCandidates(requestLine, projectScenario);
        return Success(WorkerJson.SerializePayload(payload));
    }

    string HardwarePaginationDerivedContinuationResponse(string requestLine, string? projectScenario)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(requestLine, WorkerJson.Envelope)
            ?? throw new JsonException("Could not deserialize the hardware-page request.");
        var payload = HardwarePaginationCandidates(requestLine, projectScenario);
        var continuation = request.HardwarePageContinuation;
        if (continuation is not null && !string.Equals(continuation.SnapshotHash, payload.SnapshotHash, StringComparison.Ordinal))
        {
            return JsonSerializer.Serialize(new { success = false, failureCategory = WorkerFailureCategories.CursorSnapshotMismatch, error = "Descriptor evidence changed after the preceding page." });
        }

        var total = payload.TotalDevices + payload.TotalSubnets;
        if (continuation is not null && continuation.Offset > total)
        {
            return JsonSerializer.Serialize(new { success = false, failureCategory = WorkerFailureCategories.CursorOutOfRange, error = "Continuation offset is outside the current descriptor range." });
        }

        return HardwarePaginationResponse(requestLine, projectScenario);
    }

    int NextHardwarePaginationScenarioCall(string scenario)
    {
        hardwarePaginationScenarioCalls.TryGetValue(scenario, out var calls);
        calls++;
        hardwarePaginationScenarioCalls[scenario] = calls;
        return calls;
    }

    HardwarePageCandidateResultInfo HardwarePaginationCandidates(string requestLine, string? projectScenario)
    {
        var request = JsonSerializer.Deserialize<WorkerRequest>(requestLine, WorkerJson.Envelope)
            ?? throw new JsonException("Could not deserialize the hardware-page request.");
        var pageSize = request.HardwarePageSize
            ?? throw new JsonException("HardwarePageSize is required.");
        var startOffset = request.HardwarePageContinuation?.Offset ?? 0;

        var devices = HardwarePaginationDevices(projectScenario)
            .Where(candidate => request.DeviceName is null
                || string.Equals(candidate.Device.Name, request.DeviceName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var subnets = HardwarePaginationSubnets();
        var all = devices.Cast<HardwarePageFixtureCandidate>()
            .Concat(subnets)
            .ToArray();
        var returned = all.Skip(startOffset).Take(pageSize).ToArray();

        var returnedDevices = returned
            .OfType<HardwarePageFixtureDevice>()
            .Select((candidate, index) => new HardwareDevicePageCandidateInfo(
                startOffset + index,
                candidate.Device,
                candidate.Messages))
            .ToArray();
        var returnedSubnets = returned
            .OfType<HardwarePageFixtureSubnet>()
            .Select((candidate, index) => new HardwareSubnetPageCandidateInfo(
                startOffset + returnedDevices.Length + index,
                candidate.Subnet,
                candidate.Messages))
            .ToArray();

        var payload = new HardwarePageCandidateResultInfo(
            OrderingVersion: 1,
            QueryHash: HardwarePageEvidence.CreateQueryHash(
                request.DeviceName,
                request.PlcName,
                request.IncludeIoDetails,
                request.IncludeTagMatches),
            SnapshotHash: "b3f6dbb2e1f86d7b9063c9d02b8b3f4fd4e565f4bf0b58830d5c34fc3c0ca1ee",
            StartOffset: startOffset,
            TotalDevices: devices.Count,
            TotalSubnets: subnets.Count,
            Messages: HardwarePaginationMessages(projectScenario),
            DeviceCandidates: returnedDevices,
            SubnetCandidates: returnedSubnets);
        return projectScenario switch
        {
            "hardware-pagination-malformed-offset" => payload with { DeviceCandidates = payload.DeviceCandidates.Select((candidate, index) => index == 0 ? candidate with { Offset = candidate.Offset + 1 } : candidate).ToArray() },
            "hardware-pagination-incoherent-counts" => payload with { TotalDevices = 0 },
            "hardware-pagination-snapshot-drift" when request.HardwarePageContinuation is not null => payload with { SnapshotHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
            "hardware-pagination-out-of-range" when request.HardwarePageContinuation is not null => payload with { TotalDevices = 0, TotalSubnets = 0, DeviceCandidates = Array.Empty<HardwareDevicePageCandidateInfo>(), SubnetCandidates = Array.Empty<HardwareSubnetPageCandidateInfo>() },
            _ => payload,
        };
    }

    IReadOnlyList<string> HardwarePaginationMessages(string? projectScenario)
        => string.Equals(projectScenario, "hardware-pagination-telemetry", StringComparison.Ordinal)
            ? new[] { "Fixture page diagnostic.", $"Worker candidate requests: {NextHardwarePaginationScenarioCall("hardware-pagination-telemetry")}." }
            : new[] { "Fixture page diagnostic." };

    private static List<HardwarePageFixtureDevice> HardwarePaginationDevices(string? projectScenario)
    {
        var devices = new List<HardwarePageFixtureDevice>
        {
            new(
                new DeviceInfo { Name = "PLC_DUP", TypeIdentifier = "OrderNumber:CPU-1515", Items = new List<DeviceItemInfo>() },
                new[] { "Nested locator fixture: Plant A/Cell 1/PLC_DUP." }),
            new(
                new DeviceInfo { Name = "PLC_DUP", TypeIdentifier = "OrderNumber:CPU-1516", Items = new List<DeviceItemInfo>() },
                new[] { "Nested locator fixture: Plant A/Cell 2/PLC_DUP." }),
            new(
                new DeviceInfo { Name = "ET200_GROUPED", TypeIdentifier = "OrderNumber:ET200", Items = new List<DeviceItemInfo>() },
                new[] { "Nested locator fixture: Plant A/Remote IO/ET200_GROUPED." }),
        };

        if (string.Equals(projectScenario, "hardware-pagination-trimming", StringComparison.Ordinal))
        {
            // Sized so a single LARGE_DEVICE candidate lands just under HardwarePageProjector's
            // 60,000-char page budget (~60 chars of headroom) while LARGE_DEVICE plus the next
            // candidate lands just over it (~60 chars over): exactly one device is kept per page.
            // Calibrated against the authenticated hardware cursor and
            // HardwarePaginationFakeWorkerTests.TrimmingScenarioProjectPath, which is a fixed,
            // already-rooted literal — see the comment there for why the path must be fixed for this
            // size to be portable across machines/checkouts.
            devices.Insert(0, new HardwarePageFixtureDevice(
                new DeviceInfo
                {
                    Name = "LARGE_DEVICE",
                    TypeIdentifier = new string('L', 58_602),
                    Items = new List<DeviceItemInfo>(),
                },
                new[] { "Candidate diagnostic: large device." }));
        }

        return devices;
    }

    private static List<HardwarePageFixtureSubnet> HardwarePaginationSubnets() => new()
    {
        new(
            SelectableSubnet(
                "PN/IE_MAIN", "subnet-main", "Ethernet", "Ethernet",
                Array.Empty<IoSystemInfo>(), new[] { "PLC_DUP.X1" }),
            new[] { "Candidate diagnostic: main subnet." }),
        new(
            SelectableSubnet(
                "PN/IE_REMOTE", "subnet-remote", "Ethernet", "Ethernet",
                Array.Empty<IoSystemInfo>(), new[] { "ET200_GROUPED.X1" }),
            new[] { "Candidate diagnostic: remote subnet." }),
    };

}
