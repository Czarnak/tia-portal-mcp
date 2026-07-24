using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal sealed class BlockUpdatePreflight
{
    public BlockUpdatePreflight(BlockAddress address, ParsedBlockImportBundle bundle)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        Bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
    }

    public BlockAddress Address { get; }

    public ParsedBlockImportBundle Bundle { get; }
}

internal sealed class BlockCreatePreflight
{
    public BlockCreatePreflight(BlockAddress address, string blockType, string language)
    {
        Address = address ?? throw new ArgumentNullException(nameof(address));
        BlockType = blockType ?? throw new ArgumentNullException(nameof(blockType));
        Language = language ?? throw new ArgumentNullException(nameof(language));
    }

    public BlockAddress Address { get; }

    public string BlockType { get; }

    public string Language { get; }
}

internal static class BlockWritePreflight
{
    public static BlockUpdatePreflight PrepareUpdate(
        string blockPath,
        string documentName,
        string rawContent)
    {
        var address = ParseAddress(blockPath);
        var bundle = BlockImportBundleParser.Parse(documentName, rawContent);
        return new BlockUpdatePreflight(address, bundle);
    }

    public static BlockCreatePreflight PrepareCreate(
        string blockPath,
        string blockType,
        string? language)
    {
        if (string.IsNullOrWhiteSpace(blockType))
        {
            throw ValidationFailure("Block type is required.");
        }

        var normalizedType = blockType.ToUpperInvariant();
        var normalizedLanguage = (language ?? "LAD").ToUpperInvariant();
        BlockSourceValidator.ValidateTypeLanguage(normalizedType, normalizedLanguage);

        var address = ParseAddress(blockPath);
        return new BlockCreatePreflight(address, normalizedType, normalizedLanguage);
    }

    private static BlockAddress ParseAddress(string blockPath)
    {
        try
        {
            return BlockAddress.Parse(blockPath);
        }
        catch (ArgumentException exception)
        {
            throw ValidationFailure("Block path is invalid: " + exception.Message);
        }
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
