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

        if (language == "SCL" && !HasSclSourceBody(document))
        {
            throw ValidationFailure($"Generated {blockType} SCL source must contain a non-empty compile unit.");
        }
    }

    internal static void ValidateTypeLanguage(string blockType, string language)
    {
        if (blockType is "GLOBALDB" or "DB")
        {
            if (language != "DB")
            {
                throw ValidationFailure(
                    $"Block type '{blockType}' uses language 'DB'; '{language}' is not supported. "
                    + "Omit the language parameter for data blocks.");
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

    private static bool HasSclSourceBody(XDocument document)
    {
        return document.Descendants("SW.Blocks.CompileUnit")
            .Select(compileUnit => compileUnit.Element("AttributeList"))
            .Where(attributeList => attributeList is not null)
            .Select(attributeList => attributeList!.Element("NetworkSource"))
            .Select(networkSource => networkSource?
                .Elements()
                .SingleOrDefault(element => element.Name.LocalName == "StructuredText"))
            .Any(structuredText =>
                !string.IsNullOrWhiteSpace(structuredText?.Value));
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
