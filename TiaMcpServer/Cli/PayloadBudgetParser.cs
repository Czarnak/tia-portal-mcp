using System.Globalization;
using TiaMcpServer.OperationBatches;

namespace TiaMcpServer.Cli;

/// <summary>
/// Resolves the structured response character budgets from CLI arguments and environment variables.
/// Precedence per value: CLI argument > environment variable > default. Limits can only be raised:
/// the defaults are the floor, and the item limit may not exceed the document limit.
/// </summary>
public static class PayloadBudgetParser
{
    public const string ItemFlag = "--max-item-chars";
    public const string DocumentFlag = "--max-document-chars";
    public const string ItemEnvVar = "TIA_MCP_MAX_ITEM_CHARS";
    public const string DocumentEnvVar = "TIA_MCP_MAX_DOCUMENT_CHARS";

    /// <summary>Ceiling that keeps a mistyped value from producing multi-megabyte tool results.</summary>
    public const int MaxConfigurableChars = 1_000_000;

    public static PayloadBudgetParseResult Parse(string[] args, Func<string, string?>? getEnvironmentVariable = null)
    {
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

        var item = Resolve(args, ItemFlag, ItemEnvVar, StructuredOperationBatchPayloadBudget.DefaultItemChars, getEnvironmentVariable);
        if (item.Error is not null)
            return PayloadBudgetParseResult.Fail(item.Error);

        var document = Resolve(args, DocumentFlag, DocumentEnvVar, StructuredOperationBatchPayloadBudget.DefaultDocumentChars, getEnvironmentVariable);
        if (document.Error is not null)
            return PayloadBudgetParseResult.Fail(document.Error);

        if (item.Value > document.Value)
        {
            return PayloadBudgetParseResult.Fail(
                $"The item limit ({item.Value}) exceeds the document limit ({document.Value}). "
                + $"Raise {DocumentFlag} (or {DocumentEnvVar}) to at least the item limit.");
        }

        return PayloadBudgetParseResult.Ok(item.Value, document.Value);
    }

    private static (int Value, string? Error) Resolve(
        string[] args, string flag, string envVar, int defaultValue, Func<string, string?> getEnvironmentVariable)
    {
        string? cliValue = null;
        var prefix = flag + "=";
        for (var i = 0; i < args.Length; i++)
        {
            string? candidate;
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return (0, $"{flag} requires a value.");
                candidate = args[++i];
            }
            else if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                candidate = args[i].Substring(prefix.Length);
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate))
                return (0, $"{flag} requires a value.");
            if (cliValue is not null)
                return (0, $"{flag} may be specified only once.");
            cliValue = candidate;
        }

        if (cliValue is not null)
            return Validate(cliValue, flag, defaultValue);

        var envValue = getEnvironmentVariable(envVar);
        return string.IsNullOrWhiteSpace(envValue) ? (defaultValue, null) : Validate(envValue, envVar, defaultValue);
    }

    private static (int Value, string? Error) Validate(string text, string source, int minimum)
    {
        if (int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            && value >= minimum && value <= MaxConfigurableChars)
        {
            return (value, null);
        }

        return (0, $"Invalid {source} value '{text}'. Expected a whole number of characters from {minimum} to {MaxConfigurableChars}.");
    }
}

public sealed record PayloadBudgetParseResult(bool IsValid, int ItemChars, int DocumentChars, string? Error)
{
    public static PayloadBudgetParseResult Ok(int itemChars, int documentChars) => new(true, itemChars, documentChars, null);

    public static PayloadBudgetParseResult Fail(string error) => new(false, 0, 0, error);
}
