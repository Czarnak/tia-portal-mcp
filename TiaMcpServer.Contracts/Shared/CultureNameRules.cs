using System.Globalization;

namespace TiaMcpServer.Contracts.Shared;

public static class CultureNameRules
{
    public static string? CanonicalName(string? culture)
    {
        if (string.IsNullOrWhiteSpace(culture)) return null;
        try
        {
            var name = CultureInfo.GetCultureInfo(culture).Name;
            return name.Length == 0 ? null : name;
        }
        catch (CultureNotFoundException) { return null; }
    }
}
