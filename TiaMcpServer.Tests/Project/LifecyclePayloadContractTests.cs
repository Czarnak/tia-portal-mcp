using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.ProjectLifecycle;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class LifecyclePayloadContractTests
{
    [Theory]
    [InlineData("wrongOperation")]
    [InlineData("wrongPath")]
    [InlineData("falseSuccess")]
    [InlineData("missingMember")]
    [InlineData("unreadable")]
    public void SuccessfulWorkerEnvelope_CannotCarryAnInvalidLifecycleCandidate(string defect)
    {
        var path = Path.Combine(Path.GetTempPath(), "Source.ap21");
        var payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "save_project", ProjectPath = path,
            Project = new ProjectStatusInfo { IsOpen = true, Path = path, IsModified = false }
        });
        var root = JsonNode.Parse(payload)!.AsObject();
        if (defect == "wrongOperation") root["operation"] = "archive_project";
        if (defect == "wrongPath") root["project"]!["path"] = path + ".other";
        if (defect == "falseSuccess") root["success"] = false;
        if (defect == "missingMember") root["project"]!.AsObject().Remove("metadata");
        var worker = WorkerCallResult.Ok(defect == "unreadable" ? "not json" : root.ToJsonString()) with
        { ResolvedProjectPath = path, SessionIdentity = new WorkerSessionIdentity { ProjectPath = path } };
        Assert.ThrowsAny<JsonException>(() => LifecyclePayloadContract.Decode(worker, "save_project", path));
    }

    [Fact]
    public void ClosedMutation_PreservesHistoricalSource_WhileVerificationRequiresNoOpenProject()
    {
        var path = Path.Combine(Path.GetTempPath(), "Source.ap21");
        var mutation = WorkerCallResult.Ok(WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "close_project", ProjectPath = path,
            Project = new ProjectStatusInfo { IsOpen = false, Path = path }
        })) with { SessionIdentity = new WorkerSessionIdentity() };
        Assert.Equal(path, LifecyclePayloadContract.Decode(mutation, "close_project", path).ProjectPath);
        Assert.Throws<JsonException>(() => LifecyclePayloadContract.DecodeStatus(mutation,
            "get_project_status", expectedProjectPath: null, expectOpen: false));
        var verification = mutation with { Payload = WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
            { Operation = "get_project_status", Project = new ProjectStatusInfo { IsOpen = false } }) };
        Assert.False(LifecyclePayloadContract.DecodeStatus(verification,
            "get_project_status", expectedProjectPath: null, expectOpen: false).IsOpen);
    }

    [Fact]
    public void InvalidCandidatePath_IsAProtocolFailureInsteadOfAnUnexpectedPipelineException()
    {
        var path = Path.Combine(Path.GetTempPath(), "invalid\0Project.ap21");
        var worker = WorkerCallResult.Ok(WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "create_project", ProjectPath = path,
            Project = new ProjectStatusInfo { IsOpen = true, Path = path }
        })) with { ResolvedProjectPath = path, SessionIdentity = new WorkerSessionIdentity { ProjectPath = path } };
        Assert.ThrowsAny<JsonException>(() => LifecyclePayloadContract.Decode(worker, "create_project", path, Path.GetTempPath()));
    }

    [Fact]
    public void NoOpenVerification_RejectsContradictoryResolvedPath()
    {
        var worker = WorkerCallResult.Ok(WorkerJson.SerializePayload(new ProjectLifecycleResultInfo
        {
            Operation = "get_project_status", Project = new ProjectStatusInfo { IsOpen = false }
        })) with { ResolvedProjectPath = Path.Combine(Path.GetTempPath(), "Other.ap21"), SessionIdentity = new WorkerSessionIdentity() };
        Assert.ThrowsAny<JsonException>(() => LifecyclePayloadContract.DecodeStatus(worker,
            "get_project_status", expectedProjectPath: null, expectOpen: false));
    }
}
