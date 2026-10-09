namespace TiaMcpServer.Contracts.CrossReferences;

public static class CrossReferenceFilterNames
{
    public const string AllObjects = "AllObjects";
    public const string ObjectsWithReferences = "ObjectsWithReferences";
    public const string ObjectsWithoutReferences = "ObjectsWithoutReferences";
    public const string UnusedObjects = "UnusedObjects";

    public static readonly IReadOnlyList<string> Allowed = new[]
    {
        AllObjects,
        ObjectsWithReferences,
        ObjectsWithoutReferences,
        UnusedObjects
    };

    public static string Default => ObjectsWithReferences;

    public static bool TryNormalize(string? value, out string normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = Default;
            error = null;
            return true;
        }

        foreach (var allowed in Allowed)
        {
            if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = allowed;
                error = null;
                return true;
            }
        }

        normalized = string.Empty;
        error = $"Invalid cross-reference filter '{value}'. Allowed values: {string.Join(", ", Allowed)}.";
        return false;
    }
}
