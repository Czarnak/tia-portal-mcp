using System.Text.Json;
using System.Text.Json.Nodes;
using TiaMcpServer.Contracts.Hmi;
using TiaMcpServer.Contracts.Json;
using TiaMcpServer.Contracts.Project;
using TiaMcpServer.Cursors;
using TiaMcpServer.Contracts.Worker;
using TiaMcpServer.Hmi;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>Exercises the mapped-type decode path of <see cref="HmiPayloadContract"/> for operations 1 and 16-19.</summary>
public class HmiPayloadContractDecodeTests
{
    [Theory]
    [InlineData("list_tags", "tags", "displayName")]
    [InlineData("list_tags", "tags", "plcName")]
    [InlineData("list_alarms", "alarms", "triggerBitAddress")]
    [InlineData("list_alarms", "alarms", "triggerAddress")]
    public void AdditiveRowEvidenceMembersAreRequired(string operation, string rows, string member)
    {
        object payload = operation == "list_tags"
            ? new HmiTagListInfo { Tags = { new HmiTagRowInfo() } }
            : new HmiAlarmListInfo { Alarms = { new HmiAlarmRowInfo() } };
        var node = JsonNode.Parse(WorkerJson.SerializePayload(payload))!;
        Assert.True(node[rows]![0]!.AsObject().Remove(member));

        var item = HmiPayloadContract.Project(Op(operation), WorkerCallResult.Ok(node.ToJsonString()));

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AdditiveRowEvidenceDecodesWithValuesAndExplicitNulls(bool populated)
    {
        var tags = new HmiTagListInfo { Tags = { new HmiTagRowInfo
        {
            PlcName = populated ? "PLC_1" : null,
            DisplayName = populated ? new List<HmiText> { new("en-US", "Temperature") } : null,
        } } };
        var alarms = new HmiAlarmListInfo { Alarms = { new HmiAlarmRowInfo
        {
            TriggerBitAddress = populated ? "T1.x3" : null,
            TriggerAddress = populated ? "T1" : null,
        } } };
        var tagItem = HmiPayloadContract.Project(Op("list_tags"), WorkerCallResult.Ok(WorkerJson.SerializePayload(tags)));
        var alarmItem = HmiPayloadContract.Project(Op("list_alarms"), WorkerCallResult.Ok(WorkerJson.SerializePayload(alarms)));

        Assert.Equal(OperationBatchStatus.Succeeded, tagItem.Status);
        Assert.Equal(OperationBatchStatus.Succeeded, alarmItem.Status);
        var tag = tagItem.Result!.Value.GetProperty("tags")[0];
        var alarm = alarmItem.Result!.Value.GetProperty("alarms")[0];
        Assert.Equal(populated ? JsonValueKind.String : JsonValueKind.Null, tag.GetProperty("plcName").ValueKind);
        Assert.Equal(populated ? JsonValueKind.Array : JsonValueKind.Null, tag.GetProperty("displayName").ValueKind);
        Assert.Equal(populated ? JsonValueKind.String : JsonValueKind.Null, alarm.GetProperty("triggerBitAddress").ValueKind);
        Assert.Equal(populated ? JsonValueKind.String : JsonValueKind.Null, alarm.GetProperty("triggerAddress").ValueKind);
    }

    private static HmiOperationRequest Op(string operation) => new() { OperationId = "a", Operation = operation };

    public static TheoryData<string, object> Payloads => new()
    {
        { "list_hmi_devices", new HmiDeviceListInfo
            { Devices = { new HmiDeviceInfo { DeviceName = "P", SoftwareName = "P_RT", Kind = "unified", TypeIdentifier = null } } } },
        { "get_runtime_settings", new HmiRuntimeSettingsInfo
            {
                General = { Values = { new HmiSettingValue { Name = "StartScreen", Value = "Start" }, new HmiSettingValue { Name = "GMPEnabled", Value = null } } },
                LanguageAndFonts = { new HmiLanguageAndFontInfo { Language = "en-US", Order = 1, Enable = true } },
                UnifiedTags = new HmiRuntimeSectionInfo(),
                Messages = { "HmiReportingSettings is not available on this device." },
            } },
        { "list_script_modules", new HmiScriptModuleListInfo { Modules = { "m1" } } },
        { "list_text_and_graphic_lists", new HmiTextAndGraphicListsInfo { TextLists = { "t" }, GraphicLists = { "g" }, SystemTextLists = { "s" } } },
        { "list_project_languages", new HmiProjectLanguagesInfo { Languages = { "en-US" }, EditingLanguage = "en-US", ReferenceLanguage = null } },
        { "list_tag_tables", new HmiTagTableTreeInfo
            {
                Tables = { new HmiTagTableInfo { Name = "t", TagCount = 2 } },
                Groups = { new HmiTagTableGroupInfo { Name = "g", Groups = { new HmiTagTableGroupInfo { Name = "h" } } } },
            } },
        { "list_tags", new HmiTagListInfo
            {
                Page = new HmiPage(0, 100, 1, null),
                Tags = { new HmiTagRowInfo { Name = "a", Comment = { new HmiText("en-US", "c") } } },
            } },
        { "get_tag", new HmiTagDetailInfo
            {
                Tag = new HmiTagRowInfo { Name = "a" },
                Members = { new HmiTagMemberInfo { Tag = new HmiTagRowInfo { Name = "m" }, Members = { new HmiTagMemberInfo() } } },
                UpperRange = new HmiRangeInfo { Value = new HmiVariant("System.String", JsonDocument.Parse("\"\"").RootElement.Clone()) },
                InitialValue = new HmiVariant(null, null),
                HmiEndValue = new HmiVariant("System.Double", JsonDocument.Parse("100").RootElement.Clone()),
                Thresholds = { new HmiThresholdInfo { Name = "Hi" } },
                LoggingTags = { new HmiLoggingTagRowInfo { Name = "l", Tag = "a" } },
            } },
        { "list_system_tags", new HmiSystemTagListInfo
            { Page = new HmiPage(0, 100, 1, null), SystemTags = { new HmiSystemTagInfo { Name = "@s", DataType = "Int" } } } },
        { "list_logging_tags", new HmiLoggingTagListInfo
            { Page = new HmiPage(0, 100, 1, null), LoggingTags = { new HmiLoggingTagRowInfo { Name = "l", Tag = "a" } } } },
        { "list_connections", new HmiConnectionListInfo
            {
                Connections =
                {
                    new HmiConnectionInfo
                    {
                        Name = "c",
                        InitialAddress = new HmiInitialAddressInfo { Raw = "A=1;", Parsed = { ["A"] = "1" } },
                        DriverProperties = { new HmiDriverPropertyInfo { PropertyName = "p", Value = null } },
                    },
                    new HmiConnectionInfo { Name = "d" },
                },
            } },
        { "list_alarms", new HmiAlarmListInfo
            {
                Page = new HmiPage(0, 100, 2, null),
                Alarms =
                {
                    new HmiAlarmRowInfo { Kind = "discrete", Name = "a", EventText = new List<HmiText> { new HmiText("en-US", "<body><p>x</p></body>") } },
                    new HmiAlarmRowInfo { Kind = "analog", Name = "b", ConditionValue = new HmiVariant("System.Double", JsonDocument.Parse("1.5").RootElement.Clone()), EventText = null },
                },
            } },
        { "get_alarm", new HmiAlarmDetailInfo
            { Alarm = new HmiAlarmRowInfo { Kind = "discrete", Name = "a" }, EventText9 = null, AlarmParameterTags = new List<string> { "p1", "" } } },
        { "list_alarm_classes", new HmiAlarmClassesInfo
            {
                AlarmClasses = { new HmiAlarmClassInfo { Name = "c", Id = 1 } },
                AuditClasses = { new HmiAuditClassInfo { Name = "a" } },
                OpcUaAlarmTypes = { new HmiOpcUaAlarmTypeInfo { Name = "u" } },
            } },
        { "list_logs", new HmiLogsInfo
            {
                DataLogs =
                {
                    new HmiLogInfo
                    {
                        Name = "d",
                        Settings = new HmiLogSettingsInfo { LogMaxSize = 10, LogTimePeriod = new HmiDurationInfo { Days = 1 }, StorageDevice = "Local", StorageFolder = null },
                        Segment = new HmiLogSegmentInfo { SegmentStartTime = "2026-01-01T00:00:00.0000000" },
                        Backup = new HmiLogBackupInfo { BackupMode = "NoBackup" },
                    },
                },
                AlarmLogs = { new HmiLogInfo { Name = "a" } },
            } },
        { "list_screens", new HmiScreenTreeInfo
            {
                Screens = { new HmiScreenInfo { Name = "s", DisplayName = { new HmiText("en-US", "S") }, ScreenNumber = 1, ItemCount = 2 } },
                Groups = { new HmiScreenGroupInfo { Name = "g", Groups = { new HmiScreenGroupInfo { Name = "h" } } } },
            } },
        { "list_screen_items", new HmiScreenItemListInfo
            { Screen = "s", Page = new HmiPage(0, 100, 1, null), Items = { new HmiScreenItemInfo { Name = "b", ItemType = "HmiButton", Left = 1, Top = 2, Width = 3, Height = null } } } },
        { "list_faceplate_instances", new HmiFaceplateInstanceListInfo
            {
                Page = new HmiPage(0, 100, 2, null),
                Instances =
                {
                    new HmiFaceplateInstanceInfo
                    {
                        Screen = "s", Container = "c", ContainedType = @"V0.0.3\FP",
                        Bindings = { new HmiFaceplateBindingInfo { PropertyName = "p", Value = new HmiVariant("System.Boolean", JsonDocument.Parse("false").RootElement.Clone()) } },
                    },
                    new HmiFaceplateInstanceInfo { Screen = "s", Container = "d" },
                },
            } },
        { "get_screen_navigation", new HmiScreenNavigationInfo
            { StartScreen = "s", Edges = { new HmiScreenEdgeInfo { FromScreen = "s", ViaItem = "w", ToScreen = "t" } } } },
        { "validate", new HmiValidationInfo
            {
                Page = new HmiPage(0, 100, 2, null),
                Scanned = 2,
                Clean = 1,
                Findings =
                {
                    new HmiValidationFindingInfo
                    {
                        ObjectKind = "HmiTag", Name = "a",
                        Results = { new HmiValidationResultInfo { PropertyName = null, Errors = { "e" }, Warnings = { "w" } } },
                    },
                },
                NotValidatable = { "HmiSystemTag" },
            } },
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void WorkerPayloadDecodesAsTheDeclaredType(string operation, object payload)
    {
        var json = WorkerJson.SerializePayload(payload);

        var item = HmiPayloadContract.Project(Op(operation), WorkerCallResult.Ok(json));

        Assert.Equal(OperationBatchStatus.Succeeded, item.Status);
        Assert.Equal(JsonValueKind.Object, item.Result!.Value.ValueKind);
        Assert.Equal(JsonValueKind.True, item.Result!.Value.GetProperty("isComplete").ValueKind);
    }

    [Fact]
    public void ExplicitNullsSurviveInTheCanonicalResult()
    {
        var json = WorkerJson.SerializePayload(new HmiProjectLanguagesInfo { EditingLanguage = "en-US" });

        var item = HmiPayloadContract.Project(Op("list_project_languages"), WorkerCallResult.Ok(json));

        Assert.Equal(JsonValueKind.Null, item.Result!.Value.GetProperty("referenceLanguage").ValueKind);
    }

    private static readonly HmiPageCursorCodec Codec = new(AuthenticatedCursorProtector.CreateProcessScoped());

    private static readonly ProjectBindingSnapshot Binding = new(
        ProjectBindingSnapshot.VerifiedState, "binding-a", 3, @"C:\Projects\Hmi.ap21", "worker-a", 1, 123, null);

    [Fact]
    public void Project_PageWithSnapshotId_EmitsNextCursorAndNoSnapshotId()
    {
        var request = new HmiOperationRequest { OperationId = "a", Operation = "list_tags", TableName = "T", Limit = 2 };
        var json = WorkerJson.SerializePayload(new HmiTagListInfo { Page = new HmiPage(0, 2, 5, 2) { SnapshotId = "s1" } });

        var item = HmiPayloadContract.Project(request, WorkerCallResult.Ok(json), Codec, Binding);

        var page = item.Result!.Value.GetProperty("page");
        Assert.False(page.TryGetProperty("snapshotId", out _));
        var state = Codec.Decode(page.GetProperty("nextCursor").GetString()!);
        Assert.Equal("s1", state.SnapshotId);
        Assert.Equal("list_tags", state.Operation);
        Assert.Equal(HmiPageCursorCodec.QueryHash(request), state.QueryHash);
        Assert.Equal(2, state.Offset);
        Assert.True(state.HostBinding.Matches(Binding, preserveUnboundEpoch: true));
    }

    [Fact]
    public void Project_LastPage_HasNoNextCursor()
    {
        var json = WorkerJson.SerializePayload(new HmiTagListInfo { Page = new HmiPage(4, 2, 5, null) });

        var item = HmiPayloadContract.Project(Op("list_tags"), WorkerCallResult.Ok(json), Codec, Binding);

        var page = item.Result!.Value.GetProperty("page");
        Assert.False(page.TryGetProperty("nextCursor", out _));
        Assert.False(page.TryGetProperty("snapshotId", out _));
    }

    [Theory]
    [InlineData("list_hmi_devices", """{"devices":[]}""")]
    [InlineData("get_runtime_settings", """{"isComplete":true,"messages":[]}""")]
    [InlineData("list_script_modules", """{"isComplete":true,"messages":[],"modules":"nope"}""")]
    [InlineData("list_project_languages", "[]")]
    public void PayloadOfTheWrongShapeIsAProtocolErrorAndIsNotEchoed(string operation, string payload)
    {
        var diagnostics = new List<string>();

        var item = HmiPayloadContract.Project(Op(operation), WorkerCallResult.Ok(payload), diagnostics.Add);

        Assert.Equal(OperationBatchStatus.Failed, item.Status);
        Assert.Equal(WorkerFailureCategories.ProtocolError, item.Failure!.Category);
        Assert.DoesNotContain("nope", item.Failure.Message);
        Assert.DoesNotContain("nope", string.Join("|", diagnostics));
    }
}
