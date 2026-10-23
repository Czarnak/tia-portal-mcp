using System.Reflection;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Network;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Cursors;
using TiaMcpServer.Json;
using TiaMcpServer.Network;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Tests.TestSupport;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Network;

[Collection(RealWorkerProcessCollection.Name)]
public sealed class NetworkObjectPaginationCoordinatorTests
{
    private const string ProjectPath = @"C:\Projects\Demo.ap21";

    [Theory]
    [InlineData("unsigned-worker-token")]
    [InlineData("bad.token")]
    public async Task InvalidPublicCursor_IsRejectedBeforeWorkerDispatch(string cursor)
    {
        using var fixture = new Fixture();
        var item = await fixture.Coordinator.ExecuteAsync(Operation(cursor));
        Assert.Equal(WorkerFailureCategories.InvalidCursor, item.Failure!.Category);
        Assert.Empty(fixture.Requests);
        Assert.True(fixture.Client.BindingSnapshot.IsVerified);
    }

    [Theory]
    [InlineData("bindingId")]
    [InlineData("revision")]
    [InlineData("path")]
    [InlineData("worker")]
    [InlineData("generation")]
    [InlineData("pid")]
    public async Task StaleBinding_PreservesHealthyCurrentBinding(string field)
    {
        using var fixture = new Fixture(Page());
        var state = fixture.State();
        state = field switch
        {
            "bindingId" => state with { HostBinding = state.HostBinding with { BindingId = "old-binding" } },
            "revision" => state with { HostBinding = state.HostBinding with { Revision = state.HostBinding.Revision + 1 } },
            _ => state,
        };
        if (field == "path")
        {
            state = state with { HostBinding = state.HostBinding with { NormalizedProjectPath = @"C:\Projects\Other.ap21" } };
            state.SessionIdentity.ProjectPath = state.HostBinding.NormalizedProjectPath;
        }
        if (field == "worker") state.SessionIdentity.WorkerSessionId = "old-worker";
        if (field == "generation") state.SessionIdentity.SessionGeneration++;
        if (field == "pid") state.SessionIdentity.PortalProcessId++;
        var before = fixture.Client.BindingSnapshot;
        var item = await fixture.Coordinator.ExecuteAsync(Operation(fixture.Codec.Encode(state)));
        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, item.Failure!.Category);
        Assert.Empty(fixture.Requests);
        Assert.True(before.SameBinding(fixture.Client.BindingSnapshot));
        Assert.Equal(OperationBatchStatus.Succeeded, (await fixture.Coordinator.ExecuteAsync(Operation())).Status);
        Assert.Single(fixture.InventoryRequests);
    }

    [Fact]
    public async Task SameOwnerReverify_KeepsCursorUsable()
    {
        var page = Page();
        page.Warnings = new() { "page diagnostic" };
        using var fixture = new Fixture(Page(), page);
        var before = fixture.Client.BindingSnapshot;
        var cursor = fixture.Cursor();
        var status = await fixture.Client.GetProjectStatusAsync(null);
        Assert.True(status.Success, status.Error + fixture.Diagnostics);
        Assert.True(fixture.Binding.BindVerified(status.SessionIdentity, forceRebind: true, out var error), error);
        Assert.True(before.SameBinding(fixture.Client.BindingSnapshot));
        var operation = Operation(cursor);
        operation.PageSize = 2;
        var item = await fixture.Coordinator.ExecuteAsync(operation);
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(new[] { "page diagnostic" }, item.Warnings);
        var sent = Assert.Single(fixture.InventoryRequests);
        Assert.Equal("worker-next", sent.GetProperty("networkObjectCursor").GetString());
        Assert.Equal(2, sent.GetProperty("networkObjectPageSize").GetInt32());
        Assert.Equal(cursor, operation.Cursor);
    }

    [Fact]
    public async Task QueuedRebind_DoesNotDispatchOldContinuation()
    {
        using var fixture = new Fixture();
        var before = fixture.Client.BindingSnapshot;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = fixture.Client.ExecuteWithPinnedBindingAsync(before, async () =>
        {
            entered.SetResult();
            await release.Task;
            return WorkerCallResult.Ok("{}");
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = fixture.Coordinator.ExecuteAsync(Operation(fixture.Cursor()));
        Assert.False(pending.IsCompleted);
        fixture.NewEpoch();
        var after = fixture.Client.BindingSnapshot;
        release.SetResult();
        Assert.True((await blocker).Success);
        var item = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(WorkerFailureCategories.CursorBindingMismatch, item.Failure!.Category);
        Assert.Empty(fixture.Requests);
        Assert.True(after.SameBinding(fixture.Client.BindingSnapshot));
    }

    [Fact]
    public async Task Lease_PreventsBindingChangeDuringCursorIssuance()
    {
        using var fixture = new Fixture(Page());
        fixture.BlockInventory = true;
        var before = fixture.Client.BindingSnapshot;
        var read = fixture.Coordinator.ExecuteAsync(Operation());
        await fixture.WaitForInventoryAsync();
        var bindingEntered = false;
        var rebind = fixture.Client.ExecuteWithPinnedBindingAsync(before, () =>
        {
            bindingEntered = true;
            fixture.NewEpoch();
            return Task.FromResult(WorkerCallResult.Ok("{}"));
        });
        Assert.False(bindingEntered);
        Assert.False(rebind.IsCompleted);
        fixture.ReleaseInventory();
        var item = await read.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.True(fixture.Codec.Decode(item.Result!.Value.GetProperty("nextCursor").GetString()!).HostBinding.Matches(before));
        Assert.True((await rebind.WaitAsync(TimeSpan.FromSeconds(5))).Success);
        Assert.True(bindingEntered);
        Assert.False(before.SameBinding(fixture.Client.BindingSnapshot));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("terminalMissing")]
    [InlineData("worker")]
    [InlineData("generation")]
    [InlineData("pid")]
    [InlineData("path")]
    [InlineData("resolvedPath")]
    [InlineData("noncanonicalPath")]
    public async Task IncompleteIdentity_IsProtocolError(string field)
    {
        var response = Page(terminal: field == "terminalMissing");
        if (field is "missing" or "terminalMissing") response.SessionIdentity = null;
        if (field == "worker") response.SessionIdentity!.WorkerSessionId = " ";
        if (field == "generation") response.SessionIdentity!.SessionGeneration = -1;
        if (field == "pid") response.SessionIdentity!.PortalProcessId = null;
        if (field == "path") response.SessionIdentity!.ProjectPath = null;
        if (field == "resolvedPath") response.ResolvedProjectPath = @"C:\Projects\Other.ap21";
        if (field == "noncanonicalPath") response.SessionIdentity!.ProjectPath = @"C:\Projects\sub\..\Demo.ap21";
        using var fixture = new Fixture(response);
        var item = await fixture.Coordinator.ExecuteAsync(Operation(fixture.Cursor()));
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.Null(item.Result);
        Assert.Single(fixture.InventoryRequests);
        if (field is not ("resolvedPath" or "noncanonicalPath"))
            Assert.Equal(ProjectBindingSnapshot.InvalidatedState, fixture.Client.BindingSnapshot.State);
    }

    [Theory]
    [InlineData("worker", WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData("generation", WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData("pid", WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData("path", WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData("binding", WorkerFailureCategories.CursorBindingMismatch)]
    [InlineData("portal", WorkerFailureCategories.PortalLost)]
    [InlineData("timeout", WorkerFailureCategories.WorkerTimeout)]
    [InlineData("crash", WorkerFailureCategories.WorkerCrashed)]
    public async Task WorkerIdentityLoss_InvalidatesWithoutReplay(string field, string expected)
    {
        var response = Page();
        if (field == "worker") response.SessionIdentity!.WorkerSessionId = "changed-worker";
        if (field == "generation") response.SessionIdentity!.SessionGeneration++;
        if (field == "pid") response.SessionIdentity!.PortalProcessId++;
        if (field == "path") response.SessionIdentity!.ProjectPath = @"C:\Projects\Other.ap21";
        if (field is "binding" or "portal" or "timeout" or "crash")
        {
            response = new WorkerResponse
            {
                Success = false,
                FailureCategory = field == "portal" ? WorkerFailureCategories.PortalLost : WorkerFailureCategories.BindingConflict,
                Error = field,
            };
        }
        using var fixture = new Fixture(response);
        var item = await fixture.Coordinator.ExecuteAsync(Operation(fixture.Cursor()));
        Assert.Equal(expected, item.Failure!.Category);
        Assert.Equal(ProjectBindingSnapshot.InvalidatedState, fixture.Client.BindingSnapshot.State);
        Assert.Single(fixture.InventoryRequests);
    }

    [Theory]
    [InlineData(WorkerFailureCategories.CursorFilterMismatch)]
    [InlineData(WorkerFailureCategories.CursorSnapshotMismatch)]
    [InlineData(WorkerFailureCategories.CursorOutOfRange)]
    [InlineData(WorkerFailureCategories.ProtocolError)]
    public async Task WorkerContinuationFailures_KeepCategoryAndWarnings(string category)
    {
        var response = category == WorkerFailureCategories.ProtocolError ? Page() : new WorkerResponse
        {
            Success = false, FailureCategory = category, Error = "worker continuation failure",
        };
        if (category == WorkerFailureCategories.ProtocolError) response.Payload = "{\"unexpected\":\"PRIVATE\"}";
        response.Warnings = new() { "worker diagnostic" };
        using var fixture = new Fixture(response);
        var item = await fixture.Coordinator.ExecuteAsync(Operation(fixture.Cursor()));
        Assert.Equal(category, item.Failure!.Category);
        Assert.Equal(new[] { "worker diagnostic" }, item.Warnings);
        Assert.Null(item.Result);
        Assert.DoesNotContain("PRIVATE", CanonicalJson.Serialize(item));
        Assert.Single(fixture.InventoryRequests);
        Assert.True(fixture.Client.BindingSnapshot.IsVerified);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\r\n")]
    public async Task BlankWorkerCursor_IsProtocolErrorAndLaterItemStillRuns(string cursor)
    {
        var response = Page();
        response.Payload = CanonicalJson.Serialize(new NetworkObjectListInfo
        {
            TotalCount = 0, ReturnedCount = 0, NextCursor = cursor,
        });
        response.Warnings = new() { "worker page diagnostic" };
        using var fixture = new Fixture(response, Page());
        var before = fixture.Client.BindingSnapshot;

        var result = await NetworkReadTools.NetworkRead(fixture.Client, new[] { Operation(), Operation(id: "later") });

        Assert.False(result.IsError);
        var items = AssertCanonical(result).GetProperty("batch").GetProperty("operations");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("failed", items[0].GetProperty("status").GetString());
        Assert.Equal(WorkerFailureCategories.ProtocolError,
            items[0].GetProperty("failure").GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("result").ValueKind);
        Assert.Equal("worker page diagnostic", Assert.Single(items[0].GetProperty("warnings").EnumerateArray()).GetString());
        Assert.DoesNotContain("nextCursor", items[0].GetRawText());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        var nextCursor = items[1].GetProperty("result").GetProperty("nextCursor").GetString();
        Assert.Equal("worker-next", fixture.Codec.Decode(nextCursor!).WorkerCursor);
        Assert.True(before.SameBinding(fixture.Client.BindingSnapshot));
        Assert.Equal(2, fixture.InventoryRequests.Length);
    }

    [Fact]
    public async Task OversizedIssuedCursor_IsBoundedOmission()
    {
        var path = @"C:\Projects\" + new string('a', 1800) + @"\Demo.ap21";
        using var fixture = new Fixture(path, Page(path: path), Page(path: path, terminal: true));
        var expectedSize = Assert.IsType<AuthenticatedCursorSizeException>(
            Assert.Throws<CursorException>(() => fixture.Codec.Encode(fixture.State())).InnerException);
        var result = await NetworkReadTools.NetworkRead(fixture.Client, new[] { Operation(), Operation(id: "later") });
        var document = AssertCanonical(result);
        var items = document.GetProperty("batch").GetProperty("operations");
        var item = items[0];
        Assert.Equal("omitted", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("failure").ValueKind);
        var omission = item.GetProperty("omission");
        Assert.Equal("networkObjectCursorExceededCharLimit", omission.GetProperty("reason").GetString());
        Assert.Equal(4096, omission.GetProperty("limitChars").GetInt32());
        Assert.True(omission.GetProperty("originalChars").GetInt32() > 4096);
        Assert.Equal(expectedSize.CursorChars, omission.GetProperty("originalChars").GetInt32());
        Assert.Equal("network_read", omission.GetProperty("retryTool").GetString());
        Assert.Contains("Shorten the project path", omission.GetProperty("guidance").GetString());
        Assert.Contains("terminal page", omission.GetProperty("guidance").GetString());
        Assert.Equal("succeeded", items[1].GetProperty("status").GetString());
        Assert.DoesNotContain("worker-next", CanonicalJson.Serialize(document));
        Assert.Equal(2, fixture.InventoryRequests.Length);
    }

    [Fact]
    public async Task TerminalPage_DoesNotRequireCursorIssuance()
    {
        var path = @"C:\Projects\" + new string('a', 1800) + @"\Demo.ap21";
        using var fixture = new Fixture(path, Page(path: path, terminal: true));
        var result = await NetworkReadTools.NetworkRead(fixture.Client, new[] { Operation() });
        var item = AssertCanonical(result).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal("succeeded", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("result").GetProperty("nextCursor").ValueKind);
        Assert.Single(fixture.InventoryRequests);
    }

    [Fact]
    public async Task PublicRejections_KeepCanonicalRootGateBeforeCursor()
    {
        using var fixture = new Fixture();
        var badCursor = Operation("unsigned");
        var itemResult = await NetworkReadTools.NetworkRead(fixture.Client, new[] { badCursor });
        var item = AssertCanonical(itemResult).GetProperty("batch").GetProperty("operations")[0];
        Assert.Equal(WorkerFailureCategories.InvalidCursor, item.GetProperty("failure").GetProperty("category").GetString());
        Assert.True(fixture.Binding.Clear(null, out _));
        var unbound = AssertCanonical(await NetworkReadTools.NetworkRead(fixture.Client, new[] { badCursor }));
        Assert.Equal(WorkerFailureCategories.BindingConflict, unbound.GetProperty("error").GetProperty("category").GetString());
        badCursor.PageSize = 0;
        var invalid = AssertCanonical(await NetworkReadTools.NetworkRead(fixture.Client, new[] { badCursor }));
        Assert.Equal(WorkerFailureCategories.ValidationError, invalid.GetProperty("error").GetProperty("category").GetString());
        Assert.Empty(fixture.Requests);
    }

    private static JsonElement AssertCanonical(CallToolResult result)
    {
        var document = Assert.IsType<JsonElement>(result.StructuredContent);
        Assert.Equal(Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, CanonicalJson.Serialize(document));
        return document;
    }

    private static NetworkOperationRequest Operation(string? cursor = null, string id = "objects") => new()
    {
        OperationId = id, Operation = "list_network_objects", ObjectKinds = new[] { "node" }, PageSize = 1, Cursor = cursor,
    };

    private static WorkerSessionIdentity Identity(string path = ProjectPath) => new()
    {
        WorkerSessionId = "object-worker", SessionGeneration = 1, PortalProcessId = 4242, ProjectPath = path,
    };

    private static WorkerResponse Page(string path = ProjectPath, bool terminal = false) => new()
    {
        Success = true, SessionIdentity = Identity(path), ResolvedProjectPath = path,
        Payload = CanonicalJson.Serialize(new NetworkObjectListInfo { TotalCount = 0, ReturnedCount = 0, NextCursor = terminal ? null : "worker-next" }),
    };

    // Use the real client's serialized lease and IPC validation; only the native worker is scripted.
    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "network-object-cursor-" + Guid.NewGuid().ToString("N"));
        private readonly string _requestLog;
        private readonly AuthenticatedCursorProtector _protector = AuthenticatedCursorProtector.CreateProcessScoped();
        private readonly string _enteredName = "Local\\object-page-entered-" + Guid.NewGuid().ToString("N");
        private readonly string _releaseName = "Local\\object-page-release-" + Guid.NewGuid().ToString("N");
        private readonly EventWaitHandle _entered;
        private readonly EventWaitHandle _release;
        internal ProjectSessionBinding Binding { get; } = new(null);
        internal OpennessWorkerClient Client { get; }
        internal NetworkObjectPageCursorCodec Codec { get; }
        internal NetworkObjectPaginationCoordinator Coordinator { get; }
        internal bool BlockInventory { set { if (value) File.WriteAllText(Path.Combine(_directory, "block"), "1"); } }

        internal Fixture(params WorkerResponse[] responses) : this(ProjectPath, responses) { }

        internal Fixture(string path, params WorkerResponse[] responses)
        {
            Directory.CreateDirectory(_directory);
            _requestLog = Path.Combine(_directory, "requests.jsonl");
            _entered = new EventWaitHandle(false, EventResetMode.ManualReset, _enteredName);
            _release = new EventWaitHandle(false, EventResetMode.ManualReset, _releaseName);
            Assert.True(Binding.BindVerified(Identity(path), false, out var error), error);
            Client = new OpennessWorkerClient(Binding);
            Codec = new NetworkObjectPageCursorCodec(_protector);
            Coordinator = new NetworkObjectPaginationCoordinator(Client, Codec);
            var hardwareCodec = new HardwarePageCursorCodec(_protector);
            NetworkReadTools.RegisterExecutor(Client, new NetworkReadOperationExecutor(Client,
                new HardwarePaginationCoordinator(Client, hardwareCodec, new HardwarePageProjector(hardwareCodec)), Coordinator));
            var script = Path.Combine(_directory, "worker.ps1");
            File.WriteAllText(script, Script, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_directory, "responses.json"), JsonSerializer.Serialize(responses, WorkerJson.Envelope));
            var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var transport = new PersistentWorkerTransport(powershell, TimeSpan.FromSeconds(3), workerArgs:
                $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{script}\" -Directory \"{_directory}\" -Entered \"{_enteredName}\" -Release \"{_releaseName}\"");
            typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Client, transport);
        }

        internal JsonElement[] Requests => File.Exists(_requestLog)
            ? File.ReadAllLines(_requestLog).Select(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }).ToArray() : Array.Empty<JsonElement>();
        internal JsonElement[] InventoryRequests => Requests.Where(r => r.GetProperty("method").GetString() == "list_network_objects").ToArray();
        internal string Diagnostics => string.Join("\n", Requests.Select(r => r.GetRawText()))
            + (File.Exists(Path.Combine(_directory, "errors.log")) ? File.ReadAllText(Path.Combine(_directory, "errors.log")) : "");
        internal NetworkObjectPageCursorState State() => new(1, "worker-next",
            ProjectBindingCursorState.FromSnapshot(Client.BindingSnapshot), Client.BindingSnapshot.ToWorkerIdentity()!);
        internal string Cursor() => Codec.Encode(State());
        internal void NewEpoch()
        {
            var identity = Client.BindingSnapshot.ToWorkerIdentity();
            Assert.True(Binding.Clear(null, out _));
            Assert.True(Binding.BindVerified(identity, false, out var error), error);
        }
        internal async Task WaitForInventoryAsync()
            => Assert.True(await Task.Run(() => _entered.WaitOne(TimeSpan.FromSeconds(10))));
        internal void ReleaseInventory() => _release.Set();
        public void Dispose()
        {
            _release.Set();
            Client.Dispose();
            _entered.Dispose();
            _release.Dispose();
            _protector.Dispose();
            Directory.Delete(_directory, recursive: true);
        }

        private static string Script => $$"""
            param([string]$Directory, [string]$Entered, [string]$Release)
            trap { [IO.File]::AppendAllText((Join-Path $Directory 'errors.log'), ($_ | Out-String)); exit 7 }
            $responses = ConvertFrom-Json -InputObject ([IO.File]::ReadAllText((Join-Path $Directory 'responses.json')))
            $next = 0
            while (($line = [Console]::In.ReadLine()) -ne $null) {
                $request = $line | ConvertFrom-Json
                if ($request.method -eq 'hello') {
                    $response = @{ success = $true; payload = '{}'; protocolVersion = '{{WorkerProtocol.Version}}'; capabilities = @({{string.Join(",", WorkerProtocol.RequiredCapabilities.Select(c => $"'{c}'"))}}) }
                } else {
                    [IO.File]::AppendAllText((Join-Path $Directory 'requests.jsonl'), $line + [Environment]::NewLine)
                    if ($request.method -eq 'list_network_objects' -and (Test-Path (Join-Path $Directory 'block'))) {
                        [Threading.EventWaitHandle]::OpenExisting($Entered).Set() | Out-Null
                        [Threading.EventWaitHandle]::OpenExisting($Release).WaitOne() | Out-Null
                    }
                    if ($next -ge $responses.Count) { exit 2 }
                    $response = $responses[$next]
                    $next++
                    if ($response.error -eq 'crash') { exit 3 }
                    if ($response.error -eq 'timeout') { [Threading.EventWaitHandle]::OpenExisting($Release).WaitOne() | Out-Null }
                }
                [Console]::Out.WriteLine(($response | ConvertTo-Json -Compress -Depth 40))
                [Console]::Out.Flush()
            }
            """;
    }
}
