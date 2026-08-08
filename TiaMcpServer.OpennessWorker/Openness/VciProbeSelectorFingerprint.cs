using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Deterministic SHA-256 fingerprint over a candidate engineering object's schema version, runtime
/// type, typed structural path, and stable read-only identity fields. Used by the VCI Workspace
/// Phase 1 engineering-object catalog/resolver (Task 4) as a cross-check that a resolved selector
/// still points at the same object it was captured against, never as a substitute for an approved
/// public selector or write identity.
///
/// <para>
/// Pure: no Siemens Openness dependency, no filesystem or environment I/O. Lives under
/// <c>TiaMcpServer.OpennessWorker/Openness</c> (compiled as part of the net48 worker) but is also
/// linked directly into <c>TiaMcpServer.Tests</c> (net8.0), following the same pattern established
/// by <see cref="VciProbeValueNormalizer"/> and <see cref="VciProbeObservationRunner"/> in Task 3.
/// </para>
///
/// <para>
/// The canonical serialization this type hashes is a private wire format scoped to this
/// fingerprint — it is deliberately independent of
/// <c>TiaMcpServer.Contracts.VciEngineeringObjectPathSegmentInfo</c> (the public selector's
/// structural-path DTO), which carries a positional <c>Index</c> rather than a per-name ordinal.
/// The catalog/resolver (net48, Siemens-calling code) is responsible for projecting a discovered
/// object onto both shapes.
/// </para>
/// </summary>
public static class VciProbeSelectorFingerprint
{
    /// <summary>
    /// Computes the lowercase-hex SHA-256 fingerprint of <paramref name="input"/>'s canonical
    /// serialization. Deterministic for identical input regardless of <see cref="CultureInfo.CurrentCulture"/>.
    /// </summary>
    public static string Compute(VciSelectorFingerprintInput input)
    {
        if (input is null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var canonical = Serialize(input);
        var bytes = Encoding.UTF8.GetBytes(canonical);

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);

        // BitConverter.ToString formats as e.g. "AB-CD-EF"; strip the separators and lowercase.
        // ToLowerInvariant is itself culture-invariant (it is the "invariant" overload), so this
        // never depends on CultureInfo.CurrentCulture.
        return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// Builds the exact canonical string <see cref="Compute"/> hashes: a JSON-shaped object with
    /// <c>schemaVersion</c>, <c>runtimeType</c>, <c>structuralPath</c>, and <c>identityFields</c>
    /// members, in that fixed order. Internal (rather than private) so the exact serialization is
    /// directly unit-testable without re-deriving it from the hash.
    /// </summary>
    internal static string Serialize(VciSelectorFingerprintInput input)
    {
        var builder = new StringBuilder();
        builder.Append("{\"schemaVersion\":");
        AppendJsonString(builder, input.SchemaVersion ?? string.Empty);
        builder.Append(",\"runtimeType\":");
        AppendJsonString(builder, input.RuntimeTypeName ?? string.Empty);
        builder.Append(",\"structuralPath\":");
        builder.Append(SerializeStructuralPath(input.StructuralPath ?? new List<VciSelectorFingerprintPathSegment>()));
        builder.Append(",\"identityFields\":");
        builder.Append(SerializeIdentityFields(input.IdentityFields ?? new List<VciSelectorFingerprintIdentityField>()));
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>
    /// Canonical structural-path serialization: an ordinal (source-ordered) JSON array of segment
    /// objects, each carrying exactly <c>kind</c>, <c>name</c>, and <c>sameNameOrdinal</c> members
    /// in that fixed order. Never sorts the segment list — traversal order is itself part of the
    /// identity being fingerprinted. All integer formatting uses
    /// <see cref="CultureInfo.InvariantCulture"/>, so the result never varies with
    /// <see cref="CultureInfo.CurrentCulture"/>.
    /// </summary>
    internal static string SerializeStructuralPath(IReadOnlyList<VciSelectorFingerprintPathSegment> path)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < path.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var segment = path[i];
            builder.Append("{\"kind\":");
            AppendJsonString(builder, segment.Kind ?? string.Empty);
            builder.Append(",\"name\":");
            AppendJsonString(builder, segment.Name ?? string.Empty);
            builder.Append(",\"sameNameOrdinal\":");
            builder.Append(segment.SameNameOrdinal.ToString(CultureInfo.InvariantCulture));
            builder.Append('}');
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>
    /// Canonical identity-field serialization: an ordinal (caller-ordered) JSON array of
    /// <c>{"key":...,"value":...}</c> objects. Never sorts — the caller supplies identity fields in
    /// a fixed, family-specific order (e.g. Name then Number for a PLC block).
    /// </summary>
    internal static string SerializeIdentityFields(IReadOnlyList<VciSelectorFingerprintIdentityField> fields)
    {
        if (fields is null)
        {
            throw new ArgumentNullException(nameof(fields));
        }

        var builder = new StringBuilder();
        builder.Append('[');
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var field = fields[i];
            builder.Append("{\"key\":");
            AppendJsonString(builder, field.Key ?? string.Empty);
            builder.Append(",\"value\":");
            AppendJsonString(builder, field.Value ?? string.Empty);
            builder.Append('}');
        }

        builder.Append(']');
        return builder.ToString();
    }

    /// <summary>
    /// Minimal JSON string escaping (quote, backslash, control characters) sufficient for the
    /// closed set of values this fingerprint ever serializes. Not a general-purpose JSON writer.
    /// </summary>
    private static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u");
                        builder.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}

/// <summary>
/// One segment of the fingerprint's own canonical structural-path model. <see cref="SameNameOrdinal"/>
/// is the count of preceding siblings at the same tree level sharing <see cref="Name"/> (zero-based),
/// which is more resistant to spurious fingerprint churn than a raw positional index whenever an
/// unrelated, differently-named sibling is inserted or removed earlier in the same composition.
/// </summary>
public sealed class VciSelectorFingerprintPathSegment
{
    /// <summary>Engineering object type at this level (e.g. <c>Device</c>, <c>DeviceItem</c>, <c>OB</c>, <c>TagTable</c>).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Name of the object at this level of the structural path.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Zero-based count of preceding same-named siblings at this level.</summary>
    public int SameNameOrdinal { get; set; }
}

/// <summary>One stable, read-only identity field captured for a fingerprinted candidate (e.g. a PLC block's <c>Number</c>).</summary>
public sealed class VciSelectorFingerprintIdentityField
{
    /// <summary>Field name (e.g. <c>Name</c>, <c>Number</c>, <c>PositionNumber</c>).</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Invariant-culture string rendering of the field's value.</summary>
    public string Value { get; set; } = string.Empty;
}

/// <summary>Full input to <see cref="VciProbeSelectorFingerprint.Compute"/>.</summary>
public sealed class VciSelectorFingerprintInput
{
    /// <summary>Wire schema version the fingerprint was captured under (e.g. <c>VciReadProbeContract.SchemaVersion</c>).</summary>
    public string SchemaVersion { get; set; } = string.Empty;

    /// <summary>CLR runtime type name of the fingerprinted object, as observed by the worker.</summary>
    public string RuntimeTypeName { get; set; } = string.Empty;

    /// <summary>Typed structural path from a resolvable root to the object, in source (traversal) order.</summary>
    public List<VciSelectorFingerprintPathSegment> StructuralPath { get; set; } = new();

    /// <summary>Stable, read-only identity fields captured for the object, in a fixed family-specific order.</summary>
    public List<VciSelectorFingerprintIdentityField> IdentityFields { get; set; } = new();
}
