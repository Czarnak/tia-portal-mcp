using Siemens.Engineering;
using Siemens.Engineering.Compiler;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using System.Text.Json;
using TiaMcpServer.Json;
using TiaMcpServer.OpennessWorker.Openness;
using Xunit;

namespace TiaMcpServer.Tests.Block;

public class CompileCheckerTests
{
    [Fact]
    public void Compile_PreservesTopLevelMessageAndReportedTotals()
    {
        // Catches lost compiler fields or totals recomputed from the visible message rows.
        var result = new CompilerResult
        {
            State = CompilerResultState.Error,
            ErrorCount = 7,
            WarningCount = 3,
            Messages =
            {
                new CompilerResultMessage
                {
                    Description = "Undefined tag 'MissingTag'.",
                    Path = "PLC_DP/Blocks/Main",
                    State = CompilerResultState.Error,
                    ErrorCount = 1
                }
            }
        };

        var report = CompileChecker.Compile(ProjectWithCompiler(result), "PLC_DP", null);

        var plc = Assert.Single(report.Plcs);
        var message = Assert.Single(plc.Messages);
        Assert.Equal("Undefined tag 'MissingTag'.", message.Description);
        Assert.Equal("PLC_DP/Blocks/Main", message.Path);
        Assert.Equal("Error", message.Severity);
        Assert.Equal("plc", report.Scope);
        Assert.Equal("Error", plc.State);
        Assert.Equal("Error", report.OverallState);
        Assert.Equal(7, plc.ErrorCount);
        Assert.Equal(3, plc.WarningCount);
        Assert.Equal(7, report.TotalErrorCount);
        Assert.Equal(3, report.TotalWarningCount);
    }

    [Fact]
    public void Compile_ProjectsNestedMessagesInParentFirstOrder()
    {
        // Catches the production mapper dropping descendants beneath a blank compiler header.
        var result = new CompilerResult
        {
            State = CompilerResultState.Error,
            ErrorCount = 7,
            WarningCount = 3,
            Messages =
            {
                new CompilerResultMessage
                {
                    Description = "",
                    Path = "PLC_DP/Blocks",
                    Messages =
                    {
                        new CompilerResultMessage
                        {
                            Description = "Undefined tag 'MissingTag'.",
                            Path = "PLC_DP/Blocks/Main",
                            State = CompilerResultState.Error,
                            ErrorCount = 1,
                            Messages =
                            {
                                new CompilerResultMessage
                                {
                                    Description = "Implicit conversion.",
                                    Path = "PLC_DP/Blocks/Helper",
                                    State = CompilerResultState.Warning,
                                    WarningCount = 1
                                }
                            }
                        }
                    }
                }
            }
        };

        var report = CompileChecker.Compile(ProjectWithCompiler(result), "PLC_DP", null);

        var plc = Assert.Single(report.Plcs);
        Assert.Equal(7, plc.ErrorCount);
        Assert.Equal(3, plc.WarningCount);
        Assert.Equal(7, report.TotalErrorCount);
        Assert.Equal(3, report.TotalWarningCount);
        Assert.Equal("Error", report.OverallState);
        Assert.Collection(plc.Messages,
            header =>
            {
                Assert.Equal("", header.Description);
                Assert.Equal("PLC_DP/Blocks", header.Path);
                Assert.Equal("Information", header.Severity);
            },
            error =>
            {
                Assert.Equal("Undefined tag 'MissingTag'.", error.Description);
                Assert.Equal("PLC_DP/Blocks/Main", error.Path);
                Assert.Equal("Error", error.Severity);
            },
            warning =>
            {
                Assert.Equal("Implicit conversion.", warning.Description);
                Assert.Equal("PLC_DP/Blocks/Helper", warning.Path);
                Assert.Equal("Warning", warning.Severity);
            });
    }

    [Fact]
    public void Compile_UsesMessageStateInsteadOfAggregatedChildCounts()
    {
        var result = new CompilerResult
        {
            State = CompilerResultState.Error,
            ErrorCount = 1,
            Messages =
            {
                new CompilerResultMessage
                {
                    Description = "Warning with a child error",
                    State = CompilerResultState.Warning,
                    ErrorCount = 1,
                    WarningCount = 1
                }
            }
        };

        var report = CompileChecker.Compile(ProjectWithCompiler(result), null, null);

        Assert.Equal("Warning", Assert.Single(Assert.Single(report.Plcs).Messages).Severity);
        Assert.Equal("Error", report.OverallState);
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("\u0001\"\\\r\n<>&\U0001F600")]
    public void Compile_BoundsWholeSerializedReportAcrossPlcs(string text)
    {
        var result = new CompilerResult { State = CompilerResultState.Error, ErrorCount = 9, WarningCount = 4 };
        for (var i = 0; i < 210; i++)
            result.Messages.Add(new CompilerResultMessage
            {
                Description = string.Concat(Enumerable.Repeat(text, 1100)),
                Path = string.Concat(Enumerable.Repeat(text, 1100)),
                State = CompilerResultState.Error,
                ErrorCount = 1
            });
        var project = ProjectWithCompiler(result);
        AddPlc(project, result, "PLC_2", "Station_2");

        var report = CompileChecker.Compile(project, null, null);
        var json = JsonSerializer.Serialize(report, TiaJson.Presentation);

        Assert.True(json.Length < 60000, $"Serialized report has {json.Length} characters.");
        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("plcs").GetArrayLength());
        Assert.Equal(18, report.TotalErrorCount);
        Assert.Equal(8, report.TotalWarningCount);
        Assert.Equal("Error", report.OverallState);
        Assert.NotEmpty(report.Plcs[0].Messages);
        Assert.InRange(report.Plcs.Sum(p => p.Messages.Count), 1, 200);
        Assert.InRange(report.Plcs.SelectMany(p => p.Messages).Sum(m => m.Description.Length + m.Path.Length), 1, 32000);
        Assert.All(report.Plcs, p => Assert.Single(p.DiagnosticNotes));
        Assert.All(report.Plcs.SelectMany(p => p.Messages), m =>
        {
            Assert.InRange(m.Description.Length, 0, 1024);
            Assert.InRange(m.Path.Length, 0, 1024);
        });
    }

    [Fact]
    public void Compile_AccountsForJsonEscapesEvenWhenRawProjectionFits()
    {
        // Neither field length nor the 32,000-character projection budget is exhausted.
        // Only complete JSON sizing can detect this two-PLC payload overflow.
        var result = new CompilerResult { ErrorCount = 5, State = CompilerResultState.Error };
        for (var i = 0; i < 10; i++)
            result.Messages.Add(new CompilerResultMessage
            {
                Description = new string('\u0001', 500),
                Path = new string('\u0001', 500),
                State = CompilerResultState.Error
            });
        var project = ProjectWithCompiler(result);
        AddPlc(project, result, "PLC_2", "Station_2");

        var report = CompileChecker.Compile(project, null, null);

        Assert.True(JsonSerializer.Serialize(report, TiaJson.Presentation).Length < 60000);
        Assert.Equal(10, report.TotalErrorCount);
        Assert.InRange(report.Plcs[0].Messages.Count, 1, 9);
        Assert.Empty(report.Plcs[1].Messages);
        Assert.All(report.Plcs, plc => Assert.Single(plc.DiagnosticNotes));
    }

    [Fact]
    public void Compile_ReportsReflectionOmissionsWithoutLeakingExceptions()
    {
        var result = new CompilerResult
        {
            ErrorCount = 3,
            State = CompilerResultState.Error,
            Messages =
            {
                new UnreadablePathMessage { Description = "retained first", State = CompilerResultState.Error },
                new UnreadablePathMessage { Description = "retained second", State = CompilerResultState.Error }
            }
        };

        var report = CompileChecker.Compile(ProjectWithCompiler(result), null, null);

        var plc = Assert.Single(report.Plcs);
        Assert.Equal(new[] { "retained first", "retained second" }, plc.Messages.Select(m => m.Description));
        Assert.All(plc.Messages, m => Assert.Equal("", m.Path));
        Assert.Single(plc.DiagnosticNotes);
        Assert.DoesNotContain("private-project-path", JsonSerializer.Serialize(report, TiaJson.Presentation));
        Assert.Equal(3, report.TotalErrorCount);
    }

    [Fact]
    public void Compile_RejectsOversizedIdentityMetadataWithoutReturningMalformedJson()
    {
        var project = new Siemens.Engineering.Project();
        AddPlc(project, new CompilerResult(), "PLC_DP", new string('x', 60000));

        var error = Assert.Throws<InvalidOperationException>(() => CompileChecker.Compile(project, null, null));

        Assert.Contains("response limit", error.Message);
        Assert.Contains("Compilation may have run", error.Message);
        Assert.Contains("inspect", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("in-memory", error.Message);
        Assert.DoesNotContain("select a single PLC", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(new string('x', 100), error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Compile_BlockCompilerExceptionReturnsSanitizedIdentityReport(bool engineeringFailure)
    {
        // Catches block compilation escaping to Program's raw exception-message response.
        const string privateDetail = "C:\\private-project-path\\fixture.ap21: sensitive compiler detail";
        Exception failure = engineeringFailure
            ? new EngineeringException(privateDetail)
            : new InvalidOperationException(privateDetail);
        var project = new Siemens.Engineering.Project();
        var software = AddPlc(project, new CompilerResult(), "PLC_DP", "Station_1");
        software.BlockGroup.Blocks.Items.Add(new FC
        {
            Name = "Main",
            CompilerService = new FixtureCompiler(new CompilerResult(), failure)
        });

        var report = CompileChecker.Compile(project, null, "Station_1/Blocks/Main");

        var plc = Assert.Single(report.Plcs);
        AssertIdentity(plc);
        Assert.Equal("block", report.Scope);
        Assert.Equal("Station_1/Blocks/Main", report.BlockPath);
        Assert.Equal("Error", plc.State);
        Assert.Equal("Error", report.OverallState);
        Assert.Empty(plc.Messages);
        Assert.NotEmpty(Assert.Single(plc.DiagnosticNotes));
        var json = JsonSerializer.Serialize(report, TiaJson.Presentation);
        Assert.DoesNotContain("private-project-path", json);
        Assert.DoesNotContain("sensitive compiler detail", json);
        Assert.True(json.Length < 60000);
    }

    [Theory]
    [InlineData(null, false, "Warning", 0, 7)]
    [InlineData(null, true, "Error", 3, 7)]
    [InlineData("PLC_DP/Blocks/Main", false, "Warning", 0, 7)]
    [InlineData("PLC_DP/Blocks/Main", true, "Error", 3, 7)]
    public void Compile_MessageCollectionGetterFailurePreservesKnownCompilerResult(
        string? blockPath, bool engineeringFailure, string state, int errorCount, int warningCount)
    {
        // Catches a message getter failure incorrectly erasing the already-read compiler totals.
        const string privateDetail = "C:\\private-project-path\\fixture.ap21: message collection unavailable";
        var result = new CompilerResult
        {
            State = Enum.Parse<CompilerResultState>(state),
            ErrorCount = errorCount,
            WarningCount = warningCount,
            MessagesFailure = engineeringFailure
                ? new EngineeringException(privateDetail)
                : new InvalidOperationException(privateDetail)
        };
        var project = new Siemens.Engineering.Project();
        var software = AddPlc(project, result, "PLC_DP", "Station_1");
        software.BlockGroup.Blocks.Items.Add(new FC
        {
            Name = "Main",
            CompilerService = new FixtureCompiler(result)
        });

        var report = CompileChecker.Compile(project, null, blockPath);

        var plc = Assert.Single(report.Plcs);
        AssertIdentity(plc);
        Assert.Equal(state, plc.State);
        Assert.Equal(state, report.OverallState);
        Assert.Equal(errorCount, plc.ErrorCount);
        Assert.Equal(warningCount, plc.WarningCount);
        Assert.Equal(errorCount, report.TotalErrorCount);
        Assert.Equal(warningCount, report.TotalWarningCount);
        Assert.Empty(plc.Messages);
        Assert.NotEmpty(Assert.Single(plc.DiagnosticNotes));
        var json = JsonSerializer.Serialize(report, TiaJson.Presentation);
        Assert.DoesNotContain("private-project-path", json);
        Assert.DoesNotContain("message collection unavailable", json);
        Assert.True(json.Length < 60000);
    }

    [Theory]
    [InlineData("PLC_DP")]
    [InlineData("Station_1")]
    public void Compile_ReturnsSoftwareAndDeviceIdentityForEitherSelector(string selector)
    {
        var result = new CompilerResult { State = CompilerResultState.Success };

        var report = CompileChecker.Compile(ProjectWithCompiler(result), selector, null);

        AssertIdentity(Assert.Single(report.Plcs));
    }

    [Theory]
    [InlineData("PLC_DP", false)]
    [InlineData("PLC_DP", true)]
    [InlineData("Station_1", false)]
    [InlineData("Station_1", true)]
    public void Compile_PreservesIdentityWhenCompilerFails(string selector, bool engineeringFailure)
    {
        var project = new Siemens.Engineering.Project();
        var software = AddPlc(project, new CompilerResult(), "PLC_DP", "Station_1");
        Exception failure = engineeringFailure
            ? new EngineeringException("private-project-path")
            : new InvalidOperationException("private-project-path");
        software.CompilerService = new FixtureCompiler(new CompilerResult(), failure);

        var report = CompileChecker.Compile(project, selector, null);

        var plc = Assert.Single(report.Plcs);
        AssertIdentity(plc);
        Assert.Equal("Error", plc.State);
        Assert.Equal("Error", report.OverallState);
        Assert.DoesNotContain("private-project-path", Assert.Single(plc.DiagnosticNotes));
    }

    [Theory]
    [InlineData(null, false, "session")]
    [InlineData(null, false, "io")]
    [InlineData(null, false, "cancel")]
    [InlineData(null, true, "session")]
    [InlineData(null, true, "io")]
    [InlineData(null, true, "cancel")]
    [InlineData("PLC_DP/Blocks/Main", false, "session")]
    [InlineData("PLC_DP/Blocks/Main", false, "io")]
    [InlineData("PLC_DP/Blocks/Main", false, "cancel")]
    [InlineData("PLC_DP/Blocks/Main", true, "session")]
    [InlineData("PLC_DP/Blocks/Main", true, "io")]
    [InlineData("PLC_DP/Blocks/Main", true, "cancel")]
    public void Compile_PropagatesInfrastructureAndInterruptionFailures(
        string? blockPath, bool messageAccessFailure, string failureKind)
    {
        // A typed compiler report must not hide loss of the session, I/O failure, or cancellation.
        Exception failure = failureKind switch
        {
            "session" => new NonRecoverableException("session lost"),
            "io" => new IOException("transport unavailable"),
            "cancel" => new OperationCanceledException("operation interrupted"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind))
        };
        var result = new CompilerResult
        {
            State = CompilerResultState.Warning,
            WarningCount = 7,
            MessagesFailure = messageAccessFailure ? failure : null
        };
        var compiler = new FixtureCompiler(result, messageAccessFailure ? null : failure);
        var project = new Siemens.Engineering.Project();
        var software = AddPlc(project, result, "PLC_DP", "Station_1");
        software.CompilerService = compiler;
        software.BlockGroup.Blocks.Items.Add(new FC { Name = "Main", CompilerService = compiler });

        var propagated = Assert.ThrowsAny<Exception>(() => CompileChecker.Compile(project, null, blockPath));

        Assert.Same(failure, propagated);
    }

    [Theory]
    [InlineData(null, "PLC_DP/Blocks/Main")]
    [InlineData(null, "Station_1/Blocks/Main")]
    [InlineData("PLC_DP", "Main")]
    [InlineData("Station_1", "Main")]
    [InlineData(null, "Main")]
    public void Compile_BlockScopeUsesSelectedSoftwareForIdentityAndCompilation(string? selector, string blockPath)
    {
        var project = new Siemens.Engineering.Project();
        if (blockPath != "Main" || selector != null)
        {
            var decoy = AddPlc(project, new CompilerResult(), "Other_PLC", "Other_Station");
            decoy.BlockGroup.Blocks.Items.Add(new FC
            {
                Name = "Main",
                CompilerService = new FixtureCompiler(new CompilerResult { ErrorCount = 99 })
            });
        }
        var software = AddPlc(project, new CompilerResult { ErrorCount = 42 }, "PLC_DP", "Station_1");
        software.BlockGroup.Blocks.Items.Add(new FC
        {
            Name = "Main",
            CompilerService = new FixtureCompiler(new CompilerResult { State = CompilerResultState.Error, ErrorCount = 7 })
        });

        var report = CompileChecker.Compile(project, selector, blockPath);

        Assert.Equal("block", report.Scope);
        Assert.Equal(blockPath, report.BlockPath);
        Assert.Equal(7, report.TotalErrorCount);
        var plc = Assert.Single(report.Plcs);
        AssertIdentity(plc);
        if (selector == null && blockPath == "Main")
            Assert.Single(plc.DiagnosticNotes);
        else
            Assert.Empty(plc.DiagnosticNotes);
    }

    private static void AssertIdentity(TiaMcpServer.Contracts.PlcCompileInfo plc)
    {
        Assert.Equal("PLC_DP", plc.PlcName);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(plc, TiaJson.Presentation));
        Assert.True(json.RootElement.TryGetProperty("deviceName", out var deviceName));
        Assert.Equal("Station_1", deviceName.GetString());
    }

    private static Siemens.Engineering.Project ProjectWithCompiler(CompilerResult result)
    {
        var project = new Siemens.Engineering.Project();
        AddPlc(project, result, "PLC_DP", "Station_1");
        return project;
    }

    private static PlcSoftware AddPlc(Siemens.Engineering.Project project, CompilerResult result, string softwareName, string deviceName)
    {
        var software = new PlcSoftware
        {
            Name = softwareName,
            CompilerService = new FixtureCompiler(result)
        };
        var device = new Device { Name = deviceName };
        device.DeviceItems.Items.Add(new DeviceItem
        {
            Name = "CPU_1",
            Container = new SoftwareContainer { Software = software }
        });
        project.Devices.Items.Add(device);
        return software;
    }

    private sealed class FixtureCompiler(CompilerResult result, Exception? failure = null) : ICompilable
    {
        public CompilerResult Compile() => failure is null ? result : throw failure;
    }

    private sealed class UnreadablePathMessage : CompilerResultMessage
    {
        public new string Path => throw new InvalidOperationException("private-project-path");
    }
}
