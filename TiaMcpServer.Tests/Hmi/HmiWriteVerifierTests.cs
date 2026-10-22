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
        r.GroupPath = "Outer/Inner";
        f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Groups = [new() { Name = "Outer", Groups = [new() { Name = "Inner", Tables = [new() { Name = "Renamed", TagCount = 2 }] }] }] });
        Assert.True((await Verify(f, [r])).Success);
        r.GroupPath = "Outer/Other";
        var mismatch = await Verify(f, [r]);
        Assert.Contains(mismatch.Operations, o => !o.Success && o.Check == "groupPath" && o.Expected == "Outer/Other" && o.Actual == "Outer/Inner");
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

    [Theory]
    [InlineData("create_tag")]
    [InlineData("update_tag")]
    public async Task QuotedPlcTagUsesUnquotedEffectAndVerificationWithoutChangingSetterInput(string operation)
    {
        using var f = await Fixture.Create();
        var r = Tag(operation: operation); r.TableName = operation == "create_tag" ? "Table" : null;
        r.Connection = "PLC"; r.PlcTag = "\"DB\".Member";
        var state = HmiWorkingState.Create(new HmiEvidence
        {
            HmiName = "H", Complete = true, Connections = ["PLC"], Tables = [new("Table", null)],
            Tags = operation == "create_tag" ? [] : [new() { Name = "T", Table = "Table", Connection = "PLC" }]
        });
        var effect = state.Resolve(r).Effect!;
        var canonical = JsonDocument.Parse(TiaMcpServer.Json.CanonicalJson.Serialize(effect)).RootElement;
        Assert.Equal("DB.Member", canonical.GetProperty("changes").EnumerateArray().Single(c => c.GetProperty("field").GetString() == "plcTag").GetProperty("requested").GetString());
        Assert.Equal("\"DB\".Member", HmiWriteCatalog.ToWorkerRequest(r, "H").PlcTag);
        f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", Connection = "PLC", PlcTag = "DB.Member" } });
        var verification = await Verify(f, [r]);
        Assert.True(verification.Success);
        Assert.Contains(verification.Operations, c => c.Check == "plcTag" && c.Expected == "DB.Member" && c.Actual == "DB.Member" && c.Success);
    }

    [Theory]
    [InlineData("create_alarm", "discrete", false)]
    [InlineData("create_alarm", "analog", true)]
    [InlineData("update_alarm", "discrete", true)]
    [InlineData("update_alarm", "analog", false)]
    public async Task EventText_IndependentReadbackVerifiesDirectAndInheritedExpectations(string operation, string kind, bool inherited)
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: kind); r.Operation = operation;
        r.EventText = [new("en-US", "A < & > \"quoted\" 'apostrophe' &quot; &apos;"), new("pl-PL", "Próba żółć < & > \"tekst\"")];
        r.InfoText = [new("pl-PL", "Opis")];
        var last = Alarm("last", kind); last.Priority = 7;
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = kind, Priority = 7,
            EventText = [new("en-US", "<body><p>A &lt; &amp; &gt; \"quoted\" 'apostrophe' &amp;quot; &amp;apos;</p></body>"), new("pl-PL", "<body><p>Próba żółć &lt; &amp; &gt; \"tekst\"</p></body>")],
            InfoText = [new("pl-PL", "Opis"), new("en-US", "Other")] } });
        var v = await Verify(f, inherited ? [r, last] : [r]); Assert.True(v.Success);
        Assert.Contains(v.Operations, o => o.Check == "eventText[en-US]" && o.Expected == o.Actual);
        Assert.Contains(v.Operations, o => o.Check == "eventText[pl-PL]" && o.Success && o.OperationId == (inherited ? "last" : "a"));
        Assert.Contains(v.Operations, o => o.Check == "infoText[pl-PL]" && o.Actual == "Opis");
        Assert.All(f.Requests, q => Assert.Null(q.HmiQuery!.Language));
    }

    [Theory]
    [InlineData("en-US", "<body><p>changed &amp;quot;</p></body>")]
    [InlineData("en-US", "<body><p>Text &quot;</p></body>")]
    [InlineData("en-US", "<body><p>Text &amp;quot;</p><p>extra</p></body>")]
    [InlineData("pl-PL", "<body><p>Text &amp;quot;</p></body>")]
    [InlineData("en-US", null)]
    public async Task EventText_ChangedTextStructureOrCultureFails(string culture, string? actual)
    {
        using var f = await Fixture.Create(); var r = Alarm(); r.EventText = [new("en-US", "Text &quot;")];
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete",
            EventText = actual is null ? [] : [new(culture, actual)] } });
        var v = await Verify(f, [r]);
        Assert.False(v.Success);
        Assert.Contains(v.Operations, o => o.Check == "eventText[en-US]" && !o.Success);
    }

    [Theory]
    [InlineData("50.0", "System.Int32", "50", true)]
    [InlineData("50", "System.Double", "50", true)]
    [InlineData("50.0", "System.Double", "50.00", true)]
    [InlineData("-2147483648", "System.Double", "-2147483648", true)]
    [InlineData("2147483647", "System.Double", "2147483647", true)]
    [InlineData("50", "System.Double", "51", false)]
    [InlineData("50", "System.Double", "50.5", false)]
    [InlineData("50", "System.String", "50", false)]
    [InlineData("50", "System.Decimal", "50", false)]
    [InlineData("50", "System.Single", "50", false)]
    [InlineData("50.000000000000000001", "System.Int32", "50", false)]
    [InlineData("50.5", "System.Double", "50.5", true)]
    [InlineData("50.5", "System.Double", "51", false)]
    public async Task ConditionValueChecksTypeAndValue(string expected, string type, string actual, bool success)
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonDocument.Parse(expected).RootElement.Clone();
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", ConditionValue = new(type, JsonDocument.Parse(actual).RootElement.Clone()) } });
        var v = await Verify(f, [r]);
        Assert.Equal(success, v.Success);
        if (expected == "50" && type == "System.Double")
        {
            var check = Assert.Single(v.Operations, o => o.Check == "conditionValue.type");
            Assert.Equal("System.Int32", check.Expected);
            Assert.Equal("System.Double", check.Actual);
            Assert.Equal(success, check.Success);
            if (success) Assert.Contains("lossless", check.Message);
        }
    }

    [Theory]
    [InlineData("\"50\"")]
    [InlineData("\"NaN\"")]
    [InlineData("1e400")]
    [InlineData("null")]
    public async Task ConditionValue_InvalidNumericReadbackFailsClosed(string actual)
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonSerializer.SerializeToElement(50);
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog",
            ConditionValue = new("System.Double", JsonDocument.Parse(actual).RootElement.Clone()) } });
        Assert.False((await Verify(f, [r])).Success);
    }

    [Fact]
    public async Task ConditionValue_NormalizationDoesNotHideValidationErrors()
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonSerializer.SerializeToElement(50);
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", ConditionValue = new("System.Double", JsonSerializer.SerializeToElement(50)) } });
        f.Put("hmi_validate", new HmiValidationInfo { Page = new(0, 2000, 1, null), Scanned = 1,
            Findings = [new() { Name = "A", ObjectKind = "HmiAnalogAlarm", Results = [new() { Errors = ["Invalid trigger"] }] }] });
        var v = await Verify(f, [r]);
        Assert.False(v.Success);
        Assert.Contains(v.Operations, o => o.Check == "conditionValue.type" && o.Success);
        Assert.Contains(v.Operations, o => o.Check == "postcondition_failed" && !o.Success && o.Message == "Invalid trigger");
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

    [Theory]
    [InlineData("System.Double", 50, true)]
    [InlineData("System.Double", 51, false)]
    [InlineData("System.Decimal", 50, false)]
    public async Task FailedAppliedConditionValueRetainsTypedExpectation(string type, int actual, bool success)
    {
        using var f = await Fixture.Create(); var r = Alarm(kind: "analog"); r.ConditionValue = JsonSerializer.SerializeToElement(50); r.Priority = 7;
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", ConditionValue = new(type, JsonSerializer.SerializeToElement(actual)) } });
        var batch = StructuredOperationBatch.FromItems([Outcome(r, "worker_operation_failed", new() { ObjectName = "A", AppliedProperties = ["ConditionValue"], FailedProperty = "Priority" })]);
        var v = await new HmiWriteVerifier(f.Client, [r], new Dictionary<string, HmiWriteEffect> { [r.OperationId] = Effect(r) }).VerifyAsync(null, batch);
        Assert.Equal(success, v.Success);
        Assert.Contains(v.Operations, o => o.Check == "conditionValue.type" && o.Expected == "System.Int32" && o.Actual == type && o.Success == success);
        Assert.Contains(v.Operations, o => o.Check == "observed.priority");
        Assert.False(batch.IsFullySuccessful);
        Assert.Equal("worker_operation_failed", Assert.Single(batch.Operations).Failure!.Category);
    }

    [Theory]
    [InlineData(false, 51, true)]
    [InlineData(false, 50, false)]
    [InlineData(true, 50, true)]
    [InlineData(true, 51, false)]
    public async Task ConditionValue_LastAppliedExpectationSurvivesSupersessionAndFailure(bool failed, int actual, bool success)
    {
        using var f = await Fixture.Create(); var first = Alarm(kind: "analog"); first.ConditionValue = JsonSerializer.SerializeToElement(50);
        var last = Alarm("last", "analog"); last.ConditionValue = JsonSerializer.SerializeToElement(51); last.Condition = "UpperLimit";
        f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "analog", Condition = "UpperLimit", ConditionValue = new("System.Double", JsonSerializer.SerializeToElement(actual)) } });
        var final = failed ? Outcome(last, "worker_operation_failed", new() { ObjectName = "A", AppliedProperties = ["Condition"], FailedProperty = "ConditionValue" }) : Outcome(last);
        var v = await Verify(f, [first, last], Outcome(first), final);
        Assert.Equal(success, v.Success);
        Assert.Contains(v.Operations, o => o.OperationId == "a" && o.Check == "superseded");
        Assert.Contains(v.Operations, o => o.OperationId == "last" && o.Check == "conditionValue" && o.Expected == (failed ? "50" : "51") && o.Success == success);
        Assert.Contains(v.Operations, o => o.OperationId == "last" && o.Check == "conditionValue.type" && o.Expected == "System.Int32" && o.Actual == "System.Double" && o.Success == success);
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

    [Theory]
    [InlineData("T.Member", false)]
    [InlineData("U.Member", true)]
    [InlineData(null, true)]
    public async Task FailedDeleteRetainsTransformedTriggerWhenAlarmSurvives(string? trigger, bool success)
    {
        using var f = await Fixture.Create(); var alarm = Alarm(); alarm.TriggerTag = "T.Member";
        var rename = Tag("rename"); rename.NewName = "U";
        var delete = Alarm("delete"); delete.Operation = "delete_alarm";
        f.Put("hmi_get_tag|U", new HmiTagDetailInfo { Tag = new() { Name = "U", Table = "Table" } });
        if (trigger is null) f.Remove("hmi_get_alarm|A");
        else f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = "discrete", TriggerTag = trigger } });
        var v = await Verify(f, [alarm, rename, delete], Outcome(alarm), Outcome(rename),
            Outcome(delete, "worker_operation_failed", new() { ObjectName = "A", FailedProperty = "Delete" }));
        Assert.Equal(success, v.Success);
        Assert.Contains(v.Operations, o => o.OperationId == "delete" && o.Check == "observed" && o.Actual == (trigger is null ? "absent" : "present"));
        if (trigger is not null) Assert.Contains(v.Operations, o => o.OperationId == "delete" && o.Check == "triggerTag" && o.Expected == "U.Member" && o.Actual == trigger);
    }

    [Theory]
    [InlineData("Tag", false)]
    [InlineData("Tag", true)]
    [InlineData("discrete", false)]
    [InlineData("discrete", true)]
    [InlineData("analog", false)]
    [InlineData("analog", true)]
    [InlineData("TagTable", false)]
    public async Task AppliedRenameSupersedesDeletedDestinationAndKeepsSourceFields(string kind, bool failed)
    {
        using var f = await Fixture.Create();
        var delete = kind == "Tag" ? Tag("delete", "delete_tag") : kind == "TagTable" ? Table("delete", "delete_tag_table") : Alarm("delete", kind);
        if (kind is "discrete" or "analog") delete.Operation = "delete_alarm";
        var prior = kind == "Tag" ? Tag("prior", name: "Source") : kind == "TagTable" ? Table("prior") : Alarm("prior", kind);
        var rename = kind == "Tag" ? Tag("rename", name: "Source") : kind == "TagTable" ? Table("rename") : Alarm("rename", kind);
        if (kind == "Tag") { prior.AcquisitionCycle = "T1s"; rename.NewName = "T"; }
        else if (kind == "TagTable") { prior.TableName = rename.TableName = "Source"; rename.NewName = "Table"; f.Remove("hmi_get_tag|T"); }
        else { prior.AlarmName = rename.AlarmName = "Source"; prior.Priority = 7; rename.NewName = "A"; }
        void Readback(bool correct)
        {
            if (kind == "Tag") f.Put("hmi_get_tag|T", new HmiTagDetailInfo { Tag = new() { Name = "T", Table = "Table", AcquisitionCycle = correct ? "T1s" : "T2s" } });
            else if (kind == "TagTable") f.Put("hmi_list_tag_tables", new HmiTagTableTreeInfo { Tables = [new() { Name = correct ? "Table" : "table", TagCount = 2 }] });
            else f.Put("hmi_get_alarm|A", new HmiAlarmDetailInfo { Alarm = new() { Name = "A", Kind = kind, Priority = correct ? 7 : 8 } });
        }
        if (failed) { if (kind == "Tag") rename.AcquisitionCycle = "T2s"; else rename.Priority = 8; }
        var final = failed ? Outcome(rename, "worker_operation_failed", new() { ObjectName = rename.NewName!, AppliedProperties = ["Name"], FailedProperty = kind == "Tag" ? "AcquisitionCycle" : "Priority" }) : Outcome(rename);
        Readback(true);
        var v = await Verify(f, [delete, prior, rename], Outcome(delete), Outcome(prior), final);
        Assert.True(v.Success);
        Assert.Contains(v.Operations, o => o.OperationId == "prior" && o.Check == "superseded");
        Assert.DoesNotContain(v.Operations, o => !o.Success && o.OperationId == "delete");
        if (kind != "TagTable") Assert.Contains(v.Operations, o => o.OperationId == "delete" && o.Check == "superseded");
        Readback(false);
        Assert.False((await Verify(f, [delete, prior, rename], Outcome(delete), Outcome(prior), final)).Success);
    }

    [Fact]
    public async Task RejectedRenameDoesNotSupersedeDestinationAbsence()
    {
        using var f = await Fixture.Create(); var delete = Tag("delete", "delete_tag"); var rename = Tag("rename", name: "Source"); rename.NewName = "T";
        f.Remove("hmi_get_tag|T");
        f.Put("hmi_get_tag|Source", new HmiTagDetailInfo { Tag = new() { Name = "Source", Table = "Table" } });
        var v = await Verify(f, [delete, rename], Outcome(delete), Outcome(rename, "worker_operation_failed", new() { ObjectName = "T", FailedProperty = "Name" }));
        Assert.True(v.Success);
        Assert.Contains(v.Operations, o => o.OperationId == "delete" && o.Check == "absent");
    }
    // A scripted stdio peer exercises the real client without loading or launching Siemens code.
    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "hmi-verifier-" + Guid.NewGuid().ToString("N"));
        private readonly Dictionary<string, string> payloads = new();
        private readonly ProjectSessionBinding binding;
        public OpennessWorkerClient Client { get; }
        public WorkerRequest[] Requests => File.ReadAllLines(Path.Combine(directory, "requests")).Select(l => JsonSerializer.Deserialize<WorkerRequest>(l, WorkerJson.Request)!).Where(r => r.Method is not ("get_project_status" or "hello")).ToArray();
        private Fixture()
        {
            Directory.CreateDirectory(directory);
            binding = new ProjectSessionBinding(null); Client = new(binding);
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
            Assert.True(status.Success, status.Error);
            Assert.True(f.binding.BindVerified(status.SessionIdentity!, forceRebind: false, out var bindError), bindError); return f;
        }
        public void Put<T>(string key, T payload) => PutRaw(key, WorkerJson.SerializePayload(payload));
        public void PutRaw(string key, string payload) { payloads[key] = payload; File.WriteAllText(Path.Combine(directory, "payloads"), JsonSerializer.Serialize(payloads)); }
        public void Remove(string key) { payloads.Remove(key); File.WriteAllText(Path.Combine(directory, "payloads"), JsonSerializer.Serialize(payloads)); }
        public void Dispose() { Client.Dispose(); Directory.Delete(directory, true); }
    }
}
