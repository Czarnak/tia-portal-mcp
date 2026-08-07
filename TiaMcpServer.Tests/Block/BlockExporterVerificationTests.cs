using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class BlockExporterVerificationTests
{
    [Fact]
    public void VerifyPrimaryDocument_ExportsDeclaredPrimaryWhenResolvedNameDiffers()
    {
        string? exportedDocumentName = null;

        var evidence = BlockExporter.VerifyPrimaryDocument(
            resolvedTargetDocumentName: "ResolvedTarget.xml",
            primaryDocumentName: "DeclaredPrimary.xml",
            exportDocuments: (_, documentName) =>
            {
                exportedDocumentName = documentName;
                return true;
            });

        Assert.Equal("ResolvedTarget.xml", exportedDocumentName);
        Assert.True(evidence.ReExportSucceeded);
    }

    [Fact]
    public void Re_export_uses_the_resolved_base_name_not_the_declared_document_name()
    {
        string? observedName = null;

        var evidence = BlockExporter.VerifyPrimaryDocument(
            resolvedTargetDocumentName: "Main",
            primaryDocumentName: "Main.xml",
            exportDocuments: (directory, name) =>
            {
                observedName = name;
                return true;
            },
            cleanupDirectory: _ => { });

        Assert.Equal("Main", observedName);
        Assert.True(evidence.ReExportSucceeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifyPrimaryDocument_CleanupFailureAddsCappedWarningWithoutChangingResult(
        bool reExportSucceeded)
    {
        var cleanupFailure = new string('x', 600);

        var evidence = BlockExporter.VerifyPrimaryDocument(
            resolvedTargetDocumentName: "ResolvedTarget.xml",
            primaryDocumentName: "DeclaredPrimary.xml",
            exportDocuments: (_, _) => reExportSucceeded,
            cleanupDirectory: path =>
            {
                Directory.Delete(path, recursive: true);
                throw new IOException(cleanupFailure);
            });

        Assert.Equal(reExportSucceeded, evidence.ReExportSucceeded);
        var warning = Assert.Single(evidence.Warnings);
        Assert.StartsWith("Block update verification cleanup failed: ", warning);
        Assert.Equal(512, warning.Length);
    }
}
