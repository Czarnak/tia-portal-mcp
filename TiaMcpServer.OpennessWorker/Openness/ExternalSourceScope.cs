using System;
using System.IO;
using Siemens.Engineering.SW.ExternalSources;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Owns the temp file and the PlcExternalSource project node created for one import, and
/// guarantees both are gone afterwards.
///
/// <para>
/// ExternalSources.CreateFromFile adds a node under an "External source files" folder — a visible,
/// persistent change to the user's project that has nothing to do with what they asked for. Every
/// import path must dispose this scope; <see cref="ProjectNodeRemoved"/> then reports whether the
/// removal actually succeeded, and PlcTypePostconditionVerifier turns a false into a user-facing
/// warning.
/// </para>
/// <para>
/// The scope is handed the external source group to create under rather than a PlcSoftware,
/// because a software unit owns its own PlcExternalSourceSystemGroup: a unit-scoped type must
/// register its source under the unit, not under the top-level PLC.
/// </para>
/// </summary>
internal sealed class ExternalSourceScope : IDisposable
{
    private readonly string _tempDirectory;
    private PlcExternalSource? _source;
    private bool _disposed;

    private ExternalSourceScope(string tempDirectory, PlcExternalSource source, string filePath)
    {
        _tempDirectory = tempDirectory;
        _source = source;
        FilePath = filePath;
    }

    public PlcExternalSource Source =>
        _source ?? throw new ObjectDisposedException(nameof(ExternalSourceScope));

    public string FilePath { get; }

    /// <summary>True once the project node is gone. Read by the postcondition verifier.</summary>
    public bool ProjectNodeRemoved { get; private set; }

    /// <summary>
    /// Writes <paramref name="content"/> to a temp file and registers it under
    /// <paramref name="externalSourceGroup"/> — the group of the software scope that owns the
    /// object being written (the PLC's, or a software unit's own).
    /// </summary>
    public static ExternalSourceScope Create(
        PlcExternalSourceSystemGroup externalSourceGroup,
        string fileName,
        string content)
    {
        if (externalSourceGroup is null) throw new ArgumentNullException(nameof(externalSourceGroup));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("A file name is required.", nameof(fileName));

        var tempDirectory = Path.Combine(
            Path.GetTempPath(), "tia-mcp-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var filePath = Path.Combine(tempDirectory, fileName);
            File.WriteAllBytes(filePath, SourceTextEncoding.ForFile(content));

            var sourceName = Path.GetFileNameWithoutExtension(fileName)
                + "_tiamcp_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var source = externalSourceGroup.ExternalSources.CreateFromFile(sourceName, filePath);

            return new ExternalSourceScope(tempDirectory, source, filePath);
        }
        catch
        {
            TryDeleteDirectory(tempDirectory);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _source?.Delete();
            ProjectNodeRemoved = true;
        }
        catch (Exception ex)
        {
            // Surfaced as a worker warning rather than swallowed: a surviving node is a real,
            // user-visible change to their project that they need to know about.
            Console.Error.WriteLine(
                $"Failed to remove the temporary external source node from the project: {ex.Message}");
        }
        finally
        {
            _source = null;
            TryDeleteDirectory(_tempDirectory);
        }
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception)
        {
            // A leftover temp directory is harmless; a leftover project node is not. Catching
            // everything is deliberate: Dispose runs inside a finally while another exception may
            // already be unwinding, and a cleanup failure here must never replace that exception.
        }
    }
}
