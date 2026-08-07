using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

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
    [InlineData("--- FILE:  ---\n<Document />")]
    [InlineData("--- FILE: Main.xml --\n<Document />")]
    [InlineData("--- FILE: Main.xml --- trailing\n<Document />")]
    public void Parse_RejectsMalformedDelimiterLines(string content)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("fallback.xml", content));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("prn.xml")]
    [InlineData("AUX.txt")]
    [InlineData("nul.XML")]
    [InlineData("COM1")]
    [InlineData("com9.xml")]
    [InlineData("LPT1")]
    [InlineData("lpt9.xml")]
    public void Parse_RejectsWindowsReservedDeviceNames(string name)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse(name, "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Theory]
    [InlineData("Main.xml.")]
    [InlineData("Main.xml. ")]
    [InlineData("Main.xml ")]
    public void Parse_RejectsDocumentNamesThatWindowsNormalizes(string name)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse(name, "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Theory]
    [InlineData("Main?.xml")]
    [InlineData("Main<.xml")]
    public void Parse_RejectsNamesWithInvalidFileNameCharacters(string name)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse(name, "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Parse_RejectsRootEscapingDocumentName()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("../Main.xml", "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Parse_RejectsSeparatorContainingDocumentName()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("folder/Main.xml", "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Parse_RejectsRootedDocumentName()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("C:\\temp\\Main.xml", "<Document />"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Parse_RejectsTraversalDocumentName(string name)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse(name, "<Document />"));

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
    public void Parse_RejectsUndeclaredPreamble()
    {
        const string content = "<Preamble />\n--- FILE: Main.xml ---\n<Main />";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("fallback.xml", content));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Parse_RejectsEmptyDeclaredDocument()
    {
        const string content = "--- FILE: Main.xml ---\n\n--- FILE: Types.xml ---\n<Types />";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockImportBundleParser.Parse("fallback.xml", content));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void ParsedBlockImportBundle_CopiesSuppliedDocumentList()
    {
        var documents = new List<BlockImportDocument>
        {
            new("Main.xml", "Main.xml", "<Main />")
        };

        var bundle = new ParsedBlockImportBundle("Main.xml", documents);
        documents.Clear();

        Assert.Single(bundle.Documents);
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
