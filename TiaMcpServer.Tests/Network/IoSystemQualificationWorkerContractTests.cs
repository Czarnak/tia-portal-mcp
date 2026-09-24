using System.Text.RegularExpressions;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public class IoSystemQualificationWorkerContractTests
{
    private static string Source => File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Openness/IoSystemQualificationProbeService.cs"));

    [Fact]
    public void Dispatch_ValidatesBeforeSessionAccess_AndRemainsProtected()
    {
        var program = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Program.cs"));
        Assert.Contains("\"probe_io_system_qualification\" => ProbeIoSystemQualification(request)", program);
        var body = ExtractMethodBody(program, "ProbeIoSystemQualification");
        Ordered(body, "IoSystemQualificationProbeValidator.Validate(request)", "WithSession(", "ValidateExpectedAfterProjectResolution(", "IoSystemQualificationProbeService.");
        Assert.Equal(OperationCapability.ProjectMutation, OperationPolicyCatalog.GetCapability("probe_io_system_qualification"));
    }

    [Fact]
    public void OwnerInspection_CannotObtainCompilerOrMutate()
    {
        var body = ExtractMethodBody(Source, "InspectOwner");
        Assert.Contains("RequireExactIoSystem(", body);
        Assert.Contains("RequireExactOwningDeviceItem(", body);
        Assert.DoesNotContain("Compile", body);
        Assert.DoesNotContain("SetAttribute", body);
        Assert.DoesNotContain("ExclusiveAccess", body);
        var owner = ExtractMethodBody(Source, "RequireExactOwningDeviceItem");
        Assert.Contains("matches.Count != 1", owner);
        Assert.DoesNotContain("First", owner);
        Assert.DoesNotContain("ICompilable", owner);
    }

    [Fact]
    public void Baseline_UsesExactOwnerWithoutSetter()
    {
        var body = ExtractMethodBody(Source, "CompileBaseline");
        Ordered(body, "RequireExactIoSystem(", "RequireExactOwningDeviceItem(", "CompileHardware(");
        Assert.DoesNotContain("ApplySingleField", body);
        var compile = ExtractMethodBody(Source, "CompileHardware");
        Assert.Contains("GetService<ICompilable>()", Source);
        Assert.Contains("compiler.Compile()", compile);
        Assert.Contains("compiler is null", Source);
    }

    [Fact]
    public void Mutation_CommitsBeforeFreshReadAndCompile()
    {
        var body = ExtractMethodBody(Source, "SetAndCompile");
        Ordered(body, "portal.ExclusiveAccess(", "exclusive.Transaction(project,", "RequireExactIoSystem(",
            "RequireExactOwningDeviceItem(", "ReadFiveAttributeSnapshot(", "RequireExpectedValueAndWritableMetadata(",
            "ApplySingleField(", "transaction.CommitOnDispose();", "ReadAppliedStateAndNewSelector(", "CompileHardware(");
        Assert.DoesNotContain("project.Save(", Source);
        Assert.DoesNotContain("PlcSoftware", Source);
        Assert.DoesNotContain("Download", Source);
        Assert.Contains("MutationCommitted = true", body);
        var post = ExtractMethodBody(Source, "ReadAppliedStateAndNewSelector");
        Assert.Contains("RequireExactIoSystem(", post);
    }

    private static void Ordered(string source, params string[] values)
    {
        var prior = -1;
        foreach (var value in values)
        {
            var next = source.IndexOf(value, prior + 1, StringComparison.Ordinal);
            Assert.True(next > prior, $"Missing or out-of-order {value}");
            prior = next;
        }
    }

    internal static string ExtractMethodBody(string source, string name)
    {
        var match = Regex.Match(source, @"(?:public|private|internal)\s+static\s+[^\r\n]+\s+" + name + @"\s*\(");
        Assert.True(match.Success, $"Missing method {name}");
        var start = source.IndexOf('{', match.Index);
        var depth = 0;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException();
    }

    private static string Find(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(Path.Combine(dir.FullName, "TiaMcpServer.sln"))) return path;
        }
        throw new InvalidOperationException("Repository not found.");
    }
}
