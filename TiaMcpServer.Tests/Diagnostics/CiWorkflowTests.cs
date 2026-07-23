using System.Text.RegularExpressions;
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
