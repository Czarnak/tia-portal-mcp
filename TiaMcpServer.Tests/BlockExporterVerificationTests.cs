using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests;

public class BlockExporterVerificationTests
{
    [Fact]
    public void VerifyReExportedPrimaryDocument_ExportsDeclaredPrimaryWhenResolvedNameDiffers()
    {
        string? exportedDocumentName = null;

        var evidence = BlockExporter.VerifyReExportedPrimaryDocument(
            resolvedTargetDocumentName: "Resolved.s7dcl",
            primaryDocumentName: "Declared.s7dcl",
            exportAndVerify: (_, documentName) =>
            {
                exportedDocumentName = documentName;
                return true;
            });

        Assert.Equal("Declared.s7dcl", exportedDocumentName);
        Assert.True(evidence.ReExportSucceeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifyReExportedPrimaryDocument_CleanupFailureAddsCappedWarningWithoutChangingResult(
        bool reExportSucceeded)
    {
        var cleanupFailure = new string('x', 600);

        var evidence = BlockExporter.VerifyReExportedPrimaryDocument(
            resolvedTargetDocumentName: "Resolved.s7dcl",
            primaryDocumentName: "Declared.s7dcl",
            exportAndVerify: (_, _) => reExportSucceeded,
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
