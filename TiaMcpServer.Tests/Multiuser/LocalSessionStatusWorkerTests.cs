using System.Text.Json;
using Siemens.Engineering;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using Xunit;
using static TiaMcpServer.Tests.Multiuser.LocalSessionSelectionWorkerTests;

namespace TiaMcpServer.Tests.Multiuser;

[Collection("Portal session boundary")]
public sealed class LocalSessionStatusWorkerTests
{
    [Fact]
    public void LocalStatus_UsesTypedAmcBindingWithMetadataNull()
    {
        using var f = new Fixture();
        var owner = f.AddLocal(f.A, f.Amc);
        owner.Project.IsModified = true;
        f.Session.Connect(f.Amc);

        var status = f.Session.ActiveContext!.ToLocalBasicStatusInfo();
        Assert.True(status.IsOpen);
        Assert.Equal(f.Amc, status.Path);
        Assert.True(status.IsModified);
        Assert.Null(status.Metadata);
        Assert.Equal(f.Amc, status.Context?.EngineeringProjectPath);
        Assert.Equal(f.Amc, f.Session.GetSessionIdentity().ProjectPath);
        Assert.Equal(0, owner.SaveCalls);
        Assert.Equal(0, owner.CloseCalls);
        Assert.Equal(0, owner.CommitCalls);
    }

    [Fact]
    public void ColdStatus_HasNullAlsAndRemoteIdentityWithRequiredNestedNulls()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);

        var status = f.Session.ActiveContext!.ToLocalBasicStatusInfo();
        var payload = new ProjectStatusResultInfo
        {
            Operation = "get_project_status", ProjectPath = status.Path, Project = status
        };
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(payload));
        var root = document.RootElement.GetProperty("project");
        var context = root.GetProperty("context");
        Assert.Equal(f.Amc, document.RootElement.GetProperty("projectPath").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("metadata").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("sessionContainerPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("remoteIdentity").ValueKind);
        Assert.Equal("unknown", context.GetProperty("sessionMode").GetString());
        Assert.Equal("unknown", context.GetProperty("connectionObservation").GetProperty("state").GetString());
        Assert.Equal("sessionBind", context.GetProperty("connectionObservation").GetProperty("observationSource").GetString());
        Assert.Equal(JsonValueKind.Null,
            context.GetProperty("connectionObservation").GetProperty("previousState").ValueKind);
    }

    [Fact]
    public void WorkerEnvelope_StampsRequiredLocalContextNulls()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var status = f.Session.ActiveContext!.ToLocalBasicStatusInfo();
        var response = new WorkerResponse
        {
            Success = true,
            Payload = WorkerJson.SerializePayload(new ProjectStatusResultInfo
                { Operation = "get_project_status", ProjectPath = status.Path, Project = status }),
            ResolvedProjectPath = f.Amc,
            SessionIdentity = f.Session.GetSessionIdentity()
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(response, WorkerJson.Envelope));
        var context = document.RootElement.GetProperty("sessionIdentity").GetProperty("context");
        Assert.Equal(JsonValueKind.Null, context.GetProperty("sessionContainerPath").ValueKind);
        Assert.Equal(JsonValueKind.Null, context.GetProperty("remoteIdentity").ValueKind);
        Assert.Equal(JsonValueKind.Null,
            context.GetProperty("connectionObservation").GetProperty("previousState").ValueKind);
    }

    [Fact]
    public void SameOwnerReverification_PreservesObservationTimestampAndContext()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var context = f.Session.ActiveContext;
        var first = context!.ToLocalBasicStatusInfo();

        f.Session.EnsureConnected(f.Amc);
        var second = f.Session.ActiveContext!.ToLocalBasicStatusInfo();
        Assert.Same(context, f.Session.ActiveContext);
        Assert.Equal(first.Context!.ConnectionObservation!.ObservedAt,
            second.Context!.ConnectionObservation!.ObservedAt);
        Assert.Equal("unknown", second.Context.ConnectionObservation.State);
    }

    [Fact]
    public void OfflineLocalStatus_PreservesContextAndDoesNotClaimConnectivity()
    {
        using var f = new Fixture();
        f.AddLocal(f.A, f.Amc);
        f.Session.Connect(f.Amc);
        var before = f.Session.GetSessionIdentity();

        var status = f.Session.ActiveContext!.ToLocalBasicStatusInfo();
        Assert.Equal(f.Amc, status.Path);
        Assert.Null(status.Context!.RemoteIdentity);
        Assert.Equal(ProjectServerConnectionStates.Unknown, status.Context.ConnectionObservation!.State);
        Assert.Equal(before.SessionGeneration, f.Session.GetSessionIdentity().SessionGeneration);
    }

}
