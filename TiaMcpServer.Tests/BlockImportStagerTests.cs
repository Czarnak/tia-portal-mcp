using System;
using System.IO;
using System.Linq;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockImportStagerTests
{
    [Fact]
    public void Stage_WritesEveryDocumentExactlyOnceInOrder()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            var bundle = CreateBundle(
                new BlockImportDocument("Main.xml", "Main.xml", "<Main />"),
                new BlockImportDocument("Types.xml", "Types.xml", "<Types />"));

            var stagedPaths = BlockImportStager.StageDocuments(stagingRoot, bundle);

            Assert.Equal(
                new[]
                {
                    Path.Combine(stagingRoot, "Main.xml"),
                    Path.Combine(stagingRoot, "Types.xml")
                },
                stagedPaths);
            Assert.Equal("<Main />", File.ReadAllText(stagedPaths[0]));
            Assert.Equal("<Types />", File.ReadAllText(stagedPaths[1]));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Stage_ReturnsCanonicalPathsUnderRoot()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            var stagedPaths = BlockImportStager.StageDocuments(
                Path.Combine(stagingRoot, "."),
                CreateBundle(new BlockImportDocument("Main.xml", "Main.xml", "<Main />")));

            var stagedPath = Assert.Single(stagedPaths);
            Assert.Equal(Path.GetFullPath(Path.Combine(stagingRoot, "Main.xml")), stagedPath);
            Assert.Equal(Path.GetFullPath(stagingRoot), Path.GetDirectoryName(stagedPath));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Stage_RejectsAPathThatEscapesCanonicalRoot()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            var bundle = CreateBundle(new BlockImportDocument("Main.xml", "..\\escaped.xml", "<Main />"));

            Assert.Throws<ArgumentException>(() => BlockImportStager.StageDocuments(stagingRoot, bundle));
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(stagingRoot)!, "escaped.xml")));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Stage_DoesNotCreateUndeclaredFiles()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            var bundle = CreateBundle(new BlockImportDocument("Main.xml", "Main.xml", "<Main />"));

            BlockImportStager.StageDocuments(stagingRoot, bundle);

            Assert.Equal(new[] { "Main.xml" }, Directory.EnumerateFiles(stagingRoot).Select(Path.GetFileName));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    private static ParsedBlockImportBundle CreateBundle(params BlockImportDocument[] documents)
    {
        return new ParsedBlockImportBundle(documents[0].LogicalName, documents);
    }

    private static string CreateStagingRoot()
    {
        var stagingRoot = Path.Combine(Path.GetTempPath(), "tia-mcp-stager-tests", Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(stagingRoot).FullName;
    }
}
