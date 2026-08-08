using System.Linq;
using System.Xml.Linq;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockSourceGeneratorTests
{
    [Theory]
    [InlineData("FB", "SCL")]
    [InlineData("FC", "SCL")]
    [InlineData("OB", "SCL")]
    public void Generate_SclBlock_HasCompileUnitWithEmptyNetworkSource(string blockType, string language)
    {
        var xml = BlockSourceGenerator.Generate("Task4Block", blockType, language, "ProgramCycle");

        var document = XDocument.Parse(xml);
        var block = document.Descendants($"SW.Blocks.{blockType}").Single();
        var compileUnit = document.Descendants("SW.Blocks.CompileUnit").Single();
        var source = compileUnit.Element("AttributeList")?.Element("NetworkSource");

        Assert.Equal("Task4Block", block.Element("AttributeList")?.Element("Name")?.Value);
        Assert.NotNull(source);
        Assert.True(source!.IsEmpty);
    }

    [Fact]
    public void Generate_FbStlBlock_HasEmptyNetworkSource()
    {
        var xml = BlockSourceGenerator.Generate("Task4Block", "FB", "STL", null);

        var document = XDocument.Parse(xml);
        var compileUnit = document.Descendants("SW.Blocks.CompileUnit").Single();
        var source = compileUnit.Element("AttributeList")?.Element("NetworkSource");

        Assert.NotNull(source);
        Assert.True(source!.IsEmpty);
    }

    [Fact]
    public void GlobalDb_source_declares_the_DB_programming_language()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null);

        Assert.Contains("<ProgrammingLanguage>DB</ProgrammingLanguage>", xml);
    }

    [Fact]
    public void GlobalDb_source_uses_MemoryLayout_not_an_Optimized_element()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null);

        Assert.Contains("<MemoryLayout>Optimized</MemoryLayout>", xml);
        Assert.DoesNotContain("<Optimized>", xml);
    }

    [Fact]
    public void GlobalDb_source_omits_header_attributes()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null);

        Assert.DoesNotContain("HeaderAuthor", xml);
        Assert.DoesNotContain("HeaderVersion", xml);
    }

    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    [InlineData("OB")]
    public void Scl_source_contains_a_compile_unit_with_an_empty_network_source(string blockType)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", blockType, "SCL", obEventClass: null);

        Assert.Contains("<SW.Blocks.CompileUnit", xml);
        Assert.Contains("<NetworkSource />", xml);
        Assert.Contains("<ProgrammingLanguage>SCL</ProgrammingLanguage>", xml);
    }

    [Theory]
    [InlineData("SCL")]
    [InlineData("STL")]
    public void Generated_sources_never_emit_a_StructuredText_element(string language)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", language, obEventClass: null);

        Assert.DoesNotContain("StructuredText", xml);
        Assert.DoesNotContain("StructuredText/v3", xml);
    }

    [Fact]
    public void Object_ids_increase_monotonically_in_document_order()
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", "SCL", obEventClass: null);

        var ids = System.Text.RegularExpressions.Regex.Matches(xml, "ID=\"(\\d+)\"");
        var previous = -1;
        foreach (System.Text.RegularExpressions.Match match in ids)
        {
            var current = int.Parse(match.Groups[1].Value);
            Assert.True(current > previous, $"ID {current} follows {previous} in document order.");
            previous = current;
        }
    }
}
