using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaMcpServer.OpennessWorker.Openness.Project;
using Xunit;
using TiaProject = Siemens.Engineering.Project;

namespace TiaMcpServer.Tests.Network;

public sealed class ProjectDeviceNameMatcherTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void FindMatches_SearchesEveryDeviceScope_CaseInsensitively(int scope)
    {
        var project = new TiaProject();
        var device = new Device { Name = "ET 200SP" };
        AddDevice(project, scope, device);

        var match = Assert.Single(ProjectDeviceNameMatcher.FindMatches(project, "et 200sp"));

        Assert.Same(device, match.Device);
        Assert.Equal("ET 200SP", match.Name);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    public void FindMatches_ReturnsEveryDuplicateAcrossScopes(int firstScope, int secondScope)
    {
        var project = new TiaProject();
        var first = new Device { Name = "Station" };
        var second = new Device { Name = "STATION" };
        AddDevice(project, firstScope, first);
        AddDevice(project, secondScope, second);

        var matches = ProjectDeviceNameMatcher.FindMatches(project, "station");

        Assert.Equal(2, matches.Count);
        Assert.Same(first, matches[0].Device);
        Assert.Same(second, matches[1].Device);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindMatches_FailureInLaterScopeAfterDirectCandidate_Propagates(bool ungrouped)
    {
        var project = new TiaProject();
        project.Devices.Items.Add(new Device { Name = "Station" });
        var failure = new InvalidOperationException("Incomplete project discovery");
        if (ungrouped)
        {
            project.UngroupedDevicesGroup.Devices.EnumerationFailure = failure;
        }
        else
        {
            project.DeviceGroups.EnumerationFailure = failure;
        }

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(
            () => ProjectDeviceNameMatcher.FindMatches(project, "Station")));
    }

    [Fact]
    public void FindMatches_UngroupedGetterFailureAfterDirectCandidate_Propagates()
    {
        var project = new TiaProject();
        project.Devices.Items.Add(new Device { Name = "Station" });
        var failure = new EngineeringException("Ungrouped devices unavailable");
        project.UngroupedDevicesGroupFailure = failure;
        var reported = new List<EngineeringException>();

        Assert.Same(failure, Assert.Throws<EngineeringException>(
            () => ProjectDeviceNameMatcher.FindMatches(project, "Station", reported.Add)));
        Assert.Empty(reported);
    }

    [Fact]
    public void FindMatches_DirectEnumerationFailureAfterCandidate_Propagates()
    {
        var project = new TiaProject();
        project.Devices.Items.Add(new Device { Name = "Station" });
        var failure = new InvalidOperationException("Direct devices unavailable");
        project.Devices.EnumerationFailure = failure;

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(
            () => ProjectDeviceNameMatcher.FindMatches(project, "Station")));
    }

    [Fact]
    public void FindMatches_UnreadableName_SkipsCandidateAndReportsFailure()
    {
        var project = new TiaProject();
        var failure = new EngineeringException("Device name unavailable");
        project.Devices.Items.Add(new Device { Name = "Station", NameFailure = failure });
        var readable = new Device { Name = "Station" };
        project.Devices.Items.Add(readable);
        var reported = new List<EngineeringException>();

        var match = Assert.Single(ProjectDeviceNameMatcher.FindMatches(project, "Station", reported.Add));

        Assert.Same(readable, match.Device);
        Assert.Same(failure, Assert.Single(reported));
    }

    [Fact]
    public void FindMatches_NonEngineeringNameFailure_Propagates()
    {
        var project = new TiaProject();
        var failure = new InvalidOperationException("Unexpected name getter failure");
        project.Devices.Items.Add(new Device { Name = "Station", NameFailure = failure });

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(
            () => ProjectDeviceNameMatcher.FindMatches(project, "Station")));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void FindMatches_BlankName_DoesNotSatisfySelector(string? requestedName)
    {
        var project = new TiaProject();
        project.Devices.Items.Add(new Device { Name = string.Empty });
        project.Devices.Items.Add(new Device { Name = " " });

        Assert.Empty(ProjectDeviceNameMatcher.FindMatches(project, requestedName));
    }

    [Fact]
    public void FindMatches_DifferentName_DoesNotSatisfySelector()
    {
        var project = new TiaProject();
        project.Devices.Items.Add(new Device { Name = "Another station" });

        Assert.Empty(ProjectDeviceNameMatcher.FindMatches(project, "Station"));
    }

    private static void AddDevice(TiaProject project, int scope, Device device)
    {
        switch (scope)
        {
            case 0:
                project.Devices.Items.Add(device);
                break;
            case 1:
                var group = new DeviceUserGroup { Name = "Group" };
                group.Devices.Items.Add(device);
                project.DeviceGroups.Items.Add(group);
                break;
            case 2:
                project.UngroupedDevicesGroup.Devices.Items.Add(device);
                break;
            case 3:
                var parentGroup = new DeviceUserGroup { Name = "Parent" };
                var childGroup = new DeviceUserGroup { Name = "Child" };
                childGroup.Devices.Items.Add(device);
                parentGroup.Groups.Items.Add(childGroup);
                project.DeviceGroups.Items.Add(parentGroup);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope));
        }
    }
}
