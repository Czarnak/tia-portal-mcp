using System.Linq;
using System.Xml.Linq;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockSourceGeneratorTests
{
    [Theory]
    [InlineData("FB", "SCL")]
    [InlineData("FC", "SCL")]
    [InlineData("OB", "SCL")]
    public void Generate_SclBlock_HasNonEmptyCompileUnit(string blockType, string language)
    {
        var xml = BlockSourceGenerator.Generate("Task4Block", blockType, language, "ProgramCycle");

        var document = XDocument.Parse(xml);
        var block = document.Descendants($"SW.Blocks.{blockType}").Single();
        var compileUnit = document.Descendants("SW.Blocks.CompileUnit").Single();
        var source = compileUnit.Element("AttributeList")?.Element("NetworkSource");
        var structuredText = source?.Elements().SingleOrDefault(element =>
            element.Name.LocalName == "StructuredText");

        Assert.Equal("Task4Block", block.Element("AttributeList")?.Element("Name")?.Value);
        Assert.NotNull(source);
        Assert.NotNull(structuredText);
        Assert.False(string.IsNullOrWhiteSpace(structuredText!.Value));
    }

    [Fact]
    public void Generate_FbStlBlock_DoesNotUseNonEmptySclStructuredText()
    {
        var xml = BlockSourceGenerator.Generate("Task4Block", "FB", "STL", null);

        var document = XDocument.Parse(xml);
        var compileUnit = document.Descendants("SW.Blocks.CompileUnit").Single();
        var source = compileUnit.Element("AttributeList")?.Element("NetworkSource");
        var structuredText = source?.Elements().SingleOrDefault(element =>
            element.Name.LocalName == "StructuredText");

        Assert.NotNull(structuredText);
        Assert.True(string.IsNullOrWhiteSpace(structuredText!.Value));
    }
}
