using System.Collections;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkPortalLossTests
{
    [Theory]
    [InlineData("acquire")]
    [InlineData("enumerator")]
    [InlineData("moveNext")]
    [InlineData("current")]
    [InlineData("materialize")]
    [InlineData("dispose")]
    public void HardwareTraversal_FatalFailureEscapesWithoutDegradationOrFurtherCandidates(string site)
    {
        var failure = new NonRecoverableException("closed");
        var messages = new List<string>();
        var capture = new HardwareDiscoveryEvidenceCapture("project", messages.Add);
        var visited = 0;

        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => capture.Traverse(
            () => site == "acquire" ? throw failure : new FailingEnumerable<int>(new[] { 1, 2 }, site, failure),
            _ => { visited++; if (site == "materialize") throw failure; }, "enumeration", "materialization")));

        Assert.Equal(site == "dispose" ? 2 : site == "materialize" ? 1 : 0, visited);
        Assert.Empty(messages);
        Assert.Empty(capture.Evidence.Failures);
    }

    [Theory]
    [InlineData("acquire")]
    [InlineData("enumerator")]
    [InlineData("moveNext")]
    [InlineData("current")]
    [InlineData("dispose")]
    [InlineData("name")]
    [InlineData("access")]
    [InlineData("value")]
    [InlineData("types")]
    [InlineData("typeName")]
    [InlineData("types-enumerator")]
    [InlineData("types-moveNext")]
    [InlineData("types-current")]
    [InlineData("types-dispose")]
    public void AttributeMetadata_FatalFailureStopsInspection(string site)
    {
        var failure = new NonRecoverableException("closed");
        var followingReads = 0;
        var first = Entry("A", () => 1);
        var following = Entry("B", () => { followingReads++; return 2; });
        if (site == "name") first.ReadName = () => throw failure;
        if (site == "access") first.ReadAccess = () => throw failure;
        if (site == "value") first.ReadValue = () => throw failure;
        if (site == "types") first.ReadSupportedTypes = () => throw failure;
        if (site == "typeName") first.ReadSupportedTypes = () => new[]
            { new NetworkAttributeSupportedTypeMetadata { ReadName = () => throw failure } };
        if (site.StartsWith("types-", StringComparison.Ordinal)) first.ReadSupportedTypes = () =>
            new FailingEnumerable<NetworkAttributeSupportedTypeMetadata>(
                new[] { new NetworkAttributeSupportedTypeMetadata { ReadName = () => "System.Int32" } },
                site.Substring("types-".Length), failure);

        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => NetworkAttributeMetadataProcessor.Process(
            () => site == "acquire" ? throw failure : new FailingEnumerable<NetworkAttributeMetadataEntry>(
                new[] { first, following }, site, failure), null)));
        Assert.Equal(site == "dispose" ? 1 : 0, followingReads);
    }

    [Theory]
    [InlineData("acquire")]
    [InlineData("enumerator")]
    [InlineData("moveNext")]
    [InlineData("current")]
    [InlineData("dispose")]
    [InlineData("identity")]
    [InlineData("display")]
    public void SubnetConnections_FatalFailureEscapesCapture(string site)
    {
        var failure = new NonRecoverableException("closed");
        var visited = 0;
        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => NetworkConnectionEvidenceCapture.CaptureSubnet(
            () => site == "acquire" ? throw failure : new FailingEnumerable<int>(new[] { 1, 2 }, site, failure),
            value =>
            {
                visited++;
                return site == "identity" ? throw failure : new NetworkNodeIdentityInfo { DeviceName = "A", NodeId = "E" + value };
            },
            (_, _) => { if (site == "display") throw failure; })));
        Assert.Equal(site == "dispose" ? 2 : site is "identity" or "display" ? 1 : 0, visited);
    }

    [Theory]
    [InlineData("subnet")]
    [InlineData("ioSystem")]
    [InlineData("display")]
    public void NodeConnections_FatalFailureStopsFollowingReads(string site)
    {
        var failure = new NonRecoverableException("closed");
        var displayCalls = 0;
        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => NetworkConnectionEvidenceCapture.CaptureNode(
            () => site == "subnet" ? throw failure : "subnet-a",
            () => site == "ioSystem" ? throw failure : (null, null),
            _ => { displayCalls++; if (site == "display") throw failure; })));
        Assert.Equal(site == "display" ? 1 : 0, displayCalls);
    }

    [Fact]
    public void NodeIdentity_FatalFailureIsNotAnEvidenceMismatch()
    {
        var failure = new NonRecoverableException("closed");
        var reads = 0;
        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => NetworkNodeReadSelectorBuilder.MatchNode(
            new[] { "A", "B" }, "A", null, _ => { reads++; throw failure; })));
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttributeValue_FatalFailureDoesNotTryOtherSurfacesOrAttributes(bool modeled)
    {
        var failure = new NonRecoverableException("closed");
        var followingReads = 0;
        var failed = new NetworkAttributeObservation { Name = "A", Availability = "available", ReadValue = () => throw failure };
        var following = new NetworkAttributeObservation
            { Name = "B", Availability = "available", ReadValue = () => { followingReads++; return 2; } };
        var alternate = new NetworkAttributeObservation
            { Name = "A", Availability = "available", ReadValue = () => { followingReads++; return 1; } };

        Assert.Same(failure, Assert.Throws<NonRecoverableException>(() => NetworkAttributeResultBuilder.Build(
            modeled ? new[] { failed, following } : Array.Empty<NetworkAttributeObservation>(),
            modeled ? new[] { alternate } : new[] { failed, following })));
        Assert.Equal(0, followingReads);
    }

    private static NetworkAttributeMetadataEntry Entry(string name, Func<object?> read) => new()
    {
        ReadName = () => name,
        ReadAccess = () => new() { CanRead = true, CanWrite = false },
        ReadSupportedTypes = () => Array.Empty<NetworkAttributeSupportedTypeMetadata>(),
        ReadValue = read,
    };

    private sealed class FailingEnumerable<T>(IReadOnlyList<T> items, string site, Exception failure) : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => site == "enumerator" ? throw failure : new Enumerator(items, site, failure);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator(IReadOnlyList<T> items, string site, Exception failure) : IEnumerator<T>
        {
            private int index = -1;
            public T Current => site == "current" ? throw failure : items[index];
            object IEnumerator.Current => Current!;
            public bool MoveNext() => site == "moveNext" ? throw failure : ++index < items.Count;
            public void Dispose() { if (site == "dispose") throw failure; }
            public void Reset() => throw new NotSupportedException();
        }
    }
}
