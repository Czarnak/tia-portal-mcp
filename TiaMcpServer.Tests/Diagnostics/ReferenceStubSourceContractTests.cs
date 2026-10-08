using System.Security.Cryptography;
using System.Xml.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class ReferenceStubSourceContractTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
    private static string StubPath(params string[] parts) => Path.Combine(new[] { Root, "reference-stubs" }.Concat(parts).ToArray());
    private static XDocument ReadProject(string name)
    {
        var path = StubPath(name, name + ".csproj");
        Assert.True(File.Exists(path), $"Expected source-owned reference project: {path}");
        return XDocument.Load(path);
    }

    [Fact]
    public void DedicatedSolutionContainsAllSourcesAndCompileOnlyProbe()
    {
        var path = StubPath("TiaMcpServer.ReferenceStubs.sln");
        Assert.True(File.Exists(path), $"Expected dedicated source-reference solution: {path}");
        var solution = File.ReadAllText(path);
        foreach (var name in new[] { "Siemens.Engineering.Base", "Siemens.Engineering.Step7", "Siemens.Engineering.WinCCUnified", "TiaMcpServer.OpennessReferenceProbe" })
        {
            Assert.Contains(name + "\\" + name + ".csproj", solution, StringComparison.Ordinal);
            Assert.True(File.Exists(StubPath(name, name + ".csproj")));
        }
    }

    [Theory]
    [InlineData("Siemens.Engineering.Base")]
    [InlineData("Siemens.Engineering.Step7")]
    [InlineData("Siemens.Engineering.WinCCUnified")]
    public void ProjectsPreserveIdentityAndHaveNoProductOrInstalledReferences(string name)
    {
        var project = ReadProject(name);
        Assert.Equal(name, project.Descendants("AssemblyName").Single().Value);
        Assert.Empty(project.Descendants("Reference"));
        Assert.Empty(project.Descendants("PackageReference"));
        var references = project.Descendants("ProjectReference").ToArray();
        if (name == "Siemens.Engineering.Base") Assert.Empty(references);
        else Assert.Equal("..\\Siemens.Engineering.Base\\Siemens.Engineering.Base.csproj", Assert.Single(references).Attribute("Include")!.Value);
        Assert.DoesNotContain("TiaPortalV21Dir", project.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("TiaMcpServer", project.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPropertiesRequireDeterministicPublicOnlyDelaySignedReferences()
    {
        var path = StubPath("Directory.Build.props");
        Assert.True(File.Exists(path), $"Expected deterministic reference properties: {path}");
        var document = XDocument.Load(path);
        string Property(string name) => Assert.Single(document.Descendants(name)).Value;
        Assert.Equal("net48", Property("TargetFramework"));
        Assert.Equal("enable", Property("Nullable"));
        Assert.Equal("latest", Property("LangVersion"));
        Assert.Equal("21.0.0.0", Property("AssemblyVersion"));
        Assert.Equal("21.0.0.0", Property("FileVersion"));
        foreach (var name in new[] { "Deterministic", "ProduceReferenceAssembly", "SignAssembly", "DelaySign" }) Assert.Equal("true", Property(name));
        Assert.Contains("$(MSBuildThisFileDirectory)", Property("PathMap"));
        Assert.EndsWith("Siemens.Engineering.PublicKey.snk", Property("AssemblyOriginatorKeyFile"), StringComparison.Ordinal);
        var release = Assert.Single(document.Descendants("PropertyGroup"), g => ((string?)g.Attribute("Condition"))?.Contains("Release", StringComparison.Ordinal) == true);
        Assert.Equal("none", release.Element("DebugType")!.Value);
        Assert.Equal("false", release.Element("DebugSymbols")!.Value);
        Assert.Equal("unverified", Property("StubSourceHash"));
        Assert.Contains("'$(StubSourceHash)' == ''", document.Descendants("StubSourceHash").Single().Attribute("Condition")!.Value, StringComparison.Ordinal);
        var metadata = document.Descendants("AssemblyMetadata").ToDictionary(e => e.Attribute("Include")!.Value, e => e.Attribute("Value")!.Value);
        Assert.Equal("tia-portal-mcp", metadata["StubOrigin"]);
        Assert.Equal("$(StubSourceHash)", metadata["StubSourceHash"]);
    }

    [Fact]
    public void SigningKeyContainsOnlyTheExpectedAssemblyPublicKey()
    {
        var path = StubPath("Siemens.Engineering.PublicKey.snk");
        Assert.True(File.Exists(path), $"Expected public-only identity key: {path}");
        var key = File.ReadAllBytes(path);
        // Strong-name public key header followed by a PUBLICKEYBLOB, never a PRIVATEKEYBLOB.
        Assert.True(key.Length >= 32);
        Assert.Equal(key.Length - 12, BitConverter.ToInt32(key, 8));
        Assert.Equal(0x06, key[12]);
        Assert.Equal("RSA1", System.Text.Encoding.ASCII.GetString(key, 20, 4));
        var hash = SHA1.HashData(key);
        Assert.Equal("29bfe5fdf4ba5d3b", Convert.ToHexString(hash[^8..].Reverse().ToArray()).ToLowerInvariant());
    }

    [Theory]
    [InlineData("Access")]
    [InlineData("ReferenceType")]
    public void CrossReferenceStubEnumsDeclareEveryClosedName(string enumName)
    {
        var source = File.ReadAllText(StubPath("Siemens.Engineering.Base", "CrossReference.cs"));
        var body = System.Text.RegularExpressions.Regex.Match(source,
            $@"public enum {enumName} : int\s*\{{(?<body>[^}}]*)\}}").Groups["body"].Value;
        var declared = System.Text.RegularExpressions.Regex.Matches(body, @"(\w+)\s*=\s*(\d+)")
            .Select(m => (Name: m.Groups[1].Value, Value: int.Parse(m.Groups[2].Value))).ToList();
        var expected = enumName == "Access" ? TiaMcpServer.Contracts.CrossReferenceAccessNames.All : TiaMcpServer.Contracts.CrossReferenceTypeNames.All;

        // The closed names are the V21 declaration order, so each value is its index.
        Assert.Equal(expected.Select((name, index) => (name, index)), declared);
    }

    [Fact]
    public void NamespaceOwnershipAndNonExecutablePurposeAreExplicit()
    {
        foreach (var file in new[] { "AssemblyInfo", "Core", "Compiler", "CrossReference", "Hardware", "HardwareConnections", "Online", "Multiuser" })
            Assert.True(File.Exists(StubPath("Siemens.Engineering.Base", file + ".cs")), $"Missing Base source: {file}");
        foreach (var file in new[] { "AssemblyInfo", "Software", "Blocks", "ExternalSources", "Tags", "Types", "Units" })
            Assert.True(File.Exists(StubPath("Siemens.Engineering.Step7", file + ".cs")), $"Missing Step7 source: {file}");
        var baseSource = string.Join("\n", Directory.GetFiles(StubPath("Siemens.Engineering.Base"), "*.cs").Select(File.ReadAllText));
        var step7Source = string.Join("\n", Directory.GetFiles(StubPath("Siemens.Engineering.Step7"), "*.cs").Select(File.ReadAllText));
        Assert.Contains("namespace Siemens.Engineering.Multiuser", baseSource, StringComparison.Ordinal);
        Assert.Contains("namespace Siemens.Engineering.HW", baseSource, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Siemens.Engineering.SW", baseSource, StringComparison.Ordinal);
        Assert.Contains("namespace Siemens.Engineering.SW", step7Source, StringComparison.Ordinal);
        Assert.DoesNotContain("namespace Siemens.Engineering.Multiuser", step7Source, StringComparison.Ordinal);
        var readme = File.ReadAllText(StubPath("README.md"));
        foreach (var text in new[] { "compile-only", "public-only", "29bfe5fdf4ba5d3b", "unverified", "cannot sign Siemens implementation code" }) Assert.Contains(text, readme, StringComparison.Ordinal);
    }
}
