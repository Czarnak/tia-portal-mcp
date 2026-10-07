using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Parts;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Shapes;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using TiaMcpServer.Contracts;
using TiaMcpServer.OpennessWorker;
using TiaMcpServer.OpennessWorker.Openness.Hmi;
using Xunit;
using static TiaMcpServer.Tests.Hmi.HmiTestProjects;

namespace TiaMcpServer.Tests.Hmi;

public class HmiScreenReaderTests
{
    private static HmiScreen Screen(string name, params HmiScreenItemBase[] items)
    {
        var screen = new HmiScreen
        {
            Name = name,
            DisplayName = new MultilingualText().With("en-US", name + " EN").With("pl-PL", name + " PL"),
            ScreenNumber = 3,
            Width = 1280,
            Height = 800,
        };
        screen.ScreenItems.Items.AddRange(items);
        return screen;
    }

    private static HmiScreenGroup Group(string name, IEnumerable<HmiScreen> screens, params HmiScreenGroup[] groups)
    {
        var group = new HmiScreenGroup { Name = name };
        group.Screens.Items.AddRange(screens);
        group.Groups.Items.AddRange(groups);
        return group;
    }

    private static HmiFaceplateContainer Faceplate(string name, string type, params (string Property, object? Value)[] bindings)
    {
        var container = new HmiFaceplateContainer { Name = name, ContainedType = type };
        foreach (var (property, value) in bindings)
        {
            container.Interface.Items.Add(new HmiFaceplateInterface { Name = property, PropertyName = property, Value = value! });
        }

        return container;
    }

    private static HmiScreenWindow Window(string name, string? target)
        => new() { Name = name, Screen = target! };

    /// <summary>Root: Start, alpha. Group Plant: Detail; its group Sub: Deep.</summary>
    private static HmiSoftware Plant()
    {
        var software = Unified("Panel_RT");
        software.Screens.Items.Add(Screen("Start", Window("ToDetail", "Detail"), new HmiButton { Name = "Go" }));
        software.Screens.Items.Add(Screen("alpha", Window("ToDeep", "Deep"), Window("ToStart", "Start")));
        software.ScreenGroups.Items.Add(Group(
            "Plant",
            new[] { Screen("Detail", Faceplate("FP_b", @"V0.0.3\FP_Estop", ("Tag", "Motor1")), Faceplate("FP_a", @"V1.0.0\FP_Valve")) },
            Group("Sub", new[] { Screen("Deep", Faceplate("FP_deep", @"V0.0.1\FP_Deep")) })));
        software.RuntimeSettings.StartScreen = "Start";
        return software;
    }

    [Fact]
    public void ScreenTreeNestsGroups()
    {
        var tree = HmiScreenReader.ListScreens(Plant(), null, null);

        Assert.True(tree.IsComplete);
        Assert.Null(tree.GroupPath);
        Assert.Equal(new[] { "alpha", "Start" }, tree.Screens.Select(s => s.Name));
        var plant = Assert.Single(tree.Groups);
        Assert.Equal("Plant", plant.Name);
        Assert.Equal("Detail", Assert.Single(plant.Screens).Name);
        var sub = Assert.Single(plant.Groups);
        Assert.Equal("Sub", sub.Name);
        Assert.Equal("Deep", Assert.Single(sub.Screens).Name);

        var start = tree.Screens[1];
        Assert.Equal(2, start.ItemCount);
        Assert.Equal((3, 1280L, 800L), (start.ScreenNumber, start.Width, start.Height));
        Assert.Equal(new[] { new HmiText("en-US", "Start EN"), new HmiText("pl-PL", "Start PL") }, start.DisplayName);
    }

    [Fact]
    public void ScreenTreeBelowAGroupPathAndLanguageNarrowing()
    {
        var tree = HmiScreenReader.ListScreens(Plant(), "Plant/Sub", "pl-PL");

        Assert.Equal("Plant/Sub", tree.GroupPath);
        var deep = Assert.Single(tree.Screens);
        Assert.Equal(new[] { new HmiText("pl-PL", "Deep PL") }, deep.DisplayName);
        Assert.Empty(tree.Groups);

        var ex = Assert.Throws<WorkerOperationException>(() => HmiScreenReader.ListScreens(Plant(), "Plant/plant", null));
        Assert.Equal(WorkerFailureCategories.TargetNotFound, ex.FailureCategory);
    }

    [Fact]
    public void UnreadableDisplayNameIsNullWithAMessage()
    {
        var software = Plant();
        software.Screens.Items[0].Failures["DisplayName"] = new EngineeringTargetInvocationException("no text");

        var tree = HmiScreenReader.ListScreens(software, null, null);

        Assert.Null(tree.Screens.Single(s => s.Name == "Start").DisplayName);
        Assert.False(tree.IsComplete);
        Assert.Contains(tree.Messages, m => m.Contains("DisplayName") && m.Contains("Start"));
    }

    [Fact]
    public void ScreenItemsRequireScreenAndPageByName()
    {
        var items = new HmiScreenItemBase[]
        {
            new HmiGraphicView { Name = "view", Visible = true, Enabled = false }.At(10, -20, 300, 40),
            new HmiButton { Name = "Button_2", Visible = false, Enabled = true }.At(1, 2, 3, 4),
            Window("Window", "Start"),
        };
        var software = Plant();
        software.Screens.Items.Add(Screen("Items", items));

        var first = HmiScreenReader.ListScreenItems(software, "items", 0, 2);
        var second = HmiScreenReader.ListScreenItems(software, "Items", 2, 2);

        Assert.Equal("Items", first.Screen);
        Assert.Equal(new[] { "Button_2", "view" }, first.Items.Select(i => i.Name));
        Assert.Equal(new HmiPage(0, 2, 3, 2), first.Page);
        Assert.Equal("Window", Assert.Single(second.Items).Name);
        Assert.Null(second.Page.NextOffset);

        var button = first.Items[0];
        Assert.Equal("HmiButton", button.ItemType);
        Assert.Equal((1L, 2L, 3L, 4L, false, true), (button.Left, button.Top, button.Width, button.Height, button.Visible, button.Enabled));
        var view = first.Items[1];
        Assert.Equal("HmiGraphicView", view.ItemType);
        Assert.Equal((10L, -20L, 300L, 40L), (view.Left, view.Top, view.Width, view.Height));

        var project = ProjectWith(DeviceWith("Panel", software));
        var ex = Assert.Throws<WorkerOperationException>(() =>
            HmiReadDispatch.Read(project, "hmi_list_screen_items", new HmiQueryInfo { Offset = 0, Limit = 5 }));
        Assert.Equal(WorkerFailureCategories.ValidationError, ex.FailureCategory);
        Assert.Contains("screenName", ex.Message);
    }

    [Fact]
    public void ItemTypeNameIsTheClrShortName()
    {
        Assert.Equal("HmiButton", HmiScreenReader.ItemTypeName(new HmiButton()));
        Assert.Equal("HmiScreenWindow", HmiScreenReader.ItemTypeName(Window("w", null)));
    }

    [Fact]
    public void ItemWithoutGeometryHasNullsAndAMessage()
    {
        var software = Unified("Panel_RT");
        var noGeometry = new HmiButton { Name = "Bare", Visible = true, Enabled = true };
        var failing = new HmiButton { Name = "Failing", Visible = true, Enabled = true }.At(1, 2, 3, 4);
        failing.Failures["Left"] = new EngineeringTargetInvocationException("no left");
        software.Screens.Items.Add(Screen("S", noGeometry, failing));

        var list = HmiScreenReader.ListScreenItems(software, "S", 0, 10);

        Assert.False(list.IsComplete);
        var bare = list.Items.Single(i => i.Name == "Bare");
        Assert.Equal((null, null, null, null), (bare.Left, bare.Top, bare.Width, bare.Height));
        var broken = list.Items.Single(i => i.Name == "Failing");
        Assert.Equal((null, 2L, 3L, 4L), (broken.Left, broken.Top, broken.Width, broken.Height));
        Assert.Contains(list.Messages, m => m.Contains("Left") && m.Contains("Failing"));
        Assert.Contains(list.Messages, m => m.Contains("Left") && m.Contains("Bare"));
    }

    [Fact]
    public void MissingScreenIsTargetNotFound()
    {
        var software = Plant();

        var items = Assert.Throws<WorkerOperationException>(() => HmiScreenReader.ListScreenItems(software, "Nope", 0, 10));
        var faceplates = Assert.Throws<WorkerOperationException>(() => HmiScreenReader.ListFaceplateInstances(software, "Nope", 0, 10));

        Assert.Equal(WorkerFailureCategories.TargetNotFound, items.FailureCategory);
        Assert.Equal(WorkerFailureCategories.TargetNotFound, faceplates.FailureCategory);
        Assert.Contains("list_screens", items.Message);
    }

    [Fact]
    public void FaceplateBindingsAreVariants()
    {
        var software = Unified("Panel_RT");
        software.Screens.Items.Add(Screen("S", Faceplate("FP", @"V0.0.3\FP_Estop", ("Tag", "Motor1"), ("Enabled", false), ("Unset", null)), new HmiButton { Name = "NotAFaceplate" }));

        var list = HmiScreenReader.ListFaceplateInstances(software, "S", 0, 10);

        Assert.True(list.IsComplete);
        var instance = Assert.Single(list.Instances);
        Assert.Equal(("S", "FP", @"V0.0.3\FP_Estop"), (instance.Screen, instance.Container, instance.ContainedType));
        Assert.Equal(new[] { "Enabled", "Tag", "Unset" }, instance.Bindings!.Select(b => b.PropertyName));
        Assert.Equal(("System.Boolean", false), (instance.Bindings![0].Value!.Type, instance.Bindings[0].Value!.Value!.Value.GetBoolean()));
        Assert.Equal(("System.String", "Motor1"), (instance.Bindings[1].Value!.Type, instance.Bindings[1].Value!.Value!.Value.GetString()));
        Assert.Null(instance.Bindings[2].Value!.Type);
    }

    [Fact]
    public void UnreadableInterfaceIsNullBindingsWithAMessage()
    {
        var software = Unified("Panel_RT");
        var container = Faceplate("FP", @"V0.0.3\FP_Estop", ("Tag", "x"));
        container.Failures["Interface"] = new EngineeringTargetInvocationException("no interface");
        software.Screens.Items.Add(Screen("S", container));

        var list = HmiScreenReader.ListFaceplateInstances(software, null, 0, 10);

        Assert.Null(Assert.Single(list.Instances).Bindings);
        Assert.False(list.IsComplete);
        Assert.Contains(list.Messages, m => m.Contains("Interface") && m.Contains("FP"));
    }

    [Fact]
    public void FaceplateWithoutScreenNameScansAllScreens()
    {
        var all = HmiScreenReader.ListFaceplateInstances(Plant(), null, 0, 2);
        var rest = HmiScreenReader.ListFaceplateInstances(Plant(), null, 2, 2);
        var one = HmiScreenReader.ListFaceplateInstances(Plant(), "Deep", 0, 10);

        Assert.Equal(new[] { ("Deep", "FP_deep"), ("Detail", "FP_a") }, all.Instances.Select(i => (i.Screen, i.Container)));
        Assert.Equal(new HmiPage(0, 2, 3, 2), all.Page);
        Assert.Equal(("Detail", "FP_b"), (rest.Instances.Single().Screen, rest.Instances.Single().Container));
        Assert.Equal("FP_deep", Assert.Single(one.Instances).Container);
    }

    [Fact]
    public void NavigationHasStartScreenAndWindowEdges()
    {
        var navigation = HmiScreenReader.GetScreenNavigation(Plant());

        Assert.True(navigation.IsComplete);
        Assert.Empty(navigation.Messages);
        Assert.Equal("Start", navigation.StartScreen);
        Assert.Equal(
            new[] { ("alpha", "ToDeep", "Deep"), ("alpha", "ToStart", "Start"), ("Start", "ToDetail", "Detail") },
            navigation.Edges.Select(e => (e.FromScreen, e.ViaItem, e.ToScreen)));
    }

    [Fact]
    public void WindowWithoutTargetScreenProducesNoEdgeAndAMessage()
    {
        var software = Unified("Panel_RT");
        software.Screens.Items.Add(Screen("Home", Window("Empty", string.Empty), Window("Unset", null), Window("Ok", "Home")));

        var navigation = HmiScreenReader.GetScreenNavigation(software);

        var edge = Assert.Single(navigation.Edges);
        Assert.Equal(("Home", "Ok", "Home"), (edge.FromScreen, edge.ViaItem, edge.ToScreen));
        Assert.Equal(2, navigation.Messages.Count);
        Assert.Contains(navigation.Messages, m => m.Contains("Empty") && m.Contains("Home"));
        Assert.Contains(navigation.Messages, m => m.Contains("Unset") && m.Contains("Home"));
        Assert.True(navigation.IsComplete);
    }

    [Fact]
    public void UnreadableStartScreenIsNullAndIncomplete()
    {
        var software = Plant();
        software.RuntimeSettingsFailure = new EngineeringTargetInvocationException("no runtime settings");

        var navigation = HmiScreenReader.GetScreenNavigation(software);

        Assert.Null(navigation.StartScreen);
        Assert.False(navigation.IsComplete);
        Assert.Equal(3, navigation.Edges.Count);
    }

    [Fact]
    public void UnreadableScreenCompositionFailsTheItem()
    {
        var software = Plant();
        software.Screens.EnumerationFailure = new EngineeringTargetInvocationException("gone");

        var ex = Assert.Throws<WorkerOperationException>(() => HmiScreenReader.GetScreenNavigation(software));

        Assert.Equal(WorkerFailureCategories.WorkerOperationFailed, ex.FailureCategory);
    }
}
