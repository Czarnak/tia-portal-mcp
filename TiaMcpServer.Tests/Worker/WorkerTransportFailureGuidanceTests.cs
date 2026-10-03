using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class WorkerTransportFailureGuidanceTests
{
    [Fact]
    public void TimeoutGuidance_SessionSelection_NamesOpennessDialog()
    {
        var guidance = WorkerTransportFailureGuidance.TimeoutGuidance("select_portal_project");
        Assert.Contains("Openness dialog", guidance);
        Assert.Contains("invalidated", guidance);
        Assert.Contains("bind_project", guidance);
    }

    [Theory]
    [InlineData("browse_project_tree", false)]
    [InlineData("browse_project_tree_v3_snapshot", true)]
    [InlineData("get_block_content", true)]
    [InlineData("get_type_content", true)]
    [InlineData("update_type_content", false)]
    [InlineData("get_type_content_unrecognized", false)]
    [InlineData("read_update_tag_safety_snapshot", true)]
    [InlineData("compile_check", false)]
    [InlineData("save_project", false)]
    [InlineData("update_tag", false)]
    [InlineData("start_plc", false)]
    [InlineData("unknown_method", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void SafetyClassificationComesFromOperationPolicy(string? method, bool expectedSafe)
        => Assert.Equal(expectedSafe, WorkerTransportFailureGuidance.IsSafeRead(method));
}
