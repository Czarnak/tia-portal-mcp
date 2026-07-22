using TiaMcpServer.Safety;

namespace TiaMcpServer.Tests;

/// <summary>
/// Gives a test an isolated, unique-per-instance audit directory for <see cref="WriteSafetyService"/>
/// and deletes it on dispose. Centralizes the create-directory/try/finally scaffold that used to be
/// copied into every test touching <see cref="WriteSafetyService"/>, and keeps every call site off the
/// real %LOCALAPPDATA%\TiaMcpServer\audit directory — including tests that never reach AppendAudit,
/// since Dispose is a no-op when the directory was never created.
/// </summary>
public sealed class TempAuditDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "tia-test-audit-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Builds a <see cref="WriteSafetyService"/> pointed at this instance's directory. Tests that need
    /// a controlled clock (e.g. to exercise token expiry) or a non-default lifetime can supply their
    /// own; both otherwise default to the service's normal production values.
    /// </summary>
    public WriteSafetyService CreateSafety(
        Func<DateTimeOffset>? getUtcNow = null,
        TimeSpan? tokenLifetime = null)
        => new(
            getUtcNow ?? (() => DateTimeOffset.UtcNow),
            tokenLifetime ?? WriteSafetyService.DefaultTokenLifetime,
            Path);

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
