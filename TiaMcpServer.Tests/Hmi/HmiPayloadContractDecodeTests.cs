using System.Text.Json;
using TiaMcpServer.Contracts;
using TiaMcpServer.Hmi;
using TiaMcpServer.OperationBatches;
using TiaMcpServer.Worker;
using Xunit;

namespace TiaMcpServer.Tests.Hmi;

/// <summary>Exercises the mapped-type decode path of <see cref="HmiPayloadContract"/> for operations 1 and 16-19.</summary>
public class HmiPayloadContractDecodeTests
{
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
