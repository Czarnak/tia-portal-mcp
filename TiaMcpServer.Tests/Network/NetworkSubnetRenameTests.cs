using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkSubnetRenameTests
{
    [Theory]
    [InlineData("PN/IE_1")]
    [InlineData("pn/ie_2")]
    public void Update_IgnoredRenameFailsWithoutCommitting(string requestedName)
    {
        var (portal, project, subnet) = Fixture();
        subnet.IgnoreNameWrites = true;

        var exception = Assert.Throws<WorkerOperationException>(() => SubnetLifecycleService.Update(
            portal, project, "3547-1", requestedName, 126, null));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.Contains(requestedName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("PN/IE_2", exception.Message, StringComparison.Ordinal);
        Assert.Equal("PN/IE_2", subnet.Name);
        var transaction = Assert.Single(portal.Transactions);
        Assert.True(transaction.Disposed);
        Assert.False(transaction.CommitRequested);
    }

    [Theory]
    [InlineData("PN/IE_3")]
    [InlineData("pn/ie_2")]
    [InlineData("PN/IE_2")]
    public void Update_ExactResultNameCommits(string requestedName)
    {
        var (portal, project, subnet) = Fixture();

        var result = SubnetLifecycleService.Update(portal, project, "3547-1", requestedName, null, null);

        Assert.Equal("3547-1", result.SubnetId);
        Assert.Equal(requestedName, result.Name);
        Assert.Equal(requestedName, subnet.Name);
        Assert.True(Assert.Single(portal.Transactions).Committed);
    }

    [Fact]
    public void Update_AttributeOnlyChangeCommitsWithExistingName()
    {
        var (portal, project, subnet) = Fixture();

        var result = SubnetLifecycleService.Update(portal, project, "3547-1", null, 126, null);

        Assert.Equal("PN/IE_2", result.Name);
        Assert.Equal(126, subnet.Attributes["HighestAddress"]);
        Assert.True(Assert.Single(portal.Transactions).Committed);
    }

    [Fact]
    public void Update_UnreadableNameStillFailsWithoutCommitting()
    {
        var (portal, project, subnet) = Fixture();
        subnet.NameFailure = new EngineeringException("Name is unreadable.");

        var exception = Assert.Throws<WorkerOperationException>(() => SubnetLifecycleService.Update(
            portal, project, "3547-1", "PN/IE_3", null, null));

        Assert.Equal(WorkerFailureCategories.PostconditionFailed, exception.FailureCategory);
        Assert.False(Assert.Single(portal.Transactions).CommitRequested);
    }

    private static (TiaPortal Portal, Siemens.Engineering.Project Project, Subnet Subnet) Fixture()
    {
        var portal = new TiaPortal();
        var project = new Siemens.Engineering.Project();
        var subnet = new Subnet { SubnetId = "3547-1", Name = "PN/IE_2" };
        project.Subnets.Items.Add(subnet);
        project.Subnets.Items.Add(new Subnet { SubnetId = "590-2", Name = "PN/IE_1" });
        return (portal, project, subnet);
    }
}
