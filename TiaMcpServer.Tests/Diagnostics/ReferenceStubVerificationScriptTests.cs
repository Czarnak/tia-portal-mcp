using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ReferenceStubVerificationScriptTests
{
    [Fact]
    public void MatchingExactArtifactsPassWithoutWriting()
    {
        using var fixture = new Fixture();
        var before = fixture.Snapshot();
        var result = fixture.Run();
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Contains("Both reference artifacts are current", result.Output);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Theory]
    [InlineData("Siemens.Engineering.Base.dll", false)]
    [InlineData("Siemens.Engineering.Step7.dll", false)]
    [InlineData("Siemens.Engineering.Base.dll", true)]
    [InlineData("Siemens.Engineering.Step7.dll", true)]
    public void StaleEmbeddedHashFailsClosed(string name, bool generated)
    {
        using var fixture = new Fixture();
        fixture.MakeHashStale(Path.Combine(generated ? fixture.Generated : fixture.Target, name));
        var before = fixture.Snapshot();
        var result = fixture.Run(update: generated);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(name, result.Output);
        Assert.Contains("StubSourceHash", result.Output);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MissingOrExtraGeneratedArtifactFailsClosed(bool extra, bool update)
    {
        using var fixture = new Fixture();
        if (extra) File.WriteAllText(Path.Combine(fixture.Generated, "unexpected.dll"), "unexpected");
        else File.Delete(Path.Combine(fixture.Generated, "Siemens.Engineering.Step7.dll"));
        var before = fixture.Snapshot();
        var result = fixture.Run(update);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(extra ? "unexpected.dll" : "Siemens.Engineering.Step7.dll", result.Output);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void UpdateReplacesOnlyTwoExactTargets()
    {
        using var fixture = new Fixture();
        foreach (var name in Fixture.Names) File.WriteAllText(Path.Combine(fixture.Target, name), "old artifact");
        File.WriteAllText(Path.Combine(fixture.Target, "unrelated.dll"), "preserved");
        var result = fixture.Run(update: true);
        Assert.True(result.ExitCode == 0, result.Output);
        Assert.Equal(2, result.Output.Split("Replaced ", StringSplitOptions.None).Length - 1);
        foreach (var name in Fixture.Names)
            Assert.Equal(File.ReadAllBytes(Path.Combine(fixture.Generated, name)), File.ReadAllBytes(Path.Combine(fixture.Target, name)));
        Assert.Equal("preserved", File.ReadAllText(Path.Combine(fixture.Target, "unrelated.dll")));
        Assert.Equal(3, Directory.GetFiles(fixture.Target).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InjectedDirectoryOutsideRepositoryBoundaryIsRejected(bool update)
    {
        using var fixture = new Fixture();
        var before = fixture.Snapshot();
        var result = fixture.Run(update, Path.GetTempPath());
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("boundary", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void GeneratedIdentityMismatchIsRejectedBeforeUpdate()
    {
        using var fixture = new Fixture();
        File.Copy(Path.Combine(fixture.Generated, Fixture.Names[1]), Path.Combine(fixture.Generated, Fixture.Names[0]), true);
        var before = fixture.Snapshot();
        var result = fixture.Run(update: true);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(Fixture.Names[0], result.Output);
        Assert.Contains("identity", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, fixture.Snapshot());
    }

    [Fact]
    public void NestedSourceChangesInvalidateArtifacts()
    {
        using var fixture = new Fixture();
        fixture.AddNestedSource();
        var before = fixture.Snapshot();
        var result = fixture.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("StubSourceHash", result.Output);
        Assert.Equal(before, fixture.Snapshot());
    }

    private sealed class Fixture : IDisposable
    {
        internal static readonly string[] Names = ["Siemens.Engineering.Base.dll", "Siemens.Engineering.Step7.dll"];
        private readonly string root = Path.Combine(Path.GetTempPath(), "reference-stub-test-" + Guid.NewGuid().ToString("N"));
        internal string Generated => Path.Combine(root, "generated");
        internal string Target => Path.Combine(root, "ref");
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "scripts"));
                var script = Path.Combine(ReferenceStubArtifactTests.RepositoryRoot, "scripts", "verify-reference-stubs.ps1");
                Assert.True(File.Exists(script), "Reference stub verifier is absent: " + script);
                File.Copy(script, Path.Combine(root, "scripts", "verify-reference-stubs.ps1"));
                foreach (var relative in ReferenceStubArtifactTests.SourceFiles(ReferenceStubArtifactTests.RepositoryRoot))
                {
                    var destination = Path.Combine(root, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(Path.Combine(ReferenceStubArtifactTests.RepositoryRoot, relative), destination);
                }
                Directory.CreateDirectory(Generated);
                Directory.CreateDirectory(Target);
                foreach (var name in Names)
                {
                    File.Copy(Path.Combine(ReferenceStubArtifactTests.RepositoryRoot, "ref", name), Path.Combine(Generated, name));
                    File.Copy(Path.Combine(Generated, name), Path.Combine(Target, name));
                }
            }
            catch { Dispose(); throw; }
        }
        internal string[] Snapshot() => Directory.GetFiles(Target).Order(StringComparer.Ordinal)
            .Select(p => Path.GetFileName(p) + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))).ToArray();
        internal void AddNestedSource()
        {
            var nested = Path.Combine(root, "reference-stubs", "Siemens.Engineering.Base", "Nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "Additional.cs"), "// source hash regression input");
        }
        internal void MakeHashStale(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var needle = Encoding.UTF8.GetBytes(ReferenceStubArtifactTests.SourceHash(root));
            var offset = bytes.AsSpan().IndexOf(needle);
            Assert.True(offset >= 0, "No canonical source hash in " + path);
            bytes[offset] = bytes[offset] == (byte)'0' ? (byte)'1' : (byte)'0';
            File.WriteAllBytes(path, bytes);
        }
        internal ScriptResult Run(bool update = false, string? generated = null)
        {
            var info = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-File", Path.Combine(root, "scripts", "verify-reference-stubs.ps1"), "-GeneratedReferenceDirectory", generated ?? Generated }) info.ArgumentList.Add(argument);
            if (update) info.ArgumentList.Add("-Update");
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start verifier");
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(5_000); throw new TimeoutException("Verifier timed out"); }
            Assert.True(Task.WaitAll([stdout, stderr], 5_000), "Verifier output streams did not close");
            return new(process.ExitCode, stdout.Result + stderr.Result);
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(root);
            var temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(resolved) != Path.GetFileName(root) || !Path.GetFileName(resolved).StartsWith("reference-stub-test-", StringComparison.Ordinal))
                throw new InvalidOperationException("Unsafe temporary cleanup target");
            if (Directory.Exists(resolved)) Directory.Delete(resolved, recursive: true);
        }
    }
    private sealed record ScriptResult(int ExitCode, string Output);
}
