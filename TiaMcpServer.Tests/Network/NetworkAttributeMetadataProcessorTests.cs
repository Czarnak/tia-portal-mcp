using System.Collections;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkAttributeMetadataProcessorTests
{
    [Fact]
    public void Process_CollectionAcquisitionFailure_PreservesModeledAndExplicitRequestedResults()
    {
        var processed = NetworkAttributeMetadataProcessor.Process(
            () => throw new InvalidOperationException("metadata unavailable"),
            new[] { "Modeled", "RequestedOnly" });

        Assert.Equal(new[] { "Modeled", "RequestedOnly" }, processed.Observations.Select(x => x.Name));
        Assert.Single(processed.Diagnostics);

        var attributes = NetworkAttributeResultBuilder.Build(
            new[] { Observation("Modeled", () => 7) },
            processed.Observations,
            new[] { "Modeled", "RequestedOnly" });
        Assert.Equal("available", attributes[0].Availability);
        Assert.Equal(7L, attributes[0].Value!.Value);
        Assert.Equal("readFailed", attributes[1].Availability);
        Assert.Equal("read_error", attributes[1].Diagnostic!.Category);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void Process_MoveNextFailure_FirstMiddleOrLast_DoesNotSuppressEntries(int failingCall)
    {
        var entries = ThreeEntries();
        var processed = NetworkAttributeMetadataProcessor.Process(
            () => new ThrowOnceEnumerable<NetworkAttributeMetadataEntry>(
                entries,
                moveNextFailureCall: failingCall),
            attributeNames: null);

        Assert.Equal(new[] { "First", "Last", "Middle" }, processed.Observations.Select(x => x.Name));
        Assert.Contains(processed.Diagnostics, message => message.Contains("enumeration", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Process_CurrentFailure_FirstMiddleOrLast_PreservesEveryOtherEntry(int failingIndex)
    {
        var entries = ThreeEntries();
        var processed = NetworkAttributeMetadataProcessor.Process(
            () => new ThrowOnceEnumerable<NetworkAttributeMetadataEntry>(
                entries,
                currentFailureIndex: failingIndex),
            attributeNames: null);

        var expected = entries
            .Where((_, index) => index != failingIndex)
            .Select(entry => entry.ReadName!())
            .OrderBy(name => name, StringComparer.Ordinal);
        Assert.Equal(expected, processed.Observations.Select(x => x.Name));
        Assert.Contains(processed.Diagnostics, message => message.Contains("current entry", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("name", 0)]
    [InlineData("name", 1)]
    [InlineData("name", 2)]
    [InlineData("access", 0)]
    [InlineData("access", 1)]
    [InlineData("access", 2)]
    [InlineData("supportedTypes", 0)]
    [InlineData("supportedTypes", 1)]
    [InlineData("supportedTypes", 2)]
    public void Process_MetadataPropertyFailure_FirstMiddleOrLast_IsIsolated(
        string property,
        int failingIndex)
    {
        var entries = ThreeEntries();
        var failing = entries[failingIndex];
        switch (property)
        {
            case "name":
                failing.ReadName = () => throw new InvalidOperationException("name failed");
                break;
            case "access":
                failing.ReadAccess = () => throw new InvalidOperationException("access failed");
                break;
            case "supportedTypes":
                failing.ReadSupportedTypes = () => throw new InvalidOperationException("types failed");
                break;
        }

        var processed = NetworkAttributeMetadataProcessor.Process(() => entries, attributeNames: null);

        var minimumCount = property == "name" ? 2 : 3;
        Assert.Equal(minimumCount, processed.Observations.Count);
        Assert.Contains(processed.Diagnostics, message => message.Contains("could not be read", StringComparison.Ordinal));
        Assert.Contains(processed.Observations, observation => observation.Name == "Last"
            || failingIndex == 2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Process_ValueFailure_FirstMiddleOrLast_BuilderPreservesOtherValues(int failingIndex)
    {
        var entries = ThreeEntries();
        entries[failingIndex].ReadValue = () => throw new InvalidOperationException("value failed");

        var processed = NetworkAttributeMetadataProcessor.Process(() => entries, attributeNames: null);
        var attributes = NetworkAttributeResultBuilder.Build(
            Array.Empty<NetworkAttributeObservation>(),
            processed.Observations);

        Assert.Equal(3, attributes.Count);
        Assert.Equal("readFailed", attributes.Single(x => x.Name == entries[failingIndex].ReadName!()).Availability);
        foreach (var attribute in attributes.Where(x => x.Name != entries[failingIndex].ReadName!()))
        {
            Assert.Equal("available", attribute.Availability);
        }
    }

    [Fact]
    public void Process_SupportedTypeEnumerationAndTypeNameFailures_AreIsolated()
    {
        var entries = ThreeEntries();
        entries[0].ReadSupportedTypes = () => new ThrowOnceEnumerable<NetworkAttributeSupportedTypeMetadata>(
            new[]
            {
                new NetworkAttributeSupportedTypeMetadata { ReadName = () => "System.String" },
                new NetworkAttributeSupportedTypeMetadata { ReadName = () => "System.Int32" },
            },
            moveNextFailureCall: 1);
        entries[1].ReadSupportedTypes = () => new[]
        {
            new NetworkAttributeSupportedTypeMetadata
            {
                ReadName = () => throw new InvalidOperationException("type name failed"),
            },
            new NetworkAttributeSupportedTypeMetadata { ReadName = () => "System.Boolean" },
        };

        var processed = NetworkAttributeMetadataProcessor.Process(() => entries, attributeNames: null);

        Assert.Equal(
            new[] { "System.Int32", "System.String" },
            processed.Observations.Single(observation => observation.Name == "First").SupportedTypes);
        Assert.Equal(
            new[] { "System.Boolean" },
            processed.Observations.Single(observation => observation.Name == "Middle").SupportedTypes);
        Assert.Contains(processed.Observations, observation => observation.Name == "Last");
        Assert.True(processed.Diagnostics.Count >= 2);
    }

    private static List<NetworkAttributeMetadataEntry> ThreeEntries() => new()
    {
        Entry("First", 1),
        Entry("Middle", 2),
        Entry("Last", 3),
    };

    private static NetworkAttributeMetadataEntry Entry(string name, int value) => new()
    {
        ReadName = () => name,
        ReadAccess = () => new NetworkAttributeAccessMetadata { CanRead = true, CanWrite = false },
        ReadSupportedTypes = () => new[]
        {
            new NetworkAttributeSupportedTypeMetadata { ReadName = () => "System.Int32" },
        },
        ReadValue = () => value,
    };

    private static NetworkAttributeObservation Observation(string name, Func<object?> read) => new()
    {
        Name = name,
        CanRead = true,
        CanWrite = false,
        ReadValue = read,
    };

    private sealed class ThrowOnceEnumerable<T> : IEnumerable<T>
    {
        private readonly IReadOnlyList<T> _items;
        private readonly int? _moveNextFailureCall;
        private readonly int? _currentFailureIndex;

        public ThrowOnceEnumerable(
            IReadOnlyList<T> items,
            int? moveNextFailureCall = null,
            int? currentFailureIndex = null)
        {
            _items = items;
            _moveNextFailureCall = moveNextFailureCall;
            _currentFailureIndex = currentFailureIndex;
        }

        public IEnumerator<T> GetEnumerator()
            => new Enumerator(_items, _moveNextFailureCall, _currentFailureIndex);

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private sealed class Enumerator : IEnumerator<T>
        {
            private readonly IReadOnlyList<T> _items;
            private readonly int? _moveNextFailureCall;
            private readonly int? _currentFailureIndex;
            private int _index = -1;
            private int _moveNextCalls;
            private bool _moveNextFailed;
            private bool _currentFailed;

            public Enumerator(
                IReadOnlyList<T> items,
                int? moveNextFailureCall,
                int? currentFailureIndex)
            {
                _items = items;
                _moveNextFailureCall = moveNextFailureCall;
                _currentFailureIndex = currentFailureIndex;
            }

            public T Current
            {
                get
                {
                    if (!_currentFailed && _currentFailureIndex == _index)
                    {
                        _currentFailed = true;
                        throw new InvalidOperationException("current failed");
                    }

                    return _items[_index];
                }
            }

            object IEnumerator.Current => Current!;

            public bool MoveNext()
            {
                _moveNextCalls++;
                if (!_moveNextFailed && _moveNextFailureCall == _moveNextCalls)
                {
                    _moveNextFailed = true;
                    throw new InvalidOperationException("enumeration failed");
                }

                _index++;
                return _index < _items.Count;
            }

            public void Reset() => throw new NotSupportedException();
            public void Dispose() { }
        }
    }
}
