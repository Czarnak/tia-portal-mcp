using System.Text.Json;
using TiaMcpServer.Contracts;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CompileCheckInfoTests
{
    private static readonly JsonSerializerOptions JsonOptions = WorkerJson.PayloadOptionsFor(typeof(CompileCheckReport));

    [Fact]
    public void EmptyReportSerializesWithDefaultValues()
    {
        var report = new CompileCheckReport();

        var roundTripped = RoundTrip(report);

        Assert.Equal("plc", roundTripped.Scope);
        Assert.Null(roundTripped.BlockPath);
        Assert.NotNull(roundTripped.Plcs);
        Assert.Empty(roundTripped.Plcs);
        Assert.Equal(0, roundTripped.TotalErrorCount);
        Assert.Equal(0, roundTripped.TotalWarningCount);
        Assert.Equal("Success", roundTripped.OverallState);
    }

    [Fact]
    public void FullReportRoundTripsPlcInfoAndMessages()
    {
        var report = new CompileCheckReport
        {
            Scope = "plc",
            TotalErrorCount = 1,
            TotalWarningCount = 1,
            OverallState = "Error",
            Plcs =
            {
                new PlcCompileInfo
                {
                    PlcName = "PLC_1",
                    State = "Error",
                    ErrorCount = 1,
                    WarningCount = 1,
                    Messages =
                    {
                        new CompileMessageInfo
                        {
                            Description = "Unknown tag",
                            Path = "PLC_1/Blocks/Main",
                            Severity = "Error"
                        },
                        new CompileMessageInfo
                        {
                            Description = "Implicit conversion",
                            Path = "PLC_1/Blocks/Helper",
                            Severity = "Warning"
                        }
                    },
                    DiagnosticNotes =
                    {
                        "Skipped one compiler detail."
                    }
                }
            }
        };

        var roundTripped = RoundTrip(report);
        var plc = Assert.Single(roundTripped.Plcs);
        var error = Assert.Single(plc.Messages, message => message.Severity == "Error");
        var warning = Assert.Single(plc.Messages, message => message.Severity == "Warning");

        Assert.Equal("plc", roundTripped.Scope);
        Assert.Equal("Error", roundTripped.OverallState);
        Assert.Equal(1, roundTripped.TotalErrorCount);
        Assert.Equal(1, roundTripped.TotalWarningCount);
        Assert.Equal("PLC_1", plc.PlcName);
        Assert.Equal("Error", plc.State);
        Assert.Equal(1, plc.ErrorCount);
        Assert.Equal(1, plc.WarningCount);
        Assert.Equal("Unknown tag", error.Description);
        Assert.Equal("PLC_1/Blocks/Main", error.Path);
        Assert.Equal("Implicit conversion", warning.Description);
        Assert.Equal("Skipped one compiler detail.", Assert.Single(plc.DiagnosticNotes));
    }

    [Fact]
    public void BlockScopeReportHasCorrectScopeAndBlockPath()
    {
        var report = new CompileCheckReport
        {
            Scope = "block",
            BlockPath = "PLC_1/Blocks/Main",
            OverallState = "Success"
        };

        var roundTripped = RoundTrip(report);

        Assert.Equal("block", roundTripped.Scope);
        Assert.Equal("PLC_1/Blocks/Main", roundTripped.BlockPath);
        Assert.Equal("Success", roundTripped.OverallState);
    }

    [Fact]
    public void DeviceIdentitySurvivesJsonRoundTrip()
    {
        const string json = "{\"plcName\":\"PLC_DP\",\"deviceName\":\"Station_1\",\"state\":\"Success\"}";
        var plc = JsonSerializer.Deserialize<PlcCompileInfo>(json, JsonOptions)!;

        using var roundTripped = JsonDocument.Parse(JsonSerializer.Serialize(plc, JsonOptions));

        Assert.Equal("PLC_DP", roundTripped.RootElement.GetProperty("plcName").GetString());
        Assert.True(roundTripped.RootElement.TryGetProperty("deviceName", out var deviceName));
        Assert.Equal("Station_1", deviceName.GetString());
    }

    [Fact]
    public void LegacyJsonRetainsUnknownDeviceIdentityAsExplicitNull()
    {
        var plc = JsonSerializer.Deserialize<PlcCompileInfo>("{\"plcName\":\"Legacy_PLC\"}", JsonOptions)!;

        using var roundTripped = JsonDocument.Parse(JsonSerializer.Serialize(plc, JsonOptions));

        Assert.Equal("Legacy_PLC", plc.PlcName);
        Assert.Null(plc.DeviceName);
        Assert.Equal(JsonValueKind.Null, roundTripped.RootElement.GetProperty("deviceName").ValueKind);
    }

    private static CompileCheckReport RoundTrip(CompileCheckReport report)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        return JsonSerializer.Deserialize<CompileCheckReport>(json, JsonOptions)!;
    }
}
