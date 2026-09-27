using TiaMcpServer.Batch;
using TiaMcpServer.Contracts;
using TiaMcpServer.Safety;
using TiaMcpServer.Tests.Worker;
using TiaMcpServer.Worker;
using Xunit;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace TiaMcpServer.Tests.Batch;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class BlockUpdateDispatchProvenanceTests
{
    private static OpennessWorkerClient CreateClient(
        ProjectSessionBinding binding,
        string? workerPath = null,
        TimeSpan? timeout = null,
        McpAccessMode mode = McpAccessMode.ReadWrite)
        => new(
            binding,
            workerExecutablePath: workerPath ?? FakeWorkerLocator.Locate(),
            requestTimeout: timeout,
            accessPolicy: new OperationAccessPolicy(mode));

    private static void AssertNotStarted(BlockImportOutcomeInfo? outcome, string sourceState)
    {
        Assert.NotNull(outcome);
        Assert.Equal("not_started", outcome.ImportStage);
        Assert.False(outcome.TargetMutationCommitted);
        Assert.Equal("not_started", outcome.CompileStage);
        Assert.Equal("not_started", outcome.FinalReadStage);
        Assert.Equal(sourceState, outcome.TemporarySourceState);
    }

    private static void AssertUnknown(BlockImportOutcomeInfo? outcome, string sourceState)
    {
        Assert.NotNull(outcome);
        Assert.Equal("unknown", outcome.ImportStage);
        Assert.Null(outcome.TargetMutationCommitted);
        Assert.Equal("unavailable", outcome.CompileStage);
        Assert.Equal("unavailable", outcome.FinalReadStage);
        Assert.Equal(sourceState, outcome.TemporarySourceState);
    }

    [Theory]
    [InlineData("not-sent", SourceFormatNames.Xml, "not_started", false, "not_applicable")]
    [InlineData("not-sent", SourceFormatNames.Source, "not_started", false, "not_created")]
    [InlineData("not-sent", null, "not_started", false, "unknown")]
    [InlineData("unknown", SourceFormatNames.Xml, "unknown", null, "not_applicable")]
    [InlineData("unknown", SourceFormatNames.Source, "unknown", null, "unknown")]
    [InlineData("sent", SourceFormatNames.Source, "unknown", null, "unknown")]
    public void Synthesizer_UsesOnlyTypedStateAndNormalizedFormat(
        string stateName,
        string? format,
        string expectedStage,
        bool? expectedCommitted,
        string expectedSourceState)
    {
        var state = stateName switch
        {
            "not-sent" => WorkerDispatchState.NotSent,
            "sent" => WorkerDispatchState.Sent,
            _ => WorkerDispatchState.Unknown
        };
        var outcome = BlockImportOutcomeSynthesizer.Synthesize(state, format);

        Assert.Equal(expectedStage, outcome.ImportStage);
        Assert.Equal(expectedCommitted, outcome.TargetMutationCommitted);
        Assert.Equal(expectedSourceState, outcome.TemporarySourceState);
    }

    [Fact]
    public async Task InvalidBatchFormat_IsNotSentWithUnknownSourceState()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, workerPath: Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe"));
        var result = await BatchWorkerInvoker.InvokeAsync(client, new BatchOperationRequest
        {
            OperationId = "invalid-format",
            Operation = "update_block_logic",
            ProjectPath = "must-not-start",
            BlockPath = "PLC/Blocks/Main",
            YamlContent = "secret submitted content",
            Format = "not-a-format"
        });

        Assert.False(result.Success);
        Assert.Equal(WorkerFailureCategories.ValidationError, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNotStarted(result.BlockImportOutcome, "unknown");
    }

    [Fact]
    public async Task BindingConflict_IsNotSentWithExactSourceState()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "ok");

        var result = await client.UpdateBlockLogicAsync("PLC/Blocks/Main", "secret", "different", SourceFormatNames.Source);

        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNotStarted(result.BlockImportOutcome, "not_created");
    }

    [Fact]
    public async Task ConfiguredProjectVerificationFailure_IsNotSentRelativeToUpdate()
    {
        const string scenario = "block-outcome-status-failure";
        var binding = new ProjectSessionBinding(scenario);
        using var client = CreateClient(binding);

        var result = await client.UpdateBlockLogicAsync(
            "PLC/Blocks/Main",
            "secret",
            scenario,
            SourceFormatNames.Source);

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNotStarted(result.BlockImportOutcome, "not_created");
    }

    [Fact]
    public async Task PinnedBindingDrift_IsNotSent()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding);
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, "ok");
        var pinned = binding.CaptureSnapshot();

        var execution = await client.ExecuteWithPinnedBindingAsync(
            pinned,
            async () =>
            {
                Assert.True(binding.BindVerified(
                    new WorkerSessionIdentity
                    {
                        WorkerSessionId = "replacement-worker",
                        SessionGeneration = (pinned.SessionGeneration ?? 0) + 1,
                        PortalProcessId = pinned.PortalProcessId,
                        ProjectPath = pinned.ProjectPath
                    },
                    forceRebind: true,
                    out var bindError), bindError);
                return await client.UpdateBlockLogicAsync(
                    "PLC/Blocks/Main", "secret", pinned.ProjectPath, SourceFormatNames.Xml);
            });

        Assert.True(execution.Success);
        var result = execution.Value!;
        Assert.Equal(WorkerFailureCategories.BindingConflict, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNotStarted(result.BlockImportOutcome, "not_applicable");
    }

    [Fact]
    public async Task HostAccessDenial_IsNotSent()
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, mode: McpAccessMode.ReadOnly);

        var result = await client.UpdateBlockLogicAsync("PLC/Blocks/Main", "secret", null, SourceFormatNames.Xml);

        Assert.Equal(WorkerFailureCategories.AccessDenied, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNotStarted(result.BlockImportOutcome, "not_applicable");
    }

    [Fact]
    public async Task WorkerLaunchFailure_IsNotSent()
    {
        var binding = new ProjectSessionBinding(null);
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe");
        using var client = CreateClient(binding, workerPath: missing);

        var result = await client.UpdateBlockLogicAsync("PLC/Blocks/Main", "secret", null, SourceFormatNames.Source);

        Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
        AssertNotStarted(result.BlockImportOutcome, "not_created");
    }

    [Fact]
    public async Task ProtocolCapabilityMismatch_IsNotSent()
    {
        var tempDirectory = Path.Combine(Path.GetTempPath(), "tia-block-protocol-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        var scriptPath = Path.Combine(tempDirectory, "legacy-worker.ps1");
        try
        {
            await File.WriteAllTextAsync(scriptPath, LegacyWorkerScript, new UTF8Encoding(false));
            var hello = JsonSerializer.Serialize(new
            {
                success = true,
                payload = "{}",
                protocolVersion = WorkerProtocol.Version,
                capabilities = WorkerProtocol.RequiredCapabilities
                    .Where(value => value != "typed-block-import-outcome-v1")
                    .ToArray()
            });
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(hello));
            var powershell = Path.Combine(
                Environment.SystemDirectory,
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            var args = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" -HelloResponseBase64 \"{encoded}\"";
            var binding = new ProjectSessionBinding(null);
            Assert.True(binding.BindVerified(new WorkerSessionIdentity
            {
                WorkerSessionId = "legacy",
                SessionGeneration = 1,
                PortalProcessId = 42,
                ProjectPath = "legacy-protocol"
            }, false, out var bindError), bindError);
            using var client = new OpennessWorkerClient(binding, workerExecutablePath: powershell);
            using var transport = new PersistentWorkerTransport(
                powershell,
                TimeSpan.FromSeconds(5),
                workerArgs: args);
            var field = typeof(OpennessWorkerClient).GetField(
                "_transport",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            field!.SetValue(client, transport);

            var result = await client.UpdateBlockLogicAsync(
                "PLC/Blocks/Main", "secret", "legacy-protocol", SourceFormatNames.Xml);

            Assert.Equal(WorkerFailureCategories.ProtocolError, result.FailureCategory);
            Assert.Equal(WorkerDispatchState.NotSent, result.DispatchState);
            AssertNotStarted(result.BlockImportOutcome, "not_applicable");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData("block-outcome-transport-hang", WorkerFailureCategories.WorkerTimeout)]
    [InlineData("block-outcome-transport-crash", WorkerFailureCategories.WorkerCrashed)]
    [InlineData("block-outcome-transport-malformed", WorkerFailureCategories.WorkerCrashed)]
    [InlineData("block-outcome-transport-null-response", WorkerFailureCategories.WorkerCrashed)]
    public async Task TransportAmbiguity_IsUnknown(string scenario, string category)
    {
        var binding = new ProjectSessionBinding(null);
        using var client = CreateClient(binding, timeout: TimeSpan.FromSeconds(2));
        await FakeWorkerBinding.BindVerifiedAsync(client, binding, scenario);

        var result = await client.UpdateBlockLogicAsync("PLC/Blocks/Main", "secret", scenario, SourceFormatNames.Source);

        Assert.Equal(category, result.FailureCategory);
        Assert.Equal(WorkerDispatchState.Unknown, result.DispatchState);
        AssertUnknown(result.BlockImportOutcome, "unknown");
    }

    private const string LegacyWorkerScript = """
        param([Parameter(Mandatory = $true)][string]$HelloResponseBase64)
        $null = [Console]::In.ReadLine()
        [Console]::Out.WriteLine([Text.Encoding]::UTF8.GetString(
            [Convert]::FromBase64String($HelloResponseBase64)))
        [Console]::Out.Flush()
        $null = [Console]::In.ReadLine()
        """;
}
