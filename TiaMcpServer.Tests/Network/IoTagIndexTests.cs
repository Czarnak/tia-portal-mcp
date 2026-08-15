using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

/// <summary>
/// Pure, TIA-free coverage for the normalized logical-address lookup built once per selected PLC.
/// </summary>
public class IoTagIndexTests
{
    [Fact]
    public void FindMatches_PreservesDuplicateAddressTagsInDeterministicOrder()
    {
        var index = new IoTagIndex(
            "PLC_1",
            new[]
            {
                Candidate("Zulu", "%I4.0", "Table B", "/z"),
                Candidate("Bravo", " %i4.0 ", "Table A", "/b"),
                Candidate("Alpha", "%I4.0", "Table A", "/b"),
                Candidate("FolderFirst", "%I4.0", "Table A", "/a"),
            });

        var matches = index.FindMatches(Address("I", 32, 1u));

        Assert.Equal(
            new[] { "FolderFirst", "Alpha", "Bravo", "Zulu" },
            matches.Select(candidate => candidate.Name));
    }

    [Fact]
    public void FindMatches_UsesExactAreaStartAndWidthIdentity()
    {
        var index = new IoTagIndex(
            "PLC_1",
            new[]
            {
                Candidate("InputBit", "%I4.0"),
                Candidate("InputByte", "%IB4"),
                Candidate("OutputBit", "%Q4.0"),
                Candidate("NextInputBit", "%I4.1"),
            });

        Assert.Equal("InputBit", Assert.Single(index.FindMatches(Address("I", 32, 1u))).Name);
        Assert.Equal("InputByte", Assert.Single(index.FindMatches(Address("I", 32, 8u))).Name);
        Assert.Equal("OutputBit", Assert.Single(index.FindMatches(Address("Q", 32, 1u))).Name);
        Assert.Equal("NextInputBit", Assert.Single(index.FindMatches(Address("I", 33, 1u))).Name);
        Assert.Empty(index.FindMatches(Address("I", 32, 16u)));
    }

    [Fact]
    public void Construction_SilentlyExcludesMalformedAndUnsupportedAddressesFromLookup()
    {
        var candidates = new[]
        {
            Candidate("Valid", "%I4.0"),
            Candidate("Memory", "%M4.0"),
            Candidate("Symbolic", "Motor.Run"),
            Candidate("Malformed", "%I4.8"),
            Candidate("Blank", ""),
        };

        var index = new IoTagIndex("PLC_1", candidates);

        Assert.Equal(candidates.Length, index.Candidates.Count);
        Assert.Equal("Valid", Assert.Single(index.FindMatches(Address("I", 32, 1u))).Name);
        Assert.Empty(index.FindMatches(Address("I", 39, 1u)));
    }

    [Fact]
    public void FindMatches_RepeatedLookupsDoNotReenumerateCandidates()
    {
        var candidates = new SingleEnumerationCandidateList(
            Enumerable.Range(0, 1_000)
                .Select(index => Candidate($"Tag{index}", $"%I{index}.0"))
                .Append(Candidate("DuplicateTarget", "%I512.0"))
                .ToArray());
        var index = new IoTagIndex("PLC_1", candidates);

        var first = index.FindMatches(Address("I", 4_096, 1u));
        var second = index.FindMatches(Address("I", 4_096, 1u));
        var absent = index.FindMatches(Address("Q", 4_096, 1u));

        Assert.Equal(1, candidates.EnumerationCount);
        Assert.Equal(new[] { "DuplicateTarget", "Tag512" }, first.Select(candidate => candidate.Name));
        Assert.Same(first, second);
        Assert.Empty(absent);
    }

    [Fact]
    public void Construction_FreezesCandidateMembership()
    {
        var candidates = new List<IoTagCandidate> { Candidate("Original", "%I4.0") };
        var index = new IoTagIndex("PLC_1", candidates);

        candidates.Clear();
        candidates.Add(Candidate("Replacement", "%I4.0"));

        Assert.Equal("Original", Assert.Single(index.Candidates).Name);
        Assert.Equal("Original", Assert.Single(index.FindMatches(Address("I", 32, 1u))).Name);
    }

    private static IoAbsoluteIoAddress Address(string area, int startBit, uint bitCount)
        => new(area, new IoAbsoluteBitInterval(startBit, bitCount));

    private static IoTagCandidate Candidate(
        string name,
        string logicalAddress,
        string tableName = "Tags",
        string folderPath = "/")
        => new(name, "Bool", logicalAddress, tableName, folderPath);

    private sealed class SingleEnumerationCandidateList : IReadOnlyList<IoTagCandidate>
    {
        private readonly IReadOnlyList<IoTagCandidate> _items;

        public SingleEnumerationCandidateList(IReadOnlyList<IoTagCandidate> items)
        {
            _items = items;
        }

        public int EnumerationCount { get; private set; }

        public int Count => _items.Count;

        public IoTagCandidate this[int index] => _items[index];

        public IEnumerator<IoTagCandidate> GetEnumerator()
        {
            EnumerationCount++;
            if (EnumerationCount > 1)
            {
                throw new InvalidOperationException("Candidates were enumerated after index construction.");
            }

            return _items.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
