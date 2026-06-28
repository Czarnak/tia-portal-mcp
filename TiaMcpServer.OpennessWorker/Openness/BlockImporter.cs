using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

public static class BlockImporter
{
    private const string FileSeparatorPrefix = "--- FILE:";

    public static string Import(Project project, string blockPath, string yamlContent)
    {
        if (project is null) throw new ArgumentNullException(nameof(project));
        if (blockPath is null) throw new ArgumentNullException(nameof(blockPath));
        if (yamlContent is null) throw new ArgumentNullException(nameof(yamlContent));

        var address = BlockAddress.Parse(blockPath);
        var target = BlockTargetResolver.ResolveForImport(project, address);

        string projectDir = project.Path.Directory?.FullName ?? Path.GetTempPath();
        bool isSeparator = yamlContent.Contains(FileSeparatorPrefix);
        bool isXml = IsXmlContent(yamlContent);

        // A single Simatic ML XML document must be imported via Import(FileInfo, ImportOptions).
        // ImportFromDocuments is only for documents packages whose main file is .s7dcl; a lone
        // .xml never matches there, which surfaces as a misleading "file does not exist" error.
        if (isXml && !isSeparator)
        {
            string xmlPath = Path.Combine(projectDir, target.DocumentName + ".xml");
            File.WriteAllText(xmlPath, yamlContent);
            try
            {
                var blocks = target.Group.Blocks.Import(new FileInfo(xmlPath), ImportOptions.Override);
                return $"Import succeeded: {blocks.Count} block(s) imported.";
            }
            finally
            {
                if (File.Exists(xmlPath)) File.Delete(xmlPath);
            }
        }

        // Documents package (.s7dcl main file plus optional resource files).
        string tempDir = Path.Combine(projectDir, "tia-mcp-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteContentToTempDir(tempDir, target.DocumentName, yamlContent);

            var result = target.Group.Blocks.ImportFromDocuments(
                new DirectoryInfo(tempDir),
                target.DocumentName,
                ImportDocumentOptions.Override);

            if (result.State != DocumentResultState.Success)
            {
                throw new InvalidOperationException($"Import failed with state: {result.State}");
            }

            return $"Import succeeded: state={result.State}";
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static void WriteContentToTempDir(string tempDir, string blockName, string yamlContent)
    {
        if (!yamlContent.Contains(FileSeparatorPrefix))
        {
            File.WriteAllText(Path.Combine(tempDir, blockName), yamlContent);
            return;
        }

        string[] lines = yamlContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        string? currentFileName = null;
        var sectionLines = new System.Collections.Generic.List<string>();

        foreach (string line in lines)
        {
            if (line.StartsWith(FileSeparatorPrefix, StringComparison.Ordinal))
            {
                FlushSection(tempDir, currentFileName, sectionLines);
                currentFileName = ExtractFileName(line);
                sectionLines.Clear();
            }
            else
            {
                sectionLines.Add(line);
            }
        }

        FlushSection(tempDir, currentFileName, sectionLines);
    }

    private static string ExtractFileName(string separatorLine)
    {
        // Expected format: "--- FILE: filename ---"
        string inner = separatorLine.Substring(FileSeparatorPrefix.Length).TrimEnd();
        if (inner.EndsWith("---", StringComparison.Ordinal))
        {
            inner = inner.Substring(0, inner.Length - 3);
        }

        return inner.Trim();
    }

    private static void FlushSection(
        string tempDir,
        string? fileName,
        System.Collections.Generic.List<string> lines)
    {
        if (fileName is null || lines.Count == 0)
        {
            return;
        }

        string content = string.Join(Environment.NewLine, lines);
        File.WriteAllText(Path.Combine(tempDir, fileName), content);
    }
}
