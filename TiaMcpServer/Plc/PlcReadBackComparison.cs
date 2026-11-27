namespace TiaMcpServer.Plc;

/// <summary>Builds observational submitted/observed evidence without changing the full-export hash.</summary>
internal static class PlcReadBackComparison
{
    public static PlcReadBackEvidence Build(
        string format, string submitted, string observed, PlcContentDiffBudget budget)
    {
        var sides = PlcContentDiffSides.SelectWithScope(format, observed, submitted);
        var diff = PlcContentDiff.Build(sides.Requested, sides.Current, budget);
        return new PlcReadBackEvidence(
            sides.ComparisonScope,
            diff.RawTextEqual, diff.NormalizedLinesEqual, diff.LineEndingOnly,
            diff.Current, diff.Requested,
            diff.CurrentChangedLineCount, diff.RequestedChangedLineCount,
            diff.BudgetExhausted, null);
    }
}
