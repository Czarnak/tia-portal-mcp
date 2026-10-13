using System.Reflection;
using System.Text.Json;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Hmi;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

public class HmiWriteVerifierTests
{
    private static HmiWriteOperationRequest Tag(string id = "a", string operation = "update_tag", string name = "T")
        => new() { OperationId = id, Operation = operation, HmiName = "H", TagName = name };
    private static HmiWriteOperationRequest Alarm(string id = "a", string kind = "discrete")
        => new() { OperationId = id, Operation = "update_alarm", HmiName = "H", AlarmName = "A", AlarmKind = kind };
    private static HmiWriteOperationRequest Table(string id = "a", string operation = "update_tag_table")
        => new() { OperationId = id, Operation = operation, HmiName = "H", TableName = "Table" };
    private static HmiWriteEffect Effect(HmiWriteOperationRequest r)
    {
        var kind = r.Operation.Contains("tag_table") ? "TagTable" : r.Operation.EndsWith("alarm") ? "Alarm" : "Tag";
        return new(r.Operation, new(kind, "H", r.TagName ?? r.AlarmName ?? r.TableName!, r.AlarmKind, kind == "Tag" ? "Table" : r.TableName, r.GroupPath), [],
            r.Operation == "delete_tag_table" ? new(["T"], []) : null, null);
    }
    private static StructuredOperationItem Outcome(HmiWriteOperationRequest r, string? failure = null, HmiWriteOutcomeInfo? result = null)
        => new(r.OperationId, r.Operation, failure is null ? "succeeded" : "failed", result is null ? null : JsonDocument.Parse(WorkerJson.SerializePayload(result)).RootElement.Clone(),
            failure is null ? null : new(failure, "setter rejected"), null, null, []);
    private static Task<HmiWriteVerification> Verify(Fixture f, HmiWriteOperationRequest[] items, params StructuredOperationItem[] outcomes)
        => new HmiWriteVerifier(f.Client, items, items.ToDictionary(i => i.OperationId, Effect)).VerifyAsync(null,
            StructuredOperationBatch.FromItems(outcomes.Length == 0 ? items.Select(i => Outcome(i)).ToArray() : outcomes));

    [Fact]
    public async Task TablesUseOnlyListReadAndCheckExactNameGroupAndCount()
    {
        using var f = await Fixture.Create();
        var r = Table(); r.NewName = "Renamed";
        f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "Renamed", TagCount = 2 }] });
        var v = await Verify(f, [r]);
        Assert.True(v.Success); Assert.Contains(v.Operations, o => o.Check == "tagCount" && o.Actual == "2");
        Assert.All(f.Requests, q => Assert.Equal("hmi_list_tag_tables", q.Method));
        f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "renamed" }] });
        Assert.False((await Verify(f, [r])).Success);
    }

    [Theory]
    [InlineData("T", "DB.Flag", true)]
    [InlineData("t", "DB.Flag", false)]
    [InlineData("T", "\"DB\".\"Flag\"", false)]
    public async Task TagNameAndUnquotedPlcTagAreExact(string name, string plcTag, bool success)
    {
        using var f = await Fixture.Create(); var r = Tag(); r.PlcTag = "DB.Flag";
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = name, Table = "Table", PlcTag = plcTag } });
        Assert.Equal(success, (await Verify(f, [r])).Success);
    }

    [Fact]
    public async Task EventText_EscapedTextVerifiesEqual()
    {
        using var f = await Fixture.Create(); var r = Alarm(); r.EventText = [new("en-US", "A & B < C\nnext")]; r.InfoText = [new("pl-PL", "Opis")];
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete", EventText = HmiWriteCatalog.ToWorkerRequest(r, "H").EventText, InfoText = [new("pl-PL", "Opis"), new("en-US", "Other")] } });
        var v = await Verify(f, [r]); Assert.True(v.Success);
        Assert.Contains(v.Operations, o => o.Check == "eventText[en-US]" && o.Expected == o.Actual);
        Assert.Contains(v.Operations, o => o.Check == "infoText[pl-PL]" && o.Actual == "Opis");
        Assert.All(f.Requests, q => Assert.Null(q.HmiQuery!.Language));
    }

    [Theory]
    [InlineData("50.0", "System.Int32", "50", true)]
    [InlineData("50", "System.Double", "50", false)]
    [InlineData("50.5", "System.Double", "50.5", true)]
    [InlineData("50.5", "System.Double", "51", false)]
    public async Task ConditionValueChecksTypeAndValue(string expected, string type, string actual, bool success)
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonDocument.Parse(expected).RootElement.Clone();
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", ConditionValue = new(type, JsonDocument.Parse(actual).RootElement.Clone()) } });
        Assert.Equal(success, (await Verify(f, [r])).Success);
    }

    [Theory]
    [InlineData("HmiDiscreteAlarm", true, false)]
    [InlineData("HmiDiscreteAlarm", false, true)]
    [InlineData("HmiAnalogAlarm", true, true)]
    public async Task ValidationFiltersAlarmKindAndPreservesWarnings(string kind, bool error, bool success)
    {
        using var f = await Fixture.Create();
        f.Put("hmi_validate", new HmiValidationInfo { Page = new(0, 2000, 2, null), Scanned = 2, Findings = [new() { Name = "A", ObjectKind = kind, Results = [new() { Errors = error ? ["Openness error"] : [], Warnings = error ? [] : ["Openness warning"] }] }] });
        var v = await Verify(f, [Alarm()]); Assert.Equal(success, v.Success);
        if (kind == "HmiDiscreteAlarm") Assert.Contains(v.Operations, o => o.Message != null && o.Message.Contains(error ? "Openness error" : "Openness warning") && (success || o.Check == "postcondition_failed"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCreateRequiresAbsenceEvenWhenCleanupFailed(bool remains)
    {
        using var f = await Fixture.Create(); var r = Tag(operation: "create_tag");
        if (!remains) f.Remove("hmi_get_tag|T");
        var v = await Verify(f, [r], Outcome(r, "worker_operation_failed", new() { ObjectName = "T", Cleanup = remains ? "failed" : "deleted", CleanupMessage = "cleanup evidence" }));
        Assert.Equal(!remains, v.Success); Assert.Contains(v.Operations, o => o.Check == "absent" && o.Actual == (remains ? "present" : "absent"));
    }

    [Fact]
    public async Task DeletedTableAlsoChecksMembersAbsent()
    {
        using var f = await Fixture.Create(); f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo());
        var v = await Verify(f, [Table(operation: "delete_tag_table")]); Assert.False(v.Success);
        Assert.Contains(v.Operations, o => o.Check == "absent" && o.Actual == "present");
    }

    [Fact]
    public async Task LaterRenameTransformsEarlierAlarmTriggerExpectation()
    {
        using var f = await Fixture.Create(); var a = Alarm(); a.TriggerTag = "T.Member"; var t = Tag("rename"); t.NewName = "U";
        f.Put("hmi_get_tag|U", new HmiTagDetailInfo { Tag = new() { Name = "U", Table = "Table" } });
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete", TriggerTag = "T.Member" } });
        Assert.False((await Verify(f, [a, t])).Success);
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete", TriggerTag = "U.Member" } });
        Assert.True((await Verify(f, [a, t])).Success);
    }

    [Fact]
    public async Task LaterTableRenameTransformsEarlierTagMembership()
    {
        using var f = await Fixture.Create(); var table = Table("rename"); table.NewName = "Next";
        f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "Next" }] });
        Assert.False((await Verify(f, [Tag(), table])).Success);
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Next" } });
        Assert.True((await Verify(f, [Tag(), table])).Success);
    }

    [Fact]
    public async Task DeleteRecreateVerifiesLastIncarnation()
    {
        using var f = await Fixture.Create(); var create = Tag("first", "create_tag"); var delete = Tag("delete", "delete_tag"); var last = Tag("last", "create_tag");
        var v = await Verify(f, [create, delete, last]); Assert.True(v.Success);
        Assert.Equal(2, v.Operations.Count(o => o.Check == "superseded"));
    }

    [Fact]
    public async Task FailedUpdateReadsAppliedRenameAndReportsPartialFields()
    {
        using var f = await Fixture.Create(); var first = Tag(); first.AcquisitionCycle = "T1s";
        var last = Tag("last"); last.NewName = "U"; last.AcquisitionCycle = "T2s";
        f.Put("hmi_get_tag|U", new HmiTagDetailInfo { Tag = new() { Name = "U", Table = "Table", AcquisitionCycle = "T1s" } });
        var v = await Verify(f, [first, last], Outcome(first), Outcome(last, "worker_operation_failed", new() { ObjectName = "U", AppliedProperties = ["Name"], FailedProperty = "AcquisitionCycle" }));
        Assert.True(v.Success); Assert.Contains(v.Operations, o => o.OperationId == "a" && o.Check == "superseded");
        Assert.Contains(v.Operations, o => o.Check == "observed.acquisitionCycle" && o.Actual == "T1s");
        Assert.DoesNotContain(f.Requests, q => q.HmiQuery!.TagName == "T");
    }

    [Theory]
    [InlineData("validation_error")]
    [InlineData("target_not_found")]
    [InlineData("target_ambiguous")]
    [InlineData("state_changed")]
    [InlineData("guard_blocked")]
    [InlineData("binding_conflict")]
    [InlineData("access_denied")]
    public async Task NeverRanDoesNotSupersedeEarlierSuccess(string category)
    {
        using var f = await Fixture.Create(); var first = Tag(); first.NewName = "U"; var last = Tag("last", name: "U"); last.NewName = "V";
        f.Put("hmi_get_tag|U", new HmiTagDetailInfo { Tag = new() { Name = "wrong", Table = "Table" } });
        var v = await Verify(f, [first, last], Outcome(first), Outcome(last, category));
        Assert.False(v.Success); Assert.DoesNotContain(v.Operations, o => o.Check == "superseded");
        Assert.Contains(v.Operations, o => o.OperationId == "last" && o.Check == "skipped");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    public async Task InvalidTypedPayloadFailsClosed(string payload)
    {
        using var f = await Fixture.Create(); f.PutRaw("hmi_get_tag|T", payload);
        Assert.False((await Verify(f, [Tag()])).Success);
    }

    [Fact]
    public async Task DiscreteAndAnalogSameNameAreIndependent()
    {
        using var f = await Fixture.Create(); var discrete = Alarm("d"); var analog = Alarm("n", "analog");
        f.Put("hmi_get_alarm|A|discrete", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete" } });
        f.Put("hmi_get_alarm|A|analog", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog" } });
        var v = await Verify(f, [discrete, analog]); Assert.True(v.Success);
        Assert.DoesNotContain(v.Operations, o => o.Check == "superseded");
    }

    [Theory]
    [InlineData("comment")]
    [InlineData("displayName")]
    public async Task EachWrittenTagCultureIsComparedAsScalar(string field)
    {
        using var f = await Fixture.Create(); var r = Tag();
        if (field == "comment") r.Comment = [new("en-US", "Expected"), new("pl-PL", "Opis")];
        else r.DisplayName = [new("en-US", "Expected"), new("pl-PL", "Opis")];
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", Comment = [new("en-US", "Wrong"), new("pl-PL", "Opis")], DisplayName = [new("en-US", "Wrong"), new("pl-PL", "Opis")] } });
        var v = await Verify(f, [r]); Assert.False(v.Success);
        Assert.Contains(v.Operations, o => o.Check == field + "[en-US]" && o.Expected == "Expected" && o.Actual == "Wrong");
        Assert.Contains(v.Operations, o => o.Check == field + "[pl-PL]" && o.Success && o.Actual == "Opis");
    }

    [Theory]
    [InlineData("delete_tag", false)]
    [InlineData("delete_alarm", false)]
    [InlineData("delete_tag", true)]
    [InlineData("delete_alarm", true)]
    public async Task DeletesCheckAbsence(string operation, bool remains)
    {
        using var f = await Fixture.Create(); var r = operation == "delete_tag" ? Tag() : Alarm(); r.Operation = operation;
        if (!remains) f.Remove(operation == "delete_tag" ? "hmi_get_tag|T" : "hmi_get_alarm|A");
        Assert.Equal(!remains, (await Verify(f, [r])).Success);
    }

    [Fact]
    public async Task SupersededUpdateStillChecksEarlierUnchangedFields()
    {
        using var f = await Fixture.Create(); var first = Tag(); first.AcquisitionCycle = "T1s"; var last = Tag("last"); last.DisplayName = [new("en-US", "Text")];
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", AcquisitionCycle = "T2s", DisplayName = last.DisplayName } });
        var v = await Verify(f, [first, last]); Assert.False(v.Success);
        Assert.Contains(v.Operations, o => o.OperationId == "last" && o.Check == "acquisitionCycle" && !o.Success);
    }

    [Fact]
    public async Task FailedAppliedConditionValueRetainsTypedExpectation()
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonSerializer.SerializeToElement(50);
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", ConditionValue = new("System.Double", JsonSerializer.SerializeToElement(50)) } });
        var v = await Verify(f, [r], Outcome(r, "worker_operation_failed", new() { ObjectName = "A", AppliedProperties = ["ConditionValue"], FailedProperty = "Priority" }));
        Assert.False(v.Success);
    }

    [Fact]
    public async Task FailedDeleteReportsObservedPresence()
    {
        using var f = await Fixture.Create(); var r = Tag(operation: "delete_tag");
        var v = await Verify(f, [r], Outcome(r, "worker_operation_failed", new() { ObjectName = "T", FailedProperty = "Delete" }));
        Assert.Contains(v.Operations, o => o.Check == "observed" && o.Actual == "present");
    }

    [Fact]
    public async Task ValidationWithoutScannedTargetFailsClosed()
    {
        using var f = await Fixture.Create(); f.Put("hmi_validate", new HmiValidationInfo { Page = new(0, 2000, 0, null), Scanned = 0 });
        Assert.False((await Verify(f, [Tag()])).Success);
    }

    [Fact]
    public async Task NumericReadbackUsesValueNotJsonNumberSpelling()
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonSerializer.SerializeToElement(50.5);
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", ConditionValue = new("System.Double", JsonDocument.Parse("50.50").RootElement.Clone()) } });
        Assert.True((await Verify(f, [r])).Success);
    }

    [Fact]
    public async Task CultureIdentityIsCaseInsensitiveWhileTextRemainsExact()
    {
        using var f = await Fixture.Create(); var r = Tag(); r.Comment = [new("en-us", "Text")];
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", Comment = [new("en-US", "Text")] } });
        Assert.True((await Verify(f, [r])).Success);
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", Comment = [new("en-US", "text")] } });
        Assert.False((await Verify(f, [r])).Success);
    }

    [Fact]
    public async Task LaterCultureWriteSupersedesDifferentCultureSpelling()
    {
        using var f = await Fixture.Create(); var first = Tag(); first.Comment = [new("en-US", "Before")];
        var last = Tag("last"); last.Comment = [new("en-us", "After")];
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", Comment = [new("en-US", "After")] } });
        Assert.True((await Verify(f, [first, last])).Success);
    }
    // A scripted stdio peer exercises the real client without loading or launching Siemens code.
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hmi-verifier-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> payloads = new();
        public OpennessWorkerClient Client { get; }
        public WorkerRequest[] Requests => File.ReadAllLines(Path.Combine(directory, "requests")).Select(l => JsonSerializer.Deserialize<WorkerRequest>(l, WorkerJson.Request)!).Where(r => r.Method is not ("get_project_status" or "hello")).ToArray();
        private Fixture()
        {
            Directory.CreateDirectory(directory);
            Client = new(new ProjectSessionBinding(null));
            var script = """
                param([string]$Root)
                while (($line = [Console]::In.ReadLine()) -ne $null) {
                    $r = $line | ConvertFrom-Json
                    [IO.File]::AppendAllText((Join-Path $Root 'requests'), $line + [Environment]::NewLine)
                    $envelope = Get-Content -Raw (Join-Path $Root 'envelope') | ConvertFrom-Json
                    if ($r.method -eq 'hello') { $envelope = Get-Content -Raw (Join-Path $Root 'hello') | ConvertFrom-Json }
                    elseif ($r.method -ne 'get_project_status') {
                        $data = Get-Content -Raw (Join-Path $Root 'payloads') | ConvertFrom-Json
                        $key = $r.method
                        if ($r.hmiQuery.tagName) { $key += '|' + $r.hmiQuery.tagName }
                        elseif ($r.hmiQuery.alarmName) { $key += '|' + $r.hmiQuery.alarmName; if ($data.PSObject.Properties[$key + '|' + $r.hmiQuery.alarmKind]) { $key += '|' + $r.hmiQuery.alarmKind } }
                        elseif ($r.hmiQuery.offset -gt 0) { $key += '|' + $r.hmiQuery.offset }
                        $value = $data.PSObject.Properties[$key]
                        if (!$value) { $value = $data.PSObject.Properties[$r.method] }
                        if ($value) { $envelope.payload = $value.Value }
                        else { $envelope.success = $false; $envelope | Add-Member failureCategory 'target_not_found'; $envelope | Add-Member error 'No fixture target' }
                    }
                    [Console]::Out.WriteLine(($envelope | ConvertTo-Json -Compress -Depth 30)); [Console]::Out.Flush()
                }
                """;
            File.WriteAllText(Path.Combine(directory, "worker.ps1"), script);
            var identity = new WorkerSessionIdentity { WorkerSessionId = "planner", SessionGeneration = 1, PortalProcessId = 4242, ProjectPath = Path.Combine(directory, "fixture.ap21") };
            File.WriteAllText(Path.Combine(directory, "envelope"), JsonSerializer.Serialize(new WorkerResponse { Success = true, Payload = "{}", SessionIdentity = identity, ResolvedProjectPath = identity.ProjectPath }, WorkerJson.Envelope));
            File.WriteAllText(Path.Combine(directory, "hello"), JsonSerializer.Serialize(new WorkerResponse { Success = true, ProtocolVersion = WorkerProtocol.Version, Capabilities = WorkerProtocol.RequiredCapabilities.ToList() }, WorkerJson.Envelope));
            var transport = new PersistentWorkerTransport(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"), TimeSpan.FromSeconds(15), workerArgs: $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{Path.Combine(directory, "worker.ps1")}\" -Root \"{directory}\"");
            typeof(OpennessWorkerClient).GetField("_transport", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(Client, transport);
            Put("hmi_list_hmi_devices", new HmiDeviceListInfo { Devices = [new() { DeviceName = "Device", SoftwareName = "H", Kind = "unified" }] });
            Put("hmi_list_project_languages", new HmiProjectLanguagesInfo { Languages = ["en-US"] });
            Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = "Table", TagCount = 1 }] });
            Put("hmi_list_tags", new HmiTagListInfo { Tags = [new HmiTagRowInfo { Name = "T", Table = "Table" }], Page = new(0, 2000, 1, null) });
            Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new HmiTagRowInfo { Name = "T", Table = "Table" } });
            Put("hmi_list_connections", new HmiConnectionListInfo { Connections = [new() { Name = "Connection" }] });
            Put("hmi_list_alarm_classes", new HmiAlarmClassesInfo { AlarmClasses = [new() { Name = "Class" }] });
            Put("hmi_list_alarms", new HmiAlarmListInfo { Page = new(0, 2000, 0, null) });
            Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete" } });
            Put("hmi_validate", new HmiValidationInfo { Page = new(0, 2000, 1, null), Scanned = 1, Clean = 1 });
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); var status = await f.Client.GetProjectStatusAsync(null);
            Assert.True(status.Success, status.Error); return f;
        }
        public void Put<T>(string key, T payload) => PutRaw(key, WorkerJson.SerializePayload(payload));
        public void PutRaw(string key, string payload) { payloads[key] = payload; File.WriteAllText(Path.Combine(directory, "payloads"), JsonSerializer.Serialize(payloads)); }
        public void Remove(string key) { payloads.Remove(key); File.WriteAllText(Path.Combine(directory, "payloads"), JsonSerializer.Serialize(payloads)); }
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
