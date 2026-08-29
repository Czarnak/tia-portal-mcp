using TiaMcpServer.Contracts;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePageProjectorTests
{
    private const int ItemLimit = 60_000;
    private const string QueryHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SnapshotHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Project_KeepsAnItemOfExactlyTheCanonicalCharacterLimit()
    {
        var projector = Projector();
        var baselinePayload = Payload(
            totalDevices: 1,
            totalSubnets: 0,
            devices: new[] { DeviceCandidate(0, "d") });
        var baseline = Project(projector, baselinePayload);
        var padding = ItemLimit - CanonicalJson.Serialize(baseline).Length;
        Assert.True(padding > 0);

        var exact = Project(
            projector,
            Payload(
                totalDevices: 1,
                totalSubnets: 0,
                devices: new[] { DeviceCandidate(0, "d" + new string('x', padding)) }));

        Assert.Equal(OperationBatchStatus.Succeeded, exact.Status);
        Assert.Equal(ItemLimit, CanonicalJson.Serialize(exact).Length);
    }

    [Fact]
    public void Project_TrimsOnlyTrailingCompleteCandidatesAndAdvancesByActualProgress()
    {
        var projector = Projector();
        var payload = Payload(
            totalDevices: 2,
            totalSubnets: 0,
            devices: new[]
            {
                DeviceCandidate(0, "first-" + new string('a', 28_000), "first-message"),
                DeviceCandidate(1, "second-" + new string('b', 35_000), "trimmed-message"),
            },
            pageMessages: new[] { "page-first" });

        var item = Project(projector, payload);

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.True(CanonicalJson.Serialize(item).Length <= ItemLimit);
        var result = CanonicalJson.Deserialize<HardwareConfigInfo>(item.Result!.Value.GetRawText());
        Assert.Single(result.Devices);
        Assert.Empty(result.Subnets);
        Assert.Equal(new[] { "page-first", "first-message" }, result.Messages);
        Assert.Equal(1, result.Pagination!.ReturnedDevices);
        Assert.Equal(0, result.Pagination.ReturnedSubnets);
        Assert.NotNull(result.Pagination.NextCursor);
        Assert.Equal(1, Codec().Decode(result.Pagination.NextCursor!).Offset);
    }

    [Fact]
    public void Project_ReturnsAllCandidatesAndNullCursorExactlyAtTheCombinedEnd()
    {
        var projector = Projector();
        var payload = Payload(
            totalDevices: 1,
            totalSubnets: 1,
            devices: new[] { DeviceCandidate(0, "device", "device-message") },
            subnets: new[] { SubnetCandidate(1, "subnet", "subnet-message") },
            pageMessages: new[] { "page-1", "page-2" });

        var item = Project(projector, payload);
        var result = CanonicalJson.Deserialize<HardwareConfigInfo>(item.Result!.Value.GetRawText());

        Assert.Equal(new[] { "device" }, result.Devices.Select(device => device.Name));
        Assert.Equal(new[] { "subnet" }, result.Subnets.Select(subnet => subnet.Name));
        Assert.Equal(
            new[] { "page-1", "page-2", "device-message", "subnet-message" },
            result.Messages);
        Assert.Equal(1, result.Pagination!.ReturnedDevices);
        Assert.Equal(1, result.Pagination.ReturnedSubnets);
        Assert.Null(result.Pagination.NextCursor);
    }

    [Fact]
    public void Project_OmitsDiagnosticsOnlyPageWithoutSubjectOrOffsetAdvance()
    {
        var projector = Projector();
        var payload = Payload(
            totalDevices: 1,
            totalSubnets: 0,
            devices: new[] { DeviceCandidate(0, "device") },
            pageMessages: new[] { new string('m', ItemLimit) });

        var item = Project(projector, payload);

        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(HardwarePageProjector.DiagnosticsLimitReason, item.Omission!.Reason);
        Assert.Null(item.Omission.Subject);
        Assert.Equal(HardwarePageProjector.RetryGuidance, item.Omission.Guidance);
        Assert.Null(item.Result);
    }

    [Fact]
    public void Project_OmitsAnOversizedFirstDeviceWithoutAnUnboundedSubject()
    {
        const string DeviceName = "oversized-device";
        var projector = Projector();
        var payload = Payload(
            totalDevices: 1,
            totalSubnets: 0,
            devices: new[] { DeviceCandidate(0, DeviceName + new string('x', ItemLimit)) });

        var item = Project(projector, payload);

        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(HardwarePageProjector.EntityLimitReason, item.Omission!.Reason);
        Assert.Null(item.Omission.Subject);
        Assert.True(CanonicalJson.Serialize(item).Length <= ItemLimit);
    }

    [Fact]
    public void Project_OmitsAnOversizedFirstSubnetWithoutAnUnboundedSubject()
    {
        const string SubnetName = "oversized-subnet";
        var projector = Projector();
        var payload = Payload(
            totalDevices: 0,
            totalSubnets: 1,
            subnets: new[] { SubnetCandidate(0, SubnetName + new string('x', ItemLimit)) });

        var item = Project(projector, payload);
        var serialized = CanonicalJson.Serialize(item);

        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(HardwarePageProjector.EntityLimitReason, item.Omission!.Reason);
        Assert.Null(item.Omission.Subject);
        Assert.True(serialized.Length <= ItemLimit);
        Assert.DoesNotContain("locator", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(QueryHash, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(SnapshotHash, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("worker-session", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_KeepsTheFullEntitySubjectWhenItsCanonicalOmissionFitsExactlyTheLimit()
    {
        var warnings = new[] { "public warning" };
        var subjectName = EntitySubjectNameAtExactLimit(warnings);
        var item = Project(
            Projector(),
            Payload(
                totalDevices: 1,
                totalSubnets: 0,
                devices: new[] { DeviceCandidate(0, subjectName, new string('m', ItemLimit)) }),
            warnings);

        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(subjectName, item.Omission!.Subject!.Name);
        Assert.Equal(warnings, item.Warnings);
        Assert.Equal(ItemLimit, CanonicalJson.Serialize(item).Length);
    }

    [Fact]
    public void Project_DropsTheEntitySubjectWhenItsCanonicalOmissionWouldExceedTheLimit()
    {
        var warnings = new[] { "public warning" };
        var oversizedName = EntitySubjectNameAtExactLimit(warnings) + "x";
        var item = Project(
            Projector(),
            Payload(
                totalDevices: 1,
                totalSubnets: 0,
                devices: new[] { DeviceCandidate(0, oversizedName, new string('m', ItemLimit)) }),
            warnings);

        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(HardwarePageProjector.EntityLimitReason, item.Omission!.Reason);
        Assert.Null(item.Omission.Subject);
        Assert.Equal(HardwarePageProjector.RetryGuidance, item.Omission.Guidance);
        Assert.Equal(warnings, item.Warnings);
        Assert.True(CanonicalJson.Serialize(item).Length <= ItemLimit);
    }

    [Fact]
    public void Project_DropsWarningsThatCannotFitTheDiagnosticsOmission()
    {
        var warnings = new[] { new string('w', ItemLimit) };
        var item = Project(
            Projector(),
            Payload(
                totalDevices: 1,
                totalSubnets: 0,
                devices: new[] { DeviceCandidate(0, new string('d', ItemLimit)) }),
            warnings);

        Assert.Equal(OperationBatchStatus.Omitted, item.Status);
        Assert.Equal(HardwarePageProjector.DiagnosticsLimitReason, item.Omission!.Reason);
        Assert.Null(item.Omission.Subject);
        Assert.Empty(item.Warnings);
        Assert.True(CanonicalJson.Serialize(item).Length <= ItemLimit);
    }

    private static HardwarePageProjector Projector() => new(Codec());

    private static HardwarePageCursorCodec Codec() => new(new byte[32]);

    private static StructuredOperationItem Project(
        HardwarePageProjector projector,
        HardwarePageCandidateResultInfo payload,
        IReadOnlyList<string>? warnings = null)
        => projector.Project(
            Operation(),
            payload,
            ResolvedPath(),
            Identity(),
            Unbound(),
            warnings ?? Array.Empty<string>(),
            maxItemChars: ItemLimit);

    private static string EntitySubjectNameAtExactLimit(IReadOnlyList<string> warnings)
    {
        var operation = Operation();
        var emptyNameOmission = new StructuredOperationItem(
            operation.OperationId,
            operation.Operation,
            OperationBatchStatus.Omitted,
            Result: null,
            Failure: null,
            new StructuredOperationOmission(
                HardwarePageProjector.EntityLimitReason,
                ItemLimit,
                ItemLimit * 2,
                "network_read",
                HardwarePageProjector.RetryGuidance,
                new StructuredOperationOmissionSubject("device", string.Empty, Identifier: null)),
            SkipReason: null,
            warnings);
        var nameChars = ItemLimit - CanonicalJson.Serialize(emptyNameOmission).Length;
        Assert.True(nameChars > 0);
        return new string('n', nameChars);
    }

    private static HardwarePageCandidateResultInfo Payload(
        int totalDevices,
        int totalSubnets,
        IReadOnlyList<HardwareDevicePageCandidateInfo>? devices = null,
        IReadOnlyList<HardwareSubnetPageCandidateInfo>? subnets = null,
        IReadOnlyList<string>? pageMessages = null,
        int startOffset = 0)
        => new(
            OrderingVersion: 1,
            QueryHash,
            SnapshotHash,
            StartOffset: startOffset,
            TotalDevices: totalDevices,
            TotalSubnets: totalSubnets,
            Messages: pageMessages ?? Array.Empty<string>(),
            DeviceCandidates: devices ?? Array.Empty<HardwareDevicePageCandidateInfo>(),
            SubnetCandidates: subnets ?? Array.Empty<HardwareSubnetPageCandidateInfo>());

    private static HardwareDevicePageCandidateInfo DeviceCandidate(
        int offset,
        string name,
        params string[] messages)
        => new(offset, HardwarePagePayloadContractTests.Device(name), messages);

    private static HardwareSubnetPageCandidateInfo SubnetCandidate(
        int offset,
        string name,
        params string[] messages)
        => new(offset, HardwarePagePayloadContractTests.Subnet(name), messages);

    private static NetworkOperationRequest Operation() => new()
    {
        OperationId = "hardware",
        Operation = "read_hardware_config",
        PageSize = 2,
    };

    private static string ResolvedPath() => @"C:\Projects\Paged.ap21";

    private static WorkerSessionIdentity Identity() => new()
    {
        WorkerSessionId = "worker-session",
        SessionGeneration = 3,
        PortalProcessId = 42,
        ProjectPath = ResolvedPath(),
    };

    private static ProjectBindingSnapshot Unbound() => new(
        ProjectBindingSnapshot.UnboundState,
        "unbound-binding",
        0,
        projectPath: null,
        workerSessionId: null,
        sessionGeneration: null,
        portalProcessId: null,
        invalidatedReason: null);
}
