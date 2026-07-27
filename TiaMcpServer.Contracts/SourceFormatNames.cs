using System;
using System.Collections.Generic;

namespace TiaMcpServer.Contracts;

/// <summary>
/// Document format selector shared by the type and block read/write operations.
///
/// <para>
/// Deliberately object-kind-agnostic: <see cref="Source"/> means "Siemens' external-source text
/// for whatever this object is" — .udt for a PlcType, .db for a GlobalDB, .scl for an SCL block —
/// and the extension is always derived from the resolved object, never from the caller.
/// </para>
/// <para>
/// This class exposes no default. The default is per-operation and passed in by the caller:
/// the type operations default to <see cref="Source"/> because they are net-new surface, and the
/// block operations default to <see cref="Xml"/> because they have callers whose payloads must
/// not change. Flipping block defaults belongs to roadmap Phase 5, not here.
/// </para>
/// </summary>
public static class SourceFormatNames
{
    public const string Source = "source";
    public const string Xml = "xml";

    public static readonly IReadOnlyList<string> Allowed = new[] { Source, Xml };

    public static bool TryNormalize(string? value, string fallback, out string normalized, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            normalized = fallback;
            error = null;
            return true;
        }

        foreach (var allowed in Allowed)
        {
            if (string.Equals(value, allowed, StringComparison.OrdinalIgnoreCase))
            {
                normalized = allowed;
                error = null;
                return true;
            }
        }

        normalized = string.Empty;
        error = $"Invalid format '{value}'. Allowed values: {string.Join(", ", Allowed)}.";
        return false;
    }
}