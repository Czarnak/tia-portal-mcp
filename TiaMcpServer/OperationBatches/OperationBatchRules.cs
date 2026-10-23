namespace TiaMcpServer.OperationBatches;

/// <summary>
/// The count and per-item envelope rules shared by every batch catalog and the write entry guard,
/// with one wording. Domain rules (operation names, fields, access mode) stay in each catalog.
/// </summary>
internal static class OperationBatchRules
{
    public const int MaxItems = 50;
    public const int MaxOperationIdChars = 256;

    /// <summary>Returns the count error, or null for 1 to <see cref="MaxItems"/> items.</summary>
    public static string? CheckCount(int count) => count switch
    {
        < 1 => "Batch must contain at least one operation.",
        > MaxItems => $"Batch exceeds the maximum of {MaxItems} operations (received {count}).",
        _ => null,
    };

    /// <summary>
    /// Returns the first failing envelope message in the order null, blank, length, duplicate, or
    /// null. The ID is added to <paramref name="seenIds"/> only after the length check passes.
    /// </summary>
    public static string? CheckItem(IOperationBatchItem? item, ISet<string> seenIds)
    {
        if (item is null)
        {
            return "Batch contains a null operation.";
        }

        if (string.IsNullOrWhiteSpace(item.OperationId))
        {
            return "Each operation requires a unique operationId.";
        }

        // operationId is repeated in every public item and in truncation evidence. Bound it
        // before any response construction so caller-controlled identity text cannot consume
        // the entire per-item budget or make even the minimal omission impossible to render.
        if (item.OperationId.Length > MaxOperationIdChars)
        {
            return $"Each operationId must be at most {MaxOperationIdChars} characters.";
        }

        return seenIds.Add(item.OperationId) ? null : $"Duplicate operationId '{item.OperationId}'.";
    }
}
