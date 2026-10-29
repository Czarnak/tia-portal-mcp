using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Network;
using Xunit;
using EngineeringProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Network;

public sealed class NetworkSubnetSelectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(123)]
    [InlineData("unreadable")]
    public void Subnet_IncompleteCandidateCannotProveAbsenceOrUniqueness(object? identity)
    {
        var unreadable = new Subnet { SubnetId = identity };
        if (Equals(identity, "unreadable")) unreadable.IdentityFailure = new EngineeringException("unavailable");
        foreach (var matching in new[] { false, true })
        foreach (var first in new[] { false, true })
        {
            var project = ProjectWithSubnet(out var readable);
            readable.SubnetId = matching ? "subnet-a" : "subnet-b";
            project.Subnets.Items.Insert(first ? 0 : 1, unreadable);

            AssertEvidenceMismatch(NetworkObjectSelectorResolver.Resolve(project, NetworkSelectorFactory.Subnet("subnet-a")));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IoSystem_UnreadableNumberCannotProveAbsenceOrUniqueness(bool matching)
    {
        var project = ProjectWithSubnet(out var subnet);
        subnet.IoSystems.Items.Add(new IoSystem { Number = matching ? 1 : 2 });
        subnet.IoSystems.Items.Add(new IoSystem { NumberFailure = new EngineeringException("unavailable") });

        AssertEvidenceMismatch(NetworkObjectSelectorResolver.Resolve(project, NetworkSelectorFactory.IoSystem("subnet-a", 1)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unreadable")]
    public void IoSystem_ConstrainedNameMustBeUsable(string? name)
    {
        var project = ProjectWithSubnet(out var subnet);
        var unreadable = new IoSystem { Number = 1, Name = name! };
        if (name == "unreadable") unreadable.NameFailure = new EngineeringException("unavailable");
        subnet.IoSystems.Items.Add(new IoSystem { Number = 1, Name = "system-a" });
        subnet.IoSystems.Items.Add(unreadable);

        AssertEvidenceMismatch(NetworkObjectSelectorResolver.Resolve(project,
            NetworkSelectorFactory.IoSystem("subnet-a", 1, ioSystemName: "system-a")));
    }

    [Fact]
    public void IoSystem_NegativeNumberIsIncompleteEvidence()
    {
        var project = ProjectWithSubnet(out var subnet);
        subnet.IoSystems.Items.Add(new IoSystem { Number = -1 });

        AssertEvidenceMismatch(NetworkObjectSelectorResolver.Resolve(project, NetworkSelectorFactory.IoSystem("subnet-a", 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteEnumerationFailsClosed(bool ioSystem)
    {
        var project = ProjectWithSubnet(out var subnet);
        subnet.IoSystems.Items.Add(new IoSystem { Number = 1 });
        if (ioSystem) subnet.IoSystems.EnumerationFailure = new EngineeringException("unavailable");
        else project.Subnets.EnumerationFailure = new EngineeringException("unavailable");

        AssertEvidenceMismatch(NetworkObjectSelectorResolver.Resolve(project, ioSystem
            ? NetworkSelectorFactory.IoSystem("subnet-a", 1)
            : NetworkSelectorFactory.Subnet("subnet-a")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteNamespacesRetainMissingAmbiguousAndUniqueOutcomes(bool ioSystem)
    {
        var project = ProjectWithSubnet(out var subnet);
        var target = ioSystem ? NetworkSelectorFactory.IoSystem("subnet-a", 1) : NetworkSelectorFactory.Subnet("subnet-b");
        Assert.Equal(WorkerFailureCategories.TargetNotFound, NetworkObjectSelectorResolver.Resolve(project, target).FailureCategory);

        if (ioSystem) subnet.IoSystems.Items.Add(new IoSystem { Number = 1 });
        else subnet.SubnetId = "subnet-b";
        Assert.True(NetworkObjectSelectorResolver.Resolve(project, target).Success);

        if (ioSystem) subnet.IoSystems.Items.Add(new IoSystem { Number = 1 });
        else project.Subnets.Items.Add(new Subnet { SubnetId = "subnet-b" });
        Assert.Equal(WorkerFailureCategories.TargetAmbiguous, NetworkObjectSelectorResolver.Resolve(project, target).FailureCategory);
    }

    [Fact]
    public void IoSystem_IndexAndNameConstraintsExcludeUnrelatedCandidates()
    {
        var project = ProjectWithSubnet(out var subnet);
        subnet.IoSystems.Items.Add(new IoSystem { Number = 2, NameFailure = new EngineeringException("unavailable") });
        var expected = new IoSystem { Number = 1, Name = "system-a" };
        subnet.IoSystems.Items.Add(expected);
        subnet.IoSystems.Items.Add(new IoSystem { Number = 1, Name = "system-b" });

        var selected = NetworkObjectSelectorResolver.Resolve(project,
            NetworkSelectorFactory.IoSystem("subnet-a", 1, ioSystemName: "system-a"));
        Assert.Same(expected, selected.Resolved!.Value);
        Assert.Equal(1, selected.Resolved.Target.IoSystemIndex);

        subnet.IoSystems.Items[0].NumberFailure = new EngineeringException("unavailable");
        selected = NetworkObjectSelectorResolver.Resolve(project, NetworkSelectorFactory.IoSystem("subnet-a", 1, 1, "system-a"));
        Assert.Same(expected, selected.Resolved!.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IoSystem_UnconstrainedNameIsOptional(bool unreadable)
    {
        var project = ProjectWithSubnet(out var subnet);
        subnet.IoSystems.Items.Add(new IoSystem { Number = 1, Name = null!,
            NameFailure = unreadable ? new EngineeringException("unavailable") : null });

        Assert.True(NetworkObjectSelectorResolver.Resolve(project, NetworkSelectorFactory.IoSystem("subnet-a", 1)).Success);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PortalLossAndProgrammingErrorsPropagate(bool ioSystem, bool enumeration)
    {
        foreach (var failure in new Exception[] { new NonRecoverableException("lost"), new InvalidOperationException("bug") })
        {
            var project = ProjectWithSubnet(out var subnet);
            var system = new IoSystem { Number = 1 };
            subnet.IoSystems.Items.Add(system);
            if (ioSystem && enumeration) subnet.IoSystems.EnumerationFailure = failure;
            else if (ioSystem) system.NumberFailure = failure;
            else if (enumeration) project.Subnets.EnumerationFailure = failure;
            else subnet.IdentityFailure = failure;

            var actual = Record.Exception(() => NetworkObjectSelectorResolver.Resolve(project, ioSystem
                ? NetworkSelectorFactory.IoSystem("subnet-a", 1)
                : NetworkSelectorFactory.Subnet("subnet-a")));
            Assert.Same(failure, actual);
        }
    }

    private static EngineeringProject ProjectWithSubnet(out Subnet subnet)
    {
        var project = new EngineeringProject();
        subnet = new Subnet();
        project.Subnets.Items.Add(subnet);
        return project;
    }

    private static void AssertEvidenceMismatch(NetworkObjectSelectionResult result)
    {
        Assert.False(result.Success);
        Assert.Null(result.Resolved);
        Assert.Equal(WorkerFailureCategories.TargetEvidenceMismatch, result.FailureCategory);
    }
}
