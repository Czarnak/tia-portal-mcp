using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciMirroredPathTests
{
    [Theory]
    [InlineData(null, @"\FixtureA\SoftwareA\Program blocks\GroupA")]
    [InlineData(@"exports\nested", @"\exports\nested\FixtureA\SoftwareA\Program blocks\GroupA")]
    public void Build_ReplacesRootFolderAndRetainsUserGroups(string? relative, string expected)
    {
        var key = VciMirroredPath.Build(relative, PlcPath());
        Assert.Equal(expected, key.Directory);
        Assert.Equal("LeafA", key.FileName);
    }

    [Theory]
    [InlineData("TagTableFolder", "TagTable", "PLC tags")]
    [InlineData("TypeFolder", "Type", "PLC data types")]
    [InlineData("TechnologyObjectFolder", "TechnologyObject", "Technology objects")]
    public void Build_UsesCategoryForEachPlcKind(string folder, string leaf, string category)
    {
        var key = VciMirroredPath.Build(null, new[]
        {
            Segment("Device", "FixtureA"), Segment("PlcSoftware", "SoftwareA"),
            Segment(folder, "RootFolder"), Segment(leaf, "LeafA")
        });
        Assert.Equal(@"\FixtureA\SoftwareA\" + category, key.Directory);
    }

    [Fact]
    public void Build_HmiRetainsBothDeviceAndSoftwareLevels()
    {
        var key = VciMirroredPath.Build(null, new[]
        {
            Segment("Device", "FixtureA"), Segment("HmiSoftware", "SoftwareA"), Segment("ScriptModule", "LeafA")
        });
        Assert.Equal(@"\FixtureA\SoftwareA\Scripts", key.Directory);
    }

    [Fact]
    public void TwoPlcsSameBlockName_DistinctKeys()
    {
        var other = PlcPath();
        other[0].Name = "FixtureB";
        Assert.NotEqual(VciMirroredPath.Build(null, PlcPath()), VciMirroredPath.Build(null, other));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con.xml")]
    [InlineData("CON.part.xml")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("CLOCK$")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("LeafA.")]
    [InlineData("LeafA ")]
    [InlineData("bad<name>")]
    [InlineData("bad|name")]
    [InlineData("bad?name")]
    [InlineData("bad*name")]
    [InlineData("sub\\name")]
    [InlineData("sub/name")]
    [InlineData("name:stream")]
    [InlineData("")]
    [InlineData("..")]
    public void ReservedAndTrailingDotNames_AreNotRepresentable(string name)
        => Assert.NotNull(VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey(@"\FixtureA", name)));

    [Fact]
    public void UnrepresentableDirectory_IsReported()
        => Assert.NotNull(VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey(@"\CON\GroupA", "LeafA")));

    [Theory]
    [InlineData("bad/name")]
    [InlineData(@"bad\name")]
    public void SeparatorInsideEngineeringName_IsNotRepresentable(string name)
    {
        var path = PlcPath();
        path[3].Name = name;
        Assert.NotNull(VciMirroredPath.FirstProblem(@"C:\workspace", VciMirroredPath.Build(null, path)));
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    [InlineData("Leaf.part")]
    public void RepresentableNames_AreAccepted(string name)
        => Assert.Null(VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey("\\", name)));

    [Fact]
    public void LongPath_Over259_IsReported()
    {
        var key = new VciFileKey("\\", "LeafA");
        var acceptedRoot = @"C:\" + new string('a', 259 - 3 - 1 - key.FileName.Length - VciMirroredPath.MaxExtensionLength);
        Assert.Null(VciMirroredPath.FirstProblem(acceptedRoot, key));
        Assert.NotNull(VciMirroredPath.FirstProblem(acceptedRoot + "a", key));
    }

    [Theory]
    [InlineData(@"C:\workspace", @"C:\workspace\FixtureA\GroupA", @"\FixtureA\GroupA")]
    [InlineData(@"C:\workspace\", @"c:\WORKSPACE\FixtureA\GroupA\", @"\FixtureA\GroupA")]
    [InlineData(@"C:\workspace", @"c:\WORKSPACE\", "\\")]
    public void FromMapping_StripsAbsoluteRoot(string root, string directory, string expected)
    {
        var key = VciFileKey.FromMapping(root, directory, "LeafA");
        Assert.Equal(expected, key.Directory);
        Assert.Equal("LeafA", key.FileName);
        var same = new VciFileKey(expected.ToUpperInvariant(), "leafa");
        Assert.Equal(same, key);
        Assert.Equal(same.GetHashCode(), key.GetHashCode());
        Assert.Single(new HashSet<VciFileKey> { key, same });
    }

    [Theory]
    [InlineData(@"C:\workspace-other")]
    [InlineData(@"D:\workspace\FixtureA")]
    [InlineData(@"C:\workspace\..\outside")]
    public void FromMapping_RejectsDirectoryOutsideWorkspace(string directory)
        => Assert.Throws<ArgumentException>(() => VciFileKey.FromMapping(@"C:\workspace", directory, "LeafA"));

    [Fact]
    public void Build_RequiresLeafAndRelativeSyntax()
    {
        Assert.Throws<ProjectTreeSelectionException>(() => VciMirroredPath.Build(null, PlcPath().Take(3).ToArray()));
        Assert.Throws<ArgumentException>(() => VciMirroredPath.Build(@"..\outside", PlcPath()));
    }

    private static ProjectTreeSelectorSegment[] PlcPath() => new[]
    {
        Segment("Device", "FixtureA"), Segment("PlcSoftware", "SoftwareA"),
        Segment("BlockFolder", "RootFolder"), Segment("BlockFolder", "GroupA"), Segment("FB", "LeafA")
    };

    private static ProjectTreeSelectorSegment Segment(string type, string name) => new() { NodeType = type, Name = name };
}
