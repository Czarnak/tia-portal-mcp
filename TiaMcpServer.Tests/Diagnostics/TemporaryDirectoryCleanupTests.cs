using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.OpennessWorker.Openness.Infrastructure;
using TiaMcpServer.OpennessWorker.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Diagnostics;

[Collection("Process environment")]
public sealed class TemporaryDirectoryCleanupTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "tia-test-export-cleanup-" + Guid.NewGuid().ToString("N"));
    private readonly TextWriter _originalError = Console.Error;
    private readonly StringWriter _warnings = new();

    public TemporaryDirectoryCleanupTests()
    {
        Directory.CreateDirectory(_directory);
        Console.SetError(_warnings);
    }

    [Fact]
    public void TryDeleteTempDirectory_RemovesNonEmptyDirectoryRecursively()
    {
        var child = Directory.CreateDirectory(Path.Combine(_directory, "nested"));
        File.WriteAllText(Path.Combine(child.FullName, "export.xml"), "<Document />");

        TemporaryDirectoryCleanup.TryDeleteTempDirectory(_directory);

        Assert.False(Directory.Exists(_directory));
        Assert.Empty(_warnings.ToString());
    }

    [Fact]
    public void TryDeleteTempDirectory_MissingDirectoryDoesNotWarn()
    {
        Directory.Delete(_directory);

        TemporaryDirectoryCleanup.TryDeleteTempDirectory(_directory);

        Assert.False(Directory.Exists(_directory));
        Assert.Empty(_warnings.ToString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryDeleteTempDirectory_LockedFilePreservesExportOutcome(bool exportFails)
    {
        using var locked = new FileStream(
            Path.Combine(_directory, "export.xml"), FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var exportFailure = new WorkerOperationException(
            WorkerFailureCategories.PortalLost, "Original export failure.");

        string Export()
        {
            try
            {
                if (exportFails) throw exportFailure;
                return "exported content";
            }
            finally
            {
                TemporaryDirectoryCleanup.TryDeleteTempDirectory(_directory);
            }
        }

        if (exportFails)
        {
            Assert.Same(exportFailure, Assert.Throws<WorkerOperationException>(() => Export()));
        }
        else
        {
            Assert.Equal("exported content", Export());
        }

        Assert.True(Directory.Exists(_directory));
        var warning = Assert.Single(_warnings.ToString().Split(
            Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        Assert.StartsWith("Temporary export directory cleanup failed: ", warning);
    }

    public void Dispose()
    {
        Console.SetError(_originalError);
        _warnings.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
