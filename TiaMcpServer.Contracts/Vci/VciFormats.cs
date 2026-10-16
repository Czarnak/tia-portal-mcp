namespace TiaMcpServer.Contracts.Vci;

public sealed class VciFormatChoice
{
    public VciFormatChoice(string? format, bool isFallback)
    {
        Format = format;
        IsFallback = isFallback;
    }

    public string? Format { get; }
    public bool IsFallback { get; }
}

public static class VciFormats
{
    public static VciFormatChoice Resolve(string? requested, IReadOnlyList<string> supported, bool isPlcLeaf)
    {
        var preferred = requested ?? "SimaticML";
        var format = supported.FirstOrDefault(candidate => string.Equals(candidate, preferred, StringComparison.OrdinalIgnoreCase))
            ?? supported.FirstOrDefault(candidate => string.Equals(candidate, "SimaticML", StringComparison.OrdinalIgnoreCase));

        if (format is null && supported.Count == 1
            && !(isPlcLeaf && string.Equals(supported[0], "SimaticSD", StringComparison.OrdinalIgnoreCase)))
            format = supported[0];

        return new VciFormatChoice(format,
            format is not null && !string.Equals(format, preferred, StringComparison.OrdinalIgnoreCase));
    }
}
