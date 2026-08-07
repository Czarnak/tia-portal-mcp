using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockSourceValidatorTests
{
    [Fact]
    public void Validate_RejectsSclWithoutCompileUnit()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.Validate("FB", "SCL", "<Document><SW.Blocks.FB /></Document>"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Validate_RejectsSclCompileUnitWithoutNetworkSource()
    {
        const string malformedXml =
            "<Document><SW.Blocks.FB><ObjectList><SW.Blocks.CompileUnit>" +
            "<UnrelatedContent /></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FB></Document>";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.Validate("FB", "SCL", malformedXml));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Validate_RejectsSclCompileUnitWithNestedStructuredText()
    {
        const string malformedXml =
            "<Document><SW.Blocks.FB><ObjectList><SW.Blocks.CompileUnit><AttributeList>" +
            "<NetworkSource><Wrapper><StructuredText>BEGIN END_FUNCTION</StructuredText></Wrapper>" +
            "</NetworkSource></AttributeList></SW.Blocks.CompileUnit></ObjectList></SW.Blocks.FB></Document>";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.Validate("FB", "SCL", malformedXml));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Fact]
    public void Validate_RejectsMismatchedBlockType()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.Validate("FB", "LAD", "<Document><SW.Blocks.FC /></Document>"));

        Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
    }

    [Theory]
    [InlineData("FB", "UNKNOWN")]
    [InlineData("DB", "SCL")]
    public void Generate_RejectsUnsupportedTypeLanguagePair(string blockType, string language)
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceGenerator.Generate("Task4Block", blockType, language, null));

        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            exception.FailureCategory);
    }

    [Fact]
    public void ValidateTypeLanguage_RejectsUnknownDatabaseLanguage()
    {
        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.ValidateTypeLanguage("DB", "UNKNOWN"));

        Assert.Equal(
            WorkerFailureCategories.ValidationError,
            exception.FailureCategory);
    }

    [Fact]
    public void Validation_accepts_an_scl_compile_unit_with_an_empty_network_source()
    {
        var xml = BlockSourceGenerator.Generate("MyBlock", "FB", "SCL", obEventClass: null);

        BlockSourceValidator.Validate("FB", "SCL", xml);
    }

    [Fact]
    public void Validation_rejects_a_raw_text_node_inside_StructuredText()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <SW.Blocks.FB ID=""0"">
    <ObjectList>
      <SW.Blocks.CompileUnit ID=""1"">
        <AttributeList>
          <NetworkSource>
            <StructuredText xmlns=""http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4"">// raw</StructuredText>
          </NetworkSource>
          <ProgrammingLanguage>SCL</ProgrammingLanguage>
        </AttributeList>
      </SW.Blocks.CompileUnit>
    </ObjectList>
  </SW.Blocks.FB>
</Document>";

        var exception = Assert.Throws<WorkerOperationException>(
            () => BlockSourceValidator.Validate("FB", "SCL", xml));

        Assert.Contains("StructuredText", exception.Message);
    }

    [Fact]
    public void Validation_rejects_scl_source_with_no_compile_unit()
    {
        const string xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <SW.Blocks.FB ID=""0""><ObjectList /></SW.Blocks.FB>
</Document>";

        Assert.Throws<WorkerOperationException>(() => BlockSourceValidator.Validate("FB", "SCL", xml));
    }
}
