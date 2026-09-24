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
        Assert.Contains("catch (Exception)", body);
        Assert.DoesNotContain("exception.Message", body);
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
        Assert.Contains("IoSystemQualificationEvidence.InspectOwner", owner);
        Assert.Contains("diagnostic.Reason != \"verified\"", owner);
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

    [Fact]
    public void ExpectedValueAndMetadata_MustMatchExactly()
    {
        var observation = new IoSystemQualificationAttributeInfo
        {
            Name = "Number", Available = true, Writable = true,
            SupportedTypes = new() { "System.Int32" },
            Value = new() { Kind = "integer", IntegerValue = 2 }
        };
        var request = new IoSystemQualificationProbeInfo
        {
            AttributeName = "Number", ExpectedValue = new() { Kind = "integer", IntegerValue = 2 },
            DesiredValue = new() { Kind = "integer", IntegerValue = 3 }
        };
        Assert.Null(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.Writable = false;
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.Writable = true;
        observation.Value.IntegerValue = 9;
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.Value.IntegerValue = 2;
        observation.SupportedTypes = new() { "System.String" };
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
        observation.SupportedTypes = new() { "System.Int32" };
        observation.Available = false;
        Assert.NotNull(TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.ValidateChange(observation, request));
    }

    [Fact]
    public void EvidenceBounds_PreserveCommittedOutcome_AndReportOmissions()
    {
        var result = new IoSystemQualificationResultInfo { MutationCommitted = true, CompileState = "Error" };
        for (var i = 0; i < 70; i++)
            TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.AddMessage(result, new string('x', 5000));
        Assert.Equal(32, result.Messages.Count);
        Assert.Equal(38, result.OmittedMessageCount);
        Assert.All(result.Messages, message => Assert.True(message.Length <= 512));
        var serialized = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SerializeBounded(result);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(serialized) <= 65536);
        result.Before.Add(new() { Value = new() { Kind = "string", StringValue = new string('x', 100000) } });
        serialized = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SerializeBounded(result);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(serialized) <= 65536);
        Assert.Contains("\"mutationCommitted\":true", serialized);
        Assert.Contains("\"evidenceOmitted\":true", serialized);
    }
    [Fact]
    public void CompilerEvidence_RedactsPathsAndCredentials_AndKeepsUsefulText()
    {
        var result = new IoSystemQualificationResultInfo();
        TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.AddMessage(result,
            @"Invalid station at C:\private\fixture.ap21 password=secret123 token: abcdef /home/private/project");
        var message = Assert.Single(result.Messages);
        Assert.Contains("Invalid station", message);
        Assert.DoesNotContain("fixture.ap21", message);
        Assert.DoesNotContain("secret123", message);
        Assert.DoesNotContain("abcdef", message);
        Assert.DoesNotContain("/home/private", message);
    }

    [Fact]
    public void ResultBudget_TrimsMessagesBeforeAppliedState()
    {
        var result = new IoSystemQualificationResultInfo { MutationCommitted = true };
        result.After.Add(new() { Name = "Name", Value = new() { Kind = "string", StringValue = new string('a', 54000) } });
        for (var i = 0; i < 32; i++)
            TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.AddMessage(result, new string('x', 512));
        var json = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SerializeBounded(result);
        Assert.Contains(new string('a', 54000), json);
        Assert.DoesNotContain("\"evidenceOmitted\":true", json);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(json) <= 65536);
    }
    [Theory]
    [InlineData("inspectOwner", "target_not_found")]
    [InlineData("compileBaseline", "worker_operation_failed")]
    [InlineData("setAndCompile", "worker_operation_failed")]
    public void ConvertedSessionFailure_IsNormalizedWithoutRawDiagnostics(string mode, string category)
    {
        var identity = new WorkerSessionIdentity();
        var response = new WorkerResponse
        {
            Success = false, FailureCategory = category,
            Error = @"C:\private\fixture.ap21 token=private-value " + new string('x', 100000),
            Warnings = new() { "raw private warning" }, Payload = "raw private payload",
            ResolvedProjectPath = "private-protocol-identity", SessionIdentity = identity
        };
        var normalized = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.NormalizeSessionResponse(response, mode);
        Assert.False(normalized.Success);
        Assert.Equal(category, normalized.FailureCategory);
        Assert.NotSame(response, normalized);
        Assert.Null(normalized.Payload);
        Assert.Null(normalized.Warnings);
        Assert.NotNull(normalized.Error);
        Assert.True(normalized.Error.Length <= 512);
        Assert.DoesNotContain("private", normalized.Error);
        Assert.Contains("inspect current state", normalized.Error);
        Assert.Contains("retry", normalized.Error);
        Assert.Equal(response.ResolvedProjectPath, normalized.ResolvedProjectPath);
        Assert.Same(identity, normalized.SessionIdentity);
        if (mode == "setAndCompile") Assert.Contains("may have committed", normalized.Error);
        else Assert.DoesNotContain("may have committed", normalized.Error);
    }

    [Fact]
    public void SuccessfulSessionResponse_PreservesTypedCommittedEvidence()
    {
        var response = new WorkerResponse { Success = true, Payload = "typed-committed-state" };
        Assert.Same(response, TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.NormalizeSessionResponse(response, "setAndCompile"));
    }

    [Fact]
    public void QualificationBoundary_NormalizesReturnedSessionFailures()
    {
        var program = File.ReadAllText(Find("TiaMcpServer.OpennessWorker/Program.cs"));
        var body = ExtractMethodBody(program, "ProbeIoSystemQualification");
        Assert.DoesNotContain("return WithSession(", body);
        Ordered(body, "var response = WithSession(", "IoSystemQualificationProbeService.",
            "return IoSystemQualificationEvidence.NormalizeSessionResponse(response, request.IoSystemQualification!.Mode)");
    }
    [Theory]
    [InlineData(0, "no_matches")]
    [InlineData(2, "multiple_matches")]
    public void OwnerDiagnostics_CountMatchesBeforeReadingPaths(int count, string reason)
    {
        var pathCalls = 0;
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => { for (var i = 0; i < count; i++) matches.Add(i); },
            _ => { pathCalls++; return new(); }, _ => { verifyCalls++; return true; });
        Assert.Equal("matching", diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(count, diagnostic.MatchCount);
        Assert.True(diagnostic.TraversalCompleted);
        Assert.Equal(0, pathCalls);
        Assert.Equal(0, verifyCalls);
    }

    [Fact]
    public void OwnerDiagnostics_IncompleteMatchedPathCannotReachSelectorVerification()
    {
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1),
            _ => TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.SummarizeOwnerPath("synthetic-device", new[]
            {
                new DeviceItemPathSegmentInfo { Index = 0, Name = "synthetic-item", TypeIdentifier = "", PositionNumber = -1 }
            }), _ => { verifyCalls++; return true; });
        Assert.Equal("pathEvidence", diagnostic.Stage);
        Assert.Equal("incomplete_path", diagnostic.Reason);
        Assert.Equal(1, diagnostic.MatchCount);
        Assert.True(diagnostic.TraversalCompleted);
        Assert.Equal(1, diagnostic.Path!.Depth);
        Assert.Equal(1, diagnostic.Path.BlankTypeIdentifierCount);
        Assert.Equal(1, diagnostic.Path.NegativePositionCount);
        Assert.Equal(0, verifyCalls);
        var serialized = System.Text.Json.JsonSerializer.Serialize(diagnostic);
        Assert.DoesNotContain("synthetic", serialized);
        Assert.True(serialized.Length < 1024);
    }

    [Fact]
    public void OwnerDiagnostics_TraversalExceptionRetainsOnlyClosedReasonAndPartialCount()
    {
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => { matches.Add(1); throw new ArgumentException("private exception text"); },
            _ => throw new InvalidOperationException("Path must not be read"),
            _ => { verifyCalls++; return true; });
        Assert.Equal("traversal", diagnostic.Stage);
        Assert.Equal("traversal_failed", diagnostic.Reason);
        Assert.False(diagnostic.TraversalCompleted);
        Assert.Equal(1, diagnostic.MatchCount);
        Assert.Null(diagnostic.Path);
        Assert.Equal(0, verifyCalls);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(diagnostic));
    }

    [Theory]
    [InlineData(true, "verified")]
    [InlineData(false, "identity_unverified")]
    public void OwnerDiagnostics_CompletePathStillRequiresIdentityProof(bool identityMatches, string reason)
    {
        var verifyCalls = 0;
        var diagnostic = TiaMcpServer.OpennessWorker.Openness.IoSystemQualificationEvidence.InspectOwner<int>(
            matches => matches.Add(1), _ => new() { Depth = 1 },
            _ => { verifyCalls++; return identityMatches; });
        Assert.Equal("verification", diagnostic.Stage);
        Assert.Equal(reason, diagnostic.Reason);
        Assert.Equal(1, verifyCalls);
    }

    [Fact]
    public void OwnerInspection_CollectsBeforeSelectorBuildAndReturnsOnlyAggregateFailure()
    {
        var collect = ExtractMethodBody(Source, "FindOwners");
        Assert.DoesNotContain("NetworkSelectorFactory", collect);
        Assert.Contains("new OwnerCandidate(", collect);
        var inspect = ExtractMethodBody(Source, "InspectOwner");
        Assert.Contains("OwnerDiagnostics = diagnostic", inspect);
        Assert.DoesNotContain("GetService<ICompilable>", inspect);
        Assert.DoesNotContain("SetAttribute", inspect);
        var require = ExtractMethodBody(Source, "RequireExactOwningDeviceItem");
        Assert.Contains("diagnostic.Reason != " + '"' + "verified" + '"', require);
        Assert.Contains("throw Failure(", require);
        Assert.DoesNotContain("CompileHardware", require);
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
