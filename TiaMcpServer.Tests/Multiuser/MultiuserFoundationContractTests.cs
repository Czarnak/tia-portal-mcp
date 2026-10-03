using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Multiuser;

public sealed class MultiuserFoundationContractTests
{
    [Fact]
    public void ProjectContainerKinds_ExposeTheExactOrderedVocabulary()
    {
        Assert.Equal(
            new[] { "standaloneProject", "localSession", "serverProject" },
            ProjectContainerKinds.All.ToArray());
    }

    [Fact]
    public void MultiuserSessionModes_ExposeTheExactOrderedVocabulary()
    {
        Assert.Equal(
            new[] { "multiuser", "exclusive", "unknown", "notApplicable" },
            MultiuserSessionModes.All.ToArray());
    }

    [Fact]
    public void ProjectCapabilityApplicabilities_ExposeTheExactOrderedVocabulary()
    {
        Assert.Equal(
            new[]
            {
                "projectContent", "standaloneOnly", "localSessionConditional",
                "serverProjectOnly", "notYetDelivered", "unsupported",
            },
            ProjectCapabilityApplicabilities.All.ToArray());
    }

    [Fact]
    public void ProjectServerConnectionStates_ExposeTheExactOrderedVocabulary()
    {
        Assert.Equal(
            new[] { "connected", "unavailable", "unknown" },
            ProjectServerConnectionStates.All.ToArray());
    }

    [Fact]
    public void ProjectServerConnectionObservationSources_KeepSessionBindDistinctFromSessionOpen()
    {
        Assert.Equal(
            new[] { "sessionOpen", "sessionBind", "explicitRead", "operationPreflight", "postFailure" },
            ProjectServerConnectionObservationSources.All.ToArray());
    }

    [Fact]
    public void RemoteIdentity_WritesCamelCaseAndEveryAbsentMemberAsNull()
    {
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(
            new MultiuserRemoteIdentity { ServerAlias = "engineering" }));
        var root = document.RootElement;

        Assert.Equal(
            new[] { "group", "host", "localSessionId", "localSessionPath", "port", "protocol", "serverAlias", "serverProjectName" },
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("engineering", root.GetProperty("serverAlias").GetString());
        foreach (var name in new[] { "host", "port", "protocol", "group", "serverProjectName", "localSessionId", "localSessionPath" })
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty(name).ValueKind);
        }
    }

    [Fact]
    public void RootGroup_WritesExplicitNullNameWithoutInventingASentinel()
    {
        Assert.Equal(
            """{"isRoot":true,"name":null}""",
            WorkerJson.SerializePayload(new ProjectServerGroupIdentity { IsRoot = true }));
    }

    [Fact]
    public void RemoteIdentity_WritesNumericNullableIdsAndNestedNamedGroup()
    {
        var identity = new MultiuserRemoteIdentity
        {
            ServerAlias = "engineering",
            Host = "project-server",
            Port = 8735,
            Protocol = "https",
            Group = new ProjectServerGroupIdentity { IsRoot = false, Name = "team" },
            ServerProjectName = "Line1",
            LocalSessionId = 42,
            LocalSessionPath = @"C:\Sessions\Line1.als21",
        };
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(identity));
        var root = document.RootElement;

        Assert.Equal(typeof(int?), typeof(MultiuserRemoteIdentity).GetProperty(nameof(MultiuserRemoteIdentity.Port))!.PropertyType);
        Assert.Equal(typeof(int?), typeof(MultiuserRemoteIdentity).GetProperty(nameof(MultiuserRemoteIdentity.LocalSessionId))!.PropertyType);
        Assert.Equal(JsonValueKind.Number, root.GetProperty("port").ValueKind);
        Assert.Equal(8735, root.GetProperty("port").GetInt32());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("localSessionId").ValueKind);
        Assert.Equal(42, root.GetProperty("localSessionId").GetInt32());
        Assert.Equal("project-server", root.GetProperty("host").GetString());
        Assert.Equal("https", root.GetProperty("protocol").GetString());
        Assert.Equal("Line1", root.GetProperty("serverProjectName").GetString());
        Assert.Equal(@"C:\Sessions\Line1.als21", root.GetProperty("localSessionPath").GetString());
        Assert.Equal(JsonValueKind.Object, root.GetProperty("group").ValueKind);
        Assert.False(root.GetProperty("group").GetProperty("isRoot").GetBoolean());
        Assert.Equal("team", root.GetProperty("group").GetProperty("name").GetString());
    }

    [Fact]
    public void InitialUnknownObservation_WritesExplicitNullPreviousStateAndNoTransition()
    {
        var observedAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.FromHours(2));
        var observation = new ProjectServerConnectionObservation
        {
            State = ProjectServerConnectionStates.Unknown,
            ObservedAt = observedAt,
            ObservationSource = ProjectServerConnectionObservationSources.SessionBind,
        };
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(observation));
        var root = document.RootElement;

        Assert.Equal(
            new[] { "observationSource", "observedAt", "previousState", "state", "transition" },
            root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("unknown", root.GetProperty("state").GetString());
        Assert.Equal("sessionBind", root.GetProperty("observationSource").GetString());
        Assert.Equal(typeof(DateTimeOffset), typeof(ProjectServerConnectionObservation).GetProperty(nameof(ProjectServerConnectionObservation.ObservedAt))!.PropertyType);
        Assert.Equal(observedAt, root.GetProperty("observedAt").GetDateTimeOffset());
        Assert.Equal(observedAt.Offset, root.GetProperty("observedAt").GetDateTimeOffset().Offset);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("previousState").ValueKind);
        Assert.False(root.GetProperty("transition").GetBoolean());
    }

    [Fact]
    public void ChangedObservation_PreservesThePreviousStateAndTransition()
    {
        using var document = JsonDocument.Parse(WorkerJson.SerializePayload(
            new ProjectServerConnectionObservation
            {
                State = ProjectServerConnectionStates.Unavailable,
                ObservedAt = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero),
                ObservationSource = ProjectServerConnectionObservationSources.PostFailure,
                PreviousState = ProjectServerConnectionStates.Connected,
                Transition = true,
            }));
        var root = document.RootElement;

        Assert.Equal("unavailable", root.GetProperty("state").GetString());
        Assert.Equal("connected", root.GetProperty("previousState").GetString());
        Assert.Equal("postFailure", root.GetProperty("observationSource").GetString());
        Assert.True(root.GetProperty("transition").GetBoolean());
    }

    [Fact]
    public void CapabilityInfo_WritesDescriptiveOperationAndApplicability()
    {
        Assert.Equal(
            """{"operation":"read_project","applicability":"projectContent"}""",
            WorkerJson.SerializePayload(new ProjectCapabilityInfo
            {
                Operation = "read_project",
                Applicability = ProjectCapabilityApplicabilities.ProjectContent,
            }));
    }

    [Fact]
    public void ContractsAssembly_HasNoSiemensEngineeringReference()
    {
        Assert.DoesNotContain(
            typeof(MultiuserRemoteIdentity).Assembly.GetReferencedAssemblies(),
            reference => reference.Name?.StartsWith("Siemens.Engineering", StringComparison.Ordinal) == true);
    }
}
