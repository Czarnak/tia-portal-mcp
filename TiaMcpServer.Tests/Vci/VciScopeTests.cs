using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Vci;
using TiaMcpServer.Contracts.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Vci;

public class VciScopeTests
{
    [Fact]
    public void NodeTypes_ReusePlcVocabularyAndAddVciKinds()
    {
        var expected = ProjectTreeNodeTypes.All
            .Except(new[] { "SoftwareUnit", "SystemBlockFolder" })
            .Concat(new[] { "TechnologyObjectFolder", "TechnologyObject", "HmiSoftware", "ScriptModule" });
        Assert.Equal(expected.OrderBy(x => x), VciScope.NodeTypes.OrderBy(x => x));
    }

    [Theory]
    [InlineData("Device")]
    [InlineData("Device", "PlcSoftware")]
    [InlineData("Device", "HmiSoftware")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "BlockFolder", "OB")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "FB")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "FC")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "GlobalDB")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "InstanceDB")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "ArrayDB")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "Block")]
    [InlineData("Device", "PlcSoftware", "TagTableFolder", "TagTableFolder", "TagTable")]
    [InlineData("Device", "PlcSoftware", "TypeFolder", "TypeFolder", "Type")]
    [InlineData("Device", "PlcSoftware", "TechnologyObjectFolder", "TechnologyObjectFolder", "TechnologyObject")]
    [InlineData("Device", "HmiSoftware", "ScriptModule")]
    [InlineData("Device", "PlcSoftware", "BlockFolder")]
    [InlineData("Device", "PlcSoftware", "TagTableFolder")]
    [InlineData("Device", "PlcSoftware", "TypeFolder")]
    [InlineData("Device", "PlcSoftware", "TechnologyObjectFolder")]
    public void ValidTransitions_AreAccepted(params string[] types) => VciScope.Validate(Scope(types));

    [Theory]
    [InlineData("PlcSoftware")]
    [InlineData("Device", "FB")]
    [InlineData("Device", "Device")]
    [InlineData("Device", "PlcSoftware", "ScriptModule")]
    [InlineData("Device", "HmiSoftware", "BlockFolder")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "Type")]
    [InlineData("Device", "PlcSoftware", "TypeFolder", "TagTable")]
    [InlineData("Device", "PlcSoftware", "TagTableFolder", "TechnologyObject")]
    [InlineData("Device", "PlcSoftware", "TechnologyObjectFolder", "FB")]
    [InlineData("Device", "PlcSoftware", "BlockFolder", "FB", "BlockFolder")]
    [InlineData("device")]
    [InlineData("Unknown")]
    public void ImpossibleTransitions_AreInvalidSelectors(params string[] types)
        => AssertInvalid(Scope(types));

    [Theory]
    [InlineData("SoftwareUnit")]
    [InlineData("SystemBlockFolder")]
    public void UnitOrSystemFolderScope_IsKindUnsupported(string type)
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() =>
            VciScope.Validate(Scope("Device", "PlcSoftware", type)));
        Assert.Equal(WorkerFailureCategories.TargetKindUnsupported, error.Category);
        Assert.Contains("scope", error.Message);
    }

    [Fact]
    public void OmittedScope_IsAllowedButEmptyScopeIsInvalid()
    {
        VciScope.Validate(null);
        AssertInvalid(Array.Empty<ProjectTreeSelectorSegment>());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankName_IsInvalid(string name)
        => AssertInvalid(new[] { new ProjectTreeSelectorSegment { NodeType = "Device", Name = name } });

    [Fact]
    public void NullSegment_IsInvalid() => AssertInvalid(new ProjectTreeSelectorSegment[] { null! });

    [Theory]
    [InlineData("OB", true)]
    [InlineData("FB", true)]
    [InlineData("FC", true)]
    [InlineData("GlobalDB", true)]
    [InlineData("InstanceDB", true)]
    [InlineData("ArrayDB", true)]
    [InlineData("Block", true)]
    [InlineData("TagTable", true)]
    [InlineData("Type", true)]
    [InlineData("TechnologyObject", true)]
    [InlineData("ScriptModule", true)]
    [InlineData("Device", false)]
    [InlineData("PlcSoftware", false)]
    [InlineData("HmiSoftware", false)]
    [InlineData("BlockFolder", false)]
    [InlineData("TagTableFolder", false)]
    [InlineData("TypeFolder", false)]
    [InlineData("TechnologyObjectFolder", false)]
    [InlineData("SoftwareUnit", false)]
    [InlineData("SystemBlockFolder", false)]
    [InlineData("Unknown", false)]
    public void IsLeaf_RecognizesOnlySupportedLeaves(string type, bool expected)
        => Assert.Equal(expected, VciScope.IsLeaf(type));

    [Theory]
    [InlineData("BlockFolder", "Program blocks")]
    [InlineData("TagTableFolder", "PLC tags")]
    [InlineData("TypeFolder", "PLC data types")]
    [InlineData("TechnologyObjectFolder", "Technology objects")]
    [InlineData("HmiSoftware", "Scripts")]
    public void CategoryFor_UsesMirroredLayoutNames(string type, string expected)
        => Assert.Equal(expected, VciScope.CategoryFor(type));

    private static ProjectTreeSelectorSegment[] Scope(params string[] types)
        => types.Select(type => new ProjectTreeSelectorSegment { NodeType = type, Name = "Fixture" }).ToArray();

    private static void AssertInvalid(IReadOnlyList<ProjectTreeSelectorSegment> scope)
    {
        var error = Assert.Throws<ProjectTreeSelectionException>(() => VciScope.Validate(scope));
        Assert.Equal(WorkerFailureCategories.InvalidSelector, error.Category);
        Assert.Contains("scope", error.Message);
    }
}
