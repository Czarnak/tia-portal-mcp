using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Project;

public class ArchiveModeNamesTests
{
    [Fact]
    public void EmptyArchiveModeDefaultsToCompressed()
    {
        Assert.True(ArchiveModeNames.TryNormalize(null, out var normalized, out var error));

        Assert.Equal(ArchiveModeNames.Compressed, normalized);
        Assert.Null(error);
    }

    [Fact]
    public void ArchiveModeNormalizesIgnoringCase()
    {
        Assert.True(ArchiveModeNames.TryNormalize("discardrestorabledata", out var normalized, out var error));

        Assert.Equal(ArchiveModeNames.DiscardRestorableData, normalized);
        Assert.Null(error);
    }

    [Fact]
    public void InvalidArchiveModeReturnsError()
    {
        Assert.False(ArchiveModeNames.TryNormalize("Zip", out _, out var error));

        Assert.Contains("Invalid archive mode", error);
    }

    [Theory]
    [InlineData(ArchiveModeNames.Compressed)]
    [InlineData(ArchiveModeNames.DiscardRestorableDataAndCompressed)]
    public void EnsureArchiveExtension_AppendsZap21ForCompressedModes(string mode)
    {
        var result = ArchiveModeNames.EnsureArchiveExtension("MyBackup", mode);

        Assert.Equal("MyBackup.zap21", result);
    }

    [Theory]
    [InlineData(ArchiveModeNames.None)]
    [InlineData(ArchiveModeNames.DiscardRestorableData)]
    public void EnsureArchiveExtension_LeavesNameUnchangedForFolderBasedModes(string mode)
    {
        var result = ArchiveModeNames.EnsureArchiveExtension("MyBackup", mode);

        Assert.Equal("MyBackup", result);
    }

    [Fact]
    public void EnsureArchiveExtension_DoesNotDuplicateAnExistingExtension()
    {
        var result = ArchiveModeNames.EnsureArchiveExtension("MyBackup.zap21", ArchiveModeNames.Compressed);

        Assert.Equal("MyBackup.zap21", result);
    }

    [Fact]
    public void EnsureArchiveExtension_ExtensionMatchIsCaseInsensitive()
    {
        var result = ArchiveModeNames.EnsureArchiveExtension("MyBackup.ZAP21", ArchiveModeNames.Compressed);

        Assert.Equal("MyBackup.ZAP21", result);
    }

    [Theory]
    [InlineData("MyBackup.zap2")]
    [InlineData("MyBackup.zap21x")]
    [InlineData("MyBackupzap21")]
    public void EnsureArchiveExtension_DoesNotTreatANearMissSuffixAsAlreadyPresent(string archiveName)
    {
        var result = ArchiveModeNames.EnsureArchiveExtension(archiveName, ArchiveModeNames.Compressed);

        Assert.Equal(archiveName + ArchiveModeNames.CompressedFileExtension, result);
    }

    [Fact]
    public void EnsureArchiveExtension_EmptyNameYieldsBareExtension()
    {
        var result = ArchiveModeNames.EnsureArchiveExtension(string.Empty, ArchiveModeNames.Compressed);

        Assert.Equal(".zap21", result);
    }

    [Fact]
    public void EnsureArchiveExtension_TrailingDotProducesADoubleDot()
    {
        // Documents current behavior rather than prescribing it: a name ending in "." is an
        // unusual input RequireName does not reject upstream, and this method does no trimming.
        var result = ArchiveModeNames.EnsureArchiveExtension("MyBackup.", ArchiveModeNames.Compressed);

        Assert.Equal("MyBackup..zap21", result);
    }

    [Fact]
    public void TryNormalizeThenEnsureArchiveExtension_MirrorsTheRealCallPath()
    {
        Assert.True(ArchiveModeNames.TryNormalize("compressed", out var normalizedMode, out _));

        var result = ArchiveModeNames.EnsureArchiveExtension("MyBackup", normalizedMode);

        Assert.Equal("MyBackup.zap21", result);
    }
}
