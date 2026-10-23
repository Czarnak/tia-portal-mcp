using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public sealed class ProjectCapabilityCatalogTests
{
    [Theory]
    [InlineData("read_hardware_config", true)]
    [InlineData("read_hardware_page_candidates", true)]
    [InlineData("browse_project_tree_v3_snapshot", true)]
    [InlineData("read_cross_references", true)]
    [InlineData("get_block_content", true)]
    [InlineData("list_tag_tables", true)]
    [InlineData("hmi_list_tag_tables", true)]
    [InlineData("search_equipment_catalog", true)]
    [InlineData("compile_check", true)]
    [InlineData("save_project", true)]
    [InlineData("get_project_status", false)]
    [InlineData("get_basic_project_status", false)]
    [InlineData("probe_project_status_for_lifecycle", false)]
    [InlineData("open_project", false)]
    [InlineData("create_project", false)]
    [InlineData("select_portal_project", false)]
    [InlineData("list_local_sessions", false)]
    public void RequiresBoundProject_GatesContentMethodsOnly(string method, bool expected)
        => Assert.Equal(expected, ProjectCapabilityCatalog.RequiresBoundProject(method));

    [Fact]
    public void RequiresBoundProject_GatesEveryVciRead()
        => Assert.All(VciMethods.Read, method => Assert.True(ProjectCapabilityCatalog.RequiresBoundProject(method), method));
}
