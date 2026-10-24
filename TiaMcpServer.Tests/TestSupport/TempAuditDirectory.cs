namespace TiaMcpServer.Tests.TestSupport;

/// <summary>
/// Gives a test an isolated, unique-per-instance audit directory for audit writers
/// and deletes it on dispose. Centralizes the create-directory/try/finally scaffold that used to be
/// copied into every test touching the audit sink, and keeps every call site off the
/// real %LOCALAPPDATA%\TiaMcpServer\audit directory — including tests that never write an audit record,
/// since Dispose is a no-op when the directory was never created.
/// </summary>
public sealed class TempAuditDirectory : IDisposable
{
    private readonly Action _waitBeforeRetry;

    /// <param name="waitBeforeRetry">Runs between delete attempts after a sharing violation;
    /// defaults to a 100 ms sleep. Tests of the retry itself pass a step that releases the lock.</param>
    public TempAuditDirectory(Action? waitBeforeRetry = null)
        => _waitBeforeRetry = waitBeforeRetry ?? (() => Thread.Sleep(100));

    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "tia-test-audit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        for (var attempt = 0; Directory.Exists(Path); attempt++)
        {
            try
            {
                Directory.Delete(Path, recursive: true);
                return;
            }
            catch (IOException ex) when (attempt < 20 && (ex.HResult & 0xFFFF) is 32 or 33)
            {
                // Windows sharing/lock violations can briefly outlive FakeWorker disposal.
                _waitBeforeRetry();
            }
        }
    }
}
