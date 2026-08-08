using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness;

/// <summary>
/// Bounded, vendor-free normalization of an arbitrary CLR value returned or observed by a
/// <c>probe_vci_read_contract</c> case into a stable <see cref="VciProbeNormalizedValueInfo"/>
/// tree. Pure: never touches Siemens Openness, and never performs filesystem I/O — path handling
/// only canonicalizes a string via <see cref="Path.GetFullPath(string)"/>.
///
/// <para>
/// Closed type switch over the value's runtime type. A value that is not one of the recognized
/// kinds below is recorded as <c>Kind == "unsupported_value"</c> carrying only its
/// <see cref="VciProbeNormalizedValueInfo.RuntimeType"/> — <see cref="object.ToString"/> is never
/// invoked on an object this normalizer does not recognize. A defective or hostile Siemens type's
/// <c>ToString()</c> override (or a type's <c>ToString()</c> that is merely expensive or
/// side-effecting) must never be able to fail a probe case.
/// </para>
///
/// <para>
/// Lives under <c>TiaMcpServer.OpennessWorker/Openness</c> (compiled as part of the net48 worker)
/// but is also linked directly into <c>TiaMcpServer.Tests</c> (net8.0) — it has no <c>Siemens.*</c>
/// dependency, so it can be exercised without the net48 Openness worker build.
/// </para>
/// </summary>
public static class VciProbeValueNormalizer
{
    /// <summary>
    /// Recursion bound applied when a caller does not supply an explicit <c>maxDepth</c>. Depth 0
    /// is the top-level normalized value itself; each nested collection level increases depth by
    /// one.
    /// </summary>
    public const int DefaultMaxDepth = 4;

    /// <summary>Stable <see cref="VciProbeNormalizedValueInfo.Kind"/> for a value whose runtime type this normalizer does not recognize.</summary>
    public const string UnsupportedValueKind = "unsupported_value";

    /// <summary>Stable <see cref="VciProbeNormalizedValueInfo.Kind"/> for a value that was not normalized because <c>maxDepth</c> was exhausted.</summary>
    public const string DepthExceededKind = "depth_exceeded";

    /// <summary>Normalizes <paramref name="value"/> using <see cref="DefaultMaxDepth"/> as the recursion bound.</summary>
    public static VciProbeNormalizedValueInfo Normalize(object? value, int maxCollectionItems)
        => Normalize(value, maxCollectionItems, DefaultMaxDepth);

    /// <summary>
    /// Normalizes <paramref name="value"/> into a bounded <see cref="VciProbeNormalizedValueInfo"/>
    /// tree. Ordered collections are traversed in source (enumeration) order; at most
    /// <paramref name="maxCollectionItems"/> items are normalized per collection, and any excess
    /// items are recorded as a single typed <see cref="VciProbeNormalizedValueInfo.Omission"/>
    /// rather than normalized. Nesting deeper than <paramref name="maxDepth"/> is recorded as
    /// <see cref="DepthExceededKind"/> without descending further.
    /// </summary>
    public static VciProbeNormalizedValueInfo Normalize(object? value, int maxCollectionItems, int maxDepth)
    {
        if (maxCollectionItems < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCollectionItems), maxCollectionItems, "must be 1 or greater.");
        }

        if (maxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), maxDepth, "must be 0 or greater.");
        }

        return NormalizeCore(value, maxCollectionItems, maxDepth, depth: 0);
    }

    private static VciProbeNormalizedValueInfo NormalizeCore(object? value, int maxCollectionItems, int maxDepth, int depth)
    {
        if (value is null)
        {
            return new VciProbeNormalizedValueInfo { Kind = "null", RuntimeType = string.Empty };
        }

        var runtimeType = value.GetType().FullName ?? value.GetType().Name;

        if (depth > maxDepth)
        {
            return new VciProbeNormalizedValueInfo { Kind = DepthExceededKind, RuntimeType = runtimeType };
        }

        switch (value)
        {
            case string text:
                return new VciProbeNormalizedValueInfo { Kind = "string", RuntimeType = runtimeType, StringValue = text };

            case bool boolean:
                return new VciProbeNormalizedValueInfo
                {
                    Kind = "boolean",
                    RuntimeType = runtimeType,
                    StringValue = boolean ? "true" : "false",
                };

            case Enum enumValue:
                return NormalizeEnum(enumValue, runtimeType);

            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return new VciProbeNormalizedValueInfo
                {
                    Kind = "integer",
                    RuntimeType = runtimeType,
                    StringValue = FormatIntegral(value),
                };

            case float or double or decimal:
                return new VciProbeNormalizedValueInfo
                {
                    Kind = "float",
                    RuntimeType = runtimeType,
                    StringValue = FormatFloatingPoint(value),
                };

            case CultureInfo culture:
                return new VciProbeNormalizedValueInfo { Kind = "culture", RuntimeType = runtimeType, StringValue = culture.Name };

            case FileInfo fileInfo:
                return NormalizePath(fileInfo.ToString(), "file", runtimeType);

            case DirectoryInfo directoryInfo:
                return NormalizePath(directoryInfo.ToString(), "directory", runtimeType);

            case IEnumerable enumerable:
                return NormalizeCollection(enumerable, maxCollectionItems, maxDepth, depth, runtimeType);

            default:
                // Deliberately does not call value.ToString(): an unrecognized type's ToString()
                // override is never invoked by this normalizer.
                return new VciProbeNormalizedValueInfo { Kind = UnsupportedValueKind, RuntimeType = runtimeType };
        }
    }

    private static string FormatIntegral(object value) => value switch
    {
        sbyte v => v.ToString(CultureInfo.InvariantCulture),
        byte v => v.ToString(CultureInfo.InvariantCulture),
        short v => v.ToString(CultureInfo.InvariantCulture),
        ushort v => v.ToString(CultureInfo.InvariantCulture),
        int v => v.ToString(CultureInfo.InvariantCulture),
        uint v => v.ToString(CultureInfo.InvariantCulture),
        long v => v.ToString(CultureInfo.InvariantCulture),
        ulong v => v.ToString(CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Unexpected integral runtime type '{value.GetType()}'."),
    };

    // "G17" (double) and "G9" (float) are the documented invariant-culture round-trip format
    // specifiers on both .NET Framework 4.8 and modern .NET; decimal already round-trips exactly
    // under any standard format because it stores its value as exact decimal digits.
    private static string FormatFloatingPoint(object value) => value switch
    {
        float v => v.ToString("G9", CultureInfo.InvariantCulture),
        double v => v.ToString("G17", CultureInfo.InvariantCulture),
        decimal v => v.ToString(CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"Unexpected floating-point runtime type '{value.GetType()}'."),
    };

    private static VciProbeNormalizedValueInfo NormalizeEnum(Enum value, string runtimeType)
    {
        var name = Enum.GetName(value.GetType(), value);

        string integral;
        try
        {
            integral = Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }
        catch (OverflowException)
        {
            integral = Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
        }

        return new VciProbeNormalizedValueInfo
        {
            Kind = "enum",
            RuntimeType = runtimeType,
            EnumName = name,
            EnumIntegralValue = integral,
        };
    }

    /// <summary>
    /// Canonicalizes <paramref name="originalPath"/> via <see cref="Path.GetFullPath(string)"/>
    /// inside a <c>try</c>, retaining the original path either way. Internal (rather than private)
    /// specifically so the canonicalization-failure branch is directly unit-testable with an
    /// arbitrary invalid path string — <see cref="FileInfo"/> and <see cref="DirectoryInfo"/> are
    /// sealed, so a malformed instance cannot be constructed to exercise this branch indirectly.
    /// </summary>
    internal static VciProbeNormalizedValueInfo NormalizePath(string originalPath, string pathKind, string runtimeType)
    {
        var result = new VciProbeNormalizedValueInfo
        {
            Kind = "path",
            RuntimeType = runtimeType,
            PathKind = pathKind,
            OriginalPath = originalPath,
        };

        try
        {
            result.CanonicalPath = Path.GetFullPath(originalPath);
        }
        catch (Exception ex)
        {
            result.PathCanonicalizationException = VciProbeExceptionNormalizer.Normalize(ex);
        }

        return result;
    }

    private static VciProbeNormalizedValueInfo NormalizeCollection(
        IEnumerable enumerable, int maxCollectionItems, int maxDepth, int depth, string runtimeType)
    {
        var result = new VciProbeNormalizedValueInfo { Kind = "collection", RuntimeType = runtimeType };

        var observed = 0;
        var totalSeen = 0;
        foreach (var item in enumerable)
        {
            totalSeen++;
            if (observed < maxCollectionItems)
            {
                result.Items.Add(NormalizeCore(item, maxCollectionItems, maxDepth, depth + 1));
                observed++;
            }
        }

        if (totalSeen > observed)
        {
            result.Omission = new VciProbeOmissionInfo
            {
                Reason = $"Collection truncated after {observed} item(s); {totalSeen - observed} more item(s) were observed but not normalized.",
                BudgetName = nameof(VciProbeRequestInfo.MaxCollectionItems),
                BudgetValue = maxCollectionItems,
                ObservedCount = observed,
            };
        }

        return result;
    }
}

/// <summary>
/// One normalized value in a <see cref="VciProbeValueNormalizer"/> tree. Only the member(s)
/// relevant to <see cref="Kind"/> are populated; the rest stay at their empty/null/default value.
/// Deliberately internal to the pure worker layer (VCI Workspace Phase 1 Task 3) — a later task
/// maps this tree onto the wire <see cref="VciProbeReturnInfo"/> / <see cref="VciProbeMemberObservationInfo"/>
/// shape.
/// </summary>
public sealed class VciProbeNormalizedValueInfo
{
    /// <summary>
    /// Stable discriminator: one of <c>null</c>, <c>string</c>, <c>boolean</c>, <c>integer</c>,
    /// <c>float</c>, <c>enum</c>, <c>culture</c>, <c>path</c>, <c>collection</c>,
    /// <see cref="VciProbeValueNormalizer.UnsupportedValueKind"/>, or
    /// <see cref="VciProbeValueNormalizer.DepthExceededKind"/>.
    /// </summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>CLR runtime type name of the normalized value, as observed by the worker.</summary>
    public string RuntimeType { get; set; } = string.Empty;

    /// <summary>Invariant-culture string rendering for scalar kinds (<c>string</c>, <c>boolean</c>, <c>integer</c>, <c>float</c>, <c>culture</c>).</summary>
    public string? StringValue { get; set; }

    /// <summary>Declared enumeration member name. Populated only when <see cref="Kind"/> is <c>enum</c>.</summary>
    public string? EnumName { get; set; }

    /// <summary>Invariant-culture underlying integral value. Populated only when <see cref="Kind"/> is <c>enum</c>.</summary>
    public string? EnumIntegralValue { get; set; }

    /// <summary><c>file</c> or <c>directory</c>. Populated only when <see cref="Kind"/> is <c>path</c>.</summary>
    public string? PathKind { get; set; }

    /// <summary>Original (uncanonicalized) path as reported by the source object. Populated only when <see cref="Kind"/> is <c>path</c>.</summary>
    public string? OriginalPath { get; set; }

    /// <summary><see cref="Path.GetFullPath(string)"/> of <see cref="OriginalPath"/>, or null when canonicalization failed.</summary>
    public string? CanonicalPath { get; set; }

    /// <summary>Member-level exception captured when canonicalizing <see cref="OriginalPath"/> failed.</summary>
    public VciProbeNormalizedExceptionInfo? PathCanonicalizationException { get; set; }

    /// <summary>Normalized items in source (enumeration) order. Populated only when <see cref="Kind"/> is <c>collection</c>.</summary>
    public List<VciProbeNormalizedValueInfo> Items { get; set; } = new();

    /// <summary>Present only when a collection's <c>maxCollectionItems</c> budget truncated enumeration.</summary>
    public VciProbeOmissionInfo? Omission { get; set; }
}
