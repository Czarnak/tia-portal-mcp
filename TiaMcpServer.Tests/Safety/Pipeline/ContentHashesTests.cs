using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Safety.Pipeline;
using Xunit;

namespace TiaMcpServer.Tests.Safety.Pipeline;

public sealed class ContentHashesTests
{
    private const string AbcHex = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

    [Theory]
    [InlineData("xml")]
    [InlineData("source")]
    public void Compute_Abc_MatchesSha256VectorWithFormatTag(string format)
    {
        Assert.Equal($"{format}:sha256:{AbcHex}", ContentHashes.Compute(format, "abc"));
    }

    [Fact]
    public void Compute_UnknownFormat_Throws()
    {
        Assert.Throws<ArgumentException>(() => ContentHashes.Compute("json", "abc"));
    }

    [Fact]
    public void Sha256Hex_IsLowerCaseUtf8()
    {
        Assert.Equal(AbcHex, ContentHashes.Sha256Hex("abc"));
        Assert.NotEqual(ContentHashes.Sha256Hex("a"), ContentHashes.Sha256Hex("ä"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(AbcHex)]
    [InlineData("xml:md5:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    [InlineData("xml:sha256:BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD")]
    [InlineData("xml:sha256:ba7816bf")]
    [InlineData("json:sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
    public void Check_MissingOrMalformed_IsValidationErrorWithoutEvidence(string? expected)
    {
        var check = ContentHashes.Check(expected, "xml", "abc");

        Assert.False(check.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, check.ErrorCategory);
        Assert.False(string.IsNullOrWhiteSpace(check.Message));
        Assert.Null(check.Evidence);
    }

    [Fact]
    public void Check_CrossFormat_NamesBothFormats()
    {
        var expected = ContentHashes.Compute("source", "abc");

        var check = ContentHashes.Check(expected, "xml", "abc");

        Assert.False(check.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, check.ErrorCategory);
        Assert.Contains("source", check.Message);
        Assert.Contains("xml", check.Message);
    }

    [Fact]
    public void Check_Match_SucceedsWithSatisfiedEvidence()
    {
        var expected = ContentHashes.Compute("xml", "abc");

        var check = ContentHashes.Check(expected, "xml", "abc");

        Assert.True(check.Success);
        Assert.Null(check.ErrorCategory);
        Assert.NotNull(check.Evidence);
        Assert.Equal("contentHash", check.Evidence!.Name);
        Assert.Equal(expected, check.Evidence.Expected);
        Assert.Equal(expected, check.Evidence.Actual);
        Assert.True(check.Evidence.Satisfied);
    }

    [Fact]
    public void Check_Drift_IsStateChangedWithActualHash()
    {
        var expected = ContentHashes.Compute("xml", "abc");
        var actual = ContentHashes.Compute("xml", "abd");

        var check = ContentHashes.Check(expected, "xml", "abd");

        Assert.False(check.Success);
        Assert.Equal(WorkerFailureCategories.StateChanged, check.ErrorCategory);
        Assert.NotNull(check.Evidence);
        Assert.Equal(expected, check.Evidence!.Expected);
        Assert.Equal(actual, check.Evidence.Actual);
        Assert.False(check.Evidence.Satisfied);
    }
}
