using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class WorkerTransportFailureGuidanceTests
{
    [Theory]
    [InlineData("browse_project_tree", false)]
    [InlineData("browse_project_tree_v3_snapshot", true)]
    [InlineData("get_block_content", true)]
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
