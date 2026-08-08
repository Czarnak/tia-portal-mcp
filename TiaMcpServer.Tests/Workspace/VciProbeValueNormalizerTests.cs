using System;
using System.Globalization;
using System.IO;
using System.Linq;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Workspace;

/// <summary>
/// Pure, vendor-free coverage of <see cref="VciProbeValueNormalizer"/> — VCI Workspace Phase 1
/// Task 3.1/3.2. No Siemens Openness, no live TIA Portal, no filesystem I/O (the path tests only
/// exercise <see cref="Path.GetFullPath(string)"/> string canonicalization).
/// </summary>
public class VciProbeValueNormalizerTests
{
    private const int DefaultBudget = 100;

    [Fact]
    public void Normalize_Null_ReturnsNullKind()
    {
        var result = VciProbeValueNormalizer.Normalize(null, DefaultBudget);

        Assert.Equal("null", result.Kind);
        Assert.Null(result.StringValue);
    }

    [Fact]
    public void Normalize_String_ReturnsStringKindWithValue()
    {
        var result = VciProbeValueNormalizer.Normalize("hello", DefaultBudget);

        Assert.Equal("string", result.Kind);
        Assert.Equal("System.String", result.RuntimeType);
        Assert.Equal("hello", result.StringValue);
    }

    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void Normalize_Boolean_ReturnsBooleanKindWithInvariantLowercaseValue(bool value, string expected)
    {
        var result = VciProbeValueNormalizer.Normalize(value, DefaultBudget);

        Assert.Equal("boolean", result.Kind);
        Assert.Equal(expected, result.StringValue);
    }

    [Fact]
    public void Normalize_SignedIntegral_ReturnsIntegerKind()
    {
        var result = VciProbeValueNormalizer.Normalize((sbyte)-12, DefaultBudget);

        Assert.Equal("integer", result.Kind);
        Assert.Equal("System.SByte", result.RuntimeType);
        Assert.Equal("-12", result.StringValue);
    }

    [Fact]
    public void Normalize_LongMinValue_ReturnsIntegerKindWithoutOverflow()
    {
        var result = VciProbeValueNormalizer.Normalize(long.MinValue, DefaultBudget);

        Assert.Equal("integer", result.Kind);
        Assert.Equal(long.MinValue.ToString(CultureInfo.InvariantCulture), result.StringValue);
    }

    [Fact]
    public void Normalize_UnsignedIntegral_ReturnsIntegerKind()
    {
        var result = VciProbeValueNormalizer.Normalize(ulong.MaxValue, DefaultBudget);

        Assert.Equal("integer", result.Kind);
        Assert.Equal("System.UInt64", result.RuntimeType);
        Assert.Equal(ulong.MaxValue.ToString(CultureInfo.InvariantCulture), result.StringValue);
    }

    [Fact]
    public void Normalize_Double_ReturnsFloatKindWithInvariantRoundTrippableValue()
    {
        const double original = 1.0 / 3.0;

        var result = VciProbeValueNormalizer.Normalize(original, DefaultBudget);

        Assert.Equal("float", result.Kind);
        Assert.NotNull(result.StringValue);
        Assert.Equal(original, double.Parse(result.StringValue!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Normalize_Float_ReturnsFloatKindWithInvariantRoundTrippableValue()
    {
        const float original = 1.0f / 3.0f;

        var result = VciProbeValueNormalizer.Normalize(original, DefaultBudget);

        Assert.Equal("float", result.Kind);
        Assert.NotNull(result.StringValue);
        Assert.Equal(original, float.Parse(result.StringValue!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Normalize_Decimal_ReturnsFloatKindWithInvariantRoundTrippableValue()
    {
        const decimal original = 12345.6789m;

        var result = VciProbeValueNormalizer.Normalize(original, DefaultBudget);

        Assert.Equal("float", result.Kind);
        Assert.NotNull(result.StringValue);
        Assert.Equal(original, decimal.Parse(result.StringValue!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Normalize_Enum_ReturnsEnumKindWithDeclaredNameAndInvariantIntegralValue()
    {
        var result = VciProbeValueNormalizer.Normalize(DayOfWeek.Friday, DefaultBudget);

        Assert.Equal("enum", result.Kind);
        Assert.Equal("Friday", result.EnumName);
        Assert.Equal("5", result.EnumIntegralValue);
    }

    [Fact]
    public void Normalize_CultureInfo_ReturnsCultureKindWithName()
    {
        var result = VciProbeValueNormalizer.Normalize(CultureInfo.GetCultureInfo("en-US"), DefaultBudget);

        Assert.Equal("culture", result.Kind);
        Assert.Equal("en-US", result.StringValue);
    }

    [Fact]
    public void Normalize_FileInfo_ReturnsPathKindWithOriginalAndCanonicalPath()
    {
        var relativePath = Path.Combine("subdir", "file.txt");
        var fileInfo = new FileInfo(relativePath);

        var result = VciProbeValueNormalizer.Normalize(fileInfo, DefaultBudget);

        Assert.Equal("path", result.Kind);
        Assert.Equal("file", result.PathKind);
        Assert.Equal(relativePath, result.OriginalPath);
        Assert.Equal(Path.GetFullPath(relativePath), result.CanonicalPath);
        Assert.Null(result.PathCanonicalizationException);
    }

    [Fact]
    public void Normalize_DirectoryInfo_ReturnsPathKindWithOriginalAndCanonicalPath()
    {
        var relativePath = Path.Combine("subdir", "nested");
        var directoryInfo = new DirectoryInfo(relativePath);

        var result = VciProbeValueNormalizer.Normalize(directoryInfo, DefaultBudget);

        Assert.Equal("path", result.Kind);
        Assert.Equal("directory", result.PathKind);
        Assert.Equal(relativePath, result.OriginalPath);
        Assert.Equal(Path.GetFullPath(relativePath), result.CanonicalPath);
        Assert.Null(result.PathCanonicalizationException);
    }

    [Fact]
    public void NormalizePath_CanonicalizationFailure_RetainsOriginalPathAndMemberLevelException()
    {
        // FileInfo/DirectoryInfo are sealed (cannot be subclassed to produce an instance whose
        // constructor-time GetFullPath succeeds but whose original path is later invalid), so the
        // canonicalization-failure branch is exercised directly via the internal path helper with
        // a string containing an embedded null character — reliably invalid for
        // Path.GetFullPath on every supported platform/TFM.
        var result = VciProbeValueNormalizer.NormalizePath("bad\0path", "file", "System.IO.FileInfo");

        Assert.Equal("path", result.Kind);
        Assert.Equal("bad\0path", result.OriginalPath);
        Assert.Null(result.CanonicalPath);
        Assert.NotNull(result.PathCanonicalizationException);
        Assert.NotEmpty(result.PathCanonicalizationException!.ExceptionTypeName);
    }

    [Fact]
    public void Normalize_OrderedCollection_PreservesSourceOrder()
    {
        var input = new[] { "c", "a", "b" };

        var result = VciProbeValueNormalizer.Normalize(input, DefaultBudget);

        Assert.Equal("collection", result.Kind);
        Assert.Equal(new[] { "c", "a", "b" }, result.Items.Select(i => i.StringValue));
        Assert.Null(result.Omission);
    }

    [Fact]
    public void Normalize_CollectionExceedingBudget_TruncatesAndAppendsTypedOmission()
    {
        var input = Enumerable.Range(0, 10).ToList();

        var result = VciProbeValueNormalizer.Normalize(input, maxCollectionItems: 3);

        Assert.Equal("collection", result.Kind);
        Assert.Equal(3, result.Items.Count);
        Assert.Equal(new[] { "0", "1", "2" }, result.Items.Select(i => i.StringValue));
        Assert.NotNull(result.Omission);
        Assert.Equal(3, result.Omission!.BudgetValue);
        Assert.Equal(3, result.Omission.ObservedCount);
    }

    [Fact]
    public void Normalize_CollectionWithinBudget_ProducesNoOmission()
    {
        var input = new[] { 1, 2, 3 };

        var result = VciProbeValueNormalizer.Normalize(input, maxCollectionItems: 3);

        Assert.Null(result.Omission);
    }

    [Fact]
    public void Normalize_RecursionBeyondMaxDepth_ProducesDepthExceededWithoutRecursing()
    {
        var nested = new[] { new[] { 1, 2 } };

        var result = VciProbeValueNormalizer.Normalize(nested, maxCollectionItems: DefaultBudget, maxDepth: 0);

        Assert.Equal("collection", result.Kind);
        Assert.Single(result.Items);
        Assert.Equal("depth_exceeded", result.Items[0].Kind);
        Assert.Empty(result.Items[0].Items);
    }

    [Fact]
    public void Normalize_UnsupportedObjectWhoseToStringThrows_RecordsOnlyRuntimeTypeAndNeverCallsToString()
    {
        var poison = new ToStringThrowsObject();

        var result = VciProbeValueNormalizer.Normalize(poison, DefaultBudget);

        Assert.Equal("unsupported_value", result.Kind);
        Assert.Equal(typeof(ToStringThrowsObject).FullName, result.RuntimeType);
        Assert.Null(result.StringValue);
    }

    [Fact]
    public void Normalize_StableKindValues_MatchExpectedVocabulary()
    {
        Assert.Equal("null", VciProbeValueNormalizer.Normalize(null, DefaultBudget).Kind);
        Assert.Equal("string", VciProbeValueNormalizer.Normalize("x", DefaultBudget).Kind);
        Assert.Equal("boolean", VciProbeValueNormalizer.Normalize(true, DefaultBudget).Kind);
        Assert.Equal("integer", VciProbeValueNormalizer.Normalize(1, DefaultBudget).Kind);
        Assert.Equal("float", VciProbeValueNormalizer.Normalize(1.0, DefaultBudget).Kind);
        Assert.Equal("enum", VciProbeValueNormalizer.Normalize(DayOfWeek.Monday, DefaultBudget).Kind);
        Assert.Equal("collection", VciProbeValueNormalizer.Normalize(new int[0], DefaultBudget).Kind);
        Assert.Equal("unsupported_value", VciProbeValueNormalizer.Normalize(new object(), DefaultBudget).Kind);
    }

    /// <summary>An unrecognized object whose <see cref="ToString"/> override throws if ever called.</summary>
    private sealed class ToStringThrowsObject
    {
        public override string ToString() => throw new InvalidOperationException(
            "The normalizer must never call ToString() on an unsupported object.");
    }
}
