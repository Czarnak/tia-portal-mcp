using System.Reflection;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class OpennessWorkerClientWarningTests
{
    [Fact]
    public void CapWarnings_TruncatesEachWarningLine()
    {
        var method = typeof(OpennessWorkerClient).GetMethod(
            "CapWarnings",
            BindingFlags.NonPublic | BindingFlags.Static)!;

        var warnings = (IReadOnlyList<string>)method.Invoke(null, new object?[] { new[] { new string('w', 2_000) } })!;

        Assert.Single(warnings);
        Assert.True(warnings[0].Length <= 1_000);
        Assert.Contains("TRUNCATED", warnings[0]);
    }
}
