using Siemens.Engineering;
using Siemens.Engineering.HmiUnified.RuntimeSettings;
using Siemens.Engineering.HmiUnified.Scripts;
using Siemens.Engineering.HmiUnified.TextGraphicList;
using Siemens.Engineering.HW;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiSoftwareInfoReaderTests
{
    [Fact]
    public void ListHmiDevicesReportsUnifiedAndClassicKinds()
    {
        // Comfort panel: Device.TypeIdentifier is null, the head item (named like the device) carries it.
        var comfort = DeviceWith("Panel_B", Unified("Panel_B_RT"));
        comfort.DeviceItems.Items.Insert(0, new DeviceItem { Name = "Panel_B", TypeIdentifier = "OrderNumber:6AV2 123" });
        // PC station: the type identifier is on the device.
        var pc = DeviceWith("PC_A", Unified("PC_A_RT"), typeIdentifier: "System:Device.PC");
        var classic = DeviceWith("Old", Classic("Old_RT"));
        var project = ProjectWith(comfort, pc, classic);

        var info = HmiSoftwareInfoReader.ListDevices(project);

        Assert.True(info.IsComplete);
        Assert.Equal(new[] { "Old", "Panel_B", "PC_A" }, info.Devices.Select(d => d.DeviceName));
        Assert.Equal(new[] { "classic", "unified", "unified" }, info.Devices.Select(d => d.Kind));
        Assert.Equal("OrderNumber:6AV2 123", info.Devices[1].TypeIdentifier);
        Assert.Equal("System:Device.PC", info.Devices[2].TypeIdentifier);
        Assert.Null(info.Devices[0].TypeIdentifier);
        Assert.Equal("Panel_B_RT", info.Devices[1].SoftwareName);
    }

    [Fact]
    public void UnreadableTypeIdentifierIsNullWithMessageAndIncomplete()
    {
        var device = DeviceWith("PC_A", Unified("PC_A_RT"));
        device.TypeIdentifierFailure = new EngineeringException("no type");

        var info = HmiSoftwareInfoReader.ListDevices(ProjectWith(device));

        Assert.False(info.IsComplete);
        Assert.Null(Assert.Single(info.Devices).TypeIdentifier);
        Assert.Contains("no type", Assert.Single(info.Messages));
    }

    [Fact]
    public void RuntimeSettingsMissingSubObjectIsNullWithMessage()
    {
        // S4: on a Comfort panel the reporting sub-object is null and GMPEnabled throws.
        var software = Unified("Panel_RT");
        var settings = software.RuntimeSettings;
        settings.StartScreen = "Start";
        settings.ScreenResolution = Siemens.Engineering.HmiUnified.RuntimeSettings.HmiRuntimeSettingsCommon.ScreenResolution.SR_1280X800;
        settings.Failures["GMPEnabled"] = new EngineeringException("GMPEnabled is not supported on the current device version.");
        settings.HmiUnifiedTagSettings = new HmiUnifiedTagSettings();
        settings.HmiUnifiedTagSettings.Failures["TagOptimizationActive"] = new EngineeringException("not supported");
        settings.LanguageAndFonts.Items.Add(new HmiLanguageAndFont { Language = "pl-PL", Order = 2, Enable = true, DefaultFont = "Arial" });
        settings.LanguageAndFonts.Items.Add(new HmiLanguageAndFont { Language = "en-US", Order = 1, Enable = true });

        var info = HmiSoftwareInfoReader.ReadRuntimeSettings(software);

        Assert.Null(info.Reporting);
        Assert.Null(info.Telemetry);
        Assert.Contains(info.Messages, m => m.Contains("HmiReportingSettings"));
        Assert.NotNull(info.UnifiedTags);
        Assert.False(info.IsComplete); // GMPEnabled and TagOptimizationActive were unreadable
        var general = info.General.Values.ToDictionary(v => v.Name, v => v.Value);
        Assert.Equal("Start", general["StartScreen"]);
        Assert.Equal("SR_1280X800", general["ScreenResolution"]);
        Assert.Null(general["GMPEnabled"]);
        Assert.Contains(info.Messages, m => m.Contains("GMPEnabled"));
        Assert.Null(info.UnifiedTags!.Values.Single(v => v.Name == "TagOptimizationActive").Value);
        Assert.Equal(new[] { "en-US", "pl-PL" }, info.LanguageAndFonts.Select(l => l.Language));
        Assert.Equal(true, info.LanguageAndFonts[0].Enable);
    }

    [Fact]
    public void RuntimeSettingsBooleansAreLowercaseAndNumbersInvariant()
    {
        var software = Unified("PC_RT");
        software.RuntimeSettings.MaxLoginRuntimeSettings = new HmiMaxLoginRuntimeSettings
        {
            EnableLockAfterNumberOfAttempts = true,
            MaxLoginErrors = 3,
        };

        var info = HmiSoftwareInfoReader.ReadRuntimeSettings(software);

        var values = info.MaxLogin!.Values.ToDictionary(v => v.Name, v => v.Value);
        Assert.Equal("true", values["EnableLockAfterNumberOfAttempts"]);
        Assert.Equal("3", values["MaxLoginErrors"]);
    }

    [Fact]
    public void UnreadableRuntimeSettingsCompositionFailsTheItem()
    {
        var software = Unified("Panel_RT");
        software.RuntimeSettingsFailure = new EngineeringException("settings unavailable");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareInfoReader.ReadRuntimeSettings(software));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }

    [Fact]
    public void ScriptModuleAndListNamesSortedByName()
    {
        var software = Unified("Panel_RT");
        foreach (var name in new[] { "zeta", "Alpha", "beta" })
        {
            software.Scripts.Items.Add(new HmiScriptModule { Name = name });
            software.HmiTextLists.Items.Add(new HmiTextList { Name = name });
            software.HmiGraphicLists.Items.Add(new HmiGraphicList { Name = name });
            software.HmiSystemTextLists.Items.Add(new HmiSystemTextList { Name = name });
        }

        var scripts = HmiSoftwareInfoReader.ListScriptModules(software);
        var lists = HmiSoftwareInfoReader.ListTextAndGraphicLists(software);

        var expected = new[] { "Alpha", "beta", "zeta" };
        Assert.Equal(expected, scripts.Modules);
        Assert.Equal(expected, lists.TextLists);
        Assert.Equal(expected, lists.GraphicLists);
        Assert.Equal(expected, lists.SystemTextLists);
        Assert.True(scripts.IsComplete && lists.IsComplete);
    }

    [Fact]
    public void UnreadableListNameIsSkippedWithMessageAndIncomplete()
    {
        var software = Unified("Panel_RT");
        software.Scripts.Items.Add(new HmiScriptModule { Name = "ok" });
        software.Scripts.Items.Add(new HmiScriptModule { NameFailure = new EngineeringException("name gone") });

        var scripts = HmiSoftwareInfoReader.ListScriptModules(software);

        Assert.Equal(new[] { "ok" }, scripts.Modules);
        Assert.False(scripts.IsComplete);
        Assert.Contains("name gone", Assert.Single(scripts.Messages));
    }

    [Fact]
    public void UnreadableListCompositionFailsTheItem()
    {
        var software = Unified("Panel_RT");
        software.HmiGraphicLists.EnumerationFailure = new EngineeringException("lists unavailable");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiSoftwareInfoReader.ListTextAndGraphicLists(software));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
        Assert.Contains("lists unavailable", ex.Message);
    }

    [Fact]
    public void ProjectLanguagesReportEditingAndReferenceLanguage()
    {
        var project = ProjectWith().WithLanguages("pl-PL", "en-US", "pl-PL", "en-US");

        var info = HmiSoftwareInfoReader.ListProjectLanguages(project);

        Assert.Equal(new[] { "en-US", "pl-PL" }, info.Languages);
        Assert.Equal("pl-PL", info.EditingLanguage);
        Assert.Equal("en-US", info.ReferenceLanguage);
        Assert.True(info.IsComplete);
    }

    [Fact]
    public void UnreadableReferenceLanguageIsNullWithMessage()
    {
        var project = ProjectWith().WithLanguages("en-US", "en-US", "en-US");
        project.LanguageSettings.ReferenceLanguageFailure = new EngineeringException("no reference");

        var info = HmiSoftwareInfoReader.ListProjectLanguages(project);

        Assert.Null(info.ReferenceLanguage);
        Assert.Equal("en-US", info.EditingLanguage);
        Assert.False(info.IsComplete);
        Assert.Contains("no reference", Assert.Single(info.Messages));
    }

    [Fact]
    public void DispatchRoutesTheFiveOperationsAndRejectsOthers()
    {
        var project = ProjectWith(DeviceWith("Panel", Unified("Panel_RT"))).WithLanguages("en-US", "en-US", "en-US");
        var query = new HmiQueryInfo();

        Assert.IsType<HmiDeviceListInfo>(HmiReadDispatch.Read(project, "hmi_list_hmi_devices", query));
        Assert.IsType<HmiRuntimeSettingsInfo>(HmiReadDispatch.Read(project, "hmi_get_runtime_settings", query));
        Assert.IsType<HmiScriptModuleListInfo>(HmiReadDispatch.Read(project, "hmi_list_script_modules", query));
        Assert.IsType<HmiTextAndGraphicListsInfo>(HmiReadDispatch.Read(project, "hmi_list_text_and_graphic_lists", query));
        Assert.IsType<HmiProjectLanguagesInfo>(HmiReadDispatch.Read(project, "hmi_list_project_languages", query));
        var ex = Assert.Throws<WorkerOperationException>(() => HmiReadDispatch.Read(project, "hmi_list_tags", query));
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
    }

    [Fact]
    public void DispatchResolvesHmiNameAndFailsForUnknownName()
    {
        var project = ProjectWith(DeviceWith("Panel", Unified("Panel_RT")));

        var ex = Assert.Throws<WorkerOperationException>(() =>
            HmiReadDispatch.Read(project, "hmi_list_script_modules", new HmiQueryInfo { HmiName = "nope" }));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }
}
