using System;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>The outcome of asking whether format=source applies to one block.</summary>
internal sealed class SourceFormatDecision
{
    private SourceFormatDecision(
        bool isAllowed,
        string extension,
        SourceObjectKind expectedKind,
        string? refusalMessage)
    {
        IsAllowed = isAllowed;
        Extension = extension;
        ExpectedKind = expectedKind;
        RefusalMessage = refusalMessage;
    }

    public bool IsAllowed { get; }

    /// <summary>File extension for the temp file, including the dot. Empty when refused.</summary>
    public string Extension { get; }

    /// <summary>The declaration kind a submitted source must carry. Meaningless when refused.</summary>
    public SourceObjectKind ExpectedKind { get; }

    public string? RefusalMessage { get; }

    public static SourceFormatDecision Allow(string extension, SourceObjectKind expectedKind)
        => new SourceFormatDecision(true, extension, expectedKind, null);

    public static SourceFormatDecision Refuse(string message)
        => new SourceFormatDecision(false, string.Empty, SourceObjectKind.Type, message);
}

/// <summary>
/// Decides whether Siemens external-source text is defined for a given block, and with which
/// extension.
///
/// <para>
/// Refusals name what the caller actually addressed rather than saying "unsupported", because the
/// caller cannot see the block's language from the path they typed. Graphical languages are
/// refused rather than attempted: nothing in the sample set demonstrates a working text rendering
/// for GRAPH, and a silently degraded rendering that imports back as damaged logic is the worst
/// failure this feature could produce. STL is refused for a different reason — TIA treats STL
/// external sources as a distinct file type (.awl) with no fixture and no live evidence behind it.
/// </para>
/// <para>
/// Siemens-free by construction so the test project can link and cover it: the caller extracts the
/// block's kind and language names and passes them in as strings.
/// </para>
/// </summary>
internal static class SourceFormatEligibility
{
    public const string GlobalDbExtension = ".db";
    public const string SclExtension = ".scl";
    private const string SclLanguage = "SCL";

    public static SourceFormatDecision Decide(string kindName, string languageName, string displayPath)
    {
        if (string.Equals(kindName, "GlobalDB", StringComparison.Ordinal))
        {
            return SourceFormatDecision.Allow(GlobalDbExtension, SourceObjectKind.DataBlock);
        }

        if (string.Equals(languageName, SclLanguage, StringComparison.OrdinalIgnoreCase))
        {
            switch (kindName)
            {
                case "FB": return SourceFormatDecision.Allow(SclExtension, SourceObjectKind.FunctionBlock);
                case "FC": return SourceFormatDecision.Allow(SclExtension, SourceObjectKind.Function);
                case "OB": return SourceFormatDecision.Allow(SclExtension, SourceObjectKind.OrganizationBlock);
            }
        }

        var description = Describe(kindName, languageName);

        return SourceFormatDecision.Refuse(
            $"'{displayPath}' is {description}. format={SourceFormatNames.Source} is available for "
            + $"global data blocks and SCL-language FB/FC/OB only; use format={SourceFormatNames.Xml} "
            + $"for {description}.");
    }

    public static string Describe(string kindName, string languageName)
    {
        switch (kindName)
        {
            case "InstanceDB": return "an instance data block (InstanceDB)";
            case "ArrayDB": return "an array data block (ArrayDB)";
            case "OB": return $"a {languageName} organization block (OB)";
            case "FB": return $"a {languageName} function block (FB)";
            case "FC": return $"a {languageName} function (FC)";
            default: return $"a {kindName} block ({languageName})";
        }
    }
}