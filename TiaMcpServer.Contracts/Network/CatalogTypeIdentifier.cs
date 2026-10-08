namespace TiaMcpServer.Contracts;

public static class CatalogTypeIdentifier
{
    private static readonly string[] CreatablePrefixes =
    {
        "OrderNumber:",
        "GSD:",
        "System:"
    };

    public static bool IsCreatable(string? typeIdentifier)
    {
        if (typeIdentifier is null)
        {
            return false;
        }

        var value = typeIdentifier.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var prefix in CreatablePrefixes)
        {
            if (value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string BuildValidationMessage(string? typeIdentifier)
    {
        return string.IsNullOrWhiteSpace(typeIdentifier)
            ? "TypeIdentifier is required."
            : $"TypeIdentifier '{typeIdentifier}' is not a creatable TIA Portal catalog identifier. Use a value returned in the typeIdentifier field from search_equipment_catalog, usually starting with OrderNumber:, GSD:, or System:.";
    }
}
