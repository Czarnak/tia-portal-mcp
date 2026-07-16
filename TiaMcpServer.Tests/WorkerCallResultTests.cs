using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests;

public class WorkerCallResultTests
{
    [Fact]
    public void Ok_CarriesPayloadWithoutError()
    {
        var result = WorkerCallResult.Ok("{\"a\":1}");

        Assert.True(result.Success);
        Assert.Equal("{\"a\":1}", result.Payload);
        Assert.Null(result.Error);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Fail_CarriesErrorAndEmptyPayload()
    {
        var result = WorkerCallResult.Fail("boom");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Payload);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void ToText_RendersPayloadOnSuccess()
    {
        Assert.Equal("data", WorkerCallResult.Ok("data").ToText());
    }

    [Fact]
    public void ToText_RendersErrorPrefixOnFailure()
    {
        Assert.Equal("Error: boom", WorkerCallResult.Fail("boom").ToText());
    }

    [Fact]
    public void Ok_PayloadStartingWithErrorPrefixStaysSuccessful()
    {
        // The whole point of item 1.1: payload text must never drive classification.
        var result = WorkerCallResult.Ok("Error: literal block comment content, not a failure");

        Assert.True(result.Success);
    }

    [Fact]
    public void Warnings_AreAttachedToBothShapes()
    {
        Assert.Single(WorkerCallResult.Ok("x", new[] { "w1" }).Warnings);
        Assert.Single(WorkerCallResult.Fail("e", new[] { "w1" }).Warnings);
    }
}
