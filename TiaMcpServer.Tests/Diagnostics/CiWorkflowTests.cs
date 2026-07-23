using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class CiWorkflowTests
{
    private static readonly Regex RepositoryScriptReferencePattern = new(
        @"[\w./\\-]+\.(?:ps1|cmd|bat)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        Assert.All(solutionBuildCommands, command => Assert.Contains("-m:1", command, StringComparison.Ordinal));
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
