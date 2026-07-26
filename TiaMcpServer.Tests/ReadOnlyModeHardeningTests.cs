using TiaMcpServer.Cli;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using Xunit;

namespace TiaMcpServer.Tests;

public class ReadOnlyModeHardeningTests
{
    [Fact]
    public void AccessModeParser_MissingValue_FailsClearly()
    {
        var result = AccessModeParser.Parse(new[] { "--access-mode" });

        Assert.False(result.IsValid);
        Assert.Contains("requires a value", result.Error);
    }

    [Fact]
    public void AccessModeParser_DoesNotConsumeAnotherOptionAsValue()
    {
        var result = AccessModeParser.Parse(new[] { "--access-mode", "--project", "Line.ap21" });

        Assert.False(result.IsValid);
        Assert.Contains("requires a value", result.Error);
    }

    [Fact]
    public void AccessModeParser_TrimsConfiguredValue()
    {
        var result = AccessModeParser.ParseValue("  read-only  ");

        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
    }

    [Fact]
    public void AccessModeParser_ConflictingModes_FailClearly()
    {
        var result = AccessModeParser.Parse(new[] { "--read-only", "--access-mode", "read-write" });

        Assert.False(result.IsValid);
        Assert.Contains("Conflicting", result.Error);
    }

    [Fact]
    public void AccessModeParser_RepeatedEquivalentModes_AreAllowed()
    {
        var result = AccessModeParser.Parse(new[] { "--read-only", "--access-mode=read-only" });

        Assert.True(result.IsValid);
        Assert.Equal(McpAccessMode.ReadOnly, result.Mode);
    }

    [Fact]
    public void HostArgumentFilter_RemovesBareAccessModeAlias()
    {
        var filtered = HostArgumentFilter.RemoveAccessModeArguments(
            new[] { "--read-only", "--project", "Line.ap21" });

        Assert.Equal(new[] { "--project", "Line.ap21" }, filtered);
    }

    [Fact]
    public void HostArgumentFilter_RemovesAccessModeKeyAndValue()
    {
        var filtered = HostArgumentFilter.RemoveAccessModeArguments(
            new[] { "--json", "--access-mode", "read-write", "--project=Line.ap21" });

        Assert.Equal(new[] { "--json", "--project=Line.ap21" }, filtered);
    }

    [Fact]
    public void WorkerAccessMode_InvalidExplicitValue_FailsClosed()
    {
        var mode = WorkerOperationAuthorization.ParseAccessMode(
            new[] { "--access-mode", "unexpected" });

        Assert.Equal(McpAccessMode.ReadOnly, mode);
    }

    [Fact]
    public void WorkerAccessMode_MissingExplicitValue_FailsClosed()
    {
        var mode = WorkerOperationAuthorization.ParseAccessMode(
            new[] { "--access-mode" });

        Assert.Equal(McpAccessMode.ReadOnly, mode);
    }

    [Fact]
    public void WorkerAccessMode_ConflictingModes_FailClosed()
    {
        var mode = WorkerOperationAuthorization.ParseAccessMode(
            new[] { "--access-mode=read-write", "--access-mode", "read-only" });

        Assert.Equal(McpAccessMode.ReadOnly, mode);
    }

    [Fact]
    public void WorkerTiaConfirmations_AreDisabledInReadOnlyMode()
    {
        Assert.False(WorkerOperationAuthorization.AllowsTiaConfirmations(McpAccessMode.ReadOnly));
        Assert.True(WorkerOperationAuthorization.AllowsTiaConfirmations(McpAccessMode.ReadWrite));
    }

    [Fact]
    public void DoctorCliParser_ReadOnlyAlias_ReportsReadOnlyMode()
    {
        var options = DoctorCliParser.Parse(new[] { "--read-only" });

        Assert.True(options.Valid);
        Assert.Equal(McpAccessMode.ReadOnly, options.AccessMode);
    }

    [Fact]
    public void DoctorCliParser_AccessModeOption_ReportsRequestedMode()
    {
        var options = DoctorCliParser.Parse(new[] { "--access-mode", "read-write", "--json" });

        Assert.True(options.Valid);
        Assert.True(options.Json);
        Assert.Equal(McpAccessMode.ReadWrite, options.AccessMode);
    }

    [Fact]
    public void DoctorCliParser_MissingAccessModeValue_FailsClearly()
    {
        var options = DoctorCliParser.Parse(new[] { "--access-mode" });

        Assert.False(options.Valid);
        Assert.Contains("requires a value", options.ParseError);
    }

    [Fact]
    public void DoctorCliParser_ConflictingModes_FailClearly()
    {
        var options = DoctorCliParser.Parse(new[] { "--read-only", "--read-write" });

        Assert.False(options.Valid);
        Assert.Contains("Conflicting", options.ParseError);
    }
}
