using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Tests.TestSupport;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public sealed class VciMirroredPathTests
{
    // The UI hides a hardware station behind its PLC; a PC station shows station and software.
    [Theory]
    [InlineData(null, false, @"\SoftwareA\Program blocks\GroupA")]
    [InlineData(null, true, @"\FixtureA\SoftwareA\Program blocks\GroupA")]
    [InlineData(@"exports\nested", false, @"\exports\nested\SoftwareA\Program blocks\GroupA")]
    public void Build_PlcFollowsUiProjectTree(string? relative, bool isPcStation, string expected)
    {
        var key = VciMirroredPath.Build(relative, PlcPath(), isPcStation);
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
        }, isPcStation: false);
        Assert.Equal(@"\SoftwareA\" + category, key.Directory);
    }

    // The UI hides an HMI panel's runtime software; a PC station shows station and software.
    [Theory]
    [InlineData(false, @"\FixtureA\Scripts")]
    [InlineData(true, @"\FixtureA\SoftwareA\Scripts")]
    public void Build_HmiFollowsUiProjectTree(bool isPcStation, string expected)
    {
        var key = VciMirroredPath.Build(null, new[]
        {
            Segment("Device", "FixtureA"), Segment("HmiSoftware", "SoftwareA"), Segment("ScriptModule", "LeafA")
        }, isPcStation);
        Assert.Equal(expected, key.Directory);
    }

    [Fact]
    public void TwoPlcsSameBlockName_DistinctKeys()
    {
        var other = PlcPath();
        other[1].Name = "SoftwareB";
        Assert.NotEqual(VciMirroredPath.Build(null, PlcPath(), false), VciMirroredPath.Build(null, other, false));
    }

    [Fact]
    public void SlashInHardwareStationName_IsRepresentable()
    {
        var path = PlcPath();
        path[0].Name = "S7-1500/ET200MP station_1";
        Assert.Null(VciMirroredPath.FirstProblem(@"C:\workspace", VciMirroredPath.Build(null, path, false), "SimaticML"));
    }

    [Theory]
    [InlineData("System:Device.PC", true)]
    [InlineData("OrderNumber:6AG4141-xxxxx-xxxx//Device", true)]
    [InlineData("System:Device.S71500", false)]
    [InlineData("System:Device.ET200SP", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPcStation_RecognizesPcDeviceTypes(string? typeIdentifier, bool expected)
        => Assert.Equal(expected, VciMirroredPath.IsPcStation(typeIdentifier));

    [Theory]
    [InlineData("CON")]
    [InlineData("con.xml")]
    [InlineData("CON.part")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("CLOCK$")]
    [InlineData("LeafA.")]
    [InlineData("LeafA ")]
    [InlineData("bad<name>")]
    [InlineData("bad|name")]
    [InlineData("bad?name")]
    [InlineData("bad*name")]
    [InlineData("a\"b")]
    [InlineData("..")]
    public void DirectorySegmentRules_StillApply(string name)
    {
        var problem = VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey(@"\FixtureA\" + name, "LeafA"), "SimaticML");
        Assert.Equal("Name is not representable: " + name, problem);
        var path = PlcPath();
        path[3].Name = name;
        Assert.NotNull(VciMirroredPath.FirstProblem(@"C:\workspace", VciMirroredPath.Build(null, path, false), "SimaticML"));
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("CON.part")]
    [InlineData("Trailing.")]
    [InlineData("Trailing ")]
    [InlineData("a\"b")]
    [InlineData("a|b")]
    [InlineData("a<b>")]
    public void LeafReservedOrTrailingDotName_IsLeftToPortal(string name)
        => Assert.Null(VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey(@"\FixtureA", name), "SimaticML"));

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("name:stream")]
    public void LeafBoundaryCharacters_AreNotRepresentable(string name)
    {
        var path = PlcPath();
        path[4].Name = name;
        Assert.Equal("Name is not representable: " + name,
            VciMirroredPath.FirstProblem(@"C:\workspace", VciMirroredPath.Build(null, path, false), "SimaticML"));
    }

    [Fact]
    public void UnrepresentableDirectory_IsReported()
        => Assert.NotNull(VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey(@"\CON\GroupA", "LeafA"), "SimaticML"));

    [Theory]
    [InlineData("bad/name")]
    [InlineData(@"bad\name")]
    public void SeparatorInsideEngineeringName_IsNotRepresentable(string name)
    {
        var path = PlcPath();
        path[3].Name = name;
        Assert.NotNull(VciMirroredPath.FirstProblem(@"C:\workspace", VciMirroredPath.Build(null, path, false), "SimaticML"));
    }

    [Theory]
    [InlineData("COM0")]
    [InlineData("COM10")]
    [InlineData("LPT0")]
    [InlineData("Leaf.part")]
    public void RepresentableNames_AreAccepted(string name)
        => Assert.Null(VciMirroredPath.FirstProblem(@"C:\workspace", new VciFileKey("\\", name), "SimaticML"));

    // Complete path = root + "\\" + directory + "\\" + leaf, plus the extension reserve.
    private static string RootForTotal(int total, string dir, string leaf, int reserve)
        => @"C:\" + new string('a', total - reserve - 3 - dir.Length - 1 - leaf.Length - 1);

    [Fact]
    public void SimaticMlCompletePathOf259_IsAccepted()
    {
        var key = new VciFileKey(@"\FixtureA", "LeafA");
        Assert.Null(VciMirroredPath.FirstProblem(RootForTotal(259, "FixtureA", "LeafA", 4), key, "simaticml"));
    }

    [Fact]
    public void SimaticMlCompletePathOf260_IsReported()
    {
        var key = new VciFileKey(@"\FixtureA", "LeafA");
        Assert.Equal("Mirrored path exceeds 259 characters: LeafA",
            VciMirroredPath.FirstProblem(RootForTotal(260, "FixtureA", "LeafA", 4), key, "SimaticML"));
    }

    [Fact]
    public void OtherFormat_KeepsEightCharacterReserve()
    {
        var key = new VciFileKey(@"\FixtureA", "LeafA");
        Assert.Null(VciMirroredPath.FirstProblem(RootForTotal(259, "FixtureA", "LeafA", 8), key, "SimaticSD"));
        Assert.NotNull(VciMirroredPath.FirstProblem(RootForTotal(260, "FixtureA", "LeafA", 8), key, "SimaticSD"));
    }

    [Fact]
    public void LengthCheck_LeafWithPortalOnlyCharactersReachesPortal()
    {
        // net48 Path.Combine/GetFullPath throw on these characters; the leaf must pass through to Portal.
        var key = new VciFileKey(@"\FixtureA", "\"<>|");
        Assert.Null(VciMirroredPath.FirstProblem(@"C:\workspace", key, "SimaticML"));
    }

    [Fact]
    public void FirstProblem_NeverUsesPathCombineOrGetFullPath()
    {
        // net10 does not throw on '"<>|', so the behavioural test cannot catch a regression to those APIs.
        var source = File.ReadAllText(RepositoryFiles.Path("TiaMcpServer.Contracts", "Vci", "VciMirroredPath.cs"));
        var start = source.IndexOf("public static string? FirstProblem", StringComparison.Ordinal);
        var end = source.IndexOf("LeafBoundaryChars =", start, StringComparison.Ordinal);
        var body = string.Join("\n", source.Substring(start, end - start).Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        Assert.DoesNotContain("Path.Combine", body);
        Assert.DoesNotContain("GetFullPath", body);
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
        Assert.Throws<ProjectTreeSelectionException>(() => VciMirroredPath.Build(null, PlcPath().Take(3).ToArray(), false));
        Assert.Throws<ArgumentException>(() => VciMirroredPath.Build(@"..\outside", PlcPath(), false));
    }

    private static ProjectTreeSelectorSegment[] PlcPath() => new[]
    {
        Segment("Device", "FixtureA"), Segment("PlcSoftware", "SoftwareA"),
        Segment("BlockFolder", "RootFolder"), Segment("BlockFolder", "GroupA"), Segment("FB", "LeafA")
    };

    private static ProjectTreeSelectorSegment Segment(string type, string name) => new() { NodeType = type, Name = name };
}
