using TiaMcpServer.Contracts;
using TiaMcpServer.CrossReferences;
using TiaMcpServer.Json;
using Xunit;

namespace TiaMcpServer.Tests.CrossReferences;

public class CrossReferenceBudgetTests
{
    private const string Narrowing = "Narrow with maxResults, a narrower target path, or a member.";

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

    [Fact]
    public void WorkerOmissionsAreAccumulated()
    {
        var report = Report(40, 1_000);
        report.OmittedSourceCount = 5;

        var result = CrossReferenceBudget.Apply(report, 10_000);

        Assert.Equal(5 + 40 - result.Sources.Count, result.OmittedSourceCount);
    }
}
