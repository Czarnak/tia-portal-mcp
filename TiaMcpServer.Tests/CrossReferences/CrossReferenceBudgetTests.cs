using TiaMcpServer.Contracts;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.CrossReferences;

public class CrossReferenceBudgetTests
{
    private static string Narrowing => CrossReferenceBudget.NarrowingMessage;

    private static CrossReferenceReport Report(int sources, int nameChars)
    {
        var report = new CrossReferenceReport
        {
            IsComplete = true,
            OwnerQueryCount = 1,
            SuccessfulOwnerQueryCount = 1,
            TotalSourceCount = sources,
        };
        for (var i = 0; i < sources; i++)
            report.Sources.Add(new CrossReferenceSourceInfo { Name = "S" + i + new string('x', nameChars) });
        return report;
    }

    [Fact]
    public void FittingReportIsUnchanged()
    {
        var report = Report(3, 10);

        var result = CrossReferenceBudget.Apply(report);

        Assert.Same(report, result);
        Assert.True(result.IsComplete);
        Assert.Equal(0, result.OmittedSourceCount);
    }

    [Theory]
    [InlineData(10_000, 180_000)] // value limit binds
    [InlineData(60_000, 12_000)] // document limit binds
    public void DropsTailSourcesToFitValueAndDocument(int maxValueChars, int maxDocumentChars)
    {
        var report = Report(40, 1_000);

        var result = CrossReferenceBudget.Apply(report, maxValueChars, maxDocumentChars);

        Assert.InRange(result.Sources.Count, 1, 39);
        Assert.True(CanonicalJson.Serialize(result).Length <= maxValueChars);
        Assert.True(CanonicalJson.Serialize(result).Length <= maxDocumentChars);
        Assert.Equal(40 - result.Sources.Count, result.OmittedSourceCount);
        Assert.Equal(40, result.TotalSourceCount);
        Assert.False(result.IsComplete);
        Assert.Contains(Narrowing, result.Messages);
        // Whole tail sources are dropped; the head survives in caller order.
        Assert.Equal(report.Sources.Take(result.Sources.Count).Select(s => s.Name), result.Sources.Select(s => s.Name));
    }

    private static CrossReferenceLocationInfo Location(int i) => new() { Name = "L" + i + new string('l', 40) };

    private static CrossReferenceTargetInfo Reference(int i, int locations) => new()
    {
        Name = "R" + i + new string('r', 40),
        Locations = Enumerable.Range(0, locations).Select(Location).ToList(),
    };

    private static CrossReferenceReport Leaf(CrossReferenceSourceInfo source) => new()
    {
        IsComplete = true,
        OwnerQueryCount = 1,
        SuccessfulOwnerQueryCount = 1,
        TotalSourceCount = 1,
        Sources = { source },
    };

    [Fact]
    public void SingleHugeLeafSourceIsTrimmedNotEmptied()
    {
        var source = new CrossReferenceSourceInfo
        {
            Name = "HeavyTag",
            Children = Enumerable.Range(0, 50).Select(i => new CrossReferenceSourceInfo
            {
                Name = "C" + i, References = { Reference(i, 20) },
            }).ToList(),
            References = Enumerable.Range(0, 400).Select(i => Reference(i, 5)).ToList(),
        };

        var result = CrossReferenceBudget.Apply(Leaf(source));

        var kept = Assert.Single(result.Sources);
        Assert.Equal("HeavyTag", kept.Name);
        Assert.Empty(kept.Children);
        Assert.InRange(kept.References.Count, 1, 399);
        Assert.True(CanonicalJson.Serialize(result).Length <= 60_000);
        Assert.False(result.IsComplete);
        Assert.Equal(0, result.OmittedSourceCount);
        Assert.Contains(Narrowing, result.Messages);
        Assert.Contains(result.Messages, m =>
            m.Contains("50 child sources") && m.Contains($"{50 + 400 - kept.References.Count} references")
            && m.Contains("locations"));
    }

    [Theory]
    [InlineData(60_000, 180_000)]
    [InlineData(60_000, 20_000)] // document limit binds
    public void SingleReferenceWithHugeLocationListKeepsHeadLocations(int maxValueChars, int maxDocumentChars)
    {
        var source = new CrossReferenceSourceInfo { Name = "HeavyTag", References = { Reference(0, 3_000) } };

        var result = CrossReferenceBudget.Apply(Leaf(source), maxValueChars, maxDocumentChars);

        var reference = Assert.Single(Assert.Single(result.Sources).References);
        Assert.InRange(reference.Locations.Count, 1, 2_999);
        Assert.Equal("L0" + new string('l', 40), reference.Locations[0].Name);
        Assert.True(CanonicalJson.Serialize(result).Length <= Math.Min(maxValueChars, maxDocumentChars));
        Assert.Contains(result.Messages, m => m.Contains($"{3_000 - reference.Locations.Count} locations"));
    }

    [Fact]
    public void WorkerOmissionsAreAccumulated()
    {
        var report = Report(40, 1_000);
        report.OmittedSourceCount = 5;

        var result = CrossReferenceBudget.Apply(report, 10_000);

        Assert.Equal(5 + 40 - result.Sources.Count, result.OmittedSourceCount);
    }
}
