using System;
using System.IO;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

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

            var exception = Assert.Throws<WorkerOperationException>(
                () => BlockImportStager.StageDocuments(stagingRoot, bundle));

            Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(stagingRoot)!, "escaped.xml")));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Stage_RejectsAnExistingDestinationAsValidationError()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            var destination = Path.Combine(stagingRoot, "Main.xml");
            File.WriteAllText(destination, "<Existing />");

            var exception = Assert.Throws<WorkerOperationException>(
                () => BlockImportStager.StageDocuments(
                    stagingRoot,
                    CreateBundle(new BlockImportDocument("Main.xml", "Main.xml", "<Main />"))));

            Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
            Assert.Equal("<Existing />", File.ReadAllText(destination));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Stage_RejectsAReusedRootWithAnUndeclaredFileBeforeWritingDocuments()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            File.WriteAllText(Path.Combine(stagingRoot, "undeclared.xml"), "<Undeclared />");
            var bundle = CreateBundle(
                new BlockImportDocument("Main.xml", "Main.xml", "<Main />"),
                new BlockImportDocument("Types.xml", "Types.xml", "<Types />"));

            var exception = Assert.Throws<WorkerOperationException>(
                () => BlockImportStager.StageDocuments(stagingRoot, bundle));

            Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
            Assert.False(File.Exists(Path.Combine(stagingRoot, "Main.xml")));
            Assert.False(File.Exists(Path.Combine(stagingRoot, "Types.xml")));
        }
        finally
        {
            Directory.Delete(stagingRoot, recursive: true);
        }
    }

    [Fact]
    public void Stage_RejectsAReusedRootWithAnUndeclaredSubdirectoryBeforeWritingDocuments()
    {
        var stagingRoot = CreateStagingRoot();

        try
        {
            Directory.CreateDirectory(Path.Combine(stagingRoot, "undeclared"));
            var bundle = CreateBundle(
                new BlockImportDocument("Main.xml", "Main.xml", "<Main />"),
                new BlockImportDocument("Types.xml", "Types.xml", "<Types />"));

            var exception = Assert.Throws<WorkerOperationException>(
                () => BlockImportStager.StageDocuments(stagingRoot, bundle));

            Assert.Equal(WorkerFailureCategories.ValidationError, exception.FailureCategory);
            Assert.False(File.Exists(Path.Combine(stagingRoot, "Main.xml")));
            Assert.False(File.Exists(Path.Combine(stagingRoot, "Types.xml")));
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
            var bundle = CreateBundle(
                new BlockImportDocument("Main.xml", "Main.xml", "<Main />"),
                new BlockImportDocument("Types.xml", "Types.xml", "<Types />"));

            BlockImportStager.StageDocuments(stagingRoot, bundle);

            Assert.Equal(
                new[] { "Main.xml", "Types.xml" },
                Directory.EnumerateFiles(stagingRoot).Select(Path.GetFileName).OrderBy(fileName => fileName));
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
