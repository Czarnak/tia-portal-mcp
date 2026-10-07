using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.Controls;
using Siemens.Engineering.HmiUnified.UI.Features;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using TiaMcpServer.Contracts;

namespace TiaMcpServer.OpennessWorker.Openness.Hmi;

/// <summary>
/// Operations 12 to 15: screens, screen items, faceplate instances and navigation. Reads enumerate compositions
/// (never <c>Find</c>); properties are read from explicit lists only. Item geometry lives on the concrete item
/// types, so it is read through <see cref="IHmiBoxFeature"/>; an item type without it has null geometry (a fact, no message). An unreadable
/// composition fails the item; an unreadable property is null plus a message.
/// </summary>
public static class HmiScreenReader
{
    /// <summary>The CLR short type name of a screen item, for example <c>HmiButton</c>.</summary>
    public static string ItemTypeName(object item) => item.GetType().Name;

    public static HmiScreenTreeInfo ListScreens(HmiSoftware software, string? groupPath, string? language)
    {
        List<HmiScreen> screens;
        List<HmiScreenGroup> groups;
        if (groupPath is null)
        {
            screens = HmiReadLog.Guard(() => software.Screens.ToList(), "The screens");
            groups = HmiReadLog.Guard(() => software.ScreenGroups.ToList(), "The screen groups");
        }
        else
        {
            var group = ResolveGroup(software, groupPath);
            screens = HmiReadLog.Guard(() => group.Screens.ToList(), $"The screens of group '{groupPath}'");
            groups = HmiReadLog.Guard(() => group.Groups.ToList(), $"The groups of group '{groupPath}'");
        }

        var log = new HmiReadLog();
        return new HmiScreenTreeInfo
        {
            GroupPath = groupPath,
            Screens = ScreenInfos(screens, language, log),
            Groups = GroupInfos(groups, language, log),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    public static HmiScreenItemListInfo ListScreenItems(HmiSoftware software, string screenName, int offset, int limit)
    {
        var (name, screen) = FindScreen(software, screenName);
        var items = HmiReadLog.Guard(() => screen.ScreenItems.ToList(), $"The items of screen '{name}'")
            .Select(i => (Item: i, Name: HmiReadLog.Guard(() => i.Name, $"A screen item name of screen '{name}'")))
            .ToList();
        var (page, pageInfo) = HmiPager.Page(items, i => i.Name, offset, limit);
        var log = new HmiReadLog();
        return new HmiScreenItemListInfo
        {
            Screen = name,
            Page = pageInfo,
            Items = page.Select(i => ReadItem(i.Item, i.Name, log)).ToList(),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    public static HmiFaceplateInstanceListInfo ListFaceplateInstances(
        HmiSoftware software, string? screenName, int offset, int limit)
    {
        var screens = screenName is null ? AllScreens(software) : new List<(string, HmiScreen)> { FindScreen(software, screenName) };
        var containers = new List<(string Screen, string Container, HmiFaceplateContainer Source)>();
        foreach (var (name, screen) in screens)
        {
            foreach (var item in HmiReadLog.Guard(() => screen.ScreenItems.ToList(), $"The items of screen '{name}'"))
            {
                if (item is HmiFaceplateContainer container)
                {
                    containers.Add((name, HmiReadLog.Guard(() => container.Name, $"A screen item name of screen '{name}'"), container));
                }
            }
        }

        var (page, pageInfo) = HmiPager.Page(containers, c => c.Screen + "\0" + c.Container, offset, limit);
        var log = new HmiReadLog();
        return new HmiFaceplateInstanceListInfo
        {
            Page = pageInfo,
            Instances = page.Select(c => ReadFaceplate(c.Screen, c.Container, c.Source, log)).ToList(),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    public static HmiScreenNavigationInfo GetScreenNavigation(HmiSoftware software)
    {
        var log = new HmiReadLog();
        var startScreen = log.Try(() => software.RuntimeSettings.StartScreen, "Property StartScreen of the runtime settings");
        var edges = new List<HmiScreenEdgeInfo>();
        foreach (var (screenName, screen) in AllScreens(software))
        {
            foreach (var item in HmiReadLog.Guard(() => screen.ScreenItems.ToList(), $"The items of screen '{screenName}'"))
            {
                if (item is not HmiScreenWindow window)
                {
                    continue;
                }

                var via = HmiReadLog.Guard(() => window.Name, $"A screen item name of screen '{screenName}'");
                var before = log.Messages.Count;
                var target = log.Try(() => window.Screen, $"Property Screen of screen window '{via}' on screen '{screenName}'");
                if (!string.IsNullOrEmpty(target))
                {
                    edges.Add(new HmiScreenEdgeInfo { FromScreen = screenName, ViaItem = via, ToScreen = target! });
                }
                else if (log.Messages.Count == before)
                {
                    log.Note($"Screen window '{via}' on screen '{screenName}' has no target screen, so it produces no edge.");
                }
            }
        }

        return new HmiScreenNavigationInfo
        {
            StartScreen = startScreen,
            Edges = HmiPager.InNameOrder(edges, e => e.FromScreen + "\0" + e.ViaItem).ToList(),
            IsComplete = log.IsComplete,
            Messages = log.Messages,
        };
    }

    private static HmiScreenItemInfo ReadItem(HmiScreenItemBase item, string name, HmiReadLog log)
    {
        string What(string property) => $"Property {property} of screen item '{name}'";
        // Geometry is a fact of the item type: only items implementing IHmiBoxFeature have it.
        var box = item as IHmiBoxFeature;
        long? Geometry(string property, Func<long> read) => box is null ? null : log.Try(() => (long?)read(), What(property));
        return new HmiScreenItemInfo
        {
            Name = name,
            ItemType = ItemTypeName(item),
            Left = Geometry("Left", () => box!.Left),
            Top = Geometry("Top", () => box!.Top),
            Width = Geometry("Width", () => box!.Width),
            Height = Geometry("Height", () => box!.Height),
            Visible = log.Try(() => (bool?)item.Visible, What("Visible")),
            Enabled = log.Try(() => (bool?)item.Enabled, What("Enabled")),
        };
    }

    private static HmiFaceplateInstanceInfo ReadFaceplate(string screen, string container, HmiFaceplateContainer source, HmiReadLog log)
    {
        string What(string property) => $"Property {property} of faceplate container '{container}' on screen '{screen}'";
        var bindings = HmiReadLog.Guard(() => source.Interface, $"The interface of faceplate container '{container}'");
        return new HmiFaceplateInstanceInfo
        {
            Screen = screen,
            Container = container,
            ContainedType = log.Try(() => source.ContainedType, What("ContainedType")),
            Bindings = ReadBindings(bindings, container, log),
        };
    }

    private static List<HmiFaceplateBindingInfo> ReadBindings(
        IEnumerable<Siemens.Engineering.HmiUnified.UI.Parts.HmiFaceplateInterface> interfaces, string container, HmiReadLog log)
    {
        var bindings = HmiReadLog.Guard(() => interfaces.ToList(), $"The interface of faceplate container '{container}'")
            .Select(b =>
            {
                var property = HmiReadLog.Guard(() => b.PropertyName, $"An interface property name of faceplate container '{container}'");
                return new HmiFaceplateBindingInfo
                {
                    PropertyName = property,
                    Value = HmiAlarmReader.ReadVariant(() => b.Value, log, $"Value of interface property '{property}' of faceplate container '{container}'"),
                };
            })
            .ToList();
        return HmiPager.InNameOrder(bindings, b => b.PropertyName).ToList();
    }

    private static List<HmiScreenInfo> ScreenInfos(IEnumerable<HmiScreen> screens, string? language, HmiReadLog log)
    {
        var infos = screens
            .Select(s =>
            {
                var name = HmiReadLog.Guard(() => s.Name, "A screen name");
                string What(string property) => $"Property {property} of screen '{name}'";
                return new HmiScreenInfo
                {
                    Name = name,
                    DisplayName = HmiAlarmReader.ReadText(() => s.DisplayName, language, log, What("DisplayName")),
                    ScreenNumber = log.Try(() => (int?)s.ScreenNumber, What("ScreenNumber")),
                    Width = log.Try(() => (long?)s.Width, What("Width")),
                    Height = log.Try(() => (long?)s.Height, What("Height")),
                    ItemCount = HmiReadLog.Guard(() => s.ScreenItems.Count(), $"The items of screen '{name}'"),
                };
            })
            .ToList();
        return HmiPager.InNameOrder(infos, i => i.Name).ToList();
    }

    private static List<HmiScreenGroupInfo> GroupInfos(IEnumerable<HmiScreenGroup> groups, string? language, HmiReadLog log)
    {
        var infos = groups
            .Select(g => new HmiScreenGroupInfo
            {
                Name = HmiReadLog.Guard(() => g.Name, "A screen group name"),
                Screens = ScreenInfos(HmiReadLog.Guard(() => g.Screens.ToList(), "The screens of a group"), language, log),
                Groups = GroupInfos(HmiReadLog.Guard(() => g.Groups.ToList(), "The groups of a group"), language, log),
            })
            .ToList();
        return HmiPager.InNameOrder(infos, i => i.Name).ToList();
    }

    private static HmiScreenGroup ResolveGroup(HmiSoftware software, string groupPath)
    {
        IEnumerable<HmiScreenGroup> level = HmiReadLog.Guard(() => software.ScreenGroups.ToList(), "The screen groups");
        HmiScreenGroup? current = null;
        foreach (var segment in groupPath.Split('/'))
        {
            current = level.FirstOrDefault(g => string.Equals(HmiReadLog.Guard(() => g.Name, "A screen group name"), segment, StringComparison.Ordinal))
                ?? throw new WorkerOperationException(
                    WorkerFailureCategories.TargetNotFound,
                    $"Screen group '{groupPath}' was not found. Use list_screens to see the groups.");
            var captured = current;
            level = HmiReadLog.Guard(() => captured.Groups.ToList(), $"The groups of group '{segment}'");
        }

        return current!;
    }

    /// <summary>Every screen of the HMI, at the root and in groups, with its name.</summary>
    private static List<(string Name, HmiScreen Screen)> AllScreens(HmiSoftware software)
    {
        var all = new List<(string, HmiScreen)>();
        Collect(
            HmiReadLog.Guard(() => software.Screens.ToList(), "The screens"),
            HmiReadLog.Guard(() => software.ScreenGroups.ToList(), "The screen groups"),
            all);
        return all;
    }

    private static void Collect(IEnumerable<HmiScreen> screens, IEnumerable<HmiScreenGroup> groups, List<(string, HmiScreen)> all)
    {
        foreach (var screen in screens)
        {
            all.Add((HmiReadLog.Guard(() => screen.Name, "A screen name"), screen));
        }

        foreach (var group in groups)
        {
            Collect(
                HmiReadLog.Guard(() => group.Screens.ToList(), "The screens of a group"),
                HmiReadLog.Guard(() => group.Groups.ToList(), "The groups of a group"),
                all);
        }
    }

    /// <summary>Every screen with its name, for <c>validate</c>.</summary>
    internal static List<(object Item, string Name)> NamedScreens(HmiSoftware software)
        => AllScreens(software).Select(s => ((object)s.Screen, s.Name)).ToList();

    private static (string Name, HmiScreen Screen) FindScreen(HmiSoftware software, string screenName)
    {
        var matches = AllScreens(software)
            .Where(s => string.Equals(s.Name, screenName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetNotFound,
                $"Screen '{screenName}' was not found. Use list_screens to see the screens.");
        }

        if (matches.Count > 1)
        {
            throw new WorkerOperationException(
                WorkerFailureCategories.TargetAmbiguous, $"Several screens are named '{screenName}'.");
        }

        return matches[0];
    }
}
