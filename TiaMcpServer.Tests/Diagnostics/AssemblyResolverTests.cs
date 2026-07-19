using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class AssemblyResolverTests
{
    [Fact]
    public void TiaPortalLocationRoot_AppendsV21Net48PublicApiSuffix()
    {
        var path = AssemblyResolver.ExpandTiaPortalLocation(
            @"C:\Program Files\Siemens\Automation\Portal V21");

        Assert.Equal(
            @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48",
            path);
    }

    [Fact]
    public void CandidatePaths_PreserveEnvironmentRegistryDefaultPrecedence()
    {
        var paths = AssemblyResolver.CreateCandidatePaths(
            @"C:\override\net48",
            @"C:\Portal V21",
            new[] { @"C:\registry64", @"C:\registry32" },
            @"C:\default\net48").ToArray();

        Assert.Equal(
            new[]
            {
                @"C:\override\net48",
                @"C:\Portal V21\PublicAPI\V21\net48",
                @"C:\registry64\PublicAPI\V21\net48",
                @"C:\registry32\PublicAPI\V21\net48",
                @"C:\default\net48"
            },
            paths);
    }

    [Fact]
    public void FirstIncompleteCandidate_FallsThroughToNextCompleteCandidate()
    {
        var inspected = new List<string>();
        var candidates = AssemblyResolver.CreateCandidatePaths(
            @"C:\incomplete\net48",
            @"C:\Portal V21",
            new[] { @"C:\registry64" },
            @"C:\default\net48");

        var selected = AssemblyResolver.SelectFirstCompleteCandidate(
            candidates,
            path => path.Contains("Portal V21", StringComparison.Ordinal),
            inspected.Add);

        Assert.Equal(@"C:\Portal V21\PublicAPI\V21\net48", selected);
        Assert.Equal(
            new[]
            {
                @"C:\incomplete\net48",
                @"C:\Portal V21\PublicAPI\V21\net48"
            },
            inspected);
    }
}
