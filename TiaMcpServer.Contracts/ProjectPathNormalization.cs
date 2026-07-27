using System;
using System.IO;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Shared "is this the same project?" comparability primitive. <see cref="ProjectOpenPolicy"/>
/// (worker-side: is the requested path the same as what TIA already has attached) and
/// <see cref="ProjectSessionBinding"/> (host-side: is the requested path the same as what this
/// MCP session is bound to) both need path comparisons to survive relative segments ("..",
/// trailing separators, forward vs back slashes) - not just casing - so they share this helper
/// rather than each rolling their own.
///
/// <see cref="Path.GetFullPath(string)"/> can throw for more than obviously-invalid paths: on
/// .NET Framework (where the worker runs) it also throws <see cref="System.Security.SecurityException"/>
/// when the caller lacks the required filesystem permission, on top of the usual
/// <see cref="ArgumentException"/> / <see cref="NotSupportedException"/> / <see cref="PathTooLongException"/> /
/// <see cref="IOException"/> family. This helper only ever wants a comparable string, never a
/// crash, so any failure to canonicalize falls back to the trimmed literal instead of
/// propagating - a bare catch is the correct scope here, not an enumerated exception list.
/// </summary>
public static class ProjectPathNormalization
{
    public static string? Canonicalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path!.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            // Not a resolvable path (a FakeWorker scenario keyword, a permission failure, an
            // exotic path shape, etc). Compare literally rather than letting a pure
            // comparability helper crash its caller.
            return trimmed;
        }
    }
}
