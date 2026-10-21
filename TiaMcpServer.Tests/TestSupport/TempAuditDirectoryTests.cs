using Xunit;

namespace TiaMcpServer.Tests.TestSupport;

public sealed class TempAuditDirectoryTests
{
    [Fact]
    public void Dispose_WithoutCreatedDirectory_IsNoOp()
    {
        using var directory = new TempAuditDirectory();

        directory.Dispose();

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public void Dispose_DeletesNestedFiles()
    {
        using var directory = new TempAuditDirectory();
        var nested = Directory.CreateDirectory(Path.Combine(directory.Path, "nested"));
        File.WriteAllText(Path.Combine(nested.FullName, "audit.json"), "{}");

        directory.Dispose();

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public async Task Dispose_RetriesUntilTransientFileLockIsReleased()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using var locked = new FileStream(Path.Combine(directory.Path, "requests.log"),
            FileMode.Create, FileAccess.Write, FileShare.None);
        var release = Task.Run(async () =>
        {
            await Task.Delay(200);
            locked.Dispose();
        });

        try
        {
            directory.Dispose();
        }
        finally
        {
            await release;
        }

        Assert.False(Directory.Exists(directory.Path));
    }

    [Fact]
    public void Dispose_WithPersistentFileLock_StillThrows()
    {
        using var directory = new TempAuditDirectory();
        Directory.CreateDirectory(directory.Path);
        using (var locked = new FileStream(Path.Combine(directory.Path, "requests.log"),
                   FileMode.Create, FileAccess.Write, FileShare.None))
        {
            Assert.Throws<IOException>(directory.Dispose);
            Assert.True(Directory.Exists(directory.Path));
        }
    }
}
