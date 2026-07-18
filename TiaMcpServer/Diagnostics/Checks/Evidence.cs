namespace TiaMcpServer.Diagnostics.Checks;

internal static class Evidence
{
    public static Dictionary<string, string?> Empty() => new();

    public static Dictionary<string, string?> With(string key, string? value)
    {
        var dict = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(key))
        {
            dict[key] = value;
        }
        return dict;
    }
}
