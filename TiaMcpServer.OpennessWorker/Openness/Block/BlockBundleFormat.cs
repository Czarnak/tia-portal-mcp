using TiaMcpServer.Contracts.Block;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Block;

/// <summary>
/// Worker entry point to the block bundle format owned by <see cref="BlockBundleText"/>. It only
/// translates a format refusal into a categorized <c>validation_error</c> with the same message,
/// so every worker call site keeps its failure category.
/// </summary>
internal static class BlockBundleFormat
{
    public static bool ContainsDelimiterLine(string content) => BlockBundleText.ContainsDelimiterLine(content);

    /// <inheritdoc cref="BlockBundleText.Compose"/>
    public static string Compose(IReadOnlyList<BlockImportDocument> documents)
    {
        try
        {
            return BlockBundleText.Compose(documents);
        }
        catch (BlockBundleFormatException exception)
        {
            throw ToValidationFailure(exception);
        }
    }

    internal static WorkerOperationException ToValidationFailure(BlockBundleFormatException exception)
        => new(WorkerFailureCategories.ValidationError, exception.Message);
}
