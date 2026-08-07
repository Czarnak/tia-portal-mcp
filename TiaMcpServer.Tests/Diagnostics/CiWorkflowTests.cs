using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class CiWorkflowTests
{
    private static readonly Regex RepositoryScriptReferencePattern = new(
        @"[\w./\\-]+\.(?:ps1|cmd|bat)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches the "-m:1" MSBuild node-count flag only as a whole, standalone token —
    /// i.e. not as a prefix of a longer flag such as "-m:10" or "-m:12". A plain
    /// substring check (e.g. <c>command.Contains("-m:1")</c>) would incorrectly accept
    /// those longer flags too, since "-m:10" literally contains the substring "-m:1".
    /// </summary>
    private static readonly Regex SingleNodeBuildFlagPattern = new(
        @"(?<=^|\s)-m:1(?=\s|$)",
        RegexOptions.Compiled);

    [Fact]
    public void EverySolutionBuild_IsSerialized()
    {
        var solutionBuildCommands = EnumerateWorkflowFiles()
            .SelectMany(ReadRunCommandBlocks)
            .SelectMany(ExpandRepositoryBuildScripts)
            .Where(command => command.Contains("dotnet build", StringComparison.OrdinalIgnoreCase))
            .Where(command => command.Contains("TiaMcpServer.sln", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(solutionBuildCommands);
        Assert.All(solutionBuildCommands, command => Assert.True(
            SingleNodeBuildFlagPattern.IsMatch(command),
            $"Expected solution build command to contain the exact '-m:1' flag as a standalone token, but it did not: {command}"));
    }

    [Theory]
    [InlineData("dotnet build TiaMcpServer.sln -m:1", true)]
    [InlineData("dotnet build TiaMcpServer.sln -m:1 /p:UseTiaPortalReferenceStubs=true", true)]
    [InlineData("dotnet build TiaMcpServer.sln -m:10", false)]
    [InlineData("dotnet build TiaMcpServer.sln -m:12", false)]
    [InlineData("dotnet build TiaMcpServer.sln -m:100", false)]
    public void SingleNodeBuildFlagPattern_MatchesOnlyExactToken(string command, bool expectedMatch)
    {
        Assert.Equal(expectedMatch, SingleNodeBuildFlagPattern.IsMatch(command));
    }

    [Fact]
    public void SingleNodeBuildFlagPattern_RejectsFalsePositive_ThatOldSubstringCheckWouldHaveAccepted()
    {
        const string lookalikeCommand = "dotnet build TiaMcpServer.sln -m:10";

        // This demonstrates the exact gap the old assertion had: "-m:10" contains the
        // literal substring "-m:1", so `Assert.Contains("-m:1", command, ...)` passed here.
        Assert.Contains("-m:1", lookalikeCommand, StringComparison.Ordinal);

        // The corrected token-boundary check must reject this false positive.
        Assert.DoesNotMatch(SingleNodeBuildFlagPattern, lookalikeCommand);
    }

    [Fact]
    public void CoverageRunsettings_UsesApprovedScope()
    {
        var runsettingsPath = Path.Combine(
            GetRepositoryRoot(),
            "TiaMcpServer.Tests",
            "coverage.runsettings");

        Assert.True(File.Exists(runsettingsPath), $"Expected coverage.runsettings to exist at {runsettingsPath}");

        var doc = XDocument.Load(runsettingsPath);
        var config = doc.Root?
            .Element("DataCollectionRunSettings")?
            .Element("DataCollectors")?
            .Element("DataCollector")?
            .Element("Configuration");

        Assert.NotNull(config);

        string Value(string elementName) => config.Element(elementName)?.Value ?? string.Empty;

        Assert.Equal("cobertura", Value("Format"));
        Assert.Equal("[TiaMcpServer]*,[TiaMcpServer.Contracts]*,[TiaMcpServer.Tests]TiaMcpServer.*", Value("Include"));
        Assert.Equal("[TiaMcpServer.Tests]TiaMcpServer.Tests.*,[TiaMcpServer.Tests]TiaMcpServer.OpennessWorker.*,[TiaMcpServer.FakeWorker]*,[TiaMcpServer.OpennessWorker]*", Value("Exclude"));
        Assert.Equal("true", Value("IncludeTestAssembly"));
        Assert.Contains("GeneratedCodeAttribute", Value("ExcludeByAttribute"), StringComparison.Ordinal);
        Assert.Contains("CompilerGeneratedAttribute", Value("ExcludeByAttribute"), StringComparison.Ordinal);
        Assert.Contains("**/*.g.cs", Value("ExcludeByFile"), StringComparison.Ordinal);
        Assert.Contains("**/*.Designer.cs", Value("ExcludeByFile"), StringComparison.Ordinal);
    }

    [Fact]
    public void CiCoverage_CollectsThenEnforcesBeforeUpload()
    {
        var ciWorkflowPath = Path.Combine(GetRepositoryRoot(), ".github", "workflows", "ci.yml");
        Assert.True(File.Exists(ciWorkflowPath), $"Expected CI workflow to exist at {ciWorkflowPath}");

        var workflowText = File.ReadAllText(ciWorkflowPath);

        var runsettingsIndex = workflowText.IndexOf(
            "--settings TiaMcpServer.Tests/coverage.runsettings",
            StringComparison.Ordinal);
        var thresholdScriptIndex = workflowText.IndexOf(
            "verify-coverage-threshold.ps1",
            StringComparison.Ordinal);
        var minimumLineRateIndex = workflowText.IndexOf(
            "-MinimumLineRate 0.80",
            StringComparison.Ordinal);
        var codecovIndex = workflowText.IndexOf(
            "codecov/codecov-action",
            StringComparison.Ordinal);

        Assert.True(runsettingsIndex >= 0, "Expected the CI workflow to collect coverage using coverage.runsettings.");
        Assert.True(thresholdScriptIndex >= 0, "Expected the CI workflow to invoke verify-coverage-threshold.ps1.");
        Assert.True(minimumLineRateIndex >= 0, "Expected the CI workflow to pass -MinimumLineRate 0.80.");
        Assert.True(codecovIndex >= 0, "Expected the CI workflow to upload to Codecov.");

        Assert.True(
            runsettingsIndex < thresholdScriptIndex,
            "Expected scoped coverage collection to precede the threshold script.");
        Assert.True(
            thresholdScriptIndex < minimumLineRateIndex,
            "Expected the threshold script invocation to precede its -MinimumLineRate argument.");
        Assert.True(
            minimumLineRateIndex < codecovIndex,
            "Expected the threshold enforcement to precede the Codecov upload.");
    }

    /// <summary>
    /// Documentation-side counterpart to <see cref="EverySolutionBuild_IsSerialized"/>: the "Build
    /// From Source" section is a human-facing copy of the same command and can drift independently
    /// of the CI workflow files. Reuses <see cref="SingleNodeBuildFlagPattern"/> so both surfaces
    /// are held to the exact same token-boundary standard (no false-positive match on "-m:10").
    /// </summary>
    [Fact]
    public void BuildingDoc_BuildFromSourceCommand_ContainsSingleNodeBuildFlag()
    {
        var readmePath = Path.Combine(GetRepositoryRoot(), "docs", "development", "building.md");
        Assert.True(File.Exists(readmePath), $"Expected building.md to exist at {readmePath}");

        var readmeText = File.ReadAllText(readmePath);
        var headingIndex = readmeText.IndexOf("## Build From Source", StringComparison.Ordinal);
        Assert.True(headingIndex >= 0, "Expected docs/development/building.md to contain a 'Build From Source' section.");

        var nextHeadingIndex = readmeText.IndexOf("\n## ", headingIndex + 1, StringComparison.Ordinal);
        var sectionLength = nextHeadingIndex >= 0 ? nextHeadingIndex - headingIndex : readmeText.Length - headingIndex;
        var section = readmeText.Substring(headingIndex, sectionLength);

        var solutionBuildLine = section
            .Split('\n')
            .FirstOrDefault(line =>
                line.Contains("dotnet build", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("TiaMcpServer.sln", StringComparison.OrdinalIgnoreCase));

        Assert.True(solutionBuildLine is not null, "Expected README.md 'Build From Source' section to contain a 'dotnet build TiaMcpServer.sln' command.");
        Assert.True(
            SingleNodeBuildFlagPattern.IsMatch(solutionBuildLine!),
            $"Expected the README 'Build From Source' solution build command to contain the exact '-m:1' flag as a standalone token, but it did not: {solutionBuildLine}");
    }

    /// <summary>
    /// Pins that <c>get_project_status</c> is documented as read-only/non-binding and that
    /// deliberate project switching is documented exclusively through <c>open_project</c> - never
    /// as something achievable by calling <c>get_project_status</c> with a different path. Guards
    /// against the installation guide regressing to instructing side-effecting status-based switching.
    /// </summary>
    [Fact]
    public void InstallationDoc_DescribesGetProjectStatusAsReadOnlyNonBindingAndSwitchingThroughOpenProject()
    {
        var readmePath = Path.Combine(GetRepositoryRoot(), "docs", "guides", "installation.md");
        Assert.True(File.Exists(readmePath), $"Expected installation.md to exist at {readmePath}");

        // The document hard-wraps some paragraphs at ~120 columns, so a phrase spanning a wrap point
        // would contain a newline where this assertion expects a space. Collapse all whitespace runs
        // (including line breaks) to a single space first so the check is resilient to rewrapping.
        var normalizedReadmeText = Regex.Replace(File.ReadAllText(readmePath), @"\s+", " ");

        Assert.Contains(
            "get_project_status(projectPath)` is read-only and non-binding",
            normalizedReadmeText,
            StringComparison.Ordinal);
        Assert.Contains("do not use it to switch projects", normalizedReadmeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use `open_project` for deliberate session switching", normalizedReadmeText, StringComparison.Ordinal);
    }

    [Fact]
    public void TroubleshootingDoc_DescribesLiveVerifiedBlockWritesWithoutAPendingCaveat()
    {
        var readmePath = Path.Combine(GetRepositoryRoot(), "docs", "guides", "troubleshooting.md");
        Assert.True(File.Exists(readmePath), $"Expected troubleshooting.md to exist at {readmePath}");

        var normalizedReadmeText = Regex.Replace(File.ReadAllText(readmePath), @"\s+", " ");

        Assert.DoesNotContain("pending live V21 re-verification", normalizedReadmeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("multi-document `update_block_logic` round trips are verified", normalizedReadmeText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SCL `create_block` calls are verified", normalizedReadmeText, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateWorkflowFiles()
    {
        var workflowsDirectory = Path.Combine(GetRepositoryRoot(), ".github", "workflows");

        return Directory.EnumerateFiles(workflowsDirectory, "*.yml")
            .Concat(Directory.EnumerateFiles(workflowsDirectory, "*.yaml"))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    /// <summary>
    /// Extracts the full text of every "run:" step in a workflow file, including
    /// multi-line block-scalar ("|" / ">") continuations, as one string per step.
    /// </summary>
    private static IEnumerable<string> ReadRunCommandBlocks(string workflowPath)
    {
        var lines = File.ReadAllLines(workflowPath);
        var blocks = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var runKeywordIndex = line.IndexOf("run:", StringComparison.Ordinal);
            if (runKeywordIndex < 0)
            {
                continue;
            }

            var keyIndent = line[..runKeywordIndex];
            if (keyIndent.Trim().Length > 0)
            {
                // "run:" is not the mapping key at this position (e.g. trailing in a comment or value).
                continue;
            }

            var indentLength = keyIndent.Length;
            var afterKeyword = line[(runKeywordIndex + "run:".Length)..].Trim();
            var isBlockScalarHeader = afterKeyword is "" or "|" or ">" or "|-" or ">-" or "|+" or ">+";

            if (!isBlockScalarHeader)
            {
                // Single-line "run: <command>".
                blocks.Add(afterKeyword);
                continue;
            }

            var blockLines = new List<string>();
            var next = i + 1;
            while (next < lines.Length)
            {
                var candidate = lines[next];
                if (candidate.Trim().Length == 0)
                {
                    blockLines.Add(string.Empty);
                    next++;
                    continue;
                }

                var candidateIndent = candidate.Length - candidate.TrimStart().Length;
                if (candidateIndent <= indentLength)
                {
                    break;
                }

                blockLines.Add(candidate.Trim());
                next++;
            }

            blocks.Add(string.Join('\n', blockLines));
            i = next - 1;
        }

        return blocks;
    }

    /// <summary>
    /// Returns the original command plus the contents of every repository-local
    /// ".ps1"/".cmd"/".bat" script it references, so build commands hidden behind
    /// script indirection are still visible to the assertion. References that
    /// resolve outside the repository are rejected and skipped.
    /// </summary>
    private static IEnumerable<string> ExpandRepositoryBuildScripts(string command)
    {
        yield return command;

        var repositoryRoot = GetRepositoryRoot();

        foreach (Match match in RepositoryScriptReferencePattern.Matches(command))
        {
            var referencedPath = match.Value.Replace('/', Path.DirectorySeparatorChar);
            var resolvedPath = Path.GetFullPath(Path.Combine(repositoryRoot, referencedPath));

            var isInsideRepository = resolvedPath.StartsWith(
                repositoryRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
            if (!isInsideRepository)
            {
                continue;
            }

            if (File.Exists(resolvedPath))
            {
                yield return File.ReadAllText(resolvedPath);
            }
        }
    }

    private static string GetRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            ".."));
    }
}
