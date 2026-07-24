using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

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
}
