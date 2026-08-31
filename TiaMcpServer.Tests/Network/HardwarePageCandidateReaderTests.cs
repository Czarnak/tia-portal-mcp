using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class HardwarePageCandidateReaderTests
{
    [Fact]
    public void Read_EnumeratesAndValidatesTheFullSnapshotBeforeMaterializingTheSelectedWindow()
    {
        var events = new List<string>();
        var descriptors = DescriptorSet();
        var continuation = Continuation(descriptors, offset: 1);
        var source = new HardwarePageCandidateSource(
            enumerate: () =>
            {
                events.Add("enumerate-full-set");
                return new HardwarePageCandidateInventory(descriptors, Array.Empty<string>());
            },
            materialize: descriptor =>
            {
                events.Add($"materialize:{descriptor.StructuralLocator}");
                return Materialize(descriptor);
            });

        var result = HardwarePageCandidateReader.Read(
            source,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            requestedPageSize: 2,
            continuation);

        Assert.Equal(
            new[] { "enumerate-full-set", "materialize:devices/1", "materialize:subnets/0" },
            events);
        Assert.Equal(new[] { 1 }, result.DeviceCandidates.Select(candidate => candidate.Offset));
        Assert.Equal(new[] { 2 }, result.SubnetCandidates.Select(candidate => candidate.Offset));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Read_RejectsChangedOrderingOrSnapshotBeforeMaterialization(
        bool changeOrderingVersion,
        bool changeSnapshot)
    {
        var descriptors = DescriptorSet();
        var continuation = new HardwarePageContinuationInfo(
            descriptors.OrderingVersion + (changeOrderingVersion ? 1 : 0),
            QueryHash(),
            changeSnapshot ? new string('0', 64) : descriptors.SnapshotHash,
            1);
        var materialized = 0;
        var source = Source(
            descriptors,
            materialize: descriptor =>
            {
                materialized++;
                return Materialize(descriptor);
            });

        var exception = Assert.Throws<WorkerOperationException>(() => Read(source, continuation, pageSize: 2));

        Assert.Equal(WorkerFailureCategories.CursorSnapshotMismatch, exception.FailureCategory);
        Assert.Equal(0, materialized);
    }

    [Fact]
    public void Read_ReportsAQueryMismatchBeforeAnySnapshotDifference()
    {
        var descriptors = DescriptorSet();
        var continuation = new HardwarePageContinuationInfo(
            descriptors.OrderingVersion,
            new string('f', 64),
            new string('0', 64),
            1);

        var exception = Assert.Throws<WorkerOperationException>(() =>
            Read(Source(descriptors), continuation, pageSize: 2));

        Assert.Equal(WorkerFailureCategories.CursorFilterMismatch, exception.FailureCategory);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Read_RejectsOffsetsOutsideTheCurrentCandidateRangeBeforeMaterialization(int offset)
    {
        var descriptors = DescriptorSet();
        var materialized = 0;
        var source = Source(
            descriptors,
            materialize: descriptor =>
            {
                materialized++;
                return Materialize(descriptor);
            });

        var exception = Assert.Throws<WorkerOperationException>(() =>
            Read(source, Continuation(descriptors, offset), pageSize: 2));

        Assert.Equal(WorkerFailureCategories.CursorOutOfRange, exception.FailureCategory);
        Assert.Equal(0, materialized);
    }

    [Fact]
    public void Read_AllowsAnOffsetAtTheCurrentCountAndReturnsAnEmptyWindow()
    {
        var descriptors = DescriptorSet();
        var materialized = 0;
        var source = Source(
            descriptors,
            materialize: descriptor =>
            {
                materialized++;
                return Materialize(descriptor);
            });

        var result = Read(source, Continuation(descriptors, descriptors.Descriptors.Count), pageSize: 2);

        Assert.Empty(result.DeviceCandidates);
        Assert.Empty(result.SubnetCandidates);
        Assert.Equal(0, materialized);
    }

    [Fact]
    public void Read_KeepsPageAndCandidateMessagesInTheirOwnScopes()
    {
        var descriptors = DescriptorSet();
        var source = new HardwarePageCandidateSource(
            enumerate: () => new HardwarePageCandidateInventory(
                descriptors,
                new[] { "page-level" }),
            materialize: descriptor => descriptor.Kind == HardwarePageDescriptorKind.Device
                ? HardwarePageCandidateMaterialization.ForDevice(
                    new DeviceInfo { Name = descriptor.PublicIdentity },
                    new[] { $"device:{descriptor.PublicIdentity}" })
                : HardwarePageCandidateMaterialization.ForSubnet(
                    new SubnetInfo { SubnetId = descriptor.PublicIdentity },
                    new[] { $"subnet:{descriptor.PublicIdentity}" }));

        var result = HardwarePageCandidateReader.Read(
            source,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            requestedPageSize: 3,
            continuation: null);

        Assert.Equal(new[] { "page-level" }, result.Messages);
        Assert.Equal(new[] { "device:A" }, result.DeviceCandidates[0].Messages);
        Assert.Equal(new[] { "device:B" }, result.DeviceCandidates[1].Messages);
        Assert.Equal(new[] { "subnet:s1" }, result.SubnetCandidates[0].Messages);
        Assert.DoesNotContain("device:A", result.Messages);
        Assert.DoesNotContain("page-level", result.DeviceCandidates[0].Messages);
    }

    [Fact]
    public void Read_DefaultsToFiftyOnlyForACursorOnlyRequest()
    {
        var descriptors = new HardwarePageDescriptorSet(
            Enumerable.Range(0, 60).Select(index => Device($"D{index:D2}", $"devices/{index}", index)));
        var source = Source(descriptors);

        var continued = HardwarePageCandidateReader.Read(
            source,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            requestedPageSize: null,
            Continuation(descriptors, offset: 0));
        var firstPageException = Assert.Throws<WorkerOperationException>(() =>
            HardwarePageCandidateReader.Read(
                source,
                deviceName: null,
                plcName: null,
                includeIoDetails: false,
                includeTagMatches: false,
                requestedPageSize: null,
                continuation: null));

        Assert.Equal(50, continued.DeviceCandidates.Count);
        Assert.Equal(WorkerFailureCategories.ValidationError, firstPageException.FailureCategory);
    }

    [Fact]
    public void Read_RejectsTagMatchesWithoutIoDetailsBeforeEnumeration()
    {
        var enumerated = 0;
        var source = new HardwarePageCandidateSource(
            enumerate: () =>
            {
                enumerated++;
                return new HardwarePageCandidateInventory(DescriptorSet(), Array.Empty<string>());
            },
            materialize: Materialize);

        var exception = Assert.Throws<WorkerOperationException>(() =>
            HardwarePageCandidateReader.Read(
                source,
                deviceName: null,
                plcName: null,
                includeIoDetails: false,
                includeTagMatches: true,
                requestedPageSize: 2,
                continuation: null));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
        Assert.Equal(0, enumerated);
    }

    private static HardwarePageCandidateResultInfo Read(
        HardwarePageCandidateSource source,
        HardwarePageContinuationInfo continuation,
        int pageSize)
        => HardwarePageCandidateReader.Read(
            source,
            deviceName: null,
            plcName: null,
            includeIoDetails: false,
            includeTagMatches: false,
            requestedPageSize: pageSize,
            continuation);

    private static HardwarePageCandidateSource Source(
        HardwarePageDescriptorSet descriptors,
        Func<HardwarePageDescriptor, HardwarePageCandidateMaterialization>? materialize = null)
        => new(
            enumerate: () => new HardwarePageCandidateInventory(descriptors, Array.Empty<string>()),
            materialize: materialize ?? Materialize);

    private static HardwarePageCandidateMaterialization Materialize(HardwarePageDescriptor descriptor)
        => descriptor.Kind == HardwarePageDescriptorKind.Device
            ? HardwarePageCandidateMaterialization.ForDevice(
                new DeviceInfo { Name = descriptor.PublicIdentity },
                Array.Empty<string>())
            : HardwarePageCandidateMaterialization.ForSubnet(
                new SubnetInfo { SubnetId = descriptor.PublicIdentity },
                Array.Empty<string>());

    private static HardwarePageDescriptorSet DescriptorSet()
        => new(new[]
        {
            Device("A", "devices/0", 0),
            Device("B", "devices/1", 1),
            Subnet("s1", "subnets/0", 0),
            Subnet("s2", "subnets/1", 1),
        });

    private static HardwarePageContinuationInfo Continuation(
        HardwarePageDescriptorSet descriptors,
        int offset)
        => new(descriptors.OrderingVersion, QueryHash(), descriptors.SnapshotHash, offset);

    private static string QueryHash()
        => HardwarePageEvidence.CreateQueryHash(null, null, false, false);

    private static HardwarePageDescriptor Device(string name, string locator, int sourceOrder)
        => new(HardwarePageDescriptorKind.Device, name, locator, sourceOrder);

    private static HardwarePageDescriptor Subnet(string subnetId, string locator, int sourceOrder)
        => new(HardwarePageDescriptorKind.Subnet, subnetId, locator, sourceOrder);
}
