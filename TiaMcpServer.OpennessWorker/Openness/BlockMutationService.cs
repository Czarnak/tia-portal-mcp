using System;
using System.Collections.Generic;
using System.IO;
using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class BlockMutationService
{
    public static BlockMutationResultInfo CreateBlock(
        Project project,
        string blockPath,
        string blockType,
        string? language,
        string? obEventClass)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var group = ResolveGroupFromAddress(plcSoftware, address);

        var blockName = address.BlockName;
        var normalizedType = blockType.ToUpperInvariant();
        var normalizedLang = (language ?? "LAD").ToUpperInvariant();

        if (normalizedType == "FB")
        {
            var lang = ParseLanguage(normalizedLang);
            group.Blocks.CreateFB(blockName, false, 0, lang);
        }
        else if (normalizedType is "FC" or "OB" or "GLOBALDB" or "DB")
        {
            ImportBlockFromXml(group, blockName, normalizedType, normalizedLang, obEventClass);
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown block type '{blockType}'. Valid types: FB, FC, OB, GlobalDB.");
        }

        return new BlockMutationResultInfo
        {
            Operation = "create_block",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath,
            BlockType = normalizedType,
            Language = (normalizedType is "GLOBALDB" or "DB") ? null : normalizedLang
        };
    }

    public static BlockMutationResultInfo DeleteBlock(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var target = BlockTargetResolver.ResolveForExport(project, address);

        if (target.Block is null)
        {
            throw new InvalidOperationException(
                $"Block '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");
        }

        target.Block.Delete();

        return new BlockMutationResultInfo
        {
            Operation = "delete_block",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    public static BlockMutationResultInfo CreateBlockGroup(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);
        var parentGroup = ResolveGroupFromAddress(plcSoftware, address);

        parentGroup.Groups.Create(address.BlockName);

        return new BlockMutationResultInfo
        {
            Operation = "create_block_group",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    public static BlockMutationResultInfo DeleteBlockGroup(Project project, string blockPath)
    {
        var address = BlockAddress.Parse(blockPath);
        var plcSoftware = PlcSoftwareLocator.Find(project, address.PlcName);

        // The group to delete is FolderPath + BlockName
        var allSegments = new List<string>(address.FolderPath) { address.BlockName };
        var group = FindUserGroupByPath(plcSoftware.BlockGroup, allSegments)
            ?? throw new InvalidOperationException(
                $"Block group '{address.BlockName}' was not found at '{address.ToDisplayPath()}'.");

        group.Delete();

        return new BlockMutationResultInfo
        {
            Operation = "delete_block_group",
            ProjectPath = project.Path.FullName,
            PlcName = address.PlcName ?? plcSoftware.Name,
            BlockPath = blockPath
        };
    }

    // Resolves the parent group that a new block/group would be created inside.
    private static PlcBlockGroup ResolveGroupFromAddress(PlcSoftware plcSoftware, BlockAddress address)
    {
        if (!address.IsDeterministic)
        {
            return plcSoftware.BlockGroup;
        }

        return FindGroupByPath(plcSoftware.BlockGroup, address.FolderPath)
            ?? throw new InvalidOperationException(
                $"Block group path '{string.Join("/", address.FolderPath)}' was not found.");
    }

    private static PlcBlockGroup? FindGroupByPath(PlcBlockGroup root, IEnumerable<string> path)
    {
        PlcBlockGroup current = root;
        foreach (var segment in path)
        {
            PlcBlockGroup? next = null;
            foreach (PlcBlockGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            current = next;
        }

        return current;
    }

    // Same traversal as FindGroupByPath but typed as PlcBlockUserGroup so Delete() is available.
    private static PlcBlockUserGroup? FindUserGroupByPath(PlcBlockGroup root, IEnumerable<string> path)
    {
        PlcBlockGroup current = root;
        PlcBlockUserGroup? result = null;
        foreach (var segment in path)
        {
            PlcBlockUserGroup? next = null;
            foreach (PlcBlockUserGroup child in current.Groups)
            {
                if (string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase))
                {
                    next = child;
                    break;
                }
            }

            if (next is null)
            {
                return null;
            }

            result = next;
            current = next;
        }

        return result;
    }

    private static void ImportBlockFromXml(
        PlcBlockGroup group,
        string blockName,
        string blockType,
        string language,
        string? obEventClass)
    {
        var xml = GenerateBlockXml(blockName, blockType, language, obEventClass);
        var tempFile = Path.Combine(
            Path.GetTempPath(),
            $"tia-mcp-create-{Guid.NewGuid():N}.xml");

        try
        {
            File.WriteAllText(tempFile, xml, System.Text.Encoding.UTF8);
            group.Blocks.Import(new FileInfo(tempFile), ImportOptions.Override);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private static string GenerateBlockXml(
        string blockName,
        string blockType,
        string language,
        string? obEventClass)
    {
        var engineeringVersion = "V21";

        return blockType switch
        {
            "FC" => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{engineeringVersion}"" />
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
      <ProgrammingLanguage>{ToProgrammingLanguageXml(language)}</ProgrammingLanguage>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.FC>
</Document>",

            "OB" => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{engineeringVersion}"" />
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
      <ProgrammingLanguage>{ToProgrammingLanguageXml(language)}</ProgrammingLanguage>
      <SecondaryType>{obEventClass ?? "ProgramCycle"}</SecondaryType>
      <SetENOAutomatically>false</SetENOAutomatically>
    </AttributeList>
    <ObjectList>
      <MultilingualText ID=""1"" CompositionName=""Comment"" />
      <MultilingualText ID=""2"" CompositionName=""Title"" />
    </ObjectList>
  </SW.Blocks.OB>
</Document>",

            "GLOBALDB" or "DB" => $@"<?xml version=""1.0"" encoding=""utf-8""?>
<Document>
  <Engineering version=""{engineeringVersion}"" />
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
</Document>",

            _ => throw new InvalidOperationException($"Unsupported block type for XML generation: {blockType}")
        };
    }

    private static string ToProgrammingLanguageXml(string language)
    {
        return language switch
        {
            "LAD" => "LAD",
            "FBD" => "FBD",
            "STL" => "STL",
            "SCL" => "SCL",
            "GRAPH" => "GRAPH",
            _ => throw new InvalidOperationException(
                $"Unknown programming language '{language}'. Valid values: LAD, FBD, STL, SCL, GRAPH.")
        };
    }

    private static ProgrammingLanguage ParseLanguage(string language)
    {
        return language switch
        {
            "LAD" => ProgrammingLanguage.LAD,
            "FBD" => ProgrammingLanguage.FBD,
            "STL" => ProgrammingLanguage.STL,
            "SCL" => ProgrammingLanguage.SCL,
            "GRAPH" => ProgrammingLanguage.GRAPH,
            _ => throw new InvalidOperationException(
                $"Unknown programming language '{language}'. Valid values: LAD, FBD, STL, SCL, GRAPH.")
        };
    }
}
