using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockImportBundleParserTests
{
    [Fact]
    public void Parse_RejectsMissingDocumentName()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse(" ", "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Parse_RejectsDuplicateDocumentNamesCaseInsensitively()
    {
        const string content = "--- FILE: Main.xml ---\n<Main />\n--- FILE: main.XML ---\n<Duplicate />";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("fallback.xml", content));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Theory]
    [InlineData("../Main.xml")]
    [InlineData("..\\Main.xml")]
    [InlineData("C:\\temp\\Main.xml")]
    [InlineData("/tmp/Main.xml")]
    [InlineData("folder/Main.xml")]
    [InlineData("folder\\Main.xml")]
    public void Parse_RejectsUnsafeDocumentName(string name)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse(name, "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Parse_SingleXml_ProducesOnePrimaryDocument()
    {
        var bundle = BlockImportBundleParser.Parse("Main.xml", "<Document />");

        var document = Assert.Single(bundle.Documents);
        Assert.Equal("Main.xml", bundle.PrimaryDocumentName);
        Assert.Equal("Main.xml", document.LogicalName);
        Assert.Equal("Main.xml", document.SafeFileName);
        Assert.Equal("<Document />", document.Content);
    }

    [Fact]
    public void Parse_MultiDocumentBundle_PreservesDeclarationOrder()
    {
        const string content = "--- FILE: Main.xml ---\n<Main />\n--- FILE: Types.xml ---\n<Types />";

        var bundle = BlockImportBundleParser.Parse("fallback.xml", content);

        Assert.Equal("Main.xml", bundle.PrimaryDocumentName);
        Assert.Collection(
            bundle.Documents,
            document =>
            {
                Assert.Equal("Main.xml", document.LogicalName);
                Assert.Equal("Main.xml", document.SafeFileName);
                Assert.Equal("<Main />\n", document.Content);
            },
            document =>
            {
                Assert.Equal("Types.xml", document.LogicalName);
                Assert.Equal("Types.xml", document.SafeFileName);
                Assert.Equal("<Types />", document.Content);
            });
    }
}
