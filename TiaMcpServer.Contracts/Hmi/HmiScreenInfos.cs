namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>list_screens</c>: the screens and screen groups at one level, ordered by name.</summary>
public class HmiScreenTreeInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    /// <summary>The requested group path, or null for the root.</summary>
    public string? GroupPath { get; set; }

    public List<HmiScreenInfo> Screens { get; set; } = new List<HmiScreenInfo>();

    public List<HmiScreenGroupInfo> Groups { get; set; } = new List<HmiScreenGroupInfo>();
}

public class HmiScreenGroupInfo
{
    public string Name { get; set; } = string.Empty;

    public List<HmiScreenInfo> Screens { get; set; } = new List<HmiScreenInfo>();

    public List<HmiScreenGroupInfo> Groups { get; set; } = new List<HmiScreenGroupInfo>();
}

public class HmiScreenInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Raw multilingual display name; null when unreadable.</summary>
    public List<HmiText>? DisplayName { get; set; } = new List<HmiText>();

    public int? ScreenNumber { get; set; }

    public long? Width { get; set; }

    public long? Height { get; set; }

    public int ItemCount { get; set; }
}

/// <summary>Result of <c>list_screen_items</c>: one page of the top-level items of a screen ordered by name.</summary>
public class HmiScreenItemListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public string Screen { get; set; } = string.Empty;

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public List<HmiScreenItemInfo> Items { get; set; } = new List<HmiScreenItemInfo>();
}

public class HmiScreenItemInfo
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The CLR short type name, for example <c>HmiButton</c>.</summary>
    public string ItemType { get; set; } = string.Empty;

    public long? Left { get; set; }

    public long? Top { get; set; }

    public long? Width { get; set; }

    public long? Height { get; set; }

    public bool? Visible { get; set; }

    public bool? Enabled { get; set; }
}

/// <summary>Result of <c>list_faceplate_instances</c>: one page ordered by screen, then container name.</summary>
public class HmiFaceplateInstanceListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public HmiPage Page { get; set; } = new HmiPage(0, 0, 0, null);

    public List<HmiFaceplateInstanceInfo> Instances { get; set; } = new List<HmiFaceplateInstanceInfo>();
}

public class HmiFaceplateInstanceInfo
{
    public string Screen { get; set; } = string.Empty;

    public string Container { get; set; } = string.Empty;

    /// <summary>Of the form <c>V&lt;version&gt;\&lt;faceplate type&gt;</c>; null when unreadable.</summary>
    public string? ContainedType { get; set; }

    /// <summary>Interface bindings ordered by property name; an unreadable interface fails the item.</summary>
    public List<HmiFaceplateBindingInfo> Bindings { get; set; } = new List<HmiFaceplateBindingInfo>();
}

public class HmiFaceplateBindingInfo
{
    public string PropertyName { get; set; } = string.Empty;

    public HmiVariant? Value { get; set; }
}

/// <summary>Result of <c>get_screen_navigation</c>: the start screen and one edge per screen window with a target.</summary>
public class HmiScreenNavigationInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public string? StartScreen { get; set; }

    public List<HmiScreenEdgeInfo> Edges { get; set; } = new List<HmiScreenEdgeInfo>();
}

public class HmiScreenEdgeInfo
{
    public string FromScreen { get; set; } = string.Empty;

    public string ViaItem { get; set; } = string.Empty;

    public string ToScreen { get; set; } = string.Empty;
}
