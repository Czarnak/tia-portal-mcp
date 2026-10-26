using Siemens.Engineering;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using TiaMcpServer.Contracts.Shared;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Worker;

namespace TiaMcpServer.OpennessWorker.Openness.Plc;

/// <summary>
/// Fail-closed checks the worker applies at mutation time for evidence TIA Portal does not
/// check itself: the optimistic-concurrency content hash and the consequence evidence of a
/// delete. Name collisions and occupied targets are not checked here: the Openness call is the
/// first and only collision check, and its rejection is reported as <c>engineering_rejected</c>.
/// </summary>
internal static class PlcWritePreconditions
{
    /// <summary>
    /// Every descendant block and group of a group about to be deleted must be enumerable and named,
    /// otherwise the consequence of the delete cannot be stated.
    /// </summary>
    internal static void RequireReadableDescendants(PlcBlockGroup group)
    {
        try
        {
            foreach (PlcBlock block in group.Blocks)
            {
                _ = block.Name;
            }

            foreach (PlcBlockGroup child in group.Groups)
            {
                _ = child.Name;
                RequireReadableDescendants(child);
            }
        }
        catch (EngineeringException ex)
        {
            throw Unreadable($"The contents of block group '{SafeName(group)}' could not be read, so deleting it is not safe: {ex.Message}");
        }
    }

    private static string SafeName(PlcBlockGroup group)
    {
        try
        {
            return group.Name;
        }
        catch (EngineeringException)
        {
            return "<unreadable>";
        }
    }

    /// <summary>
    /// The hash a content write was planned against must match the fresh export in the write's own
    /// format. A missing, malformed or differently formatted hash is a <c>validation_error</c>; a
    /// different hash means the document changed after planning, so it is <c>state_changed</c>.
    /// </summary>
    internal static void RequireContentHash(string? expected, string format, string freshExport)
    {
        if (!ContentHashRules.TryParse(expected, out var expectedFormat, out _))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                "ExpectedContentHash is required and must be '<format>:sha256:<64 hex>'.");
        }

        if (!string.Equals(expectedFormat, format, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.ValidationError,
                $"ExpectedContentHash is a '{expectedFormat}' hash but the write format is '{format}'.");
        }

        if (!string.Equals(ContentHashRules.Compute(format, freshExport), expected, StringComparison.Ordinal))
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.StateChanged,
                "The content changed after the write was planned; the expected content hash no longer matches the project. Nothing was imported.");
        }
    }

    /// <summary>
    /// An updated user constant must report its old value: the planned effect shows current to
    /// requested, and verification reads the value back.
    /// </summary>
    internal static void RequireReadableValue(PlcUserConstant constant)
    {
        object? value;
        try
        {
            value = constant.Value;
        }
        catch (EngineeringException ex)
        {
            throw Unreadable($"The value of user constant '{constant.Name}' could not be read: {ex.Message}");
        }

        if (value is null)
        {
            throw Unreadable($"The value of user constant '{constant.Name}' is unavailable.");
        }
    }

    private static WorkerOperationException Unreadable(string message)
        => new(WorkerFailureCategories.WorkerOperationFailed, message);
}
