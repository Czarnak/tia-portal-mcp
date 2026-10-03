using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

public class OpennessReferenceProbeContractTests
{
    private const string ProbeDirectory = "reference-stubs/TiaMcpServer.OpennessReferenceProbe";

    [Fact]
    public void ProbeIsCompileOnlyAndReferencesExactlyBaseAndStep7()
    {
        var path = RepositoryPath(ProbeDirectory, "TiaMcpServer.OpennessReferenceProbe.csproj");
        Assert.True(File.Exists(path), $"Expected compile-only reference probe project at {path}");
        var project = XDocument.Load(path);
        var properties = project.Root!.Elements("PropertyGroup").Elements().ToArray();

        Assert.Equal("Library", Assert.Single(properties, p => p.Name == "OutputType").Value);
        Assert.Equal("net48", Assert.Single(properties, p => p.Name == "TargetFramework").Value);
        Assert.Equal("false", Assert.Single(properties, p => p.Name == "IsPackable").Value, ignoreCase: true);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Empty(project.Descendants("ProjectReference"));

        var references = project.Descendants("Reference").ToArray();
        Assert.Equal(
            new[] { "Siemens.Engineering.Base", "Siemens.Engineering.Step7" },
            references.Select(r => (string?)r.Attribute("Include")).OrderBy(name => name));
        Assert.All(references, reference =>
        {
            Assert.Equal("False", reference.Element("Private")?.Value, ignoreCase: true);
            Assert.Equal(
                $"$(TiaOpennessReferenceDir)\\{reference.Attribute("Include")!.Value}.dll",
                reference.Element("HintPath")?.Value);
        });
    }

    [Fact]
    public void ProbeLocksOnlyTheFoundationMultiuserSurface()
    {
        var path = RepositoryPath(ProbeDirectory, "MultiuserReferenceSurface.cs");
        Assert.True(File.Exists(path), $"Expected compile-only Multiuser reference source at {path}");
        var source = File.ReadAllText(path);
        var code = Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*", string.Empty);

        foreach (var required in new[]
        {
            "ProjectBase standaloneBase = standaloneProject;",
            "ProjectBase multiuserBase = multiuserProject;",
            "LocalSessionComposition localSessions = portal.LocalSessions;",
            "ProjectServerComposition projectServers = portal.ProjectServers;",
            "Func<FileInfo, LocalSession> open = localSessions.Open;",
            "Func<FileInfo, LocalSession> openServerProject = localSessions.OpenServerProject;",
            "MultiuserProject sessionProject = localSession.Project;",
            "var markingService = localSession.MarkingService;",
            "Action save = localSession.Save;",
            "Action close = localSession.Close;",
            "Func<string, int> closeAndCommit = localSession.CloseAndCommit;",
            "Func<bool> isUptoDate = localSession.IsUptoDate;",
            "FileInfo projectFileInfo = sessionInfo.ProjectFileInfo;",
            "int sessionId = sessionInfo.SessionId;",
            "ProjectServer projectServer"
        })
        {
            Assert.Contains(required, code, StringComparison.Ordinal);
        }

        // All Siemens methods must be represented by method groups, never invoked.
        Assert.DoesNotMatch(@"\.\s*\w+\s*\(", code);
        Assert.DoesNotMatch(@"\b(?:Main|Create|Delete|AddProjectToServer|CreateLocalSession|Mark|Unmark|Lock|Unlock|AcquireLock|ReleaseLock)\b", code);
        Assert.DoesNotMatch(@"\bnew\s+", code);
        Assert.DoesNotMatch(@"\bstatic\s+MultiuserReferenceSurface\s*\(", code);
        Assert.DoesNotContain("TiaMcpServer.Contracts", code, StringComparison.Ordinal);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        return Path.Combine(new[] { root }.Concat(segments).ToArray());
    }
}
