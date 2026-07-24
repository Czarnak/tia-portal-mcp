using System.Security;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

internal static class BlockSourceGenerator
{
    private const string EngineeringVersion = "V21";

    public static string Generate(
        string blockName,
        string blockType,
        string language,
        string? obEventClass)
    {
        BlockSourceValidator.ValidateTypeLanguage(blockType, language);

        var escapedBlockName = SecurityElement.Escape(blockName) ?? string.Empty;
        return blockType switch
        {
            "FB" => GenerateFbXml(escapedBlockName, language),
            "FC" => GenerateFcXml(escapedBlockName, language),
            "OB" => GenerateObXml(escapedBlockName, language, obEventClass),
            "GLOBALDB" or "DB" => GenerateGlobalDbXml(escapedBlockName),
            _ => throw ValidationFailure($"Unsupported block type for XML generation: {blockType}")
        };
    }

    private static string GenerateFbXml(string blockName, string language)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{EngineeringVersion}"" />
  <SW.Blocks.FB ID=""0"">
    <AttributeList>
      <AutoNumber>true</AutoNumber>
      <HeaderAuthor></HeaderAuthor>
      <HeaderFamily></HeaderFamily>
      <HeaderName></HeaderName>
      <HeaderVersion>0.1</HeaderVersion>
      <Interface><Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5""><Section Name=""Input"" /><Section Name=""Output"" /><Section Name=""InOut"" /><Section Name=""Static"" /><Section Name=""Temp"" /><Section Name=""Constant"" /></Sections></Interface>
      <Name>{blockName}</Name>
      <Namespace></Namespace>
      <ProgrammingLanguage>{language}</ProgrammingLanguage>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />{GenerateCompileUnit(language, includeStl: true)}
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.FB>
</Document>";
    }

    private static string GenerateFcXml(string blockName, string language)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{EngineeringVersion}"" />
  <SW.Blocks.FC ID=""0"">
    <AttributeList>
      <AutoNumber>true</AutoNumber>
      <HeaderAuthor></HeaderAuthor>
      <HeaderFamily></HeaderFamily>
      <HeaderName></HeaderName>
      <HeaderVersion>0.1</HeaderVersion>
      <Interface><Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5""><Section Name=""Input"" /><Section Name=""Output"" /><Section Name=""InOut"" /><Section Name=""Temp"" /><Section Name=""Return""><Member Name=""Ret_Val"" Datatype=""Void"" /></Section></Sections></Interface>
      <Name>{blockName}</Name>
      <Namespace></Namespace>
      <ProgrammingLanguage>{language}</ProgrammingLanguage>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />{GenerateCompileUnit(language, includeStl: false)}
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.FC>
</Document>";
    }

    private static string GenerateObXml(string blockName, string language, string? obEventClass)
    {
        var escapedEventClass = SecurityElement.Escape(obEventClass ?? "ProgramCycle") ?? "ProgramCycle";
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{EngineeringVersion}"" />
  <SW.Blocks.OB ID=""0"">
    <AttributeList>
      <AutoNumber>true</AutoNumber>
      <HeaderAuthor></HeaderAuthor>
      <HeaderFamily></HeaderFamily>
      <HeaderName></HeaderName>
      <HeaderVersion>0.1</HeaderVersion>
      <Interface><Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5""><Section Name=""Temp"" /><Section Name=""Constant"" /></Sections></Interface>
      <Name>{blockName}</Name>
      <Namespace></Namespace>
      <ProgrammingLanguage>{language}</ProgrammingLanguage>
      <SecondaryType>{escapedEventClass}</SecondaryType>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />{GenerateCompileUnit(language, includeStl: false)}
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.OB>
</Document>";
    }

    private static string GenerateGlobalDbXml(string blockName)
    {
        return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{EngineeringVersion}"" />
  <SW.Blocks.GlobalDB ID=""0"">
    <AttributeList>
      <AutoNumber>true</AutoNumber>
      <HeaderAuthor></HeaderAuthor>
      <HeaderFamily></HeaderFamily>
      <HeaderName></HeaderName>
      <HeaderVersion>0.1</HeaderVersion>
      <Interface><Sections xmlns=""http://www.siemens.com/automation/Openness/SW/Interface/v5""><Section Name=""Static"" /></Sections></Interface>
      <Name>{blockName}</Name>
      <Namespace></Namespace>
      <Optimized>true</Optimized>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.GlobalDB>
</Document>";
    }

    private static string GenerateCompileUnit(string language, bool includeStl)
    {
        if (language != "SCL" && (!includeStl || language != "STL"))
        {
            return string.Empty;
        }

        return $@"
      <SW.Blocks.CompileUnit ID=""3"" CompositionName=""CompileUnits"">
        <AttributeList>
          <NetworkSource>
            <StructuredText xmlns=""http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v3"" />
          </NetworkSource>
          <ProgrammingLanguage>{language}</ProgrammingLanguage>
        </AttributeList>
        <ObjectList>
          <MultilingualText ID=""4"" CompositionName=""Comment"" />
          <MultilingualText ID=""5"" CompositionName=""Title"" />
        </ObjectList>
      </SW.Blocks.CompileUnit>";
    }

    private static WorkerOperationException ValidationFailure(string message)
    {
        return new WorkerOperationException(WorkerFailureCategories.ValidationError, message);
    }
}
