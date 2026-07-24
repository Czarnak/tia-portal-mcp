using System;
using System.Linq;
using System.Xml.Linq;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockSourceValidator
{
    public static void Validate(string blockType, string language, string xml)
    {
        ValidateTypeLanguage(blockType, language);

        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or ArgumentException)
        {
            throw ValidationFailure("Generated block source is not well-formed XML.");
        }

        var expectedBlockElement = blockType is "GLOBALDB" or "DB"
            ? "SW.Blocks.GlobalDB"
            : $"SW.Blocks.{blockType}";
        if (!document.Descendants(expectedBlockElement).Any())
        {
            throw ValidationFailure($"Generated source does not contain a {expectedBlockElement} block.");
        }

        if (language == "SCL"
            && !document.Descendants("SW.Blocks.CompileUnit")
                .Any(compileUnit => compileUnit.Descendants().Any()))
        {
            throw ValidationFailure($"Generated {blockType} SCL source must contain a non-empty compile unit.");
        }
    }

    internal static void ValidateTypeLanguage(string blockType, string language)
    {
        if (blockType is "GLOBALDB" or "DB")
        {
            if (language == "SCL")
            {
                throw ValidationFailure($"Block type '{blockType}' does not support language '{language}'.");
            }

            return;
        }

        if (blockType is not ("FB" or "FC" or "OB"))
        {
            throw ValidationFailure($"Unsupported block type '{blockType}'.");
        }

        if (language is not ("LAD" or "FBD" or "STL" or "SCL" or "GRAPH"))
        {
            throw ValidationFailure($"Unsupported programming language '{language}'.");
        }
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
