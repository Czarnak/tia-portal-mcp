using System.Diagnostics;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

/// <summary>
/// Behavior tests for <c>scripts/verify-coverage-threshold.ps1</c>, the strict
/// gate that enforces an inclusive minimum Cobertura line-rate in CI.
/// </summary>
public class CoverageThresholdScriptTests
{
    [Theory]
    [InlineData("0.79", 1)]
    [InlineData("0.80", 0)]
    [InlineData("0.81", 0)]
    public void LineRate_UsesInclusiveMinimum(string lineRate, int expectedExitCode)
    {
        var coveragePath = WriteTempCoverageFile($"<coverage line-rate=\"{lineRate}\" />");
        try
        {
            var result = RunScript(coveragePath, "0.80");

            Assert.Equal(expectedExitCode, result.ExitCode);
        }
        finally
        {
            File.Delete(coveragePath);
        }
    }

    [Fact]
    public void MissingFile_Fails()
    {
        var missingCoveragePath = Path.Combine(Path.GetTempPath(), $"coverage-missing-{Guid.NewGuid():N}.xml");

        var result = RunScript(missingCoveragePath, "0.80");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void MissingLineRate_Fails()
    {
        const string xmlWithoutLineRate = "<coverage><packages /></coverage>";
        var coveragePath = WriteTempCoverageFile(xmlWithoutLineRate);
        try
        {
            var result = RunScript(coveragePath, "0.80");

            Assert.NotEqual(0, result.ExitCode);
            AssertOutputDoesNotEchoContent(result, xmlWithoutLineRate);
        }
        finally
        {
            File.Delete(coveragePath);
        }
    }

    [Fact]
    public void MalformedXml_Fails()
    {
        const string malformedXml = "<coverage line-rate=\"0.9\" <<not-well-formed>>";
        var coveragePath = WriteTempCoverageFile(malformedXml);
        try
        {
            var result = RunScript(coveragePath, "0.80");

            Assert.NotEqual(0, result.ExitCode);
            AssertOutputDoesNotEchoContent(result, malformedXml);
        }
        finally
        {
            File.Delete(coveragePath);
        }
    }

    private static void AssertOutputDoesNotEchoContent(ScriptResult result, string fileContents)
    {
        Assert.DoesNotContain(fileContents, result.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain(fileContents, result.StandardError, StringComparison.Ordinal);
    }

    private static string WriteTempCoverageFile(string xml)
    {
        var path = Path.Combine(Path.GetTempPath(), $"coverage-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    private static ScriptResult RunScript(string coveragePath, string minimumLineRate)
    {
        var scriptPath = Path.Combine(GetRepositoryRoot(), "scripts", "verify-coverage-threshold.ps1");

        var startInfo = new ProcessStartInfo
        {
            FileName = "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-CoveragePath");
        startInfo.ArgumentList.Add(coveragePath);
        startInfo.ArgumentList.Add("-MinimumLineRate");
        startInfo.ArgumentList.Add(minimumLineRate);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start pwsh process.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new ScriptResult(process.ExitCode, standardOutput, standardError);
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

    private sealed record ScriptResult(int ExitCode, string StandardOutput, string StandardError);
}
