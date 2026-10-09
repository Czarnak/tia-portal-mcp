using System.Xml.Linq;
using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Block;
using TiaMcpServer.OpennessWorker.Worker;
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
        var xml = BlockSourceGenerator.Generate("Task4Block", blockType, language, blockType == "OB" ? "ProgramCycle" : null, blockType == "OB" ? 1 : null);

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
        var xml = BlockSourceGenerator.Generate("Task4Block", "FB", "STL", null, null);

        var document = XDocument.Parse(xml);
        var compileUnit = document.Descendants("SW.Blocks.CompileUnit").Single();
        var source = compileUnit.Element("AttributeList")?.Element("NetworkSource");

        Assert.NotNull(source);
        Assert.True(source!.IsEmpty);
    }

    [Fact]
    public void GlobalDb_source_declares_the_DB_programming_language()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null, obNumber: null);

        Assert.Contains("<ProgrammingLanguage>DB</ProgrammingLanguage>", xml);
    }

    [Fact]
    public void GlobalDb_source_uses_MemoryLayout_not_an_Optimized_element()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null, obNumber: null);

        Assert.Contains("<MemoryLayout>Optimized</MemoryLayout>", xml);
        Assert.DoesNotContain("<Optimized>", xml);
    }

    [Fact]
    public void GlobalDb_source_omits_header_attributes()
    {
        var xml = BlockSourceGenerator.Generate("MyDb", "GLOBALDB", "DB", obEventClass: null, obNumber: null);

        Assert.DoesNotContain("HeaderAuthor", xml);
        Assert.DoesNotContain("HeaderVersion", xml);
    }

    [Theory]
    [InlineData("FB")]
    [InlineData("FC")]
    [InlineData("OB")]
    public void Scl_source_contains_a_compile_unit_with_an_empty_network_source(string blockType)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", blockType, "SCL", blockType == "OB" ? "ProgramCycle" : null, blockType == "OB" ? 1 : null);

        Assert.Contains("<SW.Blocks.CompileUnit", xml);
        Assert.Contains("<NetworkSource />", xml);
        Assert.Contains("<ProgrammingLanguage>SCL</ProgrammingLanguage>", xml);
    }

    [Theory]
    [InlineData("SCL")]
    [InlineData("STL")]
    public void Generated_sources_never_emit_a_StructuredText_element(string language)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", language, obEventClass: null, obNumber: null);

        Assert.DoesNotContain("StructuredText", xml);
        Assert.DoesNotContain("StructuredText/v3", xml);
    }

    public static IEnumerable<object[]> ObClassesAndLanguages()
    {
        foreach (var cls in ObEventClasses.All)
        {
            foreach (var language in new[] { "LAD", "FBD", "SCL", "STL" })
            {
                yield return new object[] { cls.Name, language };
            }
        }
    }

    [Theory]
    [MemberData(nameof(ObClassesAndLanguages))]
    public void ObDocumentCarriesClassNumberAndFixtureInterface(string obEventClass, string language)
    {
        var xml = BlockSourceGenerator.Generate("MyOb", "OB", language, obEventClass, 200);

        BlockSourceValidator.Validate("OB", language, xml);
        var attributes = XDocument.Parse(xml).Descendants("SW.Blocks.OB").Single().Element("AttributeList")!;
        Assert.Equal(obEventClass, attributes.Element("SecondaryType")?.Value);
        Assert.Equal("200", attributes.Element("Number")?.Value);
        Assert.Equal("Optimized", attributes.Element("MemoryLayout")?.Value);
        Assert.Equal(language, attributes.Element("ProgrammingLanguage")?.Value);
        Assert.Null(attributes.Element("AutoNumber"));
        Assert.DoesNotContain("<Comment", xml);

        var fixture = XElement.Parse(ObFixtureStore.LoadSections(obEventClass));
        string[] InputMembers(XElement sections) => sections.Elements()
            .Single(section => (string?)section.Attribute("Name") == "Input")
            .Elements().Select(member => member.ToString()).ToArray();
        var actual = attributes.Element("Interface")!.Elements().Single();
        Assert.Equal(InputMembers(fixture), InputMembers(actual));
    }

    [Theory]
    [InlineData("FB", null)]
    [InlineData("FC", null)]
    [InlineData("OB", "ProgramCycle")]
    public void StlOmitsSetEnoAutomatically(string blockType, string? obEventClass)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", blockType, "STL", obEventClass, obEventClass is null ? null : 1);

        Assert.DoesNotContain("SetENOAutomatically", xml);
    }

    [Theory]
    [InlineData("FB", null, "LAD")]
    [InlineData("FB", null, "SCL")]
    [InlineData("FC", null, "LAD")]
    [InlineData("FC", null, "SCL")]
    [InlineData("OB", "ProgramCycle", "LAD")]
    [InlineData("OB", "ProgramCycle", "SCL")]
    public void NonStlKeepsSetEnoAutomaticallyFalse(string blockType, string? obEventClass, string language)
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", blockType, language, obEventClass, obEventClass is null ? null : 1);

        Assert.Contains("<SetENOAutomatically>false</SetENOAutomatically>", xml);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("ProgramCycle", null)]
    [InlineData(null, 1)]
    [InlineData("NoSuchClass", 1)]
    [InlineData("programcycle", 1)]
    public void ObWithoutNumberOrUnknownClassIsValidationError(string? obEventClass, int? obNumber)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceGenerator.Generate("MyOb", "OB", "SCL", obEventClass, obNumber));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Object_ids_increase_monotonically_in_document_order()
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", "SCL", obEventClass: null, obNumber: null);

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
