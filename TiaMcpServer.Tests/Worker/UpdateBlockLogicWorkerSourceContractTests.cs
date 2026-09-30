using Xunit;

namespace TiaMcpServer.Tests.Worker;

public sealed class UpdateBlockLogicWorkerSourceContractTests
{
    private static string ReadWorkerProgram()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TiaMcpServer.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return File.ReadAllText(Path.Combine(directory!.FullName, "TiaMcpServer.OpennessWorker", "Program.cs"));
    }

    [Fact]
    public void AuthorizationDenial_IsDecoratedOnlyForUpdateBeforeDispatch()
    {
        var source = ReadWorkerProgram();
        var authorization = source.IndexOf("WorkerOperationAuthorization.Authorize", StringComparison.Ordinal);
        var decoration = source.IndexOf("BlockUpdateOutcomeDecorator.Decorate", authorization, StringComparison.Ordinal);
        var dispatch = source.IndexOf("return request.Method switch", authorization, StringComparison.Ordinal);

        Assert.True(authorization >= 0);
        Assert.True(decoration > authorization && decoration < dispatch);
        Assert.Contains("request.Method == \"update_block_logic\"", source[authorization..dispatch], StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateBlockLogic_WrapsValidationNormalizationAndWholeProjectCall()
    {
        var source = ReadWorkerProgram();
        var start = source.IndexOf("private static WorkerResponse UpdateBlockLogic", StringComparison.Ordinal);
        var end = source.IndexOf("private static WorkerResponse GetTypeContent", start, StringComparison.Ordinal);
        var method = source[start..end];

        var tryIndex = method.IndexOf("try", StringComparison.Ordinal);
        Assert.True(tryIndex >= 0);
        Assert.True(method.IndexOf("BlockPath is required", StringComparison.Ordinal) > tryIndex);
        Assert.True(method.IndexOf("NormalizeBlockFormat", StringComparison.Ordinal) > tryIndex);
        Assert.True(method.IndexOf("WithProject", StringComparison.Ordinal) > tryIndex);
        Assert.Contains("catch (WorkerOperationException", method, StringComparison.Ordinal);
        Assert.Contains("catch (Exception", method, StringComparison.Ordinal);
        Assert.Contains("BlockUpdateOutcomeDecorator.Decorate", method, StringComparison.Ordinal);
        Assert.Contains("result.Outcome", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ImporterEntered_IsSetImmediatelyBeforeImporterCall()
    {
        var source = ReadWorkerProgram();
        var start = source.IndexOf("private static WorkerResponse UpdateBlockLogic", StringComparison.Ordinal);
        var end = source.IndexOf("private static WorkerResponse GetTypeContent", start, StringComparison.Ordinal);
        var method = source[start..end];
        var entered = method.IndexOf("importerEntered = true;", StringComparison.Ordinal);
        var import = method.IndexOf("BlockImporter.Import", StringComparison.Ordinal);

        Assert.True(entered >= 0 && import > entered);
        Assert.DoesNotContain(';', method[(entered + "importerEntered = true;".Length)..import]);
    }

    [Fact]
    public void OuterWorkerExceptionCatch_CopiesTypedOutcomeToResponse()
    {
        var source = ReadWorkerProgram();
        var catchIndex = source.IndexOf("catch (WorkerOperationException ex)", StringComparison.Ordinal);
        var nextCatch = source.IndexOf("catch (Exception ex)", catchIndex, StringComparison.Ordinal);
        Assert.Contains("ex.BlockImportOutcome", source[catchIndex..nextCatch], StringComparison.Ordinal);
    }
}
