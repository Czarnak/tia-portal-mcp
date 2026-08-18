using System.Text.RegularExpressions;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

/// <summary>
/// Source-level wiring contract for the second lifecycle identity check. Program's first check
/// occurs before dispatch; these assertions keep the second check after EnsureProject has had a
/// chance to refresh/reopen a handle, but before any Siemens mutation can run.
/// </summary>
public sealed class LifecycleSecondIdentityValidationContractTests
{
    private static string ProgramSource => File.ReadAllText(
        FindRepositoryFile("TiaMcpServer.OpennessWorker", "Program.cs"));

    private static string LifecycleServiceSource => File.ReadAllText(
        FindRepositoryFile(
            "TiaMcpServer.OpennessWorker",
            "Openness",
            "ProjectLifecycleService.cs"));

    [Theory]
    [InlineData("SaveProject", "SaveProject")]
    [InlineData("SaveProjectAs", "SaveProjectAs")]
    [InlineData("ArchiveProject", "ArchiveProject")]
    [InlineData("CloseProject", "CloseProject")]
    public void Program_ForwardsExpectedIdentityIntoLifecycleMutation(
        string programMethod,
        string serviceMethod)
    {
        var body = ExtractMethod(
            ProgramSource,
            $"private static WorkerResponse {programMethod}(WorkerRequest request)");

        Assert.Contains($"ProjectLifecycleService.{serviceMethod}(", body, StringComparison.Ordinal);
        Assert.Contains("request.ExpectedSessionIdentity", body, StringComparison.Ordinal);
        AssertOrdered(
            body,
            $"ProjectLifecycleService.{serviceMethod}(",
            "request.ExpectedSessionIdentity");
    }

    [Fact]
    public void Save_RevalidatesAfterEnsureProjectAndBeforeSave()
    {
        var body = ServiceMethod("SaveProject");

        AssertOrdered(
            body,
            "EnsureProject(session, projectPath)",
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity)",
            "project.Save();");
        AssertAdjacent(
            body,
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity);",
            "project.Save();");
    }

    [Fact]
    public void SaveAs_RevalidatesAfterEnsureProjectAndBeforeSaveAs()
    {
        var body = ServiceMethod("SaveProjectAs");

        AssertOrdered(
            body,
            "EnsureProject(session, projectPath)",
            "RequireAbsoluteDirectory(targetDirectory, \"TargetDirectory\", mustExist: true)",
            "RequireName(targetName, \"TargetName\")",
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity)",
            "project.SaveAs(");
        AssertAdjacent(
            body,
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity);",
            "project.SaveAs(");
    }

    [Fact]
    public void Archive_RevalidatesImmediatelyBeforeOptionalSaveAndAgainBeforeArchive()
    {
        var body = ServiceMethod("ArchiveProject");

        AssertOrdered(
            body,
            "EnsureProject(session, projectPath)",
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity)",
            "project.Save();",
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity)",
            "project.Archive(");
        AssertAdjacent(
            body,
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity);",
            "project.Save();");
        AssertAdjacent(
            body,
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity);",
            "project.Archive(");
    }

    [Fact]
    public void Close_RevalidatesImmediatelyBeforeOptionalSaveAndAgainBeforeClose()
    {
        var body = ServiceMethod("CloseProject");

        AssertOrdered(
            body,
            "EnsureProject(session, projectPath)",
            "ReadStatus(project)",
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity)",
            "project.Save();",
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity)",
            "project.Close();");
        AssertAdjacent(
            body,
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity);",
            "project.Save();");
        AssertAdjacent(
            body,
            "ValidateExpectedImmediatelyBeforeMutation(session, expectedSessionIdentity);",
            "project.Close();");
    }

    [Fact]
    public void SecondValidationRequiresExpectedIdentityRatherThanAllowingAnUnboundMutation()
    {
        var body = ExtractMethod(
            LifecycleServiceSource,
            "private static void ValidateExpectedImmediatelyBeforeMutation(");

        Assert.Contains("session.ValidateExpectedSessionIdentity(", body, StringComparison.Ordinal);
        Assert.Contains("expectedSessionIdentity", body, StringComparison.Ordinal);
        Assert.Contains("allowMissingExpectedIdentity: false", body, StringComparison.Ordinal);
    }

    private static string ServiceMethod(string methodName)
        => ExtractMethod(
            LifecycleServiceSource,
            $"public static ProjectLifecycleResultInfo {methodName}(");

    private static void AssertOrdered(string source, params string[] fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var index = source.IndexOf(fragment, previous + 1, StringComparison.Ordinal);
            Assert.True(
                index >= 0,
                $"Expected '{fragment}' after source offset {previous}, but it was absent or out of order.\n{source}");
            previous = index;
        }
    }

    private static void AssertAdjacent(string source, string first, string second)
    {
        Assert.Matches(
            new Regex(
                Regex.Escape(first) + @"\s*" + Regex.Escape(second),
                RegexOptions.CultureInvariant),
            source);
    }

    private static string ExtractMethod(string source, string declaration)
    {
        var declarationIndex = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(declarationIndex >= 0, $"Method declaration '{declaration}' was not found.");

        var openingBrace = source.IndexOf('{', declarationIndex + declaration.Length);
        Assert.True(openingBrace >= 0, $"Method '{declaration}' has no body.");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            switch (source[index])
            {
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        return source[declarationIndex..(index + 1)];
                    }

                    break;
            }
        }

        throw new Xunit.Sdk.XunitException($"Method '{declaration}' has an unbalanced body.");
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                new[] { directory.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{Path.Combine(relativeSegments)}'.");
    }
}
