namespace TiaMcpServer.Contracts;

/// <summary>Result of <c>list_script_modules</c>: module names ordered by name.</summary>
public class HmiScriptModuleListInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<string> Modules { get; set; } = new List<string>();
}

/// <summary>Result of <c>list_text_and_graphic_lists</c>: list names, each array ordered by name.</summary>
public class HmiTextAndGraphicListsInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<string> TextLists { get; set; } = new List<string>();

    public List<string> GraphicLists { get; set; } = new List<string>();

    public List<string> SystemTextLists { get; set; } = new List<string>();
}

/// <summary>Result of <c>list_project_languages</c>; editing and reference language are null when unreadable.</summary>
public class HmiProjectLanguagesInfo
{
    public bool IsComplete { get; set; } = true;

    public List<string> Messages { get; set; } = new List<string>();

    public List<string> Languages { get; set; } = new List<string>();

    public string? EditingLanguage { get; set; }

    public string? ReferenceLanguage { get; set; }
}
