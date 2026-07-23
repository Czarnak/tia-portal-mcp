using System.Reflection;
using TiaMcpServer.Contracts;
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

    [Fact]
    public async Task DirectStatusDivergence_WarnsButDoesNotAdoptWorkerPath()
    {
        // Bound to A ("C:\\bound\\Session.ap21"); the FakeWorker scenario keyed by that path
        // reports it actually operated on B ("C:\\actual\\Other.ap21"). A direct status read is
        // BindingTransition.None: it surfaces a single divergence warning naming both canonical
        // paths but must NOT adopt B - the binding stays A.
        var binding = new ProjectSessionBinding(null);
        Assert.True(binding.Bind("C:\\bound\\Session.ap21", forceRebind: false, out _));
        using var client = new OpennessWorkerClient(
            binding,
            logger: null,
            workerExecutablePath: FakeWorkerLocator.Locate());

        var result = await client.GetProjectStatusAsync(null);

        Assert.True(result.Success);
        Assert.Equal("C:\\bound\\Session.ap21", binding.BoundProjectPath);
        Assert.Contains(
            result.Warnings,
            w => w.Contains("C:\\bound\\Session.ap21", StringComparison.Ordinal)
                && w.Contains("C:\\actual\\Other.ap21", StringComparison.Ordinal));
    }
}
