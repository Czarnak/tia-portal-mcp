using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectRebindStateInfoTests
{
    [Fact]
    public void Create_CanonicalSourceAndDestination_WorkerOwnedDifferentPathWillClose()
    {
        var state = ProjectRebindStateInfo.Create(
            @"C:\Lifecycle\Intermediate\..\A.ap21",
            @"C:\Lifecycle\Other\..\B.ap21",
            sourceIsModified: false,
            sourceOpenedByWorker: true);

        Assert.Equal(@"C:\Lifecycle\A.ap21", state.SourceProjectPath);
        Assert.Equal(@"C:\Lifecycle\B.ap21", state.DestinationProjectPath);
        Assert.Equal(false, state.SourceIsModified);
        Assert.True(state.SourceOpenedByWorker);
        Assert.True(state.WillCloseSource);
    }

    [Fact]
    public void Create_UiOwnedDifferentPath_LeavesSourceOpen()
    {
        var state = ProjectRebindStateInfo.Create(
            @"C:\Lifecycle\A.ap21",
            @"C:\Lifecycle\B.ap21",
            sourceIsModified: true,
            sourceOpenedByWorker: false);

        Assert.Equal(true, state.SourceIsModified);
        Assert.False(state.SourceOpenedByWorker);
        Assert.False(state.WillCloseSource);
    }

    [Fact]
    public void Create_SamePathWithDifferentSpelling_IsIdempotent()
    {
        var state = ProjectRebindStateInfo.Create(
            @"C:\Lifecycle\A.ap21",
            @"c:\lifecycle\Subfolder\..\a.ap21",
            sourceIsModified: true,
            sourceOpenedByWorker: true);

        Assert.Equal(@"C:\Lifecycle\A.ap21", state.SourceProjectPath);
        Assert.Equal(@"c:\lifecycle\a.ap21", state.DestinationProjectPath);
        Assert.Equal(true, state.SourceIsModified);
        Assert.False(state.WillCloseSource);
    }

    [Fact]
    public void Create_UnboundSource_HasNoSourceOrClose()
    {
        var state = ProjectRebindStateInfo.Create(
            sourceProjectPath: null,
            destinationProjectPath: @"C:\Lifecycle\B.ap21",
            sourceIsModified: null,
            sourceOpenedByWorker: false);

        Assert.Null(state.SourceProjectPath);
        Assert.Equal(@"C:\Lifecycle\B.ap21", state.DestinationProjectPath);
        Assert.Null(state.SourceIsModified);
        Assert.False(state.SourceOpenedByWorker);
        Assert.False(state.WillCloseSource);
    }

    [Fact]
    public void Create_PresentSourceWithoutModifiedState_RejectsUnknownSnapshot()
    {
        Assert.ThrowsAny<ArgumentException>(() => ProjectRebindStateInfo.Create(
            @"C:\Lifecycle\A.ap21",
            @"C:\Lifecycle\B.ap21",
            sourceIsModified: null,
            sourceOpenedByWorker: true));
    }

    [Fact]
    public void JsonRoundTrip_UsesSettableCamelCaseSnapshotFields()
    {
        const string json = """
            {"sourceProjectPath":"C:\\Lifecycle\\A.ap21","destinationProjectPath":"C:\\Lifecycle\\B.ap21","sourceIsModified":true,"sourceOpenedByWorker":true,"willCloseSource":true}
            """;

        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var state = JsonSerializer.Deserialize<ProjectRebindStateInfo>(json, options);

        Assert.NotNull(state);
        Assert.Equal(@"C:\Lifecycle\A.ap21", state.SourceProjectPath);
        Assert.Equal(@"C:\Lifecycle\B.ap21", state.DestinationProjectPath);
        Assert.Equal(true, state.SourceIsModified);
        Assert.True(state.SourceOpenedByWorker);
        Assert.True(state.WillCloseSource);

        using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(state, options));
        Assert.Equal(5, roundTrip.RootElement.EnumerateObject().Count());
        Assert.Equal(@"C:\Lifecycle\A.ap21", roundTrip.RootElement.GetProperty("sourceProjectPath").GetString());
        Assert.Equal(@"C:\Lifecycle\B.ap21", roundTrip.RootElement.GetProperty("destinationProjectPath").GetString());
        Assert.True(roundTrip.RootElement.GetProperty("sourceIsModified").GetBoolean());
        Assert.True(roundTrip.RootElement.GetProperty("sourceOpenedByWorker").GetBoolean());
        Assert.True(roundTrip.RootElement.GetProperty("willCloseSource").GetBoolean());
    }
}
