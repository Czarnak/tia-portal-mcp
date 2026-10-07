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
